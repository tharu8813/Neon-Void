using NeonVoid.Combat;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// The join between the P1 rules: resolve, nullify, remove health, then heal from what
    /// actually landed. Ordering is the whole point, so it is what these tests pin down.
    /// </summary>
    public sealed class DamageApplicationTests
    {
        private static CombatStats Attacker(float omnivamp = 0f, float attackDamage = 100f)
        {
            var stats = CombatStats.StartingVaccine();
            stats.AttackDamage = attackDamage;
            stats.OmnivampPercent = omnivamp;
            return stats;
        }

        private static DamageRequest Hit() => DamageRequest.BasicAttack();

        [Test]
        public void HealthPoolReportsWhatWasActuallyRemovedNotWhatWasAsked()
        {
            var health = HealthPool.Full(10f);

            Assert.AreEqual(10f, health.Remove(1000f), 1e-4f);
            Assert.AreEqual(0f, health.Current, 1e-6f);
            Assert.IsFalse(health.IsAlive);
            Assert.AreEqual(0f, health.Remove(50f), 1e-6f, "A dead target kept losing health.");
        }

        [Test]
        public void HealingIsCappedAtMaximumAndReportsWhatItRestored()
        {
            var health = HealthPool.Full(100f);
            health.Remove(30f);

            Assert.AreEqual(30f, health.Heal(500f), 1e-4f);
            Assert.AreEqual(100f, health.Current, 1e-4f);
        }

        [Test]
        public void ALandedHitRemovesHealthAndReportsTheResolution()
        {
            var health = HealthPool.Full(100f);
            var defender = CombatStats.StartingVaccine();

            DamageOutcome outcome = DamageApplication.Apply(
                Hit(), Attacker(), defender, ref health, false,
                LifestealPolicy.Draft(), critRolled: false, isOnHitEvent: true);

            Assert.AreEqual(100f, outcome.Resolution.AppliedBeforeShield, 1e-3f);
            Assert.AreEqual(100f, outcome.HealthRemoved, 1e-3f);
            Assert.IsFalse(health.IsAlive);
            Assert.IsTrue(outcome.Killed);
        }

        /// <summary>
        /// The ordering bug this type exists to prevent. Computing lifesteal from the damage
        /// the attack asked for would heal the attacker for overkill on every killing blow.
        /// </summary>
        [Test]
        public void LifestealComesFromHealthActuallyRemovedNotFromTheDamageRequested()
        {
            var health = HealthPool.Full(10f);     // far less than the 100 damage incoming
            var defender = CombatStats.StartingVaccine();

            DamageOutcome outcome = DamageApplication.Apply(
                Hit(), Attacker(omnivamp: 10f), defender, ref health, false,
                LifestealPolicy.Draft(), critRolled: false, isOnHitEvent: false);

            Assert.AreEqual(10f, outcome.HealthRemoved, 1e-3f);
            Assert.AreEqual(1f, outcome.Lifesteal.FromOmnivamp, 1e-3f,
                "The attacker healed for overkill damage the target never took.");
        }

        [Test]
        public void AnInvulnerableTargetTakesNothingAndPaysNoLifesteal()
        {
            // [확정] docs/02 section 5: invulnerability nullifies damage. Free sustain from
            // hitting something that took nothing would be the obvious bug here.
            var health = HealthPool.Full(100f);
            var defender = CombatStats.StartingVaccine();

            DamageOutcome outcome = DamageApplication.Apply(
                Hit(), Attacker(omnivamp: 50f), defender, ref health, defenderIsInvulnerable: true,
                LifestealPolicy.Draft(), critRolled: false, isOnHitEvent: true);

            Assert.IsTrue(outcome.Nullified);
            Assert.AreEqual(0f, outcome.HealthRemoved, 1e-6f);
            Assert.AreEqual(100f, health.Current, 1e-6f);
            Assert.AreEqual(0f, outcome.Lifesteal.Total, 1e-6f);
            Assert.IsFalse(outcome.Killed);
        }

        [Test]
        public void ResistanceStillAppliesWhenDamageIsActuallyApplied()
        {
            var health = HealthPool.Full(1000f);
            var defender = CombatStats.StartingVaccine();
            defender.PhysicalArmor = 100f;      // halves damage

            DamageOutcome outcome = DamageApplication.Apply(
                Hit(), Attacker(), defender, ref health, false,
                LifestealPolicy.Draft(), false, true);

            Assert.AreEqual(50f, outcome.HealthRemoved, 1e-3f);
            Assert.AreEqual(950f, health.Current, 1e-3f);
        }

        [Test]
        public void KillIsReportedOnlyOnTheBlowThatCrossesZero()
        {
            var health = HealthPool.Full(150f);
            var defender = CombatStats.StartingVaccine();

            DamageOutcome first = DamageApplication.Apply(
                Hit(), Attacker(), defender, ref health, false, LifestealPolicy.Draft(), false, true);
            Assert.IsFalse(first.Killed);

            DamageOutcome second = DamageApplication.Apply(
                Hit(), Attacker(), defender, ref health, false, LifestealPolicy.Draft(), false, true);
            Assert.IsTrue(second.Killed);

            DamageOutcome third = DamageApplication.Apply(
                Hit(), Attacker(), defender, ref health, false, LifestealPolicy.Draft(), false, true);
            Assert.IsFalse(third.Killed, "A corpse was killed a second time.");
        }

        [Test]
        public void CriticalDamageFlowsThroughToHealthRemoved()
        {
            var health = HealthPool.Full(1000f);
            var defender = CombatStats.StartingVaccine();

            DamageOutcome normal = DamageApplication.Apply(
                Hit(), Attacker(), defender, ref health, false, LifestealPolicy.Draft(), critRolled: false, isOnHitEvent: true);

            var health2 = HealthPool.Full(1000f);
            DamageOutcome crit = DamageApplication.Apply(
                Hit(), Attacker(), defender, ref health2, false, LifestealPolicy.Draft(), critRolled: true, isOnHitEvent: true);

            Assert.IsTrue(crit.Resolution.IsCritical);
            Assert.AreEqual(normal.HealthRemoved * 1.20f, crit.HealthRemoved, 1e-3f);
        }

        [Test]
        public void AreaDamageOverTimeStillHealsAtOneThirdThroughTheFullPipeline()
        {
            var health = HealthPool.Full(1000f);
            var defender = CombatStats.StartingVaccine();
            var request = Hit();
            request.IsArea = true;
            request.IsPeriodic = true;

            DamageOutcome outcome = DamageApplication.Apply(
                request, Attacker(omnivamp: 10f), defender, ref health, false,
                LifestealPolicy.Draft(), false, false);

            Assert.AreEqual(100f, outcome.HealthRemoved, 1e-3f);
            Assert.AreEqual(3.3f, outcome.Lifesteal.FromOmnivamp, 1e-2f);
        }
    }
}
