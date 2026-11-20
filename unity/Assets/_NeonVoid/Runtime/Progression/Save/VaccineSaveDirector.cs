using System;
using UnityEngine;

namespace NeonVoid.Progression.Save
{
    /// <summary>
    /// The one thing in the scene that constructs a save store over the player's real files,
    /// prepares the slot, and hands the death director a service that can commit.
    /// <para>
    /// <b>This writes to the player's save directory.</b> Everything it does goes through the
    /// transaction in <see cref="SaveStore"/> - checksummed, backed up, verified by re-reading,
    /// idempotent per receipt - but a death committed here really does spend a clone, and
    /// [확정] docs/04 section 2: 복제 회복은 어떤 수단으로도 불가. The fourth death destroys the
    /// vaccine's growth, skills and database.
    /// </para>
    /// <para>
    /// <b>Nothing is shown before it is written.</b> The commit service raises its
    /// acknowledgement from the store's answer, so a failed write leaves the run in
    /// <see cref="RunDeathPhase.CommitFailed"/> and releases no result - docs/04 section 7's
    /// forbidden outcome is the UI saying it first.
    /// </para>
    /// <para>
    /// <b>If the slot cannot take a commit, no service is registered at all.</b> A halted store
    /// or a failed run id allocation means deaths stay in
    /// <see cref="RunDeathPhase.AwaitingCommit"/> and say so, which is the honest unfinished
    /// state the death director was built around. Registering a service that cannot write would
    /// turn every death into a failure dialogue instead.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-300)]
    public sealed class VaccineSaveDirector : MonoBehaviour
    {
        [Header("Permanent state")]
        [Tooltip(
            "Writes to the real save directory under Application.persistentDataPath. " +
            "Turn this off to play without permanent consequences: deaths are then reported " +
            "and left uncommitted, and no clone is spent.")]
        [SerializeField] private bool writeToRealSaveDirectory = true;

        [Tooltip("Save slot. One vaccine lineage per slot; generations within it.")]
        [SerializeField] private string slotId = "slot-1";

        [Header("After 최종 파기")]
        [Tooltip(
            "docs/04 ends the fourth death at a vaccine selection screen, which does not exist " +
            "yet. With this on, a successor vaccine is created automatically so the slot stays " +
            "playable; with it off, the slot waits and says so.")]
        [SerializeField] private bool autoStartNextVaccine = true;

        [Header("Who to tell")]
        [Tooltip("Found automatically when left empty.")]
        [SerializeField] private RunDeathDirector deathDirector;

        private bool _prepared;

        /// <summary>The store, or null when permanent writes are off or the root was refused.</summary>
        public SaveStore Store { get; private set; }

        /// <summary>What preparing the slot produced. Read by the overlay.</summary>
        public SlotBootstrapResult Result { get; private set; }

        /// <summary>
        /// The run id this session must play under, or 0 when none was allocated. A run that
        /// used anything else would mint a death receipt another run had already used.
        /// </summary>
        public long RunId => Result.RunId;

        /// <summary>True when a death reaching the store will actually be written.</summary>
        public bool CanCommit => Store != null && Result.CanCommit;

        private void Awake() => EnsurePrepared();

        /// <summary>
        /// Prepares against a store built elsewhere, instead of one over the player's files.
        /// <para>
        /// For tests and tools. It is the only way this component's wiring - finding the death
        /// director, deciding whether the slot can take a commit, registering a service that
        /// can - can be executed at all without writing into the player's save directory, which
        /// docs/04 section 4 forbids a test from touching. Production never calls it: the
        /// serialized fields have no path override, so a scene cannot be configured to save
        /// somewhere unexpected.
        /// </para>
        /// <para>
        /// Call it before the component's Awake runs - add it to a deactivated GameObject, then
        /// activate - or <see cref="EnsurePrepared"/> will already have resolved the real root.
        /// </para>
        /// </summary>
        public SlotBootstrapResult UseStore(SaveStore store, string sessionNonce = null)
        {
            if (store == null)
                throw new ArgumentNullException(nameof(store));
            if (_prepared)
                throw new InvalidOperationException($"{name} has already been prepared.");

            _prepared = true;
            Store = store;
            Result = VaccineSlotBootstrap.Prepare(
                Store,
                sessionNonce: string.IsNullOrWhiteSpace(sessionNonce)
                    ? Guid.NewGuid().ToString("N")
                    : sessionNonce,
                newVaccineId: () => "vaccine-" + Guid.NewGuid().ToString("N"),
                mayStartNextVaccine: autoStartNextVaccine);

            Report();
            if (CanCommit)
                Register();

            return Result;
        }

        /// <summary>
        /// Prepares the slot, once. Called from this component's Awake and from anything that
        /// needs the run id before then, so no execution order has to be correct for the run id
        /// to exist.
        /// </summary>
        public void EnsurePrepared()
        {
            if (_prepared)
                return;

            _prepared = true;

            if (!writeToRealSaveDirectory)
            {
                Result = VaccineSlotBootstrap.NotConnected("permanent writes are switched off");
                Debug.LogWarning(
                    $"{name}: permanent writes are off. Deaths will be reported and left " +
                    "uncommitted, and no clone will be spent.", this);
                return;
            }

            try
            {
                Store = new SaveStore(
                    new DiskSaveFileSystem(), SaveSlotLayout.Under(UnitySaveRoot.Resolve(), slotId));
            }
            catch (Exception e)
            {
                // A refused root is the legacy guard doing its job, or a platform with no data
                // path. Either way there is nowhere safe to write, and carrying on with a
                // different directory would be the silent behaviour that guard exists to stop.
                Result = VaccineSlotBootstrap.NotConnected(e.Message);
                Debug.LogError($"{name}: no save directory, so nothing will be committed. {e.Message}", this);
                return;
            }

            Result = VaccineSlotBootstrap.Prepare(
                Store,
                sessionNonce: Guid.NewGuid().ToString("N"),
                newVaccineId: () => "vaccine-" + Guid.NewGuid().ToString("N"),
                mayStartNextVaccine: autoStartNextVaccine);

            Report();
            if (CanCommit)
                Register();
        }

        private void Register()
        {
            if (deathDirector == null)
                deathDirector = FindFirstObjectByType<RunDeathDirector>();

            if (deathDirector == null)
            {
                Debug.LogWarning(
                    $"{name}: no RunDeathDirector in the scene, so a death would never reach the save.", this);
                return;
            }

            deathDirector.RegisterCommitService(new SaveDeathCommitService(Store));
            Debug.Log(
                $"{name}: deaths will be committed to {Store.Layout.SnapshotPath}. " +
                $"{Result.CopiesRemaining} clone(s) left on vaccine {Result.VaccineId} " +
                $"generation {Result.Generation}.", this);
        }

        private void Report()
        {
            switch (Result.Verdict)
            {
                case SlotBootstrapVerdict.Halted:
                    // docs/04: 데이터 불일치 시 사용자 데이터를 덮어쓰지 않고 읽기 전용
                    // 복구/오류 상태로 정지. Loud, because the alternative to being told is a
                    // session that quietly saves nothing.
                    Debug.LogError(
                        $"{name}: the save is halted and nothing will be written. {Result.Detail}", this);
                    break;

                case SlotBootstrapVerdict.WriteFailed:
                    Debug.LogError($"{name}: the save could not be prepared. {Result.Detail}", this);
                    break;

                case SlotBootstrapVerdict.AwaitingNextVaccine:
                    Debug.LogWarning(
                        $"{name}: vaccine {Result.VaccineId} was destroyed and no successor was started. " +
                        $"{Result.Detail}", this);
                    break;

                case SlotBootstrapVerdict.CreatedSlot:
                    Debug.Log($"{name}: a new save was created. {Result}", this);
                    break;

                case SlotBootstrapVerdict.StartedNextVaccine:
                    Debug.Log(
                        $"{name}: the previous vaccine was destroyed; generation {Result.Generation} " +
                        $"begins with {Result.CopiesRemaining} clones. {Result}", this);
                    break;

                default:
                    Debug.Log($"{name}: save ready. {Result}", this);
                    break;
            }
        }

        /// <summary>One line for the debug overlay, in the terms the save actually uses.</summary>
        public string StatusLine()
        {
            if (!writeToRealSaveDirectory)
                return "save: off (no clone will be spent)";
            if (Store == null)
                return "save: unavailable";
            if (Store.State == SaveStoreState.Halted)
                return $"save: HALTED - {Store.HaltReason}";
            if (!CanCommit)
                return $"save: {Result.Verdict} - {Result.Detail}";

            VaccineSaveState vaccine = Store.Snapshot.vaccine;
            return $"save: run {RunId} - gen {vaccine.generation} - clones {vaccine.copiesRemaining}" +
                $" - deaths {vaccine.deathCount} - rev {Store.Snapshot.snapshotRevision}";
        }
    }
}
