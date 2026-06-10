using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using LRCounter.Configuration;
using LRCounter.Controllers.Display;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

namespace LRCounter.Controllers
{
    /// <summary>
    /// ソロメニューのMODSタブに表示する設定画面のコントローラーです。
    /// BeatSaberMarkupLanguage（BSML）を使用します。
    /// </summary>
    [ViewDefinition("LRCounter.Views.LRSettingsView.bsml")]
    [HotReload(RelativePathToLayout = @"..\Views\LRSettingsView.bsml")]
    public class LRSettingsController : BSMLAutomaticViewController
    {
        private PluginConfig _config = null!;

        [Inject]
        public void Construct(PluginConfig config)
        {
            _config = config;
        }

        // ———— Enabled ————
        [UIValue("enabled")]
        public bool Enabled
        {
            get => _config.Enabled;
            set { _config.Enabled = value; _config.Changed(); }
        }

        // ———— TextSize ————
        [UIValue("text-size")]
        public float TextSize
        {
            get => _config.TextSize;
            set { _config.TextSize = value; _config.Changed(); }
        }

        // ———— Show toggles (per element) ————
        [UIValue("show-total-label")]
        public bool ShowTotalLabel
        {
            get => _config.ShowTotalLabel;
            set { _config.ShowTotalLabel = value; _config.Changed(); }
        }

        [UIValue("show-acc-bar")]
        public bool ShowAccBar
        {
            get => _config.ShowAccBar;
            set { _config.ShowAccBar = value; _config.Changed(); }
        }

        // ———— Shared depth ————
        [UIValue("depth-z")]
        public float DepthZ
        {
            get => _config.DepthZ;
            set { _config.DepthZ = value; _config.Changed(); }
        }

        // ———— Accuracy bar layout ————
        [UIValue("acc-spacing")]
        public float AccBarSpacing
        {
            get => _config.AccBarSpacing;
            set { _config.AccBarSpacing = value; _config.Changed(); }
        }

        [UIValue("acc-y")]
        public float AccBarY
        {
            get => _config.AccBarY;
            set { _config.AccBarY = value; _config.Changed(); }
        }

        [UIValue("acc-height")]
        public float AccBarHeight
        {
            get => _config.AccBarHeight;
            set { _config.AccBarHeight = value; _config.Changed(); }
        }

        [UIValue("acc-width")]
        public float AccBarWidth
        {
            get => _config.AccBarWidth;
            set { _config.AccBarWidth = value; _config.Changed(); }
        }

        // バー下端にマッピングする精度(%)。左右の矢印で 90/80/50/0 を切り替える。
        [UIValue("acc-min-options")]
        public List<object> AccBarMinOptions { get; } = new List<object> { 0, 50, 80, 90 };

        [UIValue("acc-min")]
        public int AccBarMin
        {
            get => _config.AccBarMin;
            set { _config.AccBarMin = value; _config.Changed(); }
        }

        // ———— Total label (combined %/PP) ————
        [UIValue("total-x")]
        public float TotalLabelX
        {
            get => _config.TotalLabelX;
            set { _config.TotalLabelX = value; _config.Changed(); }
        }

        [UIValue("total-y")]
        public float TotalLabelY
        {
            get => _config.TotalLabelY;
            set { _config.TotalLabelY = value; _config.Changed(); }
        }

        [UIValue("total-size")]
        public float TotalLabelSize
        {
            get => _config.TotalLabelSize;
            set { _config.TotalLabelSize = value; _config.Changed(); }
        }

        // ———— 11段階の精度バー色 ————
        // hex文字列で保持する設定値を Color として出し入れする（color-setting 用）。
        [UIValue("color-00")]
        public Color Color00
        {
            get => LRDisplayCommon.ParseHex(_config.Color00);
            set { _config.Color00 = LRDisplayCommon.ToHex(value); _config.Changed(); }
        }

        [UIValue("color-01")]
        public Color Color01
        {
            get => LRDisplayCommon.ParseHex(_config.Color01);
            set { _config.Color01 = LRDisplayCommon.ToHex(value); _config.Changed(); }
        }

        [UIValue("color-02")]
        public Color Color02
        {
            get => LRDisplayCommon.ParseHex(_config.Color02);
            set { _config.Color02 = LRDisplayCommon.ToHex(value); _config.Changed(); }
        }

        [UIValue("color-03")]
        public Color Color03
        {
            get => LRDisplayCommon.ParseHex(_config.Color03);
            set { _config.Color03 = LRDisplayCommon.ToHex(value); _config.Changed(); }
        }

        [UIValue("color-04")]
        public Color Color04
        {
            get => LRDisplayCommon.ParseHex(_config.Color04);
            set { _config.Color04 = LRDisplayCommon.ToHex(value); _config.Changed(); }
        }

        [UIValue("color-05")]
        public Color Color05
        {
            get => LRDisplayCommon.ParseHex(_config.Color05);
            set { _config.Color05 = LRDisplayCommon.ToHex(value); _config.Changed(); }
        }

        [UIValue("color-06")]
        public Color Color06
        {
            get => LRDisplayCommon.ParseHex(_config.Color06);
            set { _config.Color06 = LRDisplayCommon.ToHex(value); _config.Changed(); }
        }

        [UIValue("color-07")]
        public Color Color07
        {
            get => LRDisplayCommon.ParseHex(_config.Color07);
            set { _config.Color07 = LRDisplayCommon.ToHex(value); _config.Changed(); }
        }

        [UIValue("color-08")]
        public Color Color08
        {
            get => LRDisplayCommon.ParseHex(_config.Color08);
            set { _config.Color08 = LRDisplayCommon.ToHex(value); _config.Changed(); }
        }

        [UIValue("color-09")]
        public Color Color09
        {
            get => LRDisplayCommon.ParseHex(_config.Color09);
            set { _config.Color09 = LRDisplayCommon.ToHex(value); _config.Changed(); }
        }

        [UIValue("color-10")]
        public Color Color10
        {
            get => LRDisplayCommon.ParseHex(_config.Color10);
            set { _config.Color10 = LRDisplayCommon.ToHex(value); _config.Changed(); }
        }

        // ———— Reset Button ————
        [UIAction("reset-settings")]
        public void ResetSettings()
        {
            // PluginConfig のフィールド初期化子（デフォルト値）を単一の正として使う。
            // ここに値を直書きすると PluginConfig とズレてリセットが効かなくなるため、
            // 既定値のインスタンスから読み戻す。
            var d = new PluginConfig();

            _config.Enabled = d.Enabled;
            _config.TextSize = d.TextSize;
            _config.ShowTotalLabel = d.ShowTotalLabel;
            _config.ShowAccBar = d.ShowAccBar;
            _config.DepthZ = d.DepthZ;
            _config.AccBarSpacing = d.AccBarSpacing;
            _config.AccBarY = d.AccBarY;
            _config.AccBarHeight = d.AccBarHeight;
            _config.AccBarWidth = d.AccBarWidth;
            _config.AccBarMin = d.AccBarMin;
            _config.TotalLabelX = d.TotalLabelX;
            _config.TotalLabelY = d.TotalLabelY;
            _config.TotalLabelSize = d.TotalLabelSize;
            _config.Color00 = d.Color00;
            _config.Color01 = d.Color01;
            _config.Color02 = d.Color02;
            _config.Color03 = d.Color03;
            _config.Color04 = d.Color04;
            _config.Color05 = d.Color05;
            _config.Color06 = d.Color06;
            _config.Color07 = d.Color07;
            _config.Color08 = d.Color08;
            _config.Color09 = d.Color09;
            _config.Color10 = d.Color10;
            _config.Changed();

            NotifyPropertyChanged(nameof(Enabled));
            NotifyPropertyChanged(nameof(TextSize));
            NotifyPropertyChanged(nameof(ShowTotalLabel));
            NotifyPropertyChanged(nameof(ShowAccBar));
            NotifyPropertyChanged(nameof(DepthZ));
            NotifyPropertyChanged(nameof(AccBarSpacing));
            NotifyPropertyChanged(nameof(AccBarY));
            NotifyPropertyChanged(nameof(AccBarHeight));
            NotifyPropertyChanged(nameof(AccBarWidth));
            NotifyPropertyChanged(nameof(AccBarMin));
            NotifyPropertyChanged(nameof(TotalLabelX));
            NotifyPropertyChanged(nameof(TotalLabelY));
            NotifyPropertyChanged(nameof(TotalLabelSize));
            NotifyPropertyChanged(nameof(Color00));
            NotifyPropertyChanged(nameof(Color01));
            NotifyPropertyChanged(nameof(Color02));
            NotifyPropertyChanged(nameof(Color03));
            NotifyPropertyChanged(nameof(Color04));
            NotifyPropertyChanged(nameof(Color05));
            NotifyPropertyChanged(nameof(Color06));
            NotifyPropertyChanged(nameof(Color07));
            NotifyPropertyChanged(nameof(Color08));
            NotifyPropertyChanged(nameof(Color09));
            NotifyPropertyChanged(nameof(Color10));
            Plugin.Log.Info("Settings reset to defaults.");
        }
    }
}
