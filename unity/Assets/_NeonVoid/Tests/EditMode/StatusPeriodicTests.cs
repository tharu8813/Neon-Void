using System;
using System.Collections.Generic;
using NeonVoid.Combat.Status;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// 재생 and 무결성 공격: the two catalogue effects that are supposed to do something on a
    /// schedule, and until now were tracked with a duration, a stack count and a magnitude that
    /// nothing ever read.
    /// </summary>
    public sealed class StatusPeriodicTests
    {
        private static StatusContainer Container() => new StatusContainer(StatusCatalog.Draft());

        private static StatusEffectDefinition Definition(StatusEffectId id) =>
            StatusCatalog.Draft()[id];

        /// <summary>Ticks a container and returns every periodic event it produced.</summary>
        private static List<StatusPeriodicEvent> Run(StatusContainer status, int ticks)
        {
            var events = new List<StatusPeriodicEvent>();
            for (int i = 0; i < ticks; ++i)
                status.Tick(events);

            return events;
        }

        // --- the catalogue -----------------------------------------------------------------

        [Test]
        public void TheTwoPeriodicEffectsCarryAnInterval()
        {
            Assert.Greater(Definition(StatusEffectId.Regeneration).PeriodicIntervalTicks, 0);
            Assert.Greater(Definition(StatusEffectId.IntegrityAttack).PeriodicIntervalTicks, 0);
        }

        [Test]
        public void EverythingElseCarriesNone()
        {
            // An interval on an effect nothing fires would be a number that looks meaningful
            // and is not.
            Assert.AreEqual(0, Definition(StatusEffectId.Slow).PeriodicIntervalTicks);
            Assert.AreEqual(0, Definition(StatusEffectId.Empower).PeriodicIntervalTicks);
            Assert.AreEqual(0, Definition(StatusEffectId.Stun).PeriodicIntervalTicks);
        }

        [Test]
        public void AnIntervalLongerThanTheEffectIsRefused()
        {
            // It would fire once at most, or never, which is a periodic effect that is not
            // periodic - and reads in play as one that is simply broken.
            StatusEffectDefinition definition = Definition(StatusEffectId.IntegrityAttack);
            definition.PeriodicIntervalTicks = definition.BaseDurationTicks + 1;

            Assert.Throws<InvalidOperationException>(() => definition.Validate());
        }

        // --- the schedule ------------------------------------------------------------------

        [Test]
        public void NothingFiresOnTheTickTheEffectIsApplied()
        {
            // A status can be reapplied continuously, and an effect that fired on application
            // would deal its damage every tick to anyone reapplying it quickly. The skill field
            // does fire immediately, because it is placed once and deliberately.
            StatusContainer status = Container();
            status.Apply(StatusEffectId.IntegrityAttack, 3f);

            Assert.IsEmpty(Run(status, 1));
        }

        [Test]
        public void ItFiresOnceEveryInterval()
        {
            StatusEffectDefinition definition = Definition(StatusEffectId.IntegrityAttack);
            StatusContainer status = Container();
            status.Apply(StatusEffectId.IntegrityAttack, 3f);

            List<StatusPeriodicEvent> events = Run(status, definition.PeriodicIntervalTicks * 3);

            Assert.AreEqual(3, events.Count, "The rate is not the rate.");
            for (int i = 0; i < events.Count; ++i)
            {
                Assert.AreEqual(StatusEffectId.IntegrityAttack, events[i].Id);
                Assert.AreEqual(3f, events[i].Magnitude, 1e-4f);
            }
        }

        [Test]
        public void TheTickItExpiresOnFiresNothing()
        {
            // Same rule as the skill field: an extra event at the end is a silent damage
            // increase that no number in the data accounts for.
            StatusEffectDefinition definition = Definition(StatusEffectId.IntegrityAttack);
            StatusContainer status = Container();
            status.Apply(StatusEffectId.IntegrityAttack, 1f);

            List<StatusPeriodicEvent> events = Run(status, definition.BaseDurationTicks + 10);

            int expected = (definition.BaseDurationTicks - 1) / definition.PeriodicIntervalTicks;
            Assert.AreEqual(expected, events.Count);
            Assert.IsFalse(status.Has(StatusEffectId.IntegrityAttack), "It outlived its duration.");
        }

        [Test]
        public void ReapplyingAStackingPeriodicEffectAddsAnInstanceRatherThanRefreshing()
        {
            // Both catalogue effects use IndependentStacks, so every application is its own
            // instance with its own timer. That is what the data says, and I had assumed
            // otherwise: reapplying 무결성 공격 every tick does not refresh one effect, it
            // builds up many - which is also what R04 해체 프로토콜 will need.
            StatusEffectDefinition definition = Definition(StatusEffectId.IntegrityAttack);
            StatusContainer status = Container();

            // Two intervals, because the instances are staggered by one tick each: inside a
            // single interval only the oldest one has had time to come round.
            var events = new List<StatusPeriodicEvent>();
            for (int i = 0; i < definition.PeriodicIntervalTicks * 2; ++i)
            {
                status.Apply(StatusEffectId.IntegrityAttack, 1f);
                status.Tick(events);
            }

            Assert.Greater(
                events.Count, 1,
                "Independent stacks fired as one, so a stacking damage-over-time would not stack.");
        }

        [Test]
        public void RefreshingAPeriodicEffectNeitherAcceleratesNorPreventsItsNextEvent()
        {
            // The trap in both directions, on an effect whose policy really does refresh.
            // Resetting the countdown on reapplication would let an effect refreshed every tick
            // never fire at all; zeroing it would make the same reapplication fire every tick.
            // No catalogue effect combines RefreshDuration with an interval yet, so the policy
            // is exercised with a catalogue of its own rather than left untested until one does.
            var catalog = new Dictionary<StatusEffectId, StatusEffectDefinition>
            {
                [StatusEffectId.IntegrityAttack] = new StatusEffectDefinition
                {
                    Id = StatusEffectId.IntegrityAttack,
                    IsBuff = false,
                    IsControl = false,
                    StackPolicy = StatusStackPolicy.RefreshDuration,
                    MaxStacks = 1,
                    BaseDurationTicks = 180,
                    PeriodicIntervalTicks = 30,
                },
            };

            var refreshed = new StatusContainer(catalog);
            var untouched = new StatusContainer(catalog);
            refreshed.Apply(StatusEffectId.IntegrityAttack, 2f);
            untouched.Apply(StatusEffectId.IntegrityAttack, 2f);

            var refreshedEvents = new List<StatusPeriodicEvent>();
            var untouchedEvents = new List<StatusPeriodicEvent>();
            for (int i = 0; i < 60; ++i)
            {
                refreshed.Apply(StatusEffectId.IntegrityAttack, 2f);
                refreshed.Tick(refreshedEvents);
                untouched.Tick(untouchedEvents);
            }

            Assert.AreEqual(2, untouchedEvents.Count, "The untouched effect fired at the wrong rate.");
            Assert.AreEqual(
                untouchedEvents.Count, refreshedEvents.Count,
                "Refreshing moved the periodic countdown.");
        }

        [Test]
        public void RegenerationFiresOnItsOwnSchedule()
        {
            StatusEffectDefinition definition = Definition(StatusEffectId.Regeneration);
            StatusContainer status = Container();
            status.Apply(StatusEffectId.Regeneration, 4f);

            List<StatusPeriodicEvent> events = Run(status, definition.PeriodicIntervalTicks);

            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(StatusEffectId.Regeneration, events[0].Id);
            Assert.AreEqual(4f, events[0].Magnitude, 1e-4f);
        }

        [Test]
        public void TwoIndependentStacksFireIndependently()
        {
            // 재생 and 무결성 공격 are both IndependentStacks: two applications are two
            // instances, each with its own timer, so both fire.
            StatusEffectDefinition definition = Definition(StatusEffectId.Regeneration);
            StatusContainer status = Container();
            status.Apply(StatusEffectId.Regeneration, 4f);
            status.Apply(StatusEffectId.Regeneration, 4f);

            List<StatusPeriodicEvent> events = Run(status, definition.PeriodicIntervalTicks);

            Assert.AreEqual(2, events.Count, "Two independent instances fired as one.");
        }

        [Test]
        public void TheMagnitudeFiredIsTheInstancesCombinedMagnitude()
        {
            // An aggregating or stacking effect should fire for what it currently totals, not
            // for one stack's worth.
            StatusContainer status = Container();
            status.Apply(StatusEffectId.Regeneration, 2.5f);

            List<StatusPeriodicEvent> events = Run(
                status, Definition(StatusEffectId.Regeneration).PeriodicIntervalTicks);

            Assert.AreEqual(2.5f, events[0].Magnitude, 1e-4f);
        }

        [Test]
        public void AnEffectWithNoIntervalNeverFires()
        {
            StatusContainer status = Container();
            status.Apply(StatusEffectId.Slow, 0.3f);
            status.Apply(StatusEffectId.Empower, 0.3f);

            Assert.IsEmpty(Run(status, 200));
        }

        [Test]
        public void TickWithoutAListStillAdvancesEverything()
        {
            // The old one-argument Tick is still used by callers that do not care about
            // periodic events, and it must not behave differently.
            StatusEffectDefinition definition = Definition(StatusEffectId.IntegrityAttack);
            StatusContainer status = Container();
            status.Apply(StatusEffectId.IntegrityAttack, 1f);

            for (int i = 0; i < definition.BaseDurationTicks; ++i)
                status.Tick();

            Assert.IsFalse(status.Has(StatusEffectId.IntegrityAttack));
        }

        [Test]
        public void ExpiryIsStillReportedWhileEventsAreCollected()
        {
            StatusEffectDefinition definition = Definition(StatusEffectId.IntegrityAttack);
            StatusContainer status = Container();
            status.Apply(StatusEffectId.IntegrityAttack, 1f);

            IReadOnlyList<StatusEffectId> expired = null;
            var events = new List<StatusPeriodicEvent>();
            for (int i = 0; i < definition.BaseDurationTicks; ++i)
            {
                IReadOnlyList<StatusEffectId> thisTick = status.Tick(events);
                if (thisTick.Count > 0)
                    expired = thisTick;
            }

            Assert.IsNotNull(expired, "Expiry stopped being reported.");
            Assert.AreEqual(StatusEffectId.IntegrityAttack, expired[0]);
        }
    }
}
