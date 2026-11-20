using System;
using NeonVoid.Combat.Skills;
using NeonVoid.Combat.Status;
using NUnit.Framework;
using UnityEngine;

namespace NeonVoid.Tests
{
    /// <summary>
    /// The geometry and timing behind the three prototype skills of docs/03 section 6. The cast
    /// rules themselves (mana, cooldown, control gate) are covered by SkillSystemTests; these
    /// are the parts that decide who an effect reaches and how often.
    /// </summary>
    public sealed class SkillEffectTests
    {
        private const float Radius = 5f;
        private const float HalfAngle = 45f;

        private static readonly Vector2 Origin = Vector2.zero;
        private static readonly Vector2 FacingPositiveX = new Vector2(1f, 0f);

        private static bool Cone(Vector2 point, float radius = Radius, float halfAngle = HalfAngle) =>
            ConeTargeting.IsInsideCone(Origin, FacingPositiveX, point, radius, halfAngle);

        // --- cone targeting ----------------------------------------------------------------

        [Test]
        public void ATargetStraightAheadAndInRangeIsInside()
        {
            Assert.IsTrue(Cone(new Vector2(3f, 0f)));
        }

        [Test]
        public void ATargetBehindTheCasterIsOutside()
        {
            Assert.IsFalse(Cone(new Vector2(-3f, 0f)));
        }

        [Test]
        public void ATargetBeyondTheRadiusIsOutsideEvenWhenPerfectlyAimedAt()
        {
            Assert.IsFalse(Cone(new Vector2(Radius + 0.01f, 0f)));
        }

        [Test]
        public void ATargetStandingOnTheCasterIsInside()
        {
            // The trap this covers: the direction to a target at distance zero is undefined,
            // and normalising it gives NaN, which compares false. The enemy pressed right up
            // against the player would be the one target a cone could never hit.
            Assert.IsTrue(Cone(Origin));
        }

        [Test]
        public void ATargetJustInsideTheEdgeAngleIsIncluded()
        {
            // 45 degrees off the facing direction, just inside the 45 degree half angle.
            float radians = (HalfAngle - 0.5f) * Mathf.Deg2Rad;
            var edge = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * 3f;

            Assert.IsTrue(Cone(edge));
        }

        [Test]
        public void ATargetJustOutsideTheEdgeAngleIsExcluded()
        {
            float radians = (HalfAngle + 1f) * Mathf.Deg2Rad;
            var beyond = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * 3f;

            Assert.IsFalse(Cone(beyond));
        }

        [Test]
        public void AConeWithNoFacingCoversTheWholeCircleRatherThanNothing()
        {
            // An unaimable cone that hits nothing is indistinguishable from a broken skill.
            Assert.IsTrue(ConeTargeting.IsInsideCone(
                Origin, Vector2.zero, new Vector2(-3f, 0f), Radius, HalfAngle));
        }

        [Test]
        public void AHalfAngleOf180IsAFullCircle()
        {
            Assert.IsTrue(Cone(new Vector2(-3f, 0f), halfAngle: 180f));
        }

        [Test]
        public void AConeWithNoRadiusReachesNobody()
        {
            Assert.IsFalse(Cone(new Vector2(0.1f, 0f), radius: 0f));
            Assert.IsFalse(Cone(new Vector2(0.1f, 0f), radius: -1f));
        }

        // --- teleport ----------------------------------------------------------------------

        [Test]
        public void ABlinkWithinRangeLandsOnTheCursor()
        {
            var aim = new Vector2(3f, 4f);     // 5 units away
            Assert.AreEqual(aim, TeleportMath.Resolve(Origin, aim, 6f));
        }

        [Test]
        public void ABlinkBeyondRangeTravelsTheFullDistanceInThatDirection()
        {
            // Aiming further must not refuse the cast; it blinks as far as it can, so the skill
            // does not demand pixel-accurate cursor placement.
            Vector2 result = TeleportMath.Resolve(Origin, new Vector2(100f, 0f), 6f);

            Assert.AreEqual(6f, result.magnitude, 1e-4f);
            Assert.AreEqual(0f, result.y, 1e-4f);
        }

        [Test]
        public void ABlinkAimedAtYourOwnFeetStaysPutWithoutProducingNaN()
        {
            Vector2 result = TeleportMath.Resolve(Origin, Origin, 6f);

            Assert.AreEqual(Origin, result);
            Assert.IsFalse(float.IsNaN(result.x) || float.IsNaN(result.y));
        }

        [Test]
        public void ABlinkWithNoRangeStaysPut()
        {
            Assert.AreEqual(Origin, TeleportMath.Resolve(Origin, new Vector2(5f, 0f), 0f));
        }

        // --- field schedule ----------------------------------------------------------------

        [Test]
        public void AFieldDamagesOnTheTickItAppears()
        {
            Assert.IsTrue(FieldSchedule.Of(300, 30).DamagesOnTick(0));
        }

        [Test]
        public void AFieldDamagesOnceEveryIntervalAndNotInBetween()
        {
            FieldSchedule schedule = FieldSchedule.Of(300, 30);

            Assert.IsTrue(schedule.DamagesOnTick(30));
            Assert.IsFalse(schedule.DamagesOnTick(29));
            Assert.IsFalse(schedule.DamagesOnTick(31));
        }

        [Test]
        public void AFieldDoesNotGetAFreeTickOnTheTickItExpires()
        {
            // 60 ticks at an interval of 20 is three events: 0, 20 and 40. A fourth at tick 60
            // would be a 33% damage increase that no number in the data says anything about.
            FieldSchedule schedule = FieldSchedule.Of(60, 20);

            Assert.IsFalse(schedule.DamagesOnTick(60));
            Assert.IsTrue(schedule.IsExpired(60));
            Assert.AreEqual(3, schedule.DamageEventCount());
        }

        [Test]
        public void TheCountedEventsMatchWhatTickingActuallyProduces()
        {
            FieldSchedule schedule = FieldSchedule.Of(300, 30);

            int counted = 0;
            for (int elapsed = 0; !schedule.IsExpired(elapsed); ++elapsed)
                if (schedule.DamagesOnTick(elapsed))
                    counted++;

            Assert.AreEqual(schedule.DamageEventCount(), counted);
            Assert.AreEqual(10, counted, "A 5 second field damaging twice a second is 10 events.");
        }

        [Test]
        public void AFieldWhoseIntervalOutlastsItDamagesExactlyOnce()
        {
            FieldSchedule schedule = FieldSchedule.Of(30, 90);

            Assert.IsTrue(schedule.DamagesOnTick(0));
            Assert.AreEqual(1, schedule.DamageEventCount());
        }

        [Test]
        public void AFieldIsNotExpiredBeforeItsLastTick()
        {
            FieldSchedule schedule = FieldSchedule.Of(60, 20);

            Assert.IsFalse(schedule.IsExpired(59));
            Assert.IsTrue(schedule.IsExpired(60));
        }

        [Test]
        public void AFieldWithNoDurationOrNoIntervalIsRefused()
        {
            Assert.Throws<InvalidOperationException>(() => FieldSchedule.Of(0, 30).Validate());
            Assert.Throws<InvalidOperationException>(() => FieldSchedule.Of(300, 0).Validate());
        }

        // --- shape validation --------------------------------------------------------------

        [Test]
        public void ATeleportThatIsNotMarkedAsAMovementSkillIsRefused()
        {
            // The rule this protects: docs/02 section 5 has 속박 (Root) deny movement skills
            // specifically, and the loadout reads that from the definition's flag. A blink
            // without it would be castable while rooted, removing the counter to a blink, and
            // nothing about the geometry would look wrong.
            var shape = new SkillShape
            {
                Kind = SkillEffectKind.Teleport,
                TeleportMaxDistanceUnits = 6f,
            };

            Assert.Throws<InvalidOperationException>(() => shape.Validate(isMovementSkill: false));
            Assert.DoesNotThrow(() => shape.Validate(isMovementSkill: true));
        }

        [Test]
        public void ATeleportWithNoRangeIsRefused()
        {
            var shape = new SkillShape { Kind = SkillEffectKind.Teleport, TeleportMaxDistanceUnits = 0f };

            Assert.Throws<InvalidOperationException>(() => shape.Validate(isMovementSkill: true));
        }

        [Test]
        public void AConeNeedsARadiusAndAUsableAngle()
        {
            var noRadius = new SkillShape
            {
                Kind = SkillEffectKind.ConeDamage,
                RadiusUnits = 0f,
                ConeHalfAngleDegrees = 45f,
            };
            var noAngle = new SkillShape
            {
                Kind = SkillEffectKind.ConeDamage,
                RadiusUnits = 5f,
                ConeHalfAngleDegrees = 0f,
            };
            var tooWide = new SkillShape
            {
                Kind = SkillEffectKind.ConeDamage,
                RadiusUnits = 5f,
                ConeHalfAngleDegrees = 181f,
            };

            Assert.Throws<InvalidOperationException>(() => noRadius.Validate(false));
            Assert.Throws<InvalidOperationException>(() => noAngle.Validate(false));
            Assert.Throws<InvalidOperationException>(() => tooWide.Validate(false));
        }

        [Test]
        public void AFieldNeedsARadiusAndASchedule()
        {
            var noRadius = new SkillShape
            {
                Kind = SkillEffectKind.PersistentField,
                RadiusUnits = 0f,
                Field = FieldSchedule.Of(300, 30),
            };
            var noSchedule = new SkillShape
            {
                Kind = SkillEffectKind.PersistentField,
                RadiusUnits = 3f,
                Field = FieldSchedule.Of(300, 0),
            };

            Assert.Throws<InvalidOperationException>(() => noRadius.Validate(false));
            Assert.Throws<InvalidOperationException>(() => noSchedule.Validate(false));
        }

        [Test]
        public void AShapeWithNoEffectNeedsNothing()
        {
            Assert.DoesNotThrow(() => new SkillShape { Kind = SkillEffectKind.None }.Validate(false));
        }

        // --- applied status ----------------------------------------------------------------

        [Test]
        public void AStatusWithAZeroTickOverrideIsRefused()
        {
            // Zero would apply an effect that expires before it does anything, which reads in
            // play as a status that silently does not work. A negative value is the way to ask
            // for the catalogue duration.
            var status = new SkillStatusEffect
            {
                Id = StatusEffectId.Slow,
                Magnitude = 0.3f,
                DurationTicksOverride = 0,
            };

            Assert.Throws<InvalidOperationException>(() => status.Validate());
        }

        [Test]
        public void ASkillThatAppliesNoStatusIsValidAndAppliesNothing()
        {
            Assert.IsFalse(SkillStatusEffect.None.Applies);
            Assert.DoesNotThrow(() => SkillStatusEffect.None.Validate());
        }

        [Test]
        public void AStatusWithANegativeOverrideUsesTheCatalogueDuration()
        {
            var status = new SkillStatusEffect
            {
                Id = StatusEffectId.Slow,
                Magnitude = 0.3f,
                DurationTicksOverride = -1,
            };

            Assert.IsTrue(status.Applies);
            Assert.DoesNotThrow(() => status.Validate());
        }
    }
}
