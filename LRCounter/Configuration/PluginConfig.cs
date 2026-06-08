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

        // テキストサイズ (CountersPlus 準拠の canvas 単位)
        public virtual float TextSize { get; set; } = 3f;

        // ─── 共有の奥行き ───────────────────────────────────────────────
        // Canvas全体をカメラ側へ寄せて前面化する距離(ワールド単位/メートル)。大きいほど手前。
        public virtual float DepthZ { get; set; } = 0.80f;

        // ─── 精度バー(外側)の配置（いずれもCanvas高さ/幅の比 0〜1） ───────
        public virtual bool ShowAccBar { get; set; } = true;      // 表示ON/OFF
        public virtual float AccBarSpacing { get; set; } = 0.34f;  // 中央(0.5)を起点とした左右バーの間隔
        public virtual float AccBarY { get; set; } = 0.35f;       // バー下端のY
        public virtual float AccBarHeight { get; set; } = 0.50f;  // バーの高さ
        public virtual float AccBarWidth { get; set; } = 0.01f;   // バーの幅
        public virtual int AccBarMin { get; set; } = 90;          // バー下端にマッピングする精度(%)。選択肢: 90/80/50/0

        // ─── 平均点数バー(内側)の配置（いずれもCanvas比 0〜1） ──────────────
        public virtual bool ShowScoreBar { get; set; } = true;    // 表示ON/OFF
        public virtual float ScoreBarSpacing { get; set; } = 0.30f; // 中央(0.5)を起点とした左右バーの間隔
        public virtual float ScoreBarY { get; set; } = 0.45f;
        public virtual float ScoreBarHeight { get; set; } = 0.27f;
        public virtual float ScoreBarWidth { get; set; } = 0.01f;
        public virtual int ScoreBarMin { get; set; } = 110;       // バー下端にマッピングする平均点。選択肢: 110/105

        // ─── 合算ラベル（左右合計の精度・PPを中央上部に表示） ───────────────
        public virtual bool ShowTotalLabel { get; set; } = true; // 表示ON/OFF
        public virtual float TotalLabelX { get; set; } = 0.5f;   // 中心X（Canvas幅比）
        public virtual float TotalLabelY { get; set; } = 0.90f;  // 下端Y（Canvas高さ比）大きいほど上
        public virtual float TotalLabelSize { get; set; } = 4.0f;  // フォントサイズ

        // Changed イベント（IPA が生成するストアのためのメソッド）
        public virtual void Changed() { }

        public virtual void OnReload() { }
    }
}
