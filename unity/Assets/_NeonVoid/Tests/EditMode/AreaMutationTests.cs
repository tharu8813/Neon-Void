using System;
using System.Collections.Generic;
using NeonVoid.Missions;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// 변이발생, the one environment with a [확정] effect that can be built today.
    /// <para>
    /// docs/01 section 5: 변이발생: 일부 개체 능력 상승/일반·레어 플러그인 1~2개 획득. The plugin
    /// half waits on a reward commit (D-006, D-007). What these hold is the first half, and
    /// mostly the word 일부: an environment that boosted everything would be difficulty, and
    /// docs/01 section 3 leaves 난이도에 따른 HP/피해 배율 [미확정] exactly so that nobody ships
    /// it sideways.
    /// </para>
    /// </summary>
    public sealed class AreaMutationTests
    {
        private static bool[] Normals(int count) => new bool[count];

        private static bool[] WithElites(int normals, int elites)
        {
            var flags = new bool[normals + elites];
            for (int i = normals; i < flags.Length; ++i)
                flags[i] = true;
            return flags;
        }

        private static int CountTrue(bool[] flags)
        {
            int n = 0;
            for (int i = 0; i < flags.Length; ++i)
            {
                if (flags[i])
                    ++n;
            }

            return n;
        }

        // --- 일부 ----------------------------------------------------------------------------

        [Test]
        public void AnAreaWithNoMutationHasNoMutants()
        {
            bool[] mutated = AreaMutation.Choose(1234u, Normals(48), AreaMutation.NoMutation);
            Assert.AreEqual(0, CountTrue(mutated));
            Assert.AreEqual(1f, AreaMutation.HealthMultiplier(AreaMutation.NoMutation));
            Assert.AreEqual(1f, AreaMutation.DamageMultiplier(AreaMutation.NoMutation));
        }

        [Test]
        public void EveryLevelLeavesMostOfTheRoomAlone()
        {
            // 일부 개체. The draft curve stops at half, and the hard cap stops a retune from
            // quietly turning 일부 into 전부.
            for (int level = AreaConditions.MinMutationLevel; level <= AreaConditions.MaxMutationLevel; ++level)
            {
                int count = AreaMutation.MutatedCount(48, level);
                Assert.Greater(count, 0, $"Lv.{level} mutated nothing");
                Assert.LessOrEqual(
                    count * 100, 48 * AreaMutation.MaxMutatedPercent,
                    $"Lv.{level} mutated {count} of 48, which is not 일부");
            }
        }

        [Test]
        public void AHigherLevelMutatesMoreOfTheRoom()
        {
            int previous = 0;
            for (int level = AreaConditions.MinMutationLevel; level <= AreaConditions.MaxMutationLevel; ++level)
            {
                int count = AreaMutation.MutatedCount(48, level);
                Assert.Greater(count, previous, $"Lv.{level} mutated no more than Lv.{level - 1}");
                previous = count;
            }
        }

        [Test]
        public void AnnouncedMutationAlwaysMutatesSomething()
        {
            // A portal that says 변이발생 Lv.1 over a room with nothing mutated in it is the
            // portal telling the player something untrue.
            for (int enemies = 1; enemies <= 12; ++enemies)
            {
                Assert.GreaterOrEqual(
                    AreaMutation.MutatedCount(enemies, AreaConditions.MinMutationLevel), 1,
                    $"{enemies} enemies at Lv.1");
            }
        }

        [Test]
        public void TheCountIsExactRatherThanARollPerEnemy()
        {
            // A per-enemy roll would make "Lv.3 mutated nothing this time" a thing that happens
            // to one player in a hundred, which is a bug report nobody can reproduce.
            for (uint seed = 1u; seed <= 300u; ++seed)
            {
                bool[] mutated = AreaMutation.Choose(seed, Normals(48), 3);
                Assert.AreEqual(AreaMutation.MutatedCount(48, 3), CountTrue(mutated), $"seed {seed}");
            }
        }

        // --- 정예 ----------------------------------------------------------------------------

        [Test]
        public void ElitesAreNeverMutatedOnTop()
        {
            // docs/01 section 4 already calls enemy.elite 환경 변이·강인함 - the elite IS the
            // mutated one. Stacking would multiply the same idea twice.
            bool[] isElite = WithElites(48, 3);
            bool[] mutated = AreaMutation.Choose(77u, isElite, AreaConditions.MaxMutationLevel);

            for (int i = 0; i < isElite.Length; ++i)
            {
                if (isElite[i])
                    Assert.IsFalse(mutated[i], $"the 정예 at {i} was mutated as well");
            }
        }

        [Test]
        public void TheShareIsOfTheNormalsAndNotOfEverything()
        {
            bool[] isElite = WithElites(48, 3);
            bool[] mutated = AreaMutation.Choose(77u, isElite, 2);

            Assert.AreEqual(AreaMutation.MutatedCount(48, 2), CountTrue(mutated));
        }

        // --- reproducibility -----------------------------------------------------------------

        [Test]
        public void TheSameAreaAndLevelAlwaysMutateTheSameEnemies()
        {
            // docs/01 section 6 check 4. The environment is a separate decision from the
            // terrain, and it has to be just as reproducible.
            for (uint seed = 1u; seed <= 200u; ++seed)
            {
                CollectionAssert.AreEqual(
                    AreaMutation.Choose(seed, Normals(30), 4),
                    AreaMutation.Choose(seed, Normals(30), 4),
                    $"seed {seed}");
            }
        }

        [Test]
        public void TwoLevelsOfOneSeedAreNotNestedSubsets()
        {
            // If Lv.2 were Lv.1 plus one more enemy, the environment would read as a dial on
            // the same room rather than a different condition.
            int identical = 0;
            for (uint seed = 1u; seed <= 200u; ++seed)
            {
                bool[] low = AreaMutation.Choose(seed, Normals(48), 1);
                bool[] high = AreaMutation.Choose(seed, Normals(48), 2);

                bool nested = true;
                for (int i = 0; i < low.Length && nested; ++i)
                {
                    if (low[i] && !high[i])
                        nested = false;
                }

                if (nested)
                    ++identical;
            }

            // A handful by chance is fine; every one of them would mean the levels share a
            // stream.
            Assert.Less(identical, 40, $"{identical} of 200 seeds had Lv.1 as a subset of Lv.2");
        }

        [Test]
        public void TwoAreasDoNotMutateTheSameEnemies()
        {
            var seen = new HashSet<string>();
            for (uint seed = 1u; seed <= 200u; ++seed)
                seen.Add(string.Join(string.Empty, Array.ConvertAll(AreaMutation.Choose(seed, Normals(30), 3), b => b ? "1" : "0")));

            Assert.Greater(seen.Count, 190, "two hundred seeds produced only " + seen.Count + " arrangements");
        }

        // --- the numbers ---------------------------------------------------------------------

        [Test]
        public void AMutantIsStrongerThanANormalAndWeakerThanAnElite()
        {
            // A mutated normal is a harder normal, not a second elite - the pacing budget in
            // docs/01 section 7 has three enemy weights in it, not four.
            float topMutation = AreaMutation.HealthMultiplier(AreaConditions.MaxMutationLevel);

            Assert.Greater(topMutation, 1f);
            Assert.Less(topMutation, 2.5f, "a Lv.5 mutant is as tough as the 정예 multiplier");
            Assert.Greater(
                AreaMutation.HealthMultiplier(5), AreaMutation.HealthMultiplier(1),
                "the level does not change how strong a mutant is");
        }

        [Test]
        public void ALevelOutsideTheDocumentsRangeIsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => AreaMutation.HealthMultiplier(6));
            Assert.Throws<ArgumentOutOfRangeException>(() => AreaMutation.MutatedCount(10, 9));
            Assert.Throws<ArgumentOutOfRangeException>(() => AreaMutation.Choose(1u, Normals(10), 42));
            Assert.Throws<ArgumentNullException>(() => AreaMutation.Choose(1u, null, 1));
        }

        [Test]
        public void OnlyMutationCarriesALevel()
        {
            Assert.AreEqual(
                3, AreaMutation.LevelOf(new AreaConditions(
                    HardwareId.Cpu, HardwareDifficulty.Normal, EnvironmentKind.Mutation, 3)));

            Assert.AreEqual(
                AreaMutation.NoMutation,
                AreaMutation.LevelOf(AreaConditions.Clear(HardwareId.Gpu, HardwareDifficulty.Hard)));

            Assert.AreEqual(
                AreaMutation.NoMutation,
                AreaMutation.LevelOf(new AreaConditions(
                    HardwareId.Ram, HardwareDifficulty.Easy, EnvironmentKind.Spread, 0)));
        }
    }
}
