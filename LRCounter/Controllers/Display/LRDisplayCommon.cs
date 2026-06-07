using System.Linq;
using LRCounter.Controllers;
using UnityEngine;
using UnityEngine.UI;

namespace LRCounter.Controllers.Display
{
    // 表示コンポーネント間で共有するユーティリティ。
    // 色帯（バンド方式）・スプライト生成・UINoGlow・目盛り線・PP色判定をまとめる。
    internal static class LRDisplayCommon
    {
        // ─── 帯で使う基準色（名前付き） ──────────────────────────────────────────────
        public static readonly Color ColRed = new Color(0.95f, 0.15f, 0.15f); // 赤
        public static readonly Color ColOrange = new Color(1f, 0.50f, 0.10f); // オレンジ
        public static readonly Color ColYellow = new Color(1f, 0.92f, 0.15f); // 黄
        public static readonly Color ColYellowGreen = new Color(0.62f, 0.90f, 0.20f); // 黄緑
        public static readonly Color ColGreen = new Color(0.20f, 0.90f, 0.25f); // 緑
        public static readonly Color ColBlue = new Color(0.20f, 0.50f, 1f);    // 青
        public static readonly Color ColSkinDark = new Color(0.82f, 0.54f, 0.40f); // 肌色(暗)
        public static readonly Color ColSkinBright = new Color(1f, 0.85f, 0.72f); // 肌色(明)
        public static readonly Color ColMagentaDark = new Color(0.45f, 0.05f, 0.35f); // マゼンタ(暗)
        public static readonly Color ColMagentaBright = new Color(1f, 0.25f, 0.90f); // マゼンタ(明)
        public static readonly Color ColGray = new Color(0.90f, 0.90f, 0.90f); // 白に近いグレー(最高ランク)

        // 左右の手の既定色（リザルト画面の左右列などで使用）。
        // 将来、色をまとめて設定可能にするときはここを設定値に差し替える。
        public static readonly Color LeftHandColorDefault = new Color(1f, 0.33f, 0.33f);  // 赤系
        public static readonly Color RightHandColorDefault = new Color(0.33f, 0.33f, 1f); // 青系

        // 目盛り線の共通色・太さ
        public static readonly Color GridLineColor = new Color(1f, 1f, 1f, 0.45f); // 通常の目盛り線（半透明白）
        public const float GridLineHalfHeight = 0.1f; // 目盛り線の半分の高さ（全線共通）

        // 1つの帯。[Lo, Hi) の値範囲を Stops の色で滑らかに塗る（帯内グラデーション）。
        // 帯の Hi 側の色と次の帯の Lo 側の色が違うことで「切れ目」が生まれる。
        public readonly struct ColorBand
        {
            public readonly double Lo;
            public readonly double Hi;
            public readonly Color[] Stops;
            public ColorBand(double lo, double hi, Color[] stops) { Lo = lo; Hi = hi; Stops = stops; }
        }

        // 精度(%)の帯テーブル。最上端(100%以上)はグレー。
        private static readonly ColorBand[] AccuracyBands =
        {
            new ColorBand(90, 95, new[] { ColRed, ColYellow, ColYellowGreen }), // 90〜94%台: 赤→黄→黄緑
            new ColorBand(95, 98, new[] { ColGreen, ColBlue }),                 // 95〜97%台: 緑→青
            new ColorBand(98, 99, new[] { ColSkinDark, ColSkinBright }),        // 98%台: 肌色徐々に明
            new ColorBand(99, 100, new[] { ColMagentaDark, ColMagentaBright }), // 99%台: マゼンタ徐々に明
        };

        // 平均点数の帯テーブル。最上端(115以上)はグレー。
        private static readonly ColorBand[] ScoreBands =
        {
            new ColorBand(110, 111, new[] { ColRed, ColOrange }),                 // 110: 赤→橙
            new ColorBand(111, 112, new[] { ColYellow, ColYellowGreen }),         // 111: 黄→黄緑
            new ColorBand(112, 113, new[] { ColGreen, ColBlue }),                 // 112: 緑→青
            new ColorBand(113, 114, new[] { ColSkinDark, ColSkinBright }),        // 113: 肌色徐々に明
            new ColorBand(114, 115, new[] { ColMagentaDark, ColMagentaBright }),  // 114: マゼンタ徐々に明
        };

        // 精度(%)から帯方式の色を求める（100%以上はグレー）
        public static Color AccuracyBarColor(double accPercent) => EvalBands(AccuracyBands, accPercent, ColGray);

        // 平均点数から帯方式の色を求める（115以上はグレー）
        public static Color ScoreBarColor(double score) => EvalBands(ScoreBands, score, ColGray);

        // バー色を少し明るくした数字フォント色を返す（白方向へ寄せて視認性を上げる）
        public static Color BrighterLabelColor(Color barColor) => Color.Lerp(barColor, Color.white, 0.35f);

        // PPラベルの色：通常は黄、threshold(底上げライン)を超えたら緑。
        // threshold未確定(0)やアンランクのときは黄のまま。
        public static Color PPColor(LRTrackerService tracker)
        {
            bool exceeded = tracker.StarRating > 0
                && tracker.ThresholdPP > 0
                && tracker.TotalPP >= tracker.ThresholdPP;
            return exceeded ? ColGreen : ColYellow;
        }

        // 帯テーブルと値から色を求める。最下端未満は先頭色、最上端以上は topColor。
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

        // ─── スプライト・マテリアル ──────────────────────────────────────────────────

        // 1x1ピクセルの白いスプライトを生成する（Imageの土台として使用）
        public static Sprite CreateWhiteSprite()
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
        }

        // Beat Saber内蔵の「UINoGlow」マテリアル。デフォルトUIマテリアルはブルームを拾って
        // バーが眩しく滲むため、これを割り当てて発光を抑える。初回検索結果をキャッシュ。
        private static Material? _noGlowMaterial;
        private static Material? NoGlowMaterial =>
            _noGlowMaterial != null
                ? _noGlowMaterial
                : (_noGlowMaterial = Resources.FindObjectsOfTypeAll<Material>()
                    .FirstOrDefault(m => m.name == "UINoGlow"));

        // Imageにブラー（ブルーム発光）を抑えるマテリアルを適用する
        public static void ApplyNoGlow(Image img)
        {
            var mat = NoGlowMaterial;
            if (mat != null)
                img.material = mat;
        }

        // ─── 目盛り線 ────────────────────────────────────────────────────────────────

        // 目盛り横線を1本生成する（frac=0で下端, 1で上端）。halfHeightで太さ、colorで色を指定。
        public static void CreateGridLine(RectTransform barRT, int layer, string side, int idx, float frac, float halfHeight, Color color)
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

        // Transformのアクティブ切り替え（null安全）
        public static void SetActive(Transform? t, bool active)
        {
            if (t != null) t.gameObject.SetActive(active);
        }
    }
}
