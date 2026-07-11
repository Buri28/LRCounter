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
        private TMP_Text? _leftBestLabel;   // バー下: この手のベスト精度（半透明）
        private Image? _leftPPLine;        // PP取得ライン（必要精度・両手共通）
        private Image? _leftSelfBestLine;  // 両手自己ベストの精度
        private Image? _leftHandBestLine;  // この手の自己ベストの精度
        private double _prevLeftAcc;

        // 右バー
        private Image? _rightFill;
        private Image? _rightFlashOverlay;
        private Image[]? _rightBorder;  // 閾値の枠（左/右/上/下の4帯）
        private TMP_Text? _rightLabel;
        private TMP_Text? _rightPPLabel;
        private TMP_Text? _rightBestLabel;   // バー下: この手のベスト精度（半透明）
        private Image? _rightPPLine;        // PP取得ライン（必要精度・両手共通）
        private Image? _rightSelfBestLine;  // 両手自己ベストの精度
        private Image? _rightHandBestLine;  // この手の自己ベストの精度
        private double _prevRightAcc;

        // フラッシュ終了時刻（Time.time基準、-1は非アクティブ）
        private float _leftFlashEnd = -1f;
        private float _rightFlashEnd = -1f;
        private float FlashDuration => _config.FlashDuration;

        // 精度低下時のサウンド（フラッシュと同じタイミングで鳴らす）
        private readonly DropSoundPlayer _dropSound;
        // 低スコアカット検知用: 前回見た CutScoreSerial（新しいカットが来たかの判定）
        private int _leftPrevCutSerial;
        private int _rightPrevCutSerial;

        // ─── 閾値の枠（バー外周を縁取る。全面塗りだと塗り色とブレンドして判別しにくいため枠方式） ───
        // 左右独立に点灯する。優先度: 白(PP取得＝合算ThresholdPP超え・両手同時) ＞ 黄(両手の自己ベスト精度
        // 更新・両手同時) ＞ 橙(その手の自己ベスト精度更新・その手だけ)。
        // バーの外側に出すので塗り・赤フラッシュ（バー内側）とは重ならず両立する。
        // 枠の色は設定値（BorderColorPP / BorderColorScoreUpdate / BorderColorHandBest）から都度読む。
        private const float BorderThickness = 0.4f;                                 // 枠の太さ（Canvas論理単位）

        // 精度バーが表示する精度の範囲(%)。下端=low、上端=low+幅 にマッピングする。
        private const double AccDisplayMax = 100.0;

        // ─── 動的レンジ（窓を上下にスライド・左右共通） ───────────────────────
        // 左右のバーは同じ窓[low, low+幅]を共有する。切り替え条件は次のとおり（チラつき防止）。
        // ・下へ: 両手とも下端を割り、後から割った方も含め両手がそれぞれ NotesStable ノーツ維持したら1段下へ。
        // ・上へ: 両手とも上端を超え、後から超えた方も含め両手がそれぞれ NotesStable ノーツ維持したら1段上へ。
        // ・下限は0%まで下がる。動的レンジOFF(_config.AccBarDynamic=false)時は [AccBarMin,100] 固定。
        // 窓の幅(=スライドの段差)はバー下限 AccBarMin で決まる: 90→10%, 80→20%, 50→50%, 0→100%。
        //   例) 80% は [80,100]→[60,80]→[40,60]→[20,40]→[0,20] と 20% 刻みでスライド。
        //       50% は [50,100]→[0,50] と 50% 刻みでスライド。
        private double WindowHeight => AccDisplayMax - _config.AccBarMin;  // 1窓あたりの精度幅(%)
        private double MaxWindowLow => _config.AccBarMin;                  // 最上段の窓の下端(=AccBarMin)
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

        // 窓に表示する精度幅。動的・静的とも AccBarMin〜100（動的はこの幅の窓がスライドする）。
        private double WindowSpan => AccDisplayMax - _config.AccBarMin;

        // 目盛り：10%刻みで10分割（線は9本）
        private const int GridDivisions = 10;
        // 窓スライド時に色を更新するため目盛り線を保持（index0=frac0.1 … index8=frac0.9）
        private readonly Image?[] _leftGridLines = new Image?[GridDivisions - 1];
        private readonly Image?[] _rightGridLines = new Image?[GridDivisions - 1];
        private static readonly Color GridLineColorBold = new Color(0f, 0f, 0f, 1f); // 95%強調線（不透明黒）

        // 基準ライン（PP取得・両手自己ベスト・片手ベスト）の太さ
        private const float ReferenceLineHalfHeight = 0.18f;

        private const float BarLabelHeight = 0.05f; // %ラベルの高さ（バー上端からこのぶん上）
        private const float HandBestLabelAlpha = 0.7f; // バー下の片手ベスト精度ラベルの不透明度（控えめに見せる）

        public AccuracyBars(PluginConfig config, LRTrackerService tracker, Color leftColor, Color rightColor)
        {
            _config = config;
            _tracker = tracker;
            _leftColor = leftColor;
            _rightColor = rightColor;
            _dropSound = new DropSoundPlayer(config);

            // 初期の窓は最上段[AccBarMin,100]。動的ならここから下へスライドしうる。
            _windowLow = config.AccBarMin;
        }

        public void Build(RectTransform canvasRT, int layer)
        {
            (_leftFill, _leftFlashOverlay, _leftLabel, _leftPPLabel, _leftBestLabel)
                = CreateSideBar(canvasRT, layer, isLeft: true);
            (_rightFill, _rightFlashOverlay, _rightLabel, _rightPPLabel, _rightBestLabel)
                = CreateSideBar(canvasRT, layer, isLeft: false);

            // 基準ライン（PP取得・両手自己ベスト・片手ベスト）を各バー（bg）上に作る。位置・色は Update で毎回更新。
            var leftBarRT = (RectTransform)_leftFill!.transform.parent;
            var rightBarRT = (RectTransform)_rightFill!.transform.parent;
            _leftPPLine = CreateReferenceLine(leftBarRT, layer, "PP");
            _leftSelfBestLine = CreateReferenceLine(leftBarRT, layer, "SelfBest");
            _leftHandBestLine = CreateReferenceLine(leftBarRT, layer, "HandBest");
            _rightPPLine = CreateReferenceLine(rightBarRT, layer, "PP");
            _rightSelfBestLine = CreateReferenceLine(rightBarRT, layer, "SelfBest");
            _rightHandBestLine = CreateReferenceLine(rightBarRT, layer, "HandBest");

            // 閾値の枠（バー外周）。塗り・フラッシュより後に作り、バーの外側に配置する。
            _leftBorder = CreateBorder(canvasRT, layer, isLeft: true);
            _rightBorder = CreateBorder(canvasRT, layer, isLeft: false);

            // 精度低下サウンド。Canvas 配下に作るので曲終了時に一緒に破棄される。
            _dropSound.Build(canvasRT);
        }

        public void Update()
        {
            if (_leftFill == null || _rightFill == null) return;

            double leftAcc = _tracker.LeftTracker.Accuracy * 100;
            double rightAcc = _tracker.RightTracker.Accuracy * 100;

            // 前回より精度が下がっていたらフラッシュ開始。サウンドは低下量(%)が閾値以上のときだけ、
            // 左右それぞれ別の音で鳴らす（閾値0なら少しでも下がれば鳴る）
            bool leftDropped = _prevLeftAcc > 0 && leftAcc < _prevLeftAcc;
            bool rightDropped = _prevRightAcc > 0 && rightAcc < _prevRightAcc;
            if (leftDropped) _leftFlashEnd = Time.time + FlashDuration;
            if (rightDropped) _rightFlashEnd = Time.time + FlashDuration;
            // 加えて、カットスコアが設定値を下回ったときも鳴らす（例:110設定→109点以下で鳴る）。
            // 精度低下・低スコアはそれぞれ独立に有効/無効を切り替えられる。両方満たしても1回だけ鳴る。
            bool accSound = _config.DropSoundAccuracyEnabled;
            double soundThreshold = _config.DropSoundThreshold;
            bool leftLowScore = IsNewLowScoreCut(_tracker.LeftTracker, ref _leftPrevCutSerial);
            bool rightLowScore = IsNewLowScoreCut(_tracker.RightTracker, ref _rightPrevCutSerial);
            if ((accSound && leftDropped && _prevLeftAcc - leftAcc >= soundThreshold) || leftLowScore)
                _dropSound.Play(isLeft: true);
            if ((accSound && rightDropped && _prevRightAcc - rightAcc >= soundThreshold) || rightLowScore)
                _dropSound.Play(isLeft: false);
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
                    if (higher < MaxWindowLow) // 最上段の窓未満のときだけ下げる（それ以上は最上段[AccBarMin,100]のまま）
                    {
                        // 高い方の精度を含む窓の下端に合わせる（例: 幅10%なら 62%→60）。下限0。
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
            _leftPPLabel!.text = hasStar ? $"{_tracker.LeftDisplayPP:F1}PP" : "";
            _rightPPLabel!.text = hasStar ? $"{_tracker.RightDisplayPP:F1}PP" : "";
            _leftPPLabel!.color = ppColor;
            _rightPPLabel!.color = ppColor;

            // バー下のベスト精度ラベル（この手の自己ベスト精度）。記録なしは空。半透明で控えめに表示する。
            Color handBestColor = LRDisplayCommon.ParseHex(_config.BorderColorHandBest);
            handBestColor.a = HandBestLabelAlpha;
            UpdateBestLabel(_leftBestLabel, _tracker.LeftBestAccuracy, handBestColor);
            UpdateBestLabel(_rightBestLabel, _tracker.RightBestAccuracy, handBestColor);

            UpdateReferenceLines();

            // 閾値の枠：左右それぞれ独立に判定する。白=PP取得(合算・両手同時)を最優先、
            // 次に黄=その手が自分の自己ベスト精度を更新（片手ごと・点灯はその手だけ）。
            SetBorder(_leftBorder, BorderColorForHand(_tracker.LeftTracker.Accuracy, _tracker.LeftBestAccuracy));
            SetBorder(_rightBorder, BorderColorForHand(_tracker.RightTracker.Accuracy, _tracker.RightBestAccuracy));
        }

        public void TickFlash()
        {
            float t = Time.time;
            SetFlashAlpha(_leftFlashOverlay, _leftFlashEnd, t, FlashDuration);
            SetFlashAlpha(_rightFlashOverlay, _rightFlashEnd, t, FlashDuration);
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
            // バー上の精度(%)・PPラベルはそれぞれ独立にON/OFF（バー表示ONが前提）
            bool accLabelVisible = visible && _config.ShowBarAccuracyLabel;
            bool ppLabelVisible = visible && _config.ShowBarPPLabel;
            LRDisplayCommon.SetActive(_leftLabel?.transform, accLabelVisible);
            LRDisplayCommon.SetActive(_rightLabel?.transform, accLabelVisible);
            LRDisplayCommon.SetActive(_leftPPLabel?.transform, ppLabelVisible);
            LRDisplayCommon.SetActive(_rightPPLabel?.transform, ppLabelVisible);

            // バー下のベスト精度ラベルは、バー表示ONかつベストラベルONのときだけ表示する
            bool bestVisible = visible && _config.ShowHandBestLabel;
            LRDisplayCommon.SetActive(_leftBestLabel?.transform, bestVisible);
            LRDisplayCommon.SetActive(_rightBestLabel?.transform, bestVisible);
        }

        // ─── 構築 ──────────────────────────────────────────────────────────────────

        // 戻り値: (塗りつぶし画像, フラッシュオーバーレイ, %ラベル, PPラベル, ベスト精度ラベル)
        private (Image fill, Image flashOverlay, TMP_Text label, TMP_Text ppLabel,
            TMP_Text bestLabel) CreateSideBar(
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

            // --- ベスト精度ラベル（バー下端のすぐ下・半透明） ---
            var bestLabel = CreateBelowBarLabel(canvasRT, layer, $"LRBest_{side}",
                barXMin, barXMax, barBottom - BarLabelHeight, barBottom, color);

            return (fill, flashImg, label, ppLabel, bestLabel);
        }

        // バー下に置く1行ラベルを生成する（ベスト精度・ベストPP用）。色・文字は Update で更新。
        private TMP_Text CreateBelowBarLabel(RectTransform canvasRT, int layer, string name,
            float xMin, float xMax, float yMin, float yMax, Color color)
        {
            var go = new GameObject(name);
            go.layer = layer;
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(canvasRT, false);
            rt.anchorMin = new Vector2(xMin, yMin);
            rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
#pragma warning disable CS0618
            var label = BeatSaberUI.CreateText(rt, "", Vector2.zero);
#pragma warning restore CS0618
            label.color = color;
            label.fontSize = Mathf.Max(_config.HandBestLabelSize, 1f); // バー下ラベルは独立サイズ（既定はバー上より小さい）
            label.alignment = TextAlignmentOptions.Center;
#pragma warning disable CS0618
            label.enableWordWrapping = false;
#pragma warning restore CS0618
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
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

        // 基準ライン(横線)を1本生成する。位置・色は Update で更新。初期は非表示。
        private Image CreateReferenceLine(RectTransform barRT, int layer, string name)
        {
            var go = new GameObject($"LRRefLine_{name}");
            go.layer = layer;
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(barRT, false);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.offsetMin = new Vector2(0f, -ReferenceLineHalfHeight);
            rt.offsetMax = new Vector2(0f, ReferenceLineHalfHeight);
            var img = go.AddComponent<Image>();
            img.sprite = LRDisplayCommon.CreateWhiteSprite();
            img.color = Color.clear;
            LRDisplayCommon.ApplyNoGlow(img);
            go.SetActive(false);
            return img;
        }

        // ─── 更新ヘルパー ──────────────────────────────────────────────────────────

        // 3本の基準ラインの位置・色・表示を更新する。
        //   PP取得ライン   : ThresholdPP を逆算した必要精度（両手共通）。BorderColorPP。
        //   両手自己ベスト : 合算の自己ベスト精度（両手共通）。BorderColorScoreUpdate。
        //   片手ベスト     : その手の自己ベスト精度（左右で別の値）。BorderColorHandBest。
        private void UpdateReferenceLines()
        {
            // PP取得ライン（必要精度）。ランク譜面かつ Threshold 確定時のみ。
            double threshold = _tracker.ThresholdPP;
            double star = _tracker.StarRating;
            bool showPP = star > 0 && threshold > 0;
            double ppAccPct = showPP ? PPCalculator.AccuracyForPP(threshold, star) * 100.0 : 0;
            Color ppColor = LRDisplayCommon.ParseHex(_config.BorderColorPP);
            UpdateReferenceLine(_leftPPLine, showPP, ppAccPct, ppColor);
            UpdateReferenceLine(_rightPPLine, showPP, ppAccPct, ppColor);

            // 両手自己ベスト（合算精度。左右とも同じ値）。記録なし(0)は非表示。
            bool showSelf = _tracker.SelfBestAccuracy > 0;
            double selfAccPct = _tracker.SelfBestAccuracy * 100.0;
            Color selfColor = LRDisplayCommon.ParseHex(_config.BorderColorScoreUpdate);
            UpdateReferenceLine(_leftSelfBestLine, showSelf, selfAccPct, selfColor);
            UpdateReferenceLine(_rightSelfBestLine, showSelf, selfAccPct, selfColor);

            // 片手ベスト（その手の自己ベスト精度。左右で別の値）。記録なし(0)は非表示。
            Color handColor = LRDisplayCommon.ParseHex(_config.BorderColorHandBest);
            UpdateReferenceLine(_leftHandBestLine, _tracker.LeftBestAccuracy > 0,
                _tracker.LeftBestAccuracy * 100.0, handColor);
            UpdateReferenceLine(_rightHandBestLine, _tracker.RightBestAccuracy > 0,
                _tracker.RightBestAccuracy * 100.0, handColor);
        }

        // 基準ライン1本の表示ON/OFF・色・位置（共通の窓に合わせる）を設定する。
        private void UpdateReferenceLine(Image? line, bool show, double accPct, Color color)
        {
            if (line == null) return;
            line.gameObject.SetActive(show);
            if (!show) return;
            line.color = color;
            float frac = AccToFill(accPct, _windowLow);
            var rt = (RectTransform)line.transform;
            rt.anchorMin = new Vector2(0f, frac);
            rt.anchorMax = new Vector2(1f, frac);
        }

        // バー下のベスト精度ラベルの文字色・内容を更新する。
        //   bestAcc : この手のベスト精度(0〜1)。0(記録なし)は空表示。color は半透明済みを渡す。
        private static void UpdateBestLabel(TMP_Text? accLabel, double bestAcc, Color color)
        {
            if (accLabel == null) return;
            accLabel.color = color;
            accLabel.text = bestAcc > 0 ? $"PB:{bestAcc * 100.0:F1}%" : "";
        }

        // その手で新しいカット（115満点ノーツのみ）があり、そのスコアが設定閾値未満なら true。
        // チェーンノーツはスコアの意味が異なるため対象外（CutScoreSerial が増えない）。
        // シリアルの追従は無効時も行い、有効化直後に過去のカットで鳴らないようにする。
        private bool IsNewLowScoreCut(HandPPTracker tracker, ref int prevSerial)
        {
            int serial = tracker.CutScoreSerial;
            bool isNew = serial != prevSerial;
            prevSerial = serial;
            return _config.DropSoundScoreEnabled && isNew
                && tracker.LastCutScore < _config.DropSoundScoreThreshold;
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
            // 上へ: 両手とも上端超えを規定ノーツ維持。上限は最上段[AccBarMin,100]。
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

        // 目盛り線の色を精度(%)ごとに決める。95%=黒で強調 / それ以外=通常
        private static Color GridLineColorFor(int pct)
        {
            switch (pct)
            {
                case 95: return GridLineColorBold;
                default: return LRDisplayCommon.GridLineColor;
            }
        }

        // 片手ぶんの枠の色を決める。点灯なしは透明（Color.clear）。
        // 優先度: 白(PP取得) ＞ 黄(両手スコア更新) ＞ 橙(片手ベスト更新)。
        //   handAccuracy     : その手の現在精度(0〜1)
        //   handBestAccuracy : その手の前回までの自己ベスト精度(0〜1)。記録なしは0。
        private Color BorderColorForHand(double handAccuracy, double handBestAccuracy)
        {
            // NF失敗プレイは提出スコアが半減し、白(PP取得)・黄(両手ベスト)・橙(片手ベスト)いずれも
            // 実際にはベスト更新／PP獲得にならない。どのラインを超えても枠は点灯させない。
            if (_tracker.Failed) return Color.clear;

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
        private static void SetFlashAlpha(Image? overlay, float endTime, float now, float duration)
        {
            if (overlay == null) return;
            const float baseAlpha = 0.6f;
            if (endTime < 0 || now >= endTime)
            {
                overlay.color = new Color(1f, 0f, 0f, 0f);
                return;
            }
            float alpha = Mathf.Clamp01((endTime - now) / duration) * baseAlpha;
            overlay.color = new Color(1f, 0f, 0f, alpha);
        }
    }
}
