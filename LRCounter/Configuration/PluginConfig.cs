using IPA.Config.Stores;
using IPA.Config.Stores.Attributes;
using IPA.Config.Stores.Converters;
using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)]

namespace LRCounter.Configuration
{
    public class PluginConfig
    {
        public static PluginConfig Instance { get; set; } = null!;

        // MOD 有効/無効
        public virtual bool Enabled { get; set; } = true;

        // 表示する Star 評価の閾値（指定 Star 以上の譜面でのみ表示）
        public virtual float MinStarRating { get; set; } = 0f;

        // HUD からの X オフセット (ワールド座標・メートル単位)
        public virtual float PosX { get; set; } = 0f;

        // HUD からの Y オフセット (ワールド座標・メートル単位、負=下方向)
        public virtual float PosY { get; set; } = -0.5f;

        // HUD からの Z オフセット (ワールド座標・メートル単位、負=手前方向)
        public virtual float PosZ { get; set; } = -3f;

        // テキストサイズ (CountersPlus 準拠の canvas 単位、4 が標準)
        public virtual float TextSize { get; set; } = 4f;

        // 左手の表示カラー（16進文字列）
        public virtual string LeftHandColor { get; set; } = "#FF5555";

        // 右手の表示カラー（16進文字列）
        public virtual string RightHandColor { get; set; } = "#5555FF";

        // 合計PPも表示するか
        public virtual bool ShowTotalPP { get; set; } = true;

        // PPの小数点以下の桁数
        public virtual int DecimalPlaces { get; set; } = 2;

        // 手動設定のStar評価（0 = PP非表示、正の値 = そのStarでPP計算）
        public virtual float ManualStarRating { get; set; } = 0f;

        // Changed イベント（IPA が生成するストアのためのメソッド）
        public virtual void Changed() { }

        public virtual void OnReload() { }
    }
}
