using System;
using System.Globalization;

namespace NeonVoid.Progression.Save
{
    /// <summary>
    /// Reads one field out of a command result.
    /// <para>
    /// Results are stored as text because the store must be able to hand a replay back exactly
    /// what the first attempt produced without knowing what any command means. That makes the
    /// text the wire format between a command and its caller, so parsing it belongs in one
    /// tested place rather than in each caller's string handling.
    /// </para>
    /// </summary>
    public static class SaveResultFields
    {
        public static bool TryGetInt(string result, string field, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(result) || string.IsNullOrEmpty(field))
                return false;

            string prefix = field + "=";
            string[] parts = result.Split(';');
            for (int i = 0; i < parts.Length; ++i)
            {
                if (!parts[i].StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                return int.TryParse(
                    parts[i].Substring(prefix.Length), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out value);
            }

            return false;
        }
    }

    /// <summary>
    /// Takes the next run id from permanent state, which is the half of
    /// <c>BeginRun(runId,seed)</c> that can be built today.
    /// <para>
    /// docs/04 section 5 gives BeginRun two jobs: refuse 같은 runId 중복 시작, and record the
    /// run. The second needs the run snapshot, whose fields - pendingRewards, 천장, 리롤,
    /// rngStreams - are all 미확정 (D-017), so it is not here. The first needs nothing but a
    /// monotonic counter, and without it the death receipt key is not unique, which silently
    /// breaks clone accounting from the second launch onwards.
    /// </para>
    /// <para>
    /// The seed is deliberately not stored either. It is logged by the mission so a run can be
    /// reproduced, and persisting it would be the beginning of the run snapshot that D-017
    /// holds back.
    /// </para>
    /// </summary>
    public sealed class AllocateRunIdCommand : ISaveCommand
    {
        private readonly string _nonce;

        /// <param name="nonce">
        /// Identifies this attempt to begin a run. A retry after a lost acknowledgement carries
        /// the same one and is answered with the id already allocated, rather than burning a
        /// second one.
        /// </param>
        public AllocateRunIdCommand(string nonce)
        {
            if (string.IsNullOrWhiteSpace(nonce))
                throw new ArgumentException("Beginning a run needs a nonce to be retried under.", nameof(nonce));

            _nonce = nonce;
        }

        public static string ReceiptIdFor(string nonce) => $"beginrun:{nonce}";

        public string ReceiptId => ReceiptIdFor(_nonce);

        public string Name => "AllocateRunId";

        /// <summary>The run id in a committed or replayed result, or 0.</summary>
        public static long RunIdIn(string result) =>
            SaveResultFields.TryGetInt(result, "runId", out int id) ? id : 0L;

        public SaveCommandOutcome Apply(SaveDocument next)
        {
            if (next == null)
                throw new ArgumentNullException(nameof(next));

            if (!next.vaccine.alive)
            {
                return SaveCommandOutcome.Reject(
                    $"vaccine {next.vaccine.id} was destroyed and cannot begin a run");
            }

            if (next.runsStarted == int.MaxValue)
                return SaveCommandOutcome.Reject("this slot has run out of run ids");

            next.runsStarted += 1;
            return SaveCommandOutcome.Accept($"runId={next.runsStarted}");
        }
    }

    /// <summary>
    /// The vaccine that follows 최종 파기, in the same slot and the next generation.
    /// <para>
    /// docs/04 section 2 ends the fourth death at 백신 선택 화면, and section 1 gives the
    /// vaccine a lifetime of 백신 생성→최종 파기 - so a destroyed vaccine is not the end of the
    /// save, it is the end of that vaccine. The schema's <c>generation</c> field exists for
    /// this, and section 5 lists vaccineGeneration among the keys that stop a command applying
    /// to the wrong vaccine.
    /// </para>
    /// <para>
    /// <b>What is kept.</b> The receipt log, because a retry of one of the destroyed vaccine's
    /// commands must still be refused, and <see cref="SaveDocument.runsStarted"/>, so the new
    /// vaccine's first run cannot reuse an id the dead one's deaths were keyed on. Everything
    /// the destruction cleared stays cleared - this command grants nothing back.
    /// </para>
    /// <para>
    /// <b>Which vaccine the player gets is not decided here.</b> docs/04 says a selection
    /// screen; there is none, and the starting loadout and any choice it offers are not
    /// specified. This creates the documented default - three clones, level 1, nothing carried -
    /// and names the vaccine with whatever id the caller supplies.
    /// </para>
    /// </summary>
    public sealed class StartNextVaccineCommand : ISaveCommand
    {
        private readonly string _vaccineId;

        public StartNextVaccineCommand(string vaccineId, string receiptId)
        {
            if (string.IsNullOrWhiteSpace(vaccineId))
                throw new ArgumentException("A new vaccine needs an id.", nameof(vaccineId));
            if (string.IsNullOrWhiteSpace(receiptId))
                throw new ArgumentException("A command needs a receipt id.", nameof(receiptId));

            _vaccineId = vaccineId;
            ReceiptId = receiptId;
        }

        public string ReceiptId { get; }

        public string Name => "StartNextVaccine";

        public SaveCommandOutcome Apply(SaveDocument next)
        {
            if (next == null)
                throw new ArgumentNullException(nameof(next));

            if (next.vaccine.alive)
            {
                // Replacing a living vaccine would destroy it without a death, which is the one
                // thing 최종 파기 is supposed to be the only route to.
                return SaveCommandOutcome.Reject(
                    $"vaccine {next.vaccine.id} is still alive; only 최종 파기 ends a vaccine");
            }

            int generation = next.vaccine.generation + 1;
            next.vaccine = VaccineSaveState.NewVaccine(_vaccineId);
            next.vaccine.generation = generation;

            return SaveCommandOutcome.Accept(
                $"vaccineId={_vaccineId};generation={generation};" +
                $"copiesRemaining={next.vaccine.copiesRemaining}");
        }
    }
}
