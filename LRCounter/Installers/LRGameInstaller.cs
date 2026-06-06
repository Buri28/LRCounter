using LRCounter.Configuration;
using LRCounter.Controllers;
using Zenject;

namespace LRCounter.Installers
{
    /// <summary>
    /// ゲームプレイシーン（プレイ中）に必要なバインディングを登録するインストーラーです。
    /// </summary>
    public class LRGameInstaller : Installer
    {
        private readonly PluginConfig _config;

        public LRGameInstaller(PluginConfig config)
        {
            _config = config;
        }

        public override void InstallBindings()
        {
            if (!_config.Enabled) return;

            // 設定オブジェクトをシーンに提供
            Container.BindInstance(_config).AsSingle();

            // ScoreSaber API サービス
            Container.Bind<ScoreSaberApiService>()
                     .AsSingle();

            // PP追跡サービス
            Container.BindInterfacesAndSelfTo<LRTrackerService>()
                     .AsSingle()
                     .NonLazy();

            // HUD 表示コントローラー
            Container.BindInterfacesAndSelfTo<LRDisplayController>()
                     .AsSingle()
                     .NonLazy();
        }
    }
}
