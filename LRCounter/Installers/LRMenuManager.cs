using BeatSaberMarkupLanguage.GameplaySetup;
using LRCounter.Controllers.Settings;
using System;
using Zenject;

namespace LRCounter.Installers
{
    /// <summary>
    /// ソロ選曲画面の MODS タブに「LRCounter」を追加します。
    /// </summary>
    public class LRMenuManager : IInitializable, IDisposable
    {
        private readonly LRSettingsController _settingsController;

        public LRMenuManager(LRSettingsController settingsController)
        {
            _settingsController = settingsController;
        }

        public void Initialize()
        {
            GameplaySetup.Instance.AddTab("LRCounter", "LRCounter.Views.LRSettingsView.bsml", _settingsController);
            Plugin.Log.Info("MODS tab registered.");
        }

        public void Dispose()
        {
            GameplaySetup.Instance.RemoveTab("LRCounter");
        }
    }
}
