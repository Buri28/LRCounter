using LRCounter.Configuration;
using LRCounter.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Zenject;

namespace LRCounter.Controllers
{
    // ノーツイベントを受け取り、左右の精度・PPをリアルタイムで追跡するサービス
    // ゲームプレイシーン全体で1つだけ存在する（Zenjectがシングルトンで管理）
    public class LRTrackerService : IInitializable, IDisposable
    {
        private readonly ScoreController _scoreController;           // ノーツ判定イベントの発生源
        private readonly GameplayCoreSceneSetupData _sceneSetupData; // 譜面情報（ハッシュ・難易度など）
        private readonly PluginConfig _config;
        private readonly ScoreSaberApiService _apiService;           // ScoreSaber API呼び出し
        private readonly GameEnergyCounter _energyCounter;           // ノーフェイルペナルティ検出用
        private bool _penaltyApplied = false;                        // ペナルティは1回だけ適用

        // 左右それぞれの精度・PPを追跡するトラッカー（DisplayControllerから参照される）
        public HandPPTracker LeftTracker { get; } = new HandPPTracker(HandPPTracker.HandType.Left);
        public HandPPTracker RightTracker { get; } = new HandPPTracker(HandPPTracker.HandType.Right);

        // 両手合算の累積スコアと最大スコア（TotalPP計算に使う）
        private int _totalScore = 0;
        private int _maxTotalScore = 0;

        // 倍率はゲームが各ノーツに焼き付けた値(scoringElement.multiplier / maxMultiplier)を
        // そのまま使う。自前で進めると「カット時刻順(ゲームの確定順)」と
        // 「スイング完了順(scoringForNoteFinished発火順)」のズレで誤差が蓄積するため廃止した。
        // ボム・壁による倍率低下も各ノーツの multiplier に反映済みなので個別処理は不要。
        private int _lastMultiplier = 1; // 直近ノーツの実倍率（デバッグ表示用）

        // 現在の倍率を外部（デバッグ表示）から参照できるように公開
        public int CurrentMultiplier => _lastMultiplier;

        public double StarRating { get; private set; } = 0; // 譜面のStar評価（0=アンランク）
        public double TotalPP { get; private set; } = 0; // 現在のスコアから推定される合計PP
        public double ThresholdPP { get; private set; } = 0; // ランクアップに必要な最低PP
        public double PlayerTotalPP { get; private set; } = 0; // ScoreSaberに登録されているプレイヤーの現在合計PP

        // Threshold計算で取得するランクスコアのページ数（1ページ100件・並列取得）。101位以下の微小寄与まで
        // 反映するため深めに取る。0.1pp判定なら必要な深さは概ね rank≈250 までなので3ページ(300件)で十分。
        private const int ScorePagesToFetch = 3;
        // 「トータルが増えた」とみなす最小増分(pp)。この分だけ増える最低スコアを Threshold とする。
        private const double GainEpsilon = 0.1;

        // NF(ノーフェイル)で失敗すると、ゲームはスコアと最大スコアの両方を半減する（精度%は不変・スコアだけ半分）。
        // 失敗時点で削るのではなく、成立後に全体へ係数を掛けて再現する。通常1.0、NF失敗後0.5。
        private float _scoreFactor = 1f;

        // 両手合算スコア（倍率込み）。NF失敗時は係数0.5で全体を半減（最大も半減するので精度は不変）。
        public int TotalScore => (int)(_totalScore * _scoreFactor);
        public int TotalMaxScore => (int)(_maxTotalScore * _scoreFactor);
        // 精度は生の集計比なので係数の影響を受けない（分子・分母とも同じ係数で打ち消される）。
        public double TotalAccuracy => _maxTotalScore > 0 ? (double)_totalScore / _maxTotalScore : 0;

        // ゲーム本体が確定させたスコア。scoreDidChangeEvent で最新値を受け取って保持する。
        // （scoringForNoteFinishedEvent 内で直読みすると全ノーツ処理前＝1ノーツ前の値になるため）
        // multipliedScore = 倍率込み・修飾前（生）。modifiedScore = NF・譜面修飾込みの実表示スコア。
        private int _gameMultipliedScore = 0;
        private int _gameModifiedScore = 0;
        public int GameMultipliedScore => _gameMultipliedScore;
        public int GameModifiedScore => _gameModifiedScore;

        // ノーツを切るたびに発火。DisplayControllerが購読して表示を更新する
        public event Action? OnPPUpdated;
        // ThresholdPPを初めて超えたときに1回だけ発火
        public event Action? OnThresholdExceeded;

        // ThresholdExceededを一度だけ発火させるためのフラグ
        private bool _thresholdExceededFired = false;

        private readonly LRResultStore _resultStore;

        [Inject]
        public LRTrackerService(
            ScoreController scoreController,
            GameplayCoreSceneSetupData sceneSetupData,
            PluginConfig config,
            ScoreSaberApiService apiService,
            GameEnergyCounter energyCounter,
            LRResultStore resultStore)
        {
            _scoreController = scoreController;
            _sceneSetupData = sceneSetupData;
            _config = config;
            _apiService = apiService;
            _energyCounter = energyCounter;
            _resultStore = resultStore;
        }

        // 曲開始時にZenjectから呼ばれる。非同期でStar評価とThresholdを取得する
        public async void Initialize()
        {
            if (!_config.Enabled) return;

            // ノーツ判定完了イベントを購読（倍率は各ノーツのscoringElementから取得する）
            _scoreController.scoringForNoteFinishedEvent += OnScoringForNoteFinished;
            // スコア確定イベントを購読。multipliedScore はノーツ確定イベントの後で更新されるため、
            // 検算用のゲームスコアはこちらの最新値で受け取る（直読みだと1ノーツ前の値になる）。
            _scoreController.scoreDidChangeEvent += OnScoreDidChange;
            // 体力変化イベントを購読してエネルギーが0になったらペナルティを適用
            _energyCounter.gameEnergyDidChangeEvent += OnEnergyChanged;
            Plugin.Log.Info("[LRCounter] Subscribed to scoringForNoteFinishedEvent / gameEnergyDidChangeEvent");

            // Star評価をScoreSaber APIから取得する
            Plugin.Log.Info("[LRCounter] Fetching star rating from ScoreSaber...");
            double fetched = await FetchStarRatingAsync();
            if (fetched > 0)
            {
                StarRating = fetched;
                ApplyStarRating();
                Plugin.Log.Info($"[LRCounter] Fetched StarRating={StarRating:F2}");
            }
            else
            {
                Plugin.Log.Info("[LRCounter] Map is unranked or fetch failed.");
            }

            // Threshold計算は時間がかかるので、待機せずバックグラウンドで実行
            _ = InitThresholdAsync();
        }

        // 曲終了時にZenjectから呼ばれる
        public void Dispose()
        {
            _scoreController.scoringForNoteFinishedEvent -= OnScoringForNoteFinished;
            _scoreController.scoreDidChangeEvent -= OnScoreDidChange;
            _energyCounter.gameEnergyDidChangeEvent -= OnEnergyChanged;

            // 曲終了時点の左右の平均精度・PPをリザルト画面用に保存する
            _resultStore.Set(
                LeftTracker.Accuracy * 100.0,
                RightTracker.Accuracy * 100.0,
                LeftTracker.PP,
                RightTracker.PP,
                StarRating > 0);

            GC.SuppressFinalize(this);
        }

        // ─── Star評価の取得 ────────────────────────────────────────────────────────

        // 現在プレイ中の譜面のStar評価をScoreSaber APIから取得する
        private Task<double> FetchStarRatingAsync()
        {
            var key = _sceneSetupData.beatmapKey;
            var levelId = key.levelId;

            // custom_level_ プレフィックスがない = 公式譜面なのでスキップ
            if (!levelId.StartsWith("custom_level_", StringComparison.OrdinalIgnoreCase))
            {
                Plugin.Log.Info($"[LRCounter] Not a custom level ({levelId}), skipping.");
                return Task.FromResult(0.0);
            }

            // levelId から SHA-1 ハッシュ部分だけを取り出す
            string hash = levelId.Substring("custom_level_".Length).ToUpperInvariant();
            int diff = ToScoreSaberDifficulty(key.difficulty);
            string gameMode = $"Solo{key.beatmapCharacteristic.serializedName}";

            return _apiService.GetLeaderboardStarsAsync(hash, diff, gameMode);
        }

        // Star評価が確定したら左右トラッカーをリセットしてStar値を設定する
        private void ApplyStarRating()
        {
            LeftTracker.Reset();
            RightTracker.Reset();
            LeftTracker.SetStarRating(StarRating);
            RightTracker.SetStarRating(StarRating);
        }

        // BeatSaberの難易度enumをScoreSaber APIの数値に変換する
        private static int ToScoreSaberDifficulty(BeatmapDifficulty d) => d switch
        {
            BeatmapDifficulty.Easy => 1,
            BeatmapDifficulty.Normal => 3,
            BeatmapDifficulty.Hard => 5,
            BeatmapDifficulty.Expert => 7,
            BeatmapDifficulty.ExpertPlus => 9,
            _ => 9,
        };

        // ─── Threshold（ランクアップ必要PP）の計算 ────────────────────────────────

        // プレイヤーのTop100スコアを取得し、今曲でいくつ以上のPPを出せばランクが上がるかを計算する
        private async Task InitThresholdAsync()
        {
            try
            {
                // SteamIDからプレイヤーIDを取得
                string? playerId = await _apiService.GetPlayerIdAsync();
                if (string.IsNullOrEmpty(playerId))
                {
                    Plugin.Log.Warn("[LRCounter] Could not get player ID; threshold disabled.");
                    return;
                }

                string? currentHash = GetCurrentMapHash();
                // 難易度・ゲームモードも照合に使う（同じ譜面でも難易度違いは別リーダーボード＝別スコア）
                var currentKey = _sceneSetupData.beatmapKey;
                int currentDiff = ToScoreSaberDifficulty(currentKey.difficulty);
                string currentMode = $"Solo{currentKey.beatmapCharacteristic.serializedName}";

                // ランクスコア（深めに取得）と現在の合計PPを並行して取得（どちらも時間がかかるため）。
                // 101位以下の微小寄与まで反映するため Top100 ではなく複数ページ取得する。
                var scoresTask = _apiService.GetTopScoresAsync(playerId!, ScorePagesToFetch);
                var playerPPTask = _apiService.GetPlayerTotalPPAsync(playerId!);
                await Task.WhenAll(scoresTask, playerPPTask);

                PlayerTotalPP = playerPPTask.Result;
                Plugin.Log.Info($"[LRCounter] PlayerTotalPP={PlayerTotalPP:F2}");

                var rankedScores = scoresTask.Result;

                // ベースライン＝既存スコア込みの現在の重み付けトータル（全件）。これを gainEpsilon 以上超えれば「底上げ」。
                var allPp = rankedScores.Select(s => s.pp).ToList(); // PP降順（取得側で降順ソート済み）
                double baseline = ScoreSaberApiService.WeightedTotal(allPp);

                // 今プレイ中の曲が既にランクスコアにあれば、その枠は新スコアで置き換わるので候補リストから外す。
                // （ベースライン baseline には既存スコアを含めたまま比較する＝既存スコアを上回って初めて増える）
                var others = new List<double>(allPp);
                if (!string.IsNullOrEmpty(currentHash))
                {
                    int idx = rankedScores.FindIndex(s =>
                        s.hash == currentHash && s.difficulty == currentDiff && s.gameMode == currentMode);
                    if (idx >= 0)
                    {
                        Plugin.Log.Info($"[LRCounter] Existing score for this map (pp={rankedScores[idx].pp:F2}) will be replaced");
                        others.RemoveAt(idx);
                    }
                }

                // 「このPP以上を出せばトータルが GainEpsilon 以上増える」最低ラインを計算
                ThresholdPP = ScoreSaberApiService.CalculateThreshold(others, baseline, GainEpsilon);
                Plugin.Log.Info($"[LRCounter] ThresholdPP={ThresholdPP:F2}");

                // Threshold確定後に表示を更新させる
                OnPPUpdated?.Invoke();
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[LRCounter] InitThreshold failed: {ex.Message}");
            }
        }

        // levelIdからSHA-1ハッシュを取り出す（custom_level_以外はnullを返す）
        private string? GetCurrentMapHash()
        {
            var levelId = _sceneSetupData.beatmapKey.levelId;
            if (levelId.StartsWith("custom_level_", StringComparison.OrdinalIgnoreCase))
                return levelId.Substring("custom_level_".Length).ToUpperInvariant();
            return null;
        }

        // ─── ノーツ判定イベント ────────────────────────────────────────────────────

        // ノーツの判定が完了するたびに呼ばれるメインの処理。
        // 倍率はゲームが各ノーツに確定させた値(scoringElement.multiplier / maxMultiplier)を使う。
        // これらはゲームがカット時刻順に確定済みなので、同時切りでもスイング完了順でもズレない。
        private void OnScoringForNoteFinished(ScoringElement scoringElement)
        {
            if (!_config.Enabled) return;

            var noteData = scoringElement.noteData;
            // ボムは精度・スコアの対象外（倍率への影響は各ノーツのmultiplierに反映済み）
            if (noteData.gameplayType == NoteData.GameplayType.Bomb) return;
            // 得点定義の無いノーツ(Ignore/NoScore)は無視
            int maxCut = scoringElement.maxPossibleCutScore; // そのノーツの最大点(通常115/チェーンヘッド85/リンク20)
            if (maxCut <= 0) return;

            int actualMult = scoringElement.multiplier;    // 実倍率（ゲームがカット時刻順に確定）
            int idealMult  = scoringElement.maxMultiplier; // FC想定倍率（分母用）
            int cutScore   = scoringElement.cutScore;      // 生スコア（グッド以外は0）
            _lastMultiplier = actualMult;

            // 左右どちらのトラッカーか（ColorA=左手, ColorB=右手）
            HandPPTracker? tracker =
                noteData.colorType == ColorType.ColorA ? LeftTracker :
                noteData.colorType == ColorType.ColorB ? RightTracker : null;

            // グッド/バッド/ミスを種類ごとに記録する（バッドとミスは別カウント）
            if (scoringElement is GoodCutScoringElement)
                tracker?.AddCut(cutScore, maxCut, actualMult, idealMult);
            else if (scoringElement is BadCutScoringElement)
                tracker?.AddBadCut(maxCut, idealMult);
            else
                tracker?.AddMiss(maxCut, idealMult);

            //Plugin.Log.Debug($"[LRCounter] Note: color={noteData.colorType} type={scoringElement.GetType().Name} score={cutScore} ×{actualMult} (ideal ×{idealMult}, max {maxCut})");

            // 両手合算スコアを更新（ゲームの _multipliedScore / 最大スコアと一致する）
            // グッド以外は cutScore=0 なので _totalScore は増えない
            _totalScore    += cutScore * actualMult;
            _maxTotalScore += maxCut * idealMult;

            // 両手合算の精度からTotalPPを計算
            if (StarRating > 0 && _maxTotalScore > 0)
            {
                double totalAcc = (double)_totalScore / _maxTotalScore;
                TotalPP = PPCalculator.CalculatePP(totalAcc, StarRating);
            }

            // ThresholdPPを初めて超えたら1回だけイベント発火
            if (!_thresholdExceededFired && ThresholdPP > 0 && TotalPP >= ThresholdPP)
            {
                _thresholdExceededFired = true;
                OnThresholdExceeded?.Invoke();
            }

            // 表示を更新させる
            OnPPUpdated?.Invoke();
        }

        // ゲームのスコアが確定（再計算）されたタイミングで呼ばれる。multipliedScore はこの時点で
        // 今切ったノーツまで反映済みなので、検算用のゲームスコアをここで最新値に更新する。
        // 更新後に表示も更新させ、デバッグ表示が1ノーツ前の値を出さないようにする。
        private void OnScoreDidChange(int multipliedScore, int modifiedScore)
        {
            _gameMultipliedScore = multipliedScore;
            _gameModifiedScore = modifiedScore;
            OnPPUpdated?.Invoke();
        }

        // ─── ノーフェイルペナルティ ────────────────────────────────────────────────

        // 体力変化イベント：0になった瞬間に1回だけ係数を0.5にする。
        // 生の集計(_totalScore/_maxTotalScore)は変えず、読み出し時に全体へ0.5を掛けて
        // ゲームのNF挙動（スコアも最大も半減＝精度は不変）を再現する。
        private void OnEnergyChanged(float energy)
        {
            if (energy > 0 || _penaltyApplied) return;
            _penaltyApplied = true;
            _scoreFactor = 0.5f;
            Plugin.Log.Info("[LRCounter] No Fail: score factor set to 0.5 (score & max halved, accuracy unchanged).");
            OnPPUpdated?.Invoke();
        }
    }
}
