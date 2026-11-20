using System;
using System.Collections.Generic;

namespace NeonVoid.Missions
{
    /// <summary>Whether permanent state took a change, and what to say if it did not.</summary>
    public readonly struct MissionCommitResult
    {
        /// <summary>True only when permanent state was written and verified.</summary>
        public readonly bool Committed;

        /// <summary>What happened, for the log and the recovery screen. Never empty.</summary>
        public readonly string Detail;

        private MissionCommitResult(bool committed, string detail)
        {
            Committed = committed;
            Detail = string.IsNullOrEmpty(detail) ? "unspecified" : detail;
        }

        public static MissionCommitResult Ok(string detail) => new MissionCommitResult(true, detail);

        public static MissionCommitResult Refused(string detail) => new MissionCommitResult(false, detail);
    }

    /// <summary>
    /// The seam between the mission flow and permanent state.
    /// <para>
    /// <b>Synchronous, unlike <see cref="Progression.IDeathCommitService"/>.</b> That one is
    /// asynchronous because nothing implements it and a death's consequences must not be mocked
    /// into looking finished. This one has a real implementation over a store whose commit is
    /// synchronous and self-verifying - it re-reads the file it wrote - so a transition can ask
    /// and be told in the same call. If a gate ever needs to answer later, the flow will need a
    /// pending phase per transition; building one now would be a state nothing exercises.
    /// </para>
    /// <para>
    /// The flow calls this <b>before</b> it advances, and does not advance when the answer is
    /// <see cref="MissionCommitResult.Refused"/>. That ordering is docs/04 section 5 step 7 -
    /// 그 다음에만 UI·전투·성장 서비스에 반영 - and it is in the state machine rather than in a
    /// MonoBehaviour precisely so it can be tested.
    /// </para>
    /// </summary>
    public interface IMissionCommitGate
    {
        /// <summary>`CleanseHardware(area, receiptId)`. Must be idempotent per run and area.</summary>
        MissionCommitResult CommitPurification(long runId, string areaSaveKey);

        /// <summary>`ExtractRun(receiptId, learnIds)`. Must be idempotent per run.</summary>
        MissionCommitResult CommitExtraction(
            long runId, int purifiedAreas, int objectivesCompleted, IReadOnlyList<string> learnedInstanceIds);
    }

    /// <summary>
    /// The gate for a scene with no save connected. <b>Prototype</b>, as CLAUDE.md requires such
    /// a thing to say.
    /// <para>
    /// Purification is allowed, because what a purification grants is 의뢰 XP and that is
    /// temporary ([확정] C09) - it is lost with the process either way, so letting it through
    /// costs the player nothing and lets a prototype scene be played.
    /// </para>
    /// <para>
    /// <b>Extraction is refused.</b> Everything an extraction produces is permanent: 영구 XP,
    /// 성장 포인트, 신규 학습. An extraction that reported success with nothing written would
    /// teach the UI to show a reward that was never granted, which is the exact failure docs/04
    /// section 7 lists under 저장 실패 → 금지: UI 먼저 '보상 획득 완료'. So a mission cannot be
    /// completed without a save, which is honest: completing a mission <i>is</i> writing one.
    /// </para>
    /// </summary>
    public sealed class PrototypeMissionCommits : IMissionCommitGate
    {
        public static readonly PrototypeMissionCommits Instance = new PrototypeMissionCommits();

        public MissionCommitResult CommitPurification(long runId, string areaSaveKey) =>
            MissionCommitResult.Ok(
                $"prototype: {areaSaveKey} purified in run {runId}, and nothing was written. " +
                "의뢰 XP is temporary, so this costs the player nothing.");

        public MissionCommitResult CommitExtraction(
            long runId, int purifiedAreas, int objectivesCompleted, IReadOnlyList<string> learnedInstanceIds)
        {
            int learned = learnedInstanceIds?.Count ?? 0;
            return MissionCommitResult.Refused(
                $"no save is connected, so run {runId}'s extraction cannot be committed " +
                $"({purifiedAreas} areas, {objectivesCompleted} objectives, {learned} learned). " +
                "영구 XP·포인트·학습은 저장 커밋 승인 후에만 확정된다 (docs/04 section 5).");
        }

        public override string ToString() => "PrototypeMissionCommits (writes nothing, refuses extraction)";
    }
}
