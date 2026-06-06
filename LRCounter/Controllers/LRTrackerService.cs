using LRCounter.Configuration;
using LRCounter.Models;
using System;
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

        // 両手合算スコア（倍率込み）
        public int TotalScore => _totalScore;
        public int TotalMaxScore => _maxTotalScore;
        public double TotalAccuracy => _maxTotalScore > 0 ? (double)_totalScore / _maxTotalScore : 0;

        // ノーツを切るたびに発火。DisplayControllerが購読して表示を更新する
        public event Action? OnPPUpdated;
        // ThresholdPPを初めて超えたときに1回だけ発火
        public event Action? OnThresholdExceeded;

        // ThresholdExceededを一度だけ発火させるためのフラグ
        private bool _thresholdExceededFired = false;

        [Inject]
        public LRTrackerService(
            ScoreController scoreController,
            GameplayCoreSceneSetupData sceneSetupData,
            PluginConfig config,
            ScoreSaberApiService apiService,
            GameEnergyCounter energyCounter)
        {
            _scoreController = scoreController;
            _sceneSetupData = sceneSetupData;
            _config = config;
            _apiService = apiService;
            _energyCounter = energyCounter;
        }

        // 曲開始時にZenjectから呼ばれる。非同期でStar評価とThresholdを取得する
        public async void Initialize()
        {
            if (!_config.Enabled) return;

            // ノーツ判定完了イベントを購読（倍率は各ノーツのscoringElementから取得する）
            _scoreController.scoringForNoteFinishedEvent += OnScoringForNoteFinished;
            // 体力変化イベントを購読してエネルギーが0になったらペナルティを適用
            _energyCounter.gameEnergyDidChangeEvent += OnEnergyChanged;
            Plugin.Log.Info("[LRCounter] Subscribed to scoringForNoteFinishedEvent / gameEnergyDidChangeEvent");

            // Star評価の取得：手動設定があればそちらを優先、なければScoreSaber APIから取得
            if (_config.ManualStarRating > 0)
            {
                StarRating = _config.ManualStarRating;
                ApplyStarRating();
                Plugin.Log.Info($"[LRCounter] Manual StarRating={StarRating:F2}");
            }
            else
            {
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
            }

            // Threshold計算は時間がかかるので、待機せずバックグラウンドで実行
            _ = InitThresholdAsync();
        }

        // 曲終了時にZenjectから呼ばれる
        public void Dispose()
        {
            _scoreController.scoringForNoteFinishedEvent -= OnScoringForNoteFinished;
            _energyCounter.gameEnergyDidChangeEvent -= OnEnergyChanged;
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

                // Top100スコアと現在の合計PPを並行して取得（どちらも時間がかかるため）
                var top100Task = _apiService.GetTop100ScoresAsync(playerId!);
                var playerPPTask = _apiService.GetPlayerTotalPPAsync(playerId!);
                await Task.WhenAll(top100Task, playerPPTask);

                PlayerTotalPP = playerPPTask.Result;
                Plugin.Log.Info($"[LRCounter] PlayerTotalPP={PlayerTotalPP:F2}");

                var top100 = top100Task.Result;

                // 今プレイ中の曲が既にTop100に入っていたら除外する
                // （「新しく出したスコアがどれだけ増やすか」を正確に計算するため）
                if (!string.IsNullOrEmpty(currentHash))
                {
                    int idx = top100.FindIndex(s => s.hash == currentHash);
                    if (idx >= 0)
                    {
                        Plugin.Log.Info($"[LRCounter] Removing existing score for this map (pp={top100[idx].pp:F2})");
                        top100.RemoveAt(idx);
                    }
                }

                // PPリストを渡してThresholdPPを計算
                var ppList = top100.Select(s => s.pp).ToList();
                ThresholdPP = ScoreSaberApiService.CalculateThreshold(ppList);
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

            Plugin.Log.Debug($"[LRCounter] Note: color={noteData.colorType} type={scoringElement.GetType().Name} score={cutScore} ×{actualMult} (ideal ×{idealMult}, max {maxCut})");

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

        // ─── ノーフェイルペナルティ ────────────────────────────────────────────────

        // 体力変化イベント：0になった瞬間に1回だけスコアを半減する
        private void OnEnergyChanged(float energy)
        {
            if (energy > 0 || _penaltyApplied) return;
            _penaltyApplied = true;

            const float penalty = 0.5f;
            _totalScore = (int)(_totalScore * penalty);
            LeftTracker.ApplyPenalty(penalty);
            RightTracker.ApplyPenalty(penalty);
            Plugin.Log.Info("[LRCounter] No Fail penalty: scores halved.");
            OnPPUpdated?.Invoke();
        }
    }
}
