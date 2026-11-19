using System;
using NeonVoid.Combat;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// docs/02 section 9 item 3: positive and negative resistance, penetration, mixed AD/AP
    /// scaling, and the 120 percent critical multiplier. The named vectors are the ones written
    /// into docs/02 section 3.
    /// </summary>
    public sealed class DamageMathTests
    {
        [Test]
        public void DocumentedVector_BasicAttackOfFiveAdDealsFiveBeforeDefence()
        {
            var attacker = CombatStats.StartingVaccine();   // AttackDamage 5
            var defender = CombatStats.StartingVaccine();   // armor 0

            var result = DamageMath.Resolve(DamageRequest.BasicAttack(), attacker, defender, critRolled: false);

            Assert.AreEqual(5f, result.Raw, 1e-4f);
            Assert.AreEqual(5f, result.AppliedBeforeShield, 1e-4f);
            Assert.AreEqual(1f, result.MitigationMultiplier, 1e-4f);
        }

        [Test]
        public void DocumentedVector_TwoAttacksPerSecondOfThatAttackIsTenTheoreticalDps()
        {
            var attacker = CombatStats.StartingVaccine();
            var defender = CombatStats.StartingVaccine();
            const float attacksPerSecond = 2f;   // [초안] docs/02 section 2

            float perHit = DamageMath.Resolve(DamageRequest.BasicAttack(), attacker, defender, false).AppliedBeforeShield;

            Assert.AreEqual(10f, perHit * attacksPerSecond, 1e-4f);
        }

        [Test]
        public void DocumentedVector_ArmorOfOneHundredHalvesPhysicalDamage()
        {
            var attacker = CombatStats.StartingVaccine();
            var defender = CombatStats.StartingVaccine();
            defender.PhysicalArmor = 100f;

            var result = DamageMath.Resolve(DamageRequest.BasicAttack(), attacker, defender, false);

            Assert.AreEqual(0.5f, result.MitigationMultiplier, 1e-4f);
            Assert.AreEqual(2.5f, result.AppliedBeforeShield, 1e-4f);
            Assert.AreEqual(50f, result.ReductionPercent, 1e-3f);
        }

        [Test]
        public void DocumentedVector_ArmorOfMinusOneHundredAmplifiesByFiftyPercent()
        {
            var attacker = CombatStats.StartingVaccine();
            var defender = CombatStats.StartingVaccine();
            defender.PhysicalArmor = -100f;

            var result = DamageMath.Resolve(DamageRequest.BasicAttack(), attacker, defender, false);

            Assert.AreEqual(1.5f, result.MitigationMultiplier, 1e-4f);
            Assert.AreEqual(7.5f, result.AppliedBeforeShield, 1e-4f);
        }

        [Test]
        public void DocumentedVector_CriticalHitMultipliesByOneHundredTwentyPercent()
        {
            var attacker = CombatStats.StartingVaccine();
            var defender = CombatStats.StartingVaccine();

            float normal = DamageMath.Resolve(DamageRequest.BasicAttack(), attacker, defender, false).AppliedBeforeShield;
            var crit = DamageMath.Resolve(DamageRequest.BasicAttack(), attacker, defender, critRolled: true);

            Assert.AreEqual(1.20f, attacker.CritDamageMultiplier, 1e-6f);
            Assert.AreEqual(normal * 1.20f, crit.AppliedBeforeShield, 1e-4f);
            Assert.IsTrue(crit.IsCritical);
        }

        [Test]
        public void AnAttackThatCannotCritIgnoresAWinningCritRoll()
        {
            var attacker = CombatStats.StartingVaccine();
            var defender = CombatStats.StartingVaccine();
            var request = DamageRequest.BasicAttack();
            request.CanCrit = false;

            var result = DamageMath.Resolve(request, attacker, defender, critRolled: true);

            Assert.IsFalse(result.IsCritical);
            Assert.AreEqual(5f, result.AppliedBeforeShield, 1e-4f);
        }

        [Test]
        public void NegativeResistanceAmplificationSaturatesInsteadOfGrowingWithoutBound()
        {
            // The curve approaches 2x. A shredded target must not become a damage multiplier.
            Assert.AreEqual(1.5f, DamageMath.MitigationMultiplier(-100f), 1e-4f);
            Assert.Less(DamageMath.MitigationMultiplier(-1_000_000f), 2f);
            Assert.Greater(DamageMath.MitigationMultiplier(-1_000_000f), 1.99f);
        }

        [Test]
        public void TrueDamageIgnoresResistanceAndPenetrationEntirely()
        {
            var attacker = CombatStats.StartingVaccine();
            attacker.PhysicalPenFlat = 999f;
            attacker.MagicPenPercent = 1f;

            var defender = CombatStats.StartingVaccine();
            defender.PhysicalArmor = 500f;
            defender.MagicResistance = 500f;

            var request = DamageRequest.BasicAttack();
            request.Type = DamageType.True;

            var result = DamageMath.Resolve(request, attacker, defender, false);

            Assert.AreEqual(1f, result.MitigationMultiplier, 1e-6f);
            Assert.AreEqual(5f, result.AppliedBeforeShield, 1e-4f);
        }

        [Test]
        public void PercentPenetrationIsAppliedBeforeFlatPenetration()
        {
            // 100 armor, 50 percent then 20 flat -> 30. The other order would give 40.
            float afterPen = DamageMath.ResistanceAfterPenetration(100f, percentPen: 0.5f, flatPen: 20f);

            Assert.AreEqual(30f, afterPen, 1e-4f);
        }

        [Test]
        public void PenetrationCannotDrivePositiveResistanceBelowZeroAndBecomeBonusDamage()
        {
            float afterPen = DamageMath.ResistanceAfterPenetration(30f, percentPen: 0f, flatPen: 500f);

            Assert.AreEqual(0f, afterPen, 1e-6f);
            Assert.AreEqual(1f, DamageMath.MitigationMultiplier(afterPen), 1e-6f,
                "Over-penetration turned into an amplifier the design never granted.");
        }

        [Test]
        public void PenetrationLeavesAnAlreadyNegativeResistanceAlone()
        {
            Assert.AreEqual(-50f, DamageMath.ResistanceAfterPenetration(-50f, 0.5f, 20f), 1e-6f);
        }

        [Test]
        public void MagicDamageReadsMagicResistanceAndNotArmor()
        {
            var attacker = CombatStats.StartingVaccine();
            attacker.AbilityPower = 100f;

            var defender = CombatStats.StartingVaccine();
            defender.PhysicalArmor = 100f;   // must be ignored
            defender.MagicResistance = 0f;

            var request = new DamageRequest { ApRatio = 1f, Type = DamageType.Magic };
            var result = DamageMath.Resolve(request, attacker, defender, false);

            Assert.AreEqual(100f, result.AppliedBeforeShield, 1e-3f);
        }

        [Test]
        public void MixedAdAndApScalingAddsBothContributions()
        {
            var attacker = CombatStats.StartingVaccine();
            attacker.AttackDamage = 50f;
            attacker.AbilityPower = 40f;
            var defender = CombatStats.StartingVaccine();

            var request = new DamageRequest
            {
                Flat = 10f,
                AdRatio = 0.6f,
                ApRatio = 0.5f,
                Type = DamageType.Physical,
            };

            // 10 + 50*0.6 + 40*0.5 = 60
            Assert.AreEqual(60f, DamageMath.Resolve(request, attacker, defender, false).Raw, 1e-3f);
        }

        [Test]
        public void DamageNeverGoesNegativeEvenWithAbsurdInputs()
        {
            var attacker = CombatStats.StartingVaccine();
            attacker.AttackDamage = 0f;
            var defender = CombatStats.StartingVaccine();

            var request = new DamageRequest { Flat = -100f, Type = DamageType.Physical };

            Assert.AreEqual(0f, DamageMath.Resolve(request, attacker, defender, false).AppliedBeforeShield, 1e-6f);
        }

        [Test]
        public void StatValidationRejectsNaNAndAnImpossibleCritMultiplier()
        {
            var nan = CombatStats.StartingVaccine();
            nan.AttackDamage = float.NaN;
            Assert.Throws<InvalidOperationException>(() => nan.Validate());

            var weakCrit = CombatStats.StartingVaccine();
            weakCrit.CritDamageMultiplier = 0.8f;
            Assert.Throws<InvalidOperationException>(() => weakCrit.Validate());

            var percentAsHundred = CombatStats.StartingVaccine();
            percentAsHundred.PhysicalPenPercent = 100f;   // a fraction was expected
            Assert.Throws<InvalidOperationException>(() => percentAsHundred.Validate());
        }

        [Test]
        public void NegativeResistancesAreAllowedBecauseShredIsADesignedEffect()
        {
            var stats = CombatStats.StartingVaccine();
            stats.PhysicalArmor = -75f;
            stats.MagicResistance = -10f;

            Assert.DoesNotThrow(() => stats.Validate());
        }
    }
}
