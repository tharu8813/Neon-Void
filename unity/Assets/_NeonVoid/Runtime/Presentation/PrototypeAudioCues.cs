using System;
using UnityEngine;

namespace NeonVoid.Presentation
{
    /// <summary>What a sound is for. The cue id is what the voice limiter keys on.</summary>
    public enum AudioCue
    {
        /// <summary>The vaccine was hit.</summary>
        PlayerHurt = 0,

        /// <summary>A shot landed on something.</summary>
        Hit = 1,

        /// <summary>대시.</summary>
        Dash = 2,

        /// <summary>
        /// A boss is about to do something. [초안] docs/05 section 6 wants 보스 경고 to have a
        /// face of its own, and section 6 also says 위험 공격은 음향 없이도 피할 수 있어야
        /// 한다 - so this is a second channel on a warning the screen already gives, never the
        /// only one.
        /// </summary>
        BossWarning = 3,

        /// <summary>A UI element was activated.</summary>
        UiClick = 4,

        /// <summary>A purchase, a rebind, anything the player committed.</summary>
        UiConfirm = 5,

        /// <summary>Something was refused.</summary>
        UiRefuse = 6,
    }

    /// <summary>
    /// <b>Prototype.</b> Placeholder sounds, synthesized rather than loaded.
    /// <para>
    /// docs/05 section 6 allows external SFX but forbids 원곡 무단 샘플링 and files whose
    /// 배포권 is unclear, and CLAUDE.md wants a licence recorded in third_party before any
    /// asset is used. None of that has happened, so rather than ship silence - which would
    /// leave the 음량 settings controlling nothing and untestable - these are generated from
    /// a few sine waves at load. They are placeholders and are meant to be replaced; the point
    /// of them is that the buses, the pool and the volumes are real.
    /// </para>
    /// <para>
    /// Everything is generated once and reused. Each clip is a fraction of a second of mono
    /// 44.1 kHz, so the whole set is well under a megabyte of memory.
    /// </para>
    /// </summary>
    public static class PrototypeAudioCues
    {
        /// <summary>Samples per second. Unity's own default output rate.</summary>
        public const int SampleRate = 44100;

        /// <summary>Which bus a cue belongs on.</summary>
        public static AudioBus BusOf(AudioCue cue)
        {
            switch (cue)
            {
                case AudioCue.UiClick:
                case AudioCue.UiConfirm:
                case AudioCue.UiRefuse:
                    return AudioBus.Ui;

                default:
                    return AudioBus.Effects;
            }
        }

        /// <summary>
        /// How long the cue sounds, in 60 Hz ticks, so the voice limiter can free the voice
        /// without asking the engine.
        /// </summary>
        public static int LengthTicksOf(AudioCue cue) =>
            Mathf.Max(1, Mathf.CeilToInt(SecondsOf(cue) * 60f));

        /// <summary>
        /// [초안] How soon the same cue may sound again. 피격 and 명중 are the two that happen
        /// many times a second, and a hundred copies of one click is a buzz rather than a loud
        /// click.
        /// </summary>
        public static int IntervalTicksOf(AudioCue cue)
        {
            switch (cue)
            {
                case AudioCue.Hit: return 4;
                case AudioCue.PlayerHurt: return 8;
                case AudioCue.BossWarning: return 30;
                default: return VoiceLimiter.DraftSameCueIntervalTicks;
            }
        }

        private static float SecondsOf(AudioCue cue)
        {
            switch (cue)
            {
                case AudioCue.BossWarning: return 0.46f;
                case AudioCue.Dash: return 0.22f;
                case AudioCue.PlayerHurt: return 0.26f;
                case AudioCue.UiRefuse: return 0.18f;
                default: return 0.12f;
            }
        }

        /// <summary>
        /// The clip for a cue, built once. [초안] every number in here is a placeholder tuned
        /// by ear against nothing; the shapes are chosen so the seven are told apart.
        /// </summary>
        public static AudioClip Build(AudioCue cue)
        {
            switch (cue)
            {
                case AudioCue.PlayerHurt:
                    // Low, noisy, falling: the one sound that has to be unmistakable.
                    return Render(cue, 180f, 90f, noise: 0.45f, decay: 14f);

                case AudioCue.Hit:
                    return Render(cue, 760f, 520f, noise: 0.18f, decay: 34f);

                case AudioCue.Dash:
                    // Rising, so it reads as going somewhere rather than being hit.
                    return Render(cue, 260f, 720f, noise: 0.22f, decay: 16f);

                case AudioCue.BossWarning:
                    return TwoTone(cue, 330f, 247f);

                case AudioCue.UiConfirm:
                    return TwoTone(cue, 660f, 880f);

                case AudioCue.UiRefuse:
                    return Render(cue, 300f, 150f, noise: 0.05f, decay: 18f);

                default:
                    return Render(cue, 880f, 880f, noise: 0f, decay: 40f);
            }
        }

        /// <summary>A tone sliding from one pitch to another, with noise and an exponential decay.</summary>
        private static AudioClip Render(AudioCue cue, float fromHz, float toHz, float noise, float decay)
        {
            float seconds = SecondsOf(cue);
            int samples = Mathf.Max(1, Mathf.CeilToInt(seconds * SampleRate));
            var data = new float[samples];

            // Seeded per cue, so the noise is the same every launch: a placeholder that sounds
            // different each run is a placeholder nobody can tell has changed.
            var random = new System.Random((int)cue * 7919 + 17);
            double phase = 0.0;

            for (int i = 0; i < samples; ++i)
            {
                float t = i / (float)samples;
                float hz = Mathf.Lerp(fromHz, toHz, t);
                phase += 2.0 * Math.PI * hz / SampleRate;

                float tone = (float)Math.Sin(phase);
                float grit = noise > 0f ? (float)(random.NextDouble() * 2.0 - 1.0) * noise : 0f;
                float envelope = Mathf.Exp(-decay * t) * Fade(i, samples);

                data[i] = Mathf.Clamp((tone + grit) * envelope * 0.6f, -1f, 1f);
            }

            return Make(cue, data);
        }

        /// <summary>Two notes in sequence, which reads as a signal rather than an impact.</summary>
        private static AudioClip TwoTone(AudioCue cue, float firstHz, float secondHz)
        {
            float seconds = SecondsOf(cue);
            int samples = Mathf.Max(2, Mathf.CeilToInt(seconds * SampleRate));
            int half = samples / 2;
            var data = new float[samples];

            double phase = 0.0;
            for (int i = 0; i < samples; ++i)
            {
                bool second = i >= half;
                float hz = second ? secondHz : firstHz;
                int within = second ? i - half : i;
                int length = second ? samples - half : half;

                phase += 2.0 * Math.PI * hz / SampleRate;
                float t = within / (float)Mathf.Max(1, length);
                float envelope = Mathf.Exp(-9f * t) * Fade(within, length);

                data[i] = Mathf.Clamp((float)Math.Sin(phase) * envelope * 0.55f, -1f, 1f);
            }

            return Make(cue, data);
        }

        /// <summary>
        /// A couple of milliseconds of ramp at each end. Without it the buffer starts and stops
        /// at a non-zero sample and every play has a click on it that is louder than the sound.
        /// </summary>
        private static float Fade(int index, int length)
        {
            const int ramp = 64;
            if (length <= ramp * 2)
                return 1f;

            if (index < ramp)
                return index / (float)ramp;

            if (index > length - ramp)
                return Mathf.Max(0f, (length - index) / (float)ramp);

            return 1f;
        }

        private static AudioClip Make(AudioCue cue, float[] data)
        {
            AudioClip clip = AudioClip.Create(
                $"Prototype_{cue}", data.Length, channels: 1, frequency: SampleRate, stream: false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
