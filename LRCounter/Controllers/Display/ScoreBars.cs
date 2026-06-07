using LRCounter.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LRCounter.Controllers.Display
{
    // 画面中央の左右に、グッドカットの平均生スコア(110〜115)を縦塗りバーで表示する。
    internal class ScoreBars : IDisplayComponent
    {
        private readonly PluginConfig _config;
        private readonly LRTrackerService _tracker;
        private readonly Color _leftColor;
        private readonly Color _rightColor;

        private Image? _leftCutFill;
        private Image? _rightCutFill;
        private TMP_Text? _leftCutLabel;
        private TMP_Text? _rightCutLabel;

        // 表示する点数の範囲。下端=Min、上端=Max にマッピングする
        private const double ScoreDisplayMin = 110.0;
        private const double ScoreDisplayMax = 115.0;

        private const float CutLabelGap = 0.0f;        // ラベルとバー上端の隙間
        private const float CutLabelHeight = 0.05f;    // ラベルの高さ
        private const float CutLabelHalfWidth = 0.06f; // ラベルの半幅

        public ScoreBars(PluginConfig config, LRTrackerService tracker, Color leftColor, Color rightColor)
        {
            _config = config;
            _tracker = tracker;
            _leftColor = leftColor;
            _rightColor = rightColor;
        }

        public void Build(RectTransform canvasRT, int layer)
        {
            // 左右バーの中心Xは画面中央(0.5)を起点に間隔ぶん左右へ広げる。幅は設定から取得。
            float barL = 0.5f - _config.ScoreBarSpacing * 0.5f;
            float barR = 0.5f + _config.ScoreBarSpacing * 0.5f;
            float halfW = _config.ScoreBarWidth * 0.5f;
            _leftCutFill = CreateCutBar(canvasRT, layer, "L", barL - halfW, barL + halfW);
            _rightCutFill = CreateCutBar(canvasRT, layer, "R", barR - halfW, barR + halfW);

            _leftCutLabel = CreateCutLabel(canvasRT, layer, "L", barL - CutLabelHalfWidth, barL + CutLabelHalfWidth, _leftColor);
            _rightCutLabel = CreateCutLabel(canvasRT, layer, "R", barR - CutLabelHalfWidth, barR + CutLabelHalfWidth, _rightColor);
        }

        public void Update()
        {
            UpdateCutBar(_leftCutFill, _leftCutLabel, _tracker.LeftTracker.AverageCutScore, _tracker.LeftTracker.FullCutNotes);
            UpdateCutBar(_rightCutFill, _rightCutLabel, _tracker.RightTracker.AverageCutScore, _tracker.RightTracker.FullCutNotes);
        }

        public void ApplyVisibility()
        {
            bool visible = _config.ShowScoreBar;
            LRDisplayCommon.SetActive(_leftCutFill?.transform.parent, visible);
            LRDisplayCommon.SetActive(_rightCutFill?.transform.parent, visible);
            LRDisplayCommon.SetActive(_leftCutLabel?.transform, visible);
            LRDisplayCommon.SetActive(_rightCutLabel?.transform, visible);
        }

        // フラッシュ処理は無いので空実装
        public void TickFlash() { }

        // ─── 構築 ──────────────────────────────────────────────────────────────────

        // 中央の平均点数バー（背景＋下から伸びる塗りつぶし）を1本生成し、塗りつぶしImageを返す。
        private Image CreateCutBar(RectTransform canvasRT, int layer, string side, float xMin, float xMax)
        {
            // --- 背景（暗い半透明の黒） ---
            var bgGO = new GameObject($"LRCutBar_{side}_BG");
            bgGO.layer = layer;
            var bgRT = bgGO.AddComponent<RectTransform>();
            bgRT.SetParent(canvasRT, false);
            bgRT.anchorMin = new Vector2(xMin, _config.ScoreBarY);
            bgRT.anchorMax = new Vector2(xMax, _config.ScoreBarY + _config.ScoreBarHeight);
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;
            var bgImg = bgGO.AddComponent<Image>();
            bgImg.sprite = LRDisplayCommon.CreateWhiteSprite();
            bgImg.color = new Color(0f, 0f, 0f, 0.5f);
            LRDisplayCommon.ApplyNoGlow(bgImg);

            // --- 塗りつぶし（平均点数に応じて下から上へ）。BGの子にして範囲内に収める ---
            var fillGO = new GameObject($"LRCutBar_{side}_Fill");
            fillGO.layer = layer;
            var fillRT = fillGO.AddComponent<RectTransform>();
            fillRT.SetParent(bgRT, false);
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;
            var fill = fillGO.AddComponent<Image>();
            fill.sprite = LRDisplayCommon.CreateWhiteSprite();
            fill.color = Color.white; // 実際の色は Update で平均点数に応じた帯色に
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Vertical;
            fill.fillOrigin = 0;
            fill.fillAmount = 0f;
            LRDisplayCommon.ApplyNoGlow(fill);

            // --- 目盛り線（1点刻み）。塗りつぶしの上に重ねるため fill より後に追加 ---
            int scoreDivisions = (int)(ScoreDisplayMax - ScoreDisplayMin); // 5分割 → 内側に4本
            for (int i = 1; i < scoreDivisions; i++)
            {
                float frac = i / (float)scoreDivisions;
                LRDisplayCommon.CreateGridLine(bgRT, layer, side, i, frac, LRDisplayCommon.GridLineHalfHeight, LRDisplayCommon.GridLineColor);
            }

            return fill;
        }

        // 中央バーの数字ラベル（平均点数）を生成する
        private TMP_Text CreateCutLabel(RectTransform canvasRT, int layer, string side, float xMin, float xMax, Color color)
        {
            var go = new GameObject($"LRCutLabel_{side}");
            go.layer = layer;
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(canvasRT, false);
            float scoreTop = _config.ScoreBarY + _config.ScoreBarHeight;
            rt.anchorMin = new Vector2(xMin, scoreTop + CutLabelGap);
            rt.anchorMax = new Vector2(xMax, scoreTop + CutLabelGap + CutLabelHeight);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = "";
            tmp.color = color;
            tmp.fontSize = Mathf.Max(_config.TextSize, 2f);
            tmp.alignment = TextAlignmentOptions.Center;
#pragma warning disable CS0618
            tmp.enableWordWrapping = false;
#pragma warning restore CS0618
            tmp.overflowMode = TextOverflowModes.Overflow;
            return tmp;
        }

        // ─── 更新 ──────────────────────────────────────────────────────────────────

        // 塗り量・色・数字を平均点数から更新する。グッドカットがまだ無いときは空表示。
        private void UpdateCutBar(Image? fill, TMP_Text? label, double averageCutScore, int cutNotes)
        {
            if (fill == null) return;

            if (cutNotes <= 0)
            {
                fill.fillAmount = 0f;
                if (label != null) label.text = "";
                return;
            }

            fill.fillAmount = ScoreToFill(averageCutScore);
            Color barColor = LRDisplayCommon.ScoreBarColor(averageCutScore);
            fill.color = barColor;

            if (label != null)
            {
                label.text = averageCutScore.ToString("F1");
                label.color = LRDisplayCommon.BrighterLabelColor(barColor);
            }
        }

        // 平均点数を表示レンジで正規化して塗りつぶし量(0〜1)に変換する
        private static float ScoreToFill(double score)
        {
            return Mathf.Clamp01((float)((score - ScoreDisplayMin) / (ScoreDisplayMax - ScoreDisplayMin)));
        }
    }
}
