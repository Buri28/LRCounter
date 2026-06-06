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

        // テキストサイズ (CountersPlus 準拠の canvas 単位)
        public virtual float TextSize { get; set; } = 3f;

        // 左手の表示カラー（16進文字列）
        public virtual string LeftHandColor { get; set; } = "#FF5555";

        // 右手の表示カラー（16進文字列）
        public virtual string RightHandColor { get; set; } = "#5555FF";

        // PPの小数点以下の桁数
        public virtual int DecimalPlaces { get; set; } = 2;

        // 手動設定のStar評価（0 = PP非表示、正の値 = そのStarでPP計算）
        public virtual float ManualStarRating { get; set; } = 0f;

        // ─── 共有の奥行き ───────────────────────────────────────────────
        // Canvas全体をカメラ側へ寄せて前面化する距離(ワールド単位/メートル)。大きいほど手前。
        public virtual float DepthZ { get; set; } = 3f;

        // ─── 精度バー(外側)の配置（いずれもCanvas高さ/幅の比 0〜1） ───────
        public virtual bool ShowAccBar { get; set; } = true;      // 表示ON/OFF
        public virtual float AccBarLeftX { get; set; } = 0.385f;  // 左バーの中心X
        public virtual float AccBarRightX { get; set; } = 0.615f; // 右バーの中心X
        public virtual float AccBarY { get; set; } = 0.35f;       // バー下端のY
        public virtual float AccBarHeight { get; set; } = 0.50f;  // バーの高さ
        public virtual float AccBarWidth { get; set; } = 0.01f;   // バーの幅

        // ─── 平均点数バー(内側)の配置（いずれもCanvas比 0〜1） ──────────────
        public virtual bool ShowScoreBar { get; set; } = true;    // 表示ON/OFF
        public virtual float ScoreBarLeftX { get; set; } = 0.405f;
        public virtual float ScoreBarRightX { get; set; } = 0.595f;
        public virtual float ScoreBarY { get; set; } = 0.45f;
        public virtual float ScoreBarHeight { get; set; } = 0.27f;
        public virtual float ScoreBarWidth { get; set; } = 0.01f;

        // ─── 合算ラベル（左右合計の精度・PPを中央上部に表示） ───────────────
        public virtual bool ShowTotalLabel { get; set; } = true; // 表示ON/OFF
        public virtual float TotalLabelX { get; set; } = 0.5f;   // 中心X（Canvas幅比）
        public virtual float TotalLabelY { get; set; } = 0.80f;  // 下端Y（Canvas高さ比）
        public virtual float TotalLabelSize { get; set; } = 4.0f;  // フォントサイズ

        // Changed イベント（IPA が生成するストアのためのメソッド）
        public virtual void Changed() { }

        public virtual void OnReload() { }
    }
}
