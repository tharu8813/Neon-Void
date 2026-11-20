using NeonVoid.Combat.Stats;
using NeonVoid.Combat.Status;
using NeonVoid.Core;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// The five catalogue effects that act on an attack rather than on a stat: 발사속도 증가,
    /// 발사속도 감소, 탄환속도 증가, 관통 and 기능고장. Every one of them was tracked with a
    /// duration and a magnitude that nothing read, so the attack never changed.
    /// </summary>
    public sealed class AttackStatusTests
    {
        private const float BaseRate = 2f;      // docs/02's starting attacks per second

        private static StatusContainer Container() => new StatusContainer(StatusCatalog.Draft());

        private static StatusContainer With(StatusEffectId id, float magnitude)
        {
            StatusContainer status = Container();
            status.Apply(id, magnitude);
            return status;
        }

        // --- the rate ----------------------------------------------------------------------

        [Test]
        public void NothingAppliedLeavesTheBaseRate()
        {
            Assert.AreEqual(1f, StatusStatContribution.AttackSpeedMultiplier(Container()), 1e-4f);
            Assert.AreEqual(1f, StatusStatContribution.AttackSpeedMultiplier(null), 1e-4f);
            Assert.AreEqual(30, AttackRateMath.IntervalTicksFor(BaseRate, 1f, 10f),
                "2 attacks a second is 30 ticks apart.");
        }

        [Test]
        public void AttackSpeedUpShortensTheInterval()
        {
            float multiplier = StatusStatContribution.AttackSpeedMultiplier(
                With(StatusEffectId.AttackSpeedUp, 0.5f));

            Assert.Greater(multiplier, 1f);
            Assert.Less(
                AttackRateMath.IntervalTicksFor(BaseRate, multiplier, 10f),
                AttackRateMath.IntervalTicksFor(BaseRate, 1f, 10f));
        }

        [Test]
        public void AttackSpeedDownLengthensIt()
        {
            float multiplier = StatusStatContribution.AttackSpeedMultiplier(
                With(StatusEffectId.AttackSpeedDown, 0.5f));

            Assert.Less(multiplier, 1f);
            Assert.Greater(
                AttackRateMath.IntervalTicksFor(BaseRate, multiplier, 10f),
                AttackRateMath.IntervalTicksFor(BaseRate, 1f, 10f));
        }

        [Test]
        public void MalfunctionAlsoSlowsTheAttack()
        {
            // docs/02 describes 기능고장 as 공격속도 감소 plus a chance the shot fails, so it
            // has to do both rather than only the dramatic half.
            Assert.Less(
                StatusStatContribution.AttackSpeedMultiplier(With(StatusEffectId.Malfunction, 0.3f)),
                1f);
        }

        [Test]
        public void OppositeEffectsCancelRatherThanCompounding()
        {
            StatusContainer status = Container();
            status.Apply(StatusEffectId.AttackSpeedUp, 0.4f);
            status.Apply(StatusEffectId.AttackSpeedDown, 0.4f);

            Assert.AreEqual(1f, StatusStatContribution.AttackSpeedMultiplier(status), 1e-4f);
        }

        [Test]
        public void AnOverwhelmingSlowStopsTheAttackRatherThanReversingIt()
        {
            StatusContainer status = With(StatusEffectId.AttackSpeedDown, 3f);

            Assert.AreEqual(0f, StatusStatContribution.AttackSpeedMultiplier(status), 1e-4f);
            Assert.AreEqual(
                AttackRateMath.MaxIntervalTicks,
                AttackRateMath.IntervalTicksFor(BaseRate, 0f, 10f),
                "A stopped attack must still have a finite interval.");
        }

        // --- the cap -----------------------------------------------------------------------

        [Test]
        public void TheRateIsHeldAtTheCap()
        {
            // [확정] docs/02: the cap exists; its value is [미확정]. Without one, stacking
            // 발사속도 증가 ends at an interval of one tick - sixty attacks a second.
            const float cap = 10f;

            Assert.AreEqual(
                cap, AttackRateMath.EffectiveAttacksPerSecond(BaseRate, 50f, cap), 1e-4f);
            Assert.AreEqual(
                AttackRateMath.IntervalTicks(cap), AttackRateMath.IntervalTicksFor(BaseRate, 50f, cap));
        }

        [Test]
        public void TheIntervalNeverFallsBelowOneTick()
        {
            Assert.AreEqual(1, AttackRateMath.IntervalTicks(DesignConstants.SimulationTicksPerSecond * 4f));
        }

        [Test]
        public void ACapOfZeroIsRefusedByTheCaps()
        {
            StatCaps caps = StatCaps.Draft();
            caps.MaxAttacksPerSecond = 0f;

            Assert.Throws<System.InvalidOperationException>(() => caps.Validate());
        }

        [Test]
        public void ANonFiniteMultiplierStopsTheAttackRatherThanProducingNonsense()
        {
            Assert.AreEqual(0f, AttackRateMath.EffectiveAttacksPerSecond(BaseRate, float.NaN, 10f), 1e-4f);
            Assert.AreEqual(
                0f, AttackRateMath.EffectiveAttacksPerSecond(BaseRate, float.PositiveInfinity, 10f), 1e-4f);
        }

        // --- projectile speed --------------------------------------------------------------

        [Test]
        public void ProjectileSpeedUpRaisesTheSpeed()
        {
            Assert.Greater(
                StatusStatContribution.ProjectileSpeedMultiplier(
                    With(StatusEffectId.ProjectileSpeedUp, 0.6f)),
                1f);
            Assert.AreEqual(1f, StatusStatContribution.ProjectileSpeedMultiplier(Container()), 1e-4f);
        }

        // --- pierce ------------------------------------------------------------------------

        [Test]
        public void PierceAddsTargetsPerStack()
        {
            StatusContainer status = Container();
            Assert.AreEqual(0, StatusStatContribution.ExtraPierceTargets(status));

            status.Apply(StatusEffectId.Pierce, 1f);
            Assert.AreEqual(
                StatusStatContribution.PierceTargetsPerStack,
                StatusStatContribution.ExtraPierceTargets(status));
        }

        [Test]
        public void PierceOnlyRaisesHowManyDifferentEnemiesAShotMayReach()
        {
            // [확정] docs/02 section 5: 관통 lets projectiles pierce, and one projectile still
            // may not hit one enemy twice. That second half is the hit ledger's job, and this
            // number must not be mistaken for it - so the ledger keeps its own tests.
            var ledger = new NeonVoid.Combat.Projectiles.ProjectileHitLedger(
                1 + StatusStatContribution.PierceTargetsPerStack);

            Assert.AreEqual(
                NeonVoid.Combat.Projectiles.ProjectileHitVerdict.Allowed, ledger.TryRegisterHit(7));
            Assert.AreNotEqual(
                NeonVoid.Combat.Projectiles.ProjectileHitVerdict.Allowed, ledger.TryRegisterHit(7),
                "A pierced shot hit the same enemy twice.");
        }

        // --- malfunction's failure chance --------------------------------------------------

        [Test]
        public void MalfunctionProducesAFailureChance()
        {
            Assert.AreEqual(
                30f,
                StatusStatContribution.ShotFailureChancePercent(With(StatusEffectId.Malfunction, 0.3f)),
                1e-3f);
        }

        [Test]
        public void NoMalfunctionMeansNoFailureChance()
        {
            Assert.AreEqual(0f, StatusStatContribution.ShotFailureChancePercent(Container()), 1e-4f);
            Assert.AreEqual(0f, StatusStatContribution.ShotFailureChancePercent(null), 1e-4f);
        }

        [Test]
        public void TheFailureChanceIsHeldAtCertainty()
        {
            // A chance above 100 would still be certainty, and a weapon that fails more than
            // always is not a thing.
            Assert.AreEqual(
                100f,
                StatusStatContribution.ShotFailureChancePercent(With(StatusEffectId.Malfunction, 5f)),
                1e-3f);
        }
    }
}
