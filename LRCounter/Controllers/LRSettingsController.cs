using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using LRCounter.Configuration;
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
        public List<object> AccBarMinOptions { get; } = new List<object> { 90, 80, 50, 0 };

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
        public List<object> ScoreBarMinOptions { get; } = new List<object> { 110, 105 };

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
            Plugin.Log.Info("Settings reset to defaults.");
        }
    }
}
