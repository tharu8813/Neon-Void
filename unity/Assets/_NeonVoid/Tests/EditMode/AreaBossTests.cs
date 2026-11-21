using System;
using System.Collections.Generic;
using NeonVoid.Combat.AI;
using NeonVoid.Missions;
using NUnit.Framework;
using UnityEngine;

namespace NeonVoid.Tests
{
    /// <summary>
    /// The five area bosses against docs/01 section 3's contract, and against CLAUDE.md's rule
    /// that the five hardware areas are not one thing pasted five times.
    /// <para>
    /// The contract itself - 3 patterns of different shapes, a 지역 기믹, a phase change, a
    /// 0.8~1.5s telegraph - is held by <see cref="BossPatternCycle.Validate"/> and
    /// <see cref="BossPattern.Validate"/>, and <see cref="BossPatternTests"/> tests those. What
    /// is here is the other half: that every area actually has a boss, and that no two of them
    /// are the same fight.
    /// </para>
    /// </summary>
    public sealed class AreaBossTests
    {
        private static BossPatternCycle[] EveryBoss()
        {
            var cycles = new List<BossPatternCycle>();
            for (int i = 0; i < HardwareIds.All.Length; ++i)
            {
                BossPatternCycle cycle = AreaWorldBuilder.CycleFor(HardwareIds.All[i]);
                Assert.IsNotNull(cycle, $"{HardwareIds.All[i]} has no boss");
                cycles.Add(cycle);
            }

            return cycles.ToArray();
        }

        [Test]
        public void EveryHardwareAreaHasABossAndNothingElseDoes()
        {
            Assert.AreEqual(HardwareIds.All.Length, EveryBoss().Length);
            Assert.IsNull(AreaWorldBuilder.CycleFor(HardwareId.None));
            Assert.IsNull(AreaWorldBuilder.CycleFor((HardwareId)42));
        }

        [Test]
        public void EveryBossMeetsTheContract()
        {
            // Constructing one already validates it, so this is here for the failure message:
            // a broken boss should name its area rather than failing inside a builder.
            for (int i = 0; i < HardwareIds.All.Length; ++i)
            {
                HardwareId hardware = HardwareIds.All[i];
                Assert.DoesNotThrow(
                    () => AreaWorldBuilder.CycleFor(hardware).Validate(), AreaWorldBuilder.BossNameFor(hardware));
            }
        }

        [Test]
        public void NoTwoAreasShareAPattern()
        {
            // CLAUDE.md: 5개 하드웨어를 한 개의 복붙 맵으로 취급하지 말 것. A boss handed another
            // area's cycle would pass every other test in this project.
            var names = new HashSet<string>();

            BossPatternCycle[] bosses = EveryBoss();
            for (int b = 0; b < bosses.Length; ++b)
            {
                for (int p = 0; p < bosses[b].PatternCount; ++p)
                {
                    string name = bosses[b].PatternAt(p).Name;
                    Assert.IsTrue(names.Add(name), $"'{name}' belongs to two bosses");
                }

                Assert.IsTrue(names.Add(bosses[b].Gimmick.Name), $"'{bosses[b].Gimmick.Name}' is two 지역 기믹");
            }

            // Five bosses, three patterns and one gimmick each.
            Assert.AreEqual(HardwareIds.All.Length * 4, names.Count);
        }

        [Test]
        public void NoTwoAreasPulseTheirGimmickOnTheSameClock()
        {
            // The 지역 기믹 is the area's rhythm, and two areas sharing one would feel the same
            // however different their patterns were.
            var intervals = new HashSet<int>();
            for (int i = 0; i < HardwareIds.All.Length; ++i)
            {
                BossPatternCycle cycle = AreaWorldBuilder.CycleFor(HardwareIds.All[i]);
                Assert.IsTrue(
                    intervals.Add(cycle.GimmickTicksUntilTelegraph),
                    $"{HardwareIds.All[i]} pulses on a clock another area already uses");
            }
        }

        [Test]
        public void EveryBossUsesAllThreeShapes()
        {
            // 최소 3개의 형태가 다른 패턴 is the floor; using every shape the game has is what
            // keeps the five fights from being three fights and two variations.
            foreach (HardwareId hardware in HardwareIds.All)
            {
                BossPatternCycle cycle = AreaWorldBuilder.CycleFor(hardware);
                var shapes = new HashSet<BossPatternShape>();
                for (int p = 0; p < cycle.PatternCount; ++p)
                    shapes.Add(cycle.PatternAt(p).Shape);

                Assert.AreEqual(3, shapes.Count, $"{AreaWorldBuilder.BossNameFor(hardware)} repeats a shape");
            }
        }

        [Test]
        public void TheFastestTelegraphIsStillTheDocumentsFloor()
        {
            // docs/01 section 3's 0.8초 floor is on every pattern of every boss, including the
            // SSD's, whose whole identity is speed. 빠른 이동 is not a licence for 불가피 피격.
            int fastest = int.MaxValue;
            foreach (HardwareId hardware in HardwareIds.All)
            {
                BossPatternCycle cycle = AreaWorldBuilder.CycleFor(hardware);
                for (int p = 0; p < cycle.PatternCount; ++p)
                    fastest = Mathf.Min(fastest, cycle.PatternAt(p).TelegraphTicks);
            }

            Assert.AreEqual(BossPattern.MinTelegraphTicks, fastest, "nothing sits on the floor, or something is below it");
        }

        // --- 안전 영역 패턴 -----------------------------------------------------------------

        [Test]
        public void ASafeWedgeIsSafeInsideAndDangerousAroundIt()
        {
            // docs/01 gives the GPU a 회전 팬형 안전 영역: the wedge is where to stand, not what
            // to avoid. Getting the sign wrong would produce an attack that looks identical in
            // every log and kills the player for doing the right thing.
            BossPattern fan = AreaBossCatalog.RotatingSafeFan();
            Assert.IsTrue(fan.SafeInsideShape);

            Vector2 origin = Vector2.zero;
            Vector2 facing = Vector2.right;

            Assert.IsFalse(fan.Covers(origin, facing, new Vector2(6f, 0f)), "the safe wedge is being hit");
            Assert.IsFalse(fan.Covers(origin, facing, new Vector2(3f, 1f)), "just inside the wedge is being hit");
            Assert.IsTrue(fan.Covers(origin, facing, new Vector2(-6f, 0f)), "behind the boss is safe");
            Assert.IsTrue(fan.Covers(origin, facing, new Vector2(0f, 6f)), "to the side is safe");
        }

        [Test]
        public void ASafeWedgeStillEndsSomewhere()
        {
            // An attack with no outer edge cannot be dodged by running, which is docs/01's
            // 피할 수 있는 텔레그래프 in another form.
            BossPattern fan = AreaBossCatalog.RotatingSafeFan();
            Vector2 justOutside = new Vector2(0f, fan.RangeUnits + 1f);

            Assert.IsFalse(
                fan.Covers(Vector2.zero, Vector2.right, justOutside),
                "the safe-inside attack reaches past its own radius");
        }

        [Test]
        public void ASafeShapeThatCoversEverythingIsRefused()
        {
            BossPattern fan = AreaBossCatalog.RotatingSafeFan();
            fan.HalfAngleDegrees = 180f;
            Assert.Throws<InvalidOperationException>(() => fan.Validate());

            BossPattern line = AreaBossCatalog.RailCharge();
            line.SafeInsideShape = true;
            line.HalfWidthUnits = line.RangeUnits;
            Assert.Throws<InvalidOperationException>(() => line.Validate());
        }

        [Test]
        public void ARingCannotBeInverted()
        {
            // It already says "the middle is safe" with InnerRadiusUnits; inverting it as well
            // would mean two safe zones, which no document asks for.
            BossPattern ring = AreaBossCatalog.NodeWave();
            ring.SafeInsideShape = true;
            Assert.Throws<InvalidOperationException>(() => ring.Validate());
        }

        // --- what each area's fight is about -------------------------------------------------

        [Test]
        public void TheRamBossIsSafeCloseInAndTheCpuBossIsSafeFarAway()
        {
            // 진위 구분 means committing to the real thing; the CPU's 코어 펄스 means backing
            // away from it. The two rings are opposites on purpose, and a test holds that
            // because the difference is one field.
            BossPattern cacheCollapse = AreaBossCatalog.CacheCollapse();
            BossPattern corePulse = BossCatalog.CorePulse();

            Assert.Greater(cacheCollapse.InnerRadiusUnits, 0f, "캐시 붕괴 has no safe middle");
            Assert.AreEqual(0f, corePulse.InnerRadiusUnits, "코어 펄스 grew a safe middle");

            Assert.IsFalse(
                cacheCollapse.Covers(Vector2.zero, Vector2.right, new Vector2(1f, 0f)),
                "standing next to the 메모리 리치 is supposed to be the answer");
            Assert.IsTrue(
                corePulse.Covers(Vector2.zero, Vector2.right, new Vector2(1f, 0f)),
                "standing next to the 코어 워처 is supposed to be the mistake");
        }

        [Test]
        public void EveryGimmickLeavesSomewhereToStand()
        {
            // A 지역 기믹 fires while the boss is telegraphing, cooling or recovering, so one
            // that covered the room would be damage the player cannot answer - and docs/01 asks
            // for 피할 수 있는 파편 낙하 by name. The CPU's 주기적 과열 set the shape; this holds
            // that every area kept it.
            foreach (HardwareId hardware in HardwareIds.All)
            {
                BossPatternCycle cycle = AreaWorldBuilder.CycleFor(hardware);
                BossPattern gimmick = cycle.Gimmick;
                string who = AreaWorldBuilder.BossNameFor(hardware);

                bool hasSafeMiddle = gimmick.Shape == BossPatternShape.Ring && gimmick.InnerRadiusUnits > 0f;
                bool isNarrow = gimmick.Shape == BossPatternShape.Line;

                Assert.IsTrue(
                    hasSafeMiddle || isNarrow,
                    $"{who}'s 지역 기믹 '{gimmick.Name}' covers everything within {gimmick.RangeUnits}u");
            }
        }

        [Test]
        public void NoBossPatternCanCrit()
        {
            // An avoidable hit that sometimes kills is not avoidable. The CPU's patterns say so
            // one by one; this holds it for every boss at once, including ones added later.
            foreach (HardwareId hardware in HardwareIds.All)
            {
                BossPatternCycle cycle = AreaWorldBuilder.CycleFor(hardware);
                for (int p = 0; p < cycle.PatternCount; ++p)
                {
                    Assert.IsFalse(
                        cycle.PatternAt(p).Damage.CanCrit,
                        $"{cycle.PatternAt(p).Name} can crit");
                }

                Assert.IsFalse(cycle.Gimmick.Damage.CanCrit, $"{cycle.Gimmick.Name} can crit");
            }
        }

        [Test]
        public void AGimmickHitsForLessThanAnyOfTheBossesOwnPatterns()
        {
            // The 기믹 is a position tax that keeps coming whatever the boss is doing. One that
            // hit as hard as a telegraphed pattern would make standing still lethal and turn
            // the fight into a treadmill.
            foreach (HardwareId hardware in HardwareIds.All)
            {
                BossPatternCycle cycle = AreaWorldBuilder.CycleFor(hardware);
                float weakestPattern = float.MaxValue;
                for (int p = 0; p < cycle.PatternCount; ++p)
                    weakestPattern = Mathf.Min(weakestPattern, cycle.PatternAt(p).Damage.Flat);

                Assert.Less(
                    cycle.Gimmick.Damage.Flat, weakestPattern,
                    $"{AreaWorldBuilder.BossNameFor(hardware)}'s 기믹 hits as hard as its patterns");
            }
        }
    }
}
