using System;
using NeonVoid.Combat.Skills;
using NeonVoid.Core;
using UnityEngine;

namespace NeonVoid.Combat.AI
{
    /// <summary>The shapes a boss pattern can take. docs/01 describes 부채꼴, 직선 and 펄스.</summary>
    public enum BossPatternShape
    {
        /// <summary>부채꼴 - a fan in front of the boss. CPU's 붉은 부채꼴 코어 열기.</summary>
        Fan = 0,

        /// <summary>직선 - a band along the boss's facing. CPU's 직선 펄스 회로.</summary>
        Line = 1,

        /// <summary>
        /// 펄스 - an annulus around the boss. With an inner radius above zero the middle is safe,
        /// which turns a pulse into a decision about where to stand.
        /// </summary>
        Ring = 2,
    }

    /// <summary>
    /// One boss attack as data: its shape, how long it announces itself, and what it does.
    /// <para>
    /// docs/01 section 3's 보스 전투 공통 계약, which this type exists to make structural rather
    /// than a list somebody has to remember:
    /// </para>
    /// <list type="bullet">
    /// <item><b>0.8~1.5초 피할 수 있는 텔레그래프.</b> <see cref="Validate"/> refuses a telegraph
    /// outside that band, so a pattern that cannot be dodged cannot be written down.</item>
    /// <item><b>전조 시각 표시와 실제 충돌 볼륨이 일치.</b> <see cref="Covers"/> is the only
    /// function that answers "is this point in the attack", and both the telegraph and the
    /// resolution call it. They cannot drift apart, because there is nothing to drift.</item>
    /// <item><b>보스 커스텀 이펙트/사운드를 제거해도 전투 판정 동일.</b> The shape is these
    /// numbers. No particle, mesh or sound is consulted, and none exists to remove.</item>
    /// <item><b>안전 영역을 색만으로 구분하지 않는다.</b> Not a presentation type's job alone -
    /// the shape is expressible as geometry (an angle, a width, a radius), which is what lets a
    /// HUD draw an outline and a gauge rather than tint the floor.</item>
    /// </list>
    /// Pure data: no Unity object, no scene, no RNG.
    /// </summary>
    [Serializable]
    public struct BossPattern
    {
        /// <summary>0.8초 at 60 Hz. docs/01 section 3's floor on a dodgeable telegraph.</summary>
        public const int MinTelegraphTicks = 48;

        /// <summary>1.5초 at 60 Hz. The document's ceiling.</summary>
        public const int MaxTelegraphTicks = 90;

        [Tooltip("The pattern's name in docs/01, for logs and the HUD.")]
        public string Name;

        public BossPatternShape Shape;

        [Tooltip("Ticks of warning. [확정 shape] 0.8~1.5s, so 48~90.")]
        public int TelegraphTicks;

        [Tooltip("회복/반격 창 after the hit lands, in ticks. The player's turn.")]
        public int RecoveryTicks;

        [Tooltip("Fan radius, line length, or ring outer radius, in units.")]
        public float RangeUnits;

        [Tooltip("Fan half-angle in degrees.")]
        public float HalfAngleDegrees;

        [Tooltip("Line half-width in units.")]
        public float HalfWidthUnits;

        [Tooltip("Ring inner radius - the safe middle. Zero for a solid disc.")]
        public float InnerRadiusUnits;

        [Tooltip(
            "The shape marks the SAFE area and everything else within RangeUnits is the attack. " +
            "docs/01 gives the GPU a 회전 팬형 안전 영역 and the SSD 안전 공간 축소·확대, and a " +
            "'get into the wedge' attack is a different problem from 'get out of the wedge'. " +
            "Fan and Line only - a Ring already says it with InnerRadiusUnits.")]
        public bool SafeInsideShape;

        [Tooltip("What it deals, resolved through the normal damage pipeline.")]
        public DamageRequest Damage;

        /// <summary>Seconds of telegraph, for a reader and for the HUD's gauge.</summary>
        public float TelegraphSeconds => TelegraphTicks / (float)DesignConstants.SimulationTicksPerSecond;

        /// <summary>
        /// Whether a point is inside this attack. <b>The single source of the attack's volume</b>
        /// - see the type's note on 전조 표시와 충돌 볼륨의 일치.
        /// </summary>
        public bool Covers(Vector2 originXZ, Vector2 facingXZ, Vector2 pointXZ)
        {
            bool insideShape = InsideShape(originXZ, facingXZ, pointXZ);
            if (!SafeInsideShape)
                return insideShape;

            // The shape is the safe cut-out, so the attack is the rest of the disc of radius
            // RangeUnits. Bounded rather than infinite: an attack with no outer edge could not
            // be dodged by running, which is docs/01's 피할 수 있는 텔레그래프 in another form.
            if (insideShape)
                return false;

            return LineTargeting.IsInsideRing(originXZ, pointXZ, 0f, RangeUnits);
        }

        private bool InsideShape(Vector2 originXZ, Vector2 facingXZ, Vector2 pointXZ)
        {
            switch (Shape)
            {
                case BossPatternShape.Fan:
                    return ConeTargeting.IsInsideCone(originXZ, facingXZ, pointXZ, RangeUnits, HalfAngleDegrees);

                case BossPatternShape.Line:
                    return LineTargeting.IsInsideLine(originXZ, facingXZ, pointXZ, RangeUnits, HalfWidthUnits);

                case BossPatternShape.Ring:
                    return LineTargeting.IsInsideRing(originXZ, pointXZ, InnerRadiusUnits, RangeUnits);

                default:
                    return false;
            }
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
                throw new InvalidOperationException("A pattern needs its docs/01 name; a HUD has to say what is coming.");
            if (!Enum.IsDefined(typeof(BossPatternShape), Shape))
                throw new InvalidOperationException($"{Name}: unknown shape {Shape}.");

            if (TelegraphTicks < MinTelegraphTicks || TelegraphTicks > MaxTelegraphTicks)
            {
                throw new InvalidOperationException(
                    $"{Name}: a telegraph of {TelegraphTicks} ticks is outside docs/01's " +
                    $"0.8~1.5s ({MinTelegraphTicks}~{MaxTelegraphTicks}). Below it the attack cannot be " +
                    "dodged; above it the fight stops being a fight.");
            }

            if (RecoveryTicks < 1)
            {
                throw new InvalidOperationException(
                    $"{Name}: docs/01 asks for a 회복/반격 창 after the hit. Without one the player never " +
                    "gets a turn.");
            }

            if (float.IsNaN(RangeUnits) || RangeUnits <= 0f)
                throw new InvalidOperationException($"{Name}: a pattern with no reach hits nothing.");

            switch (Shape)
            {
                case BossPatternShape.Fan:
                    if (HalfAngleDegrees <= 0f || HalfAngleDegrees > 180f)
                        throw new InvalidOperationException($"{Name}: a fan's half-angle is between 0 and 180.");
                    if (SafeInsideShape && HalfAngleDegrees >= 180f)
                    {
                        throw new InvalidOperationException(
                            $"{Name}: a safe wedge of {HalfAngleDegrees} degrees covers everything, so the " +
                            "attack would be nothing at all.");
                    }

                    break;

                case BossPatternShape.Line:
                    if (HalfWidthUnits <= 0f)
                        throw new InvalidOperationException($"{Name}: a line with no width hits nothing.");
                    if (SafeInsideShape && HalfWidthUnits >= RangeUnits)
                    {
                        throw new InvalidOperationException(
                            $"{Name}: a safe band {HalfWidthUnits} wide fills a disc of radius {RangeUnits}, " +
                            "so the attack would be nothing at all.");
                    }

                    break;

                case BossPatternShape.Ring:
                    if (SafeInsideShape)
                    {
                        // A ring already says "the middle is safe" with InnerRadiusUnits, and
                        // the inverted version would be a disc with a hole around the boss plus
                        // a hole beyond it - two safe zones, which no document asks for.
                        throw new InvalidOperationException(
                            $"{Name}: a ring marks its safe middle with InnerRadiusUnits; inverting it as " +
                            "well would mean two safe zones.");
                    }

                    if (InnerRadiusUnits < 0f || InnerRadiusUnits >= RangeUnits)
                    {
                        throw new InvalidOperationException(
                            $"{Name}: a ring's safe middle ({InnerRadiusUnits}) has to be inside its outer " +
                            $"radius ({RangeUnits}).");
                    }

                    break;
            }

            // A pattern that fires, telegraphs, and does nothing is the silent no-op shape: it
            // would read as implemented in every log and change nothing on screen.
            if (Damage.Flat <= 0f && Damage.AdRatio <= 0f && Damage.ApRatio <= 0f)
                throw new InvalidOperationException($"{Name}: a pattern that deals nothing would telegraph for show.");

            Damage.Validate();
        }

        public override string ToString() =>
            $"{Name} ({Shape}{(SafeInsideShape ? " safe-inside" : string.Empty)}, " +
            $"{TelegraphSeconds:0.0#}s telegraph, {RangeUnits:0.#}u)";
    }
}
