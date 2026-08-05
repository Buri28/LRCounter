using BeatSaberMarkupLanguage.GameplaySetup;
using LRCounter.Configuration;
using LRCounter.Controllers.Gameplay;
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
        private readonly PluginConfig _config;

        public LRMenuManager(LRSettingsController settingsController, PluginConfig config)
        {
            _settingsController = settingsController;
            _config = config;
        }

        public void Initialize()
        {
            GameplaySetup.Instance.AddTab("LRCounter", "LRCounter.Views.LRSettingsView.bsml", _settingsController);

            // 精度低下サウンドのクリップをメニュー到達時に用意しておく（プレイ画面に入る前に済ませる）。
            // ビープ生成は軽く、カスタム音の読み込みは非同期なので、ここでフレームが飛ぶことはない。
            new DropSoundPlayer(_config).Prewarm();
        }

        public void Dispose()
        {
            GameplaySetup.Instance.RemoveTab("LRCounter");
        }
    }
}
