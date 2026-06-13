using System;
using HMUI;
using LRCounter.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace LRCounter.Controllers.Results
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
        private const float ColumnOffsetX = 16f;
        private const float TopOffsetY = 8f; // 上端からの位置（正で上端より上。小さいほど下）
        private const float DividerHeight = 14f; // 中央の縦区切り線の高さ
        private const float HandTextFontSize = 5f;  // 各列テキストの基準フォント（差分行はこれより1pt小さい）
        private const float HandTextLineSpacing = -55f; // 行間（負で詰める。3段を近づける）
        private const float AccToDeltaLineHeightPct = -10f; // 精度→差分 の行間（%。小さいほど詰まる。この区間だけに適用）

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
            tmp.fontSize = HandTextFontSize;
            tmp.alignment = TextAlignmentOptions.Top; // 横中央・上揃え（3段を縦に積む）
            tmp.lineSpacing = HandTextLineSpacing;    // 行間を詰める
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
            _leftText.text = FormatHand(_store.LeftAccuracyPercent, _store.LeftPP, _store.LeftCutNotes, _store.LeftTotalNotes,
                _store.HasDelta, _store.LeftDeltaPercent);
            _rightText.text = FormatHand(_store.RightAccuracyPercent, _store.RightPP, _store.RightCutNotes, _store.RightTotalNotes,
                _store.HasDelta, _store.RightDeltaPercent);
        }

        // 3段表示。1段目「精度% (PP)」／2段目「前回ベストとの差分(+/-%)」／3段目「グッドカット数 / 全ノーツ数」。
        // 差分が無いときも2段目は空行（差分行と同じ高さ）にして、3段目（ノーツ数）の位置を固定する＝上に詰めない。
        // ランク外はPPを省略する。
        private string FormatHand(double accPercent, double pp, int cutNotes, int totalNotes, bool hasDelta, double deltaPercent)
        {
            string ppPart = _store.HasStar ? $" ({pp:F1}pp)" : "";
            string cutLine = $"{cutNotes} / {totalNotes}";

            string deltaLine;
            if (hasDelta)
            {
                // 差分: プラス(同値含む)は緑 "+0.03%"、マイナスは赤 "-0.03%"。テキスト全体は手の色なので色タグで個別着色。
                // フォントは基準より2pt小さく表示する。
                string deltaBody = deltaPercent >= 0
                    ? $"<color={LRDisplayCommon.ToHex(LRDisplayCommon.ColGreen)}>+{deltaPercent:F2}%</color>"
                    : $"<color={LRDisplayCommon.ToHex(LRDisplayCommon.ColRed)}>{deltaPercent:F2}%</color>"; // 負号は数値に含まれる
                // 精度行の (pp) 表記ぶんを透明文字で末尾に付け、差分の中心を「精度の数字」の中心へ合わせる
                // （中央揃え＝左右対称は維持したまま、PP表記で右へずれる分を相殺）。直後に alpha を戻す。
                // パディングは差分と同じ size 内に入れる：行の高さは最大グリフで決まるため、外に出すと
                // 基準サイズ(=大きい方)で行が高くなり、PP有無で行間が変わってしまう。
                string invisiblePad = ppPart.Length > 0 ? $"<alpha=#00>{ppPart}<alpha=#ff>" : "";
                deltaLine = $"<size={HandTextFontSize - 1f}>{deltaBody}{invisiblePad}</size>";
            }
            else
            {
                // 差分なし：差分行と同じフォントサイズの透明スペースを置き、空行のまま高さを確保する（ノーツ数を上に詰めない）。
                deltaLine = $"<size={HandTextFontSize - 1f}><alpha=#00>-<alpha=#ff></size>";
            }

            // 精度→差分 だけ行間を詰める：精度行の line-height を縮め、直後に既定へ戻す（差分→カット数は通常のまま）。
            return $"<line-height={AccToDeltaLineHeightPct}%>{accPercent:F2}%{ppPart}</line-height>\n{deltaLine}\n{cutLine}";
        }
    }
}
