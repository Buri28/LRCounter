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

        [UIValue("show-score-bar")]
        public bool ShowScoreBar
        {
            get => _config.ShowScoreBar;
            set { _config.ShowScoreBar = value; _config.Changed(); }
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

        // ———— Score bar layout ————
        [UIValue("score-spacing")]
        public float ScoreBarSpacing
        {
            get => _config.ScoreBarSpacing;
            set { _config.ScoreBarSpacing = value; _config.Changed(); }
        }

        [UIValue("score-y")]
        public float ScoreBarY
        {
            get => _config.ScoreBarY;
            set { _config.ScoreBarY = value; _config.Changed(); }
        }

        [UIValue("score-height")]
        public float ScoreBarHeight
        {
            get => _config.ScoreBarHeight;
            set { _config.ScoreBarHeight = value; _config.Changed(); }
        }

        [UIValue("score-width")]
        public float ScoreBarWidth
        {
            get => _config.ScoreBarWidth;
            set { _config.ScoreBarWidth = value; _config.Changed(); }
        }

        // バー下端にマッピングする平均点。左右の矢印で 110/105 を切り替える。
        [UIValue("score-min-options")]
        public List<object> ScoreBarMinOptions { get; } = new List<object> { 105, 110 };

        [UIValue("score-min")]
        public int ScoreBarMin
        {
            get => _config.ScoreBarMin;
            set { _config.ScoreBarMin = value; _config.Changed(); }
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

        // ———— 11段階バー色（精度バー・点数バー共通） ————
        // hex文字列で保持する設定値を Color として出し入れする（color-setting 用）。
        [UIValue("color-red")]
        public Color Color00Red
        {
            get => LRDisplayCommon.ParseHex(_config.Color00Red);
            set { _config.Color00Red = LRDisplayCommon.ToHex(value); _config.Changed(); }
        }

        [UIValue("color-orange")]
        public Color Color01Orange
        {
            get => LRDisplayCommon.ParseHex(_config.Color01Orange);
            set { _config.Color01Orange = LRDisplayCommon.ToHex(value); _config.Changed(); }
        }

        [UIValue("color-yellow")]
        public Color Color02Yellow
        {
            get => LRDisplayCommon.ParseHex(_config.Color02Yellow);
            set { _config.Color02Yellow = LRDisplayCommon.ToHex(value); _config.Changed(); }
        }

        [UIValue("color-green")]
        public Color Color03Green
        {
            get => LRDisplayCommon.ParseHex(_config.Color03Green);
            set { _config.Color03Green = LRDisplayCommon.ToHex(value); _config.Changed(); }
        }

        [UIValue("color-blue")]
        public Color Color04Blue
        {
            get => LRDisplayCommon.ParseHex(_config.Color04Blue);
            set { _config.Color04Blue = LRDisplayCommon.ToHex(value); _config.Changed(); }
        }

        [UIValue("color-magenta")]
        public Color Color05Magenta
        {
            get => LRDisplayCommon.ParseHex(_config.Color05Magenta);
            set { _config.Color05Magenta = LRDisplayCommon.ToHex(value); _config.Changed(); }
        }

        [UIValue("color-cyan")]
        public Color Color06Cyan
        {
            get => LRDisplayCommon.ParseHex(_config.Color06Cyan);
            set { _config.Color06Cyan = LRDisplayCommon.ToHex(value); _config.Changed(); }
        }

        [UIValue("color-lightblue")]
        public Color Color07LightBlue
        {
            get => LRDisplayCommon.ParseHex(_config.Color07LightBlue);
            set { _config.Color07LightBlue = LRDisplayCommon.ToHex(value); _config.Changed(); }
        }

        [UIValue("color-skin")]
        public Color Color08Skin
        {
            get => LRDisplayCommon.ParseHex(_config.Color08Skin);
            set { _config.Color08Skin = LRDisplayCommon.ToHex(value); _config.Changed(); }
        }

        [UIValue("color-purple")]
        public Color Color09Purple
        {
            get => LRDisplayCommon.ParseHex(_config.Color09Purple);
            set { _config.Color09Purple = LRDisplayCommon.ToHex(value); _config.Changed(); }
        }

        [UIValue("color-grey")]
        public Color Color10Grey
        {
            get => LRDisplayCommon.ParseHex(_config.Color10Grey);
            set { _config.Color10Grey = LRDisplayCommon.ToHex(value); _config.Changed(); }
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
            _config.ShowScoreBar = d.ShowScoreBar;
            _config.DepthZ = d.DepthZ;
            _config.AccBarSpacing = d.AccBarSpacing;
            _config.AccBarY = d.AccBarY;
            _config.AccBarHeight = d.AccBarHeight;
            _config.AccBarWidth = d.AccBarWidth;
            _config.AccBarMin = d.AccBarMin;
            _config.ScoreBarSpacing = d.ScoreBarSpacing;
            _config.ScoreBarY = d.ScoreBarY;
            _config.ScoreBarHeight = d.ScoreBarHeight;
            _config.ScoreBarWidth = d.ScoreBarWidth;
            _config.ScoreBarMin = d.ScoreBarMin;
            _config.TotalLabelX = d.TotalLabelX;
            _config.TotalLabelY = d.TotalLabelY;
            _config.TotalLabelSize = d.TotalLabelSize;
            _config.Color00Red = d.Color00Red;
            _config.Color01Orange = d.Color01Orange;
            _config.Color02Yellow = d.Color02Yellow;
            _config.Color03Green = d.Color03Green;
            _config.Color04Blue = d.Color04Blue;
            _config.Color05Magenta = d.Color05Magenta;
            _config.Color06Cyan = d.Color06Cyan;
            _config.Color07LightBlue = d.Color07LightBlue;
            _config.Color08Skin = d.Color08Skin;
            _config.Color09Purple = d.Color09Purple;
            _config.Color10Grey = d.Color10Grey;
            _config.Changed();

            NotifyPropertyChanged(nameof(Enabled));
            NotifyPropertyChanged(nameof(TextSize));
            NotifyPropertyChanged(nameof(ShowTotalLabel));
            NotifyPropertyChanged(nameof(ShowAccBar));
            NotifyPropertyChanged(nameof(ShowScoreBar));
            NotifyPropertyChanged(nameof(DepthZ));
            NotifyPropertyChanged(nameof(AccBarSpacing));
            NotifyPropertyChanged(nameof(AccBarY));
            NotifyPropertyChanged(nameof(AccBarHeight));
            NotifyPropertyChanged(nameof(AccBarWidth));
            NotifyPropertyChanged(nameof(AccBarMin));
            NotifyPropertyChanged(nameof(ScoreBarSpacing));
            NotifyPropertyChanged(nameof(ScoreBarY));
            NotifyPropertyChanged(nameof(ScoreBarHeight));
            NotifyPropertyChanged(nameof(ScoreBarWidth));
            NotifyPropertyChanged(nameof(ScoreBarMin));
            NotifyPropertyChanged(nameof(TotalLabelX));
            NotifyPropertyChanged(nameof(TotalLabelY));
            NotifyPropertyChanged(nameof(TotalLabelSize));
            NotifyPropertyChanged(nameof(Color00Red));
            NotifyPropertyChanged(nameof(Color01Orange));
            NotifyPropertyChanged(nameof(Color02Yellow));
            NotifyPropertyChanged(nameof(Color03Green));
            NotifyPropertyChanged(nameof(Color04Blue));
            NotifyPropertyChanged(nameof(Color05Magenta));
            NotifyPropertyChanged(nameof(Color06Cyan));
            NotifyPropertyChanged(nameof(Color07LightBlue));
            NotifyPropertyChanged(nameof(Color08Skin));
            NotifyPropertyChanged(nameof(Color09Purple));
            NotifyPropertyChanged(nameof(Color10Grey));
            Plugin.Log.Info("Settings reset to defaults.");
        }
    }
}
