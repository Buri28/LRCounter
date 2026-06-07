using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Zenject;

namespace LRCounter.Controllers
{
    // ScoreSaber Web APIへのHTTPリクエストをまとめたサービスクラス
    // JSONパースにはRegexを使用（System.Text.Jsonが使えない環境のため）
    public class ScoreSaberApiService
    {
        // HttpClientはインスタンスを使い回すのがベストプラクティス（都度newするとポートを使い切る）
        private static readonly HttpClient Http = new HttpClient();

        // Beat Saber のプラットフォーム抽象。Steam/Oculus などに依存せずユーザー情報を取得する。
        // Steamworks に直接依存すると、DepotDownloader 系インストール（Steamworks未同梱）や
        // Oculus版で動かないため、必ずこの抽象経由で取得する。
        private readonly IPlatformUserModel? _platformUserModel;

        [Inject]
        public ScoreSaberApiService([InjectOptional] IPlatformUserModel? platformUserModel = null)
        {
            _platformUserModel = platformUserModel;
        }

        static ScoreSaberApiService()
        {
            Http.Timeout = TimeSpan.FromSeconds(10); // タイムアウト10秒
        }

        // ─── プレイヤーID取得 ──────────────────────────────────────────────────────

        // ローカルプレイヤーのプラットフォームユーザーID（Steamなら SteamID64）を文字列で返す。
        // Beat Saber の IPlatformUserModel から取得するので Steam/Oculus いずれでも動作する。
        public async Task<string?> GetPlayerIdAsync()
        {
            if (_platformUserModel == null)
            {
                Plugin.Log.Warn("[ScoreSaberApi] IPlatformUserModel not available; cannot get player id.");
                return null;
            }

            try
            {
                UserInfo userInfo = await _platformUserModel.GetUserInfo(CancellationToken.None);
                if (userInfo == null || string.IsNullOrEmpty(userInfo.platformUserId))
                {
                    Plugin.Log.Warn("[ScoreSaberApi] GetUserInfo returned no user id.");
                    return null;
                }
                Plugin.Log.Info($"[ScoreSaberApi] Platform={userInfo.platform} UserId={userInfo.platformUserId}");
                return userInfo.platformUserId;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[ScoreSaberApi] GetPlayerId failed: {ex.Message}");
                return null;
            }
        }

        // ─── プレイヤー情報取得 ────────────────────────────────────────────────────

        // ScoreSaberに登録されているプレイヤーの現在合計PPを取得する
        // エンドポイント(v2): /api/v2/players/{id}
        public async Task<double> GetPlayerTotalPPAsync(string playerId)
        {
            try
            {
                string url = $"https://scoresaber.com/api/v2/players/{playerId}";
                Plugin.Log.Info($"[ScoreSaberApi] GET {url}");
                string json = await Http.GetStringAsync(url);

                // v2では合計PPは stats.totalPP に入っている（v1のトップレベル "pp" から変更）
                var m = Regex.Match(json, @"""totalPP""\s*:\s*([\d.]+)");
                if (m.Success && double.TryParse(
                        m.Groups[1].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double pp))
                    return pp;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[ScoreSaberApi] GetPlayerTotalPP failed: {ex.Message}");
            }
            return 0;
        }

        // ─── 譜面Star評価取得 ──────────────────────────────────────────────────────

        // 譜面ハッシュ・難易度・ゲームモードを指定してStar評価を取得する
        // アンランク譜面や通信失敗の場合は0を返す
        // エンドポイント(v2): /api/v2/leaderboards/hash/{hash}/{mode}/{difficulty}
        // （v1のクエリ指定からパス指定に変更。mode例="SoloStandard", difficulty=1/3/5/7/9）
        public async Task<double> GetLeaderboardStarsAsync(string hash, int difficulty, string gameMode)
        {
            try
            {
                string url = $"https://scoresaber.com/api/v2/leaderboards/hash/{hash}/{gameMode}/{difficulty}";
                Plugin.Log.Info($"[ScoreSaberApi] GET {url}");
                string json = await Http.GetStringAsync(url);

                // JSONから "stars": 数値 を抽出
                var m = Regex.Match(json, @"""stars""\s*:\s*([\d.]+)");
                if (m.Success && double.TryParse(
                        m.Groups[1].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double stars))
                    return stars;

                Plugin.Log.Warn("[ScoreSaberApi] 'stars' field not found.");
            }
            catch (HttpRequestException ex)
            {
                // 404などAPIエラーは通常のケース（アンランク等）なのでInfoレベルで記録
                Plugin.Log.Info($"[ScoreSaberApi] Leaderboard API: {ex.Message}");
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[ScoreSaberApi] GetLeaderboardStars failed: {ex.Message}");
            }
            return 0;
        }

        // ─── ランクスコア取得（ページング） ─────────────────────────────────────────

        // プレイヤーのランクスコアを (PP値, 譜面ハッシュ) のリストとして取得する。
        // 1ページ100件で maxPages 分を「並列」取得する（逐次より速い）。
        // Top100だけだと101位以下の微小寄与を無視してしまうため、Threshold精緻化のため深めに取る。
        // PP降順でソート済みのリストを返す。
        // エンドポイント(v2): /api/v2/players/{id}/scores?sort=top&limit=100&page=N
        public async Task<List<(double pp, string hash, int difficulty, string gameMode)>> GetTopScoresAsync(string playerId, int maxPages)
        {
            var scores = new List<(double pp, string hash, int difficulty, string gameMode)>();
            try
            {
                // 各ページを並列でリクエストして一括待ち合わせる。範囲外ページの404等は
                // SafeGetStringAsync が "" を返すので、1ページの失敗で全体が落ちることはない。
                var pageTasks = new List<Task<string>>();
                for (int page = 1; page <= maxPages; page++)
                {
                    string url = $"https://scoresaber.com/api/v2/players/{playerId}/scores?sort=top&limit=100&page={page}";
                    Plugin.Log.Info($"[ScoreSaberApi] GET {url}");
                    pageTasks.Add(SafeGetStringAsync(url));
                }
                string[] jsons = await Task.WhenAll(pageTasks);

                foreach (string json in jsons)
                {
                    // 1スコア = score{...pp...weight...} + leaderboard{ map{hash}, difficulty{difficulty,gameMode} }
                    // という順で並ぶので、pp/hash/難易度/モードを同順で抽出してインデックスでペアにする。
                    // （同じ譜面でも難易度・モードが違えば別リーダーボード＝別スコアなので全部で照合する）
                    var ppMatches = Regex.Matches(json,
                        @"""pp""\s*:\s*([\d.]+)\s*,\s*""weight""");
                    // v2では譜面ハッシュは leaderboard.map.hash（v1の "songHash" から変更）
                    var hashMatches = Regex.Matches(json,
                        @"""hash""\s*:\s*""([A-Fa-f0-9]+)""",
                        RegexOptions.IgnoreCase);
                    // 難易度は difficulty オブジェクト内の整数 "difficulty":N（外側キーは "difficulty":{ なので一致しない）
                    var diffMatches = Regex.Matches(json, @"""difficulty""\s*:\s*(\d+)");
                    var modeMatches = Regex.Matches(json, @"""gameMode""\s*:\s*""([^""]+)""");

                    // 4種の抽出数が揃う範囲でペアにする
                    int count = Math.Min(Math.Min(ppMatches.Count, hashMatches.Count),
                                         Math.Min(diffMatches.Count, modeMatches.Count));
                    for (int i = 0; i < count; i++)
                    {
                        if (double.TryParse(ppMatches[i].Groups[1].Value,
                                NumberStyles.Float, CultureInfo.InvariantCulture, out double pp)
                            && int.TryParse(diffMatches[i].Groups[1].Value, out int diff))
                            scores.Add((pp, hashMatches[i].Groups[1].Value.ToUpperInvariant(),
                                        diff, modeMatches[i].Groups[1].Value));
                    }
                }

                // PP降順でソート
                scores.Sort((a, b) => b.pp.CompareTo(a.pp));
                Plugin.Log.Info($"[ScoreSaberApi] Parsed {scores.Count} ranked scores.");
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[ScoreSaberApi] GetTopScores failed: {ex.Message}");
            }
            return scores;
        }

        // GetStringAsync を例外で全体が落ちないようにラップする（範囲外ページの404等は ""）
        private static async Task<string> SafeGetStringAsync(string url)
        {
            try
            {
                return await Http.GetStringAsync(url);
            }
            catch (Exception ex)
            {
                Plugin.Log.Info($"[ScoreSaberApi] page fetch skipped: {ex.Message}");
                return "";
            }
        }

        // ─── Threshold（トータルPPを底上げするのに必要な最低PP）の計算 ──────────────

        // ScoreSaberの重み付け: rank位(0始まり・PP降順)のスコアに 0.965^rank を掛けて合算する。
        // ScoreSaberはTop100だけでなく全スコアを重み付けするため、ここでも件数で打ち切らず全件で計算する。
        private const double WeightDecay = 0.965;

        // PP降順リストの重み付けトータルを返す（全件）
        public static double WeightedTotal(List<double> sortedDesc)
        {
            double total = 0;
            for (int rank = 0; rank < sortedDesc.Count; rank++)
                total += sortedDesc[rank] * Math.Pow(WeightDecay, rank);
            return total;
        }

        // others(PP降順) に newPP を1件挿入したときの重み付けトータルを返す（挿入後の全件）。
        // リストを実体化せずインデックス計算で求める。
        private static double WeightedTotalWithInsert(List<double> sortedDescOthers, double newPP)
        {
            // newPPの挿入位置（降順を保つ位置）を求める
            int pos = 0;
            while (pos < sortedDescOthers.Count && sortedDescOthers[pos] > newPP)
                pos++;

            double total = 0;
            int n = sortedDescOthers.Count + 1; // 挿入後の件数
            int inserted = 0; // 0=未挿入, 1=挿入済み
            for (int rank = 0; rank < n; rank++)
            {
                double val;
                if (inserted == 0 && rank == pos)
                {
                    val = newPP;
                    inserted = 1;
                }
                else
                {
                    int idx = rank - inserted;
                    val = idx < sortedDescOthers.Count ? sortedDescOthers[idx] : 0;
                }
                total += val * Math.Pow(WeightDecay, rank);
            }
            return total;
        }

        // 「このPP以上を出せばトータルが gainEpsilon 以上増える」最低ラインを二分探索で求める。
        // others     : 今の曲の既存スコアを除いた全ランクスコア（PP降順）。新スコアはこの枠に挿入される。
        // baseline   : 既存スコア込みの現在の重み付けトータル（＝守るべき現状値）。
        // gainEpsilon: 「増えた」とみなす最小増分（例 0.01pp）。
        //   全スコアを対象にするので、101位以下の微小寄与も考慮した実際の増分で判定できる。
        //   既にその曲でスコアがあるなら、それを上回って初めて増える＝threshold は概ね自己ベスト〜100位の間に出る。
        public static double CalculateThreshold(List<double> others, double baseline, double gainEpsilon)
        {
            double target = baseline + gainEpsilon;
            // 二分探索で「挿入後トータルが target 以上になるギリギリの値」を64回反復で絞り込む
            double low = 0, high = 3000;
            for (int i = 0; i < 64; i++)
            {
                double mid = (low + high) / 2.0;
                if (WeightedTotalWithInsert(others, mid) >= target)
                    high = mid; // midで gainEpsilon 以上増えるなら上限を下げる
                else
                    low = mid;  // 足りないなら下限を上げる
            }
            return high;
        }
    }
}
