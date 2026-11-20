using System.Collections;
using NeonVoid.Combat;
using NeonVoid.Combat.Skills;
using NeonVoid.Combat.Status;
using NeonVoid.Plugins;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeonVoid.Tests.PlayModeTests
{
    /// <summary>
    /// G08 이동 버퍼: 돌진/점멸 후 2초 이동속도 증가.
    /// <para>
    /// The assertion that matters is the movement multiplier, not the status being present.
    /// 강화 was once tracked with a magnitude nothing read, so "the buff is applied" was true
    /// while the player moved at exactly the same speed - and every unit test passed.
    /// </para>
    /// </summary>
    public sealed class MovementBufferPlayModeTests
    {
        private PrototypeArena _arena;
        private GameObject _dashObject;

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

        /// <summary>Only G08 carried, so nothing else can apply 가속 or change the speed.</summary>
        private IEnumerator ArenaWithOnlyMovementBuffer()
        {
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(1);

            _arena.Procs.Loadout.Clear();
            _arena.Procs.PushPassivesToOwner();
            _arena.Procs.Grant(PluginCatalog.MovementBuffer(), out string reason);
            Assert.IsTrue(_arena.Procs.Loadout.Carries("G08"), reason);

            _dashObject = new GameObject("Scripted Dash");
            _arena.Procs.SetDashState(_dashObject.AddComponent<ScriptedDash>());
        }

        private ScriptedDash Dash => _dashObject.GetComponent<ScriptedDash>();

        // --- the dash --------------------------------------------------------------------

        [UnityTest]
        public IEnumerator FinishingADashRaisesTheMovementSpeed()
        {
            yield return ArenaWithOnlyMovementBuffer();
            float baseline = _arena.Player.MoveSpeedMultiplier;

            Dash.Dashing = true;
            yield return _arena.Tick(4);
            Dash.Dashing = false;
            yield return _arena.Tick(2);

            Assert.IsTrue(_arena.Player.Status.Has(StatusEffectId.Haste), "No 가속 after the dash.");
            Assert.Greater(
                _arena.Player.MoveSpeedMultiplier, baseline,
                "가속 was applied and the player moves at exactly the same speed.");
        }

        [UnityTest]
        public IEnumerator NothingHappensWhileTheDashIsStillRunning()
        {
            // docs/03 says 후. A buff handed out mid-dash would be spent on ticks the dash was
            // already moving fast through, and the player would feel nothing.
            yield return ArenaWithOnlyMovementBuffer();

            Dash.Dashing = true;
            yield return _arena.Tick(6);

            Assert.IsFalse(
                _arena.Player.Status.Has(StatusEffectId.Haste),
                "The buff arrived during the dash rather than after it.");
        }

        [UnityTest]
        public IEnumerator TheBuffLastsTheTwoSecondsTheCardStates()
        {
            // [원안 수치] 2초 = 120 ticks, which is NOT 가속's catalogue duration of 3 seconds -
            // the card overrides it, and a card that silently took the catalogue's number would
            // be 50% longer than it says.
            yield return ArenaWithOnlyMovementBuffer();

            Dash.Dashing = true;
            yield return _arena.Tick(2);
            Dash.Dashing = false;
            yield return _arena.Tick(1);

            Assert.IsTrue(_arena.Player.Status.Has(StatusEffectId.Haste));

            yield return _arena.Tick(PluginCatalog.StatedMovementBufferTicks - 10);
            Assert.IsTrue(_arena.Player.Status.Has(StatusEffectId.Haste), "It expired early.");

            yield return _arena.Tick(20);
            Assert.IsFalse(
                _arena.Player.Status.Has(StatusEffectId.Haste),
                "It outlasted the 2 seconds the card states.");
        }

        [UnityTest]
        public IEnumerator WithoutTheCardADashChangesNothing()
        {
            yield return ArenaWithOnlyMovementBuffer();
            _arena.Procs.Loadout.Clear();
            float baseline = _arena.Player.MoveSpeedMultiplier;

            Dash.Dashing = true;
            yield return _arena.Tick(3);
            Dash.Dashing = false;
            yield return _arena.Tick(3);

            Assert.IsFalse(_arena.Player.Status.Has(StatusEffectId.Haste));
            Assert.AreEqual(baseline, _arena.Player.MoveSpeedMultiplier, 1e-4f);
        }

        // --- the movement skill ----------------------------------------------------------

        private static SkillAsset Blink(bool isMovementSkill) => PrototypeArena.SkillFrom(
            "skill.packet_blink",
            new SkillDefinition
            {
                SkillId = 2001,
                BaseCooldownTicks = 60,
                IsMovementSkill = isMovementSkill,
            },
            new SkillShape { Kind = SkillEffectKind.Teleport, TeleportMaxDistanceUnits = 5f },
            SkillStatusEffect.None);

        private static SkillAsset Cone() => PrototypeArena.SkillFrom(
            "skill.arc_pulse",
            new SkillDefinition
            {
                SkillId = 2002,
                BaseCooldownTicks = 60,
                Damage = new DamageRequest { Flat = 5f, Type = DamageType.Magic },
            },
            new SkillShape
            {
                Kind = SkillEffectKind.ConeDamage, RadiusUnits = 4f, ConeHalfAngleDegrees = 45f,
            },
            SkillStatusEffect.None);

        [UnityTest]
        public IEnumerator BlinkingRaisesTheMovementSpeedToo()
        {
            // 점멸/순간이동 후, the half a dash cannot cover.
            yield return ArenaWithOnlyMovementBuffer();
            _arena.Caster.Equip(0, Blink(isMovementSkill: true));
            float baseline = _arena.Player.MoveSpeedMultiplier;

            SkillCastResult result = _arena.Caster.CastSlot(
                0, new Vector2(_arena.PlayerObject.transform.position.x + 3f,
                               _arena.PlayerObject.transform.position.z));
            yield return _arena.Tick(2);

            Assert.IsTrue(result.Succeeded, $"the blink did not go off: {result.Rejection}");
            Assert.IsTrue(_arena.Player.Status.Has(StatusEffectId.Haste), "No 가속 after the blink.");
            Assert.Greater(_arena.Player.MoveSpeedMultiplier, baseline);
        }

        [UnityTest]
        public IEnumerator ASkillThatDoesNotMoveThePlayerGrantsNothing()
        {
            // The 스킬 분류 태그 the card's exception column asks for, which is the same flag
            // 속박 uses to decide what it denies. A skill cannot be a movement skill for one
            // system and not the other.
            yield return ArenaWithOnlyMovementBuffer();
            _arena.Caster.Equip(0, Cone());

            SkillCastResult result = _arena.Caster.CastSlot(0, new Vector2(0f, 5f));
            yield return _arena.Tick(2);

            Assert.IsTrue(result.Succeeded, $"the cone did not go off: {result.Rejection}");
            Assert.IsFalse(
                _arena.Player.Status.Has(StatusEffectId.Haste),
                "A damage skill was treated as a movement action.");
        }

        [UnityTest]
        public IEnumerator ARefusedCastGrantsNothing()
        {
            // The event fires only for a skill that actually went off - which is what docs/03
            // G07 states for its own card (스킬 실패 시 발동하지 않음) and is no less true here.
            yield return ArenaWithOnlyMovementBuffer();
            _arena.Caster.Equip(0, Blink(isMovementSkill: true));
            _arena.Player.ApplyStatus(StatusEffectId.Silence, 1f);

            SkillCastResult result = _arena.Caster.CastSlot(0, new Vector2(3f, 0f));
            yield return _arena.Tick(2);

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(
                _arena.Player.Status.Has(StatusEffectId.Haste),
                "A refused cast handed out the buff anyway.");
        }
    }
}
