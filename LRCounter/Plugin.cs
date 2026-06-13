using HarmonyLib;
using IPA;
using IPA.Config;
using IPA.Config.Stores;
using IPA.Loader;
using LRCounter.Configuration;
using LRCounter.Installers;
using SiraUtil.Zenject;
using System.Reflection;
using IPALogger = IPA.Logging.Logger;

namespace LRCounter
{
    // IPAプラグインのエントリーポイント。BeatSaber起動時に1回だけ初期化される
    [Plugin(RuntimeOptions.SingleStartInit)]
    public class Plugin
    {
        // どこからでもログを書けるようにstaticで公開
        internal static IPALogger Log { get; private set; } = null!;
        // Harmonyパッチ（現在は未使用だが将来のパッチ用に確保）
        internal static Harmony HarmonyInstance { get; private set; } = null!;
        // 設定ファイル（UserData/LRCounter.json）へのアクセス
        internal static PluginConfig Config { get; private set; } = null!;

        // 詳細ログ。Config.DebugLogging が ON のときだけ Debug レベルで出力する（既定OFF）。
        // Warn/Error は従来どおり Plugin.Log.Warn/Error で常時出力する。
        internal static void DebugLog(string message)
        {
            if (Config != null && Config.DebugLogging)
                Log.Debug(message);
        }

        // IPAによってBeatSaber起動時に1回呼ばれるコンストラクタ
        [Init]
        public Plugin(IPALogger logger, Config conf, Zenjector zenjector)
        {
            Log            = logger;
            Config         = conf.Generated<PluginConfig>(); // IPAが設定ファイルを自動生成・ロード
            PluginConfig.Instance = Config;                  // 静的アクセス用（LRDisplayCommon のバー色取得など）
            HarmonyInstance = new Harmony("com.buri28.lrcounter");

            zenjector.UseLogger(logger);
            zenjector.UseHttpService();
            zenjector.UseMetadataBinder<Plugin>();

            // アプリ全体に常駐するストア・ScoreSaberキャッシュを登録（Player/Menuでまたいで使う）
            zenjector.Install<LRAppInstaller>(Location.App, Config);

            // ゲームプレイシーン（曲プレイ中）にサービスとコントローラーを登録
            zenjector.Install<LRGameInstaller>(Location.Player, Config);

            // メニューシーン（設定画面）に設定UIを登録
            zenjector.Install<LRMenuInstaller>(Location.Menu, Config);

            DebugLog("LRCounter initialized.");
        }

        // アプリ起動時（Init後）に呼ばれる。Harmonyパッチを適用する
        [OnStart]
        public void OnApplicationStart()
        {
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            DebugLog("LRCounter started.");
        }

        // アプリ終了時に呼ばれる。Harmonyパッチを全て解除する
        [OnExit]
        public void OnApplicationQuit()
        {
            HarmonyInstance.UnpatchSelf();
            DebugLog("LRCounter exited.");
        }
    }
}
