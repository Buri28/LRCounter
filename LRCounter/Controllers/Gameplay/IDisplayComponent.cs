using UnityEngine;

namespace LRCounter.Controllers.Display
{
    // 表示コンポーネント共通インターフェース。
    // コントローラーは各要素をこの1つの型でまとめて扱う（リストで一括 Build/Update/… できる＝拡張しやすい）。
    // 該当する処理が無いコンポーネントは空実装にする（例: ラベルの TickFlash）。
    internal interface IDisplayComponent
    {
        // Canvas配下にUIを生成する
        void Build(RectTransform canvasRT, int layer);

        // 表示内容を最新の状態に更新する（ノーツを切るたびに呼ばれる）
        void Update();

        // 自分の設定フラグに従って表示ON/OFFを反映する
        void ApplyVisibility();

        // 毎フレーム呼ばれる（フラッシュのフェード等。不要なコンポーネントは空実装）
        void TickFlash();
    }
}
