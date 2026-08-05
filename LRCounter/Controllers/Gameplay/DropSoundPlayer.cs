using LRCounter.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace LRCounter.Controllers.Gameplay
{
    // 低スコア・ミス時にビープ音またはカスタムサウンドを鳴らすプレイヤー
    // （WallHitSound の再生方式・カスタムサウンド読み込み方式を流用）。
    // AudioSource は WallHitSound と同じく独立 GameObject + DontDestroyOnLoad で保持し、
    // 曲をまたいで使い回す（static で1つだけ作る）。Canvas の子にすると環境によっては
    // 再生前に破棄されうるため、実績のある独立方式に合わせた。
    // カスタムサウンドは UserData/LRCounter/Sound フォルダの wav/ogg/mp3 を読み込む
    // （他の用途のファイルと混ざらないよう LRCounter 直下ではなくサブフォルダに置く）。
    internal class DropSoundPlayer
    {
        private readonly PluginConfig _config;

        // アプリ全体で常駐する AudioSource（WallHitSound と同じ常駐方式）。
        // 「左右」×「低スコア音／ミス音」の4通りで設定（クリップ・音量・ピッチ・パン）が異なり、
        // 同時に鳴りうる（同じ手でミスと低スコアが同時成立する）。AudioSource の volume/pitch/pan は
        // ソース単位の設定なので、1つを使い回すと同時発音で互いの設定を上書きしてしまう。
        // そのため4通りぶんのソースとクリップを別々に持つ。
        // 添字は [isLeft ? 0 : 1, isMiss ? 1 : 0]。キーで設定変更を検知してクリップを作り直す
        private static readonly AudioSource?[,] _sources = new AudioSource?[2, 2];
        private static readonly AudioClip?[,] _clips = new AudioClip?[2, 2];
        private static readonly string[,] _clipKeys = { { "", "" }, { "", "" } };

        // 生成ビープ音を表す設定値（これ以外はUserData/LRCounter/Soundのファイル名として扱う）
        public const string BeepClipName = "beep";

        public DropSoundPlayer(PluginConfig config)
        {
            _config = config;
        }

        // AudioSource が無ければ作る（初回のみ）。曲開始時に呼ぶ。
        // あわせて4通りのクリップを先に用意しておく（カスタム音のファイル読み込みは
        // メインスレッドを止めるため、プレイ中の初回再生でフリーズしないよう曲開始時に済ませる）。
        public void Build(Transform _)
        {
            EnsureAudioSource();
            for (int hand = 0; hand < 2; hand++)
                for (int kind = 0; kind < 2; kind++)
                    EnsureClip(hand, kind);
        }

        private static void EnsureAudioSource()
        {
            if (_sources[0, 0] != null) return;

            var go = new GameObject("LRCounter_DropSound");
            UnityEngine.Object.DontDestroyOnLoad(go);

            for (int hand = 0; hand < 2; hand++)
                for (int kind = 0; kind < 2; kind++)
                    _sources[hand, kind] = CreateSource(go);
        }

        private static AudioSource CreateSource(GameObject go)
        {
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0.0f;  // 2D音声（位置に依存しない）
            source.bypassEffects = false;
            source.bypassListenerEffects = false;
            source.bypassReverbZones = false;
            return source;
        }

        // 精度低下フラッシュと同じタイミングで呼ぶ（設定画面のテスト再生からも呼ばれる）。
        // 有効/無効の判定は呼び出し側（低スコア・ミスそれぞれのトグル）で行う。
        // isMiss=true でミス用の設定（クリップ・周波数・ピッチ・音量）を使う。
        public void Play(bool isLeft, bool isMiss = false)
        {
            EnsureAudioSource();
            int hand = isLeft ? 0 : 1;
            int kind = isMiss ? 1 : 0;
            AudioSource? source = _sources[hand, kind];
            if (source == null) return;
            if (!source.enabled) source.enabled = true;

            AudioClip? clip = EnsureClip(hand, kind);
            if (clip == null) return;

            source.volume = Mathf.Clamp01(isMiss
                ? (isLeft ? _config.DropSoundMissLeftVolume : _config.DropSoundMissRightVolume)
                : (isLeft ? _config.DropSoundLeftVolume : _config.DropSoundRightVolume));
            source.pitch = Mathf.Clamp(isMiss
                ? (isLeft ? _config.DropSoundMissLeftPitch : _config.DropSoundMissRightPitch)
                : (isLeft ? _config.DropSoundLeftPitch : _config.DropSoundRightPitch), 0.5f, 2.0f);
            // ステレオパン: ONなら左手=左耳のみ(-1)/右手=右耳のみ(+1)、OFFなら中央(0)
            source.panStereo = _config.DropSoundStereoPan ? (isLeft ? -1f : 1f) : 0f;
            source.PlayOneShot(clip, 1.0f);
        }

        // [hand, kind] のクリップを現在の設定に合わせて用意して返す。
        // 設定（ビープは周波数、カスタムはファイル名）が変わっていたら作り直し、古いクリップは破棄する。
        //   hand … 0=左, 1=右 ／ kind … 0=低スコア音, 1=ミス音
        private AudioClip? EnsureClip(int hand, int kind)
        {
            bool isLeft = hand == 0;
            bool isMiss = kind == 1;

            string clipName = isMiss
                ? (isLeft ? _config.DropSoundMissLeftClip : _config.DropSoundMissRightClip)
                : (isLeft ? _config.DropSoundLeftClip : _config.DropSoundRightClip);
            float frequency = isMiss
                ? (isLeft ? _config.DropSoundMissLeftFrequency : _config.DropSoundMissRightFrequency)
                : (isLeft ? _config.DropSoundLeftFrequency : _config.DropSoundRightFrequency);
            if (string.IsNullOrEmpty(clipName)) clipName = BeepClipName;

            string key = clipName == BeepClipName ? $"beep:{frequency}" : $"file:{clipName}";
            if (_clips[hand, kind] != null && _clipKeys[hand, kind] == key) return _clips[hand, kind];

            // 作り直す前に古いクリップを破棄する（スクリプト生成のAudioClipは放置すると積み上がるため）
            if (_clips[hand, kind] != null) UnityEngine.Object.Destroy(_clips[hand, kind]);
            _clips[hand, kind] = CreateClip(clipName, frequency);
            _clipKeys[hand, kind] = key;
            return _clips[hand, kind];
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
