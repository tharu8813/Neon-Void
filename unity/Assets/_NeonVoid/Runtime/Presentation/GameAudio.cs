using System.Collections.Generic;
using NeonVoid.Combat;
using NeonVoid.Core;
using NeonVoid.Player;
using NeonVoid.Progression.Save;
using UnityEngine;

namespace NeonVoid.Presentation
{
    /// <summary>
    /// Plays the game's sounds, at the volume 전역 설정 says, through a pool that cannot grow.
    /// <para>
    /// [초안] docs/05 section 6: BGM·효과음·UI·마스터 볼륨 분리, 음소거 저장/복구, 동시 발음
    /// 제한과 오디오 풀 검증. The rules are in <see cref="AudioMix"/> and
    /// <see cref="VoiceLimiter"/>, which have no Unity in them; this is the part that owns the
    /// <see cref="AudioSource"/>s.
    /// </para>
    /// <para>
    /// <b>It reads results, it does not produce them.</b> docs/05 section 5 forbids the effects
    /// layer from touching HP, rewards or plugin rolls, and the same goes for sound: this
    /// subscribes to <see cref="CombatActor.DamageTaken"/> and plays what already happened.
    /// Nothing here is on the simulation's tick list, so a missing or failed sound cannot
    /// change a run.
    /// </para>
    /// <para>
    /// <b>Volume is applied per play, not per source.</b> A pooled source is used by a
    /// different bus the next time it is claimed, so a volume stamped on it once would leak
    /// 효과음's setting onto a UI sound.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class GameAudio : MonoBehaviour
    {
        [Tooltip("How many sounds may overlap. [초안] docs/05 section 6's 동시 발음 제한.")]
        [SerializeField] private int voices = VoiceLimiter.DraftVoiceCount;

        [Tooltip("The settings these volumes come from. Found when left empty.")]
        [SerializeField] private InputBindingDirector settingsOwner;

        [Tooltip("The vaccine, whose 피격 is the one sound that must always land.")]
        [SerializeField] private CombatActor player;

        private readonly Dictionary<AudioCue, AudioClip> _clips = new Dictionary<AudioCue, AudioClip>();
        private AudioSource[] _sources;
        private VoiceLimiter _limiter;
        private SimulationRunner _runner;
        private CombatActor _subscribed;

        /// <summary>The volumes in force. Read from 전역 설정 and written back to it.</summary>
        public AudioMix Mix { get; private set; } = AudioMix.Default;

        /// <summary>What the last <see cref="Play"/> decided. For a test and the overlay.</summary>
        public VoiceClaim LastClaim { get; private set; }

        /// <summary>How many cues were asked for, including the ones not played.</summary>
        public int Requested { get; private set; }

        /// <summary>How many actually reached a source.</summary>
        public int Played { get; private set; }

        /// <summary>The pool, so a test can count what is sounding.</summary>
        public int Voices => _sources != null ? _sources.Length : 0;

        /// <summary>Fills in what a scene would have serialized.</summary>
        public void Configure(InputBindingDirector settings, CombatActor vaccine)
        {
            settingsOwner = settings;
            player = vaccine;
        }

        private void Awake()
        {
            if (settingsOwner == null)
                settingsOwner = FindFirstObjectByType<InputBindingDirector>();

            if (player == null)
            {
                var driver = FindFirstObjectByType<PlayerSimulationDriver>();
                player = driver != null ? driver.GetComponent<CombatActor>() : null;
            }

            _limiter = new VoiceLimiter(Mathf.Max(0, voices));
            _sources = new AudioSource[Mathf.Max(0, voices)];

            for (int i = 0; i < _sources.Length; ++i)
            {
                var host = new GameObject($"Voice {i}");
                host.transform.SetParent(transform, worldPositionStays: false);
                AudioSource source = host.AddComponent<AudioSource>();
                source.playOnAwake = false;

                // 2D. The camera is overhead and the listener is on it, so panning a sound by
                // its world position would put a hit to the player's left in the left ear
                // regardless of where the player is looking - which is noise, not information.
                source.spatialBlend = 0f;
                _sources[i] = source;
            }

            ReadMix();
        }

        private void OnEnable()
        {
            _runner = _runner != null ? _runner : FindFirstObjectByType<SimulationRunner>();
            Subscribe(player);
        }

        private void OnDisable() => Subscribe(null);

        private void Subscribe(CombatActor actor)
        {
            if (_subscribed == actor)
                return;

            if (_subscribed != null)
                _subscribed.DamageTaken -= OnPlayerHurt;

            _subscribed = actor;

            if (_subscribed != null)
                _subscribed.DamageTaken += OnPlayerHurt;
        }

        private void OnPlayerHurt(CombatActor actor, DamageOutcome outcome)
        {
            // Reads a resolved outcome. Nothing here can change it.
            if (outcome.HealthRemoved > 0f)
                Play(AudioCue.PlayerHurt);
        }

        /// <summary>Re-reads 음량 from 전역 설정. Called at startup and after 설정 changes it.</summary>
        public void ReadMix()
        {
            GlobalSettingsStore settings = settingsOwner != null ? settingsOwner.Settings : null;
            Mix = settings != null ? settings.Document.Mix() : AudioMix.Default;
        }

        /// <summary>
        /// Takes a new mix for this session without writing it. A slider sends one of these
        /// per step, and a file written per step would be a hundred transactions for one drag.
        /// </summary>
        public void ApplyMix(in AudioMix mix) => Mix = mix;

        /// <summary>
        /// Writes the mix in force. Called when 설정 closes and when a 음소거 toggles, which
        /// are the moments a player has finished deciding. Returns false with a reason so the
        /// screen can say a change did not stick rather than show it as taken.
        /// </summary>
        public bool PersistMix(out string detail)
        {
            detail = string.Empty;

            GlobalSettingsStore settings = settingsOwner != null ? settingsOwner.Settings : null;
            if (settings == null)
            {
                detail = "설정 파일이 없어 음량을 기록할 수 없습니다";
                return false;
            }

            // Nothing to write, and a revision bump per close would grow the file forever.
            if (SameAs(settings.Document.Mix(), Mix))
                return true;

            settings.Document.SetMix(Mix);
            return settings.Save(out detail);
        }

        /// <summary>Changes the mix and writes it in one step.</summary>
        public bool SetMix(in AudioMix mix, out string detail)
        {
            ApplyMix(mix);
            return PersistMix(out detail);
        }

        private static bool SameAs(in AudioMix a, in AudioMix b)
        {
            for (int i = 0; i < AudioMix.All.Length; ++i)
            {
                AudioBus bus = AudioMix.All[i];
                if (a.PercentOf(bus) != b.PercentOf(bus) || a.MutedOf(bus) != b.MutedOf(bus))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Plays a cue, if the pool and the per-cue interval allow it. Silent when its bus is
        /// muted - and silent means not claiming a voice, so a muted bus cannot steal one from
        /// a bus that is audible.
        /// </summary>
        public VoiceClaim Play(AudioCue cue)
        {
            ++Requested;

            AudioBus bus = PrototypeAudioCues.BusOf(cue);
            float volume = Mix.EffectiveVolume(bus);
            if (volume <= 0f)
            {
                LastClaim = new VoiceClaim(VoiceVerdict.TooSoon, -1);
                return LastClaim;
            }

            long now = _runner != null ? _runner.TickIndex : 0L;
            VoiceClaim claim = _limiter.Claim(
                cue.ToString(),
                now,
                PrototypeAudioCues.LengthTicksOf(cue),
                PrototypeAudioCues.IntervalTicksOf(cue));

            LastClaim = claim;
            if (!claim.Ok || _sources == null || claim.Voice >= _sources.Length)
                return claim;

            AudioSource source = _sources[claim.Voice];
            if (source == null)
                return claim;

            source.Stop();
            source.clip = ClipFor(cue);
            source.volume = volume;
            source.Play();
            ++Played;

            return claim;
        }

        private AudioClip ClipFor(AudioCue cue)
        {
            if (_clips.TryGetValue(cue, out AudioClip clip) && clip != null)
                return clip;

            clip = PrototypeAudioCues.Build(cue);
            _clips[cue] = clip;
            return clip;
        }

        /// <summary>One line for the debug overlay.</summary>
        public string StatusLine() =>
            $"오디오: {Mix} · 보이스 {Voices} · 요청 {Requested} 재생 {Played}";
    }
}
