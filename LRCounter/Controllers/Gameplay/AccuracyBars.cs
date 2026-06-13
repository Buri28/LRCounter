using BeatSaberMarkupLanguage;
using LRCounter.Configuration;
using LRCounter.Models;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LRCounter.Controllers.Gameplay
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
        private Image[]? _leftBorder;   // 閾値の枠（左/右/上/下の4帯）
        private TMP_Text? _leftLabel;
        private TMP_Text? _leftPPLabel;
        private Image? _leftTargetLine;
        private double _prevLeftAcc;

        // 右バー
        private Image? _rightFill;
        private Image? _rightFlashOverlay;
        private Image[]? _rightBorder;  // 閾値の枠（左/右/上/下の4帯）
        private TMP_Text? _rightLabel;
        private TMP_Text? _rightPPLabel;
        private Image? _rightTargetLine;
        private double _prevRightAcc;

        // フラッシュ終了時刻（Time.time基準、-1は非アクティブ）
        private float _leftFlashEnd = -1f;
        private float _rightFlashEnd = -1f;
        private const float FlashDuration = 0.4f;

        // ─── 閾値の枠（バー外周を縁取る。全面塗りだと塗り色とブレンドして判別しにくいため枠方式） ───
        // 左右独立に点灯する。優先度: 白(PP取得＝合算ThresholdPP超え・両手同時) ＞ 黄(両手の自己ベスト精度
        // 更新・両手同時) ＞ 橙(その手の自己ベスト精度更新・その手だけ)。
        // バーの外側に出すので塗り・赤フラッシュ（バー内側）とは重ならず両立する。
        // 枠の色は設定値（BorderColorPP / BorderColorScoreUpdate / BorderColorHandBest）から都度読む。
        private const float BorderThickness = 0.4f;                                 // 枠の太さ（Canvas論理単位）

        // 精度バーが表示する精度の範囲(%)。下端=low、上端=low+幅 にマッピングする。
        private const double AccDisplayMax = 100.0;

        // ─── 動的レンジ（隣接する10%窓を上下にスライド・左右共通） ───────────────────────
        // 左右のバーは同じ窓[low, low+幅]を共有する。切り替え条件は次のとおり（チラつき防止）。
        // ・下へ: 両手とも下端を割り、後から割った方も含め両手がそれぞれ NotesStable ノーツ維持したら1段下へ。
        // ・上へ: 両手とも上端を超え、後から超えた方も含め両手がそれぞれ NotesStable ノーツ維持したら1段上へ。
        // ・下限は0%まで下がる。動的レンジOFF(_config.AccBarDynamic=false)時は [AccBarMin,100] 固定。
        private const double WindowHeight = 10.0;            // 1窓あたりの精度幅(%)
        private const double MaxWindowLow = AccDisplayMax - WindowHeight; // 最上段の窓の下端(=90)
        private const int NotesStable = 5;                   // 切り替えに必要な「窓外維持」ノーツ数（手ごとに数える）
        private const int InitialAdjustNotes = 5;            // 開始直後に1回だけ窓を初期調整する判定ノーツ数（左右合計）
        private double _windowLow;                           // 左右共通の窓の下端
        private bool _initialWindowAdjusted;                 // 開始直後の初期調整を済ませたか（1回だけ）
        // 窓外（下端割れ/上端超え）を各手のノーツで連続カウント。窓内に戻る or 窓が動いたら0に戻す。
        private int _leftBelowCount;
        private int _rightBelowCount;
        private int _leftAboveCount;
        private int _rightAboveCount;
        // 窓の更新は「その手で新しくノーツを処理したとき」だけ行う（手ごとにノーツを数えるため）
        private int _leftPrevNotes;
        private int _rightPrevNotes;

        // 窓に表示する精度幅。動的=10%固定 / 静的=AccBarMin〜100。
        private double WindowSpan => _config.AccBarDynamic ? WindowHeight : (AccDisplayMax - _config.AccBarMin);

        // 目盛り：10%刻みで10分割（線は9本）
        private const int GridDivisions = 10;
        // 窓スライド時に色を更新するため目盛り線を保持（index0=frac0.1 … index8=frac0.9）
        private readonly Image?[] _leftGridLines = new Image?[GridDivisions - 1];
        private readonly Image?[] _rightGridLines = new Image?[GridDivisions - 1];
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

            // 初期の窓: 動的なら最上段[90,100]、静的なら[AccBarMin,100]。
            _windowLow = config.AccBarDynamic ? MaxWindowLow : config.AccBarMin;
        }

        public void Build(RectTransform canvasRT, int layer)
        {
            (_leftFill, _leftFlashOverlay, _leftLabel, _leftPPLabel) = CreateSideBar(canvasRT, layer, isLeft: true);
            (_rightFill, _rightFlashOverlay, _rightLabel, _rightPPLabel) = CreateSideBar(canvasRT, layer, isLeft: false);

            // 目標ライン（必要精度）を各バー（bg）上に作る。位置は Update で毎回更新。
            _leftTargetLine = CreateTargetLine((RectTransform)_leftFill!.transform.parent, layer);
            _rightTargetLine = CreateTargetLine((RectTransform)_rightFill!.transform.parent, layer);

            // 閾値の枠（バー外周）。塗り・フラッシュより後に作り、バーの外側に配置する。
            _leftBorder = CreateBorder(canvasRT, layer, isLeft: true);
            _rightBorder = CreateBorder(canvasRT, layer, isLeft: false);
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

            // 動的レンジ: その手で新しくノーツを処理したときだけ窓判定する（手ごとに5ノーツ数えるため）。
            // 動いたら目盛り色を更新する。
            if (_config.AccBarDynamic)
            {
                int leftNotes = _tracker.LeftTracker.TotalNotes;
                int rightNotes = _tracker.RightTracker.TotalNotes;
                bool leftNew = leftNotes != _leftPrevNotes;
                bool rightNew = rightNotes != _rightPrevNotes;
                _leftPrevNotes = leftNotes;
                _rightPrevNotes = rightNotes;

                // 初回のみ：左右合わせて最初の InitialAdjustNotes ノーツ（ミス含む）を処理した時点で、
                // 左右の高い方の精度が90%未満なら、その精度を含む10%帯から開始する（以降は通常スライドに任せる）。
                if (!_initialWindowAdjusted && (leftNew || rightNew)
                    && leftNotes + rightNotes >= InitialAdjustNotes)
                {
                    _initialWindowAdjusted = true;
                    double higher = leftAcc > rightAcc ? leftAcc : rightAcc;
                    if (higher < MaxWindowLow) // 90%未満のときだけ下げる（90%以上は最上段[90,100]のまま）
                    {
                        // 高い方の精度を含む10%帯の下端に合わせる（例: 62% → 60）。下限0。
                        double low = System.Math.Floor(higher / WindowHeight) * WindowHeight;
                        _windowLow = low < 0 ? 0 : low;
                        ResetOutsideCounts();
                        RefreshGridColors(_leftGridLines, _windowLow);
                        RefreshGridColors(_rightGridLines, _windowLow);
                    }
                }

                // その手にノーツが来たときだけ「窓外維持」カウントを更新する
                if (leftNew) UpdateOutsideCount(leftAcc, ref _leftBelowCount, ref _leftAboveCount);
                if (rightNew) UpdateOutsideCount(rightAcc, ref _rightBelowCount, ref _rightAboveCount);

                // 両手が同方向で揃ったら共通の窓を1段スライド。動いたら全目盛り色を更新。
                if ((leftNew || rightNew) && TryShiftWindow())
                {
                    RefreshGridColors(_leftGridLines, _windowLow);
                    RefreshGridColors(_rightGridLines, _windowLow);
                }
            }

            // 塗りつぶし量と帯色（左右とも共通の窓を使う）
            _leftFill!.fillAmount = AccToFill(leftAcc, _windowLow);
            _rightFill!.fillAmount = AccToFill(rightAcc, _windowLow);
            Color leftBarColor = LRDisplayCommon.AccuracyBarColor(leftAcc);
            Color rightBarColor = LRDisplayCommon.AccuracyBarColor(rightAcc);
            _leftFill!.color = leftBarColor;
            _rightFill!.color = rightBarColor;

            // %ラベル（フォント色はバー色を少し明るく）
            _leftLabel!.text = $"{leftAcc:F1}%";
            _rightLabel!.text = $"{rightAcc:F1}%";
            _leftLabel!.color = leftBarColor;
            _rightLabel!.color = rightBarColor;

            // PPラベル（色は精度と分離：通常黄／threshold超過で緑）。アンランクなら非表示。
            bool hasStar = _tracker.StarRating > 0;
            Color ppColor = LRDisplayCommon.PPColor(_tracker);
            _leftPPLabel!.text = hasStar ? $"{_tracker.LeftTracker.PP:F1}PP" : "";
            _rightPPLabel!.text = hasStar ? $"{_tracker.RightTracker.PP:F1}PP" : "";
            _leftPPLabel!.color = ppColor;
            _rightPPLabel!.color = ppColor;

            UpdateTargetLine();

            // 閾値の枠：左右それぞれ独立に判定する。白=PP取得(合算・両手同時)を最優先、
            // 次に黄=その手が自分の自己ベスト精度を更新（片手ごと・点灯はその手だけ）。
            SetBorder(_leftBorder, BorderColorForHand(_tracker.LeftTracker.Accuracy, _tracker.LeftBestAccuracy));
            SetBorder(_rightBorder, BorderColorForHand(_tracker.RightTracker.Accuracy, _tracker.RightBestAccuracy));
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
            SetBorderActive(_leftBorder, visible);
            SetBorderActive(_rightBorder, visible);
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
            double gridLow = _windowLow;
            var gridLines = isLeft ? _leftGridLines : _rightGridLines;
            for (int i = 1; i < GridDivisions; i++)
            {
                float frac = i / (float)GridDivisions;
                int pct = Mathf.RoundToInt((float)(gridLow + frac * WindowSpan));
                gridLines[i - 1] = LRDisplayCommon.CreateGridLine(bgRT, layer, side, i, frac, LRDisplayCommon.GridLineHalfHeight, GridLineColorFor(pct));
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

        // ─── 閾値の枠（バー外周の4帯）を生成する ─────────────────────────────────────
        // バーの外側に左/右/上/下の細い帯を置いて枠にする。初期は透明（消灯）。
        private Image[] CreateBorder(RectTransform canvasRT, int layer, bool isLeft)
        {
            string side = isLeft ? "L" : "R";
            float centerX = isLeft
                ? 0.5f - _config.AccBarSpacing * 0.5f
                : 0.5f + _config.AccBarSpacing * 0.5f;
            float halfW = _config.AccBarWidth * 0.5f;
            float xMin = centerX - halfW;
            float xMax = centerX + halfW;
            float yMin = _config.AccBarY;
            float yMax = _config.AccBarY + _config.AccBarHeight;
            const float t = BorderThickness;

            // anchorは「バー矩形の縁」に貼り、offsetでバーの外側へ t ぶん張り出させる（offsetはCanvas論理単位）。
            // 角を埋めるため、上下帯は左右へも t ぶん広げる。
            return new[]
            {
                // 左帯（バー左辺の外側、上下の角を含む）
                CreateBorderStrip(canvasRT, layer, $"{side}_L",
                    new Vector2(xMin, yMin), new Vector2(xMin, yMax), new Vector2(-t, -t), new Vector2(0f, t)),
                // 右帯
                CreateBorderStrip(canvasRT, layer, $"{side}_R",
                    new Vector2(xMax, yMin), new Vector2(xMax, yMax), new Vector2(0f, -t), new Vector2(t, t)),
                // 上帯
                CreateBorderStrip(canvasRT, layer, $"{side}_T",
                    new Vector2(xMin, yMax), new Vector2(xMax, yMax), new Vector2(-t, 0f), new Vector2(t, t)),
                // 下帯
                CreateBorderStrip(canvasRT, layer, $"{side}_B",
                    new Vector2(xMin, yMin), new Vector2(xMax, yMin), new Vector2(-t, -t), new Vector2(t, 0f)),
            };
        }

        private static Image CreateBorderStrip(RectTransform canvasRT, int layer, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject($"LRBorder_{name}");
            go.layer = layer;
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(canvasRT, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            var img = go.AddComponent<Image>();
            img.sprite = LRDisplayCommon.CreateWhiteSprite();
            img.color = Color.clear; // 初期は消灯
            LRDisplayCommon.ApplyNoGlow(img);
            return img;
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
            // 目標ラインも共通の窓に合わせて配置する
            float frac = AccToFill(reqAccPct, _windowLow);
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

        // 精度(%)を窓[low, low+幅]で正規化して塗りつぶし量(0〜1)に変換する
        private float AccToFill(double acc, double low)
        {
            return Mathf.Clamp01((float)((acc - low) / WindowSpan));
        }

        // その手の精度が窓外（下端割れ/上端超え）にある連続ノーツ数を数える。
        // 下端割れなら below を、上端超えなら above を加算し、もう一方と窓内復帰は0に戻す。
        private void UpdateOutsideCount(double acc, ref int below, ref int above)
        {
            double high = _windowLow + WindowHeight;
            if (acc < _windowLow) { below++; above = 0; }
            else if (acc >= high) { above++; below = 0; }
            else { below = 0; above = 0; }
        }

        // 左右共通の窓を1段スライドできるか判定して、動かしたら true。
        // 両手が同方向で揃い、後から窓外になった方（=カウントが小さい方）も NotesStable に達した時点で切り替わる。
        private bool TryShiftWindow()
        {
            // 下へ: 両手とも下端割れを規定ノーツ維持。下限は0。
            if (_windowLow > 0 && _leftBelowCount >= NotesStable && _rightBelowCount >= NotesStable)
            {
                _windowLow -= WindowHeight;
                ResetOutsideCounts();
                return true;
            }
            // 上へ: 両手とも上端超えを規定ノーツ維持。上限は最上段[90,100]。
            if (_windowLow < MaxWindowLow && _leftAboveCount >= NotesStable && _rightAboveCount >= NotesStable)
            {
                _windowLow += WindowHeight;
                ResetOutsideCounts();
                return true;
            }
            return false;
        }

        // 窓が動いたらレンジが変わるので全カウントをリセットして数え直す
        private void ResetOutsideCounts()
        {
            _leftBelowCount = _rightBelowCount = 0;
            _leftAboveCount = _rightAboveCount = 0;
        }

        // 窓スライド後に目盛り線の色を新しいレンジで塗り直す
        private void RefreshGridColors(Image?[] lines, double low)
        {
            for (int i = 1; i < GridDivisions; i++)
            {
                float frac = i / (float)GridDivisions;
                int pct = Mathf.RoundToInt((float)(low + frac * WindowHeight));
                var line = lines[i - 1];
                if (line != null) line.color = GridLineColorFor(pct);
            }
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

        // 片手ぶんの枠の色を決める。点灯なしは透明（Color.clear）。
        // 優先度: 白(PP取得) ＞ 黄(両手スコア更新) ＞ 橙(片手ベスト更新)。
        //   handAccuracy     : その手の現在精度(0〜1)
        //   handBestAccuracy : その手の前回までの自己ベスト精度(0〜1)。記録なしは0。
        private Color BorderColorForHand(double handAccuracy, double handBestAccuracy)
        {
            // PP取得（白・最優先）：白いライン(ThresholdPP)を合算TotalPPで超えたか。PPは合算の概念なので両手同時。
            // ランク譜面のみ（アンランクはPP概念が無い）。Threshold未取得(0)のうちは判定しない。
            if (_tracker.StarRating > 0 && _tracker.ThresholdPP > 0 && _tracker.TotalPP >= _tracker.ThresholdPP)
                return LRDisplayCommon.ParseHex(_config.BorderColorPP);

            // 両手スコア更新（黄）：合算（両手）の自己ベスト精度を更新 → 左右とも点灯。片手更新より優先。
            // 精度同士の比較なのでStar評価(API)に依存せず、API失敗時・アンランク譜面でも点灯する。記録なし(0)は判定しない。
            if (_tracker.SelfBestAccuracy > 0 && _tracker.TotalAccuracy >= _tracker.SelfBestAccuracy)
                return LRDisplayCommon.ParseHex(_config.BorderColorScoreUpdate);

            // 片手ベスト更新（橙）：その手の自己ベスト精度を更新 → その手だけ点灯。記録なし(0)は判定しない。
            if (handBestAccuracy > 0 && handAccuracy >= handBestAccuracy)
                return LRDisplayCommon.ParseHex(_config.BorderColorHandBest);

            return Color.clear;
        }

        // 枠（4帯）の色をまとめて設定する（透明＝消灯）。フェードは無く、状態が変わるまで点灯し続ける。
        private static void SetBorder(Image[]? strips, Color color)
        {
            if (strips == null) return;
            foreach (var s in strips)
                if (s != null) s.color = color;
        }

        // 枠（4帯）の表示ON/OFFをまとめて切り替える。
        private static void SetBorderActive(Image[]? strips, bool visible)
        {
            if (strips == null) return;
            foreach (var s in strips)
                LRDisplayCommon.SetActive(s?.transform, visible);
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
