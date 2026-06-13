using LRCounter.Configuration;
using LRCounter.Controllers;
using Zenject;

namespace LRCounter.Installers
{
    /// <summary>
    /// アプリ全体（App スコープ）に常駐するバインディングを登録するインストーラーです。
    /// ゲームプレイ(Player)とリザルト(Menu)でまたいで使う受け渡し用ストアと、
    /// ScoreSaberデータのセッションキャッシュをここに置きます。
    /// </summary>
    public class LRAppInstaller : Installer
    {
        private readonly PluginConfig _config;

        public LRAppInstaller(PluginConfig config)
        {
            _config = config;
        }

        public override void InstallBindings()
        {
            // 設定オブジェクト（App スコープのキャッシュからも参照する）
            Container.BindInstance(_config).AsSingle();

            // ScoreSaber API サービス（キャッシュ経由で使うため App スコープに置く）
            Container.Bind<ScoreSaberApiService>().AsSingle();

            // ScoreSaberデータのセッションキャッシュ。起動時にプレイヤーデータを1回だけ取得し、
            // マップ開始ごとのAPI呼び出しを無くす（レートリミット対策）
            Container.BindInterfacesAndSelfTo<PlayerDataCache>().AsSingle();

            // 直近プレイの左右結果を保持する共有ストア（Player/Menu 双方から参照される）
            Container.Bind<LRResultStore>().AsSingle();

            // 譜面ごとの左右ベスト精度を永続化するストア（リザルトの差分表示・自己ベスト更新に使う）
            Container.BindInterfacesAndSelfTo<HandAccuracyStore>().AsSingle();
        }
    }
}
