using System;
using System.Collections.Generic;
using NeonVoid.Combat;
using NeonVoid.Combat.Status;
using NeonVoid.Plugins;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>G08 이동 버퍼 as data, and which signals may and may not set it off.</summary>
    public sealed class MovementBufferTests
    {
        [Test]
        public void TheCardBuffsMovementForTheTwoSecondsItStates()
        {
            // [원안 수치] docs/03 G08: 2초. 가속's own catalogue duration is 180 ticks, so the
            // override is the difference between the card doing what it says and being half a
            // second longer than it says.
            PluginDefinition card = PluginCatalog.MovementBuffer();

            Assert.AreEqual("G08", card.ProposalId);
            Assert.AreEqual(PluginTriggerKind.OnMovementAction, card.Trigger);
            Assert.AreEqual(StatusEffectId.Haste, card.AppliedStatus);
            Assert.AreEqual(120, PluginCatalog.StatedMovementBufferTicks);
            Assert.AreEqual(PluginCatalog.StatedMovementBufferTicks, card.StatusDurationTicksOverride);
            Assert.DoesNotThrow(() => card.Validate());

            Assert.AreNotEqual(
                StatusCatalog.Draft()[StatusEffectId.Haste].BaseDurationTicks,
                card.StatusDurationTicksOverride,
                "If these ever match, this test is no longer proving the override is used.");
        }

        [Test]
        public void TheBuffItAppliesIsTheOneThatMovesThePlayer()
        {
            // 가속 is docs/02's movement speed buff, and the stat contribution reads it. A card
            // naming any other status would apply something and change no speed.
            var status = new StatusContainer(StatusCatalog.Draft());
            float before = NeonVoid.Combat.Stats.StatusStatContribution.MoveSpeedMultiplier(status);

            status.Apply(PluginCatalog.MovementBuffer().AppliedStatus, 0.3f);

            Assert.Greater(
                NeonVoid.Combat.Stats.StatusStatContribution.MoveSpeedMultiplier(status), before);
        }

        [Test]
        public void NothingAboutHittingSetsItOff()
        {
            var hit = new HitReport(1, 2, HitOrigin.Projectile, DamageType.Physical, healthRemoved: 10f);
            Assert.IsFalse(PluginLoadout.Triggers(PluginTriggerKind.OnMovementAction, hit));

            var loadout = new PluginLoadout();
            loadout.TryGrant(PluginCatalog.MovementBuffer(), out _);

            var byHit = new List<PluginDefinition>();
            loadout.CollectTriggered(hit, byHit);
            Assert.IsEmpty(byHit, "A hit granted a movement buff.");

            var byEffect = new List<PluginDefinition>();
            loadout.CollectTriggeredByEffectResolution(byEffect);
            Assert.IsEmpty(byEffect, "An on-hit effect granted a movement buff.");

            var byDash = new List<PluginDefinition>();
            loadout.CollectTriggeredByDashContact(byDash);
            Assert.IsEmpty(byDash, "Touching an enemy mid-dash granted the buff; it is 돌진 후.");
        }

        [Test]
        public void TheLoadoutOffersItOnAMovementAction()
        {
            var loadout = new PluginLoadout();
            loadout.TryGrant(PluginCatalog.DashDetonation(), out _);
            loadout.TryGrant(PluginCatalog.MovementBuffer(), out _);

            var triggered = new List<PluginDefinition>();
            loadout.CollectTriggeredByMovementAction(triggered);

            Assert.AreEqual(1, triggered.Count);
            Assert.AreEqual("G08", triggered[0].ProposalId);
        }

        [Test]
        public void AMovementActionTargetsTheMover()
        {
            // What these cards do, they do to whoever moved - so the event carries that actor
            // as both source and target, and the status lands on the player rather than on
            // whatever was last hit.
            var guard = new NeonVoid.Combat.Proc.ProcChainGuard();
            var queue = new NeonVoid.Combat.Proc.ProcQueue(guard);
            var router = new ProcTriggerRouter(new PluginLoadout(), guard, queue);
            router.TryGrant(PluginCatalog.MovementBuffer(), out _);

            router.OnMovementAction(actorId: 7, tick: 0L);

            var seen = new List<NeonVoid.Combat.Proc.ProcEvent>();
            router.Drain((evt, _) => seen.Add(evt));

            Assert.AreEqual(1, seen.Count);
            Assert.AreEqual(7, seen[0].SourceActorId);
            Assert.AreEqual(7, seen[0].TargetActorId);
        }

        [Test]
        public void ItIsInThePrototypeSetAndTheWholeSetStillGrants()
        {
            var ids = new List<string>();
            var loadout = new PluginLoadout();
            var rng = new NeonVoid.Core.DeterministicRng(1u);

            foreach (PluginDefinition card in PluginCatalog.Prototype())
            {
                ids.Add(card.ProposalId);
                Assert.AreEqual(
                    PluginGrantVerdict.Granted, loadout.TryGrant(card, rng, out string reason),
                    $"{card.ProposalId}: {reason}");
            }

            Assert.Contains("G08", ids);
        }
    }
}
