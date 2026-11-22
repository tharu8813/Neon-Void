using NeonVoid.Combat;
using NeonVoid.Core;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// docs/02 section 9 item 2: single, area, damage-over-time and area+DoT omnivamp, with the
    /// 0.33 efficiency applied exactly once.
    /// </summary>
    public sealed class LifestealMathTests
    {
        /// <summary>A hit that already landed 100 damage on health, for clean arithmetic.</summary>
        private static DamageResolution HundredDamage() =>
            new DamageResolution(100f, 1f, 100f, false, DamageType.Physical);

        private static CombatStats WithOmnivamp(float percent)
        {
            var stats = CombatStats.StartingVaccine();
            stats.OmnivampPercent = percent;
            return stats;
        }

        private static DamageRequest Hit(bool isArea, bool isPeriodic) => new DamageRequest
        {
            AdRatio = 1f,
            Type = DamageType.Physical,
            IsArea = isArea,
            IsPeriodic = isPeriodic,
        };

        private static LifestealResult Heal(DamageRequest request, CombatStats attacker) =>
            LifestealMath.Compute(
                request,
                HundredDamage(),
                attacker,
                LifestealPolicy.Draft(),
                shieldAbsorbed: 0f,
                targetHealthBeforeHit: 10_000f,
                isOnHitEvent: false);

        [Test]
        public void DocumentedVector_SingleTargetHealsTheFullPercentage()
        {
            var result = Heal(Hit(false, false), WithOmnivamp(10f));

            Assert.AreEqual(1f, result.OmnivampEfficiency, 1e-6f);
            Assert.AreEqual(10f, result.FromOmnivamp, 1e-3f);
        }

        [Test]
        public void DocumentedVector_AreaOnlyHealsAtThirtyThreePercentEfficiency()
        {
            var result = Heal(Hit(isArea: true, isPeriodic: false), WithOmnivamp(10f));

            Assert.AreEqual(DesignConstants.AreaOrPeriodicOmnivampEfficiency, result.OmnivampEfficiency, 1e-6f);
            Assert.AreEqual(3.3f, result.FromOmnivamp, 1e-3f);
        }

        [Test]
        public void DocumentedVector_PeriodicOnlyHealsAtThirtyThreePercentEfficiency()
        {
            var result = Heal(Hit(isArea: false, isPeriodic: true), WithOmnivamp(10f));

            Assert.AreEqual(3.3f, result.FromOmnivamp, 1e-3f);
        }

        /// <summary>
        /// The rule the whole module exists for. Multiplying a factor per flag would give
        /// 0.33 * 0.33 = 0.1089 and heal 1.089 instead of 3.3.
        /// </summary>
        [Test]
        public void AreaAndPeriodicTogetherStillApplyTheReductionOnlyOnce()
        {
            var result = Heal(Hit(isArea: true, isPeriodic: true), WithOmnivamp(10f));

            Assert.AreEqual(DesignConstants.AreaOrPeriodicOmnivampEfficiency, result.OmnivampEfficiency, 1e-6f,
                "Efficiency was compounded instead of chosen.");
            Assert.AreEqual(3.3f, result.FromOmnivamp, 1e-3f);
            Assert.AreEqual(
                Heal(Hit(true, false), WithOmnivamp(10f)).FromOmnivamp,
                result.FromOmnivamp,
                1e-4f,
                "Area+DoT healed differently from area alone.");
        }

        [Test]
        public void EfficiencyIsOneOnlyForASingleInstantHit()
        {
            Assert.AreEqual(1f, LifestealMath.OmnivampEfficiency(false, false), 1e-6f);
            Assert.AreEqual(0.33f, LifestealMath.OmnivampEfficiency(true, false), 1e-6f);
            Assert.AreEqual(0.33f, LifestealMath.OmnivampEfficiency(false, true), 1e-6f);
            Assert.AreEqual(0.33f, LifestealMath.OmnivampEfficiency(true, true), 1e-6f);
        }

        [Test]
        public void OnHitLifestealAndOmnivampAreSeparateResources()
        {
            var attacker = CombatStats.StartingVaccine();
            attacker.OmnivampPercent = 10f;
            attacker.OnHitLifestealPercent = 5f;

            var result = LifestealMath.Compute(
                Hit(false, false), HundredDamage(), attacker, LifestealPolicy.Draft(),
                shieldAbsorbed: 0f, targetHealthBeforeHit: 10_000f, isOnHitEvent: true);

            Assert.AreEqual(5f, result.FromOnHit, 1e-3f);
            Assert.AreEqual(10f, result.FromOmnivamp, 1e-3f);
            Assert.AreEqual(15f, result.Total, 1e-3f);
        }

        [Test]
        public void OnHitLifestealIsNotPaidForAnEventThatIsNotAnOnHit()
        {
            var attacker = CombatStats.StartingVaccine();
            attacker.OnHitLifestealPercent = 5f;

            var result = LifestealMath.Compute(
                Hit(false, true), HundredDamage(), attacker, LifestealPolicy.Draft(),
                shieldAbsorbed: 0f, targetHealthBeforeHit: 10_000f, isOnHitEvent: false);

            Assert.AreEqual(0f, result.FromOnHit, 1e-6f);
        }

        [Test]
        public void UnresolvedPolicy_ShieldAbsorbedDamageDoesNotHealUnderTheDraftReading()
        {
            // docs/02 section 4 leaves this open; the draft policy is the conservative one and
            // the switch exists so the decision can be applied without touching this math.
            var attacker = WithOmnivamp(10f);

            var conservative = LifestealMath.Compute(
                Hit(false, false), HundredDamage(), attacker, LifestealPolicy.Draft(),
                shieldAbsorbed: 60f, targetHealthBeforeHit: 10_000f, isOnHitEvent: false);

            var generous = LifestealPolicy.Draft();
            generous.CountShieldAbsorbedAsDealt = true;
            var withShieldCounted = LifestealMath.Compute(
                Hit(false, false), HundredDamage(), attacker, generous,
                shieldAbsorbed: 60f, targetHealthBeforeHit: 10_000f, isOnHitEvent: false);

            Assert.AreEqual(4f, conservative.FromOmnivamp, 1e-3f, "Only the 40 that reached health should heal.");
            Assert.AreEqual(10f, withShieldCounted.FromOmnivamp, 1e-3f);
        }

        [Test]
        public void UnresolvedPolicy_OverkillDoesNotHealUnderTheDraftReading()
        {
            var attacker = WithOmnivamp(10f);

            var conservative = LifestealMath.Compute(
                Hit(false, false), HundredDamage(), attacker, LifestealPolicy.Draft(),
                shieldAbsorbed: 0f, targetHealthBeforeHit: 10f, isOnHitEvent: false);

            var generous = LifestealPolicy.Draft();
            generous.CountOverkill = true;
            var withOverkill = LifestealMath.Compute(
                Hit(false, false), HundredDamage(), attacker, generous,
                shieldAbsorbed: 0f, targetHealthBeforeHit: 10f, isOnHitEvent: false);

            Assert.AreEqual(1f, conservative.FromOmnivamp, 1e-3f, "A 100 damage hit on 10 health should heal from 10.");
            Assert.AreEqual(10f, withOverkill.FromOmnivamp, 1e-3f);
        }

        [Test]
        public void ShieldAndOverkillAreNotDiscountedTwiceForTheSameDamage()
        {
            // 100 applied, 60 absorbed, 25 health left: 40 reached the shield-less path and
            // 25 of it could land, so the qualifying damage is 25, not 100 - 60 - 25.
            var attacker = WithOmnivamp(10f);

            var result = LifestealMath.Compute(
                Hit(false, false), HundredDamage(), attacker, LifestealPolicy.Draft(),
                shieldAbsorbed: 60f, targetHealthBeforeHit: 25f, isOnHitEvent: false);

            Assert.AreEqual(25f, result.QualifyingDamage, 1e-3f);
            Assert.AreEqual(2.5f, result.FromOmnivamp, 1e-3f);
        }

        [Test]
        public void NoOmnivampStatMeansNoHealing()
        {
            var result = Heal(Hit(true, true), CombatStats.StartingVaccine());

            Assert.AreEqual(0f, result.Total, 1e-6f);
        }

        [Test]
        public void HealingIsNeverNegativeOrNaN()
        {
            var attacker = WithOmnivamp(float.NaN);

            var result = LifestealMath.Compute(
                Hit(false, false), HundredDamage(), attacker, LifestealPolicy.Draft(),
                shieldAbsorbed: float.NaN, targetHealthBeforeHit: -5f, isOnHitEvent: true);

            Assert.IsFalse(float.IsNaN(result.Total));
            Assert.GreaterOrEqual(result.Total, 0f);
        }
    
        [Test]
        public void ShieldAbsorbedDamageGivesNoLifestealByApproval()
        {
            // [확정] D-004, approved 2026-11-22: 보호막이 흡수한 피해에는 흡혈이 발생하지
            // 않는다. The generous reading stays implemented and tested above; this holds that
            // it is not the one the game ships with.
            Assert.IsFalse(LifestealPolicy.Draft().CountShieldAbsorbedAsDealt);
        }
    }
}
