using System;

namespace NeonVoid.Missions
{
    /// <summary>Where a mission stands while it is being played.</summary>
    public enum MissionPhase
    {
        /// <summary>No mission is running.</summary>
        None = 0,

        /// <summary>Being played.</summary>
        InProgress = 1,

        /// <summary>
        /// The player died. [확정] docs/00: 사망은 해당 의뢰 실패이며 실패 의뢰 전투를 그대로
        /// 재시작하지 않는다 - a failed mission is not resumed, the next one is new.
        /// </summary>
        Failed = 2,
    }

    /// <summary>
    /// The live state of one mission: which run it is, and what has happened in it.
    /// <para>
    /// <b>In memory only, on purpose.</b> docs/04 lists 의뢰 상태 - runId, 시드, 정화,
    /// 임시 Lv/XP, 임시 플러그인, 리롤, 천장, 환경, 맵 생성 버전, 미정산 보상 - as saved state
    /// behind an approved commit. The fields here are the ones the combat layer already needs
    /// and can hold honestly; the rest are absent rather than stubbed, so nothing reads as
    /// implemented that is not. Nothing in this type writes to disk.
    /// </para>
    /// <para>
    /// Failure is one-way. A mission that could be un-failed would let a death be walked back
    /// without the save service ever hearing about it, which is the same hole the death ledger
    /// exists to close.
    /// </para>
    /// Pure C#: no Unity types, no clock, no storage.
    /// </summary>
    public sealed class MissionRunState
    {
        public RunIdentity Identity { get; private set; }

        public MissionPhase Phase { get; private set; } = MissionPhase.None;

        /// <summary>Purifications completed in this mission (정화).</summary>
        public int PurificationsCompleted { get; private set; }

        /// <summary>Simulation tick the mission began on, for elapsed time and for logs.</summary>
        public long StartedOnTick { get; private set; }

        public bool IsRunning => Phase == MissionPhase.InProgress;

        /// <summary>
        /// Begins a mission. Refuses to start on top of a running one: two missions sharing a
        /// run identity would produce two death receipts with the same key.
        /// </summary>
        public bool TryBegin(in RunIdentity identity, long onTick)
        {
            if (!identity.IsValid || Phase == MissionPhase.InProgress || onTick < 0L)
                return false;

            Identity = identity;
            Phase = MissionPhase.InProgress;
            PurificationsCompleted = 0;
            StartedOnTick = onTick;
            return true;
        }

        /// <summary>Records one purification. Refused unless a mission is actually running.</summary>
        public bool TryRecordPurification()
        {
            if (!IsRunning)
                return false;

            PurificationsCompleted++;
            return true;
        }

        /// <summary>
        /// Marks the mission failed, which is what a player death means. Idempotent: a second
        /// death report cannot fail a mission twice, for the same reason the death ledger
        /// refuses a second death.
        /// </summary>
        public bool TryFail()
        {
            if (!IsRunning)
                return false;

            Phase = MissionPhase.Failed;
            return true;
        }

        /// <summary>
        /// The seed a stream should start from in this mission, or the stream's seed for the
        /// absent run when nothing is running.
        /// </summary>
        public uint SeedFor(RandomStream stream) =>
            Identity.IsValid ? Identity.SeedFor(stream) : 0u;

        public override string ToString() =>
            $"{Identity} {Phase} purified={PurificationsCompleted} startedTick={StartedOnTick}";
    }
}
