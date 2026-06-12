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

        // 開発用デバッグラベルを非表示にする（true=消す）
        public virtual bool HideDebugLabel { get; set; } = true;

        // テキストサイズ (CountersPlus 準拠の canvas 単位)
        public virtual float TextSize { get; set; } = 3f;

        // ─── 共有の奥行き ───────────────────────────────────────────────
        // Canvas全体をカメラ側へ寄せて前面化する距離(ワールド単位/メートル)。大きいほど手前。
        public virtual float DepthZ { get; set; } = -0.30f;

        // ─── 精度バー(外側)の配置（いずれもCanvas高さ/幅の比 0〜1） ───────
        public virtual bool ShowAccBar { get; set; } = true;      // 表示ON/OFF
        public virtual float AccBarSpacing { get; set; } = 0.34f;  // 中央(0.5)を起点とした左右バーの間隔
        public virtual float AccBarY { get; set; } = 0.33f;       // バー下端のY
        public virtual float AccBarHeight { get; set; } = 0.50f;  // バーの高さ
        public virtual float AccBarWidth { get; set; } = 0.01f;   // バーの幅
        public virtual int AccBarMin { get; set; } = 90;          // バー下端にマッピングする精度(%)。選択肢: 90/80/50/0（動的レンジOFF時のみ有効）
        public virtual bool AccBarDynamic { get; set; } = true;   // 動的レンジ: 10%幅の窓を精度に追従して上下スライドする（true=ON）

        // ─── 合算ラベル（左右合計の精度・PPを中央上部に表示） ───────────────
        public virtual bool ShowTotalLabel { get; set; } = true; // 表示ON/OFF
        public virtual float TotalLabelX { get; set; } = 0.5f;   // 中心X（Canvas幅比）
        public virtual float TotalLabelY { get; set; } = 0.90f;  // 下端Y（Canvas高さ比）大きいほど上
        public virtual float TotalLabelSize { get; set; } = 4.0f;  // フォントサイズ

        // ─── 11段階の精度バー色 ───────────────────────────
        // 各バンドの「原色（上端側）」を hex(#RRGGBB) で保持。下端側は自動で白寄りに淡くなる。
        // index 昇順＝精度の昇順。最後のグレーは満点(100%)時のみ適用。
        //   00 赤(0〜49%)      01 橙(50〜69%)   02 黄(70〜79%)
        //   03 緑(80〜89%)      04 青(90〜94%)   05 マゼンタ(95%)
        //   06 シアン(96%)      07 水色(97%)     08 肌色(98%)
        //   09 紫(99%)      10 グレー(100%・満点のみ)
        public virtual string Color00 { get; set; } = "#ff0000";
        public virtual string Color01 { get; set; } = "#ff7f00";
        public virtual string Color02 { get; set; } = "#ffff00";
        public virtual string Color03 { get; set; } = "#00ff00";
        public virtual string Color04 { get; set; } = "#0000ff";
        public virtual string Color05 { get; set; } = "#ff00dd";
        public virtual string Color06 { get; set; } = "#1AE6E6";
        public virtual string Color07 { get; set; } = "#007fff";
        public virtual string Color08 { get; set; } = "#fcb880";
        public virtual string Color09 { get; set; } = "#a600ff";
        public virtual string Color10 { get; set; } = "#E6E6E6";

        // ─── 閾値の枠（ボーダー）の色 ───────────────────────────
        // バー外周を縁取る枠の色。合算PPが各ラインを超えたときに点灯する。
        //   BorderColorScoreUpdate … 自己ベストPP超え（スコア更新）
        //   BorderColorPP          … 取得ライン(ThresholdPP)超え（PP取得）。スコア更新より優先。
        public virtual string BorderColorScoreUpdate { get; set; } = "#ffff00";
        public virtual string BorderColorPP { get; set; } = "#ffffff";

        // Changed イベント（IPA が生成するストアのためのメソッド）
        public virtual void Changed() { }

        public virtual void OnReload() { }
    }
}
