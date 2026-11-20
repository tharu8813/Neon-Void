using System;
using NeonVoid.Progression;

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
        /// <summary>
        /// [확정] docs/04 section 1 and C09 in docs/08: 새 의뢰 Lv.1. The mission level is a
        /// different quantity from the vaccine's permanent level and the two must never be
        /// mixed into one variable, so this one restarts here on every <see cref="TryBegin"/>.
        /// </summary>
        public const int DesignStartingMissionLevel = 1;

        private readonly LevelCurve _missionLevels;

        public MissionRunState()
            : this(ProgressionCurves.DraftMissionLevels)
        {
        }

        /// <summary>
        /// Takes its curve, so the [초안] numbers of docs/04 section 1 can be retuned - or a
        /// test can use a curve it can state in one line - without this type knowing any of them.
        /// </summary>
        public MissionRunState(LevelCurve missionLevels)
        {
            if (!missionLevels.IsValid)
                throw new ArgumentException("A mission needs a constructed level curve.", nameof(missionLevels));

            _missionLevels = missionLevels;
        }

        public RunIdentity Identity { get; private set; }

        public MissionPhase Phase { get; private set; } = MissionPhase.None;

        /// <summary>Purifications completed in this mission (정화).</summary>
        public int PurificationsCompleted { get; private set; }

        /// <summary>Simulation tick the mission began on, for elapsed time and for logs.</summary>
        public long StartedOnTick { get; private set; }

        /// <summary>
        /// 의뢰 레벨. [확정] starts at <see cref="DesignStartingMissionLevel"/> every mission and
        /// buys nothing permanent: docs/04 section 1 makes RunXP 임시 카드 지급 and VaccineXP the
        /// permanent score.
        /// </summary>
        public int MissionLevel { get; private set; } = DesignStartingMissionLevel;

        /// <summary>Cumulative mission XP in this mission. Dies with it.</summary>
        public long MissionXp { get; private set; }

        /// <summary>The curve this mission's levels are read off.</summary>
        public LevelCurve MissionLevels => _missionLevels;

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

            // [확정] 새 의뢰 Lv.1. Reset here rather than only in the constructor, because this
            // object outlives one mission: a second TryBegin that kept the previous mission's
            // level would carry temporary progress into a run that is supposed to start over,
            // and carrying it is precisely what C09 separates the two levels to prevent.
            MissionLevel = DesignStartingMissionLevel;
            MissionXp = 0L;
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
        /// Awards mission XP (RunXP) and levels the mission up as far as it reaches.
        /// <para>
        /// Refused unless a mission is running: XP arriving after a death would raise the level
        /// of a mission that has already failed, and <see cref="MissionPhase.Failed"/> is
        /// one-way for the same reason.
        /// </para>
        /// <para>
        /// Several levels at once is normal, not an edge case - the [초안] numbers of docs/04
        /// section 1 put one cleared area at 720 XP against a first level costing 100 - so this
        /// reports how many were gained rather than assuming one.
        /// </para>
        /// </summary>
        public bool TryAwardMissionXp(long amount, out int levelsGained)
        {
            levelsGained = 0;
            if (!IsRunning || amount < 0L)
                return false;

            MissionXp += amount;
            int reached = _missionLevels.AdvanceFrom(MissionLevel, MissionXp);
            levelsGained = reached - MissionLevel;
            MissionLevel = reached;
            return true;
        }

        /// <summary>Awards mission XP without asking how many levels it produced.</summary>
        public bool TryAwardMissionXp(long amount) => TryAwardMissionXp(amount, out _);

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
            $"{Identity} {Phase} Lv.{MissionLevel} xp={MissionXp} purified={PurificationsCompleted} " +
            $"startedTick={StartedOnTick}";
    }
}
