using LRCounter.Configuration;
using System;
using System.Collections;
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
        // 添字は [isLeft ? 0 : 1, KindXxx]。キーで設定変更を検知してクリップを作り直す。
        // フェイル音は左右の区別が無いので hand=0 の枠だけ使う（[1, KindFail] は未使用）。
        private static readonly AudioSource?[,] _sources = new AudioSource?[2, KindCount];
        private static readonly AudioClip?[,] _clips = new AudioClip?[2, KindCount];
        private static readonly string[,] _clipKeys = new string[2, KindCount];
        // カスタム音を非同期読み込み中のキー（""=読み込み中でない）。二重に読み込みを走らせないための印
        private static readonly string[,] _loadingKeys = new string[2, KindCount];

        // 音の種類（クリップ枠の第2添字）
        private const int KindScore = 0; // 低スコア音
        private const int KindMiss = 1;  // ミス音
        private const int KindFail = 2;  // フェイル音（左右共通・hand=0のみ使う）
        private const int KindCount = 3;

        // フェイル音は設定できるのがサウンドのみなので、ビープ周波数・音量・ピッチは固定にする
        private const float FailBeepFrequency = 200f;
        private const float FailVolume = 1.0f;
        private const float FailPitch = 1.0f;

        static DropSoundPlayer()
        {
            // string[,] の既定値は null なので、キー比較で NullReference にならないよう空文字で埋める
            for (int hand = 0; hand < 2; hand++)
                for (int kind = 0; kind < KindCount; kind++)
                {
                    _clipKeys[hand, kind] = "";
                    _loadingKeys[hand, kind] = "";
                }
        }

        // コルーチン（カスタム音の非同期読み込み）の実行役。常駐 GameObject に付けるので曲をまたいで生き残る
        private static SoundRunner? _runner;

        // 生成ビープ音を表す設定値（これ以外はUserData/LRCounter/Soundのファイル名として扱う）
        public const string BeepClipName = "beep";

        // フェイル音の「鳴らさない」を表す設定値（＝既定値。ドロップダウンの先頭の空欄項目）
        public const string NoneClipName = "";

        public DropSoundPlayer(PluginConfig config)
        {
            _config = config;
        }

        // AudioSource が無ければ作る（初回のみ）。曲開始時に呼ぶ。
        // あわせて4通りのクリップを先に用意する。ビープの生成は数千サンプルのループだけで軽い。
        // カスタム音のファイル読み込みは重いが、非同期（コルーチン）なのでフレームを止めない。
        // どちらもクリップは static にキャッシュされるので、実際に走るのは
        // ゲーム起動後の初回と設定変更後の1回だけで、毎曲のコストにはならない。
        public void Build(Transform _)
        {
            Prewarm();
        }

        // 4通りのクリップを事前に用意する。メニュー（選曲画面）到達時にも呼ばれるので、
        // 通常はプレイ画面に入る前に準備が終わっている。クリップは static キャッシュなので
        // ここで済ませておけば曲開始時の Build は何もしない。
        public void Prewarm()
        {
            EnsureAudioSource();
            for (int hand = 0; hand < 2; hand++)
                for (int kind = 0; kind < 2; kind++)
                    EnsureClip(hand, kind);
            // フェイル音は左右共通なので1枠だけ。未設定ならここでクリップが解放される
            EnsureClip(0, KindFail);
        }

        private static void EnsureAudioSource()
        {
            if (_sources[0, 0] != null) return;

            var go = new GameObject("LRCounter_DropSound");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<SoundRunner>();

            for (int hand = 0; hand < 2; hand++)
                for (int kind = 0; kind < KindCount; kind++)
                    _sources[hand, kind] = CreateSource(go);
        }

        // コルーチンを回すためだけの MonoBehaviour（常駐 GameObject に付く）
        private class SoundRunner : MonoBehaviour { }

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
            int hand = isLeft ? 0 : 1;
            int kind = isMiss ? KindMiss : KindScore;

            float volume = isMiss
                ? (isLeft ? _config.DropSoundMissLeftVolume : _config.DropSoundMissRightVolume)
                : (isLeft ? _config.DropSoundLeftVolume : _config.DropSoundRightVolume);
            float pitch = isMiss
                ? (isLeft ? _config.DropSoundMissLeftPitch : _config.DropSoundMissRightPitch)
                : (isLeft ? _config.DropSoundLeftPitch : _config.DropSoundRightPitch);
            // ステレオパン: ONなら左手=左耳のみ(-1)/右手=右耳のみ(+1)、OFFなら中央(0)
            float pan = _config.DropSoundStereoPan ? (isLeft ? -1f : 1f) : 0f;

            PlayKind(hand, kind, volume, pitch, pan);
        }

        // フェイル音のサウンドが選ばれているか（既定の空欄なら鳴らさない）
        public bool IsFailSoundSet => !string.IsNullOrEmpty(_config.DropSoundFailClip);

        // フェイル（体力0）時に1回だけ鳴らす音。設定できるのはサウンド（クリップ）だけなので、
        // 音量・ピッチは固定、パンは中央（左右の区別が無い音なので）。
        // 未設定なら何も鳴らさない。設定画面のテスト再生からも呼ばれる。
        public void PlayFail()
        {
            if (!IsFailSoundSet) return;
            PlayKind(0, KindFail, FailVolume, FailPitch, 0f);
        }

        // 指定の枠のクリップを、指定の音量・ピッチ・パンで鳴らす。クリップが未準備なら何もしない
        private void PlayKind(int hand, int kind, float volume, float pitch, float pan)
        {
            EnsureAudioSource();
            AudioSource? source = _sources[hand, kind];
            if (source == null) return;
            if (!source.enabled) source.enabled = true;

            AudioClip? clip = EnsureClip(hand, kind);
            if (clip == null) return;

            source.volume = Mathf.Clamp01(volume);
            source.pitch = Mathf.Clamp(pitch, 0.5f, 2.0f);
            source.panStereo = pan;
            source.PlayOneShot(clip, 1.0f);
        }

        // [hand, kind] のクリップを現在の設定に合わせて用意して返す。まだ用意できていなければ null。
        // 設定（ビープは周波数、カスタムはファイル名）が変わっていたら作り直し、古いクリップは破棄する。
        // ビープはその場で生成（軽い）、カスタム音はコルーチンで非同期に読み込む（重いのでフレームを止めない）。
        //   hand … 0=左, 1=右（フェイル音は0固定） ／ kind … KindScore/KindMiss/KindFail
        private AudioClip? EnsureClip(int hand, int kind)
        {
            bool isLeft = hand == 0;
            bool isMiss = kind == KindMiss;

            string clipName;
            float frequency;
            if (kind == KindFail)
            {
                // フェイル音は周波数を設定できないので固定値のビープにする。
                // 未設定（OFF・空欄）なら用意しない。以前に用意したクリップが残っていれば破棄する
                if (!IsFailSoundSet)
                {
                    ReleaseClip(hand, kind);
                    return null;
                }
                clipName = _config.DropSoundFailClip;
                frequency = FailBeepFrequency;
            }
            else
            {
                clipName = isMiss
                    ? (isLeft ? _config.DropSoundMissLeftClip : _config.DropSoundMissRightClip)
                    : (isLeft ? _config.DropSoundLeftClip : _config.DropSoundRightClip);
                frequency = isMiss
                    ? (isLeft ? _config.DropSoundMissLeftFrequency : _config.DropSoundMissRightFrequency)
                    : (isLeft ? _config.DropSoundLeftFrequency : _config.DropSoundRightFrequency);
            }
            if (string.IsNullOrEmpty(clipName)) clipName = BeepClipName;

            string key = clipName == BeepClipName ? $"beep:{frequency}" : $"file:{clipName}";
            if (_clips[hand, kind] != null && _clipKeys[hand, kind] == key) return _clips[hand, kind];

            if (clipName == BeepClipName)
            {
                SetClip(hand, kind, key, CreateBeep(frequency));
                return _clips[hand, kind];
            }

            // カスタム音: 読み込みが終わるまで鳴らせるクリップは無い（null を返す）。
            // 同じキーの読み込みが進行中なら二重には走らせない。
            if (_loadingKeys[hand, kind] != key)
            {
                _loadingKeys[hand, kind] = key;
                if (_runner != null)
                    _runner.StartCoroutine(LoadCustomClipCoroutine(hand, kind, key, clipName, frequency));
            }
            return null;
        }

        // 読み込み・生成が済んだクリップを所定の枠に収める。古いクリップは破棄する
        // （スクリプト生成のAudioClipは放置すると積み上がるため）
        private static void SetClip(int hand, int kind, string key, AudioClip clip)
        {
            if (_clips[hand, kind] != null) UnityEngine.Object.Destroy(_clips[hand, kind]);
            _clips[hand, kind] = clip;
            _clipKeys[hand, kind] = key;
        }

        // 枠のクリップを破棄して空にする（サウンドを未設定に戻したとき）。
        // 読み込み中の印も消すので、進行中の非同期読み込みの結果は FinishLoad で捨てられる。
        private static void ReleaseClip(int hand, int kind)
        {
            if (_clips[hand, kind] != null) UnityEngine.Object.Destroy(_clips[hand, kind]);
            _clips[hand, kind] = null;
            _clipKeys[hand, kind] = "";
            _loadingKeys[hand, kind] = "";
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

        // UserData/LRCounter/Sound から拡張子を総当たりでファイルを探す。見つからなければ null
        private static string? FindSoundFile(string clipName)
        {
            string? folder = GetSoundFolder();
            if (folder == null) return null;

            foreach (var ext in AudioExtensions)
            {
                string filePath = Path.Combine(folder, clipName + ext);
                if (File.Exists(filePath)) return filePath;
            }
            Plugin.Log?.Warn($"[LRCounter] Custom sound '{clipName}' not found in {folder}");
            return null;
        }

        // カスタム音を UnityWebRequest で非同期に読み込んで所定の枠に収める。
        // 以前はメインスレッドを Thread.Sleep で最大5秒止めていたが、それだと読み込みが走る
        // フレームが確実に飛ぶため、コルーチンで待つ方式にした。読み込み中はそのクリップだけ
        // 鳴らない（EnsureClip が null を返す）が、フレームレートには影響しない。
        // ファイルが無い・読み込みに失敗した場合はビープにフォールバックする。
        private static IEnumerator LoadCustomClipCoroutine(int hand, int kind, string key,
            string clipName, float frequency)
        {
            string? filePath = FindSoundFile(clipName);
            if (filePath == null)
            {
                FinishLoad(hand, kind, key, CreateBeep(frequency));
                yield break;
            }

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            AudioType audioType = ext switch
            {
                ".wav" => AudioType.WAV,
                ".ogg" => AudioType.OGGVORBIS,
                ".mp3" => AudioType.MPEG,
                _ => AudioType.UNKNOWN,
            };
            string uriPath = "file:///" + filePath.Replace("\\", "/");

            AudioClip? loaded = null;
            using (var request = UnityWebRequestMultimedia.GetAudioClip(uriPath, audioType))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    // GetContent は例外を投げうるので、コルーチンで yield を挟まない範囲だけ try で囲む
                    try
                    {
                        loaded = DownloadHandlerAudioClip.GetContent(request);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log?.Warn($"[LRCounter] Error loading audio '{filePath}': {ex.Message}");
                    }
                }
                else
                {
                    Plugin.Log?.Warn($"[LRCounter] Failed to load audio '{filePath}': {request.error}");
                }
            }

            FinishLoad(hand, kind, key, loaded ?? CreateBeep(frequency));
        }

        // 非同期読み込みの完了処理。待っている間に設定が変わっていたら結果は捨てる
        private static void FinishLoad(int hand, int kind, string key, AudioClip clip)
        {
            if (_loadingKeys[hand, kind] != key)
            {
                // 読み込み中に別のクリップへ切り替わった。この結果はもう使わない
                UnityEngine.Object.Destroy(clip);
                return;
            }
            _loadingKeys[hand, kind] = "";
            SetClip(hand, kind, key, clip);
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
