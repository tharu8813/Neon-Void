using System;
using System.Collections.Generic;

namespace NeonVoid.Progression.Save
{
    /// <summary>
    /// The permanent state of one vaccine, as docs/04 section 1 divides it: everything that
    /// lives from 백신 생성 to 최종 파기.
    /// <para>
    /// <b>Every field is an integer, a bool or a string. There is no floating point anywhere in
    /// the save document, deliberately.</b> The snapshot checksum is computed over the
    /// serialized text and verified by re-serializing what was read back, so a value whose
    /// text form is not an exact round trip would make a healthy save look corrupt on some
    /// machines and not others. XP, points, levels and clone counts are all counting numbers,
    /// so nothing is lost by saying so - and a reflection test over this document holds the
    /// rule rather than trusting a later reader to remember it.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class VaccineSaveState
    {
        /// <summary>Slot-local identity. Not a display name.</summary>
        public string id = string.Empty;

        /// <summary>
        /// Bumped by 최종 파기, so a receipt written for the destroyed vaccine can never be
        /// mistaken for one belonging to the vaccine that replaced it. docs/04 section 5 names
        /// vaccineGeneration among the keys that stop a command applying twice.
        /// </summary>
        public int generation = 1;

        /// <summary>False only after 최종 파기. A dead vaccine is kept, not deleted.</summary>
        public bool alive = true;

        /// <summary>
        /// [확정] docs/04 section 2 starts at 3. '복제 0' is the last chance, not a dead
        /// vaccine: the fourth death is what destroys it.
        /// </summary>
        public int copiesRemaining = DesignStartingCopies;

        /// <summary>Cumulative deaths. The table in docs/04 section 2 is read off this.</summary>
        public int deathCount;

        public int permanentLevel = 1;

        /// <summary>영구 XP. Approved XP is never taken back (docs/04 section 2).</summary>
        public long permanentXp;

        public int unusedStatPoints;

        /// <summary>
        /// Instance ids carried into a mission. [확정] docs/04 section 3.5: lost on death.
        /// </summary>
        public List<string> carriedPluginInstanceIds = new List<string>();

        /// <summary>
        /// Instance ids in the archive DB. [확정] docs/04 section 3.4: moving one out of the
        /// archive removes it from the archive - the same instance moves, it is not copied - so
        /// an id must never appear in both lists. <see cref="Validate"/> refuses that.
        /// </summary>
        public List<string> archivePluginInstanceIds = new List<string>();

        /// <summary>[확정] docs/04 section 2: three clones to begin with.</summary>
        public const int DesignStartingCopies = 3;

        /// <summary>
        /// The death after which the vaccine is destroyed. [확정] docs/04 section 2: the
        /// fourth. Expressed as a count rather than inferred from copiesRemaining reaching a
        /// negative number, because 복제 0 is a playable state.
        /// </summary>
        public const int DesignDeathsBeforeFinalDestruction = 4;

        public static VaccineSaveState NewVaccine(string id) => new VaccineSaveState { id = id };

        /// <summary>
        /// A deep copy, so a command computes its next state without touching the one that is
        /// still the committed truth. docs/04 section 5 step 2 asks for an immutable snapshot
        /// of next; a shallow copy would let a rejected command's edits to a list survive.
        /// </summary>
        public VaccineSaveState Clone() => new VaccineSaveState
        {
            id = id,
            generation = generation,
            alive = alive,
            copiesRemaining = copiesRemaining,
            deathCount = deathCount,
            permanentLevel = permanentLevel,
            permanentXp = permanentXp,
            unusedStatPoints = unusedStatPoints,
            carriedPluginInstanceIds = new List<string>(carriedPluginInstanceIds),
            archivePluginInstanceIds = new List<string>(archivePluginInstanceIds),
        };

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("A vaccine needs an id to key its receipts on.");
            if (generation < 1)
                throw new InvalidOperationException("Generations start at 1.");
            if (copiesRemaining < 0)
                throw new InvalidOperationException(
                    "Clones cannot go below 0; the fourth death destroys the vaccine instead.");
            if (copiesRemaining > DesignStartingCopies)
                throw new InvalidOperationException(
                    "복제 회복은 어떤 수단으로도 불가 (docs/04 section 2), so more than the starting count is a bug.");
            if (deathCount < 0)
                throw new InvalidOperationException("Deaths cannot be negative.");
            if (permanentLevel < 1)
                throw new InvalidOperationException("Permanent level starts at 1.");
            if (permanentXp < 0L)
                throw new InvalidOperationException("Approved permanent XP is never negative.");
            if (unusedStatPoints < 0)
                throw new InvalidOperationException("Unspent points cannot be negative.");

            // The clone counter and the death counter are two views of one [확정] table
            // (docs/04 section 2), so they can only ever disagree because something wrote a
            // state that table does not contain - a hand-edited save, or a bug. Refusing here
            // means the disagreement is found on load, where it can halt safely, rather than
            // on the next death, where resolving it either grants a clone or takes one.
            if (alive)
            {
                if (copiesRemaining + deathCount != DesignStartingCopies)
                {
                    throw new InvalidOperationException(
                        $"{deathCount} deaths and {copiesRemaining} clones do not add up to " +
                        $"{DesignStartingCopies}; docs/04 section 2 gives one clone per death.");
                }

                if (deathCount >= DesignDeathsBeforeFinalDestruction)
                {
                    throw new InvalidOperationException(
                        $"a vaccine is destroyed on death {DesignDeathsBeforeFinalDestruction}, so it cannot " +
                        $"be alive after {deathCount}.");
                }
            }
            else if (copiesRemaining != 0 || deathCount < DesignDeathsBeforeFinalDestruction)
            {
                throw new InvalidOperationException(
                    $"a destroyed vaccine has no clones and has died {DesignDeathsBeforeFinalDestruction} times; " +
                    $"this one has {copiesRemaining} and {deathCount}.");
            }

            RequireNoBlanks(carriedPluginInstanceIds, nameof(carriedPluginInstanceIds));
            RequireNoBlanks(archivePluginInstanceIds, nameof(archivePluginInstanceIds));

            // [확정] docs/04 section 7: "DB 출전 이동 → DB에는 인스턴스 없어짐 / 금지: DB+소지
            // 중복 잔존". One instance in both lists is that forbidden result, and it is the
            // kind of bug that duplicates a plugin rather than losing one, so it must never be
            // written at all.
            for (int i = 0; i < carriedPluginInstanceIds.Count; ++i)
            {
                if (archivePluginInstanceIds.Contains(carriedPluginInstanceIds[i]))
                    throw new InvalidOperationException(
                        $"Plugin instance {carriedPluginInstanceIds[i]} is in both the archive and the loadout.");
            }

            RequireDistinct(carriedPluginInstanceIds, nameof(carriedPluginInstanceIds));
            RequireDistinct(archivePluginInstanceIds, nameof(archivePluginInstanceIds));
        }

        private static void RequireNoBlanks(List<string> ids, string name)
        {
            if (ids == null)
                throw new InvalidOperationException($"{name} must be a list, not null.");

            for (int i = 0; i < ids.Count; ++i)
            {
                if (string.IsNullOrWhiteSpace(ids[i]))
                    throw new InvalidOperationException($"{name} holds a blank instance id at {i}.");
            }
        }

        private static void RequireDistinct(List<string> ids, string name)
        {
            for (int i = 0; i < ids.Count; ++i)
            {
                for (int j = i + 1; j < ids.Count; ++j)
                {
                    if (ids[i] == ids[j])
                        throw new InvalidOperationException($"{name} holds {ids[i]} twice.");
                }
            }
        }
    }

    /// <summary>
    /// One committed command's receipt. docs/04 section 5 requires that re-sending a finished
    /// command <i>returns the same result</i>, and section 7 forbids re-issuing a rare card or
    /// points after a lost ack.
    /// <para>
    /// <b>Why the result is stored and not just the id.</b> The schema outline in docs/04
    /// section 6 keeps a <c>processedReceiptIds</c> array, which is enough to refuse a second
    /// application but not enough to answer the retry. A caller whose ack was lost asks again
    /// and must be told what happened the first time - how many clones are left, whether the
    /// vaccine was destroyed - and a bare list of ids can only say "already done", which sends
    /// the UI looking for the answer somewhere else. So the receipt carries the command's own
    /// serialized result, verbatim, and a replay returns that text rather than recomputing it.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class SaveReceiptRecord
    {
        /// <summary>The idempotency key. Unique within the document.</summary>
        public string receiptId = string.Empty;

        /// <summary>Which command wrote this, for logs and for the recovery screen.</summary>
        public string command = string.Empty;

        /// <summary>Snapshot revision this command produced.</summary>
        public int revisionApplied;

        /// <summary>
        /// The command's own result, serialized by the command. The store never interprets it;
        /// it stores it and hands it back on a replay.
        /// </summary>
        public string result = string.Empty;

        public SaveReceiptRecord Clone() => new SaveReceiptRecord
        {
            receiptId = receiptId,
            command = command,
            revisionApplied = revisionApplied,
            result = result,
        };
    }

    /// <summary>
    /// One save slot's whole committed state: the snapshot and the receipt log in one document.
    /// <para>
    /// docs/04 section 5 is explicit that two JSON files written one after the other must not be
    /// called atomic. The snapshot and the receipts are the pair that has to move together - a
    /// snapshot that advanced without its receipt would re-apply a command, and a receipt
    /// without its snapshot would refuse one that never happened - so they are one file, written
    /// once, with one checksum over both.
    /// </para>
    /// <para>
    /// <b>No run state here, on purpose.</b> docs/04 section 6 sketches a <c>run</c> object
    /// holding pendingRewards, rerollsLeft, pityCounter and rngStreams. Every one of those is
    /// undecided - the reward table and the pity rules are [미확정] in docs/03 section 6, and
    /// C10 in docs/08 forbids auto-resuming or abandoning an interrupted mission - so a field
    /// for them would be a permanent file format built around rules nobody has approved. The
    /// roadmap calls this phase 영구 저장 기반(보상 제외) and that is what this is.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class SaveDocument
    {
        /// <summary>
        /// The only version this build writes. A file claiming a higher one is from a newer
        /// build and is never parsed further; see <see cref="SaveStore"/>.
        /// </summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>
        /// Product tag. Present so a file that is not ours - a legacy Python save that
        /// something copied into the directory, say - is refused by name rather than parsed
        /// into default values.
        /// </summary>
        public const string ProductTag = "NeonVoidUnity";

        public int schemaVersion = CurrentSchemaVersion;
        public string product = ProductTag;
        public string saveSlotId = string.Empty;

        public VaccineSaveState vaccine = new VaccineSaveState();

        /// <summary>Committed receipts, oldest first.</summary>
        public List<SaveReceiptRecord> receipts = new List<SaveReceiptRecord>();

        /// <summary>Monotonic. Revision 1 is the created slot; every commit adds one.</summary>
        public int snapshotRevision;

        /// <summary>
        /// Checksum of this document with this field blank. Computed on save, verified on load
        /// and again after the file is replaced.
        /// </summary>
        public string snapshotChecksum = string.Empty;

        public static SaveDocument NewSlot(string slotId, string vaccineId) => new SaveDocument
        {
            saveSlotId = slotId,
            vaccine = VaccineSaveState.NewVaccine(vaccineId),
            snapshotRevision = 1,
        };

        public SaveDocument Clone()
        {
            var copy = new SaveDocument
            {
                schemaVersion = schemaVersion,
                product = product,
                saveSlotId = saveSlotId,
                vaccine = vaccine.Clone(),
                receipts = new List<SaveReceiptRecord>(receipts.Count),
                snapshotRevision = snapshotRevision,
                snapshotChecksum = snapshotChecksum,
            };

            for (int i = 0; i < receipts.Count; ++i)
                copy.receipts.Add(receipts[i].Clone());

            return copy;
        }

        /// <summary>The receipt with this id, or null. The store's idempotency check.</summary>
        public SaveReceiptRecord FindReceipt(string receiptId)
        {
            if (string.IsNullOrEmpty(receiptId))
                return null;

            for (int i = 0; i < receipts.Count; ++i)
            {
                if (receipts[i].receiptId == receiptId)
                    return receipts[i];
            }

            return null;
        }

        public void Validate()
        {
            if (schemaVersion != CurrentSchemaVersion)
                throw new InvalidOperationException(
                    $"Schema {schemaVersion} is not {CurrentSchemaVersion}; migration is not automatic (docs/04 section 4).");
            if (product != ProductTag)
                throw new InvalidOperationException($"'{product}' is not a {ProductTag} document.");
            if (string.IsNullOrWhiteSpace(saveSlotId))
                throw new InvalidOperationException("A save document needs its slot id.");
            if (snapshotRevision < 1)
                throw new InvalidOperationException("Revisions start at 1.");
            if (vaccine == null)
                throw new InvalidOperationException("A save document without a vaccine holds nothing.");

            vaccine.Validate();

            for (int i = 0; i < receipts.Count; ++i)
            {
                SaveReceiptRecord receipt = receipts[i];
                if (receipt == null || string.IsNullOrWhiteSpace(receipt.receiptId))
                    throw new InvalidOperationException($"Receipt {i} has no id, so nothing can be deduped by it.");

                for (int j = i + 1; j < receipts.Count; ++j)
                {
                    if (receipts[j] != null && receipts[j].receiptId == receipt.receiptId)
                        throw new InvalidOperationException(
                            $"Receipt {receipt.receiptId} appears twice; one command was committed twice.");
                }
            }
        }
    }
}
