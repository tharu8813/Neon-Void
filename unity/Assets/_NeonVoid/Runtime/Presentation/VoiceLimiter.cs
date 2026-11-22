using System;
using System.Collections.Generic;

namespace NeonVoid.Presentation
{
    /// <summary>What asking for a voice produced.</summary>
    public enum VoiceVerdict
    {
        /// <summary>A free voice was waiting.</summary>
        Taken = 0,

        /// <summary>No voice was free, so the one finishing soonest was taken from.</summary>
        Stolen = 1,

        /// <summary>This cue sounded too recently. Nothing is played.</summary>
        TooSoon = 2,

        /// <summary>There is no pool to play into.</summary>
        NoVoices = 3,
    }

    /// <summary>Which voice to play into, and whether to play at all.</summary>
    public readonly struct VoiceClaim
    {
        internal VoiceClaim(VoiceVerdict verdict, int voice)
        {
            Verdict = verdict;
            Voice = voice;
        }

        public VoiceVerdict Verdict { get; }

        /// <summary>The pool index to play into, or -1.</summary>
        public int Voice { get; }

        public bool Ok => Voice >= 0;

        public override string ToString() => $"{Verdict} -> {Voice}";
    }

    /// <summary>
    /// [초안] docs/05 section 6: 동시 발음 제한과 오디오 풀.
    /// <para>
    /// Two different limits, and they answer different problems. The pool caps how many sounds
    /// can overlap at all, which is about cost. The per-cue interval stops one event that
    /// happens sixty times a second - a hit, a projectile - from being played sixty times,
    /// which is about it being audible: a hundred copies of one click is not a loud click, it
    /// is a buzz.
    /// </para>
    /// <para>
    /// <b>A full pool steals rather than drops.</b> The voice taken is the one finishing
    /// soonest, so the sound that is cut is the one nearest its end. Dropping the new one
    /// instead would make a busy moment go quiet exactly when the most is happening.
    /// </para>
    /// <para>
    /// No Unity types and no clock: time is a tick the caller passes in, so the rule can be
    /// driven to its edges without a device.
    /// </para>
    /// </summary>
    public sealed class VoiceLimiter
    {
        private readonly long[] _freeAtTick;
        private readonly Dictionary<string, long> _lastPlayedTick;
        private readonly int _defaultIntervalTicks;

        /// <summary>
        /// [초안] 60 Hz ticks between two plays of the same cue. Six is a tenth of a second,
        /// which is about where two clicks stop sounding like two.
        /// </summary>
        public const int DraftSameCueIntervalTicks = 6;

        /// <summary>[초안] How many sounds may overlap. Raised when something needs it.</summary>
        public const int DraftVoiceCount = 16;

        public VoiceLimiter(int voices = DraftVoiceCount, int sameCueIntervalTicks = DraftSameCueIntervalTicks)
        {
            if (voices < 0)
                throw new ArgumentOutOfRangeException(nameof(voices));

            _freeAtTick = new long[voices];
            for (int i = 0; i < voices; ++i)
                _freeAtTick[i] = long.MinValue;

            _defaultIntervalTicks = Math.Max(0, sameCueIntervalTicks);
            _lastPlayedTick = new Dictionary<string, long>(StringComparer.Ordinal);
        }

        /// <summary>How many sounds may overlap.</summary>
        public int Voices => _freeAtTick.Length;

        /// <summary>How many are sounding at this tick.</summary>
        public int Busy(long nowTick)
        {
            int busy = 0;
            for (int i = 0; i < _freeAtTick.Length; ++i)
            {
                if (_freeAtTick[i] > nowTick)
                    ++busy;
            }

            return busy;
        }

        /// <summary>
        /// Asks for a voice for one cue.
        /// </summary>
        /// <param name="cueId">What is being played; the per-cue interval is keyed on it.</param>
        /// <param name="nowTick">The current logic tick.</param>
        /// <param name="lengthTicks">How long it will sound, so the voice frees itself.</param>
        /// <param name="intervalTicks">Overrides the default interval for this cue.</param>
        public VoiceClaim Claim(string cueId, long nowTick, int lengthTicks, int intervalTicks = -1)
        {
            if (_freeAtTick.Length == 0)
                return new VoiceClaim(VoiceVerdict.NoVoices, -1);

            string key = cueId ?? string.Empty;
            int interval = intervalTicks >= 0 ? intervalTicks : _defaultIntervalTicks;

            if (_lastPlayedTick.TryGetValue(key, out long last) && nowTick - last < interval)
                return new VoiceClaim(VoiceVerdict.TooSoon, -1);

            int chosen = -1;
            for (int i = 0; i < _freeAtTick.Length; ++i)
            {
                if (_freeAtTick[i] > nowTick)
                    continue;

                chosen = i;
                break;
            }

            VoiceVerdict verdict = VoiceVerdict.Taken;
            if (chosen < 0)
            {
                // All busy. Take the one closest to finishing; cutting a sound near its end is
                // less wrong than dropping a sound that has not started.
                chosen = 0;
                for (int i = 1; i < _freeAtTick.Length; ++i)
                {
                    if (_freeAtTick[i] < _freeAtTick[chosen])
                        chosen = i;
                }

                verdict = VoiceVerdict.Stolen;
            }

            _freeAtTick[chosen] = nowTick + Math.Max(1, lengthTicks);
            _lastPlayedTick[key] = nowTick;
            return new VoiceClaim(verdict, chosen);
        }

        /// <summary>Forgets everything, for a scene that starts again.</summary>
        public void Clear()
        {
            for (int i = 0; i < _freeAtTick.Length; ++i)
                _freeAtTick[i] = long.MinValue;

            _lastPlayedTick.Clear();
        }
    }
}
