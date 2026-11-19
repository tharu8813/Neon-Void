using System;
using NeonVoid.Combat;
using NeonVoid.Combat.Skills;
using NeonVoid.Combat.Status;
using NeonVoid.Core;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// Closes the remaining half of docs/02 section 9 item 5: mana shortage and cooldown
    /// rejection, together with the control effects from section 5 that deny a cast.
    /// </summary>
    public sealed class SkillSystemTests
    {
        private const int BoltSkill = 101;
        private const int BlinkSkill = 102;

        private static SkillDefinition Bolt(float manaCost = 20f, int cooldownTicks = 120) => new SkillDefinition
        {
            SkillId = BoltSkill,
            ManaCost = manaCost,
            BaseCooldownTicks = cooldownTicks,
            IsMovementSkill = false,
            Damage = new DamageRequest { ApRatio = 1f, Type = DamageType.Magic },
        };

        private static SkillDefinition Blink(float manaCost = 10f, int cooldownTicks = 300) => new SkillDefinition
        {
            SkillId = BlinkSkill,
            ManaCost = manaCost,
            BaseCooldownTicks = cooldownTicks,
            IsMovementSkill = true,
            Damage = default,
        };

        private static SkillLoadout LoadoutWithBolt()
        {
            var loadout = new SkillLoadout();
            loadout.Equip(0, Bolt());
            return loadout;
        }

        // --- slots ---------------------------------------------------------------------------

        [Test]
        public void ThereAreExactlyThreeActiveSlots()
        {
            Assert.AreEqual(3, DesignConstants.ActiveSkillSlots);
            Assert.AreEqual(DesignConstants.ActiveSkillSlots, new SkillLoadout().SlotCount);
        }

        [Test]
        public void EquippingOutsideTheThreeSlotsIsRejected()
        {
            var loadout = new SkillLoadout();

            Assert.Throws<ArgumentOutOfRangeException>(() => loadout.Equip(3, Bolt()));
            Assert.Throws<ArgumentOutOfRangeException>(() => loadout.Equip(-1, Bolt()));
        }

        [Test]
        public void CastingAnEmptyOrNonExistentSlotIsRefusedDistinctly()
        {
            var loadout = new SkillLoadout();
            var mana = ManaPool.Starting();
            var stats = CombatStats.StartingVaccine();

            Assert.AreEqual(SkillCastRejection.EmptySlot,
                loadout.TryCast(0, ref mana, stats, SkillGate.Open).Rejection);
            Assert.AreEqual(SkillCastRejection.NoSuchSlot,
                loadout.TryCast(7, ref mana, stats, SkillGate.Open).Rejection);
        }

        // --- mana -----------------------------------------------------------------------------

        [Test]
        public void ACastSpendsItsManaCost()
        {
            var loadout = LoadoutWithBolt();
            var mana = ManaPool.Starting();   // 100
            var stats = CombatStats.StartingVaccine();

            SkillCastResult result = loadout.TryCast(0, ref mana, stats, SkillGate.Open);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(20f, result.ManaSpent, 1e-4f);
            Assert.AreEqual(80f, mana.Current, 1e-4f);
        }

        [Test]
        public void NotEnoughManaIsRefusedAndSpendsNothing()
        {
            var loadout = LoadoutWithBolt();
            var mana = ManaPool.Starting();
            mana.Current = 5f;
            var stats = CombatStats.StartingVaccine();

            SkillCastResult result = loadout.TryCast(0, ref mana, stats, SkillGate.Open);

            Assert.AreEqual(SkillCastRejection.NotEnoughMana, result.Rejection);
            Assert.AreEqual(5f, mana.Current, 1e-4f, "A refused cast drained mana anyway.");
            Assert.AreEqual(0, loadout.CooldownRemaining(0), "A refused cast started a cooldown.");
        }

        [Test]
        public void ManaRegeneratesPerTickNotInOneSecondJumps()
        {
            var mana = ManaPool.Starting();
            mana.Current = 0f;            // regen 5 per second

            mana.Tick();

            Assert.Greater(mana.Current, 0f, "Nothing accrued on a tick inside the first second.");
            Assert.AreEqual(5f / 60f, mana.Current, 1e-5f);
        }

        [Test]
        public void ManaRegenerationReachesTheRateOverOneSecondAndStopsAtMax()
        {
            var mana = ManaPool.Starting();
            mana.Current = 0f;

            for (int tick = 0; tick < DesignConstants.SimulationTicksPerSecond; ++tick)
                mana.Tick();

            Assert.AreEqual(5f, mana.Current, 1e-3f);

            for (int tick = 0; tick < 60 * 60; ++tick)
                mana.Tick();

            Assert.AreEqual(mana.Max, mana.Current, 1e-3f);
            Assert.IsTrue(mana.IsFull);
        }

        [Test]
        public void ManaCannotBeSpentIntoTheNegativeOrByNonsenseCosts()
        {
            var mana = ManaPool.Starting();
            mana.Current = 10f;

            Assert.IsFalse(mana.TrySpend(11f));
            Assert.IsFalse(mana.TrySpend(float.NaN));
            Assert.IsFalse(mana.TrySpend(-5f));
            Assert.AreEqual(10f, mana.Current, 1e-4f);
        }

        // --- cooldown ---------------------------------------------------------------------------

        [Test]
        public void ASecondCastDuringCooldownIsRefusedThenAllowedWhenItEnds()
        {
            var loadout = LoadoutWithBolt();
            var mana = ManaPool.Starting();
            mana.Max = 1000f;
            mana.Current = 1000f;
            var stats = CombatStats.StartingVaccine();

            SkillCastResult first = loadout.TryCast(0, ref mana, stats, SkillGate.Open);
            Assert.IsTrue(first.Succeeded);
            Assert.AreEqual(120, first.CooldownTicks);

            for (int tick = 0; tick < 119; ++tick)
            {
                Assert.AreEqual(SkillCastRejection.OnCooldown,
                    loadout.TryCast(0, ref mana, stats, SkillGate.Open).Rejection,
                    "A second cast slipped through on tick " + tick);
                loadout.Tick();
            }

            loadout.Tick();
            Assert.IsTrue(loadout.TryCast(0, ref mana, stats, SkillGate.Open).Succeeded,
                "The skill never came off cooldown.");
        }

        [Test]
        public void ARefusedCooldownCastDoesNotSpendMana()
        {
            var loadout = LoadoutWithBolt();
            var mana = ManaPool.Starting();
            var stats = CombatStats.StartingVaccine();

            loadout.TryCast(0, ref mana, stats, SkillGate.Open);
            float afterFirst = mana.Current;

            loadout.TryCast(0, ref mana, stats, SkillGate.Open);

            Assert.AreEqual(afterFirst, mana.Current, 1e-4f);
        }

        [Test]
        public void SkillHasteFollowsTheDocumentedFormulaAndNeverReachesZero()
        {
            // base / (1 + H/100): 100 haste halves, 200 thirds.
            Assert.AreEqual(120, CooldownMath.EffectiveCooldownTicks(120, 0f));
            Assert.AreEqual(60, CooldownMath.EffectiveCooldownTicks(120, 100f));
            Assert.AreEqual(40, CooldownMath.EffectiveCooldownTicks(120, 200f));

            Assert.AreEqual(CooldownMath.MinimumCooldownTicks,
                CooldownMath.EffectiveCooldownTicks(120, 1_000_000f));
            Assert.Greater(CooldownMath.EffectiveCooldownTicks(120, 1_000_000f), 0,
                "docs/02 section 3 requires a cooldown floor above zero.");
        }

        [Test]
        public void NegativeHasteDoesNotLengthenACooldown()
        {
            Assert.AreEqual(120, CooldownMath.EffectiveCooldownTicks(120, -500f));
            Assert.AreEqual(120, CooldownMath.EffectiveCooldownTicks(120, float.NaN));
        }

        [Test]
        public void HasteShortensTheCooldownAnActualCastEnters()
        {
            var loadout = LoadoutWithBolt();
            var mana = ManaPool.Starting();
            var stats = CombatStats.StartingVaccine();
            stats.SkillHaste = 100f;

            SkillCastResult result = loadout.TryCast(0, ref mana, stats, SkillGate.Open);

            Assert.AreEqual(60, result.CooldownTicks);
            Assert.AreEqual(60, loadout.CooldownRemaining(0));
        }

        [Test]
        public void ASkillWithoutACooldownIsRejectedAtEquipTime()
        {
            var loadout = new SkillLoadout();
            SkillDefinition broken = Bolt(cooldownTicks: 0);

            Assert.Throws<InvalidOperationException>(() => loadout.Equip(0, broken));
        }

        // --- control effects ---------------------------------------------------------------------

        [Test]
        public void StunBlocksEverySkill()
        {
            var loadout = new SkillLoadout();
            loadout.Equip(0, Bolt());
            loadout.Equip(1, Blink());
            var mana = ManaPool.Starting();
            var stats = CombatStats.StartingVaccine();
            var gate = new SkillGate(stunned: true, silenced: false, rooted: false);

            Assert.AreEqual(SkillCastRejection.Stunned, loadout.TryCast(0, ref mana, stats, gate).Rejection);
            Assert.AreEqual(SkillCastRejection.Stunned, loadout.TryCast(1, ref mana, stats, gate).Rejection);
        }

        [Test]
        public void SilenceBlocksActiveSkills()
        {
            var loadout = LoadoutWithBolt();
            var mana = ManaPool.Starting();
            var stats = CombatStats.StartingVaccine();
            var gate = new SkillGate(false, silenced: true, rooted: false);

            Assert.AreEqual(SkillCastRejection.Silenced, loadout.TryCast(0, ref mana, stats, gate).Rejection);
        }

        /// <summary>
        /// The distinction that would be lost by flattening the three denials into one: a
        /// rooted player can still cast a stationary skill.
        /// </summary>
        [Test]
        public void RootBlocksOnlyMovementSkills()
        {
            var loadout = new SkillLoadout();
            loadout.Equip(0, Bolt());      // stationary
            loadout.Equip(1, Blink());     // movement
            var mana = ManaPool.Starting();
            var stats = CombatStats.StartingVaccine();
            var gate = new SkillGate(false, false, rooted: true);

            Assert.IsTrue(loadout.TryCast(0, ref mana, stats, gate).Succeeded,
                "Root wrongly blocked a stationary skill.");
            Assert.AreEqual(SkillCastRejection.Rooted, loadout.TryCast(1, ref mana, stats, gate).Rejection);
        }

        [Test]
        public void TheGateIsReadFromTheActorsRunningStatusEffects()
        {
            var status = new StatusContainer(StatusCatalog.Draft());
            Assert.IsFalse(SkillGate.FromStatus(status).Silenced);

            status.Apply(StatusEffectId.Silence, 1f);
            Assert.IsTrue(SkillGate.FromStatus(status).Silenced);

            status.Remove(StatusEffectId.Silence);
            status.Apply(StatusEffectId.Root, 1f);

            SkillGate gate = SkillGate.FromStatus(status);
            Assert.IsFalse(gate.Silenced);
            Assert.IsTrue(gate.Rooted);
            Assert.IsFalse(gate.Stunned);
        }

        [Test]
        public void ADeniedCastDoesNotSpendManaOrStartACooldown()
        {
            var loadout = LoadoutWithBolt();
            var mana = ManaPool.Starting();
            var stats = CombatStats.StartingVaccine();
            var gate = new SkillGate(true, true, true);

            loadout.TryCast(0, ref mana, stats, gate);

            Assert.AreEqual(100f, mana.Current, 1e-4f);
            Assert.AreEqual(0, loadout.CooldownRemaining(0));
        }

        [Test]
        public void ControlIsReportedAheadOfResourceProblems()
        {
            // Being stunned is both the stricter rule and the more useful thing to show.
            var loadout = LoadoutWithBolt();
            var mana = ManaPool.Starting();
            mana.Current = 0f;
            var stats = CombatStats.StartingVaccine();

            SkillCastResult result = loadout.TryCast(0, ref mana, stats, new SkillGate(true, false, false));

            Assert.AreEqual(SkillCastRejection.Stunned, result.Rejection);
        }

        [Test]
        public void SlotsHoldSeparateCooldowns()
        {
            var loadout = new SkillLoadout();
            loadout.Equip(0, Bolt());
            loadout.Equip(1, Blink());
            var mana = ManaPool.Starting();
            mana.Max = 1000f;
            mana.Current = 1000f;
            var stats = CombatStats.StartingVaccine();

            loadout.TryCast(0, ref mana, stats, SkillGate.Open);

            Assert.Greater(loadout.CooldownRemaining(0), 0);
            Assert.AreEqual(0, loadout.CooldownRemaining(1), "Casting one skill put another on cooldown.");
            Assert.IsTrue(loadout.TryCast(1, ref mana, stats, SkillGate.Open).Succeeded);
        }
    }
}
