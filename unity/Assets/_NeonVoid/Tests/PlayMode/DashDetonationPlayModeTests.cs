using System.Collections;
using NeonVoid.Combat;
using NeonVoid.Plugins;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeonVoid.Tests.PlayModeTests
{
    /// <summary>
    /// G16 대시 폭발, and the exception docs/03 names for it: 한 대시에서 중복 접촉.
    /// <para>
    /// That exception is the whole test. The dash lasts nine ticks in the prototype and the
    /// path is swept on every one of them, so the difference between a correct card and one
    /// that deals nine times its damage is a single HashSet - and nothing about the card's data
    /// would tell you which you had.
    /// </para>
    /// </summary>
    public sealed class DashDetonationPlayModeTests
    {
        private PrototypeArena _arena;
        private GameObject _dashObject;

        /// <summary>
        /// A dash the test can switch on. The real one is driven by the Input System, which a
        /// PlayMode test has no sensible way to press.
        /// </summary>
        private sealed class ScriptedDash : MonoBehaviour, IDashState
        {
            public bool Dashing;

            public bool IsDashing => Dashing;
        }

        [TearDown]
        public void TearDown()
        {
            if (_dashObject != null)
                Object.Destroy(_dashObject);
            _dashObject = null;
            _arena?.Dispose();
            _arena = null;
        }

        /// <summary>
        /// An arena with one enemy within the dash's reach of the player, and only G16 carried
        /// so the health readings below are the dash blast and nothing else.
        /// </summary>
        private IEnumerator ArenaWithOnlyDashDetonation()
        {
            _arena = PrototypeArena.Build(new Vector3(1f, 0.7f, 0f));
            yield return _arena.Tick(1);

            _arena.Procs.Loadout.Clear();
            _arena.Procs.PushPassivesToOwner();
            _arena.Procs.Grant(PluginCatalog.DashDetonation(), out string reason);
            Assert.IsTrue(_arena.Procs.Loadout.Carries("G16"), reason);

            _dashObject = new GameObject("Scripted Dash");
            _arena.Procs.SetDashState(_dashObject.AddComponent<ScriptedDash>());
        }

        private ScriptedDash Dash => _dashObject.GetComponent<ScriptedDash>();

        [UnityTest]
        public IEnumerator DashingThroughAnEnemyDamagesIt()
        {
            yield return ArenaWithOnlyDashDetonation();
            CombatActor enemy = _arena.Enemies[0];
            float before = enemy.Health.Current;

            Dash.Dashing = true;
            yield return _arena.Tick(2);

            Assert.Less(enemy.Health.Current, before, "The dash passed through and did nothing.");
        }

        [UnityTest]
        public IEnumerator OneDashDamagesAnEnemyOnceHoweverLongItLasts()
        {
            // 한 대시에서 중복 접촉, which is the exception docs/03 states for this card.
            yield return ArenaWithOnlyDashDetonation();
            CombatActor enemy = _arena.Enemies[0];
            float before = enemy.Health.Current;

            Dash.Dashing = true;
            yield return _arena.Tick(2);
            float afterFirstContact = enemy.Health.Current;
            float oneBlast = before - afterFirstContact;

            Assert.Greater(oneBlast, 0f, "The dash did nothing at all.");

            // Twenty more ticks of dashing, far longer than the prototype's nine tick dash.
            yield return _arena.Tick(20);

            Assert.AreEqual(
                afterFirstContact, enemy.Health.Current, 0.001f,
                $"The dash hit the same enemy again; one blast was {oneBlast} and it kept going.");
            Assert.AreEqual(1, _arena.Procs.TouchedByThisDash);
        }

        [UnityTest]
        public IEnumerator TheNextDashDamagesItAgain()
        {
            // The dedupe is per dash, not per enemy forever. A card that only ever worked once
            // would be a different card.
            yield return ArenaWithOnlyDashDetonation();
            CombatActor enemy = _arena.Enemies[0];

            Dash.Dashing = true;
            yield return _arena.Tick(2);
            float afterFirstDash = enemy.Health.Current;

            Dash.Dashing = false;
            yield return _arena.Tick(2);
            Dash.Dashing = true;
            yield return _arena.Tick(2);

            Assert.Less(enemy.Health.Current, afterFirstDash, "A second dash did nothing.");
        }

        [UnityTest]
        public IEnumerator StandingStillNextToAnEnemyDoesNothing()
        {
            // The trigger is the dash, not proximity. Without this the card would be a damage
            // aura with a misleading name.
            yield return ArenaWithOnlyDashDetonation();
            CombatActor enemy = _arena.Enemies[0];
            float before = enemy.Health.Current;

            Dash.Dashing = false;
            yield return _arena.Tick(30);

            Assert.AreEqual(before, enemy.Health.Current, 0.001f, "Being near an enemy damaged it.");
        }

        [UnityTest]
        public IEnumerator AnEnemyOutOfReachIsNotTouched()
        {
            _arena = PrototypeArena.Build(new Vector3(12f, 0.7f, 0f));
            yield return _arena.Tick(1);
            _arena.Procs.Loadout.Clear();
            _arena.Procs.PushPassivesToOwner();
            _arena.Procs.Grant(PluginCatalog.DashDetonation(), out _);
            _dashObject = new GameObject("Scripted Dash");
            _arena.Procs.SetDashState(_dashObject.AddComponent<ScriptedDash>());

            CombatActor enemy = _arena.Enemies[0];
            float before = enemy.Health.Current;

            Dash.Dashing = true;
            yield return _arena.Tick(10);

            Assert.AreEqual(before, enemy.Health.Current, 0.001f);
            Assert.AreEqual(0, _arena.Procs.TouchedByThisDash);
        }

        [UnityTest]
        public IEnumerator WithoutTheCardADashTouchesNobody()
        {
            yield return ArenaWithOnlyDashDetonation();
            _arena.Procs.Loadout.Clear();
            CombatActor enemy = _arena.Enemies[0];
            float before = enemy.Health.Current;

            Dash.Dashing = true;
            yield return _arena.Tick(10);

            Assert.AreEqual(before, enemy.Health.Current, 0.001f);
            Assert.AreEqual(
                0, _arena.Procs.TouchedByThisDash,
                "The path was swept with no card asking for it.");
        }

        [UnityTest]
        public IEnumerator TheDashBlastIsAHitLikeAnyOtherSoOtherCardsSeeIt()
        {
            // 부식 표식 fires on 타격 시. The blast is a hit, so the mark must land - which is
            // what keeps the dash inside the one event queue rather than beside it.
            yield return ArenaWithOnlyDashDetonation();
            _arena.Procs.Grant(PluginCatalog.CorrosionMark(), out _);
            CombatActor enemy = _arena.Enemies[0];

            Dash.Dashing = true;
            yield return _arena.Tick(3);

            Assert.IsTrue(
                enemy.Status.Has(NeonVoid.Combat.Status.StatusEffectId.FirewallBreach),
                "The dash blast did not register as a hit, so no other card could react to it.");
        }
    }
}
