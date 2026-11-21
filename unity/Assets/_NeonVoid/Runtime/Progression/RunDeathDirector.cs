using System;
using NeonVoid.Combat;
using NeonVoid.Core;
using NeonVoid.Missions;
using UnityEngine;

namespace NeonVoid.Progression
{
    /// <summary>
    /// Watches the player's actor and turns its death into a command to the save service.
    /// <para>
    /// This is the scene-side half of <see cref="RunDeathLedger"/>: the rules live in the
    /// ledger, which is tested; this adds the subscription, the tick index and the logging.
    /// </para>
    /// <para>
    /// [확정] docs/02: the combat engine does not write permanent state. With no
    /// <see cref="IDeathCommitService"/> registered - and none exists yet - this reports the
    /// death, keeps the run in <see cref="RunDeathPhase.AwaitingCommit"/>, and says so once in
    /// the console. Nothing is charged, nothing is destroyed, no result is released.
    /// </para>
    /// </summary>
    public sealed class RunDeathDirector : MonoBehaviour
    {
        [Header("Who the run belongs to")]
        [Tooltip("The player's CombatActor. Its death is the run's death.")]
        [SerializeField] private CombatActor playerActor;

        [Header("Run identity")]
        [Tooltip("Where the run id comes from. Found automatically when left empty.")]
        [SerializeField] private MissionRunDirector missionDirector;

        [Tooltip("Fallback run id, used only when no MissionRunDirector is in the scene.")]
        [SerializeField, Min(1)] private long fallbackRunId = 1L;

        private SimulationRunner _runner;
        private IDeathCommitService _commitService;
        private int _deathsObserved;

        /// <summary>The run's death state. Read by UI; never written by it.</summary>
        public RunDeathLedger Ledger { get; } = new RunDeathLedger();

        /// <summary>
        /// Raised when a death was accepted into the ledger. Presentation only: a listener must
        /// not show losses on this, because nothing has been committed yet.
        /// </summary>
        public event Action<DeathReport> DeathRegistered;

        private void Awake()
        {
            if (playerActor == null)
                playerActor = GetComponentInParent<CombatActor>();
            if (missionDirector == null)
                missionDirector = FindFirstObjectByType<MissionRunDirector>();
        }

        /// <summary>
        /// The run this death belongs to. Taken from the mission, because a receipt keyed on a
        /// constant would be the same key for every run the player ever plays - and the whole
        /// point of the key is telling one death from another.
        /// </summary>
        private long CurrentRunId()
        {
            if (missionDirector != null && missionDirector.Identity.IsValid)
                return missionDirector.Identity.RunId;

            Debug.LogWarning(
                $"{name}: no mission run to key this death on; falling back to run {fallbackRunId}. " +
                "Two runs would mint the same receipt id.", this);
            return fallbackRunId;
        }

        private void OnEnable()
        {
            _runner = _runner != null ? _runner : FindFirstObjectByType<SimulationRunner>();

            if (playerActor == null)
            {
                Debug.LogError($"{name}: no player CombatActor assigned; a death would go unrecorded.", this);
                return;
            }
            playerActor.Died += OnActorDied;

            // Asked for rather than waited for. VaccineSaveDirector registers itself with
            // whatever death director it finds while it is preparing the slot, which works only
            // when this component already exists - and a player spawned from a prefab, or a
            // scene built in a different order, wakes after it. A death director with no commit
            // service reports every death as AwaitingCommit and charges no clone, and nothing
            // about that looks broken, so the lookup goes both ways.
            if (_commitService == null)
            {
                var saves = FindFirstObjectByType<Save.VaccineSaveDirector>();
                if (saves != null)
                    saves.ConnectDeathsTo(this);
            }
        }

        private void OnDisable()
        {
            if (playerActor != null)
                playerActor.Died -= OnActorDied;
        }

        /// <summary>
        /// Hands this director the service that owns permanent state. Called by whatever boots
        /// progression; there is nothing to call it yet.
        /// </summary>
        /// <summary>
        /// Whether permanent state is listening. False means a death will be registered and
        /// left in <see cref="RunDeathPhase.AwaitingCommit"/>: no clone is charged, and the
        /// death screen must not claim one was.
        /// </summary>
        public bool HasCommitService => _commitService != null;

        public void RegisterCommitService(IDeathCommitService service)
        {
            if (service == null)
                return;

            if (_commitService != null)
                _commitService.CommitCompleted -= OnCommitCompleted;

            _commitService = service;
            _commitService.CommitCompleted += OnCommitCompleted;

            // A service that arrives after the death still has to hear about it, or a death
            // that happened during loading would wait for an answer nobody is sending.
            if (Ledger.Phase == RunDeathPhase.AwaitingCommit && Ledger.HasRegisteredDeath)
                _commitService.RequestDeathCommit(Ledger.RegisteredDeath);
        }

        private void OnActorDied(CombatActor actor)
        {
            // The sequence restarts at 1 in a fresh process on purpose: after a crash before
            // the ack, re-reporting the run's first death must produce the id it had the first
            // time, or the save service cannot tell it is the same death. See DeathId.
            _deathsObserved++;

            var report = new DeathReport(
                DeathId.For(CurrentRunId(), actor.ActorId, _deathsObserved),
                _runner != null ? _runner.TickIndex : 0L);

            DeathRegistrationVerdict verdict = Ledger.TryRegisterDeath(report);
            switch (verdict)
            {
                case DeathRegistrationVerdict.Accepted:
                    Debug.Log($"Death registered {report.Id} at tick {report.TickIndex}. " +
                              "Awaiting a save commit; no permanent state has changed.", this);

                    // [확정] docs/00: 사망은 해당 의뢰 실패. The mission is marked failed here,
                    // in memory; what that costs the player is still the save service's call.
                    if (missionDirector != null)
                        missionDirector.FailMission();

                    DeathRegistered?.Invoke(report);
                    RequestCommit(report);
                    break;

                case DeathRegistrationVerdict.DuplicateIgnored:
                    Debug.LogWarning($"{name}: death {report.Id} reported twice; ignored. " +
                                     "Nothing is charged twice.", this);
                    break;

                default:
                    Debug.LogError($"{name}: death {report.Id} refused ({verdict}). The run had " +
                                   $"already ended with {Ledger.RegisteredDeathId}; refusing " +
                                   "rather than charging a second death.", this);
                    break;
            }
        }

        private void RequestCommit(in DeathReport report)
        {
            if (_commitService == null)
            {
                Debug.LogWarning(
                    $"{name}: no IDeathCommitService is registered, so {report.Id} stays in " +
                    "AwaitingCommit. This is the expected state right now: clone decrement, the " +
                    "20~70% XP settlement (D-012) and plugin loss are permanent writes that " +
                    "must not be mocked. The death screen will not show losses.", this);
                return;
            }
            _commitService.RequestDeathCommit(report);
        }

        private void OnCommitCompleted(DeathCommitAck ack)
        {
            DeathAckVerdict verdict = Ledger.Acknowledge(ack);
            if (verdict == DeathAckVerdict.Applied)
            {
                if (ack.Succeeded)
                    Debug.Log($"Death {ack.Id} committed. Results may now be shown.", this);
                else
                    Debug.LogError($"Death {ack.Id} failed to commit ({ack.FailureReason}). " +
                                   "No result released; the same death may be retried.", this);
                return;
            }

            Debug.LogError($"{name}: commit answer for {ack.Id} refused ({verdict}). " +
                           "An answer about another death cannot release this one.", this);
        }

        /// <summary>
        /// Opens the ledger for the next mission of this session, and says whether that mission
        /// may begin at all. Called by <see cref="MissionRunDirector"/> before it asks permanent
        /// state for a run id.
        /// <para>
        /// The death sequence restarts with the run, so a run's first death is always its
        /// <c>death1</c> - the id is a property of the run, which is what the save service
        /// dedupes on, and not of however many missions this session happens to have played.
        /// </para>
        /// </summary>
        public RunResetVerdict TryStartNewRun()
        {
            DeathId settled = Ledger.RegisteredDeathId;
            RunResetVerdict verdict = Ledger.TryStartNewRun();
            if (verdict == RunResetVerdict.RefusedUnsettled)
            {
                Debug.LogError(
                    $"{name}: {Ledger.RegisteredDeathId} is still {Ledger.Phase}, so no new mission " +
                    "begins. 보상/사망/철수는 저장 커밋 승인 후 확정 (CLAUDE.md): starting one now " +
                    "would confirm a new run over a death nobody has paid for. " +
                    (Ledger.Phase == RunDeathPhase.CommitFailed
                        ? $"Retry the commit: {Ledger.LastFailureReason}"
                        : "Waiting for the save service."),
                    this);
                return verdict;
            }

            _deathsObserved = 0;

            if (verdict == RunResetVerdict.Reset)
                Debug.Log($"Death ledger cleared: {settled} was committed. The next mission may begin.", this);

            return verdict;
        }

        /// <summary>Re-sends a failed commit, keeping the original death id.</summary>
        public bool RetryCommit()
        {
            if (!Ledger.TryBeginRetry(out DeathReport report))
                return false;

            RequestCommit(report);
            return true;
        }
    }
}
