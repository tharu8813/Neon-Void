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

        /// <summary>
        /// 적중 효과 발생 시 - another plugin's on-hit effect resolving. R04 해체 프로토콜.
        /// <para>
        /// Not a hit, which is why it cannot be a <see cref="HitReport"/> trigger like the
        /// others: 부식 표식 applying a mark is an 적중 효과 and lands no damage at all. So it
        /// is evaluated where an effect is known to have resolved, and only there.
        /// </para>
        /// </summary>
        OnHitEffectResolved = 4,

        /// <summary>
        /// 대시 중 접촉 시 - the dasher reaching an enemy. G16 대시 폭발.
        /// <para>
        /// Not a hit either: nothing has struck anybody when a dash passes through a crowd.
        /// Evaluated per enemy the dash touches, once per dash, which is what docs/03's
        /// exception column asks for by name (한 대시에서 중복 접촉).
        /// </para>
        /// </summary>
        OnDashContact = 5,
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

        /// <summary>
        /// Changes the basic attack itself - extra projectiles, bursts - rather than a stat or
        /// an on-hit effect. R06 보조 탄환, G13 4중 캐시.
        /// </summary>
        FiringModifier = 5,

        /// <summary>
        /// Damage to the event's own target and nothing around it. G16 대시 폭발, once per
        /// enemy the dash touched.
        /// <para>
        /// Not <see cref="ExplodeAroundTarget"/>: a dash through five enemies would then be
        /// five overlapping explosions, each catching the others, and the damage a crowd takes
        /// would grow with the square of its size. docs/03 says 대시 경로 폭발 피해 - the path
        /// is the area, so the area is swept once and each enemy in it is struck once.
        /// </para>
        /// </summary>
        DamageTarget = 6,
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

        [Tooltip("What this plugin does to the basic attack: extra projectiles or bursts.")]
        public PluginFiring Firing;

        /// <summary>The effect id the proc queue knows this plugin by.</summary>
        public int EffectId => Rule.EffectId;

        /// <summary>
        /// Whether this plugin ever becomes a proc event.
        /// <para>
        /// Asked here rather than by each caller comparing against the kinds, because the two
        /// kinds that never reach the queue carry no chain rule - and the caller that forgot
        /// would hand the guard an empty rule and throw on an effect id of 0. Which is exactly
        /// what happened when the firing modifiers were added.
        /// </para>
        /// </summary>
        public bool ReachesProcQueue =>
            Effect != PluginEffectKind.Passive && Effect != PluginEffectKind.FiringModifier;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(ProposalId))
                throw new InvalidOperationException("A plugin needs its docs/03 id for traceability.");
            if (!Enum.IsDefined(typeof(PluginRarity), Rarity))
                throw new InvalidOperationException($"{ProposalId}: unknown rarity.");
            // A passive and a firing modifier have nothing to trigger them, by definition;
            // everything else must say what sets it off or it can never do anything.
            bool untriggered = Effect == PluginEffectKind.Passive || Effect == PluginEffectKind.FiringModifier;
            if (Trigger == PluginTriggerKind.None && !untriggered)
                throw new InvalidOperationException($"{ProposalId}: a plugin that nothing triggers would never fire.");

            if (untriggered && Trigger != PluginTriggerKind.None)
                throw new InvalidOperationException(
                    $"{ProposalId}: this kind is not triggered by hits; leave the trigger as None so the queue never sees it.");
            if (Effect == PluginEffectKind.None)
                throw new InvalidOperationException($"{ProposalId}: a plugin that does nothing when it fires is not a plugin.");

            // The dash path's reach is this radius: with none, the sweep finds nobody and the
            // card fires for no enemy ever. That is the silent no-op shape, so the data
            // refuses it rather than shipping a plugin that does nothing.
            if (Trigger == PluginTriggerKind.OnDashContact && (float.IsNaN(RadiusUnits) || RadiusUnits <= 0f))
            {
                throw new InvalidOperationException(
                    $"{ProposalId}: a dash effect needs a reach above zero, or the path touches nothing.");
            }

            Damage.Validate();
            Passive.Validate();
            Firing.Validate(ProposalId);

            // Only the kind that is about firing may describe firing. A chaining card that
            // quietly also added projectiles would be doing something no reader of its row in
            // docs/03 would expect.
            if (Effect != PluginEffectKind.FiringModifier && !Firing.IsEmpty)
            {
                throw new InvalidOperationException(
                    $"{ProposalId}: only a {nameof(PluginEffectKind.FiringModifier)} may change the basic attack.");
            }

            // Neither untriggered kind reaches the proc queue, so neither needs a chain
            // identity. Validating the rule anyway would force them to carry a meaningless
            // effect id.
            if (ReachesProcQueue)
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

                case PluginEffectKind.FiringModifier:
                    if (Firing.IsEmpty)
                        throw new InvalidOperationException(
                            $"{ProposalId}: a firing modifier that changes nothing about firing is not a plugin.");
                    break;

                case PluginEffectKind.DamageTarget:
                    // A damaging effect that deals nothing is the silent no-op shape again: it
                    // fires, it is counted, it reports, and the enemy is untouched.
                    if (Damage.Flat <= 0f && Damage.AdRatio <= 0f && Damage.ApRatio <= 0f)
                        throw new InvalidOperationException(
                            $"{ProposalId}: a damaging effect with no damage would fire and do nothing.");
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
