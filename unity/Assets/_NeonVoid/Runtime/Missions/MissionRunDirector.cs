using NeonVoid.Combat;
using NeonVoid.Core;
using NeonVoid.Progression;
using UnityEngine;

namespace NeonVoid.Missions
{
    /// <summary>
    /// Starts the mission and owns its live state, so everything else can ask which run this is
    /// instead of carrying its own placeholder.
    /// <para>
    /// Before this, the death receipt was keyed on a hardcoded run 1 and the plugin roll used
    /// one constant seed - so every run produced the same "random" stats and two runs would
    /// have minted the same receipt id. Both now come from here.
    /// </para>
    /// <para>
    /// <b>Nothing is saved.</b> docs/04 puts 의뢰 상태 behind an approved save commit and there
    /// is no save service. This holds the run for as long as the process lives, which is what
    /// the combat layer needs and the most that can be done honestly.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class MissionRunDirector : MonoBehaviour
    {
        [Header("Prototype run")]
        [Tooltip(
            "Fallback run id, used only when no save is connected. A connected save allocates " +
            "the id from permanent state instead, because two runs sharing an id mint the same " +
            "death receipt.")]
        [SerializeField, Min(1)] private long runId = 1L;

        [Tooltip("Seed. 0 picks one at startup and logs it, so a run can be reproduced.")]
        [SerializeField] private uint seed;

        /// <summary>The live mission. Read by the death director and the plugin director.</summary>
        public MissionRunState State { get; } = new MissionRunState();

        /// <summary>This mission's run identity, or the absent one before it begins.</summary>
        public RunIdentity Identity => State.Identity;

        /// <summary>
        /// Runs before anything else through <see cref="DefaultExecutionOrder"/>, so that any
        /// Awake asking for the run id in the same frame already has one. A system that read
        /// it first and cached a zero would silently key its receipts on nothing.
        /// </summary>
        private void Awake()
        {
            uint chosen = seed != 0u ? seed : PickSeed();
            var identity = new RunIdentity(ResolveRunId(), chosen);

            if (!State.TryBegin(identity, onTick: 0L))
            {
                Debug.LogError($"{name}: mission {identity} refused to begin.", this);
                return;
            }

            // Every actor derives its combat generator from this. Set before any actor's Awake
            // thanks to DefaultExecutionOrder, so nobody seeds itself from the default and then
            // rolls a sequence belonging to no run.
            CombatActor.SetRunCombatSeed(identity.SeedFor(RandomStream.Combat));

            // Logged because a deterministic generator whose seed nobody can read back is not
            // reproducible in practice, whatever it is in principle.
            Debug.Log(
                $"Mission begun: {identity} (mission stream {identity.SeedFor(RandomStream.Mission)}, " +
                $"combat {identity.SeedFor(RandomStream.Combat)}, reward {identity.SeedFor(RandomStream.Reward)}).",
                this);
        }

        /// <summary>
        /// The run id, from permanent state when a save is connected.
        /// <para>
        /// A serialized constant was the original, and it was wrong in a way nothing showed: a
        /// death receipt is keyed on its run, so every launch's first death minted the same id
        /// and the save - correctly - refused the second launch's as a death it had already
        /// settled. No clone would ever be charged again. docs/04 section 5 names runId a
        /// 중복 방지용 불변 키 and forbids 같은 runId 중복 시작, which only permanent state can
        /// enforce.
        /// </para>
        /// <para>
        /// The fallback is kept for a scene with no save connected, where nothing is committed
        /// and the id only has to exist. It says so rather than looking like a real run id.
        /// </para>
        /// </summary>
        private long ResolveRunId()
        {
            var saveDirector = FindFirstObjectByType<Progression.Save.VaccineSaveDirector>();
            if (saveDirector == null)
                return runId < 1L ? 1L : runId;

            // Asked for rather than waited for, so this does not depend on which Awake ran
            // first.
            saveDirector.EnsurePrepared();
            if (saveDirector.RunId > 0L)
                return saveDirector.RunId;

            Debug.LogWarning(
                $"{name}: a save is in the scene but allocated no run id ({saveDirector.Result.Verdict}), " +
                $"so this run uses the fallback id {runId} and nothing will be committed.", this);
            return runId < 1L ? 1L : runId;
        }

        /// <summary>
        /// A seed when none was given. Taken from the clock ONCE, here, and never read again:
        /// everything downstream derives from the recorded value, so the run stays reproducible
        /// from the logged number even though it started unpredictably.
        /// </summary>
        private static uint PickSeed()
        {
            unchecked
            {
                uint fromClock = (uint)System.DateTime.UtcNow.Ticks;
                return fromClock == 0u ? 0x9E3779B9u : fromClock;
            }
        }

        /// <summary>A new generator on one of the run's streams.</summary>
        public DeterministicRng CreateRng(RandomStream stream) =>
            new DeterministicRng(State.SeedFor(stream));

        /// <summary>
        /// Records a purification (정화) in the running mission and awards the area's mission XP.
        /// <para>
        /// The XP figure is applied <b>here</b> rather than inside
        /// <see cref="MissionRunState.TryRecordPurification"/>, so that reading 기본 720 임무
        /// XP/구역 (docs/04 section 1, [초안]) as "one purified area is one 구역" is a decision
        /// visible at this seam instead of a constant buried in the state object. Nothing in the
        /// documents numbers permanent XP per area, so none is awarded; the permanent side waits
        /// for 철수, and its settlement on death waits for D-012.
        /// </para>
        /// </summary>
        public bool RecordPurification()
        {
            bool recorded = State.TryRecordPurification();
            if (!recorded)
                return false;

            State.TryAwardMissionXp(ProgressionCurves.DraftMissionXpPerArea, out int levelsGained);
            Debug.Log(
                $"Purification {State.PurificationsCompleted} recorded for {Identity}: " +
                $"+{ProgressionCurves.DraftMissionXpPerArea} 임무 XP, now Lv.{State.MissionLevel} " +
                $"(+{levelsGained}).",
                this);

            return true;
        }

        /// <summary>
        /// Marks the mission failed. Called when the player dies; the consequences stay with
        /// the save service, which has not been written.
        /// </summary>
        public bool FailMission()
        {
            bool failed = State.TryFail();
            if (failed)
                Debug.Log($"Mission {Identity} failed. 사망은 해당 의뢰 실패 (docs/00).", this);

            return failed;
        }
    }
}
