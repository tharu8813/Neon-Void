using System;
using System.Collections.Generic;
using NeonVoid.Missions;
using NeonVoid.Missions.Generation;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// 환경 상호작용 — the hazards docs/01 section 2 gives each hardware.
    /// <para>
    /// CLAUDE.md names this among the things that must differ between the areas: <i>5개 하드웨어를
    /// 한 개의 복붙 맵으로 취급하지 말 것 — 서로 다른 목표·적·보스·환경 상호작용·플레이 리듬이
    /// 필요</i>. Objectives, enemies and bosses already differ; this was the leg that did not
    /// exist at all.
    /// </para>
    /// </summary>
    public sealed class AreaHazardTests
    {
        private static AreaSpec SpecFor(HardwareId hardware, uint seed = 9u) =>
            new AreaSpec(hardware, seed, objectiveCount: 2);

        private static AreaLayout LayoutFor(HardwareId hardware, uint seed = 9u)
        {
            Assert.IsTrue(
                AreaGenerator.TryGenerate(SpecFor(hardware, seed), out AreaLayout layout, out string why), why);
            return layout;
        }

        // --- five areas, five hazards ------------------------------------------------------------

        [Test]
        public void EveryAreaHasOneAndTheyAreAllDifferent()
        {
            Assert.IsTrue(
                AreaHazards.EveryAreaHasItsOwn(),
                "two hardwares share a hazard, which is the copy-pasted map CLAUDE.md forbids");

            for (int i = 0; i < HardwareIds.All.Length; ++i)
            {
                HardwareId hardware = HardwareIds.All[i];
                if (!HardwareIds.IsArea(hardware))
                    continue;

                Assert.IsFalse(AreaHazards.For(hardware).IsNone, hardware.ToString());
            }
        }

        [Test]
        public void EveryHazardIsValidData()
        {
            for (int i = 0; i < HardwareIds.All.Length; ++i)
            {
                HardwareId hardware = HardwareIds.All[i];
                if (!HardwareIds.IsArea(hardware))
                    continue;

                Assert.DoesNotThrow(() => AreaHazards.For(hardware).Validate(), hardware.ToString());
            }
        }

        [Test]
        public void OnlyRamsHazardMoves()
        {
            // 재배열 is RAM's own word, and a room that cannot be memorised is its 진위 구분·적응
            // identity. Giving it to another area would be the copy-paste again.
            Assert.IsTrue(AreaHazards.For(HardwareId.Ram).MovesBetweenCycles);

            Assert.IsFalse(AreaHazards.For(HardwareId.Cpu).MovesBetweenCycles);
            Assert.IsFalse(AreaHazards.For(HardwareId.Gpu).MovesBetweenCycles);
            Assert.IsFalse(AreaHazards.For(HardwareId.Ssd).MovesBetweenCycles);
            Assert.IsFalse(AreaHazards.For(HardwareId.Hdd).MovesBetweenCycles);
        }

        [Test]
        public void SsdsCycleIsTheShortestAndHddsHitsHardest()
        {
            // docs/01: SSD is 짧은 순환 장판, HDD is 낙하 파편 - fast and light against slow and
            // heavy. If these ever converge the two areas play the same.
            AreaHazardShape ssd = AreaHazards.For(HardwareId.Ssd);
            AreaHazardShape hdd = AreaHazards.For(HardwareId.Hdd);

            Assert.Less(ssd.CycleTicks, hdd.CycleTicks);
            Assert.Greater(hdd.Damage, ssd.Damage);
        }

        // --- the telegraph is the rule -------------------------------------------------------------

        [Test]
        public void EveryHazardWarnsBeforeItCanHurtAnyone()
        {
            // [확정] docs/01 2절: 바닥에 예고선이 먼저 나타나고 실제 레이저 판정은 늦게 발생.
            for (int i = 0; i < HardwareIds.All.Length; ++i)
            {
                HardwareId hardware = HardwareIds.All[i];
                if (!HardwareIds.IsArea(hardware))
                    continue;

                Assert.GreaterOrEqual(
                    AreaHazards.For(hardware).TelegraphTicks,
                    AreaHazardShape.MinTelegraphTicks, hardware.ToString());
            }
        }

        [Test]
        public void AHazardWithNoWarningIsRefused()
        {
            // Damage nobody could have avoided is the 이펙트만 반짝이고 판정 없는 것 docs/01
            // forbids with the sign flipped.
            var instant = new AreaHazardShape(
                AreaHazardKind.LaserField, 0, 20, 60, 2f, 5f, movesBetweenCycles: false);

            Assert.Throws<InvalidOperationException>(() => instant.Validate());
        }

        [Test]
        public void AHazardThatIsNeverDangerousIsRefused()
        {
            // The other half: an effect with no 판정 at all.
            var harmless = new AreaHazardShape(
                AreaHazardKind.LaserField, 40, 0, 60, 2f, 5f, movesBetweenCycles: false);
            var weightless = new AreaHazardShape(
                AreaHazardKind.LaserField, 40, 20, 60, 2f, 0f, movesBetweenCycles: false);
            var reachless = new AreaHazardShape(
                AreaHazardKind.LaserField, 40, 20, 60, 0f, 5f, movesBetweenCycles: false);

            Assert.Throws<InvalidOperationException>(() => harmless.Validate());
            Assert.Throws<InvalidOperationException>(() => weightless.Validate());
            Assert.Throws<InvalidOperationException>(() => reachless.Validate());
        }

        [Test]
        public void NoneValidatesAndIsNotAHazard()
        {
            Assert.DoesNotThrow(() => default(AreaHazardShape).Validate());
            Assert.IsTrue(default(AreaHazardShape).IsNone);
            Assert.AreEqual("없음", AreaHazards.NameOf(AreaHazardKind.None));
        }

        // --- where they stand -------------------------------------------------------------------------

        [Test]
        public void AnAreaPlacesSome()
        {
            for (int i = 0; i < HardwareIds.All.Length; ++i)
            {
                HardwareId hardware = HardwareIds.All[i];
                if (!HardwareIds.IsArea(hardware))
                    continue;

                Assert.IsNotEmpty(LayoutFor(hardware).Hazards, hardware.ToString());
            }
        }

        [Test]
        public void NoneStandOnTheEntranceOrAnObjective()
        {
            // [확정] docs/01 6절 2항: 플레이어/적 스폰과 함정 및 각 오브젝트 충돌 반경의 안전
            // 분리, 초반 화면 밖 즉시 피해 금지.
            for (uint seed = 1u; seed <= 40u; ++seed)
            {
                AreaLayout layout = LayoutFor(HardwareId.Cpu, seed);
                for (int i = 0; i < layout.Hazards.Count; ++i)
                {
                    AreaCell cell = layout.Hazards[i];
                    int clear = AreaHazards.CellsFromFixturesFor(
                        AreaHazards.For(layout.Spec.Hardware), layout.CellSizeUnits);

                    Assert.Greater(Distance(cell, layout.Start), clear,
                        $"seed {seed}: a hazard reached the entrance");
                    Assert.Greater(Distance(cell, layout.Boss), clear);
                    Assert.Greater(Distance(cell, layout.Purification), clear);
                    Assert.Greater(Distance(cell, layout.Extraction), clear);

                    for (int o = 0; o < layout.Objectives.Count; ++o)
                        Assert.Greater(Distance(cell, layout.Objectives[o]), clear);
                }
            }
        }

        [Test]
        public void NoneStandOnAnEnemySpawn()
        {
            // A patch on a sleeping enemy would kill it for the player - the area clearing
            // itself, which is not what an environment is for.
            for (uint seed = 1u; seed <= 30u; ++seed)
            {
                AreaLayout layout = LayoutFor(HardwareId.Gpu, seed);
                for (int i = 0; i < layout.Hazards.Count; ++i)
                {
                    for (int e = 0; e < layout.Enemies.Count; ++e)
                    {
                        Assert.Greater(
                            Distance(layout.Hazards[i], layout.Enemies[e].Cell),
                            AreaHazards.DraftSafeCellsFromSpawns,
                            $"seed {seed}: a hazard stood on an enemy");
                    }
                }
            }
        }

        [Test]
        public void TwoNeverOverlapIntoOne()
        {
            for (uint seed = 1u; seed <= 30u; ++seed)
            {
                AreaLayout layout = LayoutFor(HardwareId.Hdd, seed);
                for (int i = 0; i < layout.Hazards.Count; ++i)
                {
                    for (int j = i + 1; j < layout.Hazards.Count; ++j)
                    {
                        Assert.Greater(
                            Distance(layout.Hazards[i], layout.Hazards[j]),
                            AreaHazards.CellsApartFor(
                                AreaHazards.For(layout.Spec.Hardware), layout.CellSizeUnits),
                            $"seed {seed}: two hazards' reaches overlapped into one patch");
                    }
                }
            }
        }

        [Test]
        public void TheyAreOnWalkableFloor()
        {
            // A hazard inside a wall is a hazard nobody can be hurt by and nobody can see.
            AreaLayout layout = LayoutFor(HardwareId.Ssd);
            for (int i = 0; i < layout.Hazards.Count; ++i)
                Assert.IsTrue(layout.Grid.IsWalkable(layout.Hazards[i]), layout.Hazards[i].ToString());
        }

        [Test]
        public void TheSameSeedPlacesThemInTheSamePlaces()
        {
            // docs/01 6절 4항: a map has to be reproducible from its key, hazards included.
            AreaLayout first = LayoutFor(HardwareId.Ram, 77u);
            AreaLayout second = LayoutFor(HardwareId.Ram, 77u);

            CollectionAssert.AreEqual(
                new List<AreaCell>(first.Hazards), new List<AreaCell>(second.Hazards));
        }

        private static int Distance(AreaCell a, AreaCell b)
        {
            int dx = a.X - b.X;
            int dy = a.Y - b.Y;
            dx = dx < 0 ? -dx : dx;
            dy = dy < 0 ? -dy : dy;
            return dx > dy ? dx : dy;
        }
    }
}
