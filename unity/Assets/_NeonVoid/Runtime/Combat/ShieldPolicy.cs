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
    /// <b>The defaults below are not an approved decision.</b> D-004's approval condition is
    /// 2개 이상 보호막 예시 승인 - a worked example with two or more shields, agreed. Until
    /// that happens these are one reading among several, every one of which is implemented and
    /// tested so the choice costs a constant rather than a rewrite.
    /// </para>
    /// </summary>
    [Serializable]
    public struct ShieldPolicy
    {
        /// <summary>[미확정] docs/02 section 7: 보호막 흡수 순서.</summary>
        public ShieldAbsorptionOrder Order;

        /// <summary>[미확정] docs/02 section 7: 여러 보호막의 갱신·합산.</summary>
        public ShieldRefreshRule Refresh;

        /// <summary>
        /// [미확정] Whether a shield keeps absorbing on the tick it expires. Default false: the
        /// tick it runs out it is gone, which is the reading a player can see on a timer.
        /// </summary>
        public bool AbsorbsOnTheExpiryTick;

        /// <summary>
        /// The provisional reading. <b>Not an approved decision</b> - see the type comment.
        /// <para>
        /// Newest first, because a shield granted mid-fight is the one the player just earned
        /// and expects to be spending; separate, because merging two sources into one amount
        /// loses the information needed to answer the other two questions later.
        /// </para>
        /// </summary>
        public static ShieldPolicy Draft() => new ShieldPolicy
        {
            Order = ShieldAbsorptionOrder.NewestFirst,
            Refresh = ShieldRefreshRule.Separate,
            AbsorbsOnTheExpiryTick = false,
        };
    }
}
