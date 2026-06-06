using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using LRCounter.Configuration;
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

        // ———— MinStarRating ————
        [UIValue("min-star")]
        public float MinStarRating
        {
            get => _config.MinStarRating;
            set { _config.MinStarRating = value; _config.Changed(); }
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

        // ———— DecimalPlaces ————
        [UIValue("decimal-places")]
        public int DecimalPlaces
        {
            get => _config.DecimalPlaces;
            set { _config.DecimalPlaces = value; _config.Changed(); }
        }

        // ———— Left Hand Color ————
        [UIValue("left-color")]
        public string LeftHandColor
        {
            get => _config.LeftHandColor;
            set { _config.LeftHandColor = value; _config.Changed(); }
        }

        // ———— Right Hand Color ————
        [UIValue("right-color")]
        public string RightHandColor
        {
            get => _config.RightHandColor;
            set { _config.RightHandColor = value; _config.Changed(); }
        }

        // ———— Manual Star Rating ————
        [UIValue("manual-star")]
        public float ManualStarRating
        {
            get => _config.ManualStarRating;
            set { _config.ManualStarRating = value; _config.Changed(); }
        }

        // ———— Shared depth ————
        [UIValue("depth-z")]
        public float DepthZ
        {
            get => _config.DepthZ;
            set { _config.DepthZ = value; _config.Changed(); }
        }

        // ———— Accuracy bar layout ————
        [UIValue("acc-left-x")]
        public float AccBarLeftX
        {
            get => _config.AccBarLeftX;
            set { _config.AccBarLeftX = value; _config.Changed(); }
        }

        [UIValue("acc-right-x")]
        public float AccBarRightX
        {
            get => _config.AccBarRightX;
            set { _config.AccBarRightX = value; _config.Changed(); }
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

        // ———— Score bar layout ————
        [UIValue("score-left-x")]
        public float ScoreBarLeftX
        {
            get => _config.ScoreBarLeftX;
            set { _config.ScoreBarLeftX = value; _config.Changed(); }
        }

        [UIValue("score-right-x")]
        public float ScoreBarRightX
        {
            get => _config.ScoreBarRightX;
            set { _config.ScoreBarRightX = value; _config.Changed(); }
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
            _config.Enabled = true;
            _config.MinStarRating = 0f;
            _config.TextSize = 3f;
            _config.ShowTotalLabel = true;
            _config.ShowAccBar = true;
            _config.ShowScoreBar = true;
            _config.DecimalPlaces = 2;
            _config.LeftHandColor = "#FF5555";
            _config.RightHandColor = "#5555FF";
            _config.ManualStarRating = 0f;
            _config.DepthZ = 3f;
            _config.AccBarLeftX = 0.385f;
            _config.AccBarRightX = 0.615f;
            _config.AccBarY = 0.35f;
            _config.AccBarHeight = 0.50f;
            _config.AccBarWidth = 0.01f;
            _config.ScoreBarLeftX = 0.405f;
            _config.ScoreBarRightX = 0.595f;
            _config.ScoreBarY = 0.45f;
            _config.ScoreBarHeight = 0.27f;
            _config.ScoreBarWidth = 0.01f;
            _config.TotalLabelX = 0.5f;
            _config.TotalLabelY = 0.80f;
            _config.TotalLabelSize = 4.0f;
            _config.Changed();

            NotifyPropertyChanged(nameof(Enabled));
            NotifyPropertyChanged(nameof(MinStarRating));
            NotifyPropertyChanged(nameof(TextSize));
            NotifyPropertyChanged(nameof(ShowTotalLabel));
            NotifyPropertyChanged(nameof(ShowAccBar));
            NotifyPropertyChanged(nameof(ShowScoreBar));
            NotifyPropertyChanged(nameof(DecimalPlaces));
            NotifyPropertyChanged(nameof(LeftHandColor));
            NotifyPropertyChanged(nameof(RightHandColor));
            NotifyPropertyChanged(nameof(ManualStarRating));
            NotifyPropertyChanged(nameof(DepthZ));
            NotifyPropertyChanged(nameof(AccBarLeftX));
            NotifyPropertyChanged(nameof(AccBarRightX));
            NotifyPropertyChanged(nameof(AccBarY));
            NotifyPropertyChanged(nameof(AccBarHeight));
            NotifyPropertyChanged(nameof(AccBarWidth));
            NotifyPropertyChanged(nameof(ScoreBarLeftX));
            NotifyPropertyChanged(nameof(ScoreBarRightX));
            NotifyPropertyChanged(nameof(ScoreBarY));
            NotifyPropertyChanged(nameof(ScoreBarHeight));
            NotifyPropertyChanged(nameof(ScoreBarWidth));
            NotifyPropertyChanged(nameof(TotalLabelX));
            NotifyPropertyChanged(nameof(TotalLabelY));
            NotifyPropertyChanged(nameof(TotalLabelSize));
            Plugin.Log.Info("Settings reset to defaults.");
        }
    }
}
