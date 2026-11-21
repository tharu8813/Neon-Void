using System.Collections;
using System.Reflection;
using NeonVoid.Combat;
using NeonVoid.Combat.Projectiles;
using NeonVoid.Combat.Skills;
using NeonVoid.Combat.Status;
using NeonVoid.Plugins;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeonVoid.Tests.PlayModeTests
{
    /// <summary>
    /// G14 추적 응답: 스킬 피해 적중 시 자동발사 탄환 1개.
    /// <para>
    /// The assertion that matters is a projectile existing that would not otherwise exist. The
    /// trigger matching, the queue admitting the effect and the resolver reaching the attacker
    /// can all be right while the shot is never queued, and every pure test would still pass.
    /// </para>
    /// </summary>
    public sealed class SkillResponsePlayModeTests
    {
        private PrototypeArena _arena;

        [TearDown]
        public void TearDown()
        {
            _arena?.Dispose();
            _arena = null;
        }

        private static int LiveProjectiles() =>
            Object.FindObjectsByType<CombatProjectile>(FindObjectsSortMode.None).Length;

        /// <summary>A cone that lands on anything in front of the caster.</summary>
        private static SkillAsset ConeSkill() => PrototypeArena.SkillFrom(
            "skill.test_cone",
            new SkillDefinition
            {
                SkillId = 1401,
                ManaCost = 0f,
                BaseCooldownTicks = 1,
                Damage = new DamageRequest { Flat = 5f, Type = DamageType.Magic, IsArea = true },
            },
            new SkillShape
            {
                Kind = SkillEffectKind.ConeDamage, RadiusUnits = 8f, ConeHalfAngleDegrees = 60f,
            },
            default);

        /// <summary>
        /// An arena with an enemy in front, only G14 carried, and the weapon enabled but
        /// silent.
        /// <para>
        /// The attacker has to be enabled, because 자동발사 탄환 is a basic attack and the
        /// attacker is what fires one - a disabled weapon has no basic attack to fire, and its
        /// queue is never drained. So instead of switching it off, its next shot is pushed
        /// beyond the end of the test: a queued burst still goes off, because the attacker
        /// drains that queue before it considers its own cadence, and nothing it fires on its
        /// own can be mistaken for the card's shot.
        /// </para>
        /// </summary>
        private IEnumerator ArenaWithOnlySkillResponse()
        {
            _arena = PrototypeArena.Build(new Vector3(3f, 0.7f, 0f));
            _arena.PlayerObject.transform.forward = Vector3.right;
            yield return _arena.Tick(2);

            _arena.Procs.Loadout.Clear();
            Assert.AreEqual(
                PluginGrantVerdict.Granted,
                _arena.Procs.Loadout.TryGrant(PluginCatalog.SkillResponse(), out string why), why);
            _arena.Procs.PushPassivesToOwner();

            Silence(_arena.Attacker);
            _arena.Attacker.enabled = true;

            _arena.Caster.Equip(0, ConeSkill());
            yield return _arena.Tick(2);

            Assert.AreEqual(0, LiveProjectiles(), "the weapon fired on its own after being silenced");
        }

        /// <summary>
        /// Pushes the weapon's next shot past the end of any test here, without disabling it.
        /// Reflection because the rate is a serialized field with no setter, and adding one to
        /// the runtime for a test's convenience would be production API nobody asked for.
        /// </summary>
        private static void Silence(AutoAttacker attacker)
        {
            const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;

            typeof(AutoAttacker).GetField("attacksPerSecond", Hidden).SetValue(attacker, 0.1f);
            typeof(AutoAttacker).GetField("_ticksUntilNextShot", Hidden).SetValue(attacker, 100_000);
        }

        [UnityTest]
        public IEnumerator ASkillThatLandsFiresAShot()
        {
            yield return ArenaWithOnlySkillResponse();

            SkillCastResult cast = _arena.Caster.CastSlot(0, new Vector2(10f, 0f));
            Assert.IsTrue(cast.Succeeded, cast.Rejection.ToString());

            // One tick for the hit to be reported, one for the queue, one for the shot.
            yield return _arena.Tick(4);

            Assert.AreEqual(
                PluginCatalog.StatedSkillResponseShots, LiveProjectiles(),
                "자동발사 탄환 1개 - the skill landed and no shot went off");
        }

        [UnityTest]
        public IEnumerator ASkillThatHitsNothingFiresNothing()
        {
            // 스킬 피해 적중 시 - on a skill landing damage, not on a skill being cast. A card
            // that fired on the cast would pay the player for missing.
            yield return ArenaWithOnlySkillResponse();

            SkillCastResult cast = _arena.Caster.CastSlot(0, new Vector2(-10f, 0f));   // away
            Assert.IsTrue(cast.Succeeded, cast.Rejection.ToString());
            yield return _arena.Tick(4);

            Assert.AreEqual(0, LiveProjectiles(), "a skill that hit nothing still fired a shot");
        }

        [UnityTest]
        public IEnumerator TheShotItFiresCannotFireItAgain()
        {
            // 스킬의 자기 발동/루프 방지. The shot is a basic attack, so whatever it hits can
            // never satisfy OnSkillHit again - the loop has no second step.
            //
            // Asserted as "no shot other than that one ever appears" rather than "nothing is in
            // flight at the end": whether the shot connects depends on where the weapon happens
            // to be pointing, and this test is about the card feeding itself, not about aim.
            yield return ArenaWithOnlySkillResponse();

            SkillCastResult cast = _arena.Caster.CastSlot(0, new Vector2(10f, 0f));
            Assert.IsTrue(cast.Succeeded, cast.Rejection.ToString());
            yield return _arena.Tick(4);

            CombatProjectile[] first =
                Object.FindObjectsByType<CombatProjectile>(FindObjectsSortMode.None);
            Assert.AreEqual(1, first.Length, "the one shot never went off");
            int theOnlyShot = first[0].GetInstanceID();

            // Long enough for that shot to cross the arena and resolve, and for anything it
            // provoked to appear.
            for (int tick = 0; tick < 120; ++tick)
            {
                yield return _arena.Tick(1);

                CombatProjectile[] now =
                    Object.FindObjectsByType<CombatProjectile>(FindObjectsSortMode.None);

                for (int i = 0; i < now.Length; ++i)
                {
                    Assert.AreEqual(
                        theOnlyShot, now[i].GetInstanceID(),
                        $"a second shot appeared on tick {tick}, so 추적 응답 feeds itself");
                }
            }
        }

        [UnityTest]
        public IEnumerator WithoutTheCardASkillFiresNothing()
        {
            // The control. Without it, a test that counted one projectile would pass just as
            // well if the basic attack had fired it.
            yield return ArenaWithOnlySkillResponse();
            _arena.Procs.Loadout.Clear();
            _arena.Procs.PushPassivesToOwner();

            SkillCastResult cast = _arena.Caster.CastSlot(0, new Vector2(10f, 0f));
            Assert.IsTrue(cast.Succeeded, cast.Rejection.ToString());
            yield return _arena.Tick(4);

            Assert.AreEqual(0, LiveProjectiles(), "a shot went off with no card to fire it");
        }
    }
}
