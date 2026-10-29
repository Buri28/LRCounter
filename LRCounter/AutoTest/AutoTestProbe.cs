using System.Collections;
using System.IO;
using LRCounter.Configuration;
using LRCounter.Controllers.Gameplay;
using LRCounter.Models;
using UnityEngine;
using Zenject;

namespace LRCounter.AutoTest
{
    /// <summary>
    /// 曲が始まったら、LRCounter とは別にノーツの判定を数え、決めた秒数後に LRCounter の集計と比べて
    /// status.txt に書き出す。画面も1枚撮ってからゲームを終了する（--lrcounter-autotest のときだけ）。
    /// </summary>
    internal class AutoTestProbe : IInitializable, System.IDisposable
    {
        private static bool _ran; // 1回の起動で1回だけ

        private readonly ScoreController _scoreController;
        private readonly LRTrackerService _tracker;
        private readonly PluginConfig _config;

        // 自前の数え（[0]=左 ColorA, [1]=右 ColorB）
        private readonly int[] _total = new int[2];
        private readonly int[] _good = new int[2];
        private readonly int[] _bad = new int[2];
        private readonly int[] _miss = new int[2];
        private readonly int[] _chainLinkMissOrBad = new int[2];
        private int _notesBeforeStar = -1; // 仮の Star 評価が届いた時点で処理済みだったノーツ数

        public AutoTestProbe(ScoreController scoreController, LRTrackerService tracker, PluginConfig config)
        {
            _scoreController = scoreController;
            _tracker = tracker;
            _config = config;
        }

        public void Initialize()
        {
            if (!AutoTestOptions.Enabled || _ran) return;
            _ran = true;
            AutoTestOptions.Status($"GAMECORE loaded config: AccBarMin={_config.AccBarMin} DropSoundMissEnabled={_config.DropSoundMissEnabled}");
            _scoreController.scoringForNoteFinishedEvent += OnNote;
            AutoTestRunner.Instance.StartCoroutine(Run());
        }

        public void Dispose()
        {
            _scoreController.scoringForNoteFinishedEvent -= OnNote;
        }

        // 新規作成（File.Move）と上書き（File.Replace）の両方を通し、中身と .bak を確かめる
        private static bool CheckAtomicWrite(out string detail)
        {
            string path = Path.Combine(AutoTestOptions.OutputDir, "atomic-write-test.txt");
            try
            {
                foreach (var p in new[] { path, path + ".bak", path + ".tmp" })
                    if (File.Exists(p)) File.Delete(p);
                Controllers.HandAccuracyStore.WriteAtomically(path, "first");
                Controllers.HandAccuracyStore.WriteAtomically(path, "second");
                string now = File.ReadAllText(path);
                string bak = File.Exists(path + ".bak") ? File.ReadAllText(path + ".bak") : "(none)";
                bool tmpLeft = File.Exists(path + ".tmp");
                detail = $"content={now} bak={bak} tmpLeft={tmpLeft}";
                return now == "second" && bak == "first" && !tmpLeft;
            }
            catch (System.Exception ex)
            {
                detail = $"{ex.GetType().Name}: {ex.Message}".Replace('\r', ' ').Replace('\n', ' ');
                return false;
            }
        }

        // F8 と同じ。ゲーム本体の InputActions.ToggleAutopilot（private static）を呼ぶ（1.40.8 / 1.44.1 にある）
        private static void ToggleAutopilot()
        {
            try
            {
                var m = AutoTestLevelStarter.FindType("InputActions")?.GetMethod("ToggleAutopilot",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (m == null) { AutoTestOptions.Status("WARN autopilot: InputActions.ToggleAutopilot not found"); return; }
                m.Invoke(null, null);
                AutoTestOptions.Status("AUTOPILOT on");
            }
            catch (System.Exception ex) { AutoTestOptions.Status("WARN autopilot: " + ex.GetType().Name + " " + ex.Message); }
        }

        // LRTrackerService と同じ条件で、種類ごとに数える（爆弾と点数の無いノーツは数えない）
        private void OnNote(ScoringElement e)
        {
            var nd = e.noteData;
            if (nd.gameplayType == NoteData.GameplayType.Bomb) return;
            if (e.maxPossibleCutScore <= 0) return;
            int h = nd.colorType == ColorType.ColorA ? 0 : nd.colorType == ColorType.ColorB ? 1 : -1;
            if (h < 0) return;
            _total[h]++;
            if (e is GoodCutScoringElement) _good[h]++;
            else if (e is BadCutScoringElement) _bad[h]++;
            else _miss[h]++;
            if (!(e is GoodCutScoringElement) && nd.gameplayType == NoteData.GameplayType.BurstSliderElement)
                _chainLinkMissOrBad[h]++;
            if (_notesBeforeStar < 0 && _tracker.StarRating > 0) _notesBeforeStar = _total[0] + _total[1] - 1;
        }

        private IEnumerator Run()
        {
            float waited = 0f;
            if (AutoTestOptions.AutoplayAt > 0 && AutoTestOptions.AutoplayAt < AutoTestOptions.Duration)
            {
                yield return new WaitForSeconds(AutoTestOptions.AutoplayAt);
                waited = AutoTestOptions.AutoplayAt;
                ToggleAutopilot();
            }
            yield return new WaitForSeconds(AutoTestOptions.Duration - waited);

            bool hud = Object.FindObjectOfType<Canvas>() != null && AccuracyBars.Current != null;
            AutoTestOptions.Status($"HUD accuracyBars={AccuracyBars.Current != null} canvasFound={hud}");
            AutoTestOptions.Status($"STAR star={_tracker.StarRating} notesBeforeStar={_notesBeforeStar} " +
                $"totalPP={_tracker.TotalPP:F2} leftPP={_tracker.LeftTracker.PP:F2} rightPP={_tracker.RightTracker.PP:F2}");

            bool allOk = true;
            for (int h = 0; h < 2; h++)
            {
                HandPPTracker t = h == 0 ? _tracker.LeftTracker : _tracker.RightTracker;
                string name = h == 0 ? "L" : "R";
                int sounds = DropSoundPlayer.MissPlayCount[h];
                int mult = AccuracyBars.Current == null ? -1
                    : (h == 0 ? AccuracyBars.Current.LeftScoreThresholdMult : AccuracyBars.Current.RightScoreThresholdMult);
                AutoTestOptions.Status($"{name} probe: total={_total[h]} good={_good[h]} bad={_bad[h]} miss={_miss[h]} chainLinkMissOrBad={_chainLinkMissOrBad[h]}");
                AutoTestOptions.Status($"{name} tracker: total={t.TotalNotes} cut={t.CutNotes} bad={t.BadCuts} miss={t.MissedNotes} missSoundCount={t.MissSoundCount} missSounds={sounds} thresholdMult=x{mult}");

                // 1. 集計が消えていない（Star 評価が届いた後もノーツ数が合う）
                bool countsOk = t.TotalNotes == _total[h] && t.CutNotes == _good[h] && t.BadCuts == _bad[h] && t.MissedNotes == _miss[h];
                // 2. ミス音用の数 ＝ Miss＋バッドカット − チェーンのリンク
                bool soundCountOk = t.MissSoundCount == _miss[h] + _bad[h] - _chainLinkMissOrBad[h];
                // 3. ミス音の回数はミス音用の数を超えない（同じフレームのミスは1回にまとまるので少ないことはある）
                bool soundsOk = sounds <= t.MissSoundCount && sounds > 0;
                AutoTestOptions.Status($"CHECK {name} counts={(countsOk ? "OK" : "NG")} missSoundCount={(soundCountOk ? "OK" : "NG")} missSounds={(soundsOk ? "OK" : "NG")}");
                allOk &= countsOk && soundCountOk && soundsOk;
            }
            AutoTestOptions.Status($"CHECK star applied={(_tracker.StarRating > 0 ? "OK" : "NG")} config AccBarMin={_config.AccBarMin}");

            // 4. 片手ベストの保存と同じ書き込み（一時ファイル → File.Replace）がゲームの実行環境で動く
            //    （本物の保存は曲を最後まで遊んだときにしか走らないので、テスト用のファイルで試す）
            bool writeOk = CheckAtomicWrite(out string writeDetail);
            AutoTestOptions.Status($"CHECK atomicWrite={(writeOk ? "OK" : "NG")} {writeDetail}");
            allOk &= writeOk;
            AutoTestOptions.Status("RESULT " + (allOk ? "OK" : "NG"));

            var path = Path.Combine(AutoTestOptions.OutputDir, "shot.png");
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(path);
            AutoTestOptions.Status($"SHOT {path} ({Screen.width}x{Screen.height})");
            yield return new WaitForSeconds(2f);

            AutoTestOptions.Status("DONE");
            if (AutoTestOptions.Quit) Application.Quit();
        }
    }
}
