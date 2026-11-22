using System.Collections.Generic;
using NeonVoid.Combat;
using NeonVoid.Combat.Projectiles;
using NeonVoid.Combat.Stats;
using NeonVoid.Core;

namespace NeonVoid.Plugins
{
    /// <summary>Why a grant was refused, or that it was accepted.</summary>
    public enum PluginGrantVerdict
    {
        Granted = 0,

        /// <summary>The definition itself is not usable; the message says what.</summary>
        RejectedInvalid = 1,

        /// <summary>
        /// 신화. The grade is confirmed, the cards are not: docs/03 section 2 keeps them as
        /// drafts and says explicitly that they must not appear as real cards before approval,
        /// which is D-008.
        /// </summary>
        RejectedMythicNotApproved = 2,

        /// <summary>This plugin is already carried. Levels, not duplicates, are the plan.</summary>
        RejectedAlreadyCarried = 3,
    }

    /// <summary>
    /// The plugins an actor is carrying into a mission, and which of them a hit sets off.
    /// <para>
    /// Pure: no Unity types, no scene, no queue. Whether a plugin triggers is decided here so
    /// it can be tested exhaustively, and what the plugin then does to the world is somebody
    /// else's job.
    /// </para>
    /// <para>
    /// Mythic is refused <b>here</b>, at the only door into a loadout, rather than being
    /// filtered out of a reward pool later. A pool filter protects one path; the refusal
    /// protects every path, including a debug menu or a test that hands one over directly.
    /// </para>
    /// </summary>
    public sealed class PluginLoadout
    {
        private readonly List<PluginDefinition> _carried = new List<PluginDefinition>();

        public int Count => _carried.Count;

        public PluginDefinition At(int index) => _carried[index];

        public IReadOnlyList<PluginDefinition> Carried => _carried;

        /// <summary>Whether a plugin with this proposal id is carried.</summary>
        public bool Carries(string proposalId)
        {
            for (int i = 0; i < _carried.Count; ++i)
                if (string.Equals(_carried[i].ProposalId, proposalId, System.StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>
        /// Adds a plugin. <paramref name="reason"/> carries the detail for a refusal, so a
        /// caller can log why rather than guessing.
        /// </summary>
        public PluginGrantVerdict TryGrant(in PluginDefinition definition, out string reason) =>
            TryGrant(definition, null, out reason);

        /// <summary>
        /// Adds a plugin, rolling anything it leaves to chance from
        /// <paramref name="rewardRng"/>.
        /// <para>
        /// Rolled once, here, and stored on the carried copy. A plugin that re-rolled whenever
        /// its stats were read would change under the player mid-mission, and one that rolled
        /// from a shared clock would differ on a replay of the same seed.
        /// </para>
        /// </summary>
        public PluginGrantVerdict TryGrant(
            in PluginDefinition definition, DeterministicRng rewardRng, out string reason)
        {
            reason = string.Empty;

            if (definition.Rarity == PluginRarity.Mythic)
            {
                reason = $"{definition.ProposalId}: 신화 cards are unapproved (docs/03 section 2, D-008).";
                return PluginGrantVerdict.RejectedMythicNotApproved;
            }

            try
            {
                definition.Validate();
            }
            catch (System.InvalidOperationException error)
            {
                reason = error.Message;
                return PluginGrantVerdict.RejectedInvalid;
            }

            if (Carries(definition.ProposalId))
            {
                reason = $"{definition.ProposalId} is already carried.";
                return PluginGrantVerdict.RejectedAlreadyCarried;
            }

            PluginDefinition carried = definition;
            carried.Passive = definition.Passive.Rolled(rewardRng);
            _carried.Add(carried);

            GrantTheRareBonus(carried, rewardRng);
            return PluginGrantVerdict.Granted;
        }

        /// <summary>
        /// What G15 희귀 획득 gives, the moment it is carried.
        /// <para>
        /// [확정] D-003 (2026-11-22): one 레어, nothing when there is no candidate, never twice.
        /// Granted after the card itself is in the list, so the bonus cannot be the card that
        /// is granting it and <see cref="BonusFrom"/> reads as the carrier already holding both.
        /// </para>
        /// <para>
        /// <b>Added directly rather than through TryGrant.</b> The bonus is part of the same
        /// grant - [확정] D-003 calls it 동일 영수증 - and recursing would let a chain of
        /// granting cards resolve inside one call, with no bound on its length.
        /// </para>
        /// </summary>
        private void GrantTheRareBonus(in PluginDefinition granting, DeterministicRng rewardRng)
        {
            LastRareBonus = string.Empty;
            if (granting.Effect != PluginEffectKind.GrantRandomRare)
                return;

            PluginDefinition bonus = RareBonus.Pick(PluginCatalog.All(), _carried, rewardRng);
            if (string.IsNullOrEmpty(bonus.ProposalId))
                return;

            bonus.Passive = bonus.Passive.Rolled(rewardRng);
            _carried.Add(bonus);
            LastRareBonus = bonus.ProposalId;
        }

        /// <summary>
        /// The 레어 the last grant also gave, or empty. [확정] D-003's 추가 지급 is one card and
        /// the caller has to be able to say which, or a receipt cannot name it.
        /// </summary>
        public string LastRareBonus { get; private set; } = string.Empty;

        /// <summary>
        /// Appends every carried plugin's passive stat contributions, in carry order.
        /// <para>
        /// Passives are collected separately from triggered effects because they reach a
        /// different place: the stat snapshot, not the proc queue.
        /// </para>
        /// </summary>
        /// <summary>
        /// Whether this card's contribution is behind a gate, and so must not be counted by the
        /// ungated aggregations.
        /// <para>
        /// Counting it in both places is not a doubling that shows up as a wrong number only
        /// when the gate is shut: it is a card that is permanently on at full strength plus a
        /// gate that adds it again. The gate would look like it worked, because the speed does
        /// change when you are hit.
        /// </para>
        /// </summary>
        private static bool IsGated(in PluginDefinition card) =>
            card.Effect == PluginEffectKind.ConditionalPassive
            || card.Effect == PluginEffectKind.ChargedPassive;

        /// <summary>Every carried card that fires on its own clock. docs/03 G05 주기 차폐막.</summary>
        public void CollectTriggeredByTimer(List<PluginDefinition> into)
        {
            if (into == null)
                return;

            for (int i = 0; i < _carried.Count; ++i)
            {
                if (_carried[i].Trigger == PluginTriggerKind.OnTimer)
                    into.Add(_carried[i]);
            }
        }

        /// <summary>Every carried card that fires on a successful cast. docs/03 G07 실행 차폐.</summary>
        public void CollectTriggeredBySkillCast(List<PluginDefinition> into)
        {
            if (into == null)
                return;

            for (int i = 0; i < _carried.Count; ++i)
            {
                if (_carried[i].Trigger == PluginTriggerKind.OnSkillCast)
                    into.Add(_carried[i]);
            }
        }

        /// <summary>
        /// Every carried card that fires when the carrier is hurt and low. The health check is
        /// not here: the loadout cannot see the carrier.
        /// </summary>
        public void CollectTriggeredByOwnerLowHealth(List<PluginDefinition> into)
        {
            if (into == null)
                return;

            for (int i = 0; i < _carried.Count; ++i)
            {
                if (_carried[i].Trigger == PluginTriggerKind.OnOwnerLowHealth)
                    into.Add(_carried[i]);
            }
        }

        /// <summary>
        /// Every carried card whose passive waits on a gauge. The gauge itself is not here: the
        /// loadout has no clock and hears nothing about hits.
        /// </summary>
        public void CollectChargedPassives(List<PluginDefinition> into)
        {
            if (into == null)
                return;

            for (int i = 0; i < _carried.Count; ++i)
            {
                if (_carried[i].Effect == PluginEffectKind.ChargedPassive)
                    into.Add(_carried[i]);
            }
        }

        public void CollectPassiveModifiers(List<StatModifier> into)
        {
            if (into == null)
                return;

            for (int i = 0; i < _carried.Count; ++i)
            {
                if (IsGated(_carried[i]))
                    continue;

                StatModifier[] modifiers = _carried[i].Passive.StatModifiers;
                if (modifiers == null)
                    continue;

                for (int m = 0; m < modifiers.Length; ++m)
                    into.Add(modifiers[m]);
            }
        }

        /// <summary>
        /// Total movement speed fraction the carried plugins add. Summed rather than
        /// multiplied: docs/03 G02 leaves 가산/배수 open, and adding keeps two copies of a
        /// movement plugin from compounding into something nobody tuned.
        /// </summary>
        public float PassiveMoveSpeedFraction()
        {
            float total = 0f;
            for (int i = 0; i < _carried.Count; ++i)
            {
                if (!IsGated(_carried[i]))
                    total += _carried[i].Passive.MoveSpeedFraction;
            }

            return total;
        }

        /// <summary>[초안] G06 스로틀 증폭: fraction added to the base attack rate.</summary>
        public float PassiveAttackSpeedFraction()
        {
            float total = 0f;
            for (int i = 0; i < _carried.Count; ++i)
            {
                if (!IsGated(_carried[i]))
                    total += _carried[i].Passive.AttackSpeedFraction;
            }

            return total;
        }

        /// <summary>[초안] G06 스로틀 증폭: fraction added to base projectile speed.</summary>
        public float PassiveProjectileSpeedFraction()
        {
            float total = 0f;
            for (int i = 0; i < _carried.Count; ++i)
            {
                if (!IsGated(_carried[i]))
                    total += _carried[i].Passive.ProjectileSpeedFraction;
            }

            return total;
        }

        /// <summary>[초안] G03 확장 탄두: fraction added to the shot's collision radius.</summary>
        public float PassiveProjectileSizeFraction()
        {
            float total = 0f;
            for (int i = 0; i < _carried.Count; ++i)
            {
                if (!IsGated(_carried[i]))
                    total += _carried[i].Passive.ProjectileSizeFraction;
            }

            return total;
        }

        /// <summary>
        /// Appends every carried plugin's extra volley shots, each with its own effect fraction.
        /// <para>
        /// One entry per card rather than a total, because the fraction belongs to the card:
        /// docs/03 gives R06 보조 탄환's extra shot 80% and G13 4중 캐시's burst 70%, so summing
        /// the counts would force one of those numbers onto the other card's shots.
        /// </para>
        /// </summary>
        public void CollectExtraShots(List<ExtraShotSpec> into)
        {
            if (into == null)
                return;

            for (int i = 0; i < _carried.Count; ++i)
            {
                PluginFiring firing = _carried[i].Firing;
                if (firing.AddsExtraShots)
                {
                    into.Add(new ExtraShotSpec(
                        firing.ExtraShotsPerVolley,
                        firing.ExtraShotOnHitScale,
                        firing.ExtraShotHomingTurnDegreesPerTick));
                }
            }
        }

        /// <summary>Appends every carried plugin that earns a burst from resolved hit effects.</summary>
        public void CollectBurstPlugins(List<PluginDefinition> into)
        {
            if (into == null)
                return;

            for (int i = 0; i < _carried.Count; ++i)
            {
                if (_carried[i].Firing.FiresBursts)
                    into.Add(_carried[i]);
            }
        }

        /// <summary>Drops everything, e.g. when a mission ends. Saves nothing.</summary>
        public void Clear() => _carried.Clear();

        /// <summary>
        /// Appends every carried plugin that this hit sets off, in carry order so the queue
        /// receives them in a reproducible sequence.
        /// </summary>
        public void CollectTriggered(in HitReport hit, List<PluginDefinition> into)
        {
            if (into == null)
                return;

            for (int i = 0; i < _carried.Count; ++i)
                if (Triggers(_carried[i].Trigger, hit))
                    into.Add(_carried[i]);
        }

        /// <summary>
        /// Appends every carried plugin that another plugin's effect resolving sets off, in
        /// carry order.
        /// <para>
        /// A separate collector from the hit one, because 적중 효과 발생 is not a hit: 부식 표식
        /// applying a mark is an 적중 효과 and removes no health at all, so it would never pass
        /// the <c>hit.Landed</c> gate the hit path correctly applies.
        /// </para>
        /// </summary>
        public void CollectTriggeredByEffectResolution(List<PluginDefinition> into)
        {
            if (into == null)
                return;

            for (int i = 0; i < _carried.Count; ++i)
                if (_carried[i].Trigger == PluginTriggerKind.OnHitEffectResolved)
                    into.Add(_carried[i]);
        }

        /// <summary>
        /// Appends every carried plugin that a dash touching an enemy sets off, in carry order.
        /// </summary>
        public void CollectTriggeredByDashContact(List<PluginDefinition> into)
        {
            if (into == null)
                return;

            for (int i = 0; i < _carried.Count; ++i)
                if (_carried[i].Trigger == PluginTriggerKind.OnDashContact)
                    into.Add(_carried[i]);
        }

        /// <summary>
        /// Appends every carried plugin that a finished movement action sets off, in carry
        /// order.
        /// </summary>
        public void CollectTriggeredByMovementAction(List<PluginDefinition> into)
        {
            if (into == null)
                return;

            for (int i = 0; i < _carried.Count; ++i)
                if (_carried[i].Trigger == PluginTriggerKind.OnMovementAction)
                    into.Add(_carried[i]);
        }

        /// <summary>
        /// Appends every carried plugin whose stat grows on a kill, in carry order.
        /// </summary>
        /// <summary>
        /// Every carried card whose contribution is gated. The gate itself is not evaluated
        /// here: the loadout has no clock and does not know what has happened to the carrier.
        /// </summary>
        public void CollectConditionalPassives(List<PluginDefinition> into)
        {
            if (into == null)
                return;

            for (int i = 0; i < _carried.Count; ++i)
            {
                if (_carried[i].Effect == PluginEffectKind.ConditionalPassive)
                    into.Add(_carried[i]);
            }
        }

        public void CollectKillGrowthPlugins(List<PluginDefinition> into)
        {
            if (into == null)
                return;

            for (int i = 0; i < _carried.Count; ++i)
            {
                PluginDefinition card = _carried[i];
                if (card.Trigger == PluginTriggerKind.OnKill
                    && card.Effect == PluginEffectKind.GrowingPassive)
                {
                    into.Add(card);
                }
            }
        }

        /// <summary>
        /// Whether a trigger kind matches a hit.
        /// <para>
        /// A hit that removed no health never triggers anything. An attack absorbed by 무적
        /// still "hit"; treating it as a proc source would turn an invulnerable target into a
        /// free source of chain lightning and explosions.
        /// </para>
        /// </summary>
        public static bool Triggers(PluginTriggerKind trigger, in HitReport hit)
        {
            if (!hit.Landed)
                return false;

            return trigger switch
            {
                PluginTriggerKind.OnProjectileHit => hit.Origin == HitOrigin.Projectile,
                PluginTriggerKind.OnSkillHit => hit.Origin == HitOrigin.Skill,
                PluginTriggerKind.OnAnyHit => hit.Origin != HitOrigin.Unknown,

                // The rest land here on purpose. A hit is not an effect resolving, not a dash
                // contact, not a movement action - and a kill grows a stat rather than
                // resolving in the queue, so OnKill is handled where the stat snapshot is
                // rebuilt instead of here.
                _ => false,
            };
        }
    }
}
