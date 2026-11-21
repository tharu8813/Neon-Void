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

        [Tooltip(
            "Begin the first mission the moment this wakes. A scene with a 디펜더 허브 turns " +
            "this off: there the mission begins at the 임무 터미널, and beginning one here " +
            "would hand the player a run they never started. An arena with no hub needs a run " +
            "immediately, because every actor derives its combat generator from one.")]
        [SerializeField] private bool beginMissionOnAwake = true;

        [Tooltip(
            "The session's death ledger. A mission does not begin while the previous one's " +
            "death is unsettled. Found in the scene when left empty.")]
        [SerializeField] private RunDeathDirector deaths;

        /// <summary>The live mission. Read by the death director and the plugin director.</summary>
        public MissionRunState State { get; } = new MissionRunState();

        private IMissionCommitGate _commits;
        private MissionFlow _flow;
        private Progression.Save.VaccineSaveDirector _saveDirector;
        private uint _sessionSeed;
        private int _missionsBegun;

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
        /// The session's death ledger, looked up when it is first needed rather than cached in
        /// <see cref="Awake"/>: this director runs at -200 and a scene is free to create the
        /// death director afterwards, so an Awake lookup would cache a nothing and never check
        /// again.
        /// </summary>
        private RunDeathDirector Deaths =>
            deaths != null ? deaths : (deaths = FindFirstObjectByType<RunDeathDirector>());

        /// <summary>Names the death ledger. The injection seam for a test.</summary>
        public void ConnectDeaths(RunDeathDirector director) => deaths = director;

        /// <summary>
        /// Runs before anything else through <see cref="DefaultExecutionOrder"/>, so that any
        /// Awake asking for the run id in the same frame already has one. A system that read
        /// it first and cached a zero would silently key its receipts on nothing.
        /// </summary>
        private void Awake()
        {
            _sessionSeed = seed != 0u ? seed : PickSeed();

            // Resolved before the flow is first touched, so the flow is built over the real gate
            // when a save is connected and over the prototype one when nothing is.
            _saveDirector = FindFirstObjectByType<Progression.Save.VaccineSaveDirector>();
            _commits = ResolveCommitGate(_saveDirector);

            if (!beginMissionOnAwake)
            {
                // The flow starts in the hub, which is where it belongs: docs/00's loop is
                // 허브 → 의뢰 시작, and a mission begun here would be one the player never asked
                // for. The combat seed is left alone too - it belongs to a mission, and there is
                // no mission yet.
                Debug.Log(
                    $"{name}: waiting in the hub. The mission begins at the 임무 터미널 " +
                    $"(session seed {_sessionSeed}).", this);
                return;
            }

            TryBeginNextMission();
        }

        /// <summary>
        /// Begins the next mission of this session: a run id of its own, a seed of its own, and
        /// the flow stepped out of the hub.
        /// <para>
        /// <b>Both are per mission, not per session.</b> [확정] docs/04 section 5 forbids
        /// 같은 runId 중복 시작 and keys the death receipt on the run, so a second mission on the
        /// first one's id would have its first death refused as one already settled - and no
        /// clone would ever be charged again. [확정] docs/00 makes the next mission's map and
        /// enemies new, which a reused seed would rebuild identically.
        /// <see cref="MissionSeries"/> derives both from the ordinal, so a retry asks under the
        /// same key and is answered with the id already allocated.
        /// </para>
        /// </summary>
        public bool TryBeginNextMission()
        {
            // Before anything, and before a run id is spent: the previous mission's death has to
            // be settled. [확정] CLAUDE.md asks that 사망 be confirmed by a save commit before
            // game state is, and a ledger still holding an unsettled death would refuse the next
            // mission's death outright - charging nothing for it.
            RunDeathDirector ledger = Deaths;
            if (ledger != null && ledger.TryStartNewRun() == RunResetVerdict.RefusedUnsettled)
                return false;

            int ordinal = _missionsBegun + 1;
            uint missionSeed = MissionSeries.SeedFor(_sessionSeed, ordinal);
            long id = RunIdForMission(ordinal);
            if (id <= 0L)
            {
                // Nothing begins on an id permanent state did not give out. Playing under a
                // guess is how a death gets charged to another run.
                Debug.LogError(
                    $"{name}: mission {ordinal} has no run id, so it does not begin. " +
                    "영구 상태가 런 ID를 주지 않으면 의뢰를 시작하지 않는다.", this);
                return false;
            }

            var identity = new RunIdentity(id, missionSeed);
            if (!Flow.TryBeginMission(identity, onTick: 0L))
            {
                Debug.LogError($"{name}: mission {identity} refused to begin: {Flow.LastRefusal}", this);
                return false;
            }

            _missionsBegun = ordinal;

            // Every actor derives its combat generator from this. On the first mission of an
            // arena scene it is set before any actor's Awake thanks to DefaultExecutionOrder;
            // on a later one it is set here, before anything that mission spawns exists.
            CombatActor.SetRunCombatSeed(identity.SeedFor(RandomStream.Combat));

            // Logged because a deterministic generator whose seed nobody can read back is not
            // reproducible in practice, whatever it is in principle.
            Debug.Log(
                $"Mission {ordinal} begun: {identity} (mission stream {identity.SeedFor(RandomStream.Mission)}, " +
                $"combat {identity.SeedFor(RandomStream.Combat)}, reward {identity.SeedFor(RandomStream.Reward)}).",
                this);
            return true;
        }

        /// <summary>How many missions this session has begun. 0 before the first.</summary>
        public int MissionsBegun => _missionsBegun;

        /// <summary>
        /// Fills in what a scene would have serialized. Call before this object wakes - add the
        /// component to a deactivated GameObject, configure, then activate.
        /// </summary>
        public void ConfigureStart(bool beginOnAwake, uint sessionSeed = 0u)
        {
            beginMissionOnAwake = beginOnAwake;
            seed = sessionSeed;
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
        private long RunIdForMission(int ordinal)
        {
            if (_saveDirector == null)
                return FallbackRunId(ordinal);

            // Asked for rather than waited for, so this does not depend on which Awake ran
            // first.
            _saveDirector.EnsurePrepared();

            if (!_saveDirector.CanCommit)
            {
                // Permanent writes are off, or the slot is halted. Asking for an id would mean
                // committing a command through a store that is not there. This is a supported
                // way to play - the scene says so on the component - so it is a fallback id and
                // a line in the log, not a refusal.
                Debug.Log(
                    $"{name}: the save is not committing ({_saveDirector.Result.Verdict}), so mission " +
                    $"{ordinal} plays under a fallback id and nothing will be written.", this);
                return FallbackRunId(ordinal);
            }

            // The slot bootstrap already took an id while it was preparing the slot. The first
            // mission plays under that one rather than leaving it unused and taking another:
            // run ids are a monotonic counter in permanent state, and a session that burned one
            // per launch would make a save file claim more runs than were ever played.
            if (ordinal == MissionSeries.FirstOrdinal && _saveDirector.RunId > 0L)
                return _saveDirector.RunId;

            long allocated = _saveDirector.AllocateRunId(
                MissionSeries.NonceFor(_saveDirector.SessionNonce, ordinal));
            if (allocated > 0L)
                return allocated;

            Debug.LogWarning(
                $"{name}: a save is in the scene but allocated no run id for mission {ordinal} " +
                $"({_saveDirector.Result.Verdict}), so it uses a fallback id and nothing will be " +
                "committed.", this);
            return FallbackRunId(ordinal);
        }

        /// <summary>
        /// The id for a mission in a scene with no connected save. Nothing is committed under
        /// it, so the only thing it has to do is differ from the other missions of the session -
        /// a repeated id would make the in-memory flow look like a 중복 시작.
        /// </summary>
        private long FallbackRunId(int ordinal)
        {
            long first = runId < 1L ? 1L : runId;
            return first + (ordinal - MissionSeries.FirstOrdinal);
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
