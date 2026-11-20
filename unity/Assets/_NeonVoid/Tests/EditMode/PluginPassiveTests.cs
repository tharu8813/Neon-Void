using System;
using System.Collections.Generic;
using NeonVoid.Combat;
using NeonVoid.Combat.Stats;
using NeonVoid.Core;
using NeonVoid.Plugins;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// Plugins that do their work simply by being carried: the 플러그인 stage of docs/02
    /// section 2's pipeline, which until now nothing fed.
    /// </summary>
    public sealed class PluginPassiveTests
    {
        private static CombatStats Base()
        {
            CombatStats stats = CombatStats.StartingVaccine();
            stats.AttackDamage = 10f;
            stats.AbilityPower = 20f;
            return stats;
        }

        private static PluginLoadout Carrying(params PluginDefinition[] plugins)
        {
            var loadout = new PluginLoadout();
            for (int i = 0; i < plugins.Length; ++i)
                Assert.AreEqual(
                    PluginGrantVerdict.Granted, loadout.TryGrant(plugins[i], out string reason), reason);

            return loadout;
        }

        private static List<StatModifier> PassivesOf(PluginLoadout loadout)
        {
            var modifiers = new List<StatModifier>();
            loadout.CollectPassiveModifiers(modifiers);
            return modifiers;
        }

        // --- the carried contribution ------------------------------------------------------

        [Test]
        public void APassivePluginNeedsNoTriggerAndIsStillValid()
        {
            // A passive has nothing to set it off, by definition. Everything else must say what
            // triggers it or it can never do anything at all.
            Assert.DoesNotThrow(() => PluginCatalog.Lightweight().Validate());
            Assert.AreEqual(PluginTriggerKind.None, PluginCatalog.Lightweight().Trigger);
        }

        [Test]
        public void APassiveWithATriggerIsRefused()
        {
            // It would be queued as a proc that resolves to nothing, and the queue would count
            // it as work it did.
            PluginDefinition confused = PluginCatalog.Lightweight();
            confused.Trigger = PluginTriggerKind.OnAnyHit;

            Assert.Throws<InvalidOperationException>(() => confused.Validate());
        }

        [Test]
        public void APassiveThatContributesNothingIsRefused()
        {
            PluginDefinition empty = PluginCatalog.Lightweight();
            empty.Passive = PluginPassive.None;

            Assert.Throws<InvalidOperationException>(() => empty.Validate());
        }

        [Test]
        public void WeaknessAnalysisRaisesBothPenetrationStats()
        {
            CombatStats resolved = CombatStatResolution.Resolve(
                Base(), PassivesOf(Carrying(PluginCatalog.WeaknessAnalysis())), StatCaps.Draft());

            Assert.Greater(resolved.PhysicalPenPercent, 0f);
            Assert.Greater(resolved.MagicPenPercent, 0f);
        }

        [Test]
        public void MovementPassivesAddRatherThanCompound()
        {
            // docs/03 G02 leaves 가산/배수 open. Summing keeps two movement plugins from
            // compounding into a figure nobody tuned.
            PluginDefinition second = PluginCatalog.Lightweight();
            second.ProposalId = "G02-B";

            PluginLoadout loadout = Carrying(PluginCatalog.Lightweight(), second);
            float one = PluginCatalog.Lightweight().Passive.MoveSpeedFraction;

            Assert.AreEqual(one * 2f, loadout.PassiveMoveSpeedFraction(), 1e-4f);
        }

        [Test]
        public void PassivesAndStatusesBothReachTheSnapshotWithoutCompounding()
        {
            // Both are the same stage of docs/02's order, and every fraction is of the base, so
            // the result cannot depend on which was applied first.
            var passives = new List<StatModifier> { StatModifier.Scale(ModifiedStat.AttackDamage, 0.5f) };
            var statuses = new List<StatModifier> { StatModifier.Scale(ModifiedStat.AttackDamage, 0.5f) };

            var together = new List<StatModifier>(passives);
            together.AddRange(statuses);
            var reversed = new List<StatModifier>(statuses);
            reversed.AddRange(passives);

            Assert.AreEqual(20f, CombatStatResolution.Resolve(Base(), together, StatCaps.Draft()).AttackDamage, 1e-4f);
            Assert.AreEqual(20f, CombatStatResolution.Resolve(Base(), reversed, StatCaps.Draft()).AttackDamage, 1e-4f);
        }

        // --- the random patch roll ---------------------------------------------------------

        [Test]
        public void TheRandomPatchRollsAsManyStatsAsTheDesignStates()
        {
            var loadout = new PluginLoadout();
            loadout.TryGrant(PluginCatalog.RandomPatch(), new DeterministicRng(7u), out string reason);

            Assert.AreEqual(PluginCatalog.StatedRandomPatchStatCount, PassivesOf(loadout).Count, reason);
            Assert.AreEqual(3, PluginCatalog.StatedRandomPatchStatCount, "docs/03 G01 says 3개.");
        }

        [Test]
        public void TheRolledStatsAreDistinct()
        {
            // docs/03 leaves 같은 스탯 2회 선택 여부 open. Rolling the same stat twice makes
            // "3개 증가" a lie to the player reading the card, so distinct is the reading that
            // cannot mislead.
            for (uint seed = 1; seed <= 25; ++seed)
            {
                var loadout = new PluginLoadout();
                loadout.TryGrant(PluginCatalog.RandomPatch(), new DeterministicRng(seed), out _);

                var seen = new HashSet<ModifiedStat>();
                List<StatModifier> rolled = PassivesOf(loadout);
                for (int i = 0; i < rolled.Count; ++i)
                    Assert.IsTrue(seen.Add(rolled[i].Stat), $"Seed {seed} rolled {rolled[i].Stat} twice.");
            }
        }

        [Test]
        public void TheSameSeedRollsTheSameStats()
        {
            var first = new PluginLoadout();
            var second = new PluginLoadout();
            first.TryGrant(PluginCatalog.RandomPatch(), new DeterministicRng(99u), out _);
            second.TryGrant(PluginCatalog.RandomPatch(), new DeterministicRng(99u), out _);

            List<StatModifier> a = PassivesOf(first);
            List<StatModifier> b = PassivesOf(second);

            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; ++i)
            {
                Assert.AreEqual(a[i].Stat, b[i].Stat, "The same seed rolled a different stat.");
                Assert.AreEqual(a[i].FractionOfBase, b[i].FractionOfBase, 1e-6f);
            }
        }

        [Test]
        public void DifferentSeedsCanRollDifferentStats()
        {
            // Not a guarantee for any particular pair of seeds, but a roll that ignored the
            // stream entirely would make every seed identical, and that is worth catching.
            var distinctFirstPicks = new HashSet<ModifiedStat>();
            for (uint seed = 1; seed <= 40; ++seed)
            {
                var loadout = new PluginLoadout();
                loadout.TryGrant(PluginCatalog.RandomPatch(), new DeterministicRng(seed), out _);
                distinctFirstPicks.Add(PassivesOf(loadout)[0].Stat);
            }

            Assert.Greater(distinctFirstPicks.Count, 1, "Every seed rolled the same stat first.");
        }

        [Test]
        public void ARollRequestedForMoreStatsThanExistIsRefused()
        {
            // docs/03 leaves 유효 후보 부족 open; refusing beats silently granting fewer than
            // the card promises.
            PluginDefinition greedy = PluginCatalog.RandomPatch();
            greedy.Passive.RandomStatCount = PluginPassive.RandomCandidates.Length + 1;

            Assert.Throws<InvalidOperationException>(() => greedy.Validate());
        }

        [Test]
        public void AGrantedRollIsNotRolledAgain()
        {
            // The carried copy keeps the stats it was granted with. A plugin that re-rolled on
            // every read would change under the player mid-mission.
            var loadout = new PluginLoadout();
            loadout.TryGrant(PluginCatalog.RandomPatch(), new DeterministicRng(5u), out _);

            PluginDefinition carried = loadout.At(0);
            Assert.AreEqual(0, carried.Passive.RandomStatCount, "The roll is still pending on the carried copy.");
            Assert.AreEqual(
                PluginCatalog.StatedRandomPatchStatCount,
                carried.Passive.StatModifiers.Length);
        }

        // --- the execution bonus -----------------------------------------------------------

        [Test]
        public void TheExecutionBonusRaisesDamageBelowTheThreshold()
        {
            CombatStats attacker = Base();
            attacker.LowHealthDamageBonusPercent = PluginCatalog.StatedExecutionBonusPercent;
            attacker.LowHealthThresholdFraction = PluginCatalog.StatedExecutionThreshold;

            var request = new DamageRequest { Flat = 100f, Type = DamageType.True };
            CombatStats defender = CombatStats.StartingVaccine();

            HealthPool healthy = HealthPool.Full(100f);
            HealthPool wounded = HealthPool.Full(100f);
            wounded.Remove(60f);     // 40% left, under the 50% mark

            DamageOutcome full = DamageApplication.Apply(
                request, attacker, defender, ref healthy, false, LifestealPolicy.Draft(), false, true);
            DamageOutcome executed = DamageApplication.Apply(
                request, attacker, defender, ref wounded, false, LifestealPolicy.Draft(), false, true);

            Assert.AreEqual(100f, full.Resolution.AppliedBeforeShield, 1e-3f);
            Assert.AreEqual(115f, executed.Resolution.AppliedBeforeShield, 1e-3f,
                "docs/03 R01 states 15%.");
        }

        [Test]
        public void ATargetExactlyAtTheThresholdDoesNotQualify()
        {
            // 50% 미만 is the document's wording. An inclusive reading would also hand the
            // bonus to a full-health target whose threshold happened to be 1.
            CombatStats attacker = Base();
            attacker.LowHealthDamageBonusPercent = 15f;
            attacker.LowHealthThresholdFraction = 0.5f;

            HealthPool exactlyHalf = HealthPool.Full(100f);
            exactlyHalf.Remove(50f);

            DamageOutcome outcome = DamageApplication.Apply(
                new DamageRequest { Flat = 100f, Type = DamageType.True },
                attacker, CombatStats.StartingVaccine(), ref exactlyHalf,
                false, LifestealPolicy.Draft(), false, true);

            Assert.AreEqual(100f, outcome.Resolution.AppliedBeforeShield, 1e-3f);
        }

        [Test]
        public void TheThresholdIsReadBeforeTheHitRemovesHealth()
        {
            // docs/03 R01: 피해 확정 시 체력 기준 스냅샷. A hit that drops the target under the
            // threshold must not also collect the bonus for having done so.
            CombatStats attacker = Base();
            attacker.LowHealthDamageBonusPercent = 15f;
            attacker.LowHealthThresholdFraction = 0.5f;

            HealthPool full = HealthPool.Full(100f);

            DamageOutcome outcome = DamageApplication.Apply(
                new DamageRequest { Flat = 90f, Type = DamageType.True },
                attacker, CombatStats.StartingVaccine(), ref full,
                false, LifestealPolicy.Draft(), false, true);

            Assert.AreEqual(90f, outcome.Resolution.AppliedBeforeShield, 1e-3f,
                "The hit collected its own bonus by reading health after removing it.");
        }

        [Test]
        public void WithoutThePluginNothingChanges()
        {
            CombatStats attacker = Base();     // no bonus
            HealthPool wounded = HealthPool.Full(100f);
            wounded.Remove(80f);

            DamageOutcome outcome = DamageApplication.Apply(
                new DamageRequest { Flat = 100f, Type = DamageType.True },
                attacker, CombatStats.StartingVaccine(), ref wounded,
                false, LifestealPolicy.Draft(), false, true);

            Assert.AreEqual(100f, outcome.Resolution.AppliedBeforeShield, 1e-3f);
        }

        [Test]
        public void TheExecutionPluginCarriesTheDesignsOwnFigures()
        {
            CombatStats resolved = CombatStatResolution.Resolve(
                Base(), PassivesOf(Carrying(PluginCatalog.ExecutionAlgorithm())), StatCaps.Draft());

            Assert.AreEqual(15f, PluginCatalog.StatedExecutionBonusPercent, "docs/03 R01 states 15%.");
            Assert.AreEqual(0.5f, PluginCatalog.StatedExecutionThreshold, "docs/03 R01 states 50%.");
            Assert.AreEqual(15f, resolved.LowHealthDamageBonusPercent, 1e-3f);
        }

        [Test]
        public void ANegativeExecutionBonusIsFlooredRatherThanProtectingTheTarget()
        {
            CombatStats resolved = CombatStatResolution.Resolve(
                Base(),
                new List<StatModifier> { StatModifier.Add(ModifiedStat.LowHealthDamageBonusPercent, -50f) },
                StatCaps.Draft());

            Assert.AreEqual(0f, resolved.LowHealthDamageBonusPercent, 1e-4f);
        }

        // --- the whole prototype set -------------------------------------------------------

        [Test]
        public void EveryPrototypePluginStillValidates()
        {
            IReadOnlyList<PluginDefinition> prototype = PluginCatalog.Prototype();

            // No hardcoded total: the catalogue grows as docs/03's 27 cards are implemented,
            // and a number here would only ever be a reminder to edit this line. What must hold
            // is that every card in the set is distinct and grantable.
            Assert.IsNotEmpty(prototype);
            var ids = new HashSet<string>();
            for (int i = 0; i < prototype.Count; ++i)
                Assert.IsTrue(ids.Add(prototype[i].ProposalId), $"{prototype[i].ProposalId} appears twice.");

            var loadout = new PluginLoadout();
            var rng = new DeterministicRng(1u);
            for (int i = 0; i < prototype.Count; ++i)
                Assert.AreEqual(
                    PluginGrantVerdict.Granted, loadout.TryGrant(prototype[i], rng, out string reason),
                    $"{prototype[i].ProposalId}: {reason}");
        }
    }
}
