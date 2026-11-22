using NeonVoid.Missions;
using NeonVoid.Missions.Generation;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// What an environment does to an area.
    /// <para>
    /// [확정] docs/00 section 7 names the six and their effects in the same sentence:
    /// 스택오버플로우(적 증가), 터널감염(미학습 소지 플러그인 1~2개 비활성/역효과), 확산(맵 확대),
    /// 초기상태(맵 축소). The board had been rolling one per hardware and showing it on the gate
    /// while nothing read it - six labels that played identically.
    /// </para>
    /// </summary>
    public sealed class EnvironmentEffectsTests
    {
        private const uint Seed = 0x5EED_1234u;

        private static AreaSpec Nominal(HardwareId hardware = HardwareId.Cpu) =>
            new AreaSpec(hardware, Seed, objectiveCount: 2);

        private static EnvironmentEffects Of(EnvironmentKind kind) =>
            EnvironmentEffects.For(kind, Seed);

        // --- each kind does what the document says -----------------------------------------------

        [Test]
        public void SpreadMakesTheMapBigger()
        {
            EnvironmentEffects spread = Of(EnvironmentKind.Spread);

            Assert.Greater(spread.SizePercent, EnvironmentEffects.NominalPercent);
            Assert.AreEqual(EnvironmentEffects.NominalPercent, spread.EnemyPercent);
            Assert.Greater(
                spread.ApplyTo(Nominal()).SizeModifierPercent, Nominal().SizeModifierPercent);
        }

        [Test]
        public void InitialStateMakesItSmaller()
        {
            EnvironmentEffects shrunk = Of(EnvironmentKind.InitialState);

            Assert.Less(shrunk.SizePercent, EnvironmentEffects.NominalPercent);
            Assert.Less(
                shrunk.ApplyTo(Nominal()).SizeModifierPercent, Nominal().SizeModifierPercent);
        }

        [Test]
        public void StackOverflowAddsEnemiesWithoutChangingTheMap()
        {
            // 적 증가 is more enemies in the same rooms. A bigger map that happens to hold more
            // is 확산, and the two must not collapse into one effect.
            EnvironmentEffects stacked = Of(EnvironmentKind.StackOverflow);

            Assert.AreEqual(EnvironmentEffects.NominalPercent, stacked.SizePercent);
            Assert.Greater(stacked.EnemyPercent, EnvironmentEffects.NominalPercent);

            AreaSpec applied = stacked.ApplyTo(Nominal());
            Assert.AreEqual(Nominal().SizeModifierPercent, applied.SizeModifierPercent);
            Assert.Greater(
                AreaGenerator.NormalEnemiesFor(applied), AreaGenerator.NormalEnemiesFor(Nominal()));
        }

        [Test]
        public void TunnelInfectionDoesNothingToTheAreaAndIsStillUnbuilt()
        {
            // [확정] docs/00: 터널감염 disables 미학습 소지 플러그인 1~2개 - a change to the
            // player, not to the map. This build has no way to switch a carried card off for a
            // while, so the effect is not built; a count nothing could act on is not returned
            // either, because that is the failure this type exists to fix.
            EnvironmentEffects infection = Of(EnvironmentKind.TunnelInfection);

            Assert.AreEqual(EnvironmentEffects.NominalPercent, infection.SizePercent);
            Assert.AreEqual(EnvironmentEffects.NominalPercent, infection.EnemyPercent);
            Assert.AreEqual(Nominal().GenerationKey, infection.ApplyTo(Nominal()).GenerationKey);
        }

        [Test]
        public void ClearChangesNothing()
        {
            Assert.IsFalse(Of(EnvironmentKind.Clear).ChangesAnything);
            Assert.AreEqual(Nominal().GenerationKey, Of(EnvironmentKind.Clear).ApplyTo(Nominal()).GenerationKey);
        }

        [Test]
        public void MutationIsLeftToItsOwnImplementation()
        {
            // AreaMutation already does 변이발생, it is the one environment with a level, and a
            // second definition here would drift from the first.
            Assert.IsFalse(Of(EnvironmentKind.Mutation).ChangesAnything);
        }

        // --- the four that change something are four different things -----------------------------

        [Test]
        public void TheSixAreNotFourCopiesOfEachOther()
        {
            // The failure this fixes: a board offering six labels that all played identically.
            var keys = new System.Collections.Generic.HashSet<string>();
            foreach (EnvironmentKind kind in System.Enum.GetValues(typeof(EnvironmentKind)))
                keys.Add(Of(kind).ApplyTo(Nominal()).GenerationKey);

            // Clear, Mutation and TunnelInfection all build the same map - two of them
            // deliberately do nothing to it - so four distinct keys is the honest count.
            Assert.AreEqual(4, keys.Count, "two environments that change the map built the same one");
        }

        [Test]
        public void TheGenerationKeyCarriesTheEnvironmentsEffect()
        {
            // docs/01 section 6 check 4 wants a map reproducible from its key. An environment
            // that changed the map without changing the key would make the key a lie.
            string nominal = Nominal().GenerationKey;
            Assert.AreNotEqual(nominal, Of(EnvironmentKind.Spread).ApplyTo(Nominal()).GenerationKey);
            Assert.AreNotEqual(nominal, Of(EnvironmentKind.StackOverflow).ApplyTo(Nominal()).GenerationKey);
            StringAssert.Contains("pop", nominal);
        }

        // --- it stays inside what the generator can build -------------------------------------------

        [Test]
        public void ASpreadAreaIsClampedToWhatTheGeneratorCanBuild()
        {
            // The band exists because a boss room and an entrance do not fit in an arbitrarily
            // small grid. An environment is not a good enough reason to hand it a size it
            // cannot honour.
            AreaSpec big = new AreaSpec(
                HardwareId.Gpu, Seed, 2, AreaSpec.DraftMaxSizeModifierPercent);
            AreaSpec spread = Of(EnvironmentKind.Spread).ApplyTo(big);

            Assert.AreEqual(AreaSpec.DraftMaxSizeModifierPercent, spread.SizeModifierPercent);
        }

        [Test]
        public void AShrunkAreaIsClampedToo()
        {
            AreaSpec small = new AreaSpec(
                HardwareId.Hdd, Seed, 2, AreaSpec.DraftMinSizeModifierPercent);
            AreaSpec shrunk = Of(EnvironmentKind.InitialState).ApplyTo(small);

            Assert.AreEqual(AreaSpec.DraftMinSizeModifierPercent, shrunk.SizeModifierPercent);
        }

        [Test]
        public void EveryEnvironmentStillBuildsAValidArea()
        {
            // The whole point of clamping. An environment that produced an area the generator
            // refuses would shut a portal the board had just offered.
            foreach (EnvironmentKind kind in System.Enum.GetValues(typeof(EnvironmentKind)))
            {
                for (int h = 0; h < HardwareIds.All.Length; ++h)
                {
                    HardwareId hardware = HardwareIds.All[h];
                    if (!HardwareIds.IsArea(hardware))
                        continue;

                    AreaSpec spec = EnvironmentEffects.For(kind, Seed).ApplyTo(Nominal(hardware));
                    Assert.IsTrue(
                        AreaGenerator.TryGenerate(spec, out AreaLayout _, out string failure),
                        $"{hardware} under {kind}: {failure}");
                }
            }
        }

        // --- the seeds apply it ----------------------------------------------------------------------

        [Test]
        public void TheSpecFromTheBoardCarriesTheEnvironment()
        {
            var spread = new AreaConditions(
                HardwareId.Ssd, HardwareDifficulty.Normal, EnvironmentKind.Spread, 0);
            var clear = AreaConditions.Clear(HardwareId.Ssd, HardwareDifficulty.Normal);

            AreaSpec withSpread = AreaSeeds.DraftSpecFor(Seed, HardwareId.Ssd, spread);
            AreaSpec withNothing = AreaSeeds.DraftSpecFor(Seed, HardwareId.Ssd, clear);

            Assert.Greater(withSpread.SizeModifierPercent, withNothing.SizeModifierPercent);
            Assert.AreEqual(withSpread.Seed, withNothing.Seed, "the environment changed the seed");
            Assert.AreEqual(
                withSpread.ObjectiveCount, withNothing.ObjectiveCount,
                "the environment changed how many 소목표 the area asks for");
        }

        [Test]
        public void TheSameBoardBuildsTheSameAreaTwice()
        {
            var conditions = new AreaConditions(
                HardwareId.Ram, HardwareDifficulty.Hard, EnvironmentKind.StackOverflow, 0);

            Assert.AreEqual(
                AreaSeeds.DraftSpecFor(Seed, HardwareId.Ram, conditions).GenerationKey,
                AreaSeeds.DraftSpecFor(Seed, HardwareId.Ram, conditions).GenerationKey);
        }
    }
}
