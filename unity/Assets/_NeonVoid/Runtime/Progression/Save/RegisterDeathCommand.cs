using System;

namespace NeonVoid.Progression.Save
{
    /// <summary>
    /// What a death settles into permanent state, as a number somebody else decided.
    /// <para>
    /// docs/04 section 2 says 사망한 의뢰의 미정산 영구 XP·성장 포인트 중 20~70% is settled, and
    /// calls the rate [초안] with the explicit warning 무조건 확정 계산식으로 취급하지 않는다.
    /// D-012 in docs/08 is the same question, still open. So the amount is not computed here:
    /// the command applies the figure it is handed, and <see cref="DeathSettlementPolicy"/>
    /// holds the draft arithmetic where it can be replaced without touching the save
    /// transaction.
    /// </para>
    /// </summary>
    public readonly struct DeathSettlement
    {
        /// <summary>Permanent XP awarded by this death. Never negative.</summary>
        public readonly long PermanentXp;

        /// <summary>Growth points awarded by this death.</summary>
        public readonly int StatPoints;

        /// <summary>Purifications the failed mission completed, recorded on the receipt.</summary>
        public readonly int Purifications;

        public DeathSettlement(long permanentXp, int statPoints, int purifications)
        {
            if (permanentXp < 0L)
                throw new ArgumentOutOfRangeException(
                    nameof(permanentXp),
                    "A death settles unbanked XP; it never takes approved XP away (docs/04 section 2).");
            if (statPoints < 0)
                throw new ArgumentOutOfRangeException(nameof(statPoints), "Points settled are never negative.");
            if (purifications < 0)
                throw new ArgumentOutOfRangeException(nameof(purifications), "Purifications are counted from 0.");

            PermanentXp = permanentXp;
            StatPoints = statPoints;
            Purifications = purifications;
        }

        /// <summary>
        /// Nothing settled. The honest default while D-012 is open and while no run tracks
        /// 미정산 XP at all: a death that awards zero is a death whose settlement has not been
        /// built, which is visible, rather than one that awards an invented amount, which is not.
        /// </summary>
        public static DeathSettlement None => default;
    }

    /// <summary>
    /// The [초안] preservation rate from docs/04 section 2:
    /// <c>min(70%, 20% + 10% × 정화 구역 수)</c>.
    /// <para>
    /// Kept as a separate pure function for the reason CLAUDE.md gives for every 미확정 policy:
    /// it must be swappable without reopening the thing that writes to disk. Nothing calls this
    /// in the game yet, because no run tracks 미정산 XP - when one does, it supplies the totals
    /// here and the save transaction does not change.
    /// </para>
    /// </summary>
    public static class DeathSettlementPolicy
    {
        public const int DraftFloorPercent = 20;
        public const int DraftCeilingPercent = 70;
        public const int DraftPercentPerPurification = 10;

        /// <summary>The draft rate for a mission that purified this many areas.</summary>
        public static int PreservedPercent(int purifications)
        {
            if (purifications < 0)
                throw new ArgumentOutOfRangeException(nameof(purifications));

            int percent = DraftFloorPercent + (DraftPercentPerPurification * purifications);
            return percent > DraftCeilingPercent ? DraftCeilingPercent : percent;
        }

        /// <summary>
        /// Applies the draft rate. Rounded <b>down</b>: the settlement is a consolation for a
        /// failed mission, and rounding a share of the player's unbanked progress up would make
        /// the permanent total depend on a tie-break nobody chose.
        /// </summary>
        public static DeathSettlement Draft(long uncommittedXp, int uncommittedPoints, int purifications)
        {
            if (uncommittedXp < 0L)
                throw new ArgumentOutOfRangeException(nameof(uncommittedXp));
            if (uncommittedPoints < 0)
                throw new ArgumentOutOfRangeException(nameof(uncommittedPoints));

            int percent = PreservedPercent(purifications);
            return new DeathSettlement(
                uncommittedXp * percent / 100L,
                uncommittedPoints * percent / 100,
                purifications);
        }
    }

    /// <summary>
    /// `RegisterDeath(deathId)` from docs/04 section 5, the first real command on the save
    /// transaction.
    /// <para>
    /// [확정] docs/04 section 2 is the whole rule and it is applied literally. Deaths one to
    /// three take a clone, lose the plugins carried into the mission, keep the archive DB and
    /// every already-approved point of permanent XP, and settle whatever the mission had not
    /// banked. The fourth death destroys the vaccine: its growth, its skills, its DB and its
    /// carried plugins all go.
    /// </para>
    /// <para>
    /// <b>복제 0 is not death.</b> After the third death the vaccine has no clones left and is
    /// still playable - docs/04 calls it 마지막 기회 - so the count of deaths decides
    /// destruction, not the clone counter reaching zero. Reading destruction off
    /// <c>copiesRemaining == 0</c> would destroy the vaccine one mission early, and that is not
    /// a bug anybody could undo.
    /// </para>
    /// <para>
    /// <b>What a destroyed vaccine does not reach.</b> Global settings - graphics, volume,
    /// language, key bindings - survive 최종 파기 [확정], and other vaccine slots survive it
    /// too. Neither is in this document, so this command has nothing to delete them with. That
    /// is the protection: not a rule about what to skip, but an absence of the ability.
    /// </para>
    /// </summary>
    public sealed class RegisterDeathCommand : ISaveCommand
    {
        private readonly DeathId _deathId;
        private readonly DeathSettlement _settlement;
        private readonly VaccineLevelPolicy _levels;

        public RegisterDeathCommand(DeathId deathId, DeathSettlement settlement)
            : this(deathId, settlement, VaccineLevelPolicy.Draft)
        {
        }

        /// <summary>
        /// Takes the level policy so the [초안] curve of docs/04 section 1 is an argument of the
        /// command rather than a number the save transaction reaches for. A test can then state
        /// a curve in one line instead of arithmetic against the shipping draft.
        /// </summary>
        public RegisterDeathCommand(DeathId deathId, DeathSettlement settlement, VaccineLevelPolicy levels)
        {
            if (!deathId.IsValid)
                throw new ArgumentException("A death needs a valid id to be deduped by.", nameof(deathId));
            if (!levels.IsValid)
                throw new ArgumentException("A death that settles XP needs a level policy.", nameof(levels));

            _deathId = deathId;
            _settlement = settlement;
            _levels = levels;
        }

        /// <summary>
        /// The receipt id for a death. Derived from the <see cref="DeathId"/>, so the retry
        /// after a lost acknowledgement - including one from the next launch of the game -
        /// carries the id the first attempt carried. docs/04: 중복 deathId로 두 번 차감 금지.
        /// </summary>
        public static string ReceiptIdFor(DeathId deathId) => $"death:{deathId}";

        public string ReceiptId => ReceiptIdFor(_deathId);

        public string Name => "RegisterDeath";

        public SaveCommandOutcome Apply(SaveDocument next)
        {
            if (next == null)
                throw new ArgumentNullException(nameof(next));

            VaccineSaveState vaccine = next.vaccine;
            if (!vaccine.alive)
                return SaveCommandOutcome.Reject($"vaccine {vaccine.id} was already destroyed");

            // docs/04 section 5's precondition for RegisterDeath is 실제 전투 사망, 런 활성, and
            // this is as much of it as permanent state can check: the death has to belong to a
            // run this slot actually began.
            //
            // It earns its place by turning the worst failure mode here into a loud one. A run
            // id that was never allocated is one another run may also be using, and then this
            // death's receipt id is a receipt id that already exists - so the save answers
            // AlreadyApplied, reports success, and silently charges no clone for the rest of the
            // game. A refusal is visible; that is not.
            if (_deathId.RunId > next.runsStarted)
            {
                return SaveCommandOutcome.Reject(
                    $"run {_deathId.RunId} was never begun in this slot (it has begun " +
                    $"{next.runsStarted}), so this death's receipt id is not unique");
            }

            int deaths = vaccine.deathCount + 1;
            int expectedCopiesBefore = VaccineSaveState.DesignStartingCopies - vaccine.deathCount;
            if (vaccine.copiesRemaining != expectedCopiesBefore)
            {
                // The clone counter and the death counter are two views of one [확정] table. If
                // they disagree the save is inconsistent, and guessing which one is right would
                // either grant a clone or take one.
                return SaveCommandOutcome.Reject(
                    $"inconsistent state: {vaccine.deathCount} deaths should leave " +
                    $"{expectedCopiesBefore} clones, not {vaccine.copiesRemaining}");
            }

            vaccine.deathCount = deaths;

            // [확정] docs/04 section 3.5: 사망 시 출전 소지분은 소실. Both branches, because a
            // destroyed vaccine loses them as well.
            //
            // Counted before it is cleared, and written on the receipt. Until 출전 소지 was
            // wired to the 의뢰 (2026-11-23) this was always zero and nothing was lost; now it
            // is the one number that says how much a death actually cost, and a receipt that
            // did not carry it could never answer "what did I lose" afterwards.
            int carriedLost = vaccine.carriedPluginInstanceIds.Count;
            vaccine.carriedPluginInstanceIds.Clear();

            if (deaths >= VaccineSaveState.DesignDeathsBeforeFinalDestruction)
                return Destroy(vaccine, deaths, carriedLost);

            vaccine.copiesRemaining = VaccineSaveState.DesignStartingCopies - deaths;

            // [확정] 이미 승인된 영구 XP/포인트는 빼앗지 않는다: this only ever adds.
            //
            // The level-up happens here, inside the transaction that writes the XP, so a
            // committed total and the level read off it are one atomic change. Granting XP in
            // the save and levelling up somewhere else afterwards would leave a window - a crash
            // in it, or a lost ack - where the player has the XP and not the level, and the
            // level is where 영구 포인트 come from.
            VaccineGrowth growth = _levels.Grant(
                vaccine.permanentLevel,
                vaccine.permanentXp,
                vaccine.unusedStatPoints,
                _settlement.PermanentXp);

            vaccine.permanentLevel = growth.Level;
            vaccine.permanentXp = growth.TotalXp;

            // Two sources, deliberately kept apart on the receipt. Points from levels are
            // 백신 레벨 1당 영구 포인트 1 (docs/04 section 1); settled points are the share of the
            // failed mission's unbanked 성장 포인트 (section 2). A reader of the receipt can see
            // which was which, and a change to either curve does not make the other unreadable.
            vaccine.unusedStatPoints = growth.UnusedStatPoints + _settlement.StatPoints;

            return SaveCommandOutcome.Accept(
                $"deaths={deaths};copiesRemaining={vaccine.copiesRemaining};destroyed=false;" +
                $"settledXp={_settlement.PermanentXp};settledPoints={_settlement.StatPoints};" +
                $"purifications={_settlement.Purifications};carriedLost={carriedLost};" +
                $"level={growth.Level};levelsGained={growth.LevelsGained};" +
                $"levelPoints={growth.PointsGained};points={vaccine.unusedStatPoints}");
        }

        /// <summary>
        /// 최종 파기. The settlement is deliberately not awarded here: this same transaction
        /// destroys the vaccine's permanent growth, so adding XP first and erasing it a line
        /// later would only put a number on the receipt that the player never had. The receipt
        /// says so rather than leaving the zero to be read as a settlement that failed.
        /// </summary>
        private SaveCommandOutcome Destroy(VaccineSaveState vaccine, int deaths, int carriedLost)
        {
            vaccine.alive = false;
            vaccine.copiesRemaining = 0;

            int archiveLost = vaccine.archivePluginInstanceIds.Count;
            vaccine.archivePluginInstanceIds.Clear();
            vaccine.permanentXp = 0L;
            vaccine.permanentLevel = 1;
            vaccine.unusedStatPoints = 0;

            return SaveCommandOutcome.Accept(
                $"deaths={deaths};copiesRemaining=0;destroyed=true;settledXp=0;settledPoints=0;" +
                $"purifications={_settlement.Purifications};carriedLost={carriedLost};" +
                $"archiveLost={archiveLost};level=1;levelsGained=0;levelPoints=0;points=0;" +
                $"note=settlement-moot-on-destruction");
        }
    }
}
