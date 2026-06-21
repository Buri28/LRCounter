using LRCounter.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Zenject;

namespace LRCounter.Controllers
{
    // ScoreSaberから取得するデータをセッション中キャッシュするサービス（App スコープ）。
    // 旧実装はマップ開始のたびに scores 3ページ + totalPP + Star の計5本を取得しており、
    // リトライ・リスタートを繰り返すとレートリミットに到達していた。
    //  - プレイヤーデータ（ランクスコア一覧・合計PP）はマップ非依存なので起動時に1回だけ取得
    //  - Star評価は (hash, difficulty, gameMode) 依存なので初回取得後にセッション中保持
    // これで同じ曲のリトライ・リスタートではAPI呼び出しが0本になる。
    public class PlayerDataCache : IInitializable
    {
        private readonly ScoreSaberApiService _apiService;
        private readonly PluginConfig _config;

        // Threshold計算で取得するランクスコアのページ数（1ページ100件）。101位以下の微小寄与まで
        // 反映するため深めに取る。0.1pp判定なら必要な深さは概ね rank≈250 までなので3ページ(300件)で十分。
        private const int ScorePagesToFetch = 3;

        // プレイヤーデータの取得タスク。成功したら以降は使い回す（セッション中キャッシュ）。
        // 失敗していたら EnsureLoadedAsync が再試行する（起動時にネットワークが無かった場合など）。
        private Task<bool>? _loadTask;

        private string? _playerId;
        // ランクスコア一覧（PP降順）。クリア後はローカル更新されるので生リストは公開しない。
        private List<(double pp, string hash, int difficulty, string gameMode)> _scores = new List<(double, string, int, string)>();

        // ScoreSaberに登録されているプレイヤーの合計PP（クリア後はローカル更新で増分を反映）
        public double TotalPP { get; private set; }

        // ScoreSaberプロフィールの表示名（リプレイ所有者判定のローカル名）。未取得は null。
        public string? PlayerName { get; private set; }

        // Star評価のキャッシュ。アンランク(0)もキャッシュしてリトライ時の再取得を防ぐ。
        private readonly Dictionary<(string hash, int difficulty, string gameMode), double> _starCache
            = new Dictionary<(string, int, string), double>();

        [Inject]
        public PlayerDataCache(ScoreSaberApiService apiService, PluginConfig config)
        {
            _apiService = apiService;
            _config = config;
        }

        // ゲーム起動時にZenjectから1回だけ呼ばれる。プレイヤーデータを先読みしておく
        // （ここで失敗しても、マップ開始時の EnsureLoadedAsync が再試行する）。
        public void Initialize()
        {
            if (!_config.Enabled) return;
            _ = EnsureLoadedAsync();
        }

        // プレイヤーデータがロード済みであることを保証する。
        // 取得中なら同じタスクを返し（多重リクエスト防止）、前回失敗していたら再試行する。
        // Unityのメインスレッド（同期コンテキスト）からのみ呼ばれる前提なのでロックは不要。
        public Task<bool> EnsureLoadedAsync()
        {
            if (_loadTask == null || (_loadTask.IsCompleted && !_loadTask.Result))
                _loadTask = LoadAsync();
            return _loadTask;
        }

        // プレイヤーデータ（合計PP・ランクスコア一覧）を取得する。例外は投げず false を返す。
        private async Task<bool> LoadAsync()
        {
            try
            {
                // playerId はローカル取得（HTTPなし）。一度取れたら使い回す
                if (string.IsNullOrEmpty(_playerId))
                    _playerId = await _apiService.GetPlayerIdAsync();
                if (string.IsNullOrEmpty(_playerId))
                {
                    Plugin.Log.Warn("[LRCounter] PlayerDataCache: could not get player ID.");
                    return false;
                }

                // バーストを避けるため並列ではなく直列で取得する（起動時の1回だけなので遅くても問題ない）。
                // 合計PPと表示名は同じ /players/{id} 応答なので1回でまとめて取得する。
                // 表示名はリプレイ所有者判定（自分/他人）のローカル名に使う。
                var (totalPP, name) = await _apiService.GetPlayerProfileAsync(_playerId!);
                if (totalPP == null)
                {
                    Plugin.Log.Warn("[LRCounter] PlayerDataCache: failed to fetch total PP; will retry next map.");
                    return false;
                }

                var scores = await _apiService.GetTopScoresAsync(_playerId!, ScorePagesToFetch);

                TotalPP = totalPP.Value;
                PlayerName = name;
                _scores = scores;
                Plugin.DebugLog($"[LRCounter] PlayerDataCache loaded: totalPP={TotalPP:F2}, scores={_scores.Count}, name='{PlayerName}'");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[LRCounter] PlayerDataCache load failed: {ex.Message}");
                return false;
            }
        }

        // ランクスコア一覧のスナップショットを返す（PP降順）。
        // 呼び出し側で自由に加工できるよう、キャッシュ本体とは別のリストを返す。
        public List<(double pp, string hash, int difficulty, string gameMode)> GetScoresSnapshot()
            => new List<(double, string, int, string)>(_scores);

        // 譜面のStar評価を返す。初回はScoreSaber APIから取得し、以降はキャッシュを返す。
        // アンランク(0)もキャッシュするが、通信失敗(null)はキャッシュせず次回再試行する。
        public async Task<double> GetStarRatingAsync(string hash, int difficulty, string gameMode)
        {
            var key = (hash, difficulty, gameMode);
            if (_starCache.TryGetValue(key, out double cached))
            {
                Plugin.DebugLog($"[LRCounter] StarRating cache hit: {cached:F2}");
                return cached;
            }

            double? stars = await _apiService.GetLeaderboardStarsAsync(hash, difficulty, gameMode);
            if (stars == null) return 0; // 通信失敗はキャッシュしない（次回再試行）

            _starCache[key] = stars.Value;
            return stars.Value;
        }

        // クリアしたプレイの推定PPでキャッシュ内の自己スコアをローカル更新する（API呼び出しなし）。
        // セッション中に出した新スコアを次のThreshold計算のベースラインへ反映するため。
        // 既存スコア以下なら何もしない（ScoreSaberも上回ったときだけ置き換えるため挙動が一致する）。
        public void UpdateLocalScore(string hash, int difficulty, string gameMode, double newPP)
        {
            // ロード成功前は基準が無いので何もしない
            if (_loadTask == null || !_loadTask.IsCompleted || !_loadTask.Result) return;

            int idx = _scores.FindIndex(s =>
                s.hash == hash && s.difficulty == difficulty && s.gameMode == gameMode);
            if (idx >= 0 && _scores[idx].pp >= newPP) return;

            double oldTotal = ScoreSaberApiService.WeightedTotal(_scores.Select(s => s.pp).ToList());

            if (idx >= 0) _scores.RemoveAt(idx);
            int pos = _scores.FindIndex(s => s.pp < newPP); // PP降順を保つ挿入位置
            if (pos < 0) pos = _scores.Count;
            _scores.Insert(pos, (newPP, hash, difficulty, gameMode));

            // 合計PPは重み付けトータルの増分だけ加算する（300件より下位の寄与はほぼ0なので十分正確）
            double newTotal = ScoreSaberApiService.WeightedTotal(_scores.Select(s => s.pp).ToList());
            TotalPP += newTotal - oldTotal;
            Plugin.DebugLog($"[LRCounter] Local score updated: {newPP:F2}pp (totalPP {TotalPP:F2}, +{newTotal - oldTotal:F2})");
        }
    }
}
