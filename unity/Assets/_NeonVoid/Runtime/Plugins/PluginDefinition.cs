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

        /// <summary>
        /// 돌진/점멸/순간이동 후 - a movement action having finished. G08 이동 버퍼.
        /// <para>
        /// After, not during: docs/03 writes 후, and a buff applied while the dash is still
        /// running would be spent on ticks the dash was already moving at its own speed. So a
        /// dash raises it on the tick it ends, and a movement skill on the cast that went off.
        /// </para>
        /// <para>
        /// docs/03's list also includes 도약 and 투명 해제. There is no leap and no
        /// invisibility in the game, so those two are unreachable rather than handled - see
        /// <see cref="PluginCatalog.MovementBuffer"/>.
        /// </para>
        /// </summary>
        OnMovementAction = 6,

        /// <summary>
        /// 적 처치 시 - a hit of this actor's that killed. R02 킬 스택.
        /// <para>
        /// The hit report already knows: <see cref="HitReport.Killed"/> has been carried since
        /// the damage pipeline was written and nothing read it. A kill is the one trigger here
        /// that a hit really does describe.
        /// </para>
        /// </summary>
        OnKill = 7,

        /// <summary>
        /// The carrier is hurt and is at or below this card's health threshold. docs/03 R03
        /// 위기 반발 is 플레이어 HP 30% 이하일 때.
        /// <para>
        /// Raised on taking damage rather than polled every tick, so the card fires on the hit
        /// that put the carrier in danger. Polling would also fire it on the tick the threshold
        /// was crossed by a max-health change, which is not 위기.
        /// </para>
        /// </summary>
        OnOwnerLowHealth = 8,

        /// <summary>
        /// A fixed period has passed. docs/03 G05 주기 차폐막 is 30초마다 보호막, and docs/02
        /// section 7 states 보호막을 만드는 일반 플러그인 30초 주기 as [확정].
        /// </summary>
        OnTimer = 9,

        /// <summary>
        /// The carrier cast a skill and it went off. docs/03 G07 실행 차폐 is
        /// 스킬 사용 시 보호막 생성, whose exception column is 스킬 실패 시 발동하지 않음 -
        /// so this is raised from the cast that succeeded, not from the attempt.
        /// </summary>
        OnSkillCast = 10,
    }

    /// <summary>What the plugin does once it has been allowed to fire.</summary>
    public enum PluginEffectKind
    {
        None = 0,

        /// <summary>Damage to one other enemy near the target. G10 전위 연쇄.</summary>
        ChainToNearbyEnemy = 1,

        /// <summary>Damage to everything around the target. G17 폭발 각인.</summary>
        ExplodeAroundTarget = 2,

        /// <summary>
        /// Applies the named status to whoever the event targets. G12 부식 표식 debuffs the
        /// enemy that was hit; G08 이동 버퍼 buffs the player, whose own id the event carries.
        /// The kind is about applying a status, not about which side receives it.
        /// </summary>
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

        /// <summary>
        /// A stat contribution that grows with a counter instead of being fixed. R02 킬 스택.
        /// <para>
        /// Never reaches the proc queue: it is a term in the stat snapshot, recomputed from
        /// base like the fixed passives, not an effect that resolves.
        /// </para>
        /// </summary>
        GrowingPassive = 7,

        /// <summary>
        /// A passive that only contributes while a condition holds. G11 무피격 가속 is
        /// 이동속도 100% 증가, 피해를 받으면 비활성화.
        /// <para>
        /// A separate kind from <see cref="Passive"/> rather than a flag on it, so that the
        /// place which publishes the stat snapshot has to decide what to do about the gate.
        /// A condition nobody evaluated would be a card that is simply always on, which is
        /// the stronger half of this one.
        /// </para>
        /// </summary>
        ConditionalPassive = 8,

        /// <summary>
        /// Fires the carrier's basic attack, now. G14 추적 응답 is 스킬 피해 적중 시
        /// 자동발사 탄환 1개.
        /// <para>
        /// Not a <see cref="FiringModifier"/>: those change what every volley is, and are read
        /// off the loadout once. This one is an effect that resolves when something happens,
        /// so it goes through the proc queue and obeys its chain rules like any other.
        /// </para>
        /// </summary>
        FireProjectile = 9,

        /// <summary>
        /// A passive that is off until a gauge fills, then on for a while. docs/03 L03
        /// 피버타임 is 적중 또는 처치로 게이지, 충전 완료 후 일정시간 전체 능력치 증가.
        /// <para>
        /// Distinct from <see cref="ConditionalPassive"/>, whose gate is a fact about the
        /// carrier that it reads every tick. This one is a resource the carrier spends: it has
        /// to be filled, it is consumed when it fires, and it runs on a clock of its own.
        /// </para>
        /// </summary>
        ChargedPassive = 10,

        /// <summary>
        /// Shoves every hostile around the carrier away from them. docs/03 R03 위기 반발 is
        /// 주변 적 넉백, and deals no damage at all.
        /// </summary>
        KnockbackAroundOwner = 11,

        /// <summary>
        /// Gives the carrier a 보호막. docs/03 G05 주기 차폐막, G07 실행 차폐, R05 돌진 차폐.
        /// <para>
        /// Which shield a hit then eats is <see cref="ShieldPolicy"/>: [확정] 신규부터 as of
        /// docs/08 D-004a, with the refresh and expiry readings still open. The card grants;
        /// it does not decide what the stack does with what it granted.
        /// </para>
        /// </summary>
        GrantShield = 12,

        /// <summary>
        /// Heals the carrier. docs/03 G09 비상 복구's first reading.
        /// <para>
        /// The card says <i>회복 또는 방어 효과 [미확정]</i>, so both readings are implemented
        /// and <see cref="EmergencyRecoveryPolicy"/> chooses between them with one constant.
        /// A heal and a shield are different enough that guessing would be a balance decision
        /// nobody made: a heal is worth nothing at full health and a shield is worth the same
        /// either way.
        /// </para>
        /// </summary>
        HealOwner = 13,

        /// <summary>
        /// Grants another plugin the moment this one is carried. docs/03 G15 희귀 획득.
        /// <para>
        /// [확정] D-003 (2026-11-22): 추가 지급량은 1개, 유효 후보가 없으면 추가 지급 없음,
        /// 동일 영수증으로 중복 지급 불가. It happens at grant time rather than on a trigger,
        /// because 획득 is what the card does - there is nothing to wait for.
        /// </para>
        /// </summary>
        GrantRandomRare = 14,
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

        [Tooltip(
            "[확정] docs/03 3절 6항: 같은 플러그인 획득 = 레벨 상승. 0 and 1 are both the first " +
            "level, so a card authored without one is where every card starts.")]
        public int Level;

        [Tooltip("[초안] reach of the effect in units, where it has one.")]
        public float RadiusUnits;

        [Tooltip("Projectiles a FireProjectile effect fires. docs/03 G14 추적 응답 fires 1.")]
        public int ProjectilesFired;

        [Tooltip(
            "Health fraction at or below which an OnOwnerLowHealth card fires. docs/03 R03 " +
            "위기 반발 is 30%. 0 for every other trigger.")]
        public float OwnerHealthThresholdFraction;

        [Tooltip("[초안] units a knockback shoves an enemy. 0 for every other effect.")]
        public float KnockbackUnits;

        [Tooltip("The 보호막 a GrantShield effect gives. Empty for every other effect.")]
        public PluginShieldGrant Shield;

        [Tooltip("Ticks between firings of an OnTimer card. docs/03 G05 is 30초 = 1800. 0 otherwise.")]
        public int TimerPeriodTicks;

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

        [Tooltip("A stat contribution that grows with a counter, e.g. per kill.")]
        public PluginGrowth Growth;

        [Tooltip("What has to hold for a ConditionalPassive to contribute. None for every other kind.")]
        public PluginCondition Condition;

        [Tooltip("The gauge a ChargedPassive fills and spends. Empty for every other kind.")]
        public PluginCharge Charge;

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
            Effect != PluginEffectKind.Passive
            && Effect != PluginEffectKind.FiringModifier
            && Effect != PluginEffectKind.GrowingPassive
            && Effect != PluginEffectKind.ConditionalPassive
            && Effect != PluginEffectKind.ChargedPassive

            // Granted the moment the card is carried, so it never waits in the queue and has
            // no chain rule to register. [확정] D-003.
            && Effect != PluginEffectKind.GrantRandomRare;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(ProposalId))
                throw new InvalidOperationException("A plugin needs its docs/03 id for traceability.");
            if (!Enum.IsDefined(typeof(PluginRarity), Rarity))
                throw new InvalidOperationException($"{ProposalId}: unknown rarity.");
            // A passive and a firing modifier have nothing to trigger them, by definition;
            // everything else must say what sets it off or it can never do anything.
            // A growing passive IS triggered - by a kill - but still never reaches the queue, so
            // it is not in this set even though it shares the "no chain rule" half below.
            bool untriggered = Effect == PluginEffectKind.Passive
                || Effect == PluginEffectKind.FiringModifier
                || Effect == PluginEffectKind.ConditionalPassive
                || Effect == PluginEffectKind.ChargedPassive

                // G15 희귀 획득 fires when it is carried. 획득 has nothing to wait for, so a
                // trigger on it would be a condition that is already true.
                || Effect == PluginEffectKind.GrantRandomRare;
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

            Condition.Validate(ProposalId);

            if (Effect == PluginEffectKind.ConditionalPassive)
            {
                if (Condition.IsNone)
                    throw new InvalidOperationException(
                        $"{ProposalId}: a conditional passive with no condition is just a passive, " +
                        "and naming it conditional would hide that nothing gates it.");

                if (Passive.IsEmpty)
                    throw new InvalidOperationException(
                        $"{ProposalId}: a conditional passive that contributes nothing is a gate on nothing.");
            }
            else if (!Condition.IsNone)
            {
                throw new InvalidOperationException(
                    $"{ProposalId}: only a {nameof(PluginEffectKind.ConditionalPassive)} may carry a condition.");
            }

            if (Effect == PluginEffectKind.ChargedPassive)
            {
                Charge.Validate(ProposalId);

                if (Passive.IsEmpty)
                    throw new InvalidOperationException(
                        $"{ProposalId}: a charged passive that contributes nothing is a gauge that " +
                        "fills and buys nothing.");
            }
            else if (!Charge.IsEmpty)
            {
                throw new InvalidOperationException(
                    $"{ProposalId}: only a {nameof(PluginEffectKind.ChargedPassive)} may carry a gauge.");
            }

            if (Effect == PluginEffectKind.GrowingPassive)
                Growth.Validate(ProposalId);
            else if (!Growth.IsEmpty)
                throw new InvalidOperationException(
                    $"{ProposalId}: only a {nameof(PluginEffectKind.GrowingPassive)} may grow a stat with a counter.");

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

            if (Trigger == PluginTriggerKind.OnOwnerLowHealth)
            {
                if (OwnerHealthThresholdFraction <= 0f || OwnerHealthThresholdFraction > 1f)
                {
                    throw new InvalidOperationException(
                        $"{ProposalId}: a threshold of {OwnerHealthThresholdFraction} is not a health " +
                        "fraction, so the card would fire never or always.");
                }
            }
            else if (OwnerHealthThresholdFraction != 0f)
            {
                throw new InvalidOperationException(
                    $"{ProposalId}: only an {nameof(PluginTriggerKind.OnOwnerLowHealth)} card has a " +
                    "health threshold.");
            }

            if (Trigger == PluginTriggerKind.OnTimer)
            {
                if (TimerPeriodTicks < 1)
                {
                    throw new InvalidOperationException(
                        $"{ProposalId}: a period of {TimerPeriodTicks} ticks fires every tick or never.");
                }
            }
            else if (TimerPeriodTicks != 0)
            {
                throw new InvalidOperationException(
                    $"{ProposalId}: only an {nameof(PluginTriggerKind.OnTimer)} card has a period.");
            }

            if (Effect == PluginEffectKind.GrantShield)
                Shield.Validate(ProposalId);
            else if (!Shield.IsEmpty)
                throw new InvalidOperationException(
                    $"{ProposalId}: only a {nameof(PluginEffectKind.GrantShield)} grants a 보호막.");

            if (Effect == PluginEffectKind.KnockbackAroundOwner)
            {
                if (float.IsNaN(KnockbackUnits) || KnockbackUnits <= 0f)
                {
                    throw new InvalidOperationException(
                        $"{ProposalId}: a knockback of {KnockbackUnits} units moves nobody.");
                }

                if (float.IsNaN(RadiusUnits) || RadiusUnits <= 0f)
                {
                    throw new InvalidOperationException(
                        $"{ProposalId}: 주변 적 needs a reach, or the shove finds nobody to shove.");
                }
            }
            else if (KnockbackUnits != 0f)
            {
                throw new InvalidOperationException(
                    $"{ProposalId}: only a {nameof(PluginEffectKind.KnockbackAroundOwner)} shoves anybody.");
            }

            if (Effect == PluginEffectKind.FireProjectile)
            {
                if (ProjectilesFired < 1)
                {
                    throw new InvalidOperationException(
                        $"{ProposalId}: a firing effect that fires {ProjectilesFired} projectiles " +
                        "does nothing when it resolves.");
                }
            }
            else if (ProjectilesFired != 0)
            {
                throw new InvalidOperationException(
                    $"{ProposalId}: only a {nameof(PluginEffectKind.FireProjectile)} fires projectiles " +
                    "when it resolves.");
            }

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
