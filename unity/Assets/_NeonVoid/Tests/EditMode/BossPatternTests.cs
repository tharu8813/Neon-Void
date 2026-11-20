using System;
using System.Collections.Generic;
using NeonVoid.Combat;
using NeonVoid.Combat.AI;
using NeonVoid.Combat.Skills;
using NeonVoid.Core;
using NUnit.Framework;
using UnityEngine;

namespace NeonVoid.Tests
{
    /// <summary>
    /// docs/01 section 3's 보스 전투 공통 계약, as something that can fail.
    /// <para>
    /// The contract is a list of promises - three patterns of different shapes, a 0.8~1.5초
    /// dodgeable telegraph, a 회복/반격 창, a 지역 기믹, one phase change, and a telegraph whose
    /// volume is the attack's volume. Each of those is checked here against a boss that breaks it,
    /// because a contract nothing has ever been refused by is a contract nobody is keeping.
    /// </para>
    /// </summary>
    public sealed class BossPatternTests
    {
        private static BossPattern Fan(int telegraphTicks = 72) => new BossPattern
        {
            Name = "fan",
            Shape = BossPatternShape.Fan,
            TelegraphTicks = telegraphTicks,
            RecoveryTicks = 30,
            RangeUnits = 9f,
            HalfAngleDegrees = 55f,
            Damage = new DamageRequest { Flat = 10f, Type = DamageType.Physical },
        };

        private static BossPattern Line(int telegraphTicks = 54) => new BossPattern
        {
            Name = "line",
            Shape = BossPatternShape.Line,
            TelegraphTicks = telegraphTicks,
            RecoveryTicks = 24,
            RangeUnits = 18f,
            HalfWidthUnits = 1.3f,
            Damage = new DamageRequest { Flat = 12f, Type = DamageType.Magic },
        };

        private static BossPattern Ring(int telegraphTicks = 60, float inner = 0f) => new BossPattern
        {
            Name = "ring",
            Shape = BossPatternShape.Ring,
            TelegraphTicks = telegraphTicks,
            RecoveryTicks = 36,
            RangeUnits = 7f,
            InnerRadiusUnits = inner,
            Damage = new DamageRequest { Flat = 11f, Type = DamageType.Physical },
        };

        private static BossPattern Gimmick() => new BossPattern
        {
            Name = "gimmick",
            Shape = BossPatternShape.Ring,
            TelegraphTicks = 90,
            RecoveryTicks = 1,
            RangeUnits = 40f,
            InnerRadiusUnits = 4.5f,
            Damage = new DamageRequest { Flat = 8f, Type = DamageType.Magic },
        };

        private static BossPatternCycle Cycle(int gimmickInterval = 540) =>
            new BossPatternCycle(new[] { Fan(), Line(), Ring() }, Gimmick(), gimmickInterval);

        /// <summary>Ticks until the predicate holds, or fails the test.</summary>
        private static BossTickOutcome TickUntil(BossPatternCycle cycle, Func<BossTickOutcome, bool> done, int limit = 4000)
        {
            for (int i = 0; i < limit; ++i)
            {
                BossTickOutcome outcome = cycle.Tick();
                if (done(outcome))
                    return outcome;
            }

            Assert.Fail($"nothing matched in {limit} ticks; the cycle is stuck at {cycle}");
            return default;
        }

        // --- the shapes --------------------------------------------------------------------

        [Test]
        public void ALineCoversItsBandAndNothingBehindIt()
        {
            Vector2 origin = Vector2.zero;
            Vector2 facing = Vector2.right;

            Assert.IsTrue(LineTargeting.IsInsideLine(origin, facing, new Vector2(5f, 0f), 18f, 1.3f));
            Assert.IsTrue(LineTargeting.IsInsideLine(origin, facing, new Vector2(5f, 1.3f), 18f, 1.3f), "the edge");
            Assert.IsFalse(LineTargeting.IsInsideLine(origin, facing, new Vector2(5f, 1.4f), 18f, 1.3f));
            Assert.IsFalse(LineTargeting.IsInsideLine(origin, facing, new Vector2(19f, 0f), 18f, 1.3f), "past the end");
            Assert.IsFalse(
                LineTargeting.IsInsideLine(origin, facing, new Vector2(-5f, 0f), 18f, 1.3f),
                "a pulse fired forwards hit something behind it");
        }

        [Test]
        public void ALineWithNoFacingFallsBackToItsOwnWidthAndNotToTheWholeMap()
        {
            // A cone falls back to the whole circle; an 18-unit band doing the same would be a
            // far bigger lie than one that covers a disc of its own half-width.
            Assert.IsTrue(LineTargeting.IsInsideLine(Vector2.zero, Vector2.zero, new Vector2(1f, 0f), 18f, 1.3f));
            Assert.IsFalse(LineTargeting.IsInsideLine(Vector2.zero, Vector2.zero, new Vector2(10f, 0f), 18f, 1.3f));
        }

        [Test]
        public void ARingWithASafeMiddleLeavesItSafe()
        {
            // The whole point of 주기적 과열: the safe spot is next to the boss, so the arena's
            // hazard pushes the player toward the thing trying to kill them.
            Assert.IsFalse(LineTargeting.IsInsideRing(Vector2.zero, new Vector2(2f, 0f), 4.5f, 40f), "the middle");
            Assert.IsTrue(LineTargeting.IsInsideRing(Vector2.zero, new Vector2(4.5f, 0f), 4.5f, 40f), "the inner edge");
            Assert.IsTrue(LineTargeting.IsInsideRing(Vector2.zero, new Vector2(20f, 0f), 4.5f, 40f));
            Assert.IsFalse(LineTargeting.IsInsideRing(Vector2.zero, new Vector2(41f, 0f), 4.5f, 40f), "past the edge");
        }

        [Test]
        public void ANonsenseRingCoversNothingRatherThanEverything()
        {
            Assert.IsFalse(LineTargeting.IsInsideRing(Vector2.zero, Vector2.zero, 10f, 5f), "inner past outer");
            Assert.IsFalse(LineTargeting.IsInsideRing(Vector2.zero, Vector2.zero, 0f, 0f));
        }

        [Test]
        public void EachShapeCoversWhatItsShapeShouldAndNoOther()
        {
            Vector2 origin = Vector2.zero;
            Vector2 facing = Vector2.right;
            var behind = new Vector2(-4f, 0f);
            var ahead = new Vector2(4f, 0f);

            Assert.IsTrue(Fan().Covers(origin, facing, ahead));
            Assert.IsFalse(Fan().Covers(origin, facing, behind), "a fan reached behind the boss");

            Assert.IsTrue(Line().Covers(origin, facing, ahead));
            Assert.IsFalse(Line().Covers(origin, facing, new Vector2(4f, 3f)), "a line was wider than its width");

            // A ring ignores facing, which is why the brain gives it none.
            Assert.IsTrue(Ring().Covers(origin, facing, behind));
            Assert.IsTrue(Ring().Covers(origin, Vector2.zero, behind));
            Assert.IsFalse(Ring().Covers(origin, facing, new Vector2(8f, 0f)));
        }

        // --- the contract on one pattern ---------------------------------------------------

        [Test]
        public void ATelegraphOutsideTheDocumentsBandIsRefused()
        {
            // docs/01 section 3: 0.8~1.5초. Below it the attack cannot be dodged, which is the
            // one thing the contract is for.
            Assert.AreEqual(48, BossPattern.MinTelegraphTicks, "0.8s at 60Hz");
            Assert.AreEqual(90, BossPattern.MaxTelegraphTicks, "1.5s at 60Hz");

            Assert.Throws<InvalidOperationException>(() => Fan(BossPattern.MinTelegraphTicks - 1).Validate());
            Assert.Throws<InvalidOperationException>(() => Fan(BossPattern.MaxTelegraphTicks + 1).Validate());
            Assert.DoesNotThrow(() => Fan(BossPattern.MinTelegraphTicks).Validate());
            Assert.DoesNotThrow(() => Fan(BossPattern.MaxTelegraphTicks).Validate());
        }

        [Test]
        public void APatternWithNoRecoveryWindowIsRefused()
        {
            // docs/01 asks for a 회복/반격 창. Without one the player never gets a turn.
            BossPattern noRecovery = Fan();
            noRecovery.RecoveryTicks = 0;

            Assert.Throws<InvalidOperationException>(() => noRecovery.Validate());
        }

        [Test]
        public void APatternThatDealsNothingIsRefused()
        {
            BossPattern harmless = Fan();
            harmless.Damage = default;

            Assert.Throws<InvalidOperationException>(() => harmless.Validate());
        }

        [Test]
        public void ANamelessOrShapelessPatternIsRefused()
        {
            BossPattern nameless = Fan();
            nameless.Name = " ";
            Assert.Throws<InvalidOperationException>(() => nameless.Validate());

            BossPattern noReach = Fan();
            noReach.RangeUnits = 0f;
            Assert.Throws<InvalidOperationException>(() => noReach.Validate());

            BossPattern flatFan = Fan();
            flatFan.HalfAngleDegrees = 0f;
            Assert.Throws<InvalidOperationException>(() => flatFan.Validate());

            BossPattern thinLine = Line();
            thinLine.HalfWidthUnits = 0f;
            Assert.Throws<InvalidOperationException>(() => thinLine.Validate());

            BossPattern invertedRing = Ring(inner: 99f);
            Assert.Throws<InvalidOperationException>(() => invertedRing.Validate());
        }

        // --- the contract on a whole boss --------------------------------------------------

        [Test]
        public void ABossWithFewerThanThreePatternsIsRefused()
        {
            Assert.AreEqual(3, BossPatternCycle.MinPatterns);
            Assert.Throws<InvalidOperationException>(
                () => new BossPatternCycle(new[] { Fan(), Line() }, Gimmick(), 540));
        }

        [Test]
        public void ThreePatternsOfOneShapeAreOnePatternTunedThreeWays()
        {
            // docs/01 says 형태가 다른 패턴, and three fans of different radii are not that.
            BossPattern wide = Fan();
            wide.Name = "wide";
            BossPattern narrow = Fan();
            narrow.Name = "narrow";
            narrow.HalfAngleDegrees = 20f;

            Assert.Throws<InvalidOperationException>(
                () => new BossPatternCycle(new[] { Fan(), wide, narrow }, Gimmick(), 540));
        }

        [Test]
        public void AGimmickThatIsNeverOffIsRefused()
        {
            // An interval shorter than its own warning means the telegraph never clears.
            Assert.Throws<InvalidOperationException>(
                () => new BossPatternCycle(new[] { Fan(), Line(), Ring() }, Gimmick(), 90));
        }

        // --- the sequence ------------------------------------------------------------------

        [Test]
        public void TheFightOpensWithAnAnnouncementBeforeAnythingCanHurtAnybody()
        {
            // docs/01's 입장 예고.
            BossPatternCycle cycle = Cycle();

            Assert.AreEqual(BossActivity.Entering, cycle.Activity);
            Assert.AreEqual(BossPhase.Entering, cycle.Phase);

            for (int i = 0; i < BossPatternCycle.DraftEntranceTicks - 1; ++i)
            {
                BossTickOutcome outcome = cycle.Tick();
                Assert.AreEqual(-1, outcome.ResolvedPatternIndex, $"something resolved during the entrance, tick {i}");
                Assert.AreEqual(BossActivity.Entering, cycle.Activity);
            }

            BossTickOutcome last = cycle.Tick();

            Assert.IsTrue(last.PhaseChanged, "the fight started without saying so");
            Assert.AreEqual(BossPhase.Fighting, cycle.Phase);
            Assert.AreEqual(BossActivity.Telegraphing, cycle.Activity);
        }

        [Test]
        public void AnAttackLandsOnTheTickItsWarningRunsOutAndNotBefore()
        {
            BossPatternCycle cycle = Cycle();
            TickUntil(cycle, _ => cycle.Activity == BossActivity.Telegraphing);

            int total = cycle.TelegraphTotalTicks;
            Assert.AreEqual(Fan().TelegraphTicks, total, "the first pattern's warning was the wrong length");

            for (int i = 1; i < total; ++i)
            {
                BossTickOutcome outcome = cycle.Tick();
                Assert.AreEqual(-1, outcome.ResolvedPatternIndex, $"it landed {total - i} ticks early");
            }

            BossTickOutcome landed = cycle.Tick();

            Assert.AreEqual(0, landed.ResolvedPatternIndex);
            Assert.AreEqual(BossActivity.Recovering, cycle.Activity, "there is no 회복/반격 창");
        }

        [Test]
        public void OneTelegraphLandsExactlyOneAttack()
        {
            // A resolve that repeated every tick of the recovery would be the whole fight.
            BossPatternCycle cycle = Cycle();
            int resolves = 0;

            for (int i = 0; i < 400; ++i)
            {
                if (cycle.Tick().ResolvedPatternIndex >= 0)
                    ++resolves;
            }

            Assert.Greater(resolves, 1, "nothing landed in 400 ticks");
            Assert.Less(resolves, 6, $"{resolves} attacks in 400 ticks is not a telegraphed fight");
        }

        [Test]
        public void TheBossWorksThroughAllThreePatternsInOrder()
        {
            BossPatternCycle cycle = Cycle();
            var seen = new List<int>();

            for (int i = 0; i < 1200 && seen.Count < 4; ++i)
            {
                int resolved = cycle.Tick().ResolvedPatternIndex;
                if (resolved >= 0)
                    seen.Add(resolved);
            }

            Assert.AreEqual(new[] { 0, 1, 2, 0 }, seen, "the patterns did not cycle");
        }

        // --- 페이즈 변화: 오버클럭 ----------------------------------------------------------

        [Test]
        public void HalfHealthSendsTheBossIntoOverclock()
        {
            BossPatternCycle cycle = Cycle();
            TickUntil(cycle, _ => cycle.Phase == BossPhase.Fighting);

            Assert.IsFalse(cycle.ReportHealthFraction(0.9f), "it overclocked at full health");
            Assert.IsTrue(cycle.ReportHealthFraction(BossPatternCycle.DraftOverclockAtHealthFraction));
            Assert.AreEqual(BossPhase.Overclocked, cycle.Phase);

            Assert.IsFalse(cycle.ReportHealthFraction(0.1f), "it announced the same phase change twice");
        }

        [Test]
        public void OverclockNeverShortensAWarningBelowTheDocumentsFloor()
        {
            // 속도 증가 is paid for with less recovery and more pulses, not by becoming
            // undodgeable. This is the invariant that keeps the phase change honest.
            var fast = new BossPatternCycle(
                new[] { Fan(BossPattern.MinTelegraphTicks), Line(), Ring() }, Gimmick(), 540);
            TickUntil(fast, _ => fast.Phase == BossPhase.Fighting);
            fast.ReportHealthFraction(0.1f);

            for (int i = 0; i < fast.PatternCount; ++i)
            {
                Assert.GreaterOrEqual(
                    fast.EffectiveTelegraphTicks(i), BossPattern.MinTelegraphTicks,
                    $"overclocked pattern {i} warns for less than 0.8s");
            }
        }

        [Test]
        public void OverclockDoesShortenTheWarningsThatHaveRoom()
        {
            // The other half: a clamp that clamped everything would make the phase change
            // invisible.
            BossPatternCycle cycle = Cycle();
            TickUntil(cycle, _ => cycle.Phase == BossPhase.Fighting);

            int before = cycle.EffectiveTelegraphTicks(0);
            cycle.ReportHealthFraction(0.1f);

            Assert.Less(cycle.EffectiveTelegraphTicks(0), before, "오버클럭 changed nothing about the pacing");
        }

        [Test]
        public void AnOverclockedBossStopsToCoolAndLandsNothingWhileItDoes()
        {
            // docs/01: 오버클럭 중 속도 증가, 대신 냉각 창으로 공격 기회.
            BossPatternCycle cycle = Cycle();
            TickUntil(cycle, _ => cycle.Phase == BossPhase.Fighting);
            cycle.ReportHealthFraction(0.1f);

            TickUntil(cycle, _ => cycle.Activity == BossActivity.Cooling);

            Assert.IsTrue(cycle.IsCooling);
            for (int i = 0; i < BossPatternCycle.DraftCoolingTicks - 1; ++i)
            {
                Assert.AreEqual(
                    -1, cycle.Tick().ResolvedPatternIndex, $"it attacked during the 냉각 창, tick {i}");
            }

            cycle.Tick();
            Assert.AreEqual(BossActivity.Telegraphing, cycle.Activity, "it never came back from cooling");
        }

        [Test]
        public void TheCoolingWindowComesAfterTheDraftedNumberOfPatterns()
        {
            BossPatternCycle cycle = Cycle();
            TickUntil(cycle, _ => cycle.Phase == BossPhase.Fighting);
            cycle.ReportHealthFraction(0.1f);

            int resolves = 0;
            for (int i = 0; i < 2000 && cycle.Activity != BossActivity.Cooling; ++i)
            {
                if (cycle.Tick().ResolvedPatternIndex >= 0)
                    ++resolves;
            }

            Assert.AreEqual(BossActivity.Cooling, cycle.Activity, "it never cooled");
            Assert.AreEqual(BossPatternCycle.DraftPatternsPerCooling, resolves);
        }

        // --- 지역 기믹 ---------------------------------------------------------------------

        [Test]
        public void TheGimmickAnnouncesItselfAndThenPulsesOnItsOwnClock()
        {
            BossPatternCycle cycle = Cycle(gimmickInterval: 300);

            Assert.IsFalse(cycle.IsGimmickTelegraphing);
            TickUntil(cycle, _ => cycle.IsGimmickTelegraphing);

            int warned = 0;
            BossTickOutcome pulse = TickUntil(cycle, outcome =>
            {
                ++warned;
                return outcome.ResolvedGimmick;
            });

            Assert.IsTrue(pulse.ResolvedGimmick);
            Assert.AreEqual(Gimmick().TelegraphTicks, warned, "the 기믹 pulsed without its full warning");
            Assert.IsFalse(cycle.IsGimmickTelegraphing, "the warning stayed up after the pulse");
        }

        [Test]
        public void TheGimmickKeepsComingWhateverTheBossIsDoing()
        {
            // 주기적 과열 is the CPU's 기본 위험, not one of the boss's moves. Hanging it off the
            // pattern cycle would stop it exactly when the player has the most room to breathe.
            BossPatternCycle cycle = Cycle(gimmickInterval: 200);
            var activitiesWhenPulsed = new HashSet<BossActivity>();

            TickUntil(cycle, _ => cycle.Phase == BossPhase.Fighting);
            cycle.ReportHealthFraction(0.1f);

            for (int i = 0; i < 4000; ++i)
            {
                if (cycle.Tick().ResolvedGimmick)
                    activitiesWhenPulsed.Add(cycle.Activity);
            }

            Assert.Greater(
                activitiesWhenPulsed.Count, 1,
                "the 기믹 only ever pulsed during one activity, so it is tied to the pattern cycle");
        }

        // --- the end -----------------------------------------------------------------------

        [Test]
        public void AFinishedFightTelegraphsAndResolvesNothingMore()
        {
            BossPatternCycle cycle = Cycle();
            TickUntil(cycle, _ => cycle.Activity == BossActivity.Telegraphing);

            cycle.Finish();

            Assert.AreEqual(BossActivity.Finished, cycle.Activity);
            for (int i = 0; i < 600; ++i)
            {
                BossTickOutcome outcome = cycle.Tick();
                Assert.IsFalse(outcome.DidSomething, $"a dead boss acted on tick {i}");
            }
        }

        // --- the CPU's boss ----------------------------------------------------------------

        [Test]
        public void TheCoreWatcherKeepsTheWholeContract()
        {
            BossPatternCycle watcher = BossCatalog.CoreWatcher();

            Assert.DoesNotThrow(watcher.Validate);
            Assert.GreaterOrEqual(watcher.PatternCount, BossPatternCycle.MinPatterns);

            var shapes = new HashSet<BossPatternShape>();
            for (int i = 0; i < watcher.PatternCount; ++i)
            {
                BossPattern pattern = watcher.PatternAt(i);
                shapes.Add(pattern.Shape);
                Assert.GreaterOrEqual(pattern.TelegraphTicks, BossPattern.MinTelegraphTicks, pattern.Name);
                Assert.LessOrEqual(pattern.TelegraphTicks, BossPattern.MaxTelegraphTicks, pattern.Name);
                Assert.GreaterOrEqual(pattern.RecoveryTicks, 1, pattern.Name);
                Assert.IsFalse(pattern.Damage.CanCrit, $"{pattern.Name} can crit, so a dodgeable hit can spike");
            }

            Assert.AreEqual(3, shapes.Count, "the 코어 워처's patterns are not 형태가 다른");
        }

        [Test]
        public void TheOverheatGimmicksSafeSpotIsBesideTheBoss()
        {
            // The design of the pair: the arena's hazard pushes the player toward the boss and
            // the boss's own 코어 펄스 pushes them away, so position is a decision rather than
            // two separate dodges.
            BossPattern overheat = BossCatalog.CoreWatcherOverheat();
            BossPattern corePulse = BossCatalog.CorePulse();

            Assert.Greater(overheat.InnerRadiusUnits, 0f, "주기적 과열 has no safe middle");
            Assert.IsFalse(overheat.Covers(Vector2.zero, Vector2.zero, new Vector2(2f, 0f)), "beside the boss");
            Assert.IsTrue(overheat.Covers(Vector2.zero, Vector2.zero, new Vector2(15f, 0f)), "out in the room");

            Assert.AreEqual(0f, corePulse.InnerRadiusUnits, "코어 펄스 should be a solid disc");
            Assert.IsTrue(corePulse.Covers(Vector2.zero, Vector2.zero, new Vector2(2f, 0f)));
            Assert.IsFalse(corePulse.Covers(Vector2.zero, Vector2.zero, new Vector2(15f, 0f)));
        }

        [Test]
        public void TheOverheatIntervalIsLongerThanItsOwnWarning()
        {
            Assert.Greater(
                BossCatalog.DraftCoreWatcherOverheatIntervalTicks,
                BossCatalog.CoreWatcherOverheat().TelegraphTicks);
            Assert.AreEqual(
                540, BossCatalog.DraftCoreWatcherOverheatIntervalTicks,
                "[초안] nine seconds; this test is here so a retune is deliberate");
        }

        [Test]
        public void ATelegraphSecondsReadsAsTheDocumentsNumbers()
        {
            Assert.AreEqual(
                1.2f, BossCatalog.CoreOpening().TelegraphSeconds, 0.01f);
            Assert.AreEqual(0.9f, BossCatalog.PulseCircuit().TelegraphSeconds, 0.01f);
            Assert.AreEqual(
                1.5f, BossCatalog.CoreWatcherOverheat().TelegraphSeconds, 0.01f,
                "the room-wide pulse should use the full 1.5s ceiling");
            Assert.AreEqual(60, DesignConstants.SimulationTicksPerSecond);
        }
    }
}
