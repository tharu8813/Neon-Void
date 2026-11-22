using System;
using System.Collections.Generic;
using NeonVoid.Core;

namespace NeonVoid.Plugins
{
    /// <summary>What the player is standing in front of when a 보상 화면 opens.</summary>
    public readonly struct RewardConditions
    {
        public RewardConditions(
            RewardDifficulty difficulty, int purifiedThisMission, int luck)
        {
            Difficulty = difficulty;
            PurifiedThisMission = Math.Max(0, purifiedThisMission);
            Luck = Math.Max(0, luck);
        }

        /// <summary>The difficulty of the area that produced this reward.</summary>
        public RewardDifficulty Difficulty { get; }

        /// <summary>정화 already done in this mission, which raises the upper grades.</summary>
        public int PurifiedThisMission { get; }

        /// <summary>행운, in stat points.</summary>
        public int Luck { get; }

        public override string ToString() =>
            $"{Difficulty} · 정화 {PurifiedThisMission} · 행운 {Luck}";
    }

    /// <summary>One card on the screen.</summary>
    public readonly struct RewardCard
    {
        public RewardCard(PluginDefinition definition, PluginRarity rolledGrade)
        {
            Definition = definition;
            RolledGrade = rolledGrade;
        }

        public PluginDefinition Definition { get; }

        /// <summary>
        /// The grade the slot rolled, which can be higher than the card's own when the pool for
        /// that grade ran out. [확정] D-007: 낮은 등급의 미보유 후보로 대체.
        /// </summary>
        public PluginRarity RolledGrade { get; }

        public bool IsEmpty => string.IsNullOrEmpty(Definition.ProposalId);

        /// <summary>True when the pool for the rolled grade was empty and a lower one filled in.</summary>
        public bool FellBack => !IsEmpty && Definition.Rarity != RolledGrade;

        public override string ToString() =>
            IsEmpty ? "(없음)" : $"{Definition.ProposalId} {Definition.Rarity}" +
                                 (FellBack ? $" (←{RolledGrade})" : string.Empty);
    }

    /// <summary>
    /// A 보상 화면: three slots, each with its own grade, no two the same plugin.
    /// <para>
    /// [확정] docs/03 section 3: 한 보상 화면에 3장, 슬롯마다 등급을 개별 추첨, 유저가 선택한
    /// 한 슬롯만 다시 추첨. [확정] D-007 (2026-11-22): 한 화면에 같은 플러그인이 두 번 나오지
    /// 않고, 등급 후보가 모자라면 낮은 등급의 미보유 후보로 대체하며, 전체가 모자라면 유효한
    /// 것만 표시한다 - 빈 자리를 임의의 중복으로 메우지 않는다.
    /// </para>
    /// </summary>
    public sealed class RewardScreen
    {
        private readonly RewardCard[] _cards;

        internal RewardScreen(RewardCard[] cards, bool showedHighTierOnFirstDeal)
        {
            _cards = cards ?? new RewardCard[0];
            ShowedHighTierOnFirstDeal = showedHighTierOnFirstDeal;
        }

        public IReadOnlyList<RewardCard> Cards => _cards;

        /// <summary>How many slots actually carry a card. Fewer than three when the pool ran dry.</summary>
        public int Offered
        {
            get
            {
                int offered = 0;
                for (int i = 0; i < _cards.Length; ++i)
                {
                    if (!_cards[i].IsEmpty)
                        ++offered;
                }

                return offered;
            }
        }

        /// <summary>Whether the screen's first deal showed a 전설+.</summary>
        public bool ShowedHighTierOnFirstDeal { get; }

        /// <summary>Whether a 전설+ is on the screen right now, rerolls included.</summary>
        public bool ShowsHighTier
        {
            get
            {
                for (int i = 0; i < _cards.Length; ++i)
                {
                    if (!_cards[i].IsEmpty && _cards[i].Definition.Rarity >= PluginRarity.Legendary)
                        return true;
                }

                return false;
            }
        }

        internal void Replace(int slot, in RewardCard card) => _cards[slot] = card;

        internal bool Holds(string proposalId, int exceptSlot)
        {
            for (int i = 0; i < _cards.Length; ++i)
            {
                if (i == exceptSlot || _cards[i].IsEmpty)
                    continue;

                if (string.Equals(_cards[i].Definition.ProposalId, proposalId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        public override string ToString()
        {
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < _cards.Length; ++i)
                text.Append(_cards[i]).Append(i + 1 < _cards.Length ? " / " : string.Empty);

            return text.ToString();
        }
    }

    /// <summary>
    /// Deals 보상 화면 from the approved table.
    /// <para>
    /// <b>Pure, and free of Unity.</b> The distribution is the thing that decides what a run
    /// feels like, so it is driven by tests over tens of thousands of screens rather than by
    /// playing until it looks right.
    /// </para>
    /// <para>
    /// <b>The RNG is per slot.</b> [초안] docs/03 section 3: the stream is keyed on
    /// <c>seed+runId+rewardScreenId+slotId+rerollIndex</c>, so rerolling slot 1 cannot change
    /// what slot 2 shows - "리롤 눌렀는데 등급은 고정" and "다른 카드까지 바뀌었다" are both
    /// bugs, and keying it this way makes neither possible.
    /// </para>
    /// </summary>
    public sealed class RewardRoller
    {
        private readonly RewardRollTable _table;
        private readonly IReadOnlyList<PluginDefinition> _pool;

        /// <summary>
        /// Screens since a 전설+ was last shown. [확정] D-006 (2026-11-22): the count resets on
        /// a 전설+ anywhere on the screen, a rerolled one included. The alternative - only the
        /// first deal counting - was simulated and rejected: it makes the pity build up as the
        /// player spends rerolls, which teaches them to hoard the three they were given.
        /// </summary>
        public int ScreensWithoutHighTier { get; private set; }

        public RewardRoller(IReadOnlyList<PluginDefinition> pool, RewardRollTable table)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _table = table;
        }

        /// <summary>The roller the game uses: every authored card, the approved table.</summary>
        public static RewardRoller Approved() =>
            new RewardRoller(PluginCatalog.All(), RewardRollTable.Approved());

        /// <summary>The 전설+ multiplier in force for the next screen.</summary>
        public float PityMultiplier => _table.PityMultiplier(ScreensWithoutHighTier);

        /// <summary>True when the next screen gets a correction.</summary>
        public bool PityActive => PityMultiplier > 1f;

        /// <summary>
        /// Deals a screen.
        /// </summary>
        /// <param name="owned">
        /// Plugin ids the vaccine already carries. [확정] D-007 names 미보유 후보 for the
        /// fallback, so an owned card is not what an exhausted grade falls back to.
        /// </param>
        public RewardScreen Deal(
            long runId,
            int rewardScreenId,
            in RewardConditions conditions,
            uint sessionSeed,
            IReadOnlyCollection<string> owned = null)
        {
            float pity = PityMultiplier;
            var cards = new RewardCard[DesignConstants.RewardCardSlots];
            var screen = new RewardScreen(cards, false);
            bool highTier = false;

            for (int slot = 0; slot < cards.Length; ++slot)
            {
                DeterministicRng rng = StreamFor(sessionSeed, runId, rewardScreenId, slot, rerollIndex: 0);
                RewardCard card = DealSlot(rng, conditions, pity, screen, slot, owned);
                screen.Replace(slot, card);

                if (!card.IsEmpty && card.Definition.Rarity >= PluginRarity.Legendary)
                    highTier = true;
            }

            var dealt = new RewardScreen(cards, highTier);

            // Counted here, once per screen. [확정] docs/03 section 3 item 5: 보상 화면 단위이며
            // 15장을 따로 세지 않고 리롤 횟수로도 세지 않는다.
            if (highTier)
                ScreensWithoutHighTier = 0;
            else
                ++ScreensWithoutHighTier;

            return dealt;
        }

        /// <summary>
        /// [확정] docs/03 section 3 item 3: rerolls one slot into a new grade and a new plugin,
        /// leaving the other two alone.
        /// </summary>
        /// <returns>False when the slot is out of range.</returns>
        public bool Reroll(
            RewardScreen screen,
            int slot,
            long runId,
            int rewardScreenId,
            int rerollIndex,
            in RewardConditions conditions,
            uint sessionSeed,
            IReadOnlyCollection<string> owned = null)
        {
            if (screen == null || slot < 0 || slot >= screen.Cards.Count)
                return false;

            // The same pity the screen was dealt under. A reroll is part of that screen, so
            // recomputing it here would let a reroll ride a correction the deal did not have.
            float pity = _table.PityMultiplier(
                screen.ShowsHighTier ? 0 : Math.Max(0, ScreensWithoutHighTier - 1));

            DeterministicRng rng = StreamFor(sessionSeed, runId, rewardScreenId, slot, rerollIndex);
            RewardCard card = DealSlot(rng, conditions, pity, screen, slot, owned);
            screen.Replace(slot, card);

            // [확정] D-006: a 전설+ a reroll produced resets the count too.
            if (!card.IsEmpty && card.Definition.Rarity >= PluginRarity.Legendary)
                ScreensWithoutHighTier = 0;

            return true;
        }

        /// <summary>Forgets the pity count, for a new vaccine.</summary>
        public void Reset() => ScreensWithoutHighTier = 0;

        // --- one slot ----------------------------------------------------------------------

        private RewardCard DealSlot(
            DeterministicRng rng,
            in RewardConditions conditions,
            float pity,
            RewardScreen screen,
            int slot,
            IReadOnlyCollection<string> owned)
        {
            PluginRarity rolled = RollGrade(rng, conditions, pity);

            // [확정] D-007: 해당 등급의 후보가 부족하면 낮은 등급의 미보유 후보로 대체.
            for (PluginRarity grade = rolled; ; --grade)
            {
                PluginDefinition picked = PickFrom(rng, grade, screen, slot, owned);
                if (!string.IsNullOrEmpty(picked.ProposalId))
                    return new RewardCard(picked, rolled);

                if (grade == PluginRarity.Common)
                    break;
            }

            // [확정] D-007: 전체 후보가 부족하면 유효한 것만 표시하고 임의로 중복 생성하지 않는다.
            return default;
        }

        private PluginRarity RollGrade(DeterministicRng rng, in RewardConditions conditions, float pity)
        {
            Span<float> weights = stackalloc float[RewardRollTable.Grades];
            float total = 0f;

            for (int i = 0; i < RewardRollTable.Grades; ++i)
            {
                weights[i] = _table.WeightOf(
                    (PluginRarity)i, conditions.Difficulty, conditions.PurifiedThisMission,
                    conditions.Luck, pity);
                total += weights[i];
            }

            if (total <= 0f)
                return PluginRarity.Common;

            // A million buckets, so a 2% weight is not distorted by the integer draw.
            float pick = rng.NextInt(0, 1_000_000) / 1_000_000f * total;
            float running = 0f;
            for (int i = 0; i < RewardRollTable.Grades; ++i)
            {
                running += weights[i];
                if (pick < running)
                    return (PluginRarity)i;
            }

            return PluginRarity.Common;
        }

        /// <summary>
        /// A card of that grade that is neither on the screen already nor owned. Walking the
        /// pool from a rolled offset rather than filtering into a list first: the pool is
        /// twenty-odd entries and this allocates nothing.
        /// </summary>
        private PluginDefinition PickFrom(
            DeterministicRng rng,
            PluginRarity grade,
            RewardScreen screen,
            int slot,
            IReadOnlyCollection<string> owned)
        {
            int eligible = 0;
            for (int i = 0; i < _pool.Count; ++i)
            {
                if (IsEligible(_pool[i], grade, screen, slot, owned))
                    ++eligible;
            }

            if (eligible == 0)
                return default;

            int wanted = rng.NextInt(0, eligible);
            for (int i = 0; i < _pool.Count; ++i)
            {
                if (!IsEligible(_pool[i], grade, screen, slot, owned))
                    continue;

                if (wanted == 0)
                    return _pool[i];

                --wanted;
            }

            return default;
        }

        private static bool IsEligible(
            in PluginDefinition candidate,
            PluginRarity grade,
            RewardScreen screen,
            int slot,
            IReadOnlyCollection<string> owned)
        {
            if (candidate.Rarity != grade || string.IsNullOrEmpty(candidate.ProposalId))
                return false;

            // [확정] D-007: 한 화면에 같은 플러그인이 두 번 나오지 않는다.
            if (screen != null && screen.Holds(candidate.ProposalId, slot))
                return false;

            if (Owns(owned, candidate.ProposalId))
                return false;

            return true;
        }

        /// <summary>
        /// Walked rather than hashed: the caller's collection is whatever it has, and a set
        /// built here per candidate would cost more than the twenty-odd comparisons it saves.
        /// </summary>
        private static bool Owns(IReadOnlyCollection<string> owned, string proposalId)
        {
            if (owned == null || owned.Count == 0)
                return false;

            foreach (string carried in owned)
            {
                if (string.Equals(carried, proposalId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// [초안] docs/03 section 3: <c>seed+runId+rewardScreenId+slotId+rerollIndex</c>.
        /// Mixed rather than added, so two different keys cannot land on the same stream the
        /// way 1+4 and 2+3 would.
        /// </summary>
        public static DeterministicRng StreamFor(
            uint sessionSeed, long runId, int rewardScreenId, int slot, int rerollIndex)
        {
            unchecked
            {
                uint hash = sessionSeed == 0u ? 0x9E3779B9u : sessionSeed;
                hash = (hash * 2654435761u) ^ (uint)runId;
                hash = (hash * 2246822519u) ^ (uint)(runId >> 32);
                hash = (hash * 3266489917u) ^ (uint)rewardScreenId;
                hash = (hash * 668265263u) ^ (uint)slot;
                hash = (hash * 374761393u) ^ (uint)rerollIndex;
                return new DeterministicRng(hash == 0u ? 0x9E3779B9u : hash);
            }
        }
    }
}
