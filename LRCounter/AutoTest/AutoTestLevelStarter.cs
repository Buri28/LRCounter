using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Zenject;

namespace LRCounter.AutoTest
{
    /// <summary>
    /// メニューに入ったら曲を自動で始める（--lrcounter-autotest のときだけ）。
    /// MenuTransitionsHelper.StartStandardLevel は 1.40.8 と 1.44.1 で引数が違うため、
    /// リフレクションで引数を型ごとに埋めて呼ぶ（SkyLight の AutoTestLevelStarter と同じ方法）。
    /// </summary>
    internal class AutoTestLevelStarter : IInitializable
    {
        private static bool _started; // 1回の起動で1曲だけ
        private bool _invoked;        // StartStandardLevel を呼んだか
        private readonly DiContainer _container;

        public AutoTestLevelStarter(DiContainer container)
        {
            _container = container;
        }

        public void Initialize()
        {
            if (!AutoTestOptions.Enabled || _started) return;
            _started = true;
            AutoTestOptions.Status("MENU " + AutoTestOptions.Describe());
            AutoTestRunner.Instance.StartCoroutine(StartWhenReady());
        }

        private IEnumerator StartWhenReady()
        {
            // メニューの読み込み（曲一覧など）が落ち着くまで待つ。
            yield return new WaitForSeconds(3f);

            string? lastError = null;
            for (int i = 0; i < 600; i++) // 曲の多い環境では読み込みに数分かかる
            {
                try
                {
                    if (TryStart(out lastError)) yield break;
                }
                catch (Exception ex)
                {
                    lastError = $"{ex.GetType().Name}: {ex.Message}".Replace('\r', ' ').Replace('\n', ' ');
                    Plugin.Log.Warn($"[LRCounter][autotest] start failed: {ex}");
                    if (_invoked) break; // 曲の開始を呼んだ後の例外では、二重に始めないよう再試行しない
                }
                yield return new WaitForSeconds(1f);
            }
            AutoTestOptions.Status("FAIL could not start level: " + lastError);
        }

        private bool TryStart(out string? error)
        {
            error = null;
            var helper = Resolve("MenuTransitionsHelper");
            var levelsModel = Resolve("BeatmapLevelsModel");
            var playerDataModel = Resolve("PlayerDataModel");
            var envList = Resolve("EnvironmentsListModel");
            if (helper == null || levelsModel == null || playerDataModel == null || envList == null)
            {
                error = $"not resolved: helper={helper != null} levels={levelsModel != null} playerData={playerDataModel != null} envList={envList != null}";
                return false;
            }

            var level = FindLevel(levelsModel);
            if (level == null) { error = "level not found (custom levels not loaded yet?)"; return false; }

            var key = PickKey(level);
            if (key == null) { error = "no beatmap key"; return false; }

            var playerData = Get(playerDataModel, "playerData");
            var playerSettings = playerData != null ? Get(playerData, "playerSpecificSettings") : null;
            if (playerSettings == null) { error = "playerSpecificSettings not found"; return false; }

            // 何も操作しないのでノーツは全部 Miss になる。途中で落ちないよう NoFail にする。
            var modifiersType = FindType("GameplayModifiers");
            var modifiers = modifiersType != null ? Activator.CreateInstance(modifiersType) : null;
            modifiersType?.GetField("_noFailOn0Energy", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(modifiers, true);

            var method = PickStartMethod(helper.GetType());
            if (method == null) { error = "StartStandardLevel not found"; return false; }

            var ps = method.GetParameters();
            var args = new object?[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                var p = ps[i];
                var t = p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType;
                string n = p.Name ?? "";
                if (t == typeof(string))
                    args[i] = n.IndexOf("gameMode", StringComparison.OrdinalIgnoreCase) >= 0 ? "Solo"
                            : n.IndexOf("backButton", StringComparison.OrdinalIgnoreCase) >= 0 ? "Menu" : null;
                else if (t.Name == "BeatmapKey") args[i] = key;
                else if (t.Name == "BeatmapLevel") args[i] = level;
                else if (t.Name == "GameplayModifiers") args[i] = modifiers;
                else if (t.Name == "PlayerSpecificSettings") args[i] = playerSettings;
                else if (t.Name == "EnvironmentsListModel") args[i] = envList;
                else if (t.Name == "GameplayAdditionalInformation") args[i] = CreateWithDefaults(t);
                else if (t == typeof(bool)) args[i] = false;
                else if (p.HasDefaultValue) args[i] = p.DefaultValue is DBNull ? null : p.DefaultValue;
                else if (t.IsValueType && Nullable.GetUnderlyingType(t) == null) args[i] = Activator.CreateInstance(t);
                else args[i] = null;
            }

            AutoTestOptions.Status($"START level={Get(level, "levelID")} song={Get(level, "songName")} key={Get(key, "difficulty")} " +
                $"method=({string.Join(", ", ps.Select(p => p.ParameterType.Name))})");
            _invoked = true;
            method.Invoke(helper, args);
            return true;
        }

        private object? Resolve(string typeName)
        {
            var t = FindType(typeName);
            return t == null ? null : _container.TryResolve(t);
        }

        // カスタム曲から、曲名が一致するもの（指定が無ければ最初のもの）を選ぶ。
        // OST 曲は使わない（levelID が custom_level_ でないと、ハッシュを切り出す MOD が例外を出すことがあるため）。
        private static object? FindLevel(object levelsModel)
        {
            foreach (var repoName in new[] { "_customLevelsRepository", "_allLoadedBeatmapLevelsRepository" })
            {
                foreach (var l in LevelsIn(Get(levelsModel, repoName)))
                {
                    if (!(Get(l, "levelID") as string ?? "").StartsWith("custom_level_", StringComparison.Ordinal)) continue;
                    if (!string.IsNullOrEmpty(AutoTestOptions.SongName) &&
                        !string.Equals(Get(l, "songName") as string, AutoTestOptions.SongName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrEmpty(AutoTestOptions.Mapper) &&
                        !(Get(l, "allMappers") is string[] mappers &&
                          mappers.Any(m => string.Equals(m, AutoTestOptions.Mapper, StringComparison.OrdinalIgnoreCase))))
                        continue;
                    return l;
                }
            }
            return null;
        }

        private static IEnumerable<object> LevelsIn(object? repo)
        {
            if (repo == null || !(Get(repo, "beatmapLevelPacks") is IEnumerable packs)) yield break;
            foreach (var pack in packs)
            {
                if (pack == null) continue;
                if (Get(pack, "_beatmapLevels") is IEnumerable levels)
                    foreach (var l in levels)
                        if (l != null) yield return l;
            }
        }

        // Standard の指定難易度。無ければ Standard の最初、それも無ければ最初のキー。
        private static object? PickKey(object level)
        {
            var m = level.GetType().GetMethod("GetBeatmapKeys");
            if (m == null || !(m.Invoke(level, null) is IEnumerable keysEnum)) return null;
            var keys = keysEnum.Cast<object>().ToList();
            bool IsStandard(object k) => (Get(Get(k, "beatmapCharacteristic")!, "serializedName") as string) == "Standard";
            return keys.FirstOrDefault(k => IsStandard(k) &&
                       string.Equals(Get(k, "difficulty")?.ToString(), AutoTestOptions.Difficulty, StringComparison.OrdinalIgnoreCase))
                ?? keys.FirstOrDefault(IsStandard)
                ?? keys.FirstOrDefault();
        }

        // StartStandardLevel の中から、曲データ(IBeatmapLevelData)を自分で渡さなくてよいものを選ぶ。
        private static MethodInfo? PickStartMethod(Type helperType)
        {
            var all = helperType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => m.Name == "StartStandardLevel").ToList();
            bool NeedsLevelData(MethodInfo m) => m.GetParameters()
                .Any(p => p.ParameterType.Name == "IBeatmapLevelData" && !p.HasDefaultValue);
            return all.FirstOrDefault(m => !NeedsLevelData(m)) ?? all.FirstOrDefault();
        }

        private static object? CreateWithDefaults(Type t)
        {
            var ctor = t.GetConstructors().OrderBy(c => c.GetParameters().Length).FirstOrDefault();
            if (ctor == null) return null;
            var ps = ctor.GetParameters();
            var a = new object?[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].ParameterType == typeof(string) && (ps[i].Name ?? "").IndexOf("backButton", StringComparison.OrdinalIgnoreCase) >= 0)
                    a[i] = "Menu";
                else if (ps[i].HasDefaultValue) a[i] = ps[i].DefaultValue is DBNull ? null : ps[i].DefaultValue;
                else a[i] = ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null;
            }
            return ctor.Invoke(a);
        }

        // フィールド・プロパティを名前で読む（public/非public 両方）。
        internal static object? Get(object? obj, string name)
        {
            if (obj == null) return null;
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (var t = obj.GetType(); t != null; t = t.BaseType)
            {
                var prop = t.GetProperty(name, F);
                if (prop != null && prop.GetIndexParameters().Length == 0) return prop.GetValue(obj);
                var field = t.GetField(name, F);
                if (field != null) return field.GetValue(obj);
            }
            return null;
        }

        private static readonly Dictionary<string, Type?> _typeCache = new Dictionary<string, Type?>();

        internal static Type? FindType(string name)
        {
            if (_typeCache.TryGetValue(name, out var cached)) return cached;
            Type? found = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { found = asm.GetType(name, false); }
                catch { /* 読めないアセンブリは飛ばす */ }
                if (found != null) break;
            }
            _typeCache[name] = found;
            return found;
        }
    }
}
