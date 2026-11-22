using System;
using System.Collections.Generic;

namespace NeonVoid.Plugins
{
    /// <summary>
    /// 플러그인 레벨.
    /// <para>
    /// [확정] docs/03 section 3 item 6: <i>같은 플러그인 획득 = 레벨 상승, 현재 최대로 강화된
    /// 플러그인은 후보에서 제거. 개별 maxLevel·다음 단계 효과는 데이터로 정의.</i> Three rules,
    /// and all three are structural: a duplicate levels up rather than being refused, a card at
    /// its ceiling stops being offered, and both the ceiling and the step live in data.
    /// </para>
    /// <para>
    /// <b>What the document does not give is the numbers.</b> No card has a stated maxLevel and
    /// no card has stated per-level values, so the ceiling and the step below are [초안] and
    /// live here, in one place, where approving real per-card data replaces a table rather than
    /// hunting through the catalogue.
    /// </para>
    /// <para>
    /// <b>The step scales magnitudes and nothing else.</b> A level multiplies what a card is
    /// worth - its passive fractions, its damage, its shield, its status strength. It does not
    /// touch durations, cooldowns, radii or counts: those change what a card <i>does</i> rather
    /// than how much, and docs/03 says 다음 단계 효과는 데이터로 정의 rather than giving a rule to
    /// infer one from. The single exception is written down as such below.
    /// </para>
    /// Pure C#: no Unity types.
    /// </summary>
    public static class PluginLevels
    {
        /// <summary>Every card starts here. A definition with no level is at this one.</summary>
        public const int FirstLevel = 1;

        /// <summary>
        /// [초안] how far a card goes when its own data says nothing. Three, because a card
        /// that can be taken twice more is a build decision the player can still feel, and
        /// docs/04 gives the run two 학습 slots - a ceiling far above that would make levelling
        /// the only thing a long run does.
        /// </summary>
        public const int DraftMaxLevel = 3;

        /// <summary>
        /// [초안] what each level past the first adds, as a fraction of the card's own value.
        /// Level 1 is 1.00, level 2 is 1.50, level 3 is 2.00.
        /// </summary>
        public const float DraftStepFraction = 0.5f;

        /// <summary>
        /// [초안] per-card ceilings. Empty: no card has a stated maxLevel, so every one takes
        /// <see cref="DraftMaxLevel"/>. The table exists because docs/03 says the ceiling is
        /// data - an approved one lands here and nothing else changes.
        /// </summary>
        private static readonly Dictionary<string, int> MaxLevels =
            new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>How far this card may be levelled.</summary>
        public static int MaxLevelFor(string proposalId)
        {
            if (!string.IsNullOrEmpty(proposalId) && MaxLevels.TryGetValue(proposalId, out int max))
                return max;

            return DraftMaxLevel;
        }

        /// <summary>A level that is inside the card's range. 0 and anything lower read as 1.</summary>
        public static int Clamp(string proposalId, int level)
        {
            int max = MaxLevelFor(proposalId);
            if (level <= FirstLevel)
                return FirstLevel;

            return level > max ? max : level;
        }

        /// <summary>Whether this card can still go up.</summary>
        public static bool CanLevel(string proposalId, int level) =>
            Clamp(proposalId, level) < MaxLevelFor(proposalId);

        /// <summary>
        /// What a card at this level is worth, as a multiplier of its level 1 values.
        /// </summary>
        public static float MultiplierFor(string proposalId, int level) =>
            1f + (Clamp(proposalId, level) - FirstLevel) * DraftStepFraction;

        /// <summary>
        /// The same card at another level, scaled from the level it is at now.
        /// <para>
        /// <b>Scaled from its current values, not rebuilt from the catalogue.</b> G01 랜덤 패치
        /// rolled its stats when it was granted, and rebuilding would roll them again - a
        /// level-up that silently replaced the player's three stats with three others is a
        /// different card, not a stronger one.
        /// </para>
        /// </summary>
        public static PluginDefinition At(in PluginDefinition card, int level)
        {
            int from = Clamp(card.ProposalId, card.Level);
            int to = Clamp(card.ProposalId, level);
            if (from == to)
            {
                PluginDefinition unchanged = card;
                unchanged.Level = to;
                return unchanged;
            }

            float ratio = MultiplierFor(card.ProposalId, to) / MultiplierFor(card.ProposalId, from);

            PluginDefinition scaled = card;
            scaled.Level = to;
            scaled.Passive = ScalePassive(card.Passive, ratio);
            scaled.Damage = ScaleDamage(card.Damage, ratio);
            scaled.StatusMagnitude = card.StatusMagnitude * ratio;

            PluginShieldGrant shield = card.Shield;
            shield.Amount = shield.Amount * ratio;
            scaled.Shield = shield;

            // [확정] docs/03 L02 부메랑 프로토콜: 레벨 상승 시 부메랑 수 증가. The one per-level
            // effect any card actually states, so it is the one count a level changes. Written
            // against the card's own data rather than hardcoded in the catalogue, so an approved
            // table can take it over.
            PluginFiring firing = card.Firing;
            if (firing.ShotConversion == PluginShotConversion.Boomerang && firing.MainShotCount > 0)
                firing.MainShotCount = firing.MainShotCount + (to - FirstLevel) - (from - FirstLevel);

            scaled.Firing = firing;
            return scaled;
        }

        /// <summary>The same card one level higher, or unchanged when it is already at its top.</summary>
        public static PluginDefinition LevelledUp(in PluginDefinition card) =>
            At(card, Clamp(card.ProposalId, card.Level) + 1);

        /// <summary>What a card's level is called on a screen.</summary>
        public static string LabelFor(string proposalId, int level)
        {
            int at = Clamp(proposalId, level);
            return at <= FirstLevel ? string.Empty : $"Lv.{at}";
        }

        private static PluginPassive ScalePassive(in PluginPassive passive, float ratio)
        {
            PluginPassive scaled = passive;
            scaled.MoveSpeedFraction = passive.MoveSpeedFraction * ratio;
            scaled.AttackSpeedFraction = passive.AttackSpeedFraction * ratio;
            scaled.ProjectileSpeedFraction = passive.ProjectileSpeedFraction * ratio;
            scaled.ProjectileSizeFraction = passive.ProjectileSizeFraction * ratio;
            scaled.RandomStatFraction = passive.RandomStatFraction * ratio;

            // The rolled modifiers themselves, not the roll: the card keeps the stats it rolled
            // and each one is worth more.
            if (passive.StatModifiers != null)
            {
                var modifiers = new Combat.Stats.StatModifier[passive.StatModifiers.Length];
                for (int i = 0; i < modifiers.Length; ++i)
                {
                    Combat.Stats.StatModifier one = passive.StatModifiers[i];
                    modifiers[i] = new Combat.Stats.StatModifier(
                        one.Stat, one.Flat * ratio, one.FractionOfBase * ratio);
                }

                scaled.StatModifiers = modifiers;
            }

            return scaled;
        }

        private static Combat.DamageRequest ScaleDamage(in Combat.DamageRequest damage, float ratio)
        {
            Combat.DamageRequest scaled = damage;
            scaled.Flat = damage.Flat * ratio;
            scaled.AdRatio = damage.AdRatio * ratio;
            scaled.ApRatio = damage.ApRatio * ratio;
            return scaled;
        }
    }
}
