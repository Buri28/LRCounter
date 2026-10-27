using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LRCounter.AutoTest
{
    /// <summary>
    /// 自動テストの設定。起動引数で指定する（設定ファイルは使わないので普段のプレイには影響しない）。
    ///   --lrcounter-autotest            自動テストを有効にする（これが無いと何もしない）
    ///   --lrcounter-song=NAME           曲名（songName）が一致するカスタム曲を選ぶ（省略時は最初のカスタム曲）
    ///   --lrcounter-mapper=NAME         譜面作者名でも絞る（同じ曲名の譜面が複数あるとき）
    ///   --lrcounter-diff=Expert         難易度
    ///   --lrcounter-duration=130        曲が始まってから結果を書き出すまでの秒数（その後ゲームを終了する）
    ///   --lrcounter-fake-star=5         Star 評価を ScoreSaber から取らず、この値にする
    ///   --lrcounter-star-delay=10       仮の Star 評価を返すまでの秒数（取得が遅いときの再現）
    ///   --lrcounter-autoplay-at=20      曲が始まってからこの秒数でオートパイロット（F8 と同じ）に切り替える（0=しない）
    ///   --lrcounter-noquit              終わってもゲームを終了しない
    /// 結果は UserData/LRCounter/AutoTest/ に status.txt（最後の行が DONE / FAIL）と shot.png。
    /// 何も操作しないのでノーツは全部 Miss になる。曲の終わりまでは進めず途中で終了するので、
    /// スコアの提出・片手ベストの保存は起きない。
    /// </summary>
    internal static class AutoTestOptions
    {
        public static readonly bool Enabled;
        public static readonly string? SongName;
        public static readonly string? Mapper;
        public static readonly string Difficulty = "Expert";
        public static readonly float Duration = 130f;
        public static readonly double FakeStar;
        public static readonly float StarDelay;
        public static readonly float AutoplayAt;
        public static readonly bool Quit = true;

        static AutoTestOptions()
        {
            string[] args;
            try { args = Environment.GetCommandLineArgs(); }
            catch { return; }

            foreach (var a in args)
            {
                if (a.Equals("--lrcounter-autotest", StringComparison.OrdinalIgnoreCase)) Enabled = true;
                else if (a.Equals("--lrcounter-noquit", StringComparison.OrdinalIgnoreCase)) Quit = false;
                else if (TryValue(a, "--lrcounter-song=", out var v)) SongName = v;
                else if (TryValue(a, "--lrcounter-mapper=", out v)) Mapper = v;
                else if (TryValue(a, "--lrcounter-diff=", out v)) Difficulty = v;
                else if (TryValue(a, "--lrcounter-duration=", out v) && TryFloat(v, out var f)) Duration = f;
                else if (TryValue(a, "--lrcounter-fake-star=", out v) && TryFloat(v, out f)) FakeStar = f;
                else if (TryValue(a, "--lrcounter-star-delay=", out v) && TryFloat(v, out f)) StarDelay = f;
                else if (TryValue(a, "--lrcounter-autoplay-at=", out v) && TryFloat(v, out f)) AutoplayAt = f;
            }
        }

        public static string OutputDir => Path.Combine(IPA.Utilities.UnityGame.UserDataPath, "LRCounter", "AutoTest");

        // status.txt に1行追記し、ログにも出す。外部スクリプトはこのファイルで進み具合と終了を知る。
        public static void Status(string line)
        {
            Plugin.Log.Info($"[LRCounter][autotest] {line}");
            try
            {
                Directory.CreateDirectory(OutputDir);
                File.AppendAllText(Path.Combine(OutputDir, "status.txt"),
                    $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}");
            }
            catch (Exception ex) { Plugin.Log.Warn($"[LRCounter][autotest] status write failed: {ex.Message}"); }
        }

        private static bool TryValue(string arg, string prefix, out string value)
        {
            value = "";
            if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            value = arg.Substring(prefix.Length).Trim('"');
            return value.Length > 0;
        }

        private static bool TryFloat(string s, out float f)
            => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f);

        public static string Describe()
            => $"song={SongName ?? "(first custom)"} mapper={Mapper ?? "(any)"} diff={Difficulty} duration={Duration} fakeStar={FakeStar} starDelay={StarDelay} autoplayAt={AutoplayAt} quit={Quit} " +
               $"args=[{string.Join(" ", Environment.GetCommandLineArgs().Skip(1))}]";
    }

    /// <summary>自動テストのコルーチンを動かすための常駐オブジェクト（シーンをまたいで残る）。</summary>
    internal class AutoTestRunner : MonoBehaviour
    {
        private static AutoTestRunner? _instance;

        public static AutoTestRunner Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("LRCounterAutoTestRunner");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<AutoTestRunner>();
                }
                return _instance;
            }
        }
    }
}
