using System;
using System.Collections.Generic;
using NeonVoid.Missions;
using NeonVoid.Missions.Generation;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// The seed each hardware area is generated from.
    /// <para>
    /// docs/04 section 5's retry policy for <c>EnterHardware</c> is 동일 seed/genVer 재사용 and
    /// docs/01 section 6 check 4 makes <c>(seed, hardwareId, generatorVersion)</c> the key a map
    /// must be reproducible from. Both of those are claims about this derivation, so they are
    /// tested here rather than assumed by the generator's tests.
    /// </para>
    /// </summary>
    public sealed class AreaSeedsTests
    {
        [Test]
        public void OneMissionGivesEachAreaItsOwnSeed()
        {
            var seeds = new HashSet<uint>();
            for (int i = 0; i < HardwareIds.All.Length; ++i)
                Assert.IsTrue(seeds.Add(AreaSeeds.For(42u, HardwareIds.All[i])), "two areas share a seed");
        }

        [Test]
        public void TheSameMissionAndAreaAlwaysGiveTheSameSeed()
        {
            // The retry policy: 동일 seed 재사용. A seed rolled at the portal could not be re-used.
            for (uint mission = 1; mission <= 500; ++mission)
            {
                for (int i = 0; i < HardwareIds.All.Length; ++i)
                {
                    HardwareId hardware = HardwareIds.All[i];
                    Assert.AreEqual(
                        AreaSeeds.For(mission, hardware), AreaSeeds.For(mission, hardware),
                        $"mission {mission} {hardware}");
                }
            }
        }

        [Test]
        public void NeighbouringMissionsDoNotGiveNeighbouringAreaSeeds()
        {
            // An xorshift started one apart produces visibly similar early values, so CPU and GPU
            // would generate maps that felt like each other - the mistake RunIdentity documents
            // for its own streams.
            var seeds = new HashSet<uint>();
            for (uint mission = 1; mission <= 200; ++mission)
            {
                for (int i = 0; i < HardwareIds.All.Length; ++i)
                    seeds.Add(AreaSeeds.For(mission, HardwareIds.All[i]));
            }

            Assert.AreEqual(
                200 * HardwareIds.All.Length, seeds.Count,
                "two of a thousand mission/area pairs collided");
        }

        [Test]
        public void NoAreaEverGetsASeedOfZero()
        {
            // Zero is legitimate for the generator, which guards it - but two areas landing on it
            // would share a map.
            for (uint mission = 0; mission <= 5000; ++mission)
            {
                for (int i = 0; i < HardwareIds.All.Length; ++i)
                    Assert.AreNotEqual(0u, AreaSeeds.For(mission, HardwareIds.All[i]), $"mission {mission}");
            }
        }

        [Test]
        public void SomethingThatIsNotAnAreaHasNoSeed()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => AreaSeeds.For(1u, HardwareId.None));
            Assert.Throws<ArgumentOutOfRangeException>(() => AreaSeeds.For(1u, (HardwareId)42));
        }

        // --- the spec -----------------------------------------------------------------------

        [Test]
        public void TheDraftObjectiveCountStaysInsideTheDocumentsRange()
        {
            // [초안] docs/01 section 1: 2~3개 소목표.
            var counts = new HashSet<int>();

            for (uint mission = 1; mission <= 2000; ++mission)
            {
                for (int i = 0; i < HardwareIds.All.Length; ++i)
                {
                    int objectives = AreaSeeds.DraftObjectiveCountFor(mission, HardwareIds.All[i]);
                    Assert.GreaterOrEqual(objectives, HardwareIds.DraftMinObjectivesPerArea);
                    Assert.LessOrEqual(objectives, HardwareIds.DraftMaxObjectivesPerArea);
                    counts.Add(objectives);
                }
            }

            Assert.AreEqual(2, counts.Count, "the objective count never varies, so the roll is stuck");
        }

        [Test]
        public void TheDraftSpecCarriesTheAreasOwnSeedAndNominalSize()
        {
            // No size modifier: docs/01 section 6 check 5 mentions 맵 크기 축소·확대 환경 but none
            // of section 5's six [확정] kinds names a size change, so picking one would invent it.
            AreaSpec spec = AreaSeeds.DraftSpecFor(77u, HardwareId.Ssd);

            Assert.AreEqual(HardwareId.Ssd, spec.Hardware);
            Assert.AreEqual(AreaSeeds.For(77u, HardwareId.Ssd), spec.Seed);
            Assert.AreEqual(AreaSpec.NominalSizeModifierPercent, spec.SizeModifierPercent);
            Assert.AreEqual(AreaSpec.CurrentGeneratorVersion, spec.GeneratorVersion);
        }

        [Test]
        public void EveryAreaOfEveryMissionGeneratesAMapThatPasses()
        {
            // The derivation is only useful if what it produces is buildable. Fewer missions than
            // the generator's own sweep, because this is five maps each.
            for (uint mission = 1; mission <= 40; ++mission)
            {
                for (int i = 0; i < HardwareIds.All.Length; ++i)
                {
                    HardwareId hardware = HardwareIds.All[i];
                    AreaSpec spec = AreaSeeds.DraftSpecFor(mission, hardware);

                    Assert.IsTrue(
                        AreaGenerator.TryGenerate(spec, out AreaLayout layout, out string failure),
                        $"mission {mission} {hardware}: {failure}");
                    Assert.IsTrue(
                        AreaLayoutValidator.Validate(layout).IsValid,
                        $"mission {mission} {hardware}");
                }
            }
        }

        [Test]
        public void TheFiveAreasOfOneMissionAreFiveDifferentMaps()
        {
            // CLAUDE.md: 5개 하드웨어를 한 개의 복붙 맵으로 취급하지 말 것. The archetype mixes
            // differ by hardware; so must the terrain.
            var bossCells = new HashSet<string>();

            for (int i = 0; i < HardwareIds.All.Length; ++i)
            {
                AreaSpec spec = AreaSeeds.DraftSpecFor(9u, HardwareIds.All[i]);
                Assert.IsTrue(AreaGenerator.TryGenerate(spec, out AreaLayout layout, out string failure), failure);
                bossCells.Add($"{layout.Boss}:{layout.Grid.WalkableCount}");
            }

            Assert.AreEqual(HardwareIds.All.Length, bossCells.Count, "two areas of one mission are the same map");
        }
    }
}
