using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Zenject;

namespace LRCounter.Controllers.Gameplay
{
    // リプレイ再生の有無と「誰のリプレイか（自分／他人）」を、ScoreSaber / BeatLeader への
    // アセンブリ参照を持たずにリフレクションで判定するヘルパー。
    //
    // 判定の出どころ（実機DLLを解析して確認した内部API）：
    //   BeatLeader … ReplayerLauncher.IsStartedAsReplay（再生中か）と
    //                LaunchData.MainReplay.ReplayData.Player.Id（リプレイ主のID）を
    //                ProfileManager.Profile.Id（ローカルプレイヤーのID）と突き合わせる。IDなので確実。
    //   ScoreSaber … ReplayState.IsPlaybackEnabled（再生中か）と
    //                ReplayState.CurrentPlayerName（リプレイ主の表示名）を
    //                ローカルの表示名と突き合わせる。ReplayStateへの到達経路はバージョンで変わるため
    //                GetScoreSaberReplayState() が既知の入口を順に試す（3.3系: Plugin.ReplayState[static] /
    //                3.4系: ReplayStateRegistry.Current[static]・Plugin.Instance.ReplayState）。
    //                ScoreSaberはリプレイにプレイヤーIDを保持しないため名前一致でしか判定できないが、
    //                両者とも同じScoreSaberプロフィール名なので実用上は十分信頼できる。
    //
    // 検出に失敗した場合（Mod未導入・内部API変更など）は NotReplay にフォールバックするので、
    // 通常プレイは従来どおり記録される。リプレイと判って所有者だけ不明なときは Unknown（安全側＝記録しない）。
    internal static class ReplayDetector
    {
        public enum Ownership
        {
            NotReplay, // リプレイ再生中ではない（通常プレイ）
            Own,       // 自分のリプレイ
            Foreign,   // 他人のリプレイ
            Unknown,   // リプレイ中だが所有者を特定できない
        }

        // 記録（左右ベスト・PBキャッシュ）を「スキップすべき」か。
        // 他人のリプレイ(Foreign)・所有者不明(Unknown)はスキップ。通常プレイ(NotReplay)・自分のリプレイ(Own)は記録する。
        public static bool ShouldSkip(Ownership o) => o == Ownership.Foreign || o == Ownership.Unknown;

        // 現在の再生状態と所有者を判定する。BeatLeader→ScoreSaberの順に見て、再生中のものを採用する。
        // scoreSaberLocalName: ScoreSaberのローカル表示名（自前APIで取得した名前）。ScoreSaberの所有者判定に使う。
        //   ScoreSaberの PlayerService は別コンテナにあり App 静的からは解決できないため、外から渡す。
        public static Ownership GetOwnership(string? scoreSaberLocalName = null)
        {
            try
            {
                var bl = BeatLeaderOwnership();
                if (bl != Ownership.NotReplay) return bl;

                var ss = ScoreSaberOwnership(scoreSaberLocalName);
                if (ss != Ownership.NotReplay) return ss;
            }
            catch { /* 再生中と判る前の失敗は通常プレイ扱い（判った後の失敗は各判定内で Unknown に倒す） */ }
            return Ownership.NotReplay;
        }

        // ─── BeatLeader ──────────────────────────────────────────────────────────────
        // BeatLeaderのリプレイ再生中かを判定し、再生中なら所有者を返す。
        // 失敗した場合は NotReplay にフォールバックする。
        // BeatLeaderのリプレイ再生中かを判定するには、ReplayerLauncher.IsStartedAsReplay を参照する。
        // さらに、ReplayerLauncher.LaunchData.MainReplay.ReplayData.Player.Id と
        // ProfileManager.Profile.Id を比較して、所有者が自分か他人かを判定する。
        // どちらかが null なら Unknown（安全側＝記録しない）。
        // どちらも一致すれば Own、異なれば Foreign
        private static Ownership BeatLeaderOwnership()
        {
            object? startedObj = GetStatic("BeatLeader.Replayer.ReplayerLauncher", "IsStartedAsReplay");
            if (!(startedObj is bool started))
            {
                // 型かメンバーが見つからない（BeatLeader未導入、または内部APIの名前が変わった）
                WarnIfBeatLeaderUnreachable();
                return Ownership.NotReplay;
            }
            if (!started) return Ownership.NotReplay;

            // ここから先は再生中と確定済み。所有者判定で失敗しても通常プレイ扱いに戻してはいけないので、
            // 例外は Unknown（安全側＝記録しない）に倒す。
            try
            {
                var launchData = GetStatic("BeatLeader.Replayer.ReplayerLauncher", "LaunchData");
                var mainReplay = GetMember(launchData, "MainReplay");
                var replayData = GetMember(mainReplay, "ReplayData");
                var player = GetMember(replayData, "Player");
                string? replayId = GetMember(player, "Id", "id") as string;

                var profile = GetStatic("BeatLeader.DataManager.ProfileManager", "Profile");
                string? localId = GetMember(profile, "Id", "id") as string;

                var result = Compare(replayId, localId);
                Plugin.DebugLog($"[LRCounter] BeatLeader replay detected: replayId='{replayId}' localId='{localId}' -> {result}");
                return result;
            }
            catch { return Ownership.Unknown; }
        }

        // ─── ScoreSaber ──────────────────────────────────────────────────────────────
        // ScoreSaberのリプレイ再生中かを判定し、再生中なら所有者を返す。
        // 判定材料は ReplayState.IsPlaybackEnabled（再生中か）と ReplayState.CurrentPlayerName（リプレイ主の表示名）。
        // ただし「ReplayStateにどう辿り着くか」はScoreSaberのバージョンで変わるため GetScoreSaberReplayState() に分離した。
        // ReplayStateが取れない場合は NotReplay（通常プレイ扱い）にフォールバックしつつ、
        // ScoreSaberが導入済みなら警告を1回だけ出す（黙って落ちると他人のリプレイを記録してしまうため）。
        private static Ownership ScoreSaberOwnership(string? localNameOverride)
        {
            var replayState = GetScoreSaberReplayState();
            if (replayState == null)
            {
                WarnIfScoreSaberUnreachable();
                return Ownership.NotReplay; // ScoreSaber未導入 or 状態に到達できず
            }

            if (!(GetMember(replayState, "IsPlaybackEnabled") is bool enabled) || !enabled)
                return Ownership.NotReplay;

            // ここから先は再生中と確定済み。所有者判定で失敗しても通常プレイ扱いに戻してはいけないので、
            // 例外は Unknown（安全側＝記録しない）に倒す。
            try
            {
                string? replayName = GetMember(replayState, "CurrentPlayerName") as string;
                // ローカル名は外から渡された自前API取得名を優先。無ければScoreSaber内部からの取得を試みる（通常は別コンテナで失敗）。
                string? localName = !string.IsNullOrWhiteSpace(localNameOverride) ? localNameOverride : GetScoreSaberLocalName();

                var result = Compare(replayName, localName);
                Plugin.DebugLog($"[LRCounter] ScoreSaber replay detected: replayName='{replayName}' localName='{localName}' -> {result}");
                return result;
            }
            catch { return Ownership.Unknown; }
        }

        // ScoreSaberの ReplayState 相当のインスタンスを返す。見つからなければ null。
        //
        // ScoreSaberは 3.4.0 で内部構成が大きく変わっており、入口が1つではない：
        //   3.3系まで … ScoreSaber.Plugin.ReplayState が static（型は ScoreSaber.Core.ReplaySystem.ReplayState）
        //   3.4系から … ScoreSaber.Plugin.ReplayState は instance に降格。状態は
        //               ScoreSaber.Features.Replays.ReplayStateRegistry.Current（static）配下へ移動
        //               （Plugin.Instance.ReplayState と同一インスタンス）。
        // そのため既知の入口を順に試し、どれも当たらなければ限定的なアセンブリ走査で拾う。
        // ReplayStateインスタンスは ReplayStateRegistry.Use() で差し替えられうるので、値はキャッシュせず毎回読む。
        private static object? GetScoreSaberReplayState()
        {
            // 3.4系: ReplayStateRegistry.Current（static internal プロパティ）
            var state = GetStatic("ScoreSaber.Features.Replays.ReplayStateRegistry", "Current");
            if (LooksLikeReplayState(state)) return state;

            // 3.3系まで: Plugin.ReplayState（static internal プロパティ）
            state = GetStatic("ScoreSaber.Plugin", "ReplayState");
            if (LooksLikeReplayState(state)) return state;

            // 3.4系の別経路: Plugin.Instance（static）→ ReplayState（instance internal プロパティ）
            state = GetMember(GetStatic("ScoreSaber.Plugin", "Instance"), "ReplayState");
            if (LooksLikeReplayState(state)) return state;

            return ScanScoreSaberForReplayState();
        }

        // IsPlaybackEnabled(bool) を持っていれば ReplayState 相当とみなす（型名には依存しない）。
        private static bool LooksLikeReplayState(object? o) => o != null && GetMember(o, "IsPlaybackEnabled") is bool;

        // 既知の入口が全部外れた（＝将来のScoreSaberでまた構成が変わった）ときの保険。
        // ScoreSaberアセンブリ内の「名前に Replay を含む型」の staticメンバーだけを、名前を絞って覗く。
        // 当たった入口(型・メンバー名)だけをキャッシュする。空振りはキャッシュせず次回また走査する
        // ——「見つからなかった」を覚えてしまうと、状態が後から生成される版で最初の1回の空振りが
        // セッション中ずっと尾を引き、この保険自体が効かなくなるため。走査は曲開始時の1回だけなので
        // 毎回やり直しても負荷にはならない。
        private static readonly string[] _ssStateMemberNames = { "Current", "ReplayState", "State", "Instance" };
        private static Type? _ssScanType;
        private static string? _ssScanMember;

        private static object? ScanScoreSaberForReplayState()
        {
            if (_ssScanType != null)
            {
                var cached = GetStatic(_ssScanType, _ssScanMember!);
                if (LooksLikeReplayState(cached)) return cached;
                _ssScanType = null; // 覚えた入口が形を変えたら覚え直す
                _ssScanMember = null;
            }

            var asm = FindScoreSaberAssembly();
            if (asm == null) return null;

            foreach (var type in GetTypesSafe(asm))
            {
                if (type.Name.IndexOf("Replay", StringComparison.OrdinalIgnoreCase) < 0) continue;
                foreach (var member in _ssStateMemberNames)
                {
                    var value = GetStatic(type, member);
                    if (!LooksLikeReplayState(value)) continue;
                    _ssScanType = type;
                    _ssScanMember = member;
                    Plugin.Log.Info($"[LRCounter] ScoreSaber replay state located by scan: {type.FullName}.{member}");
                    return value;
                }
            }
            return null;
        }

        // BeatLeaderは入っているのに ReplayerLauncher.IsStartedAsReplay を読めない場合だけ、起動中1回だけ警告する。
        // この状態ではBeatLeaderリプレイが「通常プレイ」に見えてしまい、他人のリプレイでも
        // 左右ベストが記録されるため、黙って進まず気づけるようにしておく。
        private static bool _blWarned;
        private static void WarnIfBeatLeaderUnreachable()
        {
            if (_blWarned) return;
            _blWarned = true;
            Assembly? asm = null;
            try
            {
                asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "BeatLeader", StringComparison.OrdinalIgnoreCase));
            }
            catch { /* ignore */ }
            if (asm == null) return; // BeatLeader未導入。通常プレイなので何も言わない
            Plugin.Log.Warn($"[LRCounter] BeatLeader {asm.GetName().Version} is loaded but its replay state could not be found. " +
                            "BeatLeader replay playback will be treated as normal play (someone else's replay may update your left/right bests).");
        }

        // ScoreSaberは入っているのに ReplayState に到達できない場合だけ、起動中1回だけ警告する。
        // この状態ではScoreSaberリプレイが「通常プレイ」に見えてしまい、他人のリプレイでも
        // 左右ベストが記録されるため、黙って進まず気づけるようにしておく。
        private static bool _ssWarned;
        private static void WarnIfScoreSaberUnreachable()
        {
            if (_ssWarned) return;
            var asm = FindScoreSaberAssembly();
            if (asm == null) return; // ScoreSaber未導入。通常プレイなので何も言わない
            _ssWarned = true;
            Plugin.Log.Warn($"[LRCounter] ScoreSaber {asm.GetName().Version} is loaded but its replay state could not be found. " +
                            "ScoreSaber replay playback will be treated as normal play (someone else's replay may update your left/right bests).");
        }

        // ScoreSaberのDIコンテナからローカルプレイヤー情報を引き、表示名を返す。
        // これは「自前APIで表示名が取れなかった」ときのフォールバックであり、取れなければ null を返す
        // （呼び出し側で Unknown ＝記録しない側に倒れる）。
        // なお ScoreSaber 3.4系には静的なDiContainer入口（旧 Plugin.Container）が無いため、
        // 3.4系ではこの経路は常に null になる。3.3系までのフォールバックとして残している。
        private static string? GetScoreSaberLocalName()
        {
            try
            {
                // 3.3系まで: Plugin.Container（static internal フィールド）。3.4系では存在しない。
                if (!(GetStatic("ScoreSaber.Plugin", "Container") is DiContainer container)) return null;

                // LocalPlayerInfo は単独でDIバインドされておらず、サービス側が保持する。
                //   3.3系まで … Core.Services.PlayerService.localPlayerInfo
                //   3.4系から … Features.Players.Services.GameSessionService.LocalPlayerInfo
                object? lpi = null;
                foreach (var serviceName in new[]
                {
                    "ScoreSaber.Features.Players.Services.GameSessionService",
                    "ScoreSaber.Core.Services.PlayerService",
                })
                {
                    var serviceType = FindType(serviceName);
                    if (serviceType == null) continue;
                    object? service = container.TryResolve(serviceType);
                    lpi = GetMember(service, "LocalPlayerInfo", "localPlayerInfo");
                    if (lpi != null) break;
                }

                // フォールバック：LocalPlayerInfo が直接バインドされている版に備える。
                if (lpi == null)
                {
                    foreach (var lpiName in new[]
                    {
                        "ScoreSaber.Features.Players.Domain.LocalPlayerInfo",
                        "ScoreSaber.Core.Data.LocalPlayerInfo",
                    })
                    {
                        var lpiType = FindType(lpiName);
                        if (lpiType == null) continue;
                        lpi = container.TryResolve(lpiType);
                        if (lpi != null) break;
                    }
                }

                return GetMember(lpi, "playerName", "PlayerName") as string;
            }
            catch { return null; }
        }

        // ─── 比較 ────────────────────────────────────────────────────────────────────
        // どちらか欠けていれば Unknown（特定不能＝安全側）。一致で Own、不一致で Foreign。
        // ScoreSaberの色付き名（<color=...>Name</color> 等のリッチテキスト）を除去・前後空白を詰めて比較する。
        // これにより、自分が色付き名でも「素の名前」同士で一致判定できる。大文字小文字は区別しない。
        private static Ownership Compare(string? replayOwner, string? localOwner)
        {
            string a = NormalizeName(replayOwner);
            string b = NormalizeName(localOwner);
            if (a.Length == 0 || b.Length == 0) return Ownership.Unknown;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ? Ownership.Own : Ownership.Foreign;
        }

        // リッチテキストタグ(<...>)を除去し前後空白を詰める。色付き名でも素の名前で比較するための正規化。
        private static string NormalizeName(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            return Regex.Replace(s, "<[^>]+>", "").Trim();
        }

        // ─── リフレクション補助 ──────────────────────────────────────────────────────
        // 型名→Typeのキャッシュ。見つからなかった型も null でキャッシュして再走査を防ぐ。
        // 型名はアセンブリ名を含む完全修飾名で指定する（例: "ScoreSaber.Core.Data.LocalPlayerInfo"）。
        // 型名にアセンブリ名を含めない場合は、ロード済み全アセンブリから順に探す。
        private static readonly Dictionary<string, Type?> _typeCache = new Dictionary<string, Type?>();

        // 型名をロード済み全アセンブリから探す（見つからなくても結果をキャッシュして再走査を防ぐ）。
        // 型名はアセンブリ名を含む完全修飾名で指定する（例: "ScoreSaber.Core.Data.LocalPlayerInfo"）。
        private static Type? FindType(string fullName)
        {
            if (_typeCache.TryGetValue(fullName, out var cached)) return cached;
            Type? type = null;
            try
            {
                type = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => { try { return a.GetType(fullName, false); } catch { return null; } })
                    .FirstOrDefault(t => t != null);
            }
            catch { /* ignore */ }
            _typeCache[fullName] = type;
            return type;
        }

        // ロード済みアセンブリからScoreSaber本体を探す（見つからなければ null＝未導入）。
        private static Assembly? _ssAssembly;
        private static bool _ssAssemblyLookedUp;
        private static Assembly? FindScoreSaberAssembly()
        {
            if (_ssAssemblyLookedUp) return _ssAssembly;
            _ssAssemblyLookedUp = true;
            try
            {
                _ssAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "ScoreSaber", StringComparison.OrdinalIgnoreCase));
            }
            catch { /* ignore */ }
            return _ssAssembly;
        }

        // アセンブリ内の型一覧。一部の型がロードできなくても、読めた分だけ返す。
        private static IEnumerable<Type> GetTypesSafe(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null)!; }
            catch { return Enumerable.Empty<Type>(); }
        }

        // 静的プロパティ/フィールドの値を読む。型・メンバーが無ければ null。
        private static object? GetStatic(string typeName, string member) => GetStatic(FindType(typeName), member);

        private static object? GetStatic(Type? type, string member)
        {
            if (type == null) return null;
            const BindingFlags bf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            try
            {
                var p = type.GetProperty(member, bf);
                if (p != null && p.CanRead) return p.GetValue(null);
                var f = type.GetField(member, bf);
                if (f != null) return f.GetValue(null);
            }
            catch { /* ignore */ }
            return null;
        }

        // インスタンスのプロパティ/フィールド値を緩く読む。明示的インターフェース実装も拾えるよう
        // 宣言型→実装インターフェース→フィールドの順に候補名で探す。
        private static object? GetMember(object? obj, params string[] names)
        {
            if (obj == null) return null;
            var type = obj.GetType();
            const BindingFlags bf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            try
            {
                foreach (var name in names)
                {
                    var p = type.GetProperty(name, bf);
                    if (p != null && p.CanRead) return p.GetValue(obj);
                }
                foreach (var itf in type.GetInterfaces())
                    foreach (var name in names)
                    {
                        var p = itf.GetProperty(name);
                        if (p != null && p.CanRead) return p.GetValue(obj);
                    }
                foreach (var name in names)
                {
                    var f = type.GetField(name, bf);
                    if (f != null) return f.GetValue(obj);
                }
            }
            catch { /* ignore */ }
            return null;
        }
    }
}
