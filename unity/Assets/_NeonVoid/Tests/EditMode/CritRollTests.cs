using NeonVoid.Combat;
using NeonVoid.Core;
using NeonVoid.Missions;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// [확정] docs/02 section 3: 치명타 가능 여부는 공격별 데이터, and the chance is the
    /// attacker's stat. docs/06 section 4 keeps combat randomness in its own stream so a fight
    /// can be replayed.
    /// <para>
    /// All of the crit arithmetic was already wired through DamageMath and every caller passed
    /// <c>critRolled: false</c>, so crit chance was a stat that did nothing. These tests are
    /// about the roll that was missing.
    /// </para>
    /// </summary>
    public sealed class CritRollTests
    {
        private static CombatStats WithChance(float percent)
        {
            CombatStats stats = CombatStats.StartingVaccine();
            stats.CritChancePercent = percent;
            stats.AttackDamage = 10f;
            return stats;
        }

        private static DamageRequest Attack(bool canCrit = true) => new DamageRequest
        {
            Flat = 100f,
            Type = DamageType.True,
            CanCrit = canCrit,
        };

        private static int CountCrits(float chancePercent, bool canCrit, int rolls, uint seed = 12345u)
        {
            var rng = new DeterministicRng(seed);
            DamageRequest request = Attack(canCrit);
            CombatStats attacker = WithChance(chancePercent);

            int crits = 0;
            for (int i = 0; i < rolls; ++i)
                if (CritRoll.Roll(request, attacker, rng))
                    crits++;

            return crits;
        }

        // --- the decided cases -------------------------------------------------------------

        [Test]
        public void AnAttackThatCannotCritNeverDoesHoweverLuckyTheAttacker()
        {
            // 공격별 데이터: the attack decides whether a crit is even possible, and no amount
            // of crit chance overrides it.
            Assert.AreEqual(0, CountCrits(100f, canCrit: false, rolls: 500));
        }

        [Test]
        public void NoChanceMeansNoCrits()
        {
            Assert.AreEqual(0, CountCrits(0f, canCrit: true, rolls: 500));
        }

        [Test]
        public void CertaintyMeansEveryHit()
        {
            Assert.AreEqual(500, CountCrits(100f, canCrit: true, rolls: 500));
            Assert.AreEqual(500, CountCrits(140f, canCrit: true, rolls: 500), "Above 100 is still certain.");
        }

        [Test]
        public void ADecidedOutcomeDrawsNoNumber()
        {
            // The stream must not be advanced by attacks whose result was never in doubt, or a
            // basic attack sequence would consume different draws depending on whether a
            // damage-over-time tick happened to land between two of them.
            var rng = new DeterministicRng(777u);
            uint before = rng.State;

            CritRoll.Roll(Attack(canCrit: false), WithChance(50f), rng);
            CritRoll.Roll(Attack(), WithChance(0f), rng);
            CritRoll.Roll(Attack(), WithChance(100f), rng);

            Assert.AreEqual(before, rng.State, "A decided roll moved the stream.");
        }

        [Test]
        public void ANaNChanceIsTreatedAsNoChance()
        {
            Assert.AreEqual(0, CountCrits(float.NaN, canCrit: true, rolls: 100));
        }

        [Test]
        public void WithNoGeneratorNothingCrits()
        {
            // An undecided roll with nothing to draw from must not guess; a crit that happened
            // without the stream could never be replayed.
            Assert.IsFalse(CritRoll.Roll(Attack(), WithChance(50f), null));
        }

        // --- the distribution --------------------------------------------------------------

        [Test]
        public void TheRateFollowsTheChance()
        {
            const int rolls = 20000;

            int quarter = CountCrits(25f, canCrit: true, rolls: rolls);
            int half = CountCrits(50f, canCrit: true, rolls: rolls);

            Assert.AreEqual(rolls * 0.25f, quarter, rolls * 0.03f, "25% drifted too far.");
            Assert.AreEqual(rolls * 0.5f, half, rolls * 0.03f, "50% drifted too far.");
        }

        [Test]
        public void FractionalChancesAreNotRoundedAway()
        {
            // Hundredths of a percent, so 12.5% is exactly that rather than 12 or 13 - which
            // matters once plugins start adding small amounts of crit chance together.
            const int rolls = 20000;
            int lower = CountCrits(12f, canCrit: true, rolls: rolls, seed: 99u);
            int upper = CountCrits(13f, canCrit: true, rolls: rolls, seed: 99u);
            int middle = CountCrits(12.5f, canCrit: true, rolls: rolls, seed: 99u);

            Assert.LessOrEqual(lower, middle);
            Assert.LessOrEqual(middle, upper);
            Assert.AreEqual(10000, CritRoll.Resolution);
        }

        // --- reproducibility ---------------------------------------------------------------

        [Test]
        public void TheSameSeedRollsTheSameFight()
        {
            Assert.AreEqual(
                CountCrits(35f, canCrit: true, rolls: 400, seed: 4242u),
                CountCrits(35f, canCrit: true, rolls: 400, seed: 4242u));
        }

        [Test]
        public void ADifferentRunRollsADifferentFight()
        {
            var a = new RunIdentity(1L, 500u);
            var b = new RunIdentity(2L, 500u);

            int first = CountCrits(35f, true, 400, a.SeedFor(RandomStream.Combat));
            int second = CountCrits(35f, true, 400, b.SeedFor(RandomStream.Combat));

            Assert.AreNotEqual(first, second, "Two runs produced an identical crit sequence.");
        }

        // --- what a crit is worth ----------------------------------------------------------

        [Test]
        public void ARolledCritMultipliesTheDamageByTheAttackersMultiplier()
        {
            CombatStats attacker = WithChance(100f);
            CombatStats defender = CombatStats.StartingVaccine();
            DamageRequest request = Attack();

            HealthPool normalTarget = HealthPool.Full(1000f);
            HealthPool critTarget = HealthPool.Full(1000f);

            DamageOutcome normal = DamageApplication.Apply(
                request, attacker, defender, ref normalTarget, false, LifestealPolicy.Draft(), false, true);
            DamageOutcome crit = DamageApplication.Apply(
                request, attacker, defender, ref critTarget, false, LifestealPolicy.Draft(), true, true);

            Assert.IsFalse(normal.Resolution.IsCritical);
            Assert.IsTrue(crit.Resolution.IsCritical);
            Assert.AreEqual(
                normal.Resolution.AppliedBeforeShield * attacker.CritDamageMultiplier,
                crit.Resolution.AppliedBeforeShield,
                0.01f);
        }

        [Test]
        public void AnAttackThatCannotCritIsNotCriticalEvenWhenTheRollSaysSo()
        {
            // Belt and braces: DamageMath checks CanCrit again, so a caller that rolled for an
            // attack it should not have cannot turn it into a crit.
            CombatStats attacker = WithChance(100f);
            HealthPool target = HealthPool.Full(1000f);

            DamageOutcome outcome = DamageApplication.Apply(
                Attack(canCrit: false), attacker, CombatStats.StartingVaccine(), ref target,
                false, LifestealPolicy.Draft(), critRolled: true, isOnHitEvent: true);

            Assert.IsFalse(outcome.Resolution.IsCritical);
        }
    }
}
