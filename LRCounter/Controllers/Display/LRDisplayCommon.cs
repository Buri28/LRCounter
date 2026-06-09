using System.Linq;
using LRCounter.Configuration;
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

        // ─── 11段階バー色（精度バー・点数バー共通） ───────────────────────────────
        // 各バンドの下端境界（その値以上でそのバンド）。最後のグレー(満点)は perfect 値でのみ適用。
        // index 0..9 が設定の色 00〜09 に対応。10(グレー)は満点(100% / 115)のときだけ。
        private static readonly double[] AccLowerBounds = { 0, 50, 70, 80, 90, 95, 96, 97, 98, 99 };
        private static readonly double[] ScoreLowerBounds = { 105, 106, 107, 108, 109, 110, 111, 112, 113, 114 };
        private const double AccPerfect = 100.0;
        private const double ScorePerfect = 115.0;

        // バンド下端での淡さ（白へ寄せる割合）。上端＝原色、下端＝この割合だけ白寄り。
        private const float BandPaleAmount = 0.5f;

        // 精度(%)から11段階の色を求める（100%は満点時のみグレー）
        public static Color AccuracyBarColor(double accPercent)
            => EvalElevenBands(AccLowerBounds, AccPerfect, GetBandColors(), accPercent);

        // 平均点数から11段階の色を求める（115は満点時のみグレー）
        public static Color ScoreBarColor(double score)
            => EvalElevenBands(ScoreLowerBounds, ScorePerfect, GetBandColors(), score);

        // 設定から11色（index0=最下位…index10=満点色）を取得する
        public static Color[] GetBandColors()
        {
            var c = PluginConfig.Instance;
            return new[]
            {
                ParseHex(c.Color00Red),
                ParseHex(c.Color01Orange),
                ParseHex(c.Color02Yellow),
                ParseHex(c.Color03Green),
                ParseHex(c.Color04Blue),
                ParseHex(c.Color05Magenta),
                ParseHex(c.Color06Cyan),
                ParseHex(c.Color07LightBlue),
                ParseHex(c.Color08Skin),
                ParseHex(c.Color09Purple),
                ParseHex(c.Color10Grey),
            };
        }

        // hex(#RRGGBB)→Color。失敗時は白。
        public static Color ParseHex(string hex)
            => ColorUtility.TryParseHtmlString(hex, out var col) ? col : Color.white;

        // Color→hex(#RRGGBB)
        public static string ToHex(Color col) => "#" + ColorUtility.ToHtmlStringRGB(col);

        // 値を11段階で評価する。各バンドは下端=淡い→上端=原色のグラデーション。満点で colors の最後(グレー)。
        private static Color EvalElevenBands(double[] lowers, double perfect, Color[] colors, double value)
        {
            if (value >= perfect) return colors[colors.Length - 1]; // 満点（100% / 115）のみ
            for (int i = lowers.Length - 1; i >= 0; i--)
            {
                if (value >= lowers[i])
                {
                    double hi = (i + 1 < lowers.Length) ? lowers[i + 1] : perfect;
                    float local = Mathf.Clamp01((float)((value - lowers[i]) / (hi - lowers[i])));
                    Color vivid = colors[i];
                    return Color.Lerp(Color.Lerp(vivid, Color.white, BandPaleAmount), vivid, local);
                }
            }
            // 最下端境界未満（点数で105未満など）→ 最も淡い最下位色
            return Color.Lerp(colors[0], Color.white, BandPaleAmount);
        }

        // PPラベルの色：通常は黄、threshold(底上げライン)を超えたら緑。
        // threshold未確定(0)やアンランクのときは黄のまま。
        public static Color PPColor(LRTrackerService tracker)
        {
            bool exceeded = tracker.StarRating > 0
                && tracker.ThresholdPP > 0
                && tracker.TotalPP >= tracker.ThresholdPP;
            return exceeded ? ColGreen : ColYellow;
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
        public static Image CreateGridLine(RectTransform barRT, int layer, string side, int idx, float frac, float halfHeight, Color color)
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
            return img;
        }

        // Transformのアクティブ切り替え（null安全）
        public static void SetActive(Transform? t, bool active)
        {
            if (t != null) t.gameObject.SetActive(active);
        }
    }
}
