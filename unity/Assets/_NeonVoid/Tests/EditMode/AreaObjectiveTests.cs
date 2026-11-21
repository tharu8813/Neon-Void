using System;
using System.Collections.Generic;
using NeonVoid.Missions;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// [초안] the per-hardware 소목표 table, against docs/01 section 2's 핵심 동사 column and
    /// CLAUDE.md's rule that the five areas are not one copy-pasted map.
    /// <para>
    /// The point of these is not the numbers, which D-015 owns and will retune. It is that the
    /// five hardware keep asking for <b>different things</b>, and that nothing in the table can
    /// produce an area that cannot be finished.
    /// </para>
    /// </summary>
    public sealed class AreaObjectiveTests
    {
        private const int Min = HardwareIds.DraftMinObjectivesPerArea;
        private const int Max = HardwareIds.DraftMaxObjectivesPerArea;

        // --- the table -----------------------------------------------------------------------

        [Test]
        public void EveryHardwareOffersThreeNamedCandidates()
        {
            foreach (HardwareId hardware in HardwareIds.All)
            {
                AreaObjectiveSpec[] candidates = AreaObjectives.CandidatesFor(hardware);

                Assert.AreEqual(
                    Max, candidates.Length,
                    $"{hardware}: docs/01 section 2 gives every area 목표 후보 3종");

                var names = new HashSet<string>();
                for (int i = 0; i < candidates.Length; ++i)
                {
                    Assert.IsFalse(
                        string.IsNullOrWhiteSpace(candidates[i].Name), $"{hardware} objective {i} has no name");
                    Assert.IsTrue(
                        names.Add(candidates[i].Name),
                        $"{hardware} lists {candidates[i].Name} twice");
                }
            }
        }

        [Test]
        public void NoTwoHardwareAskForTheSameThing()
        {
            // The whole reason the table exists. A signature rather than just the kinds, because
            // GPU and SSD both walk a chain and what separates them is SSD's clock - docs/01
            // gives SSD 짧은 시간에 연결 and GPU 공간 읽기.
            var signatures = new Dictionary<string, HardwareId>();

            foreach (HardwareId hardware in HardwareIds.All)
            {
                string signature = Signature(hardware);
                Assert.IsFalse(
                    signatures.TryGetValue(signature, out HardwareId other),
                    $"{hardware} asks exactly what {other} asks: {signature}");

                signatures[signature] = hardware;
            }
        }

        [Test]
        public void NoObjectiveNameIsUsedByTwoHardware()
        {
            // A name that appeared in two areas would be the copy-paste showing through the one
            // part of this the player actually reads.
            var owners = new Dictionary<string, HardwareId>();

            foreach (HardwareId hardware in HardwareIds.All)
            {
                AreaObjectiveSpec[] candidates = AreaObjectives.CandidatesFor(hardware);
                for (int i = 0; i < candidates.Length; ++i)
                {
                    string name = candidates[i].Name;
                    Assert.IsFalse(
                        owners.TryGetValue(name, out HardwareId other),
                        $"{hardware} and {other} both call an objective {name}");

                    owners[name] = hardware;
                }
            }
        }

        [Test]
        public void NoAreaEverGetsAChainOfOne()
        {
            // A chain of one node is a Hold with extra words - it has no order to get wrong -
            // and an area whose chain was one node would quietly lose its hardware's verb. What
            // keeps it from happening is the order of the candidates, so it is asserted over
            // every count an area can be built with rather than assumed.
            foreach (HardwareId hardware in HardwareIds.All)
            {
                for (int count = Min; count <= Max; ++count)
                {
                    int length = AreaObjectives.ChainLengthFor(hardware, count);
                    Assert.AreNotEqual(
                        1, length, $"{hardware} with {count} objective(s) has a chain of one node");
                }
            }
        }

        [Test]
        public void ChainNodesAreNumberedFromZeroInOrder()
        {
            // The area runtime builds its chain from the nodes that are present and offers each
            // node by its own index, so a gap or a repeat would be a chain that can never land.
            foreach (HardwareId hardware in HardwareIds.All)
            {
                for (int count = Min; count <= Max; ++count)
                {
                    AreaObjectiveSpec[] specs = AreaObjectives.For(hardware, count);
                    int expected = 0;

                    for (int i = 0; i < specs.Length; ++i)
                    {
                        if (specs[i].Kind != AreaObjectiveKind.Sequence)
                        {
                            Assert.AreEqual(-1, specs[i].ChainIndex, $"{specs[i].Name} is not a chain node");
                            continue;
                        }

                        Assert.AreEqual(
                            expected, specs[i].ChainIndex,
                            $"{hardware}: {specs[i].Name} is out of place in the chain");
                        ++expected;
                    }

                    Assert.AreEqual(
                        AreaObjectives.ChainLengthFor(hardware, count), expected,
                        $"{hardware} with {count}: the chain length and the nodes disagree");
                }
            }
        }

        [Test]
        public void AnAreaTakesTheFirstCandidatesInOrder()
        {
            foreach (HardwareId hardware in HardwareIds.All)
            {
                AreaObjectiveSpec[] candidates = AreaObjectives.CandidatesFor(hardware);

                for (int count = 1; count <= candidates.Length; ++count)
                {
                    AreaObjectiveSpec[] taken = AreaObjectives.For(hardware, count);
                    Assert.AreEqual(count, taken.Length);

                    for (int i = 0; i < count; ++i)
                        Assert.AreEqual(candidates[i].Name, taken[i].Name, $"{hardware} objective {i}");
                }
            }
        }

        [Test]
        public void AnAreaCannotAskForMoreObjectivesThanItHasCandidates()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => AreaObjectives.For(HardwareId.Cpu, Max + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => AreaObjectives.For(HardwareId.Cpu, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => AreaObjectives.CandidatesFor(HardwareId.None));
        }

        [Test]
        public void TheDraftedCountsStillFitTheTable()
        {
            // docs/01 section 1 drafts 2~3개 소목표. If that band ever moved past the candidate
            // list, areas would start throwing at build time rather than here.
            Assert.GreaterOrEqual(Min, 1);
            Assert.LessOrEqual(Max, AreaObjectives.CandidatesFor(HardwareId.Cpu).Length);
        }

        // --- the shapes ----------------------------------------------------------------------

        [Test]
        public void OnlyHddHoldsItsGroundForLonger()
        {
            // 위치 관리·장기전 against the other four. docs/01 asks that HDD's long area not just
            // be bigger health bars, so the length is in what the player has to do.
            Assert.Greater(
                AreaObjectives.HoldTicksFor(HardwareId.Hdd), AreaObjectives.DraftHoldTicks,
                "HDD holds for no longer than anywhere else");

            foreach (HardwareId hardware in HardwareIds.All)
            {
                if (hardware == HardwareId.Hdd)
                    continue;

                Assert.AreEqual(
                    AreaObjectives.DraftHoldTicks, AreaObjectives.HoldTicksFor(hardware),
                    $"{hardware} holds for a time of its own that nothing in docs/01 asks for");
            }
        }

        [Test]
        public void OnlySsdRunsItsChainAgainstAClock()
        {
            Assert.Greater(
                AreaObjectives.ChainWindowTicksFor(HardwareId.Ssd), AreaObjectives.NoChainWindow,
                "SSD's 짧은 시간에 연결 has no clock");

            foreach (HardwareId hardware in HardwareIds.All)
            {
                if (hardware == HardwareId.Ssd)
                    continue;

                Assert.AreEqual(
                    AreaObjectives.NoChainWindow, AreaObjectives.ChainWindowTicksFor(hardware),
                    $"{hardware} has a chain clock docs/01 does not give it");
            }
        }

        [Test]
        public void AChainNodeIsHeldForLessTimeThanAPlainStation()
        {
            // The walk between the nodes is the task, so a node that took as long as a Hold
            // would turn a chain into three Holds in a row.
            Assert.Less(
                AreaObjectives.DraftChainNodeTicks, AreaObjectives.DraftHoldTicks,
                "a chain node asks for as long as a whole Hold");
        }

        [Test]
        public void ASsdChainCanBeWalkedInsideItsOwnWindow()
        {
            // Not a balance claim - a sanity one. A window shorter than the dwells it contains
            // would be an objective that cannot be completed at all, which is the one thing
            // docs/01 section 6 exists to prevent.
            int nodes = AreaObjectives.ChainLengthFor(HardwareId.Ssd, Max);
            int dwelling = nodes * AreaObjectives.ChainNodeTicksFor(HardwareId.Ssd);

            Assert.Greater(
                AreaObjectives.ChainWindowTicksFor(HardwareId.Ssd), dwelling,
                "SSD's window is shorter than standing on its own nodes takes");
        }

        [Test]
        public void EveryHardwareAsksForSomethingThatIsNotShooting()
        {
            // The state this table was written to end: every area's objectives being three
            // destructibles. At least one objective per hardware has to be another verb.
            foreach (HardwareId hardware in HardwareIds.All)
            {
                AreaObjectiveSpec[] specs = AreaObjectives.For(hardware, Min);
                bool dwells = false;

                for (int i = 0; i < specs.Length; ++i)
                    dwells |= specs[i].IsDwell;

                Assert.IsTrue(dwells, $"{hardware}'s objectives are all Destroy, as they used to be everywhere");
            }
        }

        [Test]
        public void TheObjectivesThatWereNotBuiltAreNamedWithTheirReason()
        {
            // CLAUDE.md: 미확정 policies are separated behind an interface that can wait for a
            // decision, and what is missing is recorded rather than quietly dropped.
            string deferred = AreaObjectives.Deferred;

            Assert.IsNotEmpty(deferred);
            StringAssert.Contains("방어", deferred, "the 방어 objectives are not accounted for");
            StringAssert.Contains("진위 구분", deferred, "RAM's 진위 구분 is not accounted for");
        }

        // --- the spec ------------------------------------------------------------------------

        [Test]
        public void ASpecRefusesToBeBuiltWrong()
        {
            Assert.Throws<ArgumentException>(
                () => new AreaObjectiveSpec(AreaObjectiveKind.Hold, "   "),
                "an objective with no name would be a blank line on the HUD");

            Assert.Throws<ArgumentOutOfRangeException>(
                () => new AreaObjectiveSpec(AreaObjectiveKind.Sequence, "레일"),
                "a chain node with no place in the chain can never be offered");

            Assert.Throws<ArgumentOutOfRangeException>(
                () => new AreaObjectiveSpec(AreaObjectiveKind.Hold, "코어", 0),
                "only a chain node has a place in a chain");
        }

        [Test]
        public void OnlyADestroyObjectiveIsNotADwell()
        {
            Assert.IsFalse(new AreaObjectiveSpec(AreaObjectiveKind.Destroy, "밸브").IsDwell);
            Assert.IsTrue(new AreaObjectiveSpec(AreaObjectiveKind.Hold, "코어").IsDwell);
            Assert.IsTrue(new AreaObjectiveSpec(AreaObjectiveKind.Sequence, "레일", 0).IsDwell);
        }

        private static string Signature(HardwareId hardware)
        {
            AreaObjectiveSpec[] specs = AreaObjectives.CandidatesFor(hardware);
            var text = new System.Text.StringBuilder();

            for (int i = 0; i < specs.Length; ++i)
                text.Append(specs[i].Kind).Append('/');

            text.Append("hold").Append(AreaObjectives.HoldTicksFor(hardware));
            text.Append("/window").Append(AreaObjectives.ChainWindowTicksFor(hardware));
            text.Append("/node").Append(AreaObjectives.ChainNodeTicksFor(hardware));
            return text.ToString();
        }
    }
}
