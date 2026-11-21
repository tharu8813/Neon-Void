using System;

namespace NeonVoid.UI
{
    /// <summary>What the player can do where they are standing, in one line.</summary>
    public readonly struct HudPrompt
    {
        /// <summary>What the thing is called. Empty when there is nothing in reach.</summary>
        public readonly string Subject;

        /// <summary>
        /// What happens, or why nothing does. A terminal that is not connected yet says so
        /// here rather than being left off the screen - a pillar the player walks up to and
        /// gets no answer from reads as a bug.
        /// </summary>
        public readonly string Detail;

        /// <summary>Whether acting on it does anything right now.</summary>
        public readonly bool Available;

        /// <summary>
        /// Whether this is something held rather than pressed. The 정화 콘솔, the 철수 패드 and
        /// the 메인보드 포털 are all held.
        /// <para>
        /// Its own field rather than a sentinel in <see cref="Progress"/>, because a struct's
        /// default has to mean "nothing in reach" - and a sentinel of -1 cannot be the default
        /// of a float, so <see cref="None"/> read as a dwell sitting at 0%.
        /// </para>
        /// </summary>
        public readonly bool IsDwell;

        /// <summary>0 to 1 while something is being held. Meaningless unless <see cref="IsDwell"/>.</summary>
        public readonly float Progress;

        public HudPrompt(string subject, string detail, bool available)
        {
            Subject = subject ?? string.Empty;
            Detail = detail ?? string.Empty;
            Available = available;
            IsDwell = false;
            Progress = 0f;
        }

        private HudPrompt(string subject, string detail, float progress)
        {
            Subject = subject ?? string.Empty;
            Detail = detail ?? string.Empty;
            Available = true;
            IsDwell = true;
            Progress = HudSnapshot.Clamp01(progress);
        }

        /// <summary>Nothing is in reach.</summary>
        public static HudPrompt None => default;

        public bool HasSubject => !string.IsNullOrEmpty(Subject);

        /// <summary>
        /// A prompt for something the player holds rather than presses, with how far along
        /// they are.
        /// </summary>
        public static HudPrompt Holding(string subject, string detail, float progress) =>
            new HudPrompt(subject, detail, progress);

        /// <summary>A prompt for something that is there and does nothing yet, and says why.</summary>
        public static HudPrompt Unavailable(string subject, string why) =>
            new HudPrompt(subject, why, false);

        public override string ToString() =>
            HasSubject ? $"{Subject}: {Detail}" : "nothing in reach";
    }
}
