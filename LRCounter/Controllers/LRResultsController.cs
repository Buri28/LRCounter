using System;
using HMUI;
using LRCounter.Configuration;
using LRCounter.Controllers.Display;
using TMPro;
using UnityEngine;
using Zenject;

namespace LRCounter.Controllers
{
    // リザルト画面（ステージクリア）の上部に、左右の平均精度とPPを表示する（メニュースコープ）。
    // 値は LRResultStore（App スコープ）経由でゲームプレイシーンから受け取る。
    // 表示はゲーム画面に合わせ、左右それぞれ「L/R → PP → 精度」の3段（PPが上）で出す。
    public class LRResultsController : IInitializable, IDisposable
    {
        private readonly ResultsViewController _resultsViewController;
        private readonly LRResultStore _store;
        private readonly PluginConfig _config;

        private TMP_Text? _leftText;
        private TMP_Text? _rightText;

        // 左右列の中央Xからの横オフセット（バナー上の空きスペースに左右で並べる）
        private const float ColumnOffsetX = 14f;
        private const float TopOffsetY = 14f; // 上端からの位置（正で上端より上）

        [Inject]
        public LRResultsController(
            ResultsViewController resultsViewController,
            LRResultStore store,
            PluginConfig config)
        {
            _resultsViewController = resultsViewController;
            _store = store;
            _config = config;
        }

        public void Initialize()
        {
            _resultsViewController.didActivateEvent += OnResultsActivated;
        }

        public void Dispose()
        {
            _resultsViewController.didActivateEvent -= OnResultsActivated;
            if (_leftText != null) GameObject.Destroy(_leftText.gameObject);
            if (_rightText != null) GameObject.Destroy(_rightText.gameObject);
        }

        // リザルト画面が表示されるたびに呼ばれる
        private void OnResultsActivated(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            if (!_config.Enabled) return;
            if (_leftText == null || _rightText == null)
            {
                _leftText = CreateHandText("LRResultL", -ColumnOffsetX);
                _rightText = CreateHandText("LRResultR", ColumnOffsetX);
            }
            UpdateTexts();
        }

        // 1列ぶん（中央上部から xOffset ずらした位置）のテキストを作る
        private TMP_Text CreateHandText(string name, float xOffset)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(_resultsViewController.transform, false);
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(xOffset, TopOffsetY);
            rt.sizeDelta = new Vector2(40f, 24f);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = "";
            tmp.fontSize = 5f;
            tmp.alignment = TextAlignmentOptions.Top; // 横中央・上揃え（3段を縦に積む）
            tmp.lineSpacing = -12f;                   // 行間を詰める
            tmp.richText = true;
#pragma warning disable CS0618
            tmp.enableWordWrapping = false;
#pragma warning restore CS0618
            tmp.overflowMode = TextOverflowModes.Overflow;
            return tmp;
        }

        // ストアの値で左右テキストを更新する。手の色で着色し、ランク外ならPP行は "---"。
        private void UpdateTexts()
        {
            if (_leftText == null || _rightText == null) return;

            if (!_store.HasResult)
            {
                _leftText.text = "";
                _rightText.text = "";
                return;
            }

            _leftText.color = LRDisplayCommon.LeftHandColorDefault;
            _rightText.color = LRDisplayCommon.RightHandColorDefault;
            _leftText.text = FormatHand("L", _store.LeftPP, _store.LeftAccuracyPercent);
            _rightText.text = FormatHand("R", _store.RightPP, _store.RightAccuracyPercent);
        }

        // 「L/R → PP → 精度」の3段（PPが上＝ゲーム画面と同じ並び）。ランク外はPPを "---"。
        private string FormatHand(string label, double pp, double accPercent)
        {
            string ppLine = _store.HasStar ? $"{pp:F1}pp" : "---";
            return $"{label}\n{ppLine}\n{accPercent:F2}%";
        }
    }
}
