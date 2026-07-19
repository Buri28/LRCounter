using LRCounter.Models;
using TMPro;
using UnityEngine;

namespace LRCounter.Controllers.Gameplay
{
    // 精度/PP/Star/カット内訳/集計検算/閾値を1か所にまとめて表示する開発用ラベル。
    internal class DebugLabel : IDisplayComponent
    {
        private readonly LRTrackerService _tracker;
        private readonly AccuracyBars _accuracyBars; // 低スコア音の閾値倍率（左右）の参照元

        private TMP_Text? _label;

        // Canvasよりプレイヤー側へ寄せるローカルZオフセット（Canvasスケール0.06 → -8で約0.5m手前）
        private const float LocalZOffset = -8f;

        public DebugLabel(LRTrackerService tracker, AccuracyBars accuracyBars)
        {
            _tracker = tracker;
            _accuracyBars = accuracyBars;
        }

        public void Build(RectTransform canvasRT, int layer)
        {
            var go = new GameObject("LRDebug");
            go.layer = layer;
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(canvasRT, false);
            // バーより下に配置（0=Canvas下端 / 1=上端）
            rt.anchorMin = new Vector2(0.00f, 0.15f);
            rt.anchorMax = new Vector2(1.00f, 0.32f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            // ラベルだけCanvas面より手前（プレイヤー側）に出す
            rt.anchoredPosition3D = new Vector3(0f, 0f, LocalZOffset);

            // BeatSaberUI.CreateText は位置が制御できないため TextMeshProUGUI を直接使用
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = "";
            tmp.color = Color.yellow;
            tmp.fontSize = 3f;
            tmp.alignment = TextAlignmentOptions.Center;
#pragma warning disable CS0618
            tmp.enableWordWrapping = false;
#pragma warning restore CS0618
            tmp.overflowMode = TextOverflowModes.Overflow;
            _label = tmp;
        }

        public void Update()
        {
            if (_label == null) return;

            var L = _tracker.LeftTracker;
            var R = _tracker.RightTracker;
            int mult = _tracker.CurrentMultiplier;

            // LastCutScore が -1 のときはまだそちらの手でカットしていない
            string lScore = L.LastCutScore >= 0 ? $"{L.LastCutScore}" : "---";
            string rScore = R.LastCutScore >= 0 ? $"{R.LastCutScore}" : "---";

            // 自前集計スコア（NF係数込み）とゲームの実表示スコアを突き合わせ、ズレたら赤字にする。
            int calcScore = _tracker.TotalScore;
            int gameScore = _tracker.GameModifiedScore;
            bool scoreMatches = calcScore == gameScore;

            double totalAcc = _tracker.TotalAccuracy * 100.0;
            double totalPP = _tracker.TotalPP;
            double star = _tracker.StarRating;
            // 閾値（トータルを底上げできる最低PP）と、それを必要精度に逆算した%
            double thrPP = _tracker.ThresholdPP;
            double thrAcc = PPCalculator.AccuracyForPP(thrPP, star) * 100.0;

            _label.text =
                $"{totalAcc:F2}%  {totalPP:F1}pp  ★{star:F2}  L:{lScore}  R:{rScore}  x{mult}\n" +
                $"{calcScore}：({gameScore}) /{_tracker.TotalMaxScore}\n" +
                $"Thr:{thrPP:F2}pp ({thrAcc:F2}%)  " +
                $"Snd L:x{_accuracyBars.LeftScoreThresholdMult} R:x{_accuracyBars.RightScoreThresholdMult}";
            _label.color = scoreMatches ? Color.yellow : Color.red;
        }

        // 表示ON/OFFのトグルは無い（常に表示）ので空実装
        public void ApplyVisibility() { }

        // フラッシュ処理は無いので空実装
        public void TickFlash() { }
    }
}
