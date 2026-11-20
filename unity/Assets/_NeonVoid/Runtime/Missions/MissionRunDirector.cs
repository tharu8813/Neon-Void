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

        private IMissionCommitGate _commits;
        private MissionFlow _flow;

        /// <summary>
        /// 허브 → 메인보드 → 구역 → 목표 → 보스 → 정화 → 철수. Built once, over whatever can
        /// commit in this scene.
        /// <para>
        /// The gate is fixed at construction on purpose: a flow that could be handed a different
        /// one halfway through would have committed some of its purifications to one place and
        /// the rest to another, and the extraction counts them all.
        /// </para>
        /// </summary>
        public MissionFlow Flow =>
            _flow ?? (_flow = new MissionFlow(State, _commits ?? PrototypeMissionCommits.Instance));

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

            // Resolved before the flow is first touched, so the flow is built over the real gate
            // when a save is connected and over the prototype one when nothing is.
            var saveDirector = FindFirstObjectByType<Progression.Save.VaccineSaveDirector>();
            var identity = new RunIdentity(ResolveRunId(saveDirector), chosen);
            _commits = ResolveCommitGate(saveDirector);

            if (!Flow.TryBeginMission(identity, onTick: 0L))
            {
                Debug.LogError($"{name}: mission {identity} refused to begin: {Flow.LastRefusal}", this);
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
        private long ResolveRunId(Progression.Save.VaccineSaveDirector saveDirector)
        {
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
        /// The gate the flow's 정화 and 철수 go through.
        /// <para>
        /// A connected save that <i>cannot</i> commit - a halted slot, a future schema, a
        /// vaccine awaiting its replacement - gets the prototype gate rather than a store that
        /// would refuse everything, and says so once. The difference matters: the prototype gate
        /// lets the area loop be played and refuses only the extraction, which is the one
        /// transition whose product is permanent.
        /// </para>
        /// </summary>
        private IMissionCommitGate ResolveCommitGate(Progression.Save.VaccineSaveDirector saveDirector)
        {
            if (saveDirector == null)
            {
                Debug.Log(
                    $"{name}: no save in the scene, so 정화 is played and nothing is written, and " +
                    "철수 is refused. 영구 보상은 저장 커밋 승인 후에만 확정된다.", this);
                return PrototypeMissionCommits.Instance;
            }

            saveDirector.EnsurePrepared();
            if (!saveDirector.CanCommit)
            {
                Debug.LogWarning(
                    $"{name}: the save cannot commit ({saveDirector.Result.Verdict}), so this mission " +
                    "runs on the prototype gate: nothing is written and 철수 is refused.", this);
                return PrototypeMissionCommits.Instance;
            }

            return new Progression.Save.SaveMissionCommitGate(saveDirector.Store);
        }

        /// <summary>
        /// Marks the mission failed. Called when the player dies; what the death costs stays
        /// with <see cref="Progression.IDeathCommitService"/>, which nothing implements.
        /// </summary>
        public bool FailMission()
        {
            if (!Flow.TryFail())
            {
                Debug.LogWarning($"{name}: mission {Identity} was not failed: {Flow.LastRefusal}", this);
                return false;
            }

            Debug.Log($"Mission {Identity} failed. 사망은 해당 의뢰 실패 (docs/00).", this);
            return true;
        }

        /// <summary>One line for the debug overlay and the completion report.</summary>
        public string StatusLine() =>
            $"{Identity} · {Flow} · 의뢰 Lv.{State.MissionLevel} ({State.MissionXp} XP) · {_commits}";
    }
}
