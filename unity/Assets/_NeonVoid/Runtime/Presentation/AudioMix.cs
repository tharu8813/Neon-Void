using System;

namespace NeonVoid.Presentation
{
    /// <summary>
    /// [초안] docs/05 section 6: BGM·효과음·UI·마스터 볼륨 분리.
    /// <para>
    /// Four, because that is the list. 마스터 is not a fifth sound - it multiplies the other
    /// three - and that difference is the whole reason this is an enum rather than a number
    /// per source.
    /// </para>
    /// </summary>
    public enum AudioBus
    {
        /// <summary>Multiplies every other bus. Not a sound of its own.</summary>
        Master = 0,

        /// <summary>BGM.</summary>
        Music = 1,

        /// <summary>효과음: combat, the world, everything that happens.</summary>
        Effects = 2,

        /// <summary>UI: menus, confirmations, the terminal.</summary>
        Ui = 3,
    }

    /// <summary>
    /// How loud each bus is and whether it is muted.
    /// <para>
    /// [초안] docs/05 section 6 asks for 음소거 저장/복구 beside the volumes, and the two are
    /// different things on purpose: muting and then unmuting has to give the player the volume
    /// they had, which a mute implemented as "set it to zero" cannot.
    /// </para>
    /// <para>
    /// Volumes are whole percents. The settings file's checksum is verified by re-serializing
    /// what was read back, so a float whose text form round-trips differently on another
    /// runtime would make a healthy file look corrupt there and nowhere else - the same reason
    /// the save document holds no floating point numbers.
    /// </para>
    /// </summary>
    public readonly struct AudioMix
    {
        /// <summary>Everything at full, nothing muted.</summary>
        public const int FullPercent = 100;

        private readonly int _master;
        private readonly int _music;
        private readonly int _effects;
        private readonly int _ui;
        private readonly bool _hasValues;

        private AudioMix(
            int master, int music, int effects, int ui,
            bool masterMuted, bool musicMuted, bool effectsMuted, bool uiMuted)
        {
            _master = Clamp(master);
            _music = Clamp(music);
            _effects = Clamp(effects);
            _ui = Clamp(ui);
            MasterMuted = masterMuted;
            MusicMuted = musicMuted;
            EffectsMuted = effectsMuted;
            UiMuted = uiMuted;
            _hasValues = true;
        }

        /// <summary>
        /// What a player who has never opened 설정 hears. Also what a default struct reads as,
        /// so a mix nobody configured is full volume rather than silence.
        /// </summary>
        public static AudioMix Default =>
            new AudioMix(FullPercent, FullPercent, FullPercent, FullPercent, false, false, false, false);

        public bool MasterMuted { get; }
        public bool MusicMuted { get; }
        public bool EffectsMuted { get; }
        public bool UiMuted { get; }

        /// <summary>One bus's own setting, 0~100, before 마스터 is applied.</summary>
        public int PercentOf(AudioBus bus)
        {
            if (!_hasValues)
                return FullPercent;

            switch (bus)
            {
                case AudioBus.Master: return _master;
                case AudioBus.Music: return _music;
                case AudioBus.Effects: return _effects;
                default: return _ui;
            }
        }

        /// <summary>Whether that bus is muted on its own.</summary>
        public bool MutedOf(AudioBus bus)
        {
            switch (bus)
            {
                case AudioBus.Master: return MasterMuted;
                case AudioBus.Music: return MusicMuted;
                case AudioBus.Effects: return EffectsMuted;
                default: return UiMuted;
            }
        }

        /// <summary>
        /// What an AudioSource on that bus should actually be set to, 0~1: the bus times
        /// 마스터, and zero when either of them is muted.
        /// </summary>
        public float EffectiveVolume(AudioBus bus)
        {
            if (MasterMuted || MutedOf(bus))
                return 0f;

            float master = PercentOf(AudioBus.Master) / (float)FullPercent;
            if (bus == AudioBus.Master)
                return master;

            return master * (PercentOf(bus) / (float)FullPercent);
        }

        /// <summary>True when nothing on this bus would be heard.</summary>
        public bool IsSilent(AudioBus bus) => EffectiveVolume(bus) <= 0f;

        public AudioMix WithPercent(AudioBus bus, int percent)
        {
            int master = PercentOf(AudioBus.Master);
            int music = PercentOf(AudioBus.Music);
            int effects = PercentOf(AudioBus.Effects);
            int ui = PercentOf(AudioBus.Ui);

            switch (bus)
            {
                case AudioBus.Master: master = percent; break;
                case AudioBus.Music: music = percent; break;
                case AudioBus.Effects: effects = percent; break;
                default: ui = percent; break;
            }

            return new AudioMix(
                master, music, effects, ui, MasterMuted, MusicMuted, EffectsMuted, UiMuted);
        }

        public AudioMix WithMuted(AudioBus bus, bool muted)
        {
            bool master = MasterMuted;
            bool music = MusicMuted;
            bool effects = EffectsMuted;
            bool ui = UiMuted;

            switch (bus)
            {
                case AudioBus.Master: master = muted; break;
                case AudioBus.Music: music = muted; break;
                case AudioBus.Effects: effects = muted; break;
                default: ui = muted; break;
            }

            return new AudioMix(
                PercentOf(AudioBus.Master), PercentOf(AudioBus.Music),
                PercentOf(AudioBus.Effects), PercentOf(AudioBus.Ui),
                master, music, effects, ui);
        }

        /// <summary>Rebuilds a mix from what a settings file holds.</summary>
        public static AudioMix From(
            int master, int music, int effects, int ui,
            bool masterMuted, bool musicMuted, bool effectsMuted, bool uiMuted) =>
            new AudioMix(master, music, effects, ui, masterMuted, musicMuted, effectsMuted, uiMuted);

        /// <summary>The buses in the order 설정 lists them, 마스터 first.</summary>
        public static readonly AudioBus[] All =
        {
            AudioBus.Master, AudioBus.Music, AudioBus.Effects, AudioBus.Ui,
        };

        /// <summary>What 설정 calls each bus.</summary>
        public static string LabelOf(AudioBus bus)
        {
            switch (bus)
            {
                case AudioBus.Master: return "마스터";
                case AudioBus.Music: return "음악";
                case AudioBus.Effects: return "효과음";
                default: return "UI";
            }
        }

        private static int Clamp(int percent) =>
            percent < 0 ? 0 : (percent > FullPercent ? FullPercent : percent);

        public override string ToString() =>
            $"마스터 {PercentOf(AudioBus.Master)}{(MasterMuted ? " (음소거)" : string.Empty)}" +
            $" · 음악 {PercentOf(AudioBus.Music)}{(MusicMuted ? " (음소거)" : string.Empty)}" +
            $" · 효과음 {PercentOf(AudioBus.Effects)}{(EffectsMuted ? " (음소거)" : string.Empty)}" +
            $" · UI {PercentOf(AudioBus.Ui)}{(UiMuted ? " (음소거)" : string.Empty)}";
    }
}
