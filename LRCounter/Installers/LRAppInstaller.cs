using LRCounter.Controllers;
using Zenject;

namespace LRCounter.Installers
{
    /// <summary>
    /// アプリ全体（App スコープ）に常駐するバインディングを登録するインストーラーです。
    /// ゲームプレイ(Player)とリザルト(Menu)でまたいで使う受け渡し用ストアをここに置きます。
    /// </summary>
    public class LRAppInstaller : Installer
    {
        public override void InstallBindings()
        {
            // 直近プレイの左右結果を保持する共有ストア（Player/Menu 双方から参照される）
            Container.Bind<LRResultStore>().AsSingle();
        }
    }
}
