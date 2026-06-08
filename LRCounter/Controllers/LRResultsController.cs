using System;
using HMUI;
using LRCounter.Configuration;
using LRCounter.Controllers.Display;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace LRCounter.Controllers
{
    // リザルト画面（ステージクリア）の上部に、左右の結果を中央の縦線で区切って表示する（メニュースコープ）。
    // 値は LRResultStore（App スコープ）経由でゲームプレイシーンから受け取る。
    // 各列は2段：1段目「精度% (PP)」／2段目「グッドカット数 / 全ノーツ数」。
    public class LRResultsController : IInitializable, IDisposable
    {
        private readonly ResultsViewController _resultsViewController;
        private readonly LRResultStore _store;
        private readonly PluginConfig _config;

        private TMP_Text? _leftText;
        private TMP_Text? _rightText;
        private Image? _divider;

        // 左右列の中央Xからの横オフセット（バナー上の空きスペースに左右で並べる）
        private const float ColumnOffsetX = 18f;
        private const float TopOffsetY = 7f; // 上端からの位置（正で上端より上。小さいほど下）
        private const float DividerHeight = 16f; // 中央の縦区切り線の高さ

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
            if (_divider != null) GameObject.Destroy(_divider.gameObject);
        }

        // リザルト画面が表示されるたびに呼ばれる
        private void OnResultsActivated(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            if (!_config.Enabled) return;
            if (_leftText == null || _rightText == null)
            {
                _leftText = CreateHandText("LRResultL", -ColumnOffsetX);
                _rightText = CreateHandText("LRResultR", ColumnOffsetX);
                _divider = CreateDivider("LRResultDivider");
            }
            UpdateTexts();
        }

        // 中央の縦区切り線を作る
        private Image CreateDivider(string name)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(_resultsViewController.transform, false);
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, TopOffsetY);
            rt.sizeDelta = new Vector2(0.4f, DividerHeight);

            var img = go.AddComponent<Image>();
            img.sprite = LRDisplayCommon.CreateWhiteSprite();
            img.color = new Color(1f, 1f, 1f, 0.5f);
            LRDisplayCommon.ApplyNoGlow(img);
            return img;
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
            _leftText.text = FormatHand(_store.LeftAccuracyPercent, _store.LeftPP, _store.LeftCutNotes, _store.LeftTotalNotes);
            _rightText.text = FormatHand(_store.RightAccuracyPercent, _store.RightPP, _store.RightCutNotes, _store.RightTotalNotes);
        }

        // 2段表示。1段目「精度% (PP)」／2段目「グッドカット数 / 全ノーツ数」。ランク外はPPを省略。
        private string FormatHand(double accPercent, double pp, int cutNotes, int totalNotes)
        {
            string ppPart = _store.HasStar ? $" ({pp:F1}pp)" : "";
            return $"{accPercent:F2}%{ppPart}\n{cutNotes} / {totalNotes}";
        }
    }
}
