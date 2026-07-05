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

        // 詳細ログ出力フラグ。true のときだけ Debug レベルの診断ログを出す（既定OFF＝通常はWarn/Errorのみ）。
        // Plugin.DebugLog() がこのフラグを参照する。LRCounter.json で切り替える。
        public virtual bool DebugLogging { get; set; } = false;

        // テキストサイズ (CountersPlus 準拠の canvas 単位)
        public virtual float TextSize { get; set; } = 3f;

        // ─── 共有の奥行き ───────────────────────────────────────────────
        // Canvas全体をカメラ側へ寄せて前面化する距離(ワールド単位/メートル)。大きいほど手前。
        public virtual float DepthZ { get; set; } = -0.30f;

        // 表示の位置・スケールを環境非依存の固定にするか。
        //   true (既定) … 環境（Environment）に関係なく固定位置・固定スケール。環境オーバーライド譜面でもズレない。
        //   false       … 従来どおりゲームHUDの位置・スケールに追従（HUDが動く環境では一緒にズレる）。
        public virtual bool FixedCanvasPlacement { get; set; } = true;

        // ─── 精度バー(外側)の配置（いずれもCanvas高さ/幅の比 0〜1） ───────
        public virtual bool ShowAccBar { get; set; } = true;      // 表示ON/OFF
        public virtual float AccBarSpacing { get; set; } = 0.34f;  // 中央(0.5)を起点とした左右バーの間隔
        public virtual float AccBarY { get; set; } = 0.35f;       // バー下端のY
        public virtual float AccBarHeight { get; set; } = 0.50f;  // バーの高さ
        public virtual float AccBarWidth { get; set; } = 0.01f;   // バーの幅
        public virtual int AccBarMin { get; set; } = 90;          // バー下端にマッピングする精度(%)。選択肢: 90/80/50/0。動的レンジON時は窓幅=(100-この値)になり、その幅でスライドする
        public virtual bool AccBarDynamic { get; set; } = true;   // 動的レンジ: 幅(100-AccBarMin)%の窓を精度に追従して上下スライドする（true=ON）
        public virtual bool ShowHandBestLabel { get; set; } = true; // バー下に「この手の自己ベスト精度」を半透明で表示する（true=ON）
        public virtual float HandBestLabelSize { get; set; } = 2.5f;  // バー下の片手ベスト精度ラベルのフォントサイズ（バー上のラベルとは独立）
        public virtual bool ShowBarAccuracyLabel { get; set; } = true; // バー上の精度(%)ラベルを表示する（true=ON）
        public virtual bool ShowBarPPLabel { get; set; } = true;       // バー上のPPラベルを表示する（true=ON）
        public virtual float FlashDuration { get; set; } = 0.6f;      // ノーツヒット時のフラッシュが消えるまでの秒数

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
        // バー外周を縁取る枠の色。優先度: PP取得(白) ＞ 両手スコア更新(黄) ＞ 片手ベスト更新(橙)。
        //   BorderColorPP          … 取得ライン(ThresholdPP)超え（PP取得）。最優先・両手同時。
        //   BorderColorScoreUpdate … 両手(合算)の自己ベスト精度更新。両手同時。
        //   BorderColorHandBest    … その手の自己ベスト精度更新。その手だけ点灯。最下位。
        public virtual string BorderColorScoreUpdate { get; set; } = "#ffff00";
        public virtual string BorderColorPP { get; set; } = "#ffffff";
        public virtual string BorderColorHandBest { get; set; } = "#ff7f00";

        // Changed イベント（IPA が生成するストアのためのメソッド）
        public virtual void Changed() { }

        public virtual void OnReload() { }
    }
}
