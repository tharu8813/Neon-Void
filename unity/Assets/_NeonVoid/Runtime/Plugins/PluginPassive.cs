using System;
using NeonVoid.Combat.Stats;
using NeonVoid.Core;
using UnityEngine;

namespace NeonVoid.Plugins
{
    /// <summary>
    /// What a plugin does simply by being carried: no trigger, no proc event, just a
    /// contribution to the stat snapshot.
    /// <para>
    /// This is the 플러그인 stage of docs/02 section 2's pipeline, which until now nothing fed:
    /// the only contributions reaching the resolver came from status effects.
    /// </para>
    /// </summary>
    [Serializable]
    public struct PluginPassive
    {
        [Tooltip("Fixed contributions, applied as written.")]
        public StatModifier[] StatModifiers;

        [Tooltip("[초안] fraction added to base movement speed, e.g. 0.12 for +12%.")]
        public float MoveSpeedFraction;

        [Tooltip("How many distinct stats to roll. docs/03 G01 랜덤 패치 rolls 3.")]
        public int RandomStatCount;

        [Tooltip("[초안] fraction of base each rolled stat gains.")]
        public float RandomStatFraction;

        /// <summary>True when this plugin contributes nothing by being carried.</summary>
        public bool IsEmpty =>
            (StatModifiers == null || StatModifiers.Length == 0)
            && Mathf.Approximately(MoveSpeedFraction, 0f)
            && RandomStatCount <= 0;

        public static PluginPassive None => default;

        /// <summary>
        /// The stats 랜덤 패치 may roll. A fixed, ordered list, because the roll has to be
        /// reproducible: docs/06 keeps reward randomness in its own deterministic stream, and a
        /// candidate set that depended on enumeration order would re-roll differently on a
        /// replay even with the same seed.
        /// </summary>
        public static readonly ModifiedStat[] RandomCandidates =
        {
            ModifiedStat.AttackDamage,
            ModifiedStat.AbilityPower,
            ModifiedStat.PhysicalArmor,
            ModifiedStat.MagicResistance,
            ModifiedStat.SkillHaste,
            ModifiedStat.OmnivampPercent,
            ModifiedStat.CritChancePercent,
        };

        public void Validate()
        {
            if (!Mathf.Approximately(MoveSpeedFraction, 0f)
                && (float.IsNaN(MoveSpeedFraction) || float.IsInfinity(MoveSpeedFraction)))
                throw new InvalidOperationException($"{nameof(MoveSpeedFraction)} must be finite.");

            if (RandomStatCount < 0)
                throw new InvalidOperationException($"{nameof(RandomStatCount)} must not be negative.");

            if (RandomStatCount > RandomCandidates.Length)
                throw new InvalidOperationException(
                    $"{nameof(RandomStatCount)} of {RandomStatCount} exceeds the {RandomCandidates.Length} candidates, " +
                    "so distinct stats cannot be drawn. docs/03 G01 leaves 유효 후보 부족 open; refusing beats silently rolling fewer.");

            if (RandomStatCount > 0 && (RandomStatFraction <= 0f || float.IsNaN(RandomStatFraction)))
                throw new InvalidOperationException(
                    $"{nameof(RandomStatFraction)} must be above zero, or a rolled stat would gain nothing.");

            if (StatModifiers == null)
                return;

            for (int i = 0; i < StatModifiers.Length; ++i)
                if (!StatModifiers[i].IsUsable)
                    throw new InvalidOperationException(
                        $"Passive contribution {i} is not usable (NaN, infinite or unknown stat).");
        }

        /// <summary>
        /// Rolls <see cref="RandomStatCount"/> DISTINCT stats from the candidates and returns a
        /// passive with them appended.
        /// <para>
        /// Distinct, because docs/03 G01 leaves 같은 스탯 2회 선택 여부 open and rolling the
        /// same stat twice makes "3개 증가" a lie to the player reading the card. The duplicate
        /// question stays open; this is the reading that cannot mislead.
        /// </para>
        /// <para>
        /// Drawn with a partial Fisher-Yates over the candidate list: one draw per stat, no
        /// retry loop, so the number of RNG values consumed is fixed and a replay stays in step
        /// with the stream.
        /// </para>
        /// </summary>
        public PluginPassive Rolled(DeterministicRng rng)
        {
            if (RandomStatCount <= 0 || rng == null)
                return this;

            var pool = (ModifiedStat[])RandomCandidates.Clone();
            int remaining = pool.Length;
            var rolled = new StatModifier[RandomStatCount];

            for (int i = 0; i < RandomStatCount; ++i)
            {
                int pick = rng.NextInt(0, remaining);
                rolled[i] = StatModifier.Scale(pool[pick], RandomStatFraction);

                pool[pick] = pool[remaining - 1];
                remaining--;
            }

            int existing = StatModifiers != null ? StatModifiers.Length : 0;
            var combined = new StatModifier[existing + rolled.Length];
            for (int i = 0; i < existing; ++i)
                combined[i] = StatModifiers[i];
            for (int i = 0; i < rolled.Length; ++i)
                combined[existing + i] = rolled[i];

            return new PluginPassive
            {
                StatModifiers = combined,
                MoveSpeedFraction = MoveSpeedFraction,
                RandomStatCount = 0,          // already drawn; a second grant must not re-roll
                RandomStatFraction = RandomStatFraction,
            };
        }
    }
}
