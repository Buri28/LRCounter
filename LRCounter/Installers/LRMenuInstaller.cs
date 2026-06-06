using LRCounter.Configuration;
using LRCounter.Controllers;
using Zenject;

namespace LRCounter.Installers
{
    /// <summary>
    /// メニューシーンに必要なバインディングを登録するインストーラーです。
    /// BSML 設定タブを追加します。
    /// </summary>
    public class LRMenuInstaller : Installer
    {
        private readonly PluginConfig _config;

        public LRMenuInstaller(PluginConfig config)
        {
            _config = config;
        }

        public override void InstallBindings()
        {
            // 設定オブジェクト
            Container.BindInstance(_config).AsSingle();

            // 設定画面コントローラー
            Container.Bind<LRSettingsController>()
                     .FromNewComponentAsViewController()
                     .AsSingle();

            // BSML の MOD 設定メニューに追加
            Container.BindInterfacesTo<LRMenuManager>()
                     .AsSingle()
                     .NonLazy();
        }
    }
}
