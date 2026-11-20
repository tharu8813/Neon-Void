using NeonVoid.Core;
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
        [Tooltip("Run id this session plays as. A real campaign takes it from permanent state.")]
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
            var identity = new RunIdentity(runId < 1L ? 1L : runId, chosen);

            if (!State.TryBegin(identity, onTick: 0L))
            {
                Debug.LogError($"{name}: mission {identity} refused to begin.", this);
                return;
            }

            // Logged because a deterministic generator whose seed nobody can read back is not
            // reproducible in practice, whatever it is in principle.
            Debug.Log(
                $"Mission begun: {identity} (mission stream {identity.SeedFor(RandomStream.Mission)}, " +
                $"combat {identity.SeedFor(RandomStream.Combat)}, reward {identity.SeedFor(RandomStream.Reward)}).",
                this);
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

        /// <summary>Records a purification (정화) in the running mission.</summary>
        public bool RecordPurification()
        {
            bool recorded = State.TryRecordPurification();
            if (recorded)
                Debug.Log($"Purification {State.PurificationsCompleted} recorded for {Identity}.", this);

            return recorded;
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
