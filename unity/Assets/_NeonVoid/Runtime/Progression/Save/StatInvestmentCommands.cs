using System;
using System.Collections.Generic;

namespace NeonVoid.Progression.Save
{
    /// <summary>
    /// Reads a vaccine's 영구 투자 out of the save document, in the terms the rules use.
    /// <para>
    /// Separate from the document because the document holds strings and integers - a save
    /// file outlives a build and an enum's numbers move - while everything that computes with
    /// an allocation wants <see cref="InvestableStat"/>. A row whose key this build does not
    /// know is skipped rather than refused: a save written by a later build should lose that
    /// row's effect here, not refuse to load.
    /// </para>
    /// </summary>
    public static class VaccineInvestments
    {
        /// <summary>The allocation, as the stat rules take it.</summary>
        public static List<VaccineStatInvestment> Read(VaccineSaveState vaccine)
        {
            var allocation = new List<VaccineStatInvestment>();
            if (vaccine?.permanentInvestments == null)
                return allocation;

            for (int i = 0; i < vaccine.permanentInvestments.Count; ++i)
            {
                StatInvestmentState entry = vaccine.permanentInvestments[i];
                if (entry == null || !StatInvestments.TryParse(entry.stat, out InvestableStat stat))
                    continue;

                allocation.Add(new VaccineStatInvestment(stat, entry.steps, entry.pointsSpent));
            }

            return allocation;
        }

        /// <summary>Points this vaccine has put into the table, across every stat.</summary>
        public static int PointsSpent(VaccineSaveState vaccine)
        {
            int spent = 0;
            if (vaccine?.permanentInvestments == null)
                return spent;

            for (int i = 0; i < vaccine.permanentInvestments.Count; ++i)
            {
                StatInvestmentState entry = vaccine.permanentInvestments[i];
                if (entry != null)
                    spent += entry.pointsSpent;
            }

            return spent;
        }

        /// <summary>One line naming the whole allocation, for a log or the debug overlay.</summary>
        public static string Describe(VaccineSaveState vaccine)
        {
            List<VaccineStatInvestment> allocation = Read(vaccine);
            if (allocation.Count == 0)
                return "투자 없음";

            var text = new System.Text.StringBuilder();
            for (int i = 0; i < allocation.Count; ++i)
            {
                if (i > 0)
                    text.Append(" · ");
                text.Append(StatInvestments.For(allocation[i].Stat).DisplayName)
                    .Append(" x").Append(allocation[i].Steps);
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// Spends 영구 능력치 포인트 on one stat.
    /// <para>
    /// [확정] GDD v1 section 4.3: 백신 레벨업마다 영구 포인트를 지급하고, 허브에서 투자한다. The
    /// amounts and the costs are [초안] (GDD v1 section 13.1 says so, and D-011 holds the
    /// balance open), so they come from <see cref="StatInvestments"/> rather than from here.
    /// </para>
    /// <para>
    /// <b>A stat nothing applies is refused.</b> Seven rows of the table have no field in the
    /// combat stat block - 이동속도, 발사·탄환 수치, 행운, 마나 - and taking a point for one of
    /// them would spend something permanent on nothing at all. The refusal names the reason, so
    /// connecting one later is a visible change rather than a silent one.
    /// </para>
    /// <para>
    /// <b>Why the points are checked and not clamped.</b> A player who asks to spend more than
    /// they have has been shown a stale number, and committing a smaller purchase than they
    /// asked for is the kind of quiet correction docs/04 section 7 forbids for rewards. The
    /// command refuses and says what they actually have.
    /// </para>
    /// </summary>
    public sealed class InvestStatPointsCommand : ISaveCommand
    {
        private readonly InvestableStat _stat;
        private readonly int _steps;
        private readonly string _nonce;

        /// <param name="steps">How many times to invest. Each costs the stat's point cost.</param>
        /// <param name="nonce">
        /// Identifies this purchase. A player may legitimately invest in the same stat again,
        /// so the stat and the amount are not enough to tell a retry from a second purchase -
        /// docs/04 section 5 lists commandNonce for exactly this.
        /// </param>
        public InvestStatPointsCommand(InvestableStat stat, int steps, string nonce)
        {
            if (steps < 1)
                throw new ArgumentOutOfRangeException(nameof(steps), steps, "An investment buys at least one step.");
            if (string.IsNullOrWhiteSpace(nonce))
            {
                throw new ArgumentException(
                    "An investment needs a nonce, or a retry could not be told from a second purchase.",
                    nameof(nonce));
            }

            // Throws for a value that is not in the table at all, which is a programming error
            // rather than a refusal: the caller got the stat from the table in the first place.
            StatInvestments.For(stat);

            _stat = stat;
            _steps = steps;
            _nonce = nonce;
        }

        /// <summary>
        /// The idempotency key. It carries the purchase, not just the nonce, and that is
        /// deliberate: a terminal that lost the acknowledgement of one purchase and then made a
        /// different one under the same nonce would be answered with the first purchase's
        /// result, and the player would be given a stat they did not ask for. With the stat and
        /// the amount in the key, only a retry of the identical purchase can replay.
        /// </summary>
        public static string ReceiptIdFor(InvestableStat stat, int steps, string nonce) =>
            $"invest:{StatInvestments.SaveKeyOf(stat)}:{steps}:{nonce}";

        public string ReceiptId => ReceiptIdFor(_stat, _steps, _nonce);

        public string Name => "InvestStatPoints";

        public SaveCommandOutcome Apply(SaveDocument next)
        {
            if (next == null)
                throw new ArgumentNullException(nameof(next));

            VaccineSaveState vaccine = next.vaccine;
            if (!vaccine.alive)
            {
                // 최종 파기 resets every bit of this vaccine's growth (docs/04 section 2).
                return SaveCommandOutcome.Reject($"vaccine {vaccine.id} was destroyed; its growth is gone");
            }

            StatInvestmentStep row = StatInvestments.For(_stat);
            if (!row.IsConnected)
            {
                return SaveCommandOutcome.Reject(
                    $"{row.DisplayName}에 포인트를 쓸 수 없다: {StatInvestments.WhyNotConnected(_stat)}");
            }

            int cost = row.DraftPointCost * _steps;
            if (vaccine.unusedStatPoints < cost)
            {
                return SaveCommandOutcome.Reject(
                    $"{row.DisplayName} x{_steps} costs {cost} points and {vaccine.unusedStatPoints} are unspent");
            }

            StatInvestmentState entry = Find(vaccine, row.SaveKey);
            if (entry == null)
            {
                entry = new StatInvestmentState { stat = row.SaveKey };
                vaccine.permanentInvestments.Add(entry);
            }

            vaccine.unusedStatPoints -= cost;
            entry.steps += _steps;
            entry.pointsSpent += cost;

            return SaveCommandOutcome.Accept(
                $"stat={row.SaveKey};steps={entry.steps};spent={entry.pointsSpent};" +
                $"paid={cost};unused={vaccine.unusedStatPoints}");
        }

        internal static StatInvestmentState Find(VaccineSaveState vaccine, string saveKey)
        {
            for (int i = 0; i < vaccine.permanentInvestments.Count; ++i)
            {
                StatInvestmentState entry = vaccine.permanentInvestments[i];
                if (entry != null && string.Equals(entry.stat, saveKey, StringComparison.Ordinal))
                    return entry;
            }

            return null;
        }
    }

    /// <summary>
    /// 무료 재분배: gives every spent point back and empties the allocation.
    /// <para>
    /// [확정] GDD v1 section 4.3: 허브에서 재분배 비용 없이 언제든 초기화·재투자. No cost, no
    /// cooldown, no partial refund - this command exists to make that one sentence true, and a
    /// fee or a limit added later would have to change it here where the rule is written down.
    /// </para>
    /// <para>
    /// <b>It refunds what was paid, not what it would cost today.</b> That is the whole reason
    /// the document stores the points beside the steps: the [초안] costs will be retuned
    /// (D-011), and a refund priced at the new cost would create points out of a patch, or
    /// destroy them.
    /// </para>
    /// </summary>
    public sealed class RedistributeStatPointsCommand : ISaveCommand
    {
        private readonly string _nonce;

        public RedistributeStatPointsCommand(string nonce)
        {
            if (string.IsNullOrWhiteSpace(nonce))
                throw new ArgumentException("A redistribution needs a nonce to be retried under.", nameof(nonce));

            _nonce = nonce;
        }

        public static string ReceiptIdFor(string nonce) => $"redistribute:{nonce}";

        public string ReceiptId => ReceiptIdFor(_nonce);

        public string Name => "RedistributeStatPoints";

        public SaveCommandOutcome Apply(SaveDocument next)
        {
            if (next == null)
                throw new ArgumentNullException(nameof(next));

            VaccineSaveState vaccine = next.vaccine;
            if (!vaccine.alive)
                return SaveCommandOutcome.Reject($"vaccine {vaccine.id} was destroyed; its growth is gone");

            int refunded = VaccineInvestments.PointsSpent(vaccine);
            if (refunded == 0)
            {
                // Nothing to give back. Refused rather than accepted, so a terminal the player
                // leans on does not write a revision per press for no change at all.
                return SaveCommandOutcome.Reject("투자한 포인트가 없다");
            }

            vaccine.permanentInvestments.Clear();
            vaccine.unusedStatPoints += refunded;

            return SaveCommandOutcome.Accept(
                $"refunded={refunded};unused={vaccine.unusedStatPoints}");
        }
    }
}
