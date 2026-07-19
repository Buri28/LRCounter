using HMUI;
using LRCounter.Configuration;
using System;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

namespace LRCounter.Controllers.Gameplay
{
    // ゲーム中に各表示要素（精度バー/合算ラベル/デバッグラベル）を
    // ワールドスペースCanvasにまとめて表示するコントローラー。
    // 各要素の実体は Display 名前空間のコンポーネントに分離し、ここはそれらへ移譲する。
    public class LRDisplayController : IInitializable, IDisposable
    {
        private readonly LRTrackerService _trackerService; // スコア・精度データの提供元
        private readonly PluginConfig _config;               // 設定（表示ON/OFF、色、位置など）
        private readonly CoreGameHUDController _hudController; // 位置合わせの基準となるゲームHUD

        private GameObject? _canvasObject; // 表示用Canvasのルートオブジェクト

        // 表示コンポーネント群。全要素を共通インターフェース1つで保持し、リストで一括処理する
        // （要素を増やすときはここに add するだけで Build/Update/… が回る＝拡張しやすい）。
        private readonly List<IDisplayComponent> _components = new List<IDisplayComponent>();

        // Canvasの基準Yオフセット(ワールド単位)。ゲームHUDのCanvas位置からこのぶん上にずらす。
        private const float CanvasBaseYOffset = 1.3f;

        // Canvasの固定スケール（環境非依存）。以前はゲームHUDの lossyScale×3 をコピーしていたが、
        // 環境（Environment）オーバーライドを使う譜面ではHUDのスケールが変わり、LRCounterも一緒に
        // 拡大して「左右に広がる／近づく」ズレが出ていた。環境に依存しない固定値にして一定に保つ。
        // 値は従来の標準環境相当（フォールバック 0.02 × 3 = 0.06）。
        private const float FixedCanvasScale = 0.06f;

        // Canvasの基準位置・回転（環境非依存の固定値）。標準環境のHUD(EnergyPanel)の値に一致する。
        // 環境オーバーライドを使う譜面ではHUDが手前・上に動き、追従すると「近づく／広がる」ズレが出るため、
        // HUDのTransformはコピーせずこの固定値を使う（layer/sorting だけは見つけたHUD Canvasから取る）。
        private static readonly Vector3 FixedRefPos = new Vector3(0f, -0.64f, 7.75f);
        private static readonly Quaternion FixedRefRot = Quaternion.identity;

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

            // 左右の手の色は共通の既定色を使う（バー・ラベル色は実際には帯色/threshold色で上書きされる）
            Color leftColor = LRDisplayCommon.LeftHandColorDefault;
            Color rightColor = LRDisplayCommon.RightHandColorDefault;

            // 各表示コンポーネントを生成して共通インターフェースのリストに登録（移譲先）。
            // 要素を追加したいときはここに add するだけでよい。
            var accuracyBars = new AccuracyBars(_config, _trackerService, leftColor, rightColor);
            _components.Add(accuracyBars);
            _components.Add(new TotalLabel(_config, _trackerService));
            if (!_config.HideDebugLabel)
                // 低スコア音の閾値倍率を表示するため AccuracyBars を渡す
                _components.Add(new DebugLabel(_trackerService, accuracyBars));

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

        // ─── Canvas構築 ────────────────────────────────────────────────────────────

        private void CreateDisplay()
        {
            // ゲームHUDのCanvasを参照して、位置・回転・スケールを合わせる
            var refCanvas = _hudController.GetComponent<Canvas>()
                            ?? _hudController.GetComponentInChildren<Canvas>(true);

            // 配置の基準。既定（FixedCanvasPlacement=true）は環境非依存の固定値を使う。
            // 設定でOFFにすると従来どおりHUD CanvasのTransform（位置・回転・スケール×3）に追従する。
            // HUDからは layer/sorting は常に借りて前面描画に合わせる。
            Vector3 refPos = FixedRefPos;
            Quaternion refRot = FixedRefRot;
            Vector3 canvasScale = Vector3.one * FixedCanvasScale;
            int layer = _hudController.gameObject.layer;

            if (refCanvas != null)
            {
                layer = refCanvas.gameObject.layer;

                if (!_config.FixedCanvasPlacement)
                {
                    // HUD追従モード：位置・回転・スケール(×3)をHUD Canvasからコピーする（環境でズレうる）。
                    refPos = refCanvas.transform.position;
                    refRot = refCanvas.transform.rotation;
                    canvasScale = refCanvas.transform.lossyScale * 3f;
                }

                // 参考: 見つけたHUD Canvasの実Transform（環境で変動しうる）。
                Plugin.DebugLog($"[LRCounter] RefCanvas='{refCanvas.name}' pos={refCanvas.transform.position} " +
                    $"rotEuler={refCanvas.transform.rotation.eulerAngles} lossyScale={refCanvas.transform.lossyScale} " +
                    $"fixedPlacement={_config.FixedCanvasPlacement}");
            }

            // ワールドスペースCanvasを生成
            _canvasObject = new GameObject("LRCounter_Canvas");
            _canvasObject.layer = layer;

            var canvas = _canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            _canvasObject.AddComponent<CurvedCanvasSettings>().SetRadius(0f); // 湾曲なし（平面）

            // 参照キャンバスと同じソーティングレイヤーを使い、3Dオブジェクトに隠れず前面描画させる
            if (refCanvas != null)
            {
                canvas.sortingLayerID = refCanvas.sortingLayerID;
                canvas.sortingOrder = refCanvas.sortingOrder + 1;
            }
            canvas.overrideSorting = true;

            Plugin.DebugLog($"[LRCounter] refPos={refPos}  fixedScale={FixedCanvasScale}  sortingLayer={canvas.sortingLayerName}");
            // Z はワールド座標でカメラ側へ DepthZ ぶん寄せて前面化する（傾きの影響を受けず高さは不変）。
            _canvasObject.transform.position = new Vector3(
                refPos.x,
                refPos.y + CanvasBaseYOffset,
                refPos.z - _config.DepthZ);
            _canvasObject.transform.rotation = refRot;
            _canvasObject.transform.localScale = canvasScale;

            // Canvasの論理サイズ（200×100）。anchorで割合指定するので実際の見た目に影響する
            var canvasRT = (RectTransform)_canvasObject.transform;
            canvasRT.sizeDelta = new Vector2(200f, 100f);

            // 各コンポーネントを構築（移譲）
            foreach (var c in _components) c.Build(canvasRT, layer);

            // フラッシュのフェードアウトは毎フレーム処理が要るため MonoBehaviour で TickFlash() を橋渡し
            _canvasObject.AddComponent<DisplayTicker>().Controller = this;

            ApplyVisibility(); // 設定の表示ON/OFFを反映
            UpdateDisplay();   // 初期表示
        }

        // 各要素の表示ON/OFFを設定に従って反映する（各コンポーネントが自分の設定フラグで判断する）
        private void ApplyVisibility()
        {
            foreach (var c in _components) c.ApplyVisibility();
        }

        // ─── 表示更新（ノーツを切るたびに呼ばれる） ────────────────────────────────

        private void UpdateDisplay()
        {
            if (!_config.Enabled) return;
            foreach (var c in _components) c.Update();
        }

        // DisplayTickerのUpdate()から毎フレーム呼ばれる。各コンポーネントへ移譲（不要なものは空実装）。
        internal void TickFlash()
        {
            foreach (var c in _components) c.TickFlash();
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
