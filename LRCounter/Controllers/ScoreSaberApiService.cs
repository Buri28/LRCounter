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

        // 例外の真因を1行に整形する。HttpRequestException の "An error occurred while sending the request"
        // は定型文で、実際の原因（SocketException=接続切断 / TLS失敗 / ObjectDisposed=HttpClient破棄 等）は
        // InnerException 側に入っている。切り分けのため型名込みで内側まで全部つなげて出す。
        private static string Describe(Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            for (Exception? e = ex; e != null; e = e.InnerException)
            {
                if (sb.Length > 0) sb.Append(" -> ");
                sb.Append(e.GetType().Name).Append(": ").Append(e.Message);
            }
            return sb.ToString();
        }

        // ─── v1フォールバック制御 ──────────────────────────────────────────────────

        // v2 APIが失敗して v1 で成功したら、以降このセッションは v1 に直行する
        // （落ちている v2 へのリクエスト＋タイムアウト10秒を毎回払わないため）。
        // 両方失敗した場合はネットワーク全断の可能性があるのでフラグは立てず、次回も v2 から試す。
        private bool _v2Unavailable = false;

        private void NoteV2Unavailable()
        {
            if (_v2Unavailable) return;
            _v2Unavailable = true;
            Plugin.Log.Warn("[ScoreSaberApi] v2 API unavailable; switching to v1 API for this session.");
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
                Plugin.DebugLog($"[ScoreSaberApi] Platform={userInfo.platform} UserId={userInfo.platformUserId}");
                return userInfo.platformUserId;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[ScoreSaberApi] GetPlayerId failed: {Describe(ex)}");
                return null;
            }
        }

        // ─── プレイヤー情報取得 ────────────────────────────────────────────────────

        // ScoreSaberに登録されているプレイヤーの「合計PP」と「表示名」を1回の取得でまとめて返す。
        // どちらも同じ /players/{id} 応答に含まれるので、APIは1本で済む（以前は totalPP と name で2回叩いていた）。
        // 表示名はリプレイ所有者判定（自分/他人）のローカル名に使う。
        // totalPP が取れなければ失敗扱いで (null, name) を返す（呼び出し側でキャッシュせず再試行できるよう0と区別する）。
        // v2が失敗したらv1へフォールバックする。
        // エンドポイント(v2): /api/v2/players/{id}   … 合計PP=stats.totalPP / 表示名=トップレベル "name"
        // エンドポイント(v1): /api/player/{id}/full  … 合計PP=トップレベル "pp"  / 表示名=トップレベル "name"
        public async Task<(double? totalPP, string? name)> GetPlayerProfileAsync(string playerId)
        {
            if (!_v2Unavailable)
            {
                // v2では合計PPは stats.totalPP に入っている（v1のトップレベル "pp" から変更）
                var v2 = await FetchProfileAsync(
                    $"https://scoresaber.com/api/v2/players/{playerId}",
                    @"""totalPP""\s*:\s*([\d.]+)");
                if (v2.totalPP != null) return v2;
            }

            // v1ではトップレベルの "pp"。最初に一致する "pp": がそれ
            var v1 = await FetchProfileAsync(
                $"https://scoresaber.com/api/player/{playerId}/full",
                @"""pp""\s*:\s*([\d.]+)");
            if (v1.totalPP != null) NoteV2Unavailable();
            return v1;
        }

        // 指定URLを1回だけ取得し、合計PP（ppPattern の最初の一致）と表示名（最初の "name"）を同時に取り出す。
        // 表示名はリッチテキスト（色付き名）込みの生の値。正規化は呼び出し側（所有者判定）で行う。失敗時は (null, null)。
        private static async Task<(double? totalPP, string? name)> FetchProfileAsync(string url, string ppPattern)
        {
            try
            {
                Plugin.DebugLog($"[ScoreSaberApi] GET {url}");
                string json = await Http.GetStringAsync(url);

                double? pp = null;
                var pm = Regex.Match(json, ppPattern);
                if (pm.Success && double.TryParse(
                        pm.Groups[1].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double value))
                    pp = value;
                else
                    Plugin.Log.Warn("[ScoreSaberApi] 'totalPP/pp' field not found.");

                string? name = null;
                var nm = Regex.Match(json, @"""name""\s*:\s*""((?:[^""\\]|\\.)*)""");
                if (nm.Success) name = Regex.Unescape(nm.Groups[1].Value);

                return (pp, name);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[ScoreSaberApi] GET profile failed: {Describe(ex)}");
                return (null, null);
            }
        }

        // ─── 譜面Star評価取得 ──────────────────────────────────────────────────────

        // 譜面ハッシュ・難易度・ゲームモードを指定してStar評価を取得する。
        // アンランク（リーダーボード未登録=404）は0、通信失敗（レートリミット等）は null を返す。
        // 呼び出し側（PlayerDataCache）が「0はキャッシュ・nullは再試行」と区別できるようにするため。
        // v2が失敗したらv1へフォールバックする（404=アンランクは確定値なのでフォールバックしない）。
        // エンドポイント(v2): /api/v2/leaderboards/hash/{hash}/{mode}/{difficulty}
        // エンドポイント(v1): /api/leaderboard/by-hash/{hash}/info?difficulty={n}&gameMode={mode}
        public async Task<double?> GetLeaderboardStarsAsync(string hash, int difficulty, string gameMode)
        {
            if (!_v2Unavailable)
            {
                double? stars = await FetchStarsAsync(
                    $"https://scoresaber.com/api/v2/leaderboards/hash/{hash}/{gameMode}/{difficulty}");
                if (stars != null) return stars; // 0（アンランク404）も確定値として返す
            }

            double? v1Stars = await FetchStarsAsync(
                $"https://scoresaber.com/api/leaderboard/by-hash/{hash}/info?difficulty={difficulty}&gameMode={gameMode}");
            if (v1Stars != null) NoteV2Unavailable();
            return v1Stars;
        }

        // 指定URLからStar評価を取得する。404（アンランク）は0、通信失敗・解析失敗は null。
        private static async Task<double?> FetchStarsAsync(string url)
        {
            try
            {
                Plugin.DebugLog($"[ScoreSaberApi] GET {url}");
                using var response = await Http.GetAsync(url);

                // 404 = リーダーボード未登録（アンランク）。正常ケースなのでInfoレベルで記録して0を返す
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    Plugin.DebugLog("[ScoreSaberApi] Leaderboard not found (unranked).");
                    return 0;
                }
                // レートリミット(429)やサーバーエラー等は失敗としてnullを返す（キャッシュさせない）
                if (!response.IsSuccessStatusCode)
                {
                    Plugin.Log.Warn($"[ScoreSaberApi] Leaderboard API returned {(int)response.StatusCode}.");
                    return null;
                }

                string json = await response.Content.ReadAsStringAsync();

                // JSONから "stars": 数値 を抽出
                var m = Regex.Match(json, @"""stars""\s*:\s*([\d.]+)");
                if (m.Success && double.TryParse(
                        m.Groups[1].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double stars))
                    return stars;

                Plugin.Log.Warn("[ScoreSaberApi] 'stars' field not found.");
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[ScoreSaberApi] GetLeaderboardStars failed: {Describe(ex)}");
            }
            return null;
        }

        // ─── ランクスコア取得（ページング） ─────────────────────────────────────────

        // ページ取得の間に挟むディレイ(ms)。同時バーストでレートリミットに当たるのを避ける。
        // 取得は起動時（キャッシュロード時）の1回だけなので、多少遅くても問題ない。
        private const int PageFetchDelayMs = 250;

        // プレイヤーのランクスコアを (PP値, 譜面ハッシュ) のリストとして取得する。
        // 1ページ100件で maxPages 分を「直列＋小ディレイ」で取得する（バースト保護）。
        // Top100だけだと101位以下の微小寄与を無視してしまうため、Threshold精緻化のため深めに取る。
        // PP降順でソート済みのリストを返す。ページ単位でv2→v1フォールバックする。
        // エンドポイント(v2): /api/v2/players/{id}/scores?sort=top&limit=100&page=N
        // エンドポイント(v1): /api/player/{id}/scores?sort=top&limit=100&page=N
        public async Task<List<(double pp, string hash, int difficulty, string gameMode)>> GetTopScoresAsync(string playerId, int maxPages)
        {
            var scores = new List<(double pp, string hash, int difficulty, string gameMode)>();
            try
            {
                for (int page = 1; page <= maxPages; page++)
                {
                    if (page > 1) await Task.Delay(PageFetchDelayMs); // バースト回避

                    int count = await FetchScoresPageAsync(playerId, page, scores);
                    // 失敗(-1)はここで打ち切り。1ページ100件未満ならそれが最終ページなので
                    // 以降のリクエストを省略する
                    if (count < 100) break;
                }

                // PP降順でソート
                scores.Sort((a, b) => b.pp.CompareTo(a.pp));
                Plugin.DebugLog($"[ScoreSaberApi] Parsed {scores.Count} ranked scores.");
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[ScoreSaberApi] GetTopScores failed: {Describe(ex)}");
            }
            return scores;
        }

        // ランクスコア1ページ分を取得して scores に追加し、ページ内の件数を返す（両APIとも失敗なら -1）。
        // 範囲外ページの404等は SafeGetStringAsync が "" を返すので、1ページの失敗で全体が落ちることはない。
        private async Task<int> FetchScoresPageAsync(
            string playerId, int page, List<(double pp, string hash, int difficulty, string gameMode)> scores)
        {
            if (!_v2Unavailable)
            {
                string url = $"https://scoresaber.com/api/v2/players/{playerId}/scores?sort=top&limit=100&page={page}";
                Plugin.DebugLog($"[ScoreSaberApi] GET {url}");
                string json = await SafeGetStringAsync(url);
                if (!string.IsNullOrEmpty(json))
                {
                    // v2では譜面ハッシュは leaderboard.map.hash（v1の "songHash" から変更）
                    return ParseScoresPage(json, @"""hash""\s*:\s*""([A-Fa-f0-9]+)""", scores);
                }
            }

            string v1Url = $"https://scoresaber.com/api/player/{playerId}/scores?sort=top&limit=100&page={page}";
            Plugin.DebugLog($"[ScoreSaberApi] GET {v1Url}");
            string v1Json = await SafeGetStringAsync(v1Url);
            if (string.IsNullOrEmpty(v1Json)) return -1;

            NoteV2Unavailable();
            // v1では譜面ハッシュは leaderboard.songHash
            return ParseScoresPage(v1Json, @"""songHash""\s*:\s*""([A-Fa-f0-9]+)""", scores);
        }

        // 1ページ分のJSONをパースして scores に追加し、ページ内のスコア件数を返す。
        // 1スコア = score{...pp...weight...} + leaderboard{ ハッシュ, difficulty{difficulty,gameMode} }
        // という順で並ぶので、pp/hash/難易度/モードを同順で抽出してインデックスでペアにする。
        // （同じ譜面でも難易度・モードが違えば別リーダーボード＝別スコアなので全部で照合する）
        private static int ParseScoresPage(
            string json, string hashPattern, List<(double pp, string hash, int difficulty, string gameMode)> scores)
        {
            var ppMatches = Regex.Matches(json,
                @"""pp""\s*:\s*([\d.]+)\s*,\s*""weight""");
            // 譜面ハッシュのキー名はAPIバージョンで異なるので呼び出し側からパターンで渡す
            var hashMatches = Regex.Matches(json, hashPattern, RegexOptions.IgnoreCase);
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
            return count;
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
                Plugin.DebugLog($"[ScoreSaberApi] page fetch skipped: {Describe(ex)}");
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
