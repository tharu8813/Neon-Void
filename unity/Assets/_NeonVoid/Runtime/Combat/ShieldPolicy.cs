using System;

namespace NeonVoid.Combat
{
    /// <summary>Which shield a hit eats into first. docs/02 section 7: 신규부터? 오래된 것부터?</summary>
    public enum ShieldAbsorptionOrder
    {
        /// <summary>The most recently granted shield absorbs first.</summary>
        NewestFirst = 0,

        /// <summary>The oldest shield absorbs first.</summary>
        OldestFirst = 1,

        /// <summary>The shield that expires soonest absorbs first.</summary>
        ExpiringSoonestFirst = 2,
    }

    /// <summary>What a second shield from the same source does. docs/02 section 7: 갱신·합산.</summary>
    public enum ShieldRefreshRule
    {
        /// <summary>It stands beside the first one as its own shield.</summary>
        Separate = 0,

        /// <summary>It replaces the first one's remaining amount and expiry.</summary>
        Replace = 1,

        /// <summary>Its amount is added to the first one, and the longer expiry wins.</summary>
        Merge = 2,
    }

    /// <summary>
    /// The parts of 보호막 that docs/02 section 7 marks <b>[미확정]</b> and docs/08 tracks as
    /// <b>D-004</b>, isolated so they can be switched when the design decides and so no default
    /// silently becomes the rule.
    /// <para>
    /// CLAUDE.md requires undecided policy to live behind an interface that can wait for a
    /// decision, and docs/08's safe handling for D-004 is 독립 전략 + 테스트 with the live
    /// connection held until approval. These are exactly the open questions: which shield is
    /// eaten first, what a second shield from the same source does, and whether a shield that
    /// runs out of time leaves anything behind.
    /// </para>
    /// <para>
    /// <b>The absorption order is settled; the rest is not.</b> D-004a was approved on
    /// 2026-11-22: 신규부터, the most recently granted shield is eaten first. See
    /// <see cref="ApprovedOrder"/>. The refresh rule and the expiry-tick reading were
    /// explicitly NOT part of that approval and remain [미확정], so they are still one reading
    /// among several - every one implemented and tested, so choosing costs a constant.
    /// </para>
    /// </summary>
    [Serializable]
    public struct ShieldPolicy
    {
        /// <summary>
        /// docs/02 section 7: 보호막 흡수 순서. <b>[확정] 2026-11-22 (docs/08 D-004a)</b>:
        /// <see cref="ApprovedOrder"/>. Still a field rather than a constant because the other
        /// readings remain implemented and tested, and a test may drive them.
        /// </summary>
        public ShieldAbsorptionOrder Order;

        /// <summary>[미확정] docs/02 section 7: 여러 보호막의 갱신·합산.</summary>
        public ShieldRefreshRule Refresh;

        /// <summary>
        /// [미확정] Whether a shield keeps absorbing on the tick it expires. Default false: the
        /// tick it runs out it is gone, which is the reading a player can see on a timer.
        /// </summary>
        public bool AbsorbsOnTheExpiryTick;

        /// <summary>
        /// [확정] docs/08 D-004a, approved 2026-11-22: 신규부터. A shield granted mid-fight is
        /// the one the player just earned and expects to be spending, and oldest-first leaves
        /// short-lived shields expiring unused.
        /// </summary>
        public const ShieldAbsorptionOrder ApprovedOrder = ShieldAbsorptionOrder.NewestFirst;

        /// <summary>
        /// [확정] D-004, approved 2026-11-22: 보호막은 개별적으로 중첩·만료 처리.
        /// <para>
        /// Two grants of the same card stand as two layers with their own timers rather than
        /// one being replaced or the amounts added into a single longer-lived pool. The other
        /// two rules stay implemented and tested so a later change is a constant, not a
        /// rewrite - but they are not what the game does.
        /// </para>
        /// </summary>
        public const ShieldRefreshRule ApprovedRefresh = ShieldRefreshRule.Separate;

        /// <summary>
        /// [확정] D-004, approved 2026-11-22: 만료되는 틱까지 피해를 흡수한다.
        /// <para>
        /// A shield granted at tick 0 for 60 ticks still absorbs on tick 60 and is gone on
        /// tick 61. The player was told it lasts a second; losing it a frame early is the
        /// reading they would experience as the shield not working.
        /// </para>
        /// </summary>
        public const bool ApprovedAbsorbsOnTheExpiryTick = true;

        /// <summary>
        /// The policy in force: the approved order, and the provisional reading of everything
        /// D-004 still leaves open.
        /// <para>
        /// Separate rather than merged, because merging two sources into one amount loses the
        /// information needed to answer the remaining questions later - and it is reversible,
        /// which a merge that has already thrown the sources away is not.
        /// </para>
        /// </summary>
        public static ShieldPolicy Draft() => new ShieldPolicy
        {
            Order = ApprovedOrder,
            Refresh = ApprovedRefresh,
            AbsorbsOnTheExpiryTick = ApprovedAbsorbsOnTheExpiryTick,
        };
    }
}
