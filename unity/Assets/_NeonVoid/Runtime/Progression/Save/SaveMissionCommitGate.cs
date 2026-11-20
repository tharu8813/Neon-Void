using System;
using System.Collections.Generic;
using NeonVoid.Missions;

namespace NeonVoid.Progression.Save
{
    /// <summary>
    /// <see cref="IMissionCommitGate"/> over a real <see cref="SaveStore"/>: the mission flow's
    /// 정화 and 철수 become `CleanseHardware` and `ExtractRun`.
    /// <para>
    /// This is the only place where the flow's progress becomes permanent, and it reports nothing
    /// as committed that the store did not acknowledge. <see cref="SaveCommitResult.Acknowledged"/>
    /// covers Committed and AlreadyApplied, which is right for both: a retry after a lost ack is
    /// the same purification, not a second one, and docs/04 section 7 asks for exactly that -
    /// 저장 성공 ACK 유실 → 기존 영수증 조회.
    /// </para>
    /// <para>
    /// <b>Nothing is thrown at the flow.</b> A refused transition is a state the flow reports;
    /// an exception escaping a gate would be a state it cannot. The command constructors do
    /// throw - on more than two learns, on a settlement with no purification - so they are
    /// built inside a guard and a throw becomes a refusal with the message attached.
    /// </para>
    /// </summary>
    public sealed class SaveMissionCommitGate : IMissionCommitGate
    {
        private readonly SaveStore _store;

        public SaveMissionCommitGate(SaveStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>
        /// 성장 데이터 캐시 collected this mission, worth a 성장 포인트 each ([초안 v0.1] GDD v1
        /// section 5.6).
        /// <para>
        /// Settable and zero by default, because nothing in the game drops one yet: the caches
        /// appear on a 40% per-area roll, and a roll belongs to the mission's reward stream where
        /// it is reproducible from the run seed. Zero is the honest figure until something rolls
        /// them, rather than a number invented at settlement time.
        /// </para>
        /// </summary>
        public int GrowthCachesCollected { get; set; }

        public MissionCommitResult CommitPurification(long runId, string areaSaveKey)
        {
            CleanseHardwareCommand command;
            try
            {
                command = new CleanseHardwareCommand(runId, areaSaveKey);
            }
            catch (Exception e)
            {
                return MissionCommitResult.Refused($"CleanseHardware could not be built: {e.Message}");
            }

            SaveCommitResult result = _store.Commit(command);
            return result.Acknowledged
                ? MissionCommitResult.Ok($"{result.Verdict}: {result.Result}")
                : MissionCommitResult.Refused($"{result.Verdict}: {result.Detail}");
        }

        public MissionCommitResult CommitExtraction(
            long runId, int purifiedAreas, int objectivesCompleted, IReadOnlyList<string> learnedInstanceIds)
        {
            ExtractRunCommand command;
            try
            {
                ExtractionSettlement settlement = ExtractionSettlementPolicy.Draft(
                    purifiedAreas, objectivesCompleted, GrowthCachesCollected);
                command = new ExtractRunCommand(runId, settlement, learnedInstanceIds);
            }
            catch (Exception e)
            {
                return MissionCommitResult.Refused($"ExtractRun could not be built: {e.Message}");
            }

            SaveCommitResult result = _store.Commit(command);
            return result.Acknowledged
                ? MissionCommitResult.Ok($"{result.Verdict}: {result.Result}")
                : MissionCommitResult.Refused($"{result.Verdict}: {result.Detail}");
        }

        public override string ToString() => $"SaveMissionCommitGate({_store.State})";
    }
}
