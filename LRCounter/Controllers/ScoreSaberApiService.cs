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
        // エンドポイント: /api/player/{id}/full
        public async Task<double> GetPlayerTotalPPAsync(string playerId)
        {
            try
            {
                string url = $"https://scoresaber.com/api/player/{playerId}/full";
                Plugin.Log.Info($"[ScoreSaberApi] GET {url}");
                string json = await Http.GetStringAsync(url);

                // レスポンスJSON中の先頭の "pp": 数値 を取得（最初に出てくるのが合計PP）
                var m = Regex.Match(json, @"""pp""\s*:\s*([\d.]+)");
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
        // エンドポイント: /api/leaderboard/by-hash/{hash}/info
        public async Task<double> GetLeaderboardStarsAsync(string hash, int difficulty, string gameMode)
        {
            try
            {
                string url = $"https://scoresaber.com/api/leaderboard/by-hash/{hash}/info"
                           + $"?difficulty={difficulty}&gameMode={gameMode}";
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
                // 404などAPIエラーは通常のケースなのでInfoレベルで記録
                Plugin.Log.Info($"[ScoreSaberApi] Leaderboard API: {ex.Message}");
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[ScoreSaberApi] GetLeaderboardStars failed: {ex.Message}");
            }
            return 0;
        }

        // ─── Top100スコア取得 ──────────────────────────────────────────────────────

        // プレイヤーのTop100スコアを (PP値, 譜面ハッシュ) のリストとして取得する
        // PP降順でソート済みのリストを返す
        // エンドポイント: /api/player/{id}/scores?sort=top&limit=100
        public async Task<List<(double pp, string hash)>> GetTop100ScoresAsync(string playerId)
        {
            var scores = new List<(double pp, string hash)>();
            try
            {
                string url = $"https://scoresaber.com/api/player/{playerId}/scores?sort=top&limit=100";
                Plugin.Log.Info($"[ScoreSaberApi] GET {url}");
                string json = await Http.GetStringAsync(url);

                // "pp": X, "weight" というパターンでスコアのPPだけを抽出する
                // （プレイヤー情報のppと区別するため "weight" の前にあるものを使う）
                var ppMatches = Regex.Matches(json,
                    @"""pp""\s*:\s*([\d.]+)\s*,\s*""weight""");
                var hashMatches = Regex.Matches(json,
                    @"""songHash""\s*:\s*""([A-Fa-f0-9]+)""",
                    RegexOptions.IgnoreCase);

                // ppとhashの数が一致する範囲でペアにする
                int count = Math.Min(ppMatches.Count, hashMatches.Count);
                for (int i = 0; i < count; i++)
                {
                    if (double.TryParse(ppMatches[i].Groups[1].Value,
                            NumberStyles.Float, CultureInfo.InvariantCulture, out double pp))
                        scores.Add((pp, hashMatches[i].Groups[1].Value.ToUpperInvariant()));
                }

                // PP降順でソート
                scores.Sort((a, b) => b.pp.CompareTo(a.pp));
                Plugin.Log.Info($"[ScoreSaberApi] Parsed {scores.Count} top scores.");
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[ScoreSaberApi] GetTop100Scores failed: {ex.Message}");
            }
            return scores;
        }

        // ─── Threshold（ランクアップ必要PP）の計算 ────────────────────────────────

        // 「このPP以上を出せばトータルPPがプラスになる」最低ラインを二分探索で求める
        // topScores: 今の曲の既存スコアを除いたTop100（PP降順）
        public static double CalculateThreshold(List<double> topScores)
        {
            // Top100が埋まっていなければ、どんな小さいPPでもプラスになる
            if (topScores.Count < 100)
                return 0.01;

            // 二分探索で「deltaが0を超えるギリギリの値」を64回反復して絞り込む
            double low = 0, high = 3000;
            for (int i = 0; i < 64; i++)
            {
                double mid = (low + high) / 2.0;
                if (CalculateDeltaPP(topScores, mid) > 0)
                    high = mid; // midで増加するなら上限を下げる
                else
                    low  = mid; // midで増加しないなら下限を上げる
            }
            return high;
        }

        // newPPをTop100に追加したときのトータルPP変化量を計算する
        // ScoreSaberの重み付け: rank位のスコアには 0.965^rank を掛けて合算する
        private static double CalculateDeltaPP(List<double> sortedDesc, double newPP)
        {
            // newPPが入る位置を探す（降順リストへの挿入位置）
            int pos = 0;
            while (pos < sortedDesc.Count && sortedDesc[pos] > newPP)
                pos++;

            // newPPを挿入した場合と挿入しない場合のTop100加重合計をそれぞれ計算する
            // リストを実際に生成せず、インデックス計算で直接求める（メモリ節約）
            double newTotal = 0;
            double oldTotal = 0;
            int inserted = 0; // newPPをまだ挿入していない=0、挿入済み=1

            for (int rank = 0; rank < 100; rank++)
            {
                double w = Math.Pow(0.965, rank); // このランクの重み

                double newVal;
                int oldIdx = rank - inserted;
                if (inserted == 0 && rank == pos)
                {
                    // このランクにnewPPを挿入する
                    newVal = newPP;
                    inserted = 1;
                }
                else
                {
                    // 挿入後のリスト上のインデックスに対応するスコアを取得
                    int idx = rank - inserted;
                    newVal = idx < sortedDesc.Count ? sortedDesc[idx] : 0;
                }
                newTotal += newVal * w;

                // 挿入前のリストの同ランクのスコア
                if (oldIdx < sortedDesc.Count)
                    oldTotal += sortedDesc[oldIdx] * w;
            }

            return newTotal - oldTotal; // プラスなら今のスコアより順位が上がる
        }
    }
}
