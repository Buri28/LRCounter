using BeatSaberMarkupLanguage;
using HMUI;
using LRCounter.Configuration;
using LRCounter.Models;
using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace LRCounter.Controllers
{
    // ゲーム中に左右の精度バーをワールドスペースCanvasで表示するコントローラー
    public class LRDisplayController : IInitializable, IDisposable
    {
        private readonly LRTrackerService _trackerService; // スコア・精度データの提供元
        private readonly PluginConfig _config;               // 設定（表示ON/OFF、色、位置など）
        private readonly CoreGameHUDController _hudController; // 位置合わせの基準となるゲームHUD

        private GameObject? _canvasObject; // 表示用Canvasのルートオブジェクト

        // 左バー関連
        private Image? _leftFill;          // 精度に応じて縦に伸びる塗りつぶし部分
        private Image? _leftFlashOverlay;  // 精度が下がったときに赤く光るオーバーレイ
        private TMP_Text? _leftLabel;         // バー上部に表示する%テキスト
        private TMP_Text? _leftPPLabel;       // %ラベルのさらに上に表示するPP数値
        private double _prevLeftAcc = 0;   // 前フレームの精度（下落検出に使う）

        // 右バー関連
        private Image? _rightFill;
        private Image? _rightFlashOverlay;
        private TMP_Text? _rightLabel;
        private TMP_Text? _rightPPLabel;
        private double _prevRightAcc = 0;

        // 目標ライン：ThresholdPP（トータルを底上げするのに必要なPP）を必要精度に逆算して
        // 各精度バー上に横線で表示する。この線を超えれば現状のトータルPPを上回る。
        private Image? _leftTargetLine;
        private Image? _rightTargetLine;

        // フラッシュ終了時刻（Time.time基準、-1は非アクティブ）
        private float _leftFlashEnd = -1f;
        private float _rightFlashEnd = -1f;
        private const float FlashDuration = 0.4f; // フラッシュが消えるまでの秒数

        private Color _leftColor;  // 左手バーの色（設定から取得）
        private Color _rightColor; // 右手バーの色（設定から取得）

        // デバッグ用：バー下部にスコア内訳を表示するテキスト（背景なし）
        private TMP_Text? _debugLabel;

        // 合算ラベル：左右合計の精度・PPを中央上部に表示（位置・サイズは設定で可変）
        private TMP_Text? _totalLabel;

        // 平均点数表示：グッドカットの平均生スコア(0〜115)を画面中央の左右に縦バーで表示する。
        // ブロック累積方式をやめ、外側の精度バーと同じ「現在値で連続した塗りつぶし」方式にした。
        // （プレイ中は直前カットのブロックを目で追う余裕が無かったため、平均値の塗りバーに変更）
        private Image? _leftCutFill;       // 左手の平均点数に応じて下から上に伸びる塗りつぶし
        private Image? _rightCutFill;      // 右手の同上
        private TMP_Text? _leftCutLabel;   // 左手の平均点数（数字・小数1桁）
        private TMP_Text? _rightCutLabel;  // 右手の平均点数（数字・小数1桁）

        // 内側バー(平均点数)が表示する点数の範囲。下端=ScoreDisplayMin、上端=ScoreDisplayMax にマッピングする
        private const double ScoreDisplayMin = 110.0;
        private const double ScoreDisplayMax = 115.0;

        // 精度バーの目盛り：横線でバーを GridDivisions 分割する（線は GridDivisions-1 本）
        private const int GridDivisions = 10;
        private const float GridLineHalfHeight = 0.1f;      // 目盛り線の半分の高さ（Canvas論理単位、全線共通。細め）
        private const float TargetLineHalfHeight = 0.18f;   // 目標ラインの半分の高さ（目盛り線より太く目立たせる）
        private static readonly Color TargetLineColor = new Color(1f, 1f, 1f, 0.95f); // 目標ラインの色（白・太め）
        private static readonly Color GridLineColor = new Color(1f, 1f, 1f, 0.45f); // 通常の目盛り線の色（半透明白）
        private static readonly Color GridLineColorBold = new Color(0f, 0f, 0f, 1f); // 強調線の色（不透明黒）
        // 特定の精度の目盛り線を専用色で強調する: 95%=黒 / 98%=オレンジ / 99%=赤。色は GridLineColorFor() で決定。

        // 精度バーが表示する精度の範囲(%)。下端=AccDisplayMin、上端=AccDisplayMax にマッピングする
        private const double AccDisplayMin = 90.0;
        private const double AccDisplayMax = 100.0;

        // ─── バー塗りつぶしの色基準（帯方式） ───────────────────────────────────────
        // 連続グラデーションではなく「帯(バンド)」に区切る。帯の中だけ滑らかに色が変わり、
        // 帯の境目では色がガラッと飛ぶ（＝切れ目）。境目を跨いだ瞬間に色が大きく変わるので
        // 「ランクが上がった／下がった」がプレイ中でも一目で分かる。
        // バーは常に「現在値の1色」で全体を塗る（バー内での縦グラデーションはしない）。
        //
        // 精度バー(外側):  90〜94%台=赤〜黄〜黄緑 / 95〜97%台=緑〜青 / 98%台=肌色(徐々に明) /
        //                  99%台=マゼンタ(徐々に明) / 100%=グレー
        // 平均点数バー(内側): 110=赤〜橙 / 111=黄〜黄緑 / 112=緑〜青 / 113=肌色(徐々に明) /
        //                  114=マゼンタ(徐々に明) / 115=グレー

        // 帯で使う基準色（名前付き）。同系の「暗→明」は徐々に明るくする帯で使う。
        private static readonly Color ColRed = new Color(0.95f, 0.15f, 0.15f); // 赤
        private static readonly Color ColOrange = new Color(1f, 0.50f, 0.10f); // オレンジ
        private static readonly Color ColYellow = new Color(1f, 0.92f, 0.15f); // 黄
        private static readonly Color ColYellowGreen = new Color(0.62f, 0.90f, 0.20f); // 黄緑
        private static readonly Color ColGreen = new Color(0.20f, 0.90f, 0.25f); // 緑
        private static readonly Color ColBlue = new Color(0.20f, 0.50f, 1f);    // 青
        private static readonly Color ColSkinDark = new Color(0.82f, 0.54f, 0.40f); // 肌色(暗・小麦色)
        private static readonly Color ColSkinBright = new Color(1f, 0.85f, 0.72f); // 肌色(明)
        private static readonly Color ColMagentaDark = new Color(0.45f, 0.05f, 0.35f); // マゼンタ(暗)
        private static readonly Color ColMagentaBright = new Color(1f, 0.25f, 0.90f); // マゼンタ(明)
        private static readonly Color ColGray = new Color(0.90f, 0.90f, 0.90f); // 白に近いグレー(最高ランク)

        // 1つの帯。[Lo, Hi) の値範囲を Stops の色で滑らかに塗る（帯内グラデーション）。
        // 帯の Hi 側の色と次の帯の Lo 側の色が違うことで「切れ目」が生まれる。
        private readonly struct ColorBand
        {
            public readonly double Lo;
            public readonly double Hi;
            public readonly Color[] Stops;
            public ColorBand(double lo, double hi, Color[] stops) { Lo = lo; Hi = hi; Stops = stops; }
        }

        // 精度(%)の帯テーブル。最上端(100%以上)は AccTopColor(グレー)で塗る。
        private static readonly ColorBand[] AccuracyBands =
        {
            new ColorBand(90, 95, new[] { ColRed, ColYellow, ColYellowGreen }), // 90〜94%台: 赤→黄→黄緑
            new ColorBand(95, 98, new[] { ColGreen, ColBlue }),                 // 95〜97%台: 緑→青
            new ColorBand(98, 99, new[] { ColSkinDark, ColSkinBright }),        // 98%台: 肌色徐々に明
            new ColorBand(99, 100, new[] { ColMagentaDark, ColMagentaBright }), // 99%台: マゼンタ徐々に明
        };

        // 平均点数の帯テーブル。最上端(115以上)は ScoreTopColor(グレー)で塗る。
        private static readonly ColorBand[] ScoreBands =
        {
            new ColorBand(110, 111, new[] { ColRed, ColOrange }),                 // 110: 赤→橙
            new ColorBand(111, 112, new[] { ColYellow, ColYellowGreen }),         // 111: 黄→黄緑
            new ColorBand(112, 113, new[] { ColGreen, ColBlue }),                 // 112: 緑→青
            new ColorBand(113, 114, new[] { ColSkinDark, ColSkinBright }),        // 113: 肌色徐々に明
            new ColorBand(114, 115, new[] { ColMagentaDark, ColMagentaBright }),  // 114: マゼンタ徐々に明
        };

        // バー位置・サイズ・奥行きは PluginConfig（Mods設定）から取得する。ここはラベル等の派生寸法のみ。
        private const float BarLabelHeight = 0.05f;   // 精度バー%ラベルの高さ（バー上端からこのぶん上に置く）
        private const float CutLabelGap = 0.0f;       // 平均点数ラベルとバー上端の隙間
        private const float CutLabelHeight = 0.05f;   // 平均点数ラベルの高さ（バー上端のすぐ上に置く）
        private const float CutLabelHalfWidth = 0.06f; // 平均点数ラベルの半幅

        // Canvasの基準Yオフセット(ワールド単位)。ゲームHUDのCanvas位置からこのぶん上にずらす。
        private const float CanvasBaseYOffset = 1.3f;

        [Inject]
        public LRDisplayController(
            LRTrackerService trackerService,
            PluginConfig config,
            CoreGameHUDController hudController)
        {
            _trackerService = trackerService;
            _config = config;
            _hudController = hudController;
        }

        // Zenjectによって曲開始時に呼ばれる初期化処理
        public void Initialize()
        {
            if (!_config.Enabled) return;

            // 設定のカラーコード(#RRGGBB)をUnityのColorに変換。失敗時はデフォルト色
            if (!ColorUtility.TryParseHtmlString(_config.LeftHandColor, out _leftColor))
                _leftColor = new Color(1f, 0.33f, 0.33f); // デフォルト: 赤系
            if (!ColorUtility.TryParseHtmlString(_config.RightHandColor, out _rightColor))
                _rightColor = new Color(0.33f, 0.33f, 1f); // デフォルト: 青系

            CreateDisplay();
            // ノーツを切るたびにTrackerServiceからイベントが来るので表示を更新する
            _trackerService.OnPPUpdated += UpdateDisplay;
        }

        // 曲終了時にCanvasを破棄してイベントを解除する
        public void Dispose()
        {
            _trackerService.OnPPUpdated -= UpdateDisplay;
            if (_canvasObject != null)
                GameObject.Destroy(_canvasObject);
            GC.SuppressFinalize(this);
        }

        // ─── スプライト生成 ────────────────────────────────────────────────────────

        // 1x1ピクセルの白いスプライトを生成する（Imageの土台として使用）
        private static Sprite CreateWhiteSprite()
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
        }

        // 帯テーブルと値から色を求める。値が最下端未満なら先頭色、最上端(最後の帯のHi)以上なら topColor。
        // 帯内では Stops を滑らかに補間し、帯の境目では色が飛ぶ（切れ目）。
        private static Color EvalBands(ColorBand[] bands, double value, Color topColor)
        {
            if (value <= bands[0].Lo) return bands[0].Stops[0];
            foreach (var band in bands)
            {
                if (value < band.Hi)
                {
                    double local = (value - band.Lo) / (band.Hi - band.Lo);
                    return LerpStops(band.Stops, (float)local);
                }
            }
            return topColor; // 最後の帯の Hi 以上＝最高ランク色
        }

        // 帯内の色ストップ配列を t(0〜1)で補間する
        private static Color LerpStops(Color[] stops, float t)
        {
            if (stops.Length == 1) return stops[0];
            t = Mathf.Clamp01(t);
            int segs = stops.Length - 1;
            float scaled = t * segs;
            int i = Mathf.Min((int)scaled, segs - 1);
            return Color.Lerp(stops[i], stops[i + 1], scaled - i);
        }

        // 精度(%)から帯方式の色を求める（100%以上はグレー）
        private static Color AccuracyBarColor(double accPercent) => EvalBands(AccuracyBands, accPercent, ColGray);

        // 平均点数から帯方式の色を求める（115以上はグレー）
        private static Color ScoreBarColor(double score) => EvalBands(ScoreBands, score, ColGray);

        // バー色を少し明るくした数字フォント色を返す（白方向へ寄せて視認性を上げる）。
        // バーと同系色のまま、バーより一段明るい色にする。
        private static Color BrighterLabelColor(Color barColor) => Color.Lerp(barColor, Color.white, 0.35f);

        // Beat Saber内蔵の「UINoGlow」マテリアル。デフォルトのUIマテリアルはブルーム
        // （発光ポストエフェクト）を拾ってバーが眩しく滲んでしまうため、これを割り当てて
        // 発光を抑え、くっきり表示する。初回検索結果をキャッシュする。
        private static Material? _noGlowMaterial;
        private static Material? NoGlowMaterial =>
            _noGlowMaterial != null
                ? _noGlowMaterial
                : (_noGlowMaterial = Resources.FindObjectsOfTypeAll<Material>()
                    .FirstOrDefault(m => m.name == "UINoGlow"));

        // Imageにブラー（ブルーム発光）を抑えるマテリアルを適用する
        private static void ApplyNoGlow(Image img)
        {
            var mat = NoGlowMaterial;
            if (mat != null)
                img.material = mat;
        }

        // ─── Canvas・UI構築 ────────────────────────────────────────────────────────

        private void CreateDisplay()
        {
            // ゲームHUDのCanvasを参照して、位置・回転・スケールを合わせる
            var refCanvas = _hudController.GetComponent<Canvas>()
                            ?? _hudController.GetComponentInChildren<Canvas>(true);

            // 参照Canvasが見つからない場合に備えたフォールバック値（通常は下で上書きされる）
            Vector3 refPos = new Vector3(0f, -0.64f, 7.75f);
            Quaternion refRot = Quaternion.identity;
            Vector3 refScale = Vector3.one * 0.02f;
            int layer = _hudController.gameObject.layer;

            if (refCanvas != null)
            {
                Plugin.Log.Info("[LRCounter] Found ref canvas: " + refCanvas.name);
                refPos = refCanvas.transform.position;
                refRot = refCanvas.transform.rotation;
                refScale = refCanvas.transform.lossyScale;
                layer = refCanvas.gameObject.layer;
            }

            // ワールドスペースCanvasを生成
            _canvasObject = new GameObject("LRCounter_Canvas");
            _canvasObject.layer = layer;

            var canvas = _canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            _canvasObject.AddComponent<CurvedCanvasSettings>().SetRadius(0f); // 湾曲なし（平面）

            // 参照キャンバスと同じソーティングレイヤーを使うことで
            // 3Dオブジェクト（プラットフォームなど）に隠れず正しく前面描画される
            if (refCanvas != null)
            {
                canvas.sortingLayerID = refCanvas.sortingLayerID;
                canvas.sortingOrder = refCanvas.sortingOrder + 1;
            }
            canvas.overrideSorting = true;

            Plugin.Log.Info($"[LRCounter] refPos={refPos}  refScale={refScale}  sortingLayer={canvas.sortingLayerName}");
            // Z はワールド座標でカメラ側へ DepthZ ぶん寄せて前面化する（傾きの影響を受けず高さは不変）。
            // X/Y のオフセットは廃止し、バーごとの個別設定（Canvas内アンカー）で位置を決める。
            _canvasObject.transform.position = new Vector3(
                refPos.x,
                refPos.y + CanvasBaseYOffset,
                refPos.z - _config.DepthZ);
            _canvasObject.transform.rotation = refRot;
            // ゲームHUDのスケールをそのまま使うと小さすぎるため3倍に拡大。
            // （DepthZ で近づけたぶんは遠近で素直に拡大して見える＝Zが見た目の奥行きとして効く）
            _canvasObject.transform.localScale = refScale * 3f;

            // Canvasの論理サイズ（200×100）。anchorで割合指定するので実際の見た目に影響する
            var canvasRT = (RectTransform)_canvasObject.transform;
            canvasRT.sizeDelta = new Vector2(200f, 100f);

            // 左右のバーを生成
            (_leftFill, _leftFlashOverlay, _leftLabel, _leftPPLabel) = CreateSideBar(canvasRT, layer, isLeft: true);
            (_rightFill, _rightFlashOverlay, _rightLabel, _rightPPLabel) = CreateSideBar(canvasRT, layer, isLeft: false);
            _debugLabel = CreateDebugLabel(canvasRT, layer);
            _totalLabel = CreateTotalLabel(canvasRT, layer);
            CreateCenterBar(canvasRT, layer);

            // 目標ライン（必要精度）を各精度バー上に作る。位置はUpdateDisplayで毎回更新。
            _leftTargetLine = CreateTargetLine((RectTransform)_leftFill!.transform.parent, layer);
            _rightTargetLine = CreateTargetLine((RectTransform)_rightFill!.transform.parent, layer);

            // フラッシュのフェードアウトはUpdate()で毎フレーム処理する必要があるため
            // MonoBehaviourをアタッチしてTickFlash()を呼ばせる
            _canvasObject.AddComponent<DisplayTicker>().Controller = this;

            ApplyVisibility(); // 設定の表示ON/OFFを反映
            UpdateDisplay(); // 初期表示
        }

        // 各要素（精度バー/平均点数バー/合算ラベル）の表示ON/OFFを設定に従って反映する。
        // 要素自体は常に生成し（UpdateDisplayのnull参照を避けるため）、GameObjectのactiveで出し分ける。
        private void ApplyVisibility()
        {
            bool acc = _config.ShowAccBar;
            SetActive(_leftFill?.transform.parent, acc);   // bg（fill・目盛り線を含む）
            SetActive(_rightFill?.transform.parent, acc);
            SetActive(_leftFlashOverlay?.transform, acc);
            SetActive(_rightFlashOverlay?.transform, acc);
            SetActive(_leftLabel?.transform, acc);
            SetActive(_rightLabel?.transform, acc);
            SetActive(_leftPPLabel?.transform, acc);
            SetActive(_rightPPLabel?.transform, acc);

            bool score = _config.ShowScoreBar;
            SetActive(_leftCutFill?.transform.parent, score);
            SetActive(_rightCutFill?.transform.parent, score);
            SetActive(_leftCutLabel?.transform, score);
            SetActive(_rightCutLabel?.transform, score);

            SetActive(_totalLabel?.transform, _config.ShowTotalLabel);
        }

        private static void SetActive(Transform? t, bool active)
        {
            if (t != null) t.gameObject.SetActive(active);
        }

        // ─── デバッグラベルの生成 ──────────────────────────────────────────────────

        // バーラベルのすぐ下（Y 0.68〜0.85）にデバッグテキストを表示する
        // BeatSaberUI.CreateText は位置が制御できないため TextMeshProUGUI を直接使用
        private TMP_Text CreateDebugLabel(RectTransform canvasRT, int layer)
        {
            var go = new GameObject("LRDebug");
            go.layer = layer;
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(canvasRT, false);
            // バーより下に配置（Yを下げるほど画面の下側へ。0=Canvas下端 / 1=上端）
            rt.anchorMin = new Vector2(0.00f, 0.15f);
            rt.anchorMax = new Vector2(1.00f, 0.32f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // BeatSaberUI.CreateText を使わず直接 TextMeshProUGUI を追加することで
            // RectTransform の anchor 設定が確実に反映される
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = "";
            tmp.color = Color.yellow;
            tmp.fontSize = 3f;
            tmp.alignment = TextAlignmentOptions.Center;
            // enableWordWrapping は新しいBeat Saber(TMPro)では非推奨だが、1.40.8には後継の
            // textWrappingMode が無いためこちらを使用。バージョン差の警告を局所的に抑制する。
#pragma warning disable CS0618
            tmp.enableWordWrapping = false;
#pragma warning restore CS0618
            tmp.overflowMode = TextOverflowModes.Overflow;
            return tmp;
        }

        // ─── 合算ラベルの生成 ──────────────────────────────────────────────────────

        // 左右合計の精度・PPを中央上部に表示する。位置(中心X/下端Y)・フォントサイズは設定で可変。
        private TMP_Text CreateTotalLabel(RectTransform canvasRT, int layer)
        {
            var go = new GameObject("LRTotal");
            go.layer = layer;
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(canvasRT, false);
            // 中心Xを基準に左右へ広め(±0.25)に取り、中央寄せ＋オーバーフローで実際の文字幅に依存しない
            float x = _config.TotalLabelX;
            float y = _config.TotalLabelY;
            rt.anchorMin = new Vector2(x - 0.25f, y);
            rt.anchorMax = new Vector2(x + 0.25f, y + 0.08f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = "";
            tmp.color = Color.yellow;                 // PP・%とも黄色
            tmp.fontSize = Mathf.Max(_config.TotalLabelSize, 1f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.lineSpacing = -30f;                   // PPと%の行間を詰める
#pragma warning disable CS0618
            tmp.enableWordWrapping = false;
#pragma warning restore CS0618
            tmp.overflowMode = TextOverflowModes.Overflow;
            return tmp;
        }

        // ─── 平均点数バー（中央の縦塗りつぶしバー）の生成 ──────────────────────────────

        // 画面中央のすぐ左右に、外側の精度バーと同じ連続塗りつぶしバーを1本ずつ置く。
        // グッドカットの平均生スコア(111〜115)を「下から上への塗り量」で表す。上ほど高得点。
        private void CreateCenterBar(RectTransform canvasRT, int layer)
        {
            // 左右バーの中心Xは画面中央(0.5)を起点に間隔ぶん左右へ広げる。幅は設定から取得。
            float barL = 0.5f - _config.ScoreBarSpacing * 0.5f;
            float barR = 0.5f + _config.ScoreBarSpacing * 0.5f;
            float halfW = _config.ScoreBarWidth * 0.5f;
            _leftCutFill = CreateCutBar(canvasRT, layer, "L", barL - halfW, barL + halfW);
            _rightCutFill = CreateCutBar(canvasRT, layer, "R", barR - halfW, barR + halfW);

            // 平均点数（数字・小数1桁）を各バーの上に表示（各バーの中心に合わせる）
            _leftCutLabel = CreateCutLabel(canvasRT, layer, "L", barL - CutLabelHalfWidth, barL + CutLabelHalfWidth, _leftColor);
            _rightCutLabel = CreateCutLabel(canvasRT, layer, "R", barR - CutLabelHalfWidth, barR + CutLabelHalfWidth, _rightColor);
        }

        // 中央の平均点数バー（背景＋下から伸びる塗りつぶし）を1本生成し、塗りつぶしImageを返す。
        // 外側の精度バーと同じ構造（暗い背景の上に縦Filledの塗りを重ねる）。
        private Image CreateCutBar(RectTransform canvasRT, int layer, string side, float xMin, float xMax)
        {
            // --- 背景（暗い半透明の黒）。下端を体力バーのすぐ上(約0.68)に合わせ上へ伸ばす ---
            var bgGO = new GameObject($"LRCutBar_{side}_BG");
            bgGO.layer = layer;
            var bgRT = bgGO.AddComponent<RectTransform>();
            bgRT.SetParent(canvasRT, false);
            bgRT.anchorMin = new Vector2(xMin, _config.ScoreBarY);
            bgRT.anchorMax = new Vector2(xMax, _config.ScoreBarY + _config.ScoreBarHeight);
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;
            var bgImg = bgGO.AddComponent<Image>();
            bgImg.sprite = CreateWhiteSprite();
            bgImg.color = new Color(0f, 0f, 0f, 0.5f);
            ApplyNoGlow(bgImg);
            // 前面化は Canvas ルートをワールドZで手前に出すことで一括して行うため、ここでの個別Z押し出しは不要

            // --- 塗りつぶし（平均点数に応じて下から上に伸びる）。BGの子にして範囲内に収める ---
            var fillGO = new GameObject($"LRCutBar_{side}_Fill");
            fillGO.layer = layer;
            var fillRT = fillGO.AddComponent<RectTransform>();
            fillRT.SetParent(bgRT, false);
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;
            var fill = fillGO.AddComponent<Image>();
            fill.sprite = CreateWhiteSprite();
            fill.color = Color.white;                       // 実際の色は UpdateDisplay で平均点数に応じた1色をtint
            fill.type = Image.Type.Filled;                  // fillAmountで表示量を制御
            fill.fillMethod = Image.FillMethod.Vertical;    // 縦方向に伸びる
            fill.fillOrigin = 0;                            // 0=下から上へ
            fill.fillAmount = 0f;                           // 初期値0（UpdateDisplayで更新）
            ApplyNoGlow(fill);

            // --- 目盛り線（1点刻みでバーを分割する横線） ---
            // 範囲(111〜115)の整数点ごとに線を引く。塗りつぶしの上に重ねたいので fill の後に bg の子として追加する。
            int scoreDivisions = (int)(ScoreDisplayMax - ScoreDisplayMin); // 4分割 → 内側に3本(112,113,114)
            for (int i = 1; i < scoreDivisions; i++)
            {
                float frac = i / (float)scoreDivisions;
                CreateGridLine(bgRT, layer, side, i, frac, GridLineHalfHeight, GridLineColor);
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
            rt.anchorMin = new Vector2(xMin, scoreTop + CutLabelGap); // バー上端の少し上に数字を出す
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

        // ─── 左右バーのUI要素を生成 ────────────────────────────────────────────────

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

            // --- 背景（暗い半透明の黒） ---
            var bgGO = new GameObject($"LRBar_{side}_BG");
            bgGO.layer = layer;
            var bgRT = bgGO.AddComponent<RectTransform>();
            bgRT.SetParent(canvasRT, false);
            // ★見えるバーの下端・上端はここで決まる（フラッシュ/ラベルではなくこのbgが本体）
            bgRT.anchorMin = new Vector2(barXMin, barBottom);
            bgRT.anchorMax = new Vector2(barXMax, barTop);
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;
            var bgImg = bgGO.AddComponent<Image>();
            bgImg.sprite = CreateWhiteSprite();
            bgImg.color = new Color(0f, 0f, 0f, 0.5f);
            ApplyNoGlow(bgImg);

            // --- 塗りつぶし（精度に応じて下から上に伸びる） ---
            // BGの子にすることでBGの範囲内に自動的に収まる
            var fillGO = new GameObject($"LRBar_{side}_Fill");
            fillGO.layer = layer;
            var fillRT = fillGO.AddComponent<RectTransform>();
            fillRT.SetParent(bgRT, false); // BGの子
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;
            var fill = fillGO.AddComponent<Image>();
            // 塗りつぶしは単色（白スプライト）。実際の色は UpdateDisplay で現在の精度に応じた1色をtintする
            // （バー全体が下から上まで同じ1色になる）
            fill.sprite = CreateWhiteSprite();
            fill.color = Color.white; // 初期色（UpdateDisplayで現在精度の色に更新）
            fill.type = Image.Type.Filled;     // fillAmountで表示量を制御
            fill.fillMethod = Image.FillMethod.Vertical; // 縦方向に伸びる
            fill.fillOrigin = 0;                     // 0=下から上へ
            fill.fillAmount = 0f;                    // 初期値0（UpdateDisplayで更新）
            ApplyNoGlow(fill);

            // --- 目盛り線（10%刻みでバーを10分割する横線） ---
            // 塗りつぶしの上に重ねたいので fill より後に bg の子として追加する
            for (int i = 1; i < GridDivisions; i++)
            {
                float frac = i / (float)GridDivisions;
                // この線が表す精度(%)。特定の精度だけ専用色で強調する（GridLineColorFor）。太さは全線共通。
                int pct = Mathf.RoundToInt((float)(AccDisplayMin + frac * (AccDisplayMax - AccDisplayMin)));
                CreateGridLine(bgRT, layer, side, i, frac, GridLineHalfHeight, GridLineColorFor(pct));
            }

            // --- フラッシュオーバーレイ（精度低下時に赤く光る） ---
            // 初期アルファ0で非表示、TickFlash()で徐々に消えていく
            var flashGO = new GameObject($"LRFlash_{side}");
            flashGO.layer = layer;
            var flashRT = flashGO.AddComponent<RectTransform>();
            flashRT.SetParent(canvasRT, false);
            flashRT.anchorMin = new Vector2(barXMin, barBottom); // bgと同じ範囲にする（バー全体を覆う）
            flashRT.anchorMax = new Vector2(barXMax, barTop);
            flashRT.offsetMin = Vector2.zero;
            flashRT.offsetMax = Vector2.zero;
            var flashImg = flashGO.AddComponent<Image>();
            flashImg.sprite = CreateWhiteSprite();
            flashImg.color = new Color(1f, 0f, 0f, 0f); // 赤・透明
            ApplyNoGlow(flashImg);

            // --- ラベル（バーの上に精度%を表示） ---
            var labelGO = new GameObject($"LRLabel_{side}");
            labelGO.layer = layer;
            var labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.SetParent(canvasRT, false);
            labelRT.anchorMin = new Vector2(barXMin, barTop); // バー上端の上に%ラベルを配置
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

            // --- PPラベル（%ラベルのさらに上に数値だけ表示） ---
            var ppGO = new GameObject($"LRPP_{side}");
            ppGO.layer = layer;
            var ppRT = ppGO.AddComponent<RectTransform>();
            ppRT.SetParent(canvasRT, false);
            ppRT.anchorMin = new Vector2(barXMin, barTop + BarLabelHeight);      // %ラベルの1段上
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

            // 前面化は Canvas ルートをワールドZで手前に出すことで一括して行う（個別のZ押し出しは廃止）

            return (fill, flashImg, label, ppLabel);
        }

        // 目盛り線の色を精度(%)ごとに決める。95%=黒で強調 / 98%=オレンジ / 99%=赤 / それ以外=通常(半透明白)
        private static Color GridLineColorFor(int pct)
        {
            switch (pct)
            {
                case 95: return GridLineColorBold;
                case 98: return ColOrange;
                case 99: return ColRed;
                default: return GridLineColor;
            }
        }

        // 精度バーの目盛り横線を1本生成する（frac=0で下端=0%, 1で上端=100%）。halfHeightで太さ、colorで色を指定
        private static void CreateGridLine(RectTransform barRT, int layer, string side, int idx, float frac, float halfHeight, Color color)
        {
            var go = new GameObject($"LRGrid_{side}_{idx}");
            go.layer = layer;
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(barRT, false);
            // 高さ0の横ラインをfracの位置に置き、offsetで上下に厚みを持たせる
            rt.anchorMin = new Vector2(0f, frac);
            rt.anchorMax = new Vector2(1f, frac);
            rt.offsetMin = new Vector2(0f, -halfHeight);
            rt.offsetMax = new Vector2(0f, halfHeight);
            var img = go.AddComponent<Image>();
            img.sprite = CreateWhiteSprite();
            img.color = color;
            ApplyNoGlow(img);
        }

        // 目標ライン(必要精度)を1本生成する。位置は後で SetTargetLineFrac で更新する。初期は非表示。
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
            img.sprite = CreateWhiteSprite();
            img.color = TargetLineColor;
            ApplyNoGlow(img);
            go.SetActive(false);
            return img;
        }

        // 目標ラインを縦位置 frac(0〜1) に移動する
        private static void SetTargetLineFrac(Image? line, float frac)
        {
            if (line == null) return;
            var rt = (RectTransform)line.transform;
            rt.anchorMin = new Vector2(0f, frac);
            rt.anchorMax = new Vector2(1f, frac);
        }

        // ThresholdPP を必要精度に逆算して目標ラインの位置を更新する。
        // Star評価が無い/Threshold未確定なら線を隠す。
        private void UpdateTargetLine()
        {
            double threshold = _trackerService.ThresholdPP;
            double star = _trackerService.StarRating;
            bool show = star > 0 && threshold > 0;

            if (_leftTargetLine != null) _leftTargetLine.gameObject.SetActive(show);
            if (_rightTargetLine != null) _rightTargetLine.gameObject.SetActive(show);
            if (!show) return;

            double reqAccPct = PPCalculator.AccuracyForPP(threshold, star) * 100.0;
            float frac = AccToFill(reqAccPct);
            SetTargetLineFrac(_leftTargetLine, frac);
            SetTargetLineFrac(_rightTargetLine, frac);
        }

        // 精度(%)を表示レンジ(AccDisplayMin〜Max)で正規化して塗りつぶし量(0〜1)に変換する
        private static float AccToFill(double acc)
        {
            return Mathf.Clamp01((float)((acc - AccDisplayMin) / (AccDisplayMax - AccDisplayMin)));
        }

        // 平均点数を表示レンジ(ScoreDisplayMin〜Max)で正規化して塗りつぶし量(0〜1)に変換する
        private static float ScoreToFill(double score)
        {
            return Mathf.Clamp01((float)((score - ScoreDisplayMin) / (ScoreDisplayMax - ScoreDisplayMin)));
        }

        // ─── 表示更新（ノーツを切るたびに呼ばれる） ────────────────────────────────

        private void UpdateDisplay()
        {
            if (!_config.Enabled) return;
            if (_leftFill == null || _rightFill == null) return;

            // TrackerServiceから現在の精度を取得（0.0〜1.0 → 0〜100%に変換）
            double leftAcc = _trackerService.LeftTracker.Accuracy * 100;
            double rightAcc = _trackerService.RightTracker.Accuracy * 100;

            // 前回より精度が下がっていたらフラッシュ開始
            if (_prevLeftAcc > 0 && leftAcc < _prevLeftAcc)
                _leftFlashEnd = Time.time + FlashDuration;
            if (_prevRightAcc > 0 && rightAcc < _prevRightAcc)
                _rightFlashEnd = Time.time + FlashDuration;
            _prevLeftAcc = leftAcc;
            _prevRightAcc = rightAcc;

            // バーの塗りつぶし量を精度に合わせて更新。表示レンジ(AccDisplayMin〜Max)を
            // 下端0〜上端1にマッピングし、レンジ外は0/1にClampする
            float leftFillAmt = AccToFill(leftAcc);
            float rightFillAmt = AccToFill(rightAcc);
            _leftFill!.fillAmount = leftFillAmt;
            _rightFill!.fillAmount = rightFillAmt;

            // バー全体を現在の精度に応じた帯方式の1色で塗る（下から上まで同色）。境目で色が飛ぶ
            Color leftBarColor = AccuracyBarColor(leftAcc);
            Color rightBarColor = AccuracyBarColor(rightAcc);
            _leftFill!.color = leftBarColor;
            _rightFill!.color = rightBarColor;

            // ラベルに精度%を表示。フォント色はバー色を少し明るくして合わせる
            _leftLabel!.text = $"{leftAcc:F1}%";
            _rightLabel!.text = $"{rightAcc:F1}%";
            _leftLabel!.color = BrighterLabelColor(leftBarColor);
            _rightLabel!.color = BrighterLabelColor(rightBarColor);

            // PP色は精度の帯色とは分離する：通常は黄、threshold(底上げライン)を超えたら緑。
            // threshold未確定(0)やアンランクのときは超過扱いにしない＝黄のまま。
            bool hasStar = _trackerService.StarRating > 0;
            bool thresholdExceeded = hasStar
                && _trackerService.ThresholdPP > 0
                && _trackerService.TotalPP >= _trackerService.ThresholdPP;
            Color ppColor = thresholdExceeded ? ColGreen : ColYellow;

            // %ラベルの上にPPを表示（小数1桁＋末尾PP）。アンランク(StarRating<=0)なら ""
            _leftPPLabel!.text = hasStar ? $"{_trackerService.LeftTracker.PP:F1}PP" : "";
            _rightPPLabel!.text = hasStar ? $"{_trackerService.RightTracker.PP:F1}PP" : "";
            _leftPPLabel!.color = ppColor;
            _rightPPLabel!.color = ppColor;

            // 中央上部の合算ラベル：上段にPP（黄/緑）、下段に%（精度帯色）。アンランクなら%のみ。
            // PPと%で色を分けるためリッチテキストの<color>でそれぞれ着色する。
            if (_totalLabel != null)
            {
                double totalAccPct = _trackerService.TotalAccuracy * 100.0;
                Color accColor = BrighterLabelColor(AccuracyBarColor(totalAccPct));
                string accHex = ColorUtility.ToHtmlStringRGB(accColor);
                string accLine = $"<color=#{accHex}>{totalAccPct:F2}%</color>";
                if (hasStar)
                {
                    string ppHex = ColorUtility.ToHtmlStringRGB(ppColor);
                    _totalLabel.text = $"<color=#{ppHex}>{_trackerService.TotalPP:F1}PP</color>\n{accLine}";
                }
                else
                {
                    _totalLabel.text = accLine;
                }
                _totalLabel.color = Color.white; // 実際の色は<color>タグで指定する
            }

            // 目標ライン（必要精度）の位置を更新
            UpdateTargetLine();

            // デバッグ：直前ノーツの生スコアと現在倍率を表示
            if (_debugLabel != null)
            {
                var L = _trackerService.LeftTracker;
                var R = _trackerService.RightTracker;
                int mult = _trackerService.CurrentMultiplier;

                // LastCutScore が -1 のときはまだそちらの手でカットしていない
                string lScore = L.LastCutScore >= 0 ? $"{L.LastCutScore}" : "---";
                string rScore = R.LastCutScore >= 0 ? $"{R.LastCutScore}" : "---";

                // 自前集計スコア（NF係数込み）とゲームの実表示スコア(modifiedScore)を突き合わせる。
                // 食い違ったら集計ズレなので赤字にする。
                int calcScore = _trackerService.TotalScore;
                int gameScore = _trackerService.GameModifiedScore;
                bool scoreMatches = calcScore == gameScore;

                double totalAcc = _trackerService.TotalAccuracy * 100.0;
                double totalPP = _trackerService.TotalPP;
                double star = _trackerService.StarRating;
                // 閾値（トータルを底上げできる最低PP）と、それを必要精度に逆算した%
                double thrPP = _trackerService.ThresholdPP;
                double thrAcc = PPCalculator.AccuracyForPP(thrPP, star) * 100.0;
                _debugLabel.text =
                    $"{totalAcc:F2}%  {totalPP:F1}pp  ★{star:F2}  L:{lScore}  R:{rScore}  x{mult}\n" +
                    $"{calcScore}：({gameScore}) /{_trackerService.TotalMaxScore}\n" +
                    $"Thr:{thrPP:F2}pp ({thrAcc:F2}%)";
                // 一致=通常色(黄)、不一致=赤。検算が合っているか一目で分かるようにする。
                _debugLabel.color = scoreMatches ? Color.yellow : Color.red;
            }

            // 中央バー：グッドカットの平均点数を塗り量・色・数字(小数1桁)で更新する
            UpdateCutBar(_leftCutFill, _leftCutLabel, _trackerService.LeftTracker.AverageCutScore, _trackerService.LeftTracker.FullCutNotes);
            UpdateCutBar(_rightCutFill, _rightCutLabel, _trackerService.RightTracker.AverageCutScore, _trackerService.RightTracker.FullCutNotes);
        }

        // 中央バーの塗り量・色・数字を平均点数から更新する。
        // 塗り量・色とも ScoreDisplayMin〜Max(111〜115) を 0〜1 にマッピングする
        // （111未満は空・115で満タン、色は111=赤〜115=紫）。グッドカットがまだ無いときは空表示。
        private void UpdateCutBar(Image? fill, TMP_Text? label, double averageCutScore, int cutNotes)
        {
            if (fill == null) return;

            if (cutNotes <= 0)
            {
                // まだグッドカットが無い → 空のバー・数字なし
                fill.fillAmount = 0f;
                if (label != null) label.text = "";
                return;
            }

            float fillAmt = ScoreToFill(averageCutScore);
            Color barColor = ScoreBarColor(averageCutScore); // 帯方式（110=赤〜115=グレー、境目で色が飛ぶ）
            fill.fillAmount = fillAmt;
            fill.color = barColor;

            if (label != null)
            {
                label.text = averageCutScore.ToString("F1");
                label.color = BrighterLabelColor(barColor); // フォント色はバー色を少し明るくして合わせる
            }
        }

        // ─── フラッシュ制御 ────────────────────────────────────────────────────────

        // DisplayTickerのUpdate()から毎フレーム呼ばれる
        internal void TickFlash()
        {
            float t = Time.time;
            SetFlashAlpha(_leftFlashOverlay, _leftFlashEnd, t, new Color(1f, 0f, 0f, 0.6f));
            SetFlashAlpha(_rightFlashOverlay, _rightFlashEnd, t, new Color(1f, 0f, 0f, 0.6f));
        }

        // フラッシュの残り時間に応じてアルファ値を計算し、時間切れなら透明にする
        private static void SetFlashAlpha(Image? overlay, float endTime, float now, Color baseColor)
        {
            if (overlay == null) return;
            if (endTime < 0 || now >= endTime)
            {
                // 非アクティブ or 時間切れ → 完全透明
                overlay.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);
                return;
            }
            // 残り時間が短いほどアルファが下がるリニアフェード
            float alpha = Mathf.Clamp01((endTime - now) / FlashDuration) * baseColor.a;
            overlay.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
        }

        // ─── フレーム更新用MonoBehaviour ──────────────────────────────────────────

        // IInitializable/IDisposableはUnityのUpdateを持てないため、
        // MonoBehaviourをCanvasにアタッチしてUpdate()を橋渡しする
        private class DisplayTicker : MonoBehaviour
        {
            internal LRDisplayController? Controller;
            private void Update() => Controller?.TickFlash();
        }
    }
}
