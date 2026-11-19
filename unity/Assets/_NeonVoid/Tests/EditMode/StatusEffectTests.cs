using System;
using System.Collections.Generic;
using NeonVoid.Combat.Status;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// docs/02 section 9 item 5: status application, expiry, stacking and boss resistance,
    /// plus the section 5 rule that tenacity must not be over-applied.
    /// </summary>
    public sealed class StatusEffectTests
    {
        private static IReadOnlyDictionary<StatusEffectId, StatusEffectDefinition> Catalog() =>
            StatusCatalog.Draft();

        private static StatusContainer Container() => new StatusContainer(Catalog());

        private static StatusEffectDefinition Definition(StatusEffectId id) => Catalog()[id];

        // --- catalogue shape ---------------------------------------------------------------

        [Test]
        public void CatalogueHasTheEightBuffsAndNineDebuffsTheDocumentLists()
        {
            var catalog = Catalog();
            int buffs = 0, debuffs = 0;
            foreach (var entry in catalog.Values)
            {
                if (entry.IsBuff) buffs++; else debuffs++;
            }

            Assert.AreEqual(17, catalog.Count);
            Assert.AreEqual(8, buffs);
            Assert.AreEqual(9, debuffs);
        }

        /// <summary>
        /// The classification tenacity depends on. Getting this wrong hands players
        /// tenacity-based immunity to damage over time.
        /// </summary>
        [Test]
        public void OnlyMovementAndActionDenialAreClassifiedAsControl()
        {
            foreach (var id in new[]
            {
                StatusEffectId.Slow, StatusEffectId.Malfunction, StatusEffectId.Silence,
                StatusEffectId.Root, StatusEffectId.Stun,
            })
                Assert.IsTrue(Definition(id).IsControl, id + " should be a control effect.");

            foreach (var id in new[]
            {
                StatusEffectId.IntegrityAttack,   // damage over time
                StatusEffectId.Weaken,            // stat reduction
                StatusEffectId.FirewallBreach,    // defence reduction
                StatusEffectId.AttackSpeedDown,   // stat reduction
            })
                Assert.IsFalse(Definition(id).IsControl,
                    id + " must not be control, or tenacity would shrink damage or stat loss.");
        }

        [Test]
        public void NoBuffIsClassifiedAsControl()
        {
            foreach (var entry in Catalog().Values)
                if (entry.IsBuff)
                    Assert.IsFalse(entry.IsControl, entry.Id + " is a buff and must not be control.");
        }

        [Test]
        public void ABuffMarkedAsControlIsRejectedByValidation()
        {
            var broken = new StatusEffectDefinition
            {
                Id = StatusEffectId.Empower,
                IsBuff = true,
                IsControl = true,
                StackPolicy = StatusStackPolicy.RefreshDuration,
                MaxStacks = 1,
                BaseDurationTicks = 60,
            };

            Assert.Throws<InvalidOperationException>(() => broken.Validate());
        }

        // --- tenacity ------------------------------------------------------------------------

        [Test]
        public void TenacityShortensAControlEffect()
        {
            int duration = TenacityMath.EffectiveDurationTicks(
                Definition(StatusEffectId.Stun), 100, tenacityPercent: 30f, controlResistancePercent: 0f);

            Assert.AreEqual(70, duration);
        }

        [Test]
        public void TenacityLeavesDamageOverTimeAndStatReductionsAlone()
        {
            // The over-application guard from docs/02 section 5.
            foreach (var id in new[]
            {
                StatusEffectId.IntegrityAttack, StatusEffectId.Weaken,
                StatusEffectId.FirewallBreach, StatusEffectId.AttackSpeedDown,
            })
            {
                int duration = TenacityMath.EffectiveDurationTicks(Definition(id), 100, 90f, 0f);
                Assert.AreEqual(100, duration, id + " was shortened by tenacity.");
            }
        }

        [Test]
        public void TenacityLeavesBuffsAlone()
        {
            int duration = TenacityMath.EffectiveDurationTicks(
                Definition(StatusEffectId.Regeneration), 100, 90f, 0f);

            Assert.AreEqual(100, duration);
        }

        [Test]
        public void TenacityCanNeverReduceAControlEffectToNothing()
        {
            int duration = TenacityMath.EffectiveDurationTicks(
                Definition(StatusEffectId.Stun), 100, tenacityPercent: 100f, controlResistancePercent: 100f);

            Assert.AreEqual(TenacityMath.MinimumControlDurationTicks, duration);
            Assert.Greater(duration, 0, "Crowd control silently stopped existing at high tenacity.");
        }

        [Test]
        public void TenacityAboveOneHundredPercentClampsInsteadOfInvertingIntoABonus()
        {
            int duration = TenacityMath.EffectiveDurationTicks(
                Definition(StatusEffectId.Root), 100, tenacityPercent: 250f, controlResistancePercent: 0f);

            Assert.AreEqual(TenacityMath.MinimumControlDurationTicks, duration);
        }

        [Test]
        public void NegativeOrNaNTenacityIsTreatedAsNone()
        {
            Assert.AreEqual(100, TenacityMath.EffectiveDurationTicks(Definition(StatusEffectId.Stun), 100, -50f, 0f));
            Assert.AreEqual(100, TenacityMath.EffectiveDurationTicks(Definition(StatusEffectId.Stun), 100, float.NaN, 0f));
        }

        [Test]
        public void BossControlResistanceCombinesWithTenacityMultiplicatively()
        {
            // Two 50 percent sources leave a quarter, not nothing.
            int duration = TenacityMath.EffectiveDurationTicks(
                Definition(StatusEffectId.Stun), 100, tenacityPercent: 50f, controlResistancePercent: 50f);

            Assert.AreEqual(25, duration);
            Assert.AreEqual(0.25f, TenacityMath.RemainingFraction(50f, 50f), 1e-4f);
        }

        [Test]
        public void BossResistanceAloneStillOnlyTouchesControlEffects()
        {
            int dot = TenacityMath.EffectiveDurationTicks(
                Definition(StatusEffectId.IntegrityAttack), 100, 0f, controlResistancePercent: 80f);

            Assert.AreEqual(100, dot);
        }

        // --- stack policies -------------------------------------------------------------------

        [Test]
        public void RefreshDurationKeepsOneInstanceAndResetsItsTimer()
        {
            var container = Container();
            container.Apply(StatusEffectId.Root, magnitude: 1f);

            for (int i = 0; i < 30; ++i)
                container.Tick();
            Assert.Less(container.RemainingTicksOf(StatusEffectId.Root), 60);

            StatusApplyOutcome outcome = container.Apply(StatusEffectId.Root, 1f);

            Assert.AreEqual(StatusApplyOutcome.RefreshedExisting, outcome);
            Assert.AreEqual(1, container.StacksOf(StatusEffectId.Root));
            Assert.AreEqual(60, container.RemainingTicksOf(StatusEffectId.Root));
        }

        [Test]
        public void UniqueIgnoresASecondApplicationIncludingItsTimer()
        {
            var container = Container();
            container.Apply(StatusEffectId.Invulnerable, 1f);
            container.Tick();

            StatusApplyOutcome outcome = container.Apply(StatusEffectId.Invulnerable, 1f);

            Assert.AreEqual(StatusApplyOutcome.Ignored, outcome);
            Assert.AreEqual(29, container.RemainingTicksOf(StatusEffectId.Invulnerable),
                "A second invulnerability extended the first into near-permanent immunity.");
        }

        [Test]
        public void ReplaceStrongerIgnoresAWeakerSourceTimerIncluded()
        {
            var container = Container();
            container.Apply(StatusEffectId.Slow, magnitude: 0.5f);
            container.Tick();
            int afterOneTick = container.RemainingTicksOf(StatusEffectId.Slow);

            StatusApplyOutcome weak = container.Apply(StatusEffectId.Slow, magnitude: 0.2f);

            Assert.AreEqual(StatusApplyOutcome.Ignored, weak);
            Assert.AreEqual(0.5f, container.TotalMagnitudeOf(StatusEffectId.Slow), 1e-4f);
            Assert.AreEqual(afterOneTick, container.RemainingTicksOf(StatusEffectId.Slow),
                "A weak source kept a strong slow alive by refreshing it.");
        }

        [Test]
        public void ReplaceStrongerTakesOverWhenTheNewSourceIsStronger()
        {
            var container = Container();
            container.Apply(StatusEffectId.Slow, 0.2f);

            StatusApplyOutcome strong = container.Apply(StatusEffectId.Slow, 0.6f);

            Assert.AreEqual(StatusApplyOutcome.RefreshedExisting, strong);
            Assert.AreEqual(0.6f, container.TotalMagnitudeOf(StatusEffectId.Slow), 1e-4f);
        }

        [Test]
        public void IndependentStacksEachKeepTheirOwnTimer()
        {
            var container = Container();
            container.Apply(StatusEffectId.IntegrityAttack, magnitude: 3f, durationTicksOverride: 10);
            container.Tick();
            container.Tick();
            container.Apply(StatusEffectId.IntegrityAttack, magnitude: 3f, durationTicksOverride: 10);

            Assert.AreEqual(2, container.StacksOf(StatusEffectId.IntegrityAttack));
            Assert.AreEqual(6f, container.TotalMagnitudeOf(StatusEffectId.IntegrityAttack), 1e-4f);

            // The first one ends two ticks before the second.
            for (int i = 0; i < 8; ++i)
                container.Tick();

            Assert.AreEqual(1, container.StacksOf(StatusEffectId.IntegrityAttack),
                "Independent instances expired together instead of separately.");
            Assert.AreEqual(3f, container.TotalMagnitudeOf(StatusEffectId.IntegrityAttack), 1e-4f);
        }

        [Test]
        public void AdditiveStacksGrowToTheCapAndThenOnlyRefresh()
        {
            var container = Container();
            StatusEffectDefinition empower = Definition(StatusEffectId.Empower);   // MaxStacks 5

            Assert.AreEqual(StatusApplyOutcome.Applied, container.Apply(StatusEffectId.Empower, 2f));
            for (int i = 1; i < empower.MaxStacks; ++i)
                Assert.AreEqual(StatusApplyOutcome.AddedStack, container.Apply(StatusEffectId.Empower, 2f));

            Assert.AreEqual(5, container.StacksOf(StatusEffectId.Empower));
            Assert.AreEqual(10f, container.TotalMagnitudeOf(StatusEffectId.Empower), 1e-4f);

            container.Tick();
            StatusApplyOutcome atCap = container.Apply(StatusEffectId.Empower, 2f);

            Assert.AreEqual(StatusApplyOutcome.RefreshedExisting, atCap);
            Assert.AreEqual(5, container.StacksOf(StatusEffectId.Empower), "The cap was exceeded.");
            Assert.AreEqual(empower.BaseDurationTicks, container.RemainingTicksOf(StatusEffectId.Empower),
                "A capped buff fell off while its source kept applying it.");
        }

        /// <summary>
        /// [초안] Stacks keep the magnitude of the application that created the effect. Pinned by
        /// a test so a later change to per-source magnitudes is a deliberate decision, not a
        /// silent drift.
        /// </summary>
        [Test]
        public void AdditiveStacksKeepTheMagnitudeOfTheFirstApplication()
        {
            var container = Container();
            container.Apply(StatusEffectId.Empower, magnitude: 2f);
            container.Apply(StatusEffectId.Empower, magnitude: 100f);

            Assert.AreEqual(4f, container.TotalMagnitudeOf(StatusEffectId.Empower), 1e-4f);
        }

        [Test]
        public void UnboundedAggregationIsNotSilentlyCappedButIsObservable()
        {
            // docs/02 section 6 names this as a performance risk. Capping it would change a
            // game rule to save frame time, so the count is reported instead.
            var container = Container();
            for (int i = 0; i < 5000; ++i)
                container.Apply(StatusEffectId.FirewallBreach, 0.01f);

            Assert.AreEqual(5000, container.StacksOf(StatusEffectId.FirewallBreach));
            Assert.AreEqual(1, container.Instances.Count, "Unbounded aggregation should stay in one instance.");
        }

        // --- lifetime ---------------------------------------------------------------------------

        [Test]
        public void AnEffectExpiresOnExactlyTheTickItsDurationRunsOut()
        {
            var container = Container();
            container.Apply(StatusEffectId.Stun, 1f, durationTicksOverride: 3);

            Assert.IsEmpty(container.Tick());
            Assert.IsEmpty(container.Tick());
            Assert.IsTrue(container.Has(StatusEffectId.Stun), "It ended a tick early.");

            IReadOnlyList<StatusEffectId> expired = container.Tick();

            CollectionAssert.AreEqual(new[] { StatusEffectId.Stun }, expired);
            Assert.IsFalse(container.Has(StatusEffectId.Stun));
        }

        [Test]
        public void SeveralEffectsEndingOnTheSameTickAreReportedInApplicationOrder()
        {
            var container = Container();
            container.Apply(StatusEffectId.Root, 1f, durationTicksOverride: 2);
            container.Apply(StatusEffectId.Silence, 1f, durationTicksOverride: 2);
            container.Apply(StatusEffectId.Weaken, 1f, durationTicksOverride: 2);

            container.Tick();
            IReadOnlyList<StatusEffectId> expired = container.Tick();

            CollectionAssert.AreEqual(
                new[] { StatusEffectId.Root, StatusEffectId.Silence, StatusEffectId.Weaken },
                expired,
                "Simultaneous expiry order was not deterministic.");
        }

        [Test]
        public void ApplyingWithZeroDurationDoesNotCreateAGhostInstance()
        {
            var container = Container();

            StatusApplyOutcome outcome = container.Apply(StatusEffectId.Stun, 1f, durationTicksOverride: 0);

            Assert.AreEqual(StatusApplyOutcome.ExpiredImmediately, outcome);
            Assert.IsFalse(container.Has(StatusEffectId.Stun));
        }

        [Test]
        public void MalfunctionIsAControlEffectAndIsShortenedByTenacity()
        {
            // Called out in docs/02 section 9 item 5 by name.
            var container = Container();
            container.Apply(StatusEffectId.Malfunction, 1f, tenacityPercent: 50f, durationTicksOverride: 100);

            Assert.IsTrue(container.Has(StatusEffectId.Malfunction));
            Assert.AreEqual(50, container.RemainingTicksOf(StatusEffectId.Malfunction));
        }

        [Test]
        public void RemovingAnEffectClearsEveryInstanceOfIt()
        {
            var container = Container();
            container.Apply(StatusEffectId.IntegrityAttack, 1f);
            container.Apply(StatusEffectId.IntegrityAttack, 1f);
            container.Apply(StatusEffectId.Root, 1f);

            int removed = container.Remove(StatusEffectId.IntegrityAttack);

            Assert.AreEqual(2, removed);
            Assert.IsFalse(container.Has(StatusEffectId.IntegrityAttack));
            Assert.IsTrue(container.Has(StatusEffectId.Root), "Removing one effect took another with it.");
        }

        [Test]
        public void AnEffectOutsideTheCatalogueIsRejectedRatherThanSilentlyIgnored()
        {
            var container = new StatusContainer(new Dictionary<StatusEffectId, StatusEffectDefinition>());

            Assert.Throws<InvalidOperationException>(() => container.Apply(StatusEffectId.Stun, 1f));
        }

        [Test]
        public void NonFiniteMagnitudeIsNeutralisedInsteadOfPoisoningTheTotal()
        {
            var container = Container();
            container.Apply(StatusEffectId.Weaken, float.NaN);

            Assert.IsFalse(float.IsNaN(container.TotalMagnitudeOf(StatusEffectId.Weaken)));
        }
    }
}
