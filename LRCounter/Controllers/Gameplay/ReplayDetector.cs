using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    //   ScoreSaber … Plugin.ReplayState.IsPlaybackEnabled（再生中か）と
    //                Plugin.ReplayState.CurrentPlayerName（リプレイ主の表示名）を
    //                LocalPlayerInfo.playerName（ローカルの表示名）と突き合わせる。
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
        public static Ownership GetOwnership()
        {
            try
            {
                var bl = BeatLeaderOwnership();
                if (bl != Ownership.NotReplay) return bl;

                var ss = ScoreSaberOwnership();
                if (ss != Ownership.NotReplay) return ss;
            }
            catch { /* 検出失敗は通常プレイ扱い */ }
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
            if (!(GetStatic("BeatLeader.Replayer.ReplayerLauncher", "IsStartedAsReplay") is bool started) || !started)
                return Ownership.NotReplay;

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

        // ─── ScoreSaber ──────────────────────────────────────────────────────────────
        // ScoreSaberのリプレイ再生中かを判定し、再生中なら所有者を返す。
        // 失敗した場合は NotReplay にフォールバックする。
        // ScoreSaberのリプレイ再生中かを判定するには、
        // Plugin.ReplayState.IsPlaybackEnabled を参照する。
        // さらに、Plugin.ReplayState.CurrentPlayerName と
        // LocalPlayerInfo.playerName を比較して、所有者が自分か他人かを判定する。
        // どちらかが null なら Unknown（安全側＝記録しない）。
        // どちらも一致すれば Own、異なれば Foreign
        private static Ownership ScoreSaberOwnership()
        {
            var replayState = GetStatic("ScoreSaber.Plugin", "ReplayState");
            if (replayState == null) return Ownership.NotReplay; // ScoreSaber未導入 or 状態未生成

            if (!(GetMember(replayState, "IsPlaybackEnabled") is bool enabled) || !enabled)
                return Ownership.NotReplay;

            string? replayName = GetMember(replayState, "CurrentPlayerName") as string;
            string? localName = GetScoreSaberLocalName();

            var result = Compare(replayName, localName);
            Plugin.DebugLog($"[LRCounter] ScoreSaber replay detected: replayName='{replayName}' localName='{localName}' -> {result}");
            return result;
        }

        // ScoreSaberのDIコンテナからローカルプレイヤー情報を引き、表示名を返す。
        // DIコンテナにバインドされていない場合は null を返す（例外は投げない）。
        // 失敗した場合は NotReplay にフォールバックする。
        // 参照する型・メンバーは ScoreSaber 5.0.0 で確認したもの。将来のバージョンで変更される可能性がある。
        private static string? GetScoreSaberLocalName()
        {
            try
            {
                var lpiType = FindType("ScoreSaber.Core.Data.LocalPlayerInfo");
                if (lpiType == null) return null;
                if (!(GetStatic("ScoreSaber.Plugin", "Container") is DiContainer container)) return null;

                object? lpi = container.TryResolve(lpiType); // 未バインドなら null（例外を投げない）
                return GetMember(lpi, "playerName", "PlayerName") as string;
            }
            catch { return null; }
        }

        // ─── 比較 ────────────────────────────────────────────────────────────────────
        // どちらか欠けていれば Unknown（特定不能＝安全側）。一致で Own、不一致で Foreign。
        // null/空白文字列は無視して比較する。
        // 文字列の前後空白は無視して比較する。大文字小文字は区別しない。
        private static Ownership Compare(string? replayOwner, string? localOwner)
        {
            if (string.IsNullOrWhiteSpace(replayOwner) || string.IsNullOrWhiteSpace(localOwner))
                return Ownership.Unknown;
            return string.Equals(replayOwner!.Trim(), localOwner!.Trim(), StringComparison.OrdinalIgnoreCase)
                ? Ownership.Own : Ownership.Foreign;
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

        // 静的プロパティ/フィールドの値を読む。型・メンバーが無ければ null。
        private static object? GetStatic(string typeName, string member)
        {
            var type = FindType(typeName);
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
