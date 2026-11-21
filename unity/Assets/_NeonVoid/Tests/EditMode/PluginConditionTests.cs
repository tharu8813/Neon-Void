using System;
using NeonVoid.Plugins;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// G11 무피격 가속 as data: the gate, and the refusals that keep a gated card from quietly
    /// becoming an ungated one.
    /// <para>
    /// docs/03 states 이동속도 100% 증가 and 피해를 받으면 비활성화, and leaves 재활성화 시점
    /// 미확정. The [원안 수치] is asserted exactly; the recovery time is only asserted to exist
    /// and to be the card's own number, because it is a draft bound to be decided.
    /// </para>
    /// </summary>
    public sealed class PluginConditionTests
    {
        // --- the gate ------------------------------------------------------------------------

        [Test]
        public void ACarrierWhoHasNeverBeenHurtIsAlreadyGoingFast()
        {
            // 피해를 받으면 비활성화, not 피해를 받으면 활성화. A card that needed a first hit
            // before it counted would be off for the opening of every mission.
            PluginCondition gate = PluginCondition.UndamagedFor(180);

            Assert.IsTrue(gate.IsMet(int.MaxValue), "a carrier who has never been hurt is gated");
        }

        [Test]
        public void TheGateShutsOnDamageAndOpensAgainOnTime()
        {
            PluginCondition gate = PluginCondition.UndamagedFor(180);

            Assert.IsFalse(gate.IsMet(0), "the gate stayed open on the tick damage landed");
            Assert.IsFalse(gate.IsMet(179), "the gate opened a tick early");
            Assert.IsTrue(gate.IsMet(180), "the gate did not open on its own tick");
            Assert.IsTrue(gate.IsMet(100_000), "the gate shut again by itself");
        }

        [Test]
        public void NoConditionIsAlwaysMet()
        {
            Assert.IsTrue(PluginCondition.None.IsMet(0));
            Assert.IsTrue(PluginCondition.None.IsNone);
        }

        [Test]
        public void AGateOfNoTicksIsOpenImmediately()
        {
            // Not nonsense, and worth pinning: it is what a retune to "comes back at once"
            // looks like, and it must mean exactly that rather than never.
            PluginCondition gate = PluginCondition.UndamagedFor(0);
            Assert.IsTrue(gate.IsMet(0));
        }

        [Test]
        public void AGateCannotAskForNegativeTime()
        {
            var backwards = new PluginCondition
            {
                Kind = PluginConditionKind.Undamaged,
                TicksUndamaged = -1,
            };

            Assert.Throws<InvalidOperationException>(() => backwards.Validate("TEST"));
        }

        // --- the card ------------------------------------------------------------------------

        [Test]
        public void UntouchedAccelerationIsTheDocumentsOwnNumber()
        {
            PluginDefinition card = PluginCatalog.UntouchedAcceleration();

            Assert.DoesNotThrow(() => card.Validate());
            Assert.AreEqual("G11", card.ProposalId);
            Assert.AreEqual(PluginEffectKind.ConditionalPassive, card.Effect);
            Assert.AreEqual(PluginTriggerKind.None, card.Trigger);

            // [원안 수치] docs/03 G11: 이동속도 100% 증가.
            Assert.AreEqual(1.0f, card.Passive.MoveSpeedFraction, 0.0001f);
            Assert.AreEqual(PluginConditionKind.Undamaged, card.Condition.Kind);
            Assert.Greater(
                card.Condition.TicksUndamaged, 0,
                "a recovery of no time makes 피해를 받으면 비활성화 last exactly one tick");
        }

        // --- the refusals ---------------------------------------------------------------------

        [Test]
        public void AConditionalPassiveWithNoConditionIsRefused()
        {
            // The dangerous direction: a gated card losing its gate becomes a strictly stronger
            // card that still reads as the gated one. 무피격 가속 without the gate is
            // 이동속도 100% 증가, unconditionally, which is not a card anybody approved.
            PluginDefinition ungated = PluginCatalog.UntouchedAcceleration();
            ungated.Condition = PluginCondition.None;

            Assert.Throws<InvalidOperationException>(() => ungated.Validate());
        }

        [Test]
        public void AConditionalPassiveThatContributesNothingIsRefused()
        {
            PluginDefinition hollow = PluginCatalog.UntouchedAcceleration();
            hollow.Passive = PluginPassive.None;

            Assert.Throws<InvalidOperationException>(() => hollow.Validate());
        }

        [Test]
        public void AnUngatedKindMayNotCarryACondition()
        {
            // The other direction: a condition on a card nothing evaluates would be a gate that
            // is silently always open.
            PluginDefinition lightweight = PluginCatalog.Lightweight();
            lightweight.Condition = PluginCondition.UndamagedFor(60);

            Assert.Throws<InvalidOperationException>(() => lightweight.Validate());
        }

        [Test]
        public void AConditionalPassiveIsNotTriggeredByHits()
        {
            PluginDefinition triggered = PluginCatalog.UntouchedAcceleration();
            triggered.Trigger = PluginTriggerKind.OnAnyHit;

            Assert.Throws<InvalidOperationException>(
                () => triggered.Validate(),
                "a gated passive that the queue also sees would resolve as an effect");
        }

        // --- the loadout ------------------------------------------------------------------------

        [Test]
        public void TheLoadoutHandsOverGatedCardsWithoutEvaluatingThem()
        {
            // The loadout has no clock and does not know what has happened to the carrier, so
            // the gate must not be its decision - and the card must not be counted among the
            // ungated move speed either, or it would apply while shut.
            var loadout = new PluginLoadout();
            Assert.AreEqual(
                PluginGrantVerdict.Granted,
                loadout.TryGrant(PluginCatalog.UntouchedAcceleration(), out string why), why);

            Assert.AreEqual(
                0f, loadout.PassiveMoveSpeedFraction(),
                "a gated card was counted among the ungated contributions");

            var gated = new System.Collections.Generic.List<PluginDefinition>();
            loadout.CollectConditionalPassives(gated);
            Assert.AreEqual(1, gated.Count);
            Assert.AreEqual("G11", gated[0].ProposalId);
        }
    }
}
