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
            set
            {
                _config.Enabled = value;
                _config.Changed();
            }
        }

        // ———— MinStarRating ————
        [UIValue("min-star")]
        public float MinStarRating
        {
            get => _config.MinStarRating;
            set
            {
                _config.MinStarRating = value;
                _config.Changed();
            }
        }

        // ———— TextSize ————
        [UIValue("text-size")]
        public float TextSize
        {
            get => _config.TextSize;
            set
            {
                _config.TextSize = value;
                _config.Changed();
            }
        }

        // ———— ShowTotalPP ————
        [UIValue("show-total")]
        public bool ShowTotalPP
        {
            get => _config.ShowTotalPP;
            set
            {
                _config.ShowTotalPP = value;
                _config.Changed();
            }
        }

        // ———— DecimalPlaces ————
        [UIValue("decimal-places")]
        public int DecimalPlaces
        {
            get => _config.DecimalPlaces;
            set
            {
                _config.DecimalPlaces = value;
                _config.Changed();
            }
        }

        // ———— Left Hand Color ————
        [UIValue("left-color")]
        public string LeftHandColor
        {
            get => _config.LeftHandColor;
            set
            {
                _config.LeftHandColor = value;
                _config.Changed();
            }
        }

        // ———— Right Hand Color ————
        [UIValue("right-color")]
        public string RightHandColor
        {
            get => _config.RightHandColor;
            set
            {
                _config.RightHandColor = value;
                _config.Changed();
            }
        }

        // ———— Position X ————
        [UIValue("pos-x")]
        public float PosX
        {
            get => _config.PosX;
            set
            {
                _config.PosX = value;
                _config.Changed();
            }
        }

        // ———— Position Y ————
        [UIValue("pos-y")]
        public float PosY
        {
            get => _config.PosY;
            set
            {
                _config.PosY = value;
                _config.Changed();
            }
        }

        // ———— Manual Star Rating ————
        [UIValue("manual-star")]
        public float ManualStarRating
        {
            get => _config.ManualStarRating;
            set
            {
                _config.ManualStarRating = value;
                _config.Changed();
            }
        }

        // ———— Reset Button ————
        [UIAction("reset-settings")]
        public void ResetSettings()
        {
            _config.Enabled = true;
            _config.MinStarRating = 0f;
            _config.TextSize = 4f;
            _config.ShowTotalPP = true;
            _config.DecimalPlaces = 2;
            _config.LeftHandColor = "#FF5555";
            _config.RightHandColor = "#5555FF";
            _config.PosX = 0f;
            _config.PosY = -300f;
            _config.PosZ = 0f;
            _config.ManualStarRating = 0f;
            _config.Changed();
            NotifyPropertyChanged(nameof(Enabled));
            NotifyPropertyChanged(nameof(MinStarRating));
            NotifyPropertyChanged(nameof(TextSize));
            NotifyPropertyChanged(nameof(ShowTotalPP));
            NotifyPropertyChanged(nameof(DecimalPlaces));
            NotifyPropertyChanged(nameof(LeftHandColor));
            NotifyPropertyChanged(nameof(RightHandColor));
            NotifyPropertyChanged(nameof(PosX));
            NotifyPropertyChanged(nameof(PosY));
            NotifyPropertyChanged(nameof(ManualStarRating));
            Plugin.Log.Info("Settings reset to defaults.");
        }
    }
}
