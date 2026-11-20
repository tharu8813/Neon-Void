using System;
using System.Collections.Generic;
using NeonVoid.Core;

namespace NeonVoid.Progression.Save
{
    /// <summary>
    /// What a successful extraction banks, as figures somebody else computed.
    /// <para>
    /// The same split as <see cref="DeathSettlement"/>: the command applies figures and
    /// <see cref="ExtractionSettlementPolicy"/> holds the [초안 v0.1] arithmetic, so retuning the
    /// economy never reopens the thing that writes to disk.
    /// </para>
    /// <para>
    /// <b><see cref="PurifiedAreas"/> is a claim, not an input.</b> It is the count the XP figure
    /// was computed from, and <see cref="ExtractRunCommand"/> checks it against the purification
    /// receipts this slot actually committed. They can only disagree if a purification was shown
    /// to the player and never committed - and docs/04 section 5 step 7 is explicit that the
    /// reward follows the commit, not the screen. So a disagreement is refused rather than paid.
    /// </para>
    /// </summary>
    public readonly struct ExtractionSettlement
    {
        /// <summary>영구 XP banked by this extraction.</summary>
        public readonly long PermanentXp;

        /// <summary>성장 포인트 banked by this extraction, beyond those its levels pay.</summary>
        public readonly int StatPoints;

        /// <summary>정화한 하드웨어 수 the figures were computed from. Verified, not trusted.</summary>
        public readonly int PurifiedAreas;

        /// <summary>주요 목표 달성 수, recorded on the receipt.</summary>
        public readonly int ObjectivesCompleted;

        public ExtractionSettlement(long permanentXp, int statPoints, int purifiedAreas, int objectivesCompleted)
        {
            if (permanentXp < 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(permanentXp), permanentXp, "An extraction banks XP; it never takes any away.");
            }

            if (statPoints < 0)
                throw new ArgumentOutOfRangeException(nameof(statPoints), statPoints, "Points banked are never negative.");
            if (purifiedAreas < 1)
            {
                // [확정] docs/04 section 7: 정화 0개 철수 → 성공 불가, 보상 지급 금지. A settlement
                // claiming no purification is not a settlement for a successful extraction, so it
                // cannot be constructed at all.
                throw new ArgumentOutOfRangeException(
                    nameof(purifiedAreas), purifiedAreas,
                    "성공 철수는 최소 1개 정화를 요구한다 ([확정] docs/04 section 7).");
            }

            if (purifiedAreas > DesignConstants.HardwareCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(purifiedAreas), purifiedAreas,
                    $"There are only {DesignConstants.HardwareCount} hardware areas to purify.");
            }

            if (objectivesCompleted < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(objectivesCompleted), objectivesCompleted, "Objectives are counted from 0.");
            }

            PermanentXp = permanentXp;
            StatPoints = statPoints;
            PurifiedAreas = purifiedAreas;
            ObjectivesCompleted = objectivesCompleted;
        }

        public override string ToString() =>
            $"+{PermanentXp}xp +{StatPoints}p from {PurifiedAreas} areas / {ObjectivesCompleted} objectives";
    }

    /// <summary>
    /// The [초안 v0.1] extraction arithmetic of GDD v1 section 5.6, kept apart from the save
    /// transaction so the economy can be retuned without touching it.
    /// <para>
    /// Unlike <see cref="DeathSettlementPolicy"/>, this one is connected: D-011's safe handling
    /// is to use the GDD's own draft curve as separable data, which is exactly what this is. The
    /// death settlement stays disconnected because D-012 asks a different question - what share
    /// of unbanked progress a <i>failure</i> keeps - and no draft answers it with approval.
    /// </para>
    /// </summary>
    public static class ExtractionSettlementPolicy
    {
        /// <summary>[초안 v0.1] GDD v1 section 5.6: 캐시 1개당 1P 상당.</summary>
        public const int DraftPointsPerGrowthCache = 1;

        /// <summary>
        /// The settlement for a mission that purified <paramref name="purifiedAreas"/> areas,
        /// completed <paramref name="objectivesCompleted"/> objectives and collected
        /// <paramref name="growthCachesCollected"/> growth caches.
        /// <para>
        /// The caches are passed in rather than rolled here: GDD v1 gives them a 40% appearance
        /// chance per area, and a roll belongs to the mission's reward stream
        /// (<see cref="Missions.RandomStream.Reward"/>) where it is reproducible from the run
        /// seed - not to a settlement computed at the end.
        /// </para>
        /// </summary>
        public static ExtractionSettlement Draft(int purifiedAreas, int objectivesCompleted, int growthCachesCollected)
        {
            if (growthCachesCollected < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(growthCachesCollected), growthCachesCollected, "Caches are counted from 0.");
            }

            return new ExtractionSettlement(
                VaccineXpBudget.XpEarned(purifiedAreas, objectivesCompleted),
                growthCachesCollected * DraftPointsPerGrowthCache,
                purifiedAreas,
                objectivesCompleted);
        }
    }

    /// <summary>
    /// `ExtractRun(receiptId, learnIds)` from docs/04 section 5: the successful end of a mission.
    /// <para>
    /// [확정] rules this applies literally:
    /// </para>
    /// <list type="bullet">
    /// <item>최소 1개 정화 뒤 성공 철수 (docs/01 section 1, R-004). Checked against the
    /// purification receipts this slot committed, not against a number the caller claims - see
    /// <see cref="SaveDocument.CountReceipts"/>.</item>
    /// <item>이중 철수 차단 (section 5). The receipt id is <c>extract:{runId}</c>, so a second
    /// extraction of one run is answered from the first one's receipt and pays nothing again.</item>
    /// <item>일반·레어 플러그인을 성공 철수 후 0~2개 새로 학습 (section 3.1, R-015). At most
    /// <see cref="DesignConstants.LearnablePluginsPerSuccessfulRun"/>, and zero is allowed.</item>
    /// <item>성공 철수에서 살아 돌아온 학습 소지분은 자동 DB 입고가 아니다 (section 3.5). The
    /// carried list is left exactly as it was; the player moves it with
    /// <see cref="MovePluginCommand"/> in the hub.</item>
    /// </list>
    /// <para>
    /// <b>Newly learned instances go to the archive, not the loadout.</b> Section 3.3 treats
    /// 학습된 플러그인 as what the 보관 DB holds, and the loadout is what is lost on the next death
    /// (section 3.5) - so putting something just learned there would make learning undone by one
    /// death. D-002 is still open about the <i>relationship</i> between re-archiving carried
    /// learned plugins and this limit of two; nothing here touches the carried list, so that
    /// question stays untouched too.
    /// </para>
    /// <para>
    /// <b>What it cannot check.</b> 전설·신화는 학습 불가 ([확정] section 3.2). The save holds
    /// instance identity and ownership and nothing about what a plugin is - deliberately, because
    /// docs/03's ids are proposal ids and writing one into a save would make it permanent by
    /// accident. So rarity is a precondition of whatever offers the learn, and is named here
    /// rather than quietly dropped.
    /// </para>
    /// </summary>
    public sealed class ExtractRunCommand : ISaveCommand
    {
        private readonly long _runId;
        private readonly ExtractionSettlement _settlement;
        private readonly List<string> _learnedInstanceIds;
        private readonly VaccineLevelPolicy _levels;

        public ExtractRunCommand(long runId, ExtractionSettlement settlement, IReadOnlyList<string> learnedInstanceIds)
            : this(runId, settlement, learnedInstanceIds, VaccineLevelPolicy.Draft)
        {
        }

        public ExtractRunCommand(
            long runId,
            ExtractionSettlement settlement,
            IReadOnlyList<string> learnedInstanceIds,
            VaccineLevelPolicy levels)
        {
            if (runId < 1L)
                throw new ArgumentOutOfRangeException(nameof(runId), runId, "Run ids start at 1.");
            if (!levels.IsValid)
                throw new ArgumentException("An extraction that banks XP needs a level policy.", nameof(levels));

            _learnedInstanceIds = new List<string>();
            if (learnedInstanceIds != null)
            {
                for (int i = 0; i < learnedInstanceIds.Count; ++i)
                    _learnedInstanceIds.Add(learnedInstanceIds[i]);
            }

            if (_learnedInstanceIds.Count > DesignConstants.LearnablePluginsPerSuccessfulRun)
            {
                // [확정] R-015. Refused at construction rather than trimmed: trimming would pick
                // which of the player's chosen plugins to drop, silently.
                throw new ArgumentException(
                    $"성공 철수 시 신규 학습은 최대 {DesignConstants.LearnablePluginsPerSuccessfulRun}개이다 " +
                    $"([확정] docs/04 section 3.1); {_learnedInstanceIds.Count} were offered.",
                    nameof(learnedInstanceIds));
            }

            _runId = runId;
            _settlement = settlement;
            _levels = levels;
        }

        public static string ReceiptIdFor(long runId) => $"extract:{runId}";

        public string ReceiptId => ReceiptIdFor(_runId);

        public string Name => "ExtractRun";

        public SaveCommandOutcome Apply(SaveDocument next)
        {
            if (next == null)
                throw new ArgumentNullException(nameof(next));

            VaccineSaveState vaccine = next.vaccine;
            if (!vaccine.alive)
                return SaveCommandOutcome.Reject($"vaccine {vaccine.id} was destroyed");

            if (_runId > next.runsStarted)
            {
                return SaveCommandOutcome.Reject(
                    $"run {_runId} was never begun in this slot (it has begun {next.runsStarted})");
            }

            if (_settlement.PurifiedAreas < 1)
            {
                // Unreachable through the constructor, which refuses it. Checked anyway: this is
                // the forbidden row of docs/04 section 7 (정화 0개 철수 → 보상 지급 금지) and a
                // default-constructed struct would otherwise walk straight past it.
                return SaveCommandOutcome.Reject("성공 철수는 최소 1개 정화를 요구한다");
            }

            int committedPurifications = next.CountReceipts(CleanseHardwareCommand.ReceiptPrefixFor(_runId));
            if (committedPurifications < 1)
            {
                return SaveCommandOutcome.Reject(
                    $"run {_runId} has no committed purification, so there is no successful extraction to settle");
            }

            if (committedPurifications != _settlement.PurifiedAreas)
            {
                // The reward was computed from a number of purifications this slot did not
                // commit. Paying it would be paying for what a screen said rather than for what
                // was saved, which is the inversion docs/04 section 5 step 7 exists to prevent.
                return SaveCommandOutcome.Reject(
                    $"the settlement claims {_settlement.PurifiedAreas} purified areas but run {_runId} " +
                    $"committed {committedPurifications}");
            }

            string ownershipProblem = FindOwnershipProblem(vaccine);
            if (ownershipProblem != null)
                return SaveCommandOutcome.Reject(ownershipProblem);

            // [확정] 이미 승인된 영구 XP/포인트는 빼앗지 않는다, and the level-up happens in the
            // same transaction as the XP for the reason RegisterDeath gives.
            VaccineGrowth growth = _levels.Grant(
                vaccine.permanentLevel, vaccine.permanentXp, vaccine.unusedStatPoints, _settlement.PermanentXp);

            vaccine.permanentLevel = growth.Level;
            vaccine.permanentXp = growth.TotalXp;
            vaccine.unusedStatPoints = growth.UnusedStatPoints + _settlement.StatPoints;

            // 학습: into the archive. The carried list is not touched - section 3.5.
            for (int i = 0; i < _learnedInstanceIds.Count; ++i)
                vaccine.archivePluginInstanceIds.Add(_learnedInstanceIds[i]);

            return SaveCommandOutcome.Accept(
                $"run={_runId};purified={committedPurifications};objectives={_settlement.ObjectivesCompleted};" +
                $"bankedXp={_settlement.PermanentXp};bankedPoints={_settlement.StatPoints};" +
                $"learned={_learnedInstanceIds.Count};level={growth.Level};levelsGained={growth.LevelsGained};" +
                $"levelPoints={growth.PointsGained};points={vaccine.unusedStatPoints};" +
                $"archive={vaccine.archivePluginInstanceIds.Count};loadout={vaccine.carriedPluginInstanceIds.Count}");
        }

        /// <summary>
        /// Whether the instances being learned can be owned, or a reason they cannot.
        /// <para>
        /// An id the vaccine already holds would either be refused later by
        /// <see cref="SaveDocument.Validate"/> - after the XP had been computed - or, if it were
        /// in the archive already, silently duplicate it there. Both are the 중복 잔존 that
        /// docs/04 section 7 forbids, so they are caught before anything is written.
        /// </para>
        /// </summary>
        private string FindOwnershipProblem(VaccineSaveState vaccine)
        {
            for (int i = 0; i < _learnedInstanceIds.Count; ++i)
            {
                string id = _learnedInstanceIds[i];
                if (string.IsNullOrWhiteSpace(id))
                    return $"a learned instance at {i} has no id";

                for (int j = i + 1; j < _learnedInstanceIds.Count; ++j)
                {
                    if (_learnedInstanceIds[j] == id)
                        return $"{id} was offered to be learned twice";
                }

                if (vaccine.archivePluginInstanceIds.Contains(id))
                    return $"{id} is already in the archive";
                if (vaccine.carriedPluginInstanceIds.Contains(id))
                    return $"{id} is already carried";
            }

            return null;
        }
    }
}
