using System;
using System.Collections.Generic;
using NeonVoid.Core;
using NeonVoid.Missions;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// docs/01 section 5's 쉬움 보장 알고리즘 테스트 계약, run as the document writes it.
    /// <para>
    /// The contract is unusually precise for this project, so it is worth quoting: <i>missionSeed로
    /// 5개 하드웨어 난이도 선정 → 결과에 Easy가 하나도 없으면 결정적인 규칙으로 1개를 Easy로
    /// 보정(어느 하드웨어인지는 시드 RNG로 선택). 10,000개 시드 [초안 테스트 수]에서 최소 1개 항상
    /// 통과해야 함. 난이도를 전부 Easy로 강제하지 않음.</i> All four clauses are checked, including
    /// the last one - a guarantee implemented as "make everything Easy" would satisfy the first
    /// three.
    /// </para>
    /// </summary>
    public sealed class MissionBoardTests
    {
        /// <summary>The document's own [초안 테스트 수].</summary>
        private const int ContractSeeds = 10_000;

        // --- the [확정] shape -------------------------------------------------------------

        [Test]
        public void ThereAreExactlySixEnvironmentKinds()
        {
            // [확정] docs/01 section 5: 이상없음, 변이발생, 스택오버플로우, 터널감염, 확산,
            // 초기상태. R-003 in docs/08 names 환경 6분류 as a confirmed requirement, so a seventh
            // invented as a tuning convenience would be a new approved rule nobody approved.
            Assert.AreEqual(6, Enum.GetValues(typeof(EnvironmentKind)).Length);

            Assert.IsTrue(Enum.IsDefined(typeof(EnvironmentKind), EnvironmentKind.Clear));
            Assert.IsTrue(Enum.IsDefined(typeof(EnvironmentKind), EnvironmentKind.Mutation));
            Assert.IsTrue(Enum.IsDefined(typeof(EnvironmentKind), EnvironmentKind.StackOverflow));
            Assert.IsTrue(Enum.IsDefined(typeof(EnvironmentKind), EnvironmentKind.TunnelInfection));
            Assert.IsTrue(Enum.IsDefined(typeof(EnvironmentKind), EnvironmentKind.Spread));
            Assert.IsTrue(Enum.IsDefined(typeof(EnvironmentKind), EnvironmentKind.InitialState));
        }

        [Test]
        public void ThereAreThreeDifficultiesUntilTheDocumentSaysOtherwise()
        {
            // 난이도 3·4·5단계 여부 is [미확정] in docs/01 section 5. Three tiers is the smallest
            // thing that can carry the [확정] Easy guarantee, and this test is what makes adding
            // a fourth a deliberate act.
            Assert.AreEqual(3, Enum.GetValues(typeof(HardwareDifficulty)).Length);
        }

        [Test]
        public void ABoardCoversEveryHardwareAreaExactlyOnce()
        {
            MissionBoardPlan plan = MissionBoard.Select(12345u);

            Assert.AreEqual(DesignConstants.HardwareCount, plan.Count);

            var seen = new HashSet<HardwareId>();
            for (int i = 0; i < plan.Count; ++i)
                Assert.IsTrue(seen.Add(plan[i].Hardware), $"{plan[i].Hardware} is on the board twice");

            for (int i = 0; i < HardwareIds.All.Length; ++i)
                Assert.IsTrue(seen.Contains(HardwareIds.All[i]), $"{HardwareIds.All[i]} is not on the board");
        }

        // --- the contract: 10,000 seeds ---------------------------------------------------

        [Test]
        public void EveryOneOfTenThousandSeedsOffersAtLeastOneEasyArea()
        {
            // [확정] R-003 and docs/01 section 5. The one clause that must never fail.
            for (uint seed = 1; seed <= ContractSeeds; ++seed)
            {
                MissionBoardPlan plan = MissionBoard.Select(seed);

                Assert.GreaterOrEqual(
                    plan.EasyCount, 1, $"seed {seed} offered no Easy area: {plan}");
                Assert.IsTrue(MissionBoard.KeepsTheEasyGuarantee(plan), $"seed {seed}");
            }
        }

        [Test]
        public void TheGuaranteeIsNotImplementedByMakingEverythingEasy()
        {
            // 난이도를 전부 Easy로 강제하지 않음. A board that forced all five would satisfy the
            // clause above perfectly, which is why the document says this separately.
            var easyCounts = new Dictionary<int, int>();
            int allEasy = 0;

            for (uint seed = 1; seed <= ContractSeeds; ++seed)
            {
                MissionBoardPlan plan = MissionBoard.Select(seed);
                easyCounts.TryGetValue(plan.EasyCount, out int count);
                easyCounts[plan.EasyCount] = count + 1;

                if (plan.EasyCount == DesignConstants.HardwareCount)
                    ++allEasy;
            }

            Assert.Greater(easyCounts.Count, 2, $"the number of Easy areas barely varies: {Describe(easyCounts)}");
            Assert.Less(
                allEasy, ContractSeeds / 20,
                $"{allEasy} of {ContractSeeds} boards were entirely Easy; the guarantee is forcing them");
            Assert.Greater(
                easyCounts.ContainsKey(1) ? easyCounts[1] : 0, 0,
                "no board ever offered exactly one Easy area");
        }

        [Test]
        public void TheCorrectionTouchesExactlyOneArea()
        {
            // 1개를 Easy로 보정. A mission that rolled four Hard areas keeps all four.
            int corrected = 0;

            for (uint seed = 1; seed <= ContractSeeds; ++seed)
            {
                MissionBoardPlan plan = MissionBoard.Select(seed);
                if (!plan.WasCorrected)
                    continue;

                ++corrected;
                Assert.AreEqual(
                    1, plan.EasyCount,
                    $"seed {seed} was corrected and ended up with {plan.EasyCount} Easy areas: {plan}");
                Assert.AreEqual(
                    HardwareDifficulty.Easy, plan[plan.CorrectedIndex].Difficulty,
                    $"seed {seed} reported a correction that did not happen");
            }

            Assert.Greater(corrected, 0, "the correction never ran in 10,000 seeds, so it is untested");
            Assert.Less(
                corrected, ContractSeeds / 2,
                $"{corrected} of {ContractSeeds} boards needed correcting; the natural Easy rate is too low");
        }

        [Test]
        public void WhichAreaIsCorrectedComesFromTheSeed()
        {
            // 어느 하드웨어인지는 시드 RNG로 선택. A rule that always corrected the CPU would be
            // deterministic too, and would make the first area Easy for the rest of the game.
            var correctedHardware = new HashSet<HardwareId>();

            for (uint seed = 1; seed <= ContractSeeds; ++seed)
            {
                MissionBoardPlan plan = MissionBoard.Select(seed);
                if (plan.WasCorrected)
                    correctedHardware.Add(plan[plan.CorrectedIndex].Hardware);
            }

            Assert.AreEqual(
                DesignConstants.HardwareCount, correctedHardware.Count,
                "the correction never fell on some of the hardware areas");
        }

        [Test]
        public void AnUncorrectedBoardSaysSo()
        {
            int uncorrected = 0;
            for (uint seed = 1; seed <= ContractSeeds && uncorrected < 1; ++seed)
            {
                MissionBoardPlan plan = MissionBoard.Select(seed);
                if (plan.WasCorrected)
                    continue;

                ++uncorrected;
                Assert.AreEqual(-1, plan.CorrectedIndex);
                Assert.GreaterOrEqual(plan.EasyCount, 1, "it passed without a correction and without an Easy area");
            }

            Assert.AreEqual(1, uncorrected, "every board needed correcting");
        }

        // --- determinism ------------------------------------------------------------------

        [Test]
        public void TheSameSeedAlwaysOffersTheSameBoard()
        {
            // The motherboard panel, the hardware gates and whatever later records the mission all
            // have to agree about what was offered.
            for (uint seed = 1; seed <= 500; ++seed)
            {
                MissionBoardPlan first = MissionBoard.Select(seed);
                MissionBoardPlan second = MissionBoard.Select(seed);

                Assert.AreEqual(first.CorrectedIndex, second.CorrectedIndex, $"seed {seed}");
                for (int i = 0; i < first.Count; ++i)
                {
                    Assert.AreEqual(first[i].Hardware, second[i].Hardware, $"seed {seed} area {i}");
                    Assert.AreEqual(first[i].Difficulty, second[i].Difficulty, $"seed {seed} area {i}");
                    Assert.AreEqual(first[i].Environment, second[i].Environment, $"seed {seed} area {i}");
                    Assert.AreEqual(first[i].MutationLevel, second[i].MutationLevel, $"seed {seed} area {i}");
                }
            }
        }

        [Test]
        public void DifferentSeedsOfferDifferentBoards()
        {
            var boards = new HashSet<string>();
            for (uint seed = 1; seed <= 500; ++seed)
                boards.Add(MissionBoard.Select(seed).ToString());

            Assert.Greater(boards.Count, 400, "five hundred seeds produced too few distinct boards");
        }

        [Test]
        public void ASeedOfZeroStillOffersABoard()
        {
            MissionBoardPlan plan = MissionBoard.Select(0u);

            Assert.AreEqual(DesignConstants.HardwareCount, plan.Count);
            Assert.GreaterOrEqual(plan.EasyCount, 1);
        }

        // --- the environments -------------------------------------------------------------

        [Test]
        public void EveryEnvironmentKindActuallyComesUp()
        {
            // A weight table with a zero in it would silently remove one of six [확정] kinds from
            // the game.
            var seen = new HashSet<EnvironmentKind>();
            for (uint seed = 1; seed <= 2000; ++seed)
            {
                MissionBoardPlan plan = MissionBoard.Select(seed);
                for (int i = 0; i < plan.Count; ++i)
                    seen.Add(plan[i].Environment);
            }

            Assert.AreEqual(6, seen.Count, "some environment kind never appears");
        }

        [Test]
        public void OnlyAMutationCarriesALevelAndItIsAlwaysOneToFive()
        {
            // [확정] 변이발생 Lv.1~5. A level on anything else is a number a gate would display
            // and nothing would mean.
            var levelsSeen = new HashSet<int>();

            for (uint seed = 1; seed <= ContractSeeds; ++seed)
            {
                MissionBoardPlan plan = MissionBoard.Select(seed);
                for (int i = 0; i < plan.Count; ++i)
                {
                    AreaConditions area = plan[i];
                    if (area.Environment == EnvironmentKind.Mutation)
                    {
                        Assert.GreaterOrEqual(area.MutationLevel, AreaConditions.MinMutationLevel, $"seed {seed}");
                        Assert.LessOrEqual(area.MutationLevel, AreaConditions.MaxMutationLevel, $"seed {seed}");
                        levelsSeen.Add(area.MutationLevel);
                    }
                    else
                    {
                        Assert.AreEqual(0, area.MutationLevel, $"seed {seed}: {area.Environment} has a level");
                    }
                }
            }

            Assert.AreEqual(5, levelsSeen.Count, "not every 변이 단계 appears");
        }

        [Test]
        public void AnAreaHoldsOneEnvironmentAndNotASet()
        {
            // 환경 상호배타/중첩 is [미확정], so stacking two is a rule nobody approved - and the
            // restrictive reading cannot surprise a player with two penalties at once. This test
            // is the reminder that widening it is a decision.
            Assert.AreEqual(
                typeof(EnvironmentKind), typeof(AreaConditions).GetField("Environment").FieldType,
                "the environment became a collection without the decision being made");
        }

        [Test]
        public void AnImpossibleConditionCannotBeBuilt()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new AreaConditions(HardwareId.None, HardwareDifficulty.Easy, EnvironmentKind.Clear, 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new AreaConditions(HardwareId.Cpu, HardwareDifficulty.Easy, EnvironmentKind.Mutation, 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new AreaConditions(HardwareId.Cpu, HardwareDifficulty.Easy, EnvironmentKind.Mutation, 6));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new AreaConditions(HardwareId.Cpu, HardwareDifficulty.Easy, EnvironmentKind.Clear, 3));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new AreaConditions(HardwareId.Cpu, (HardwareDifficulty)9, EnvironmentKind.Clear, 0));
        }

        // --- the gate line ----------------------------------------------------------------

        [Test]
        public void TheGateLineNamesTheEnvironmentAndTheDifficulty()
        {
            // [초안] docs/01 section 5 asks the hardware gate for 환경 명칭/영향/대상/복구 가능
            // 여부/구역 난이도. Two of the five exist; the other three are the environment's
            // effects, and no environment has one yet.
            var mutated = new AreaConditions(
                HardwareId.Gpu, HardwareDifficulty.Hard, EnvironmentKind.Mutation, 4);
            string line = mutated.GateLine();

            StringAssert.Contains("Gpu", line);
            StringAssert.Contains("Hard", line);
            StringAssert.Contains("Mutation", line);
            StringAssert.Contains("Lv.4", line);

            string clear = AreaConditions.Clear(HardwareId.Cpu, HardwareDifficulty.Easy).GateLine();
            StringAssert.DoesNotContain("Lv.", clear, "a non-mutation showed a level");
        }

        [Test]
        public void TheCorrectedAreaIsNamedInTheBoardsOwnDescription()
        {
            for (uint seed = 1; seed <= 200; ++seed)
            {
                MissionBoardPlan plan = MissionBoard.Select(seed);
                if (!plan.WasCorrected)
                    continue;

                StringAssert.Contains("쉬움 보장", plan.ToString());
                return;
            }

            Assert.Fail("no corrected board in 200 seeds");
        }

        private static string Describe(Dictionary<int, int> easyCounts)
        {
            var parts = new List<string>();
            foreach (KeyValuePair<int, int> pair in easyCounts)
                parts.Add($"{pair.Value} board(s) with {pair.Key} Easy");

            return string.Join(", ", parts.ToArray());
        }
    }
}
