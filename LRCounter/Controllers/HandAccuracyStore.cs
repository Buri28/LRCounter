using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Zenject;

namespace LRCounter.Controllers
{
    // 譜面ごとの左右の自己ベスト精度(%)＋付随情報（左右PP・曲名・譜面作者）をディスクへ永続化するストア（App スコープ）。
    // リザルト画面で「前回ベストとの差分(+/-%)」を出し、上回ったら記録を更新するために使う。
    // System.Text.Json / Newtonsoft に依存しないよう、1行をTAB区切りのプレーンテキストで保存する：
    //   key <TAB> leftAcc <TAB> rightAcc <TAB> leftPP <TAB> rightPP <TAB> songName <TAB> author
    public class HandAccuracyStore : IInitializable
    {
        // 保存ファイル。設定(LRCounter.json)と同じ UserData フォルダに置く。
        private static readonly string FilePath =
            Path.Combine(Environment.CurrentDirectory, "UserData", "LRCounter_HandBests.txt");

        // 1譜面ぶんの記録。精度(%)とそのときのPP、曲名・作者。
        public struct Record
        {
            public double LeftAcc;   // 左手ベスト精度(%)
            public double RightAcc;  // 右手ベスト精度(%)
            public double LeftPP;    // 左手ベスト精度時のPP（アンランクは0）
            public double RightPP;   // 右手ベスト精度時のPP（アンランクは0）
            public string SongName;  // 曲名
            public string Author;    // 譜面作者（マッパー）
        }

        // key = "levelId|difficulty|gameMode" → 記録
        private readonly Dictionary<string, Record> _records = new Dictionary<string, Record>();

        // 起動時に1回だけ呼ばれてファイルを読み込む
        public void Initialize() => Load();

        // 譜面キーに対応する左右ベスト精度(%)を返す。記録が無ければ false（left/right は0）。
        public bool TryGet(string key, out double left, out double right)
        {
            if (_records.TryGetValue(key, out var v)) { left = v.LeftAcc; right = v.RightAcc; return true; }
            left = right = 0;
            return false;
        }

        // 左右それぞれ、既存ベスト精度を上回った分だけ更新してファイルへ保存する。
        // 片手だけ更新でももう片方は維持する（手ごとの自己ベスト）。更新した手のPPも一緒に差し替える。
        // 曲名・作者は取得できていれば毎回最新で上書きする。変化が無ければ保存しない。
        public void UpdateIfBetter(string key, double leftAcc, double rightAcc, double leftPP, double rightPP,
            string songName, string author)
        {
            bool had = _records.TryGetValue(key, out var cur); // 無ければ cur は全0/null

            double newLeftAcc = cur.LeftAcc, newLeftPP = cur.LeftPP;
            if (leftAcc > cur.LeftAcc) { newLeftAcc = leftAcc; newLeftPP = leftPP; }

            double newRightAcc = cur.RightAcc, newRightPP = cur.RightPP;
            if (rightAcc > cur.RightAcc) { newRightAcc = rightAcc; newRightPP = rightPP; }

            // 曲名・作者は取れていれば最新で上書き、取れなければ既存値を維持
            string newName = !string.IsNullOrEmpty(songName) ? songName : (cur.SongName ?? "");
            string newAuthor = !string.IsNullOrEmpty(author) ? author : (cur.Author ?? "");

            var updated = new Record
            {
                LeftAcc = newLeftAcc,
                RightAcc = newRightAcc,
                LeftPP = newLeftPP,
                RightPP = newRightPP,
                SongName = newName,
                Author = newAuthor,
            };

            if (had && Same(updated, cur)) return; // 変化なし

            _records[key] = updated;
            Save();
        }

        private static bool Same(Record a, Record b)
            => a.LeftAcc == b.LeftAcc && a.RightAcc == b.RightAcc
            && a.LeftPP == b.LeftPP && a.RightPP == b.RightPP
            && string.Equals(a.SongName ?? "", b.SongName ?? "", StringComparison.Ordinal)
            && string.Equals(a.Author ?? "", b.Author ?? "", StringComparison.Ordinal);

        // ファイルから全レコードを読み込む。壊れた行・項目不足の行はスキップし、失敗しても例外は投げない。
        private void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split('\t');
                    if (parts.Length < 7) continue;

                    if (!TryParse(parts[1], out double leftAcc) || !TryParse(parts[2], out double rightAcc))
                        continue;

                    var rec = new Record { LeftAcc = leftAcc, RightAcc = rightAcc, SongName = parts[5], Author = parts[6] };
                    TryParse(parts[3], out rec.LeftPP);
                    TryParse(parts[4], out rec.RightPP);
                    _records[parts[0]] = rec;
                }
                Plugin.Log.Info($"[LRCounter] HandAccuracyStore loaded: {_records.Count} records.");
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[LRCounter] HandAccuracyStore load failed: {ex.Message}");
            }
        }

        // 全レコードをファイルへ書き出す（件数が少ないので毎回まるごと上書きで十分）。
        private void Save()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var kv in _records)
                {
                    var r = kv.Value;
                    sb.Append(kv.Key).Append('\t')
                      .Append(Num(r.LeftAcc)).Append('\t').Append(Num(r.RightAcc)).Append('\t')
                      .Append(Num(r.LeftPP)).Append('\t').Append(Num(r.RightPP)).Append('\t')
                      .Append(San(r.SongName)).Append('\t').Append(San(r.Author)).Append('\n');
                }

                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[LRCounter] HandAccuracyStore save failed: {ex.Message}");
            }
        }

        private static bool TryParse(string s, out double value)
            => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        private static string Num(double d) => d.ToString("R", CultureInfo.InvariantCulture);

        // 区切り(TAB)・改行を含む曲名/作者でも1行1レコードが崩れないよう、空白へ置換する。
        private static string San(string? s)
            => string.IsNullOrEmpty(s) ? "" : s!.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
