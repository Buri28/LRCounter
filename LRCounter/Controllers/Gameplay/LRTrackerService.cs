using LRCounter.Configuration;
using LRCounter.Controllers.Results;
using LRCounter.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Zenject;

namespace LRCounter.Controllers.Gameplay
{
    // ノーツイベントを受け取り、左右の精度・PPをリアルタイムで追跡するサービス
    // ゲームプレイシーン全体で1つだけ存在する（Zenjectがシングルトンで管理）
    public class LRTrackerService : IInitializable, IDisposable
    {
        private readonly ScoreController _scoreController;           // ノーツ判定イベントの発生源
        private readonly GameplayCoreSceneSetupData _sceneSetupData; // 譜面情報（ハッシュ・難易度など）
        private readonly PluginConfig _config;
        private readonly PlayerDataCache _playerDataCache;           // ScoreSaberデータのセッションキャッシュ（App スコープ）
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
        // ローカルのPlayerDataから読んだ、この譜面の合算（両手）自己ベスト精度(0〜1)。未プレイ/不明は0。
        // 合算精度がこれを超えたら「両手の自己ベスト更新」として左右とも黄色のボーダーを点ける。
        // スコア更新判定は精度同士で比較する（PPは精度の単調関数なのでPP比較と等価）。
        // Star評価（=ScoreSaber API）に依存しないので、API失敗時やアンランク譜面でも判定できる。
        private double _selfBestAccuracy = 0;
        public double SelfBestAccuracy => _selfBestAccuracy;

        // この譜面の左右ベスト精度(0〜1)。曲開始時に HandAccuracyStore から読む（＝今回プレイで更新する前の値）。
        // 片手ごとのボーダー点灯（その手の自己ベスト精度更新）判定に使う。記録なし／練習モードは0。
        public double LeftBestAccuracy { get; private set; }
        public double RightBestAccuracy { get; private set; }

        // 「トータルが増えた」とみなす最小増分(pp)。この分だけ増える最低スコアを Threshold とする。
        private const double GainEpsilon = 0.1;

        // NF(ノーフェイル)で失敗すると、ゲームはスコアと最大スコアの両方を半減する（精度%は不変・スコアだけ半分）。
        // 失敗時点で削るのではなく、成立後に全体へ係数を掛けて再現する。通常1.0、NF失敗後0.5。
        private float _scoreFactor = 1f;

        // NF失敗による減点係数（1.0=通常／0.5=失敗後）。自己ベスト判定（黄/橙ボーダー・左右ベスト保存）は
        // 精度同士で比較するが、失敗時は提出スコアが半減して実際にはベスト更新にならないため、
        // 「精度×この係数」を実効精度として比較・保存することで挙動を揃える。
        public double ScoreFactor => _scoreFactor;
        // NF失敗したか（PP取得＝白ボーダーは失敗プレイでは成立しないので消灯させる判定に使う）。
        public bool Failed => _penaltyApplied;

        // 曲開始時に判定したリプレイ所有者。曲終了時(Dispose)はこの値で記録要否を決める。
        // 終了時に判定するとBeatLeader等が先に後片付けしてフラグが戻り「通常プレイ」に化けるため、開始時に固定する。
        private ReplayDetector.Ownership _replayOwnership = ReplayDetector.Ownership.NotReplay;

        // ゲーム画面に表示するPP。NF失敗時は提出スコア半減を反映し、実効精度（精度×係数）から再計算する。
        // 通常クリア（係数1.0）は LeftTracker.PP / RightTracker.PP / TotalPP と同値。
        public double LeftDisplayPP => PPCalculator.CalculatePP(LeftTracker.Accuracy * _scoreFactor, StarRating);
        public double RightDisplayPP => PPCalculator.CalculatePP(RightTracker.Accuracy * _scoreFactor, StarRating);
        public double TotalDisplayPP => PPCalculator.CalculatePP(TotalAccuracy * _scoreFactor, StarRating);

        // 両手合算スコア（倍率込み）。NF失敗時は係数0.5で全体を半減（最大も半減するので精度は不変）。
        public int TotalScore => (int)(_totalScore * _scoreFactor);
        public int TotalMaxScore => (int)(_maxTotalScore * _scoreFactor);
        // 精度は生の集計比なので係数の影響を受けない（分子・分母とも同じ係数で打ち消される）。
        public double TotalAccuracy => _maxTotalScore > 0 ? (double)_totalScore / _maxTotalScore : 0;

        // ゲーム本体が確定させた実表示スコア（NF・譜面修飾込みの modifiedScore）。scoreDidChangeEvent で
        // 最新値を受け取って保持する（scoringForNoteFinishedEvent 内で直読みすると1ノーツ前の値になるため）。
        // デバッグ表示で自前集計スコアとの突き合わせに使う。
        private int _gameModifiedScore = 0;
        public int GameModifiedScore => _gameModifiedScore;

        // ノーツを切るたびに発火。DisplayControllerが購読して表示を更新する
        public event Action? OnPPUpdated;

        private readonly LRResultStore _resultStore;
        private readonly PlayerDataModel _playerDataModel; // ローカルの合算自己ベストスコア参照用
        private readonly HandAccuracyStore _handAccuracyStore; // 譜面ごとの左右ベスト精度（永続）

        [Inject]
        public LRTrackerService(
            ScoreController scoreController,
            GameplayCoreSceneSetupData sceneSetupData,
            PluginConfig config,
            PlayerDataCache playerDataCache,
            GameEnergyCounter energyCounter,
            LRResultStore resultStore,
            PlayerDataModel playerDataModel,
            HandAccuracyStore handAccuracyStore)
        {
            _scoreController = scoreController;
            _sceneSetupData = sceneSetupData;
            _config = config;
            _playerDataCache = playerDataCache;
            _energyCounter = energyCounter;
            _resultStore = resultStore;
            _playerDataModel = playerDataModel;
            _handAccuracyStore = handAccuracyStore;
        }

        // 曲開始時にZenjectから呼ばれる。非同期でStar評価とThresholdを取得する
        public async void Initialize()
        {
            if (!_config.Enabled) return;

            // リプレイ所有者を曲開始時に確定させる（終了時はフラグが戻りうるので、ここでキャッシュする）。
            // ScoreSaberのローカル名は自前APIで取得した名前を渡す（内部の PlayerService は別コンテナで解決できないため）。
            _replayOwnership = ReplayDetector.GetOwnership(_playerDataCache.PlayerName);
            Plugin.Log.Info($"[LRCounter] Replay ownership at start: {_replayOwnership}");

            // ノーツ判定完了イベントを購読（倍率は各ノーツのscoringElementから取得する）
            _scoreController.scoringForNoteFinishedEvent += OnScoringForNoteFinished;
            // スコア確定イベントを購読。multipliedScore はノーツ確定イベントの後で更新されるため、
            // 検算用のゲームスコアはこちらの最新値で受け取る（直読みだと1ノーツ前の値になる）。
            _scoreController.scoreDidChangeEvent += OnScoreDidChange;
            // 体力変化イベントを購読してエネルギーが0になったらペナルティを適用
            _energyCounter.gameEnergyDidChangeEvent += OnEnergyChanged;

            // ローカルの合算自己ベスト（PB）精度を読む（ネットワーク不要・即時）
            ComputeSelfBestAccuracy();
            // この譜面の左右ベスト精度を読む（永続ストアから・ネットワーク不要・即時）
            LoadHandBestAccuracies();

            // Star評価を取得する（キャッシュ済みなら即時、初回のみScoreSaber APIを叩く）
            double fetched = await FetchStarRatingAsync();
            if (fetched > 0)
            {
                StarRating = fetched;
                ApplyStarRating();
                Plugin.DebugLog($"[LRCounter] Fetched StarRating={StarRating:F2}");
            }
            else
            {
                Plugin.DebugLog("[LRCounter] Map is unranked or fetch failed.");
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

            // 左右ベスト精度との差分を求める（フルクリア時のみ。Set前に求めて結果ストアへ渡す）。
            (bool hasDelta, double leftDelta, double rightDelta) = UpdateAndDiffHandBests();

            // 曲終了時点の左右の平均精度・PPをリザルト画面用に保存する。
            // NF失敗時は提出スコアが半減するので、リザルト表示の精度・PPとも実効値（精度×係数）にする。
            // PPは精度の関数なので、実効精度から再計算する（通常クリアは係数1.0＝従来と同値）。
            // （差分も UpdateAndDiffHandBests で同じ実効精度を使って計算済み＝表示と差分が一致する）
            double leftEffAcc = LeftTracker.Accuracy * _scoreFactor;
            double rightEffAcc = RightTracker.Accuracy * _scoreFactor;
            _resultStore.Set(
                leftEffAcc * 100.0,
                rightEffAcc * 100.0,
                PPCalculator.CalculatePP(leftEffAcc, StarRating),
                PPCalculator.CalculatePP(rightEffAcc, StarRating),
                StarRating > 0,
                LeftTracker.CutNotes,
                LeftTracker.TotalNotes,
                RightTracker.CutNotes,
                RightTracker.TotalNotes,
                hasDelta,
                leftDelta,
                rightDelta);

            // クリアまで到達したプレイなら、推定PPをキャッシュへローカル反映する（API呼び出しなし）。
            // これでセッション中に出した新スコアが次のThresholdベースラインに反映される。
            TryUpdateCachedScore();

            GC.SuppressFinalize(this);
        }

        // 今回のプレイの推定PPでキャッシュ内の自己スコアを更新する。
        // ScoreSaberに実際に提出される（＝クリアした）プレイだけを対象にするため、
        // 全ノーツを判定し終えたかどうかで判定する。途中リスタート・手動退出・(NFなしの)失敗は
        // 最大スコアが満たないのでここで弾ける。
        // 注意: スコア修飾子（Slower Song等）使用時は提出PPが推定より低くなるが、
        // Thresholdラインがやや高めに出るだけなので許容する。
        private void TryUpdateCachedScore()
        {
            try
            {
                if (StarRating <= 0 || TotalPP <= 0) return;          // アンランクは対象外
                if (_penaltyApplied) return;                          // NF失敗は提出スコアが半減するので推定が合わない
                if (_sceneSetupData.practiceSettings != null) return; // 練習モードは提出されない
                if (ReplayDetector.ShouldSkip(_replayOwnership)) return; // 他人(または所有者不明)のリプレイはキャッシュも汚さない

                string? hash = GetCurrentMapHash();
                if (hash == null) return;

                int fullMaxScore = ScoreModel.ComputeMaxMultipliedScoreForBeatmap(_sceneSetupData.transformedBeatmapData);
                if (fullMaxScore <= 0 || _maxTotalScore < fullMaxScore) return; // クリアまで到達していない

                var key = _sceneSetupData.beatmapKey;
                _playerDataCache.UpdateLocalScore(
                    hash,
                    ToScoreSaberDifficulty(key.difficulty),
                    $"Solo{key.beatmapCharacteristic.serializedName}",
                    TotalPP);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[LRCounter] TryUpdateCachedScore failed: {ex.Message}");
            }
        }

        // ─── 左右ベスト精度（永続）の更新と差分 ──────────────────────────────────────

        // フルクリア時に、この譜面の左右ベスト精度を更新し、更新前ベストとの差分(%)を返す。
        // 戻り値 hasDelta=false: 差分を表示しない（練習モード・未クリア・初回プレイ・キー取得失敗）。
        // 練習モードや途中退出は提出と無関係＝ベストとして不適切なので除外する（推定PB汚染を防ぐ）。
        private (bool hasDelta, double leftDelta, double rightDelta) UpdateAndDiffHandBests()
        {
            try
            {
                if (_sceneSetupData.practiceSettings != null) return (false, 0, 0); // 練習は記録しない
                // 他人(または所有者不明)のリプレイは記録しない。自分のリプレイは過去ベストの復元になりうるので記録する。
                if (ReplayDetector.ShouldSkip(_replayOwnership)) return (false, 0, 0);

                int fullMaxScore = ScoreModel.ComputeMaxMultipliedScoreForBeatmap(_sceneSetupData.transformedBeatmapData);
                if (fullMaxScore <= 0 || _maxTotalScore < fullMaxScore) return (false, 0, 0); // フルクリアのみ

                string key = BuildMapKey();
                // NF失敗時は提出スコアが半減するので、自己ベスト判定・保存も実効精度（精度×係数）で行う。
                // 通常クリアは係数1.0で従来どおり。失敗プレイは半分の値になるため、既存ベスト(=通常クリア)を
                // 上書きしにくくなり、誤って「ベスト更新」と記録されるのを防ぐ。
                double curLeft = LeftTracker.Accuracy * 100.0 * _scoreFactor;
                double curRight = RightTracker.Accuracy * 100.0 * _scoreFactor;

                // 差分は「更新前のベスト」と比較するので、更新前に読み出す。
                bool had = _handAccuracyStore.TryGet(key, out double oldLeft, out double oldRight);
                // 付随情報（左右PP・曲名・作者）も保存する。PPはアンランクなら0。
                var level = _sceneSetupData.beatmapLevel;
                string songName = level != null ? level.songName : "";
                string author = level != null && level.allMappers != null ? string.Join(", ", level.allMappers) : "";
                _handAccuracyStore.UpdateIfBetter(key, curLeft, curRight, LeftTracker.PP, RightTracker.PP, songName, author);

                if (!had) return (false, 0, 0); // 初回プレイは比較対象が無いので差分なし
                return (true, curLeft - oldLeft, curRight - oldRight);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[LRCounter] UpdateAndDiffHandBests failed: {ex.Message}");
                return (false, 0, 0);
            }
        }

        // 左右ベスト精度の永続キー。難易度・ゲームモード違いは別譜面として扱う。
        private string BuildMapKey()
        {
            var key = _sceneSetupData.beatmapKey;
            return $"{key.levelId}|{(int)key.difficulty}|{key.beatmapCharacteristic.serializedName}";
        }

        // ─── 自己ベスト精度の読み込み ────────────────────────────────────────────────

        // ローカルのPlayerDataから、この譜面の合算（両手）自己ベストスコアを読み、最大スコアで割って精度(0〜1)を求める。
        // 画面の「PB」表示と同じローカル記録を使うのでネットワーク不要。未プレイ/記録なしは0のまま。
        // 注意: highScore は修飾子込みの実スコアなので、スコア修飾子(NF/Faster等)を付けた記録は精度がずれる。
        private void ComputeSelfBestAccuracy()
        {
            try
            {
                var stats = _playerDataModel.playerData.TryGetPlayerLevelStatsData(_sceneSetupData.beatmapKey);
                if (stats == null || !stats.validScore || stats.highScore <= 0) return;

                int maxScore = ScoreModel.ComputeMaxMultipliedScoreForBeatmap(_sceneSetupData.transformedBeatmapData);
                if (maxScore <= 0) return;

                double acc = (double)stats.highScore / maxScore;
                _selfBestAccuracy = acc < 0 ? 0 : (acc > 1 ? 1 : acc); // 念のため[0,1]に丸める
                Plugin.DebugLog($"[LRCounter] SelfBest (local PB): {stats.highScore}/{maxScore} = {_selfBestAccuracy * 100:F2}%");
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[LRCounter] ComputeSelfBestAccuracy failed: {ex.Message}");
            }
        }

        // ─── 左右ベスト精度（永続）の読み込み ────────────────────────────────────────

        // この譜面の左右ベスト精度を HandAccuracyStore から読み、0〜1 に直して保持する。
        // 今回プレイで更新する前の値（＝前回までのベスト）なので、ボーダー点灯の比較基準になる。
        // 練習モードは記録対象外なのでベースラインも読まない（誤点灯防止）。記録なしは0のまま。
        private void LoadHandBestAccuracies()
        {
            try
            {
                if (_sceneSetupData.practiceSettings != null) return; // 練習は比較対象にしない

                if (_handAccuracyStore.TryGet(BuildMapKey(), out double left, out double right))
                {
                    LeftBestAccuracy = left / 100.0;   // 保存は%なので0〜1へ
                    RightBestAccuracy = right / 100.0;
                    Plugin.DebugLog($"[LRCounter] HandBest baseline: L={left:F2}% R={right:F2}%");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[LRCounter] LoadHandBestAccuracies failed: {ex.Message}");
            }
        }

        // ─── Star評価の取得 ────────────────────────────────────────────────────────

        // 現在プレイ中の譜面のStar評価を取得する（PlayerDataCache経由・セッション中キャッシュ）
        private Task<double> FetchStarRatingAsync()
        {
            var key = _sceneSetupData.beatmapKey;
            var levelId = key.levelId;

            // custom_level_ プレフィックスがない = 公式譜面なのでスキップ
            if (!levelId.StartsWith("custom_level_", StringComparison.OrdinalIgnoreCase))
            {
                Plugin.DebugLog($"[LRCounter] Not a custom level ({levelId}), skipping.");
                return Task.FromResult(0.0);
            }

            // levelId から SHA-1 ハッシュ部分だけを取り出す
            string hash = levelId.Substring("custom_level_".Length).ToUpperInvariant();
            int diff = ToScoreSaberDifficulty(key.difficulty);
            string gameMode = $"Solo{key.beatmapCharacteristic.serializedName}";

            return _playerDataCache.GetStarRatingAsync(hash, diff, gameMode);
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

        // キャッシュ済みのランクスコア一覧から、今曲でいくつ以上のPPを出せばランクが上がるかを計算する
        private async Task InitThresholdAsync()
        {
            try
            {
                // プレイヤーデータはAppスコープのキャッシュから読む（APIは起動時の1回だけ。
                // 起動時に失敗していた場合のみここで再取得される）
                bool loaded = await _playerDataCache.EnsureLoadedAsync();
                if (!loaded)
                {
                    Plugin.Log.Warn("[LRCounter] Player data not available; threshold disabled.");
                    return;
                }

                string? currentHash = GetCurrentMapHash();
                // 難易度・ゲームモードも照合に使う（同じ譜面でも難易度違いは別リーダーボード＝別スコア）
                var currentKey = _sceneSetupData.beatmapKey;
                int currentDiff = ToScoreSaberDifficulty(currentKey.difficulty);
                string currentMode = $"Solo{currentKey.beatmapCharacteristic.serializedName}";

                Plugin.DebugLog($"[LRCounter] PlayerTotalPP={_playerDataCache.TotalPP:F2} (cached)");

                var rankedScores = _playerDataCache.GetScoresSnapshot();

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
                        // 今プレイ中の曲の既存スコアはThreshold計算では新スコアに置き換わるので候補から外す。
                        Plugin.DebugLog($"[LRCounter] Existing score for this map (pp={rankedScores[idx].pp:F2}) will be replaced");
                        others.RemoveAt(idx);
                    }
                }

                // 「このPP以上を出せばトータルが GainEpsilon 以上増える」最低ラインを計算
                ThresholdPP = ScoreSaberApiService.CalculateThreshold(others, baseline, GainEpsilon);
                Plugin.DebugLog($"[LRCounter] ThresholdPP={ThresholdPP:F2}");

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
            int idealMult = scoringElement.maxMultiplier; // FC想定倍率（分母用）
            int cutScore = scoringElement.cutScore;      // 生スコア（グッド以外は0）
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
            _totalScore += cutScore * actualMult;
            _maxTotalScore += maxCut * idealMult;

            // 両手合算の精度からTotalPPを計算
            if (StarRating > 0 && _maxTotalScore > 0)
            {
                double totalAcc = (double)_totalScore / _maxTotalScore;
                TotalPP = PPCalculator.CalculatePP(totalAcc, StarRating);
            }

            // 表示を更新させる
            OnPPUpdated?.Invoke();
        }

        // ゲームのスコアが確定（再計算）されたタイミングで呼ばれる。modifiedScore はこの時点で
        // 今切ったノーツまで反映済みなので、検算用のゲームスコアをここで最新値に更新する。
        // 更新後に表示も更新させ、デバッグ表示が1ノーツ前の値を出さないようにする。
        // （multipliedScore は使わないが scoreDidChangeEvent のシグネチャ上、引数として受け取る）
        private void OnScoreDidChange(int multipliedScore, int modifiedScore)
        {
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
            Plugin.DebugLog("[LRCounter] No Fail: score factor set to 0.5 (score & max halved, accuracy unchanged).");
            OnPPUpdated?.Invoke();
        }
    }
}
