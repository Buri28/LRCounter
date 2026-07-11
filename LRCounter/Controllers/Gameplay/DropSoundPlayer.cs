using LRCounter.Configuration;
using UnityEngine;

namespace LRCounter.Controllers.Gameplay
{
    // 精度低下時に低いビープ音を鳴らすプレイヤー（WallHitSound の再生方式を流用）。
    // AudioSource は WallHitSound と同じく独立 GameObject + DontDestroyOnLoad で保持し、
    // 曲をまたいで使い回す（static で1つだけ作る）。Canvas の子にすると環境によっては
    // 再生前に破棄されうるため、実績のある独立方式に合わせた。
    internal class DropSoundPlayer
    {
        private readonly PluginConfig _config;

        // アプリ全体で1つの AudioSource / クリップ（WallHitSound と同じ常駐方式）
        // クリップは左右で周波数が異なるため別々に持つ
        private static AudioSource? _audioSource;
        private static AudioClip? _leftClip;
        private static AudioClip? _rightClip;
        private static float _leftClipFrequency;  // 生成済みクリップの周波数（設定変更を検知して作り直す）
        private static float _rightClipFrequency;

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
            Object.DontDestroyOnLoad(go);

            _audioSource = go.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0.0f;  // 2D音声（位置に依存しない）
            _audioSource.bypassEffects = false;
            _audioSource.bypassListenerEffects = false;
            _audioSource.bypassReverbZones = false;
        }

        // 精度低下フラッシュと同じタイミングで呼ぶ。左右で別の周波数のビープを鳴らす。
        // 有効/無効の判定は呼び出し側（精度低下・低スコアそれぞれのトグル）で行う。
        public void Play(bool isLeft)
        {
            EnsureAudioSource();
            if (_audioSource == null) return;
            if (!_audioSource.enabled) _audioSource.enabled = true;

            float frequency = isLeft ? _config.DropSoundLeftFrequency : _config.DropSoundRightFrequency;

            // 周波数設定が変わっていたらその手のクリップを作り直す
            AudioClip clip;
            if (isLeft)
            {
                if (_leftClip == null || _leftClipFrequency != frequency)
                {
                    _leftClip = CreateBeep(frequency);
                    _leftClipFrequency = frequency;
                }
                clip = _leftClip;
            }
            else
            {
                if (_rightClip == null || _rightClipFrequency != frequency)
                {
                    _rightClip = CreateBeep(frequency);
                    _rightClipFrequency = frequency;
                }
                clip = _rightClip;
            }

            float volume = Mathf.Clamp01(_config.DropSoundVolume);
            _audioSource.volume = volume;
            _audioSource.PlayOneShot(clip, 1.0f);
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
