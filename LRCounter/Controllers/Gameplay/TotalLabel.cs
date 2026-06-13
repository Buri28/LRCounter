using LRCounter.Configuration;
using TMPro;
using UnityEngine;

namespace LRCounter.Controllers.Display
{
    // 上段にPP（黄/緑）、下段に%（精度帯色）を中央上部に表示する。位置・サイズは設定で可変。
    internal class TotalLabel : IDisplayComponent
    {
        private readonly PluginConfig _config;
        private readonly LRTrackerService _tracker;

        private TMP_Text? _label;

        public TotalLabel(PluginConfig config, LRTrackerService tracker)
        {
            _config = config;
            _tracker = tracker;
        }

        public void Build(RectTransform canvasRT, int layer)
        {
            var go = new GameObject("LRTotal");
            go.layer = layer;
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(canvasRT, false);
            // 中心Xを基準に左右へ広め(±0.25)に取り、中央寄せ＋オーバーフローで文字幅に依存しない
            float x = _config.TotalLabelX;
            float y = _config.TotalLabelY;
            rt.anchorMin = new Vector2(x - 0.25f, y);
            rt.anchorMax = new Vector2(x + 0.25f, y + 0.08f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = "";
            tmp.color = Color.white;
            tmp.fontSize = Mathf.Max(_config.TotalLabelSize, 1f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.lineSpacing = -30f; // PPと%の行間を詰める
#pragma warning disable CS0618
            tmp.enableWordWrapping = false;
#pragma warning restore CS0618
            tmp.overflowMode = TextOverflowModes.Overflow;
            _label = tmp;
        }

        public void Update()
        {
            if (_label == null) return;

            bool hasStar = _tracker.StarRating > 0;
            double totalAccPct = _tracker.TotalAccuracy * 100.0;

            // %は精度帯色（バー色と同色）、PPは黄/緑（threshold超過で緑）。PPと%で色を分けるためリッチテキストで着色。
            Color accColor = LRDisplayCommon.AccuracyBarColor(totalAccPct);
            string accHex = ColorUtility.ToHtmlStringRGB(accColor);
            string accLine = $"<color=#{accHex}>{totalAccPct:F2}%</color>";

            if (hasStar)
            {
                string ppHex = ColorUtility.ToHtmlStringRGB(LRDisplayCommon.PPColor(_tracker));
                _label.text = $"<color=#{ppHex}>{_tracker.TotalPP:F1}PP</color>\n{accLine}";
            }
            else
            {
                _label.text = accLine;
            }
            _label.color = Color.white; // 実際の色は<color>タグで指定する
        }

        public void ApplyVisibility()
        {
            LRDisplayCommon.SetActive(_label?.transform, _config.ShowTotalLabel);
        }

        // フラッシュ処理は無いので空実装
        public void TickFlash() { }
    }
}
