using LRCounter.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace LRCounter.Controllers.Gameplay
{
    // 精度低下・低スコア時にビープ音またはカスタムサウンドを鳴らすプレイヤー
    // （WallHitSound の再生方式・カスタムサウンド読み込み方式を流用）。
    // AudioSource は WallHitSound と同じく独立 GameObject + DontDestroyOnLoad で保持し、
    // 曲をまたいで使い回す（static で1つだけ作る）。Canvas の子にすると環境によっては
    // 再生前に破棄されうるため、実績のある独立方式に合わせた。
    // カスタムサウンドは UserData/LRCounter/Sound フォルダの wav/ogg/mp3 を読み込む
    // （他の用途のファイルと混ざらないよう LRCounter 直下ではなくサブフォルダに置く）。
    internal class DropSoundPlayer
    {
        private readonly PluginConfig _config;

        // アプリ全体で1つの AudioSource / クリップ（WallHitSound と同じ常駐方式）
        // クリップは左右で設定が異なるため別々に持つ。キーで設定変更を検知して作り直す
        private static AudioSource? _audioSource;
        private static AudioClip? _leftClip;
        private static AudioClip? _rightClip;
        private static string _leftClipKey = "";
        private static string _rightClipKey = "";

        // 生成ビープ音を表す設定値（これ以外はUserData/LRCounter/Soundのファイル名として扱う）
        public const string BeepClipName = "beep";

        public DropSoundPlayer(PluginConfig config)
        {
            _config = config;
        }

        // AudioSource が無ければ作る（初回のみ）。曲開始時に呼ぶ
        public void Build(Transform _)
        {
            EnsureAudioSource();
        }

        private static void EnsureAudioSource()
        {
            if (_audioSource != null) return;

            var go = new GameObject("LRCounter_DropSound");
            UnityEngine.Object.DontDestroyOnLoad(go);

            _audioSource = go.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0.0f;  // 2D音声（位置に依存しない）
            _audioSource.bypassEffects = false;
            _audioSource.bypassListenerEffects = false;
            _audioSource.bypassReverbZones = false;
        }

        // 精度低下フラッシュと同じタイミングで呼ぶ（設定画面のテスト再生からも呼ばれる）。
        // 有効/無効の判定は呼び出し側（精度低下・低スコアそれぞれのトグル）で行う。
        public void Play(bool isLeft)
        {
            EnsureAudioSource();
            if (_audioSource == null) return;
            if (!_audioSource.enabled) _audioSource.enabled = true;

            string clipName = isLeft ? _config.DropSoundLeftClip : _config.DropSoundRightClip;
            float frequency = isLeft ? _config.DropSoundLeftFrequency : _config.DropSoundRightFrequency;
            if (string.IsNullOrEmpty(clipName)) clipName = BeepClipName;

            // 設定が変わっていたらその手のクリップを作り直す（ビープは周波数、カスタムはファイル名で判定）
            string key = clipName == BeepClipName ? $"beep:{frequency}" : $"file:{clipName}";
            AudioClip? clip;
            if (isLeft)
            {
                if (_leftClip == null || _leftClipKey != key)
                {
                    _leftClip = CreateClip(clipName, frequency);
                    _leftClipKey = key;
                }
                clip = _leftClip;
            }
            else
            {
                if (_rightClip == null || _rightClipKey != key)
                {
                    _rightClip = CreateClip(clipName, frequency);
                    _rightClipKey = key;
                }
                clip = _rightClip;
            }
            if (clip == null) return;

            _audioSource.volume = Mathf.Clamp01(_config.DropSoundVolume);
            _audioSource.pitch = Mathf.Clamp(
                isLeft ? _config.DropSoundLeftPitch : _config.DropSoundRightPitch, 0.5f, 2.0f);
            _audioSource.PlayOneShot(clip, 1.0f);
        }

        // クリップ名からAudioClipを作る。カスタムファイルの読み込みに失敗したらビープにフォールバック
        private static AudioClip CreateClip(string clipName, float frequency)
        {
            if (clipName == BeepClipName) return CreateBeep(frequency);
            return LoadCustomClip(clipName) ?? CreateBeep(frequency);
        }

        // ─── カスタムサウンド（UserData/LRCounter の wav/ogg/mp3） ───────────────────

        private static readonly string[] AudioExtensions = { ".wav", ".ogg", ".mp3" };

        // カスタムサウンドの置き場所（UserData/LRCounter/Sound）。無ければ作る
        public static string? GetSoundFolder()
        {
            try
            {
                string path = Path.Combine(IPA.Utilities.UnityGame.UserDataPath, "LRCounter", "Sound");
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                return path;
            }
            catch (Exception ex)
            {
                Plugin.Log?.Warn($"[LRCounter] Could not get sound folder: {ex.Message}");
                return null;
            }
        }

        // 設定画面のドロップダウン用: フォルダ内のサウンドファイル名一覧（拡張子なし）
        public static List<string> GetCustomSoundNames()
        {
            try
            {
                string? folder = GetSoundFolder();
                if (folder == null) return new List<string>();
                return Directory.GetFiles(folder)
                    .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .Select(Path.GetFileNameWithoutExtension)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Plugin.Log?.Warn($"[LRCounter] Could not list custom sounds: {ex.Message}");
                return new List<string>();
            }
        }

        // UserData/LRCounter/Sound から拡張子を総当たりでファイルを探して読み込む。見つからなければ null
        private static AudioClip? LoadCustomClip(string clipName)
        {
            string? folder = GetSoundFolder();
            if (folder == null) return null;

            foreach (var ext in AudioExtensions)
            {
                string filePath = Path.Combine(folder, clipName + ext);
                if (File.Exists(filePath)) return LoadAudioClipViaWeb(filePath);
            }
            Plugin.Log?.Warn($"[LRCounter] Custom sound '{clipName}' not found in {folder}");
            return null;
        }

        // UnityWebRequest でローカルのオーディオファイルを同期読み込みする（WallHitSound と同方式）。
        // 一度読み込んだらキャッシュされるので待ちが発生するのは設定変更後の初回再生のみ
        private static AudioClip? LoadAudioClipViaWeb(string filePath)
        {
            try
            {
                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                AudioType audioType = ext switch
                {
                    ".wav" => AudioType.WAV,
                    ".ogg" => AudioType.OGGVORBIS,
                    ".mp3" => AudioType.MPEG,
                    _ => AudioType.UNKNOWN,
                };
                string uriPath = "file:///" + filePath.Replace("\\", "/");

                using var request = UnityWebRequestMultimedia.GetAudioClip(uriPath, audioType);
                var task = request.SendWebRequest();

                // 読み込み完了まで待機（タイムアウト約5秒）
                int timeoutCounter = 0;
                while (!task.isDone && timeoutCounter < 500)
                {
                    System.Threading.Thread.Sleep(10);
                    timeoutCounter++;
                }

                if (request.result == UnityWebRequest.Result.Success)
                {
                    AudioClip? clip = DownloadHandlerAudioClip.GetContent(request);
                    if (clip != null) return clip;
                }
                Plugin.Log?.Warn($"[LRCounter] Failed to load audio '{filePath}': {request.error}");
            }
            catch (Exception ex)
            {
                Plugin.Log?.Warn($"[LRCounter] Error loading audio '{filePath}': {ex.Message}");
            }
            return null;
        }

        // 指定周波数の正弦波ビープを生成する（約0.12秒・クリックノイズ防止の簡易フェード付き）
        private static AudioClip CreateBeep(float frequency)
        {
            const int sampleRate = 44100;
            int sampleCount = (int)(sampleRate * 0.12f);
            float[] data = new float[sampleCount];

            int fadeSamples = sampleCount / 10; // 先頭・末尾のフェード区間
            for (int i = 0; i < sampleCount; i++)
            {
                float envelope = 1f;
                if (i < fadeSamples) envelope = (float)i / fadeSamples;
                else if (i > sampleCount - fadeSamples) envelope = (float)(sampleCount - i) / fadeSamples;

                data[i] = Mathf.Sin((2f * Mathf.PI * frequency * i) / sampleRate) * 0.8f * envelope;
            }

            AudioClip clip = AudioClip.Create("lrcounter_drop_beep", sampleCount, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
