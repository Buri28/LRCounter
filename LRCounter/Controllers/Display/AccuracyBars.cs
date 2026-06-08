using BeatSaberMarkupLanguage;
using LRCounter.Configuration;
using LRCounter.Models;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LRCounter.Controllers.Display
{
    // 左右の精度バー（背景・塗り・目盛り・フラッシュ・%/PPラベル・目標ライン）をまとめて扱う。
    internal class AccuracyBars : IDisplayComponent
    {
        private readonly PluginConfig _config;
        private readonly LRTrackerService _tracker;
        private readonly Color _leftColor;
        private readonly Color _rightColor;

        // 左バー
        private Image? _leftFill;
        private Image? _leftFlashOverlay;
        private TMP_Text? _leftLabel;
        private TMP_Text? _leftPPLabel;
        private Image? _leftTargetLine;
        private double _prevLeftAcc;

        // 右バー
        private Image? _rightFill;
        private Image? _rightFlashOverlay;
        private TMP_Text? _rightLabel;
        private TMP_Text? _rightPPLabel;
        private Image? _rightTargetLine;
        private double _prevRightAcc;

        // フラッシュ終了時刻（Time.time基準、-1は非アクティブ）
        private float _leftFlashEnd = -1f;
        private float _rightFlashEnd = -1f;
        private const float FlashDuration = 0.4f;

        // 精度バーが表示する精度の範囲(%)。下端=Min、上端=Max にマッピングする。
        // 下端は設定(AccBarMin)で 90/80/50/0 から選べる。
        private double AccDisplayMin => _config.AccBarMin;
        private const double AccDisplayMax = 100.0;

        // 目盛り：10%刻みで10分割（線は9本）
        private const int GridDivisions = 10;
        private static readonly Color GridLineColorBold = new Color(0f, 0f, 0f, 1f); // 95%強調線（不透明黒）

        // 目標ライン（必要精度）
        private const float TargetLineHalfHeight = 0.18f;
        private static readonly Color TargetLineColor = new Color(1f, 1f, 1f, 0.95f);

        private const float BarLabelHeight = 0.05f; // %ラベルの高さ（バー上端からこのぶん上）

        public AccuracyBars(PluginConfig config, LRTrackerService tracker, Color leftColor, Color rightColor)
        {
            _config = config;
            _tracker = tracker;
            _leftColor = leftColor;
            _rightColor = rightColor;
        }

        public void Build(RectTransform canvasRT, int layer)
        {
            (_leftFill, _leftFlashOverlay, _leftLabel, _leftPPLabel) = CreateSideBar(canvasRT, layer, isLeft: true);
            (_rightFill, _rightFlashOverlay, _rightLabel, _rightPPLabel) = CreateSideBar(canvasRT, layer, isLeft: false);

            // 目標ライン（必要精度）を各バー（bg）上に作る。位置は Update で毎回更新。
            _leftTargetLine = CreateTargetLine((RectTransform)_leftFill!.transform.parent, layer);
            _rightTargetLine = CreateTargetLine((RectTransform)_rightFill!.transform.parent, layer);
        }

        public void Update()
        {
            if (_leftFill == null || _rightFill == null) return;

            double leftAcc = _tracker.LeftTracker.Accuracy * 100;
            double rightAcc = _tracker.RightTracker.Accuracy * 100;

            // 前回より精度が下がっていたらフラッシュ開始
            if (_prevLeftAcc > 0 && leftAcc < _prevLeftAcc)
                _leftFlashEnd = Time.time + FlashDuration;
            if (_prevRightAcc > 0 && rightAcc < _prevRightAcc)
                _rightFlashEnd = Time.time + FlashDuration;
            _prevLeftAcc = leftAcc;
            _prevRightAcc = rightAcc;

            // 塗りつぶし量と帯色
            _leftFill!.fillAmount = AccToFill(leftAcc);
            _rightFill!.fillAmount = AccToFill(rightAcc);
            Color leftBarColor = LRDisplayCommon.AccuracyBarColor(leftAcc);
            Color rightBarColor = LRDisplayCommon.AccuracyBarColor(rightAcc);
            _leftFill!.color = leftBarColor;
            _rightFill!.color = rightBarColor;

            // %ラベル（フォント色はバー色を少し明るく）
            _leftLabel!.text = $"{leftAcc:F1}%";
            _rightLabel!.text = $"{rightAcc:F1}%";
            _leftLabel!.color = LRDisplayCommon.BrighterLabelColor(leftBarColor);
            _rightLabel!.color = LRDisplayCommon.BrighterLabelColor(rightBarColor);

            // PPラベル（色は精度と分離：通常黄／threshold超過で緑）。アンランクなら非表示。
            bool hasStar = _tracker.StarRating > 0;
            Color ppColor = LRDisplayCommon.PPColor(_tracker);
            _leftPPLabel!.text = hasStar ? $"{_tracker.LeftTracker.PP:F1}PP" : "";
            _rightPPLabel!.text = hasStar ? $"{_tracker.RightTracker.PP:F1}PP" : "";
            _leftPPLabel!.color = ppColor;
            _rightPPLabel!.color = ppColor;

            UpdateTargetLine();
        }

        public void TickFlash()
        {
            float t = Time.time;
            SetFlashAlpha(_leftFlashOverlay, _leftFlashEnd, t);
            SetFlashAlpha(_rightFlashOverlay, _rightFlashEnd, t);
        }

        public void ApplyVisibility()
        {
            bool visible = _config.ShowAccBar;
            LRDisplayCommon.SetActive(_leftFill?.transform.parent, visible);   // bg（fill・目盛り線・目標ラインを含む）
            LRDisplayCommon.SetActive(_rightFill?.transform.parent, visible);
            LRDisplayCommon.SetActive(_leftFlashOverlay?.transform, visible);
            LRDisplayCommon.SetActive(_rightFlashOverlay?.transform, visible);
            LRDisplayCommon.SetActive(_leftLabel?.transform, visible);
            LRDisplayCommon.SetActive(_rightLabel?.transform, visible);
            LRDisplayCommon.SetActive(_leftPPLabel?.transform, visible);
            LRDisplayCommon.SetActive(_rightPPLabel?.transform, visible);
        }

        // ─── 構築 ──────────────────────────────────────────────────────────────────

        // 戻り値: (塗りつぶし画像, フラッシュオーバーレイ, %ラベル, PPラベル)
        private (Image fill, Image flashOverlay, TMP_Text label, TMP_Text ppLabel) CreateSideBar(
            RectTransform canvasRT, int layer, bool isLeft)
        {
            Color color = isLeft ? _leftColor : _rightColor;
            string side = isLeft ? "L" : "R";

            // バーの中心Xは画面中央(0.5)を起点に間隔ぶん左右へ広げる。幅・下端・高さは設定から取得。
            float centerX = isLeft
                ? 0.5f - _config.AccBarSpacing * 0.5f
                : 0.5f + _config.AccBarSpacing * 0.5f;
            float halfW = _config.AccBarWidth * 0.5f;
            float barXMin = centerX - halfW;
            float barXMax = centerX + halfW;
            float barBottom = _config.AccBarY;
            float barTop = _config.AccBarY + _config.AccBarHeight;

            // --- 背景（暗い半透明の黒）。見えるバーの下端・上端はここで決まる ---
            var bgGO = new GameObject($"LRBar_{side}_BG");
            bgGO.layer = layer;
            var bgRT = bgGO.AddComponent<RectTransform>();
            bgRT.SetParent(canvasRT, false);
            bgRT.anchorMin = new Vector2(barXMin, barBottom);
            bgRT.anchorMax = new Vector2(barXMax, barTop);
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;
            var bgImg = bgGO.AddComponent<Image>();
            bgImg.sprite = LRDisplayCommon.CreateWhiteSprite();
            bgImg.color = new Color(0f, 0f, 0f, 0.5f);
            LRDisplayCommon.ApplyNoGlow(bgImg);

            // --- 塗りつぶし（精度に応じて下から上へ）。BGの子にして範囲内に収める ---
            var fillGO = new GameObject($"LRBar_{side}_Fill");
            fillGO.layer = layer;
            var fillRT = fillGO.AddComponent<RectTransform>();
            fillRT.SetParent(bgRT, false);
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;
            var fill = fillGO.AddComponent<Image>();
            fill.sprite = LRDisplayCommon.CreateWhiteSprite();
            fill.color = Color.white; // 実際の色は Update で現在精度の帯色に
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Vertical;
            fill.fillOrigin = 0;       // 0=下から上へ
            fill.fillAmount = 0f;
            LRDisplayCommon.ApplyNoGlow(fill);

            // --- 目盛り線（10%刻み）。塗りつぶしの上に重ねるため fill より後に bg の子として追加 ---
            for (int i = 1; i < GridDivisions; i++)
            {
                float frac = i / (float)GridDivisions;
                int pct = Mathf.RoundToInt((float)(AccDisplayMin + frac * (AccDisplayMax - AccDisplayMin)));
                LRDisplayCommon.CreateGridLine(bgRT, layer, side, i, frac, LRDisplayCommon.GridLineHalfHeight, GridLineColorFor(pct));
            }

            // --- フラッシュオーバーレイ（精度低下時に赤く光る。初期アルファ0） ---
            var flashGO = new GameObject($"LRFlash_{side}");
            flashGO.layer = layer;
            var flashRT = flashGO.AddComponent<RectTransform>();
            flashRT.SetParent(canvasRT, false);
            flashRT.anchorMin = new Vector2(barXMin, barBottom);
            flashRT.anchorMax = new Vector2(barXMax, barTop);
            flashRT.offsetMin = Vector2.zero;
            flashRT.offsetMax = Vector2.zero;
            var flashImg = flashGO.AddComponent<Image>();
            flashImg.sprite = LRDisplayCommon.CreateWhiteSprite();
            flashImg.color = new Color(1f, 0f, 0f, 0f);
            LRDisplayCommon.ApplyNoGlow(flashImg);

            // --- %ラベル（バー上端の上） ---
            var labelGO = new GameObject($"LRLabel_{side}");
            labelGO.layer = layer;
            var labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.SetParent(canvasRT, false);
            labelRT.anchorMin = new Vector2(barXMin, barTop);
            labelRT.anchorMax = new Vector2(barXMax, barTop + BarLabelHeight);
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = Vector2.zero;
#pragma warning disable CS0618
            var label = BeatSaberUI.CreateText(labelRT, "---", Vector2.zero);
#pragma warning restore CS0618
            label.color = color;
            label.fontSize = Mathf.Max(_config.TextSize, 2f);
            label.alignment = TextAlignmentOptions.Center;
#pragma warning disable CS0618
            label.enableWordWrapping = false;
#pragma warning restore CS0618
            label.overflowMode = TextOverflowModes.Overflow;

            // --- PPラベル（%ラベルのさらに上） ---
            var ppGO = new GameObject($"LRPP_{side}");
            ppGO.layer = layer;
            var ppRT = ppGO.AddComponent<RectTransform>();
            ppRT.SetParent(canvasRT, false);
            ppRT.anchorMin = new Vector2(barXMin, barTop + BarLabelHeight);
            ppRT.anchorMax = new Vector2(barXMax, barTop + BarLabelHeight * 2f);
            ppRT.offsetMin = Vector2.zero;
            ppRT.offsetMax = Vector2.zero;
#pragma warning disable CS0618
            var ppLabel = BeatSaberUI.CreateText(ppRT, "", Vector2.zero);
#pragma warning restore CS0618
            ppLabel.color = color;
            ppLabel.fontSize = Mathf.Max(_config.TextSize, 2f);
            ppLabel.alignment = TextAlignmentOptions.Center;
#pragma warning disable CS0618
            ppLabel.enableWordWrapping = false;
#pragma warning restore CS0618
            ppLabel.overflowMode = TextOverflowModes.Overflow;

            return (fill, flashImg, label, ppLabel);
        }

        // 目標ライン(必要精度)を1本生成する。位置は Update で更新。初期は非表示。
        private Image CreateTargetLine(RectTransform barRT, int layer)
        {
            var go = new GameObject("LRTargetLine");
            go.layer = layer;
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(barRT, false);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.offsetMin = new Vector2(0f, -TargetLineHalfHeight);
            rt.offsetMax = new Vector2(0f, TargetLineHalfHeight);
            var img = go.AddComponent<Image>();
            img.sprite = LRDisplayCommon.CreateWhiteSprite();
            img.color = TargetLineColor;
            LRDisplayCommon.ApplyNoGlow(img);
            go.SetActive(false);
            return img;
        }

        // ─── 更新ヘルパー ──────────────────────────────────────────────────────────

        // ThresholdPP を必要精度に逆算して目標ラインの位置を更新する。
        private void UpdateTargetLine()
        {
            double threshold = _tracker.ThresholdPP;
            double star = _tracker.StarRating;
            bool show = star > 0 && threshold > 0;

            if (_leftTargetLine != null) _leftTargetLine.gameObject.SetActive(show);
            if (_rightTargetLine != null) _rightTargetLine.gameObject.SetActive(show);
            if (!show) return;

            double reqAccPct = PPCalculator.AccuracyForPP(threshold, star) * 100.0;
            float frac = AccToFill(reqAccPct);
            SetTargetLineFrac(_leftTargetLine, frac);
            SetTargetLineFrac(_rightTargetLine, frac);
        }

        private static void SetTargetLineFrac(Image? line, float frac)
        {
            if (line == null) return;
            var rt = (RectTransform)line.transform;
            rt.anchorMin = new Vector2(0f, frac);
            rt.anchorMax = new Vector2(1f, frac);
        }

        // 精度(%)を表示レンジで正規化して塗りつぶし量(0〜1)に変換する
        private float AccToFill(double acc)
        {
            return Mathf.Clamp01((float)((acc - AccDisplayMin) / (AccDisplayMax - AccDisplayMin)));
        }

        // 目盛り線の色を精度(%)ごとに決める。95%=黒で強調 / 98%=オレンジ / 99%=赤 / それ以外=通常
        private static Color GridLineColorFor(int pct)
        {
            switch (pct)
            {
                case 95: return GridLineColorBold;
                case 98: return LRDisplayCommon.ColOrange;
                case 99: return LRDisplayCommon.ColRed;
                default: return LRDisplayCommon.GridLineColor;
            }
        }

        // フラッシュの残り時間に応じてアルファを計算し、時間切れなら透明にする
        private static void SetFlashAlpha(Image? overlay, float endTime, float now)
        {
            if (overlay == null) return;
            const float baseAlpha = 0.6f;
            if (endTime < 0 || now >= endTime)
            {
                overlay.color = new Color(1f, 0f, 0f, 0f);
                return;
            }
            float alpha = Mathf.Clamp01((endTime - now) / FlashDuration) * baseAlpha;
            overlay.color = new Color(1f, 0f, 0f, alpha);
        }
    }
}
