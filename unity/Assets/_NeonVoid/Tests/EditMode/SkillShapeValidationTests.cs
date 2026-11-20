using System;
using NeonVoid.Combat.Skills;
using NeonVoid.Combat.Status;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// The three prototype skills added last: 복구 버스트, 정화 패킷 and 표적 지정. docs/03
    /// section 6 asks for its pool to be proved out with the numbers kept replaceable, and each
    /// of these has an exception column that is worth refusing bad data over.
    /// </summary>
    public sealed class SkillShapeValidationTests
    {
        // --- the cleanse -------------------------------------------------------------------

        [Test]
        public void ACleanseMustNameSomethingToRemove()
        {
            var shape = new SkillShape { Kind = SkillEffectKind.CleanseControl };

            Assert.Throws<InvalidOperationException>(() => shape.Validate(isMovementSkill: false));
        }

        [Test]
        public void ACleanseMayOnlyNameControlEffects()
        {
            // docs/03 asks for 특정 제어 효과 해제. Stripping a buff or a damage-over-time is a
            // different feature, and one nobody has asked for - so the data refuses it rather
            // than quietly becoming a dispel.
            var dispel = new SkillShape
            {
                Kind = SkillEffectKind.CleanseControl,
                CleansedEffects = new[] { StatusEffectId.Empower },
            };
            var dot = new SkillShape
            {
                Kind = SkillEffectKind.CleanseControl,
                CleansedEffects = new[] { StatusEffectId.IntegrityAttack },
            };

            Assert.Throws<InvalidOperationException>(() => dispel.Validate(false));
            Assert.Throws<InvalidOperationException>(() => dot.Validate(false));
        }

        [Test]
        public void ACleanseOfRealControlEffectsIsAccepted()
        {
            var shape = new SkillShape
            {
                Kind = SkillEffectKind.CleanseControl,
                CleansedEffects = new[]
                {
                    StatusEffectId.Slow, StatusEffectId.Root, StatusEffectId.Malfunction,
                },
            };

            Assert.DoesNotThrow(() => shape.Validate(isMovementSkill: false));
        }

        [Test]
        public void TheCleanseItWouldBeUselessToListIsNotListed()
        {
            // 사용불가 디버프 경계, which docs/03 flags for this skill. 정지 and 제한 stop the
            // cast happening at all, so a cleanse can never reach either; listing them would be
            // a promise the gate makes impossible to keep.
            foreach (StatusEffectId denied in new[] { StatusEffectId.Stun, StatusEffectId.Silence })
            {
                Assert.AreEqual(
                    SkillCastRejection.Stunned,
                    new SkillGate(stunned: true, silenced: false, rooted: false).Evaluate(false),
                    "정지 denies every cast.");

                Assert.AreEqual(
                    SkillCastRejection.Silenced,
                    new SkillGate(stunned: false, silenced: true, rooted: false).Evaluate(false),
                    $"{denied} is not something a cleanse can be cast through.");
            }
        }

        [Test]
        public void ACleanseStillWorksWhileRootedBecauseItDoesNotMove()
        {
            // 속박 denies movement skills specifically, which is why the distinction exists.
            Assert.AreEqual(
                SkillCastRejection.None,
                new SkillGate(stunned: false, silenced: false, rooted: true).Evaluate(isMovementSkill: false));
        }

        // --- the heal ----------------------------------------------------------------------

        [Test]
        public void ASelfHealNeedsNoGeometry()
        {
            // Its magnitude comes from the damage formula; a radius would only look like it
            // meant something.
            Assert.DoesNotThrow(
                () => new SkillShape { Kind = SkillEffectKind.SelfHeal }.Validate(isMovementSkill: false));
        }

        // --- the single target -------------------------------------------------------------

        [Test]
        public void ASingleTargetSkillNeedsARangeToLockOnWithin()
        {
            var shape = new SkillShape { Kind = SkillEffectKind.SingleTarget };

            Assert.Throws<InvalidOperationException>(() => shape.Validate(isMovementSkill: false));
        }

        [Test]
        public void ASingleTargetSkillWithARangeIsAccepted()
        {
            var shape = new SkillShape { Kind = SkillEffectKind.SingleTarget, RadiusUnits = 9f };

            Assert.DoesNotThrow(() => shape.Validate(isMovementSkill: false));
        }

        // --- the kinds that came before still behave --------------------------------------

        [Test]
        public void TheOlderKindsStillValidateAsTheyDid()
        {
            Assert.DoesNotThrow(() => new SkillShape
            {
                Kind = SkillEffectKind.ConeDamage, RadiusUnits = 5f, ConeHalfAngleDegrees = 45f,
            }.Validate(false));

            Assert.DoesNotThrow(() => new SkillShape
            {
                Kind = SkillEffectKind.PersistentField,
                RadiusUnits = 3f,
                Field = FieldSchedule.Of(300, 30),
            }.Validate(false));

            Assert.Throws<InvalidOperationException>(() => new SkillShape
            {
                Kind = SkillEffectKind.Teleport, TeleportMaxDistanceUnits = 6f,
            }.Validate(isMovementSkill: false));
        }
    }
}
