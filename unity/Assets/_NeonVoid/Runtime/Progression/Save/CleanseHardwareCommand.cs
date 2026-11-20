using System;

namespace NeonVoid.Progression.Save
{
    /// <summary>
    /// `CleanseHardware(area, receiptId)` from docs/04 section 5: one hardware area purified, in
    /// one run, recorded permanently.
    /// <para>
    /// <b>It writes no snapshot field at all, and that is the design.</b> The document holds no
    /// run state (D-017), so there is nowhere to put "CPU is purified" - and nowhere is needed.
    /// The receipt log is permanent and its ids are structured, so the purification exists in
    /// the save as the receipt <c>cleanse:{runId}:{areaId}</c>. That single fact does the work
    /// of three rules the table asks for:
    /// </para>
    /// <list type="bullet">
    /// <item>중복 보상 없음 - a second purification of the same area in the same run carries the
    /// same receipt id, so the store answers AlreadyApplied and nothing is paid twice.</item>
    /// <item>이미 정화한 하드웨어의 완료 상태는 런 동안 유지 (docs/01 section 1) - the record
    /// outlives the process, not just the run.</item>
    /// <item>최소 1개 정화 for an extraction - <see cref="ExtractRunCommand"/> counts these
    /// receipts rather than trusting a figure it was handed.</item>
    /// </list>
    /// <para>
    /// <b>What it cannot check.</b> The table's precondition is 목표/보스 완료. Nothing about
    /// objectives or a boss is in permanent state, and D-017 is why; so that precondition belongs
    /// to whatever offers the purification, exactly as 허브 상호작용 does for
    /// <see cref="MovePluginCommand"/>. It is named here rather than quietly dropped.
    /// </para>
    /// <para>
    /// <b>No XP.</b> The table's 성공 이후 column says 정화 상태·철수 활성/XP, and the XP it means
    /// is 의뢰 XP, which is temporary ([확정] C09) and lives in
    /// <see cref="Missions.MissionRunState"/>. The 백신 영구 XP a purification is worth stays
    /// unbanked until an extraction commits it - docs/04 section 2 settles only a share of it on
    /// death, which it could not do if this had already banked it.
    /// </para>
    /// </summary>
    public sealed class CleanseHardwareCommand : ISaveCommand
    {
        private readonly long _runId;
        private readonly string _areaId;

        /// <param name="runId">
        /// The run this purification belongs to. Part of the receipt id, so purifying CPU in two
        /// different runs is two purifications while purifying it twice in one run is one.
        /// </param>
        /// <param name="areaId">
        /// Which hardware area. An id rather than an enum: docs/01 fixes five hardware areas
        /// [확정], but nothing in this project has named them in code yet, and inventing the
        /// enum here would put those names in a save file before anybody approved them - the
        /// same trap docs/03's proposal ids set for plugin instances.
        /// </param>
        public CleanseHardwareCommand(long runId, string areaId)
        {
            if (runId < 1L)
                throw new ArgumentOutOfRangeException(nameof(runId), runId, "Run ids start at 1.");
            if (string.IsNullOrWhiteSpace(areaId))
                throw new ArgumentException("A purification needs the area it purified.", nameof(areaId));
            if (areaId.IndexOf(':') >= 0)
            {
                // The receipt id is built by joining with colons, so a colon in the area id
                // would let two different areas produce one key - and the second would be
                // refused as already purified.
                throw new ArgumentException(
                    $"An area id must not contain ':' ({areaId}); it is a receipt key separator.", nameof(areaId));
            }

            _runId = runId;
            _areaId = areaId;
        }

        /// <summary>The prefix every purification receipt of one run shares.</summary>
        public static string ReceiptPrefixFor(long runId) => $"cleanse:{runId}:";

        public static string ReceiptIdFor(long runId, string areaId) => $"{ReceiptPrefixFor(runId)}{areaId}";

        public string ReceiptId => ReceiptIdFor(_runId, _areaId);

        public string Name => "CleanseHardware";

        public SaveCommandOutcome Apply(SaveDocument next)
        {
            if (next == null)
                throw new ArgumentNullException(nameof(next));

            if (!next.vaccine.alive)
                return SaveCommandOutcome.Reject($"vaccine {next.vaccine.id} was destroyed");

            // The same check RegisterDeath makes, for the same reason: a run id that was never
            // allocated may be in use by another run, and then this receipt id is one that
            // already exists - so the store would answer AlreadyApplied and a real purification
            // would vanish into another run's record.
            if (_runId > next.runsStarted)
            {
                return SaveCommandOutcome.Reject(
                    $"run {_runId} was never begun in this slot (it has begun {next.runsStarted})");
            }

            // Counted from the log, including this one, which is what the store is about to
            // append. The count is on the receipt so the extraction screen and the recovery
            // screen can read progress without replaying the log themselves.
            int purifiedBefore = next.CountReceipts(ReceiptPrefixFor(_runId));
            if (purifiedBefore >= Core.DesignConstants.HardwareCount)
            {
                return SaveCommandOutcome.Reject(
                    $"run {_runId} has already purified all {Core.DesignConstants.HardwareCount} hardware areas");
            }

            return SaveCommandOutcome.Accept(
                $"run={_runId};area={_areaId};purified={purifiedBefore + 1}");
        }
    }
}
