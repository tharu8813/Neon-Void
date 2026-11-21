using System;
using NeonVoid.Plugins;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// G14 추적 응답 as data. The shot going off is a scene behaviour and is tested there; what
    /// is here is the card and the refusals that keep a firing effect from being declared
    /// somewhere nothing fires it.
    /// </summary>
    public sealed class SkillResponseTests
    {
        [Test]
        public void SkillResponseFiresOneShotOnASkillHit()
        {
            PluginDefinition card = PluginCatalog.SkillResponse();

            Assert.DoesNotThrow(() => card.Validate());
            Assert.AreEqual("G14", card.ProposalId);
            Assert.AreEqual(PluginTriggerKind.OnSkillHit, card.Trigger);
            Assert.AreEqual(PluginEffectKind.FireProjectile, card.Effect);

            // [원안 수치] docs/03 G14: 자동발사 탄환 1개.
            Assert.AreEqual(1, card.ProjectilesFired);
        }

        [Test]
        public void ItGoesThroughTheProcQueueLikeAnyOtherEffect()
        {
            // It happens because something happened, so it is bounded by the queue's chain
            // rules. A FiringModifier would instead be read off the loadout once and would
            // change every volley, which is a different card.
            PluginDefinition card = PluginCatalog.SkillResponse();

            Assert.IsTrue(card.ReachesProcQueue);
            Assert.AreNotEqual(0, card.EffectId, "a queued effect with no identity cannot be bounded");
            Assert.IsTrue(card.Firing.IsEmpty, "G14 does not change what a volley is");
            Assert.IsTrue(card.Passive.IsEmpty, "G14 contributes no stat");
        }

        [Test]
        public void ItCarriesNoInternalCooldownBecauseTheDocumentGivesNone()
        {
            // G10 and G17 both carry 대상별 내부 CD 3초 because docs/03 states it for them. It
            // states none here, and the card is already bounded by how often a skill can land -
            // so inventing one would be a rate nobody wrote down.
            Assert.AreEqual(0, PluginCatalog.SkillResponse().Rule.InternalCooldownTicks);
        }

        // --- the refusals ---------------------------------------------------------------------

        [Test]
        public void AFiringEffectThatFiresNothingIsRefused()
        {
            // The silent-no-op shape: the card resolves, the queue counts it, and no shot
            // appears.
            PluginDefinition hollow = PluginCatalog.SkillResponse();
            hollow.ProjectilesFired = 0;

            Assert.Throws<InvalidOperationException>(() => hollow.Validate());
        }

        [Test]
        public void AnotherKindMayNotClaimToFireProjectiles()
        {
            // The other direction: a shot count on a card nothing fires from would be dead
            // data that reads like a behaviour.
            PluginDefinition mark = PluginCatalog.CorrosionMark();
            mark.ProjectilesFired = 1;

            Assert.Throws<InvalidOperationException>(() => mark.Validate());
        }

        [Test]
        public void NoOtherCardFiresProjectilesWhenItResolves()
        {
            PluginDefinition[] others =
            {
                PluginCatalog.ArcChain(),
                PluginCatalog.DetonationGlyph(),
                PluginCatalog.CorrosionMark(),
                PluginCatalog.SupportRound(),
                PluginCatalog.QuadCache(),
                PluginCatalog.SeekerRound(),
                PluginCatalog.DashDetonation(),
            };

            for (int i = 0; i < others.Length; ++i)
            {
                Assert.AreEqual(
                    0, others[i].ProjectilesFired,
                    $"{others[i].ProposalId} fires projectiles when it resolves");
            }
        }
    }
}
