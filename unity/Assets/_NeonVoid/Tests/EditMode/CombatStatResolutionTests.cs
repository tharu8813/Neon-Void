using System.Collections.Generic;
using NeonVoid.Combat;
using NeonVoid.Combat.Stats;
using NeonVoid.Combat.Status;
using NeonVoid.Player;
using NUnit.Framework;
using UnityEngine;

namespace NeonVoid.Tests
{
    /// <summary>
    /// docs/02 section 2: <c>base → 승인 영구 투자 → 플러그인/버프·디버프 → 캡·검증 → 최종 전투
    /// 스냅샷</c>, with 원본 기반 재계산 and NaN / infinity / unknown ids refused.
    /// </summary>
    public sealed class CombatStatResolutionTests
    {
        private static CombatStats Base()
        {
            CombatStats stats = CombatStats.StartingVaccine();
            stats.AttackDamage = 10f;
            stats.AbilityPower = 20f;
            stats.PhysicalArmor = 30f;
            stats.MagicResistance = 40f;
            return stats;
        }

        private static StatCaps Caps() => StatCaps.Draft();

        private static StatusContainer Container() => new StatusContainer(StatusCatalog.Draft());

        private static List<StatModifier> Mods(params StatModifier[] modifiers) =>
            new List<StatModifier>(modifiers);

        // --- the pipeline ------------------------------------------------------------------

        [Test]
        public void NoContributionsLeavesTheBaseUntouched()
        {
            CombatStats resolved = CombatStatResolution.Resolve(Base(), null, Caps());

            Assert.AreEqual(10f, resolved.AttackDamage, 1e-4f);
            Assert.AreEqual(30f, resolved.PhysicalArmor, 1e-4f);
        }

        [Test]
        public void ResolvingTwiceGivesTheSameAnswer()
        {
            // 원본 기반 재계산. A resolver that added onto the previous result would be right
            // the first time and drift upward on every recomputation, and the symptom - stats
            // creeping up each time a zone is re-entered - reads as a balance problem.
            List<StatModifier> modifiers = Mods(
                StatModifier.Add(ModifiedStat.AttackDamage, 5f),
                StatModifier.Scale(ModifiedStat.PhysicalArmor, 0.5f));

            CombatStats first = CombatStatResolution.Resolve(Base(), modifiers, Caps());
            CombatStats second = CombatStatResolution.Resolve(Base(), modifiers, Caps());

            Assert.AreEqual(first.AttackDamage, second.AttackDamage, 1e-4f);
            Assert.AreEqual(first.PhysicalArmor, second.PhysicalArmor, 1e-4f);
            Assert.AreEqual(15f, second.AttackDamage, 1e-4f);
            Assert.AreEqual(45f, second.PhysicalArmor, 1e-4f);
        }

        [Test]
        public void FractionsAreOfTheBaseRatherThanTheRunningTotal()
        {
            // Two +100% contributions give 3x base, not 4x. Multiplying running totals would
            // make the result depend on the order they arrived in, which for a status container
            // means it depends on what the player walked into first.
            CombatStats resolved = CombatStatResolution.Resolve(
                Base(),
                Mods(StatModifier.Scale(ModifiedStat.AttackDamage, 1f),
                     StatModifier.Scale(ModifiedStat.AttackDamage, 1f)),
                Caps());

            Assert.AreEqual(30f, resolved.AttackDamage, 1e-4f);
        }

        [Test]
        public void TheOrderContributionsArriveInDoesNotMatter()
        {
            var forward = Mods(
                StatModifier.Add(ModifiedStat.AttackDamage, 7f),
                StatModifier.Scale(ModifiedStat.AttackDamage, 0.5f),
                StatModifier.Scale(ModifiedStat.AttackDamage, -0.2f));
            var reversed = Mods(
                StatModifier.Scale(ModifiedStat.AttackDamage, -0.2f),
                StatModifier.Scale(ModifiedStat.AttackDamage, 0.5f),
                StatModifier.Add(ModifiedStat.AttackDamage, 7f));

            Assert.AreEqual(
                CombatStatResolution.Resolve(Base(), forward, Caps()).AttackDamage,
                CombatStatResolution.Resolve(Base(), reversed, Caps()).AttackDamage,
                1e-4f);
        }

        [Test]
        public void PermanentInvestmentIsAppliedBeforeTheTemporaryContributions()
        {
            // Both are fractions of the base, so the only way to tell the stages apart is that
            // neither compounds on the other: +50% then +50% is 2x, not 2.25x.
            CombatStats resolved = CombatStatResolution.Resolve(
                Base(),
                Mods(StatModifier.Scale(ModifiedStat.AttackDamage, 0.5f)),
                Mods(StatModifier.Scale(ModifiedStat.AttackDamage, 0.5f)),
                Caps(),
                out int rejected);

            Assert.AreEqual(20f, resolved.AttackDamage, 1e-4f);
            Assert.AreEqual(0, rejected);
        }

        // --- refusal -----------------------------------------------------------------------

        [Test]
        public void UnusableContributionsAreRefusedAndCountedWhileTheRestStillApply()
        {
            // docs/02: 음수/NaN/무한대/잘못된 ID 거부. A NaN that reaches a stat poisons every
            // comparison downstream, including the ones deciding whether an actor is alive.
            CombatStats resolved = CombatStatResolution.Resolve(
                Base(),
                null,
                Mods(StatModifier.Add(ModifiedStat.AttackDamage, float.NaN),
                     StatModifier.Add(ModifiedStat.AttackDamage, float.PositiveInfinity),
                     StatModifier.Scale(ModifiedStat.AttackDamage, float.NaN),
                     StatModifier.Add((ModifiedStat)999, 5f),
                     StatModifier.Add(ModifiedStat.AttackDamage, 3f)),
                Caps(),
                out int rejected);

            Assert.AreEqual(4, rejected);
            Assert.AreEqual(13f, resolved.AttackDamage, 1e-4f);
            Assert.IsFalse(float.IsNaN(resolved.AttackDamage));
        }

        // --- caps --------------------------------------------------------------------------

        [Test]
        public void PercentageStatsAreHeldInsideTheirCaps()
        {
            StatCaps caps = Caps();
            CombatStats resolved = CombatStatResolution.Resolve(
                Base(),
                Mods(StatModifier.Add(ModifiedStat.CritChancePercent, 500f),
                     StatModifier.Add(ModifiedStat.PhysicalPenPercent, 300f),
                     StatModifier.Add(ModifiedStat.TenacityPercent, 500f),
                     StatModifier.Add(ModifiedStat.SkillHaste, 99999f)),
                caps);

            Assert.AreEqual(caps.MaxCritChancePercent, resolved.CritChancePercent, 1e-4f);
            Assert.AreEqual(caps.MaxPenetrationPercent, resolved.PhysicalPenPercent, 1e-4f);
            Assert.AreEqual(caps.MaxTenacityPercent, resolved.TenacityPercent, 1e-4f);
            Assert.AreEqual(caps.MaxSkillHaste, resolved.SkillHaste, 1e-4f);
        }

        [Test]
        public void AttackDamageCannotGoNegative()
        {
            // A negative attack damage would heal whatever it hit.
            CombatStats resolved = CombatStatResolution.Resolve(
                Base(), Mods(StatModifier.Add(ModifiedStat.AttackDamage, -1000f)), Caps());

            Assert.AreEqual(0f, resolved.AttackDamage, 1e-4f);
        }

        [Test]
        public void ResistanceIsAllowedBelowZeroBecauseThatIsWhatArmourReductionDoes()
        {
            // DamageMath has a branch for negative resistance; clamping it here would silently
            // cap every 방어 감소 effect at the target's base armour.
            CombatStats resolved = CombatStatResolution.Resolve(
                Base(), Mods(StatModifier.Scale(ModifiedStat.PhysicalArmor, -2f)), Caps());

            Assert.Less(resolved.PhysicalArmor, 0f);
            Assert.AreEqual(-30f, resolved.PhysicalArmor, 1e-4f);
        }

        [Test]
        public void ACapOfZeroIsRefusedBecauseItWouldDisableTheStat()
        {
            StatCaps caps = Caps();
            caps.MaxCritChancePercent = 0f;

            Assert.Throws<System.InvalidOperationException>(() => caps.Validate());
        }

        // --- statuses finally meaning something --------------------------------------------

        [Test]
        public void EmpowerRaisesAttackDamageAndWeakenLowersIt()
        {
            // Until the contribution mapping existed, applying 강화 tracked a buff with a stack
            // count that nothing read, so the attack damage never changed at all.
            StatusContainer empowered = Container();
            empowered.Apply(StatusEffectId.Empower, 0.25f);

            var modifiers = new List<StatModifier>();
            StatusStatContribution.Collect(empowered, modifiers);
            CombatStats resolved = CombatStatResolution.Resolve(Base(), modifiers, Caps());

            Assert.AreEqual(12.5f, resolved.AttackDamage, 1e-3f);

            StatusContainer weakened = Container();
            weakened.Apply(StatusEffectId.Weaken, 0.25f);
            modifiers.Clear();
            StatusStatContribution.Collect(weakened, modifiers);

            Assert.AreEqual(7.5f, CombatStatResolution.Resolve(Base(), modifiers, Caps()).AttackDamage, 1e-3f);
        }

        [Test]
        public void TheCorrosionMarkReducesBothResistances()
        {
            // docs/03 G12 is 물리·마법 방어 감소 - both.
            StatusContainer status = Container();
            status.Apply(StatusEffectId.FirewallBreach, 3f);
            status.Apply(StatusEffectId.FirewallBreach, 3f);

            var modifiers = new List<StatModifier>();
            StatusStatContribution.Collect(status, modifiers);
            CombatStats resolved = CombatStatResolution.Resolve(Base(), modifiers, Caps());

            Assert.Less(resolved.PhysicalArmor, 30f, "Physical armour was not reduced.");
            Assert.Less(resolved.MagicResistance, 40f, "Magic resistance was not reduced.");
        }

        [Test]
        public void TheCorrosionMarkStillBitesATargetWithNoArmourAtAll()
        {
            // The bug this is here to stop coming back. The reduction was first written as a
            // fraction of base resistance, which does nothing whatsoever to a target whose
            // base is zero - and every enemy in the prototype has zero. The mark applied,
            // stacked and displayed while reducing exactly nothing, with no error anywhere,
            // and the first version of this test missed it by using a base with 30 armour.
            // Test data has to be the data the game has.
            CombatStats unarmoured = CombatStats.StartingVaccine();
            Assert.AreEqual(0f, unarmoured.PhysicalArmor, "The starting block is unarmoured.");

            StatusContainer status = Container();
            status.Apply(StatusEffectId.FirewallBreach, 3f);

            var modifiers = new List<StatModifier>();
            StatusStatContribution.Collect(status, modifiers);
            CombatStats resolved = CombatStatResolution.Resolve(unarmoured, modifiers, Caps());

            Assert.Less(resolved.PhysicalArmor, 0f, "A mark on an unarmoured target did nothing.");
            Assert.Less(resolved.MagicResistance, 0f, "A mark on an unarmoured target did nothing.");
        }

        [Test]
        public void SkillHasteFromAStatusReachesTheStatBlock()
        {
            StatusContainer status = Container();
            status.Apply(StatusEffectId.SkillHaste, 20f);

            var modifiers = new List<StatModifier>();
            StatusStatContribution.Collect(status, modifiers);

            Assert.AreEqual(20f, CombatStatResolution.Resolve(Base(), modifiers, Caps()).SkillHaste, 1e-3f);
        }

        [Test]
        public void ControlEffectsContributeNoStatChanges()
        {
            // 정지, 속박 and 제한 deny actions; that is the skill gate's job. A stat
            // contribution for them would apply the same rule in two places.
            StatusContainer status = Container();
            status.Apply(StatusEffectId.Stun, 1f);
            status.Apply(StatusEffectId.Root, 1f);
            status.Apply(StatusEffectId.Silence, 1f);

            var modifiers = new List<StatModifier>();
            StatusStatContribution.Collect(status, modifiers);

            Assert.IsEmpty(modifiers);
        }

        // --- movement, for both sides ------------------------------------------------------

        [Test]
        public void NothingAppliedMeansFullSpeed()
        {
            Assert.AreEqual(1f, StatusStatContribution.MoveSpeedMultiplier(Container()), 1e-4f);
            Assert.AreEqual(1f, StatusStatContribution.MoveSpeedMultiplier(null), 1e-4f);
        }

        [Test]
        public void ASlowReducesSpeedAndStunOrRootStopsItCompletely()
        {
            StatusContainer slowed = Container();
            slowed.Apply(StatusEffectId.Slow, 0.3f);
            Assert.AreEqual(0.7f, StatusStatContribution.MoveSpeedMultiplier(slowed), 1e-3f);

            StatusContainer stunned = Container();
            stunned.Apply(StatusEffectId.Stun, 1f);
            Assert.AreEqual(0f, StatusStatContribution.MoveSpeedMultiplier(stunned), 1e-4f);

            StatusContainer rooted = Container();
            rooted.Apply(StatusEffectId.Root, 1f);
            Assert.AreEqual(0f, StatusStatContribution.MoveSpeedMultiplier(rooted), 1e-4f);
        }

        [Test]
        public void AnOverwhelmingSlowStopsMovementRatherThanReversingIt()
        {
            StatusContainer status = Container();
            status.Apply(StatusEffectId.Slow, 1.5f);

            Assert.AreEqual(0f, StatusStatContribution.MoveSpeedMultiplier(status), 1e-4f);
        }

        [Test]
        public void HasteRaisesTheMultiplierAboveOne()
        {
            StatusContainer status = Container();
            status.Apply(StatusEffectId.Haste, 0.4f);

            Assert.AreEqual(1.4f, StatusStatContribution.MoveSpeedMultiplier(status), 1e-3f);
        }

        // --- the player obeys it too -------------------------------------------------------

        [Test]
        public void AHalvedMultiplierHalvesThePlayersDisplacement()
        {
            PlayerMotionConfig config = PlayerMotionConfig.Draft();
            PlayerMotionState state = PlayerMotionState.Spawn(Vector2.zero, Vector2.up);
            var command = new PlayerInputCommand(new Vector2(1f, 0f), new Vector2(0f, 5f));

            PlayerMotionSimulation.Step(state, command, config, out Vector2 full, 1f);
            PlayerMotionSimulation.Step(state, command, config, out Vector2 halved, 0.5f);

            Assert.AreEqual(full.magnitude * 0.5f, halved.magnitude, 1e-4f);
        }

        [Test]
        public void AStoppedPlayerDoesNotMoveAndCannotSpendADash()
        {
            // D-009: 정지 중 대시 미발동. Without the gate a stunned player would start a dash,
            // travel nowhere, and pay the cooldown for it.
            PlayerMotionConfig config = PlayerMotionConfig.Draft();
            PlayerMotionState state = PlayerMotionState.Spawn(Vector2.zero, Vector2.up);
            var command = new PlayerInputCommand(
                new Vector2(1f, 0f), new Vector2(0f, 5f), dashPressed: true);

            PlayerMotionState next = PlayerMotionSimulation.Step(
                state, command, config, out Vector2 displacement, 0f);

            Assert.AreEqual(0f, displacement.magnitude, 1e-4f);
            Assert.IsFalse(next.IsDashing, "A stunned player started a dash.");
            Assert.AreEqual(0, next.DashCooldownTicksRemaining, "A refused dash still cost its cooldown.");
        }

        [Test]
        public void ADashAlreadyInFlightIsStoppedByARoot()
        {
            PlayerMotionConfig config = PlayerMotionConfig.Draft();
            PlayerMotionState state = PlayerMotionState.Spawn(Vector2.zero, Vector2.up);
            var dash = new PlayerInputCommand(
                new Vector2(1f, 0f), new Vector2(0f, 5f), dashPressed: true);

            PlayerMotionState dashing = PlayerMotionSimulation.Step(state, dash, config, out _, 1f);
            Assert.IsTrue(dashing.IsDashing);

            var idle = new PlayerInputCommand(Vector2.zero, new Vector2(0f, 5f));
            PlayerMotionSimulation.Step(dashing, idle, config, out Vector2 displacement, 0f);

            Assert.AreEqual(0f, displacement.magnitude, 1e-4f,
                "A root let the dash carry the player out of the effect holding them.");
        }

        [Test]
        public void ANonFiniteMultiplierStopsMovementRatherThanPoisoningThePosition()
        {
            PlayerMotionConfig config = PlayerMotionConfig.Draft();
            PlayerMotionState state = PlayerMotionState.Spawn(Vector2.zero, Vector2.up);
            var command = new PlayerInputCommand(new Vector2(1f, 0f), new Vector2(0f, 5f));

            PlayerMotionState next = PlayerMotionSimulation.Step(
                state, command, config, out Vector2 displacement, float.NaN);

            Assert.AreEqual(0f, displacement.magnitude, 1e-4f);
            Assert.IsFalse(float.IsNaN(next.PositionXZ.x) || float.IsNaN(next.PositionXZ.y));
        }
    }
}
