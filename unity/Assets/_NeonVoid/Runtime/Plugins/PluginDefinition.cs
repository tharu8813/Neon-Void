using System;
using NeonVoid.Combat;
using NeonVoid.Combat.Proc;
using NeonVoid.Combat.Status;
using UnityEngine;

namespace NeonVoid.Plugins
{
    /// <summary>What kind of hit sets a plugin off.</summary>
    public enum PluginTriggerKind
    {
        None = 0,

        /// <summary>탄환 적중 시 - a basic attack projectile landing. G10, G17.</summary>
        OnProjectileHit = 1,

        /// <summary>타격 시 - any hit this actor lands. G12.</summary>
        OnAnyHit = 2,

        /// <summary>스킬 피해 적중 시 - an active skill landing. G14 and others.</summary>
        OnSkillHit = 3,
    }

    /// <summary>What the plugin does once it has been allowed to fire.</summary>
    public enum PluginEffectKind
    {
        None = 0,

        /// <summary>Damage to one other enemy near the target. G10 전위 연쇄.</summary>
        ChainToNearbyEnemy = 1,

        /// <summary>Damage to everything around the target. G17 폭발 각인.</summary>
        ExplodeAroundTarget = 2,

        /// <summary>A status effect on the target. G12 부식 표식.</summary>
        MarkTarget = 3,

        /// <summary>
        /// Nothing happens when a hit lands; the plugin contributes to the stat snapshot simply
        /// by being carried. G01 랜덤 패치, G02 경량화, G04 취약점 분석, R01 처형 알고리즘.
        /// </summary>
        Passive = 4,
    }

    /// <summary>
    /// One plugin as data, keyed by its document id.
    /// <para>
    /// <see cref="ProposalId"/> is the id from docs/03 (G10, R06, L01). The document is explicit
    /// that those are <b>proposal</b> ids for development and that a stable id must be approved
    /// before anything saves them, so this field is for traceability and must not become the
    /// save key.
    /// </para>
    /// <para>
    /// Every number that the original design states is reproduced exactly: G10 and G17 both
    /// carry 대상별 내부 CD 3초, G12 lasts 4초. Numbers the document does not state are marked
    /// [초안] on the field and may be retuned freely.
    /// </para>
    /// </summary>
    [Serializable]
    public struct PluginDefinition
    {
        [Tooltip("Development id from docs/03, e.g. G10. Never a save key.")]
        public string ProposalId;

        public PluginRarity Rarity;

        public PluginTriggerKind Trigger;

        public PluginEffectKind Effect;

        [Tooltip("Proc identity and chain rules, including the internal cooldown.")]
        public ProcEffectRule Rule;

        [Tooltip("Damage the effect requests, resolved through the normal pipeline.")]
        public DamageRequest Damage;

        [Tooltip("[초안] reach of the effect in units, where it has one.")]
        public float RadiusUnits;

        [Tooltip("Status applied by a marking effect, or None.")]
        public StatusEffectId AppliedStatus;

        [Tooltip("[초안] strength of the applied status.")]
        public float StatusMagnitude;

        [Tooltip("Ticks; negative uses the catalogue duration.")]
        public int StatusDurationTicksOverride;

        [Tooltip("What this plugin contributes simply by being carried.")]
        public PluginPassive Passive;

        /// <summary>The effect id the proc queue knows this plugin by.</summary>
        public int EffectId => Rule.EffectId;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(ProposalId))
                throw new InvalidOperationException("A plugin needs its docs/03 id for traceability.");
            if (!Enum.IsDefined(typeof(PluginRarity), Rarity))
                throw new InvalidOperationException($"{ProposalId}: unknown rarity.");
            // A passive has nothing to trigger it, by definition; everything else must say
            // what sets it off or it can never do anything.
            if (Trigger == PluginTriggerKind.None && Effect != PluginEffectKind.Passive)
                throw new InvalidOperationException($"{ProposalId}: a plugin that nothing triggers would never fire.");

            if (Effect == PluginEffectKind.Passive && Trigger != PluginTriggerKind.None)
                throw new InvalidOperationException(
                    $"{ProposalId}: a passive is not triggered by hits; leave the trigger as None so the queue never sees it.");
            if (Effect == PluginEffectKind.None)
                throw new InvalidOperationException($"{ProposalId}: a plugin that does nothing when it fires is not a plugin.");

            Damage.Validate();
            Passive.Validate();

            // A passive never reaches the proc queue, so it needs no chain identity. Validating
            // the rule anyway would force every passive to carry a meaningless effect id.
            if (Effect != PluginEffectKind.Passive)
                Rule.Validate();

            switch (Effect)
            {
                case PluginEffectKind.ChainToNearbyEnemy:
                case PluginEffectKind.ExplodeAroundTarget:
                    if (float.IsNaN(RadiusUnits) || RadiusUnits <= 0f)
                        throw new InvalidOperationException($"{ProposalId}: this effect needs a radius above zero.");
                    break;

                case PluginEffectKind.Passive:
                    if (Passive.IsEmpty)
                        throw new InvalidOperationException(
                            $"{ProposalId}: a passive that contributes nothing is not a plugin.");
                    break;

                case PluginEffectKind.MarkTarget:
                    if (AppliedStatus == StatusEffectId.None)
                        throw new InvalidOperationException($"{ProposalId}: a marking plugin must name the status it applies.");
                    if (StatusDurationTicksOverride == 0)
                        throw new InvalidOperationException(
                            $"{ProposalId}: a 0 tick status expires before it does anything; use a negative value for the catalogue duration.");
                    break;
            }

            // A chaining effect reaching new targets needs its internal cooldown, or the same
            // enemy is hit again the moment a new chain starts - which no cycle rule can catch,
            // because each activation is a fresh chain with its own path.
            if (Effect == PluginEffectKind.ChainToNearbyEnemy && Rule.InternalCooldownTicks < 1)
                throw new InvalidOperationException(
                    $"{ProposalId}: a chaining effect without an internal cooldown can re-hit the same target every tick.");
        }
    }
}
