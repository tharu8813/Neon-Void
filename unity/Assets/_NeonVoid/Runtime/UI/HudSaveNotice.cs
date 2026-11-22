using NeonVoid.Progression.Save;

namespace NeonVoid.UI
{
    /// <summary>
    /// What the HUD says about whether this session is being written down.
    /// <para>
    /// It exists because permanent writes are off by default, and a session that quietly does
    /// not record itself is indistinguishable from one that does until the player quits and
    /// finds out. The one line costs nothing and answers it.
    /// </para>
    /// <para>
    /// <b>The chosen case and the broken cases do not read alike.</b> Writes switched off is a
    /// setting, stated plainly. A slot that could not be opened, or one that stopped accepting
    /// writes part way through a session that had been counting, is a failure - and a player
    /// who reads it as the ordinary notice would keep playing a run they believe is being kept.
    /// </para>
    /// </summary>
    public readonly struct HudSaveNotice
    {
        /// <summary>Nothing to say, because the session is being written down.</summary>
        public static readonly HudSaveNotice None = default;

        private HudSaveNotice(string text, bool isFailure)
        {
            Text = text;
            IsFailure = isFailure;
        }

        /// <summary>The line to draw, or empty when there is nothing to say.</summary>
        public string Text { get; }

        /// <summary>True when this is something gone wrong rather than something chosen.</summary>
        public bool IsFailure { get; }

        /// <summary>True when the line should be on screen at all.</summary>
        public bool Shown => !string.IsNullOrEmpty(Text);

        /// <summary>The line for a save in the given state.</summary>
        public static HudSaveNotice For(SaveCommitReadiness readiness)
        {
            switch (readiness)
            {
                case SaveCommitReadiness.SwitchedOff:
                    return new HudSaveNotice("진행 저장 꺼짐", isFailure: false);

                case SaveCommitReadiness.Unavailable:
                    return new HudSaveNotice("저장 연결 실패 · 진행이 기록되지 않습니다", isFailure: true);

                case SaveCommitReadiness.Halted:
                    return new HudSaveNotice("저장 중단 · 진행이 기록되지 않습니다", isFailure: true);

                default:
                    return None;
            }
        }

        public override string ToString() => Shown ? Text : "저장 중";
    }
}
