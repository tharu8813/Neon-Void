using System.Collections.Generic;
using NeonVoid.Combat;
using NeonVoid.Combat.Proc;
using NeonVoid.Combat.Stats;
using NeonVoid.Combat.Status;

namespace NeonVoid.Plugins
{
    /// <summary>
    /// The prototype plugins, as verifiable data in one place - the same shape
    /// <see cref="StatusCatalog"/> uses for the status catalogue.
    /// <para>
    /// Fifteen of the original 27. Each was built because it needed a guarantee nothing else
    /// was exercising: G10 a per-target internal cooldown and a bounded self-chain, G17 an
    /// internal cooldown on an area effect, G12 a status applied through the target's own
    /// tenacity path, G06 and G03 a carried card that changes the shape of the basic attack
    /// rather than a stat - there is no 공격속도, 탄환속도 or 탄환 크기 in
    /// <see cref="CombatStats"/>, because docs/02's stat block does not have them.
    /// </para>
    /// <para>
    /// Numbers the original design states are reproduced exactly: G10 and G17 both have
    /// 대상별 내부 CD 3초 (180 ticks), and G12 lasts 4초 (240 ticks), which is why it overrides
    /// the catalogue's 300. Everything else is [초안].
    /// </para>
    /// <para>
    /// <b>Why G14 추적 응답 is not here.</b> It fires on 스킬 피해 적중, and one firewall field
    /// ticking ten times would be ten skill hits, so it would launch ten projectiles from one
    /// cast. The document gives it no internal cooldown and does not say whether a periodic
    /// tick counts, so any behaviour I picked would be an invented rule in a plugin whose own
    /// exception column already warns about self-triggering loops. It waits for that decision.
    /// </para>
    /// </summary>
    public static class PluginCatalog
    {
        /// <summary>3초 at 60 Hz. Stated by docs/03 for both G10 and G17.</summary>
        public const int StatedInternalCooldownTicks = 180;

        /// <summary>4초 at 60 Hz. Stated by docs/03 for G12.</summary>
        public const int StatedCorrosionDurationTicks = 240;

        /// <summary>G10 전위 연쇄 - 탄환 적중 시 근처 적에게 번개, 대상별 내부 CD 3초.</summary>
        public static PluginDefinition ArcChain() => new PluginDefinition
        {
            ProposalId = "G10",
            Rarity = PluginRarity.Common,
            Trigger = PluginTriggerKind.OnProjectileHit,
            Effect = PluginEffectKind.ChainToNearbyEnemy,
            // Self-chaining with a bound: the lightning hops onward to new enemies, and the
            // guard still refuses a hop back to one already in the chain. 다단 연쇄 depth is
            // [미확정] in the document, so 3 is [초안].
            Rule = ProcEffectRule.SelfChaining(10, maxOccurrences: 3, internalCooldownTicks: StatedInternalCooldownTicks),
            Damage = new DamageRequest
            {
                Flat = 6f,                 // [초안]
                ApRatio = 0.2f,            // [초안]
                Type = DamageType.Magic,
                CanCrit = false,
                IsArea = false,
                IsPeriodic = false,
            },
            RadiusUnits = 4.5f,            // [초안] what "근처" means
            AppliedStatus = StatusEffectId.None,
            StatusDurationTicksOverride = -1,
        };

        /// <summary>G17 폭발 각인 - 탄환 적중 시 대상 폭발, 대상별 내부 CD 3초.</summary>
        public static PluginDefinition DetonationGlyph() => new PluginDefinition
        {
            ProposalId = "G17",
            Rarity = PluginRarity.Common,
            Trigger = PluginTriggerKind.OnProjectileHit,
            Effect = PluginEffectKind.ExplodeAroundTarget,
            // Single use per chain: an explosion that could trigger its own explosion is the
            // 연쇄 허용 정책 the document leaves open, so the safe reading applies.
            Rule = new ProcEffectRule
            {
                EffectId = 17,
                CanRetriggerItself = false,
                MaxOccurrencesPerChain = 1,
                InternalCooldownTicks = StatedInternalCooldownTicks,
            },
            Damage = new DamageRequest
            {
                Flat = 8f,                 // [초안]
                ApRatio = 0.25f,           // [초안]
                Type = DamageType.Magic,
                CanCrit = false,
                // The document's own exception column asks for the area flag to be explicit.
                // It carries omnivamp efficiency, so getting it wrong is a sustain change.
                IsArea = true,
                IsPeriodic = false,
            },
            RadiusUnits = 2.5f,            // [초안]
            AppliedStatus = StatusEffectId.None,
            StatusDurationTicksOverride = -1,
        };

        /// <summary>G12 부식 표식 - 타격 시 적에게 물리·마법 방어 감소 중첩 4초.</summary>
        public static PluginDefinition CorrosionMark() => new PluginDefinition
        {
            ProposalId = "G12",
            Rarity = PluginRarity.Common,
            Trigger = PluginTriggerKind.OnAnyHit,
            Effect = PluginEffectKind.MarkTarget,
            // No internal cooldown: 재적용 규칙 is [미확정] and the effect is a stacking mark,
            // so the stack policy in the status catalogue is what bounds it, not the queue.
            Rule = ProcEffectRule.SingleUse(12),
            Damage = default,              // it deals none
            RadiusUnits = 0f,
            // 방화벽 차단 is the catalogue's own "armour and damage reduction weakened".
            AppliedStatus = StatusEffectId.FirewallBreach,
            // [초안] resistance POINTS per stack, not a percentage: a percentage reduction is
            // inert against the unarmoured enemies the prototype actually has. 중첩 상한 is
            // [미확정], so the status catalogue's stacking policy is what bounds it.
            StatusMagnitude = 3f,
            StatusDurationTicksOverride = StatedCorrosionDurationTicks,
        };

        /// <summary>[원안 수치] docs/03 R01: 체력 50% 미만, 피해 15% 증가.</summary>
        public const float StatedExecutionThreshold = 0.5f;

        /// <summary>[원안 수치] docs/03 R01.</summary>
        public const float StatedExecutionBonusPercent = 15f;

        /// <summary>[원안 수치] docs/03 G01 랜덤 패치: 무작위 능력치 3개 증가.</summary>
        public const int StatedRandomPatchStatCount = 3;

        /// <summary>G01 랜덤 패치 - 무작위 능력치 3개 증가.</summary>
        public static PluginDefinition RandomPatch() => new PluginDefinition
        {
            ProposalId = "G01",
            Rarity = PluginRarity.Common,
            Trigger = PluginTriggerKind.None,
            Effect = PluginEffectKind.Passive,
            Damage = default,
            Passive = new PluginPassive
            {
                RandomStatCount = StatedRandomPatchStatCount,
                RandomStatFraction = 0.15f,     // [초안] how much each rolled stat gains
            },
            StatusDurationTicksOverride = -1,
        };

        /// <summary>G02 경량화 - 이동속도 증가. 가산/배수/상한 [미확정].</summary>
        public static PluginDefinition Lightweight() => new PluginDefinition
        {
            ProposalId = "G02",
            Rarity = PluginRarity.Common,
            Trigger = PluginTriggerKind.None,
            Effect = PluginEffectKind.Passive,
            Damage = default,
            Passive = new PluginPassive
            {
                MoveSpeedFraction = 0.12f,      // [초안]
            },
            StatusDurationTicksOverride = -1,
        };

        /// <summary>[원안 수치] docs/03 G11 무피격 가속: 이동속도 100% 증가.</summary>
        public const float StatedUntouchedMoveSpeedFraction = 1.0f;

        /// <summary>
        /// [초안] ticks without taking damage before 무피격 가속 comes back. docs/03 leaves
        /// 재활성화 시점 미확정, so this is a draft bound to be decided, not an approved one.
        /// 180 is three seconds.
        /// </summary>
        public const int DraftUntouchedRecoveryTicks = 180;

        /// <summary>
        /// G11 무피격 가속 - 이동속도 100% 증가, 피해를 받으면 비활성화.
        /// <para>
        /// The 100% is the document's own number. What it leaves open is 재활성화 시점, and that
        /// is an [초안] on the card rather than a duration in the status catalogue: a status
        /// would need a duration, and any duration written there answers the open question
        /// silently and in a place nobody looks when the decision is made.
        /// </para>
        /// <para>
        /// It is live from the start. A carrier who has never been hurt has gone an unbounded
        /// number of ticks unhurt, so the card does not need a first hit before it begins -
        /// which is the reading that matches 피해를 받으면 비활성화 rather than 피해를 받으면
        /// 활성화.
        /// </para>
        /// </summary>
        public static PluginDefinition UntouchedAcceleration() => new PluginDefinition
        {
            ProposalId = "G11",
            Rarity = PluginRarity.Common,
            Trigger = PluginTriggerKind.None,
            Effect = PluginEffectKind.ConditionalPassive,
            Damage = default,
            Condition = PluginCondition.UndamagedFor(DraftUntouchedRecoveryTicks),
            Passive = new PluginPassive
            {
                MoveSpeedFraction = StatedUntouchedMoveSpeedFraction,
            },
            StatusDurationTicksOverride = -1,
        };

        /// <summary>[초안] docs/03 G06 states no numbers at all, only the two things it raises.</summary>
        public const float DraftThrottleAttackSpeedFraction = 0.15f;

        /// <summary>[초안] docs/03 G06.</summary>
        public const float DraftThrottleProjectileSpeedFraction = 0.20f;

        /// <summary>[초안] docs/03 G03 states no number, only 탄환 크기 증가.</summary>
        public const float DraftWarheadSizeFraction = 0.40f;

        /// <summary>
        /// G06 스로틀 증폭 - 공격속도 + 탄환속도 증가.
        /// <para>
        /// The attack rate still goes through <see cref="AttackRateMath"/>, so this card meets
        /// the cap [확정] docs/02 section 3 requires - docs/03 lists 공격속도 상한 검사 against
        /// G06 by name, and a rate contribution that bypassed the cap would be the one card in
        /// the set that could reach an interval of one tick.
        /// </para>
        /// <para>
        /// Both halves are one card and neither is a stat: there is no 공격속도 or 탄환속도 in
        /// <see cref="CombatStats"/>, because docs/02's stat block does not have them - the
        /// attack rate lives on the attacker and the shot's speed on the shot.
        /// </para>
        /// </summary>
        public static PluginDefinition ThrottleAmplifier() => new PluginDefinition
        {
            ProposalId = "G06",
            Rarity = PluginRarity.Common,
            Trigger = PluginTriggerKind.None,
            Effect = PluginEffectKind.Passive,
            Damage = default,
            Passive = new PluginPassive
            {
                AttackSpeedFraction = DraftThrottleAttackSpeedFraction,
                ProjectileSpeedFraction = DraftThrottleProjectileSpeedFraction,
            },
            StatusDurationTicksOverride = -1,
        };

        /// <summary>
        /// [확정] docs/02 section 7 and docs/03 G05: 보호막을 만드는 일반 플러그인 30초 주기.
        /// 1800 ticks at 60 Hz.
        /// </summary>
        public const int StatedPeriodicShieldTicks = 1800;

        /// <summary>[초안] none of the three shield cards states an amount.</summary>
        public const float DraftPeriodicShieldAmount = 40f;

        /// <summary>[초안] ticks G05's shield lasts if nothing eats it.</summary>
        public const int DraftPeriodicShieldLifetimeTicks = 1800;

        /// <summary>[초안] G07 실행 차폐.</summary>
        public const float DraftSkillShieldAmount = 25f;

        /// <summary>[초안] ticks G07's shield lasts.</summary>
        public const int DraftSkillShieldLifetimeTicks = 300;

        /// <summary>[초안] R05 돌진 차폐, a rare, so worth more than G07's.</summary>
        public const float DraftDashShieldAmount = 35f;

        /// <summary>[초안] ticks R05's shield lasts.</summary>
        public const int DraftDashShieldLifetimeTicks = 240;

        /// <summary>
        /// G05 주기 차폐막 - 30초마다 보호막.
        /// <para>
        /// The 30초 is [확정]: docs/02 section 7 states 보호막을 만드는 일반 플러그인 30초 주기
        /// outright, and docs/03 repeats it. The amount and how long the shield lasts are
        /// [초안] - neither document gives either.
        /// </para>
        /// <para>
        /// 중첩·갱신·만료, which docs/03 lists as this card's exception, is not answered here:
        /// it is <see cref="NeonVoid.Combat.ShieldPolicy"/>, where the absorption order is
        /// [확정] 신규부터 (docs/08 D-004a) and the refresh rule is still open.
        /// </para>
        /// </summary>
        public static PluginDefinition PeriodicBarrier() => new PluginDefinition
        {
            ProposalId = "G05",
            Rarity = PluginRarity.Common,
            Trigger = PluginTriggerKind.OnTimer,
            Effect = PluginEffectKind.GrantShield,
            Rule = ProcEffectRule.SingleUse(5),
            Damage = default,
            TimerPeriodTicks = StatedPeriodicShieldTicks,
            Shield = new PluginShieldGrant
            {
                Amount = DraftPeriodicShieldAmount,
                LifetimeTicks = DraftPeriodicShieldLifetimeTicks,
            },
            StatusDurationTicksOverride = -1,
        };

        /// <summary>
        /// G07 실행 차폐 - 스킬 사용 시 보호막 생성.
        /// <para>
        /// 스킬 실패 시 발동하지 않음, which docs/03 lists as the exception, holds because the
        /// caster raises its cast event only for a cast that actually went off - a refused
        /// cast never reaches the router at all, so there is no check to forget.
        /// </para>
        /// </summary>
        public static PluginDefinition ExecutionBarrier() => new PluginDefinition
        {
            ProposalId = "G07",
            Rarity = PluginRarity.Common,
            Trigger = PluginTriggerKind.OnSkillCast,
            Effect = PluginEffectKind.GrantShield,
            Rule = ProcEffectRule.SingleUse(7),
            Damage = default,
            Shield = new PluginShieldGrant
            {
                Amount = DraftSkillShieldAmount,
                LifetimeTicks = DraftSkillShieldLifetimeTicks,
            },
            StatusDurationTicksOverride = -1,
        };

        /// <summary>
        /// R05 돌진 차폐 - 돌진 또는 점멸 뒤 보호막 생성.
        /// <para>
        /// The same 분류 태그 G08 이동 버퍼 uses, which is the 분류 태그 docs/03 lists as this
        /// card's exception: a skill counts as a dash or blink because its definition says
        /// <c>IsMovementSkill</c>, not because of its name.
        /// </para>
        /// </summary>
        public static PluginDefinition DashBarrier() => new PluginDefinition
        {
            ProposalId = "R05",
            Rarity = PluginRarity.Rare,
            Trigger = PluginTriggerKind.OnMovementAction,
            Effect = PluginEffectKind.GrantShield,
            Rule = ProcEffectRule.SingleUse(15),
            Damage = default,
            Shield = new PluginShieldGrant
            {
                Amount = DraftDashShieldAmount,
                LifetimeTicks = DraftDashShieldLifetimeTicks,
            },
            StatusDurationTicksOverride = -1,
        };

        /// <summary>[원안 수치] docs/03 R03 위기 반발: 플레이어 HP 30% 이하.</summary>
        public const float StatedCrisisThresholdFraction = 0.30f;

        /// <summary>[초안] docs/03 R03 states no distance, only 넉백.</summary>
        public const float DraftCrisisKnockbackUnits = 4f;

        /// <summary>[초안] docs/03 R03 leaves 내부 CD 미확정. 600 is ten seconds.</summary>
        public const int DraftCrisisCooldownTicks = 600;

        /// <summary>[초안] how far 주변 적 reaches.</summary>
        public const float DraftCrisisRadiusUnits = 5f;

        /// <summary>
        /// R03 위기 반발 - 플레이어 HP 30% 이하일 때 주변 적 넉백.
        /// <para>
        /// The 30% is the document's own number. 내부 CD is [미확정] and a card with none would
        /// fire on every hit taken while low, which is a permanent no-contact field exactly
        /// when the player is cornered - so there is a draft cooldown rather than no cooldown,
        /// and it is one line to change.
        /// </para>
        /// <para>
        /// <b>보스 저항 [초안]: a boss is not moved.</b> docs/03 leaves the policy open and
        /// docs/01 section 3 is explicit that 강한 CC는 강인함·저항/회피창 검증 없이는 넣지
        /// 않는다. Shoving a boss out of its own telegraphed pattern is the strongest version
        /// of that, and none of the verification that rule asks for has happened - so the
        /// cautious reading is the one that can be loosened later, rather than a boss fight
        /// that has already been built around being pushable.
        /// </para>
        /// </summary>
        public static PluginDefinition CrisisRepulse() => new PluginDefinition
        {
            ProposalId = "R03",
            Rarity = PluginRarity.Rare,
            Trigger = PluginTriggerKind.OnOwnerLowHealth,
            Effect = PluginEffectKind.KnockbackAroundOwner,
            Rule = new ProcEffectRule
            {
                EffectId = 3,
                CanRetriggerItself = false,
                MaxOccurrencesPerChain = 1,
                InternalCooldownTicks = DraftCrisisCooldownTicks,
            },
            Damage = default,                 // 넉백 only; the card deals none
            RadiusUnits = DraftCrisisRadiusUnits,
            OwnerHealthThresholdFraction = StatedCrisisThresholdFraction,
            KnockbackUnits = DraftCrisisKnockbackUnits,
            StatusDurationTicksOverride = -1,
        };

        /// <summary>[초안] docs/03 L03 피버타임 leaves 충전 단위 미확정; one point per landed hit.</summary>
        public const float DraftFeverChargePerHit = 1f;

        /// <summary>[초안] a kill is worth this much on top of the hit that caused it.</summary>
        public const float DraftFeverChargePerKill = 4f;

        /// <summary>[초안] gauge needed to set 피버타임 off.</summary>
        public const float DraftFeverChargeRequired = 30f;

        /// <summary>[초안] ticks 피버타임 lasts. 일정시간 is not a number; 480 is eight seconds.</summary>
        public const int DraftFeverActiveTicks = 480;

        /// <summary>[초안] what 전체 능력치 증가 is worth, per stat, while it runs.</summary>
        public const float DraftFeverAttackDamage = 6f;
        public const float DraftFeverAbilityPower = 10f;
        public const float DraftFeverArmor = 8f;
        public const float DraftFeverMagicResistance = 8f;
        public const float DraftFeverSkillHaste = 15f;
        public const float DraftFeverCritChancePercent = 10f;
        public const float DraftFeverOmnivampPercent = 5f;
        public const float DraftFeverMoveSpeedFraction = 0.2f;

        /// <summary>
        /// L03 피버타임 - 적중 또는 처치로 게이지, 충전 완료 후 일정시간 전체 능력치 증가.
        /// <para>
        /// The first 전설 in the catalogue, and the only one of the three that is not blocked:
        /// L01 레이저 코어 and L02 부메랑 프로토콜 both redefine what a projectile is, which is
        /// the 여러 탄환 형태의 합성 question D-005 has open. This one touches no projectile at
        /// all.
        /// </para>
        /// <para>
        /// Every number is [초안]. docs/03 states none of them and lists
        /// 버프 스택·충전 단위·계수 미확정 by name, so 전체 능력치 is spelled out here as the
        /// stats this game actually has rather than as a single multiplier applied to a notion
        /// of "all stats" that <see cref="CombatStats"/> does not define.
        /// </para>
        /// </summary>
        public static PluginDefinition FeverTime() => new PluginDefinition
        {
            ProposalId = "L03",
            Rarity = PluginRarity.Legendary,
            Trigger = PluginTriggerKind.None,
            Effect = PluginEffectKind.ChargedPassive,
            Damage = default,
            Charge = new PluginCharge
            {
                PerHit = DraftFeverChargePerHit,
                PerKill = DraftFeverChargePerKill,
                Required = DraftFeverChargeRequired,
                ActiveTicks = DraftFeverActiveTicks,
            },
            Passive = new PluginPassive
            {
                MoveSpeedFraction = DraftFeverMoveSpeedFraction,
                StatModifiers = new[]
                {
                    StatModifier.Add(ModifiedStat.AttackDamage, DraftFeverAttackDamage),
                    StatModifier.Add(ModifiedStat.AbilityPower, DraftFeverAbilityPower),
                    StatModifier.Add(ModifiedStat.PhysicalArmor, DraftFeverArmor),
                    StatModifier.Add(ModifiedStat.MagicResistance, DraftFeverMagicResistance),
                    StatModifier.Add(ModifiedStat.SkillHaste, DraftFeverSkillHaste),
                    StatModifier.Add(ModifiedStat.CritChancePercent, DraftFeverCritChancePercent),
                    StatModifier.Add(ModifiedStat.OmnivampPercent, DraftFeverOmnivampPercent),
                },
            },
            StatusDurationTicksOverride = -1,
        };

        /// <summary>[원안 수치] docs/03 G14 추적 응답: 자동발사 탄환 1개.</summary>
        public const int StatedSkillResponseShots = 1;

        /// <summary>
        /// G14 추적 응답 - 스킬 피해 적중 시 자동발사 탄환 1개.
        /// <para>
        /// The one number the document gives is the shot count, and it is here exactly. What it
        /// asks to be handled is 스킬의 자기 발동/루프 방지, and that holds without a guard of
        /// its own: the shot is a basic attack, so what it hits can never satisfy
        /// <see cref="PluginTriggerKind.OnSkillHit"/> again, and the loop has no second step.
        /// </para>
        /// <para>
        /// An effect that resolves rather than a <see cref="PluginEffectKind.FiringModifier"/>:
        /// R06 and G13 change what every volley is and are read off the loadout once, while
        /// this one happens because something happened, so it goes through the proc queue and
        /// obeys its chain rules.
        /// </para>
        /// </summary>
        public static PluginDefinition SkillResponse() => new PluginDefinition
        {
            ProposalId = "G14",
            Rarity = PluginRarity.Common,
            Trigger = PluginTriggerKind.OnSkillHit,
            Effect = PluginEffectKind.FireProjectile,
            // No internal cooldown: docs/03 gives none, and the card is already bounded by how
            // often a skill can land. Inventing one would be a rate the document does not have.
            Rule = ProcEffectRule.SingleUse(14),
            Damage = default,
            ProjectilesFired = StatedSkillResponseShots,
            StatusDurationTicksOverride = -1,
        };

        /// <summary>[초안] docs/03 R07 자동 추적탄 states no number, only 유도탄 발사.</summary>
        public const float DraftSeekerTurnDegreesPerTick = 3.5f;

        /// <summary>[초안] effect damage R07's guided shot carries, read across from R06's 80%.</summary>
        public const float DraftSeekerOnHitScale = 0.8f;

        /// <summary>
        /// R07 자동 추적탄 - 유도탄 발사.
        /// <para>
        /// <b>It adds a guided shot rather than guiding the basic attack.</b> docs/03 says
        /// 유도탄 발사 - it fires one - and the additive reading is the one that keeps this card
        /// out of D-005. Converting the basic attack is what L01 레이저 코어 and L02 부메랑
        /// 프로토콜 do, and 여러 탄환 형태의 합성 is exactly the undecided question those two
        /// are blocked on. A card that redefined every shot would have to answer it.
        /// </para>
        /// <para>
        /// The three exceptions docs/03 lists against it are answered in
        /// <see cref="CombatProjectile"/>, where the steering is, and each one is [초안]:
        /// 타깃 소실 flies on straight, 벽 gets no special rule because the sweep already stops
        /// a shot on geometry, and 관통/연쇄 gets none because the shot keeps the ledger it was
        /// launched with.
        /// </para>
        /// </summary>
        public static PluginDefinition SeekerRound() => new PluginDefinition
        {
            ProposalId = "R07",
            Rarity = PluginRarity.Rare,
            Trigger = PluginTriggerKind.None,
            Effect = PluginEffectKind.FiringModifier,
            Damage = default,
            Firing = new PluginFiring
            {
                ExtraShotsPerVolley = 1,
                ExtraShotOnHitScale = DraftSeekerOnHitScale,
                ExtraShotHomingTurnDegreesPerTick = DraftSeekerTurnDegreesPerTick,
            },
            StatusDurationTicksOverride = -1,
        };

        /// <summary>
        /// G03 확장 탄두 - 탄환 크기 증가.
        /// <para>
        /// It grows the shot's <b>collision</b> radius, which is the 유효 충돌크기 half of the
        /// 유효 충돌크기·VFX 크기 분리 docs/03 asks for against this card. The visible body is a
        /// separate concern on purpose: a card that made the shot look bigger without hitting
        /// any wider would read as working and would not be, and that is the exact failure this
        /// project has shipped before.
        /// </para>
        /// <para>
        /// It only ever grows. <see cref="PluginPassive.Validate"/> refuses a negative fraction
        /// rather than letting a retune shrink a shot until it passes through everything.
        /// </para>
        /// </summary>
        public static PluginDefinition ExpandedWarhead() => new PluginDefinition
        {
            ProposalId = "G03",
            Rarity = PluginRarity.Common,
            Trigger = PluginTriggerKind.None,
            Effect = PluginEffectKind.Passive,
            Damage = default,
            Passive = new PluginPassive
            {
                ProjectileSizeFraction = DraftWarheadSizeFraction,
            },
            StatusDurationTicksOverride = -1,
        };

        /// <summary>
        /// G04 취약점 분석 - 물리·마법 비율 관통 증가. The document leaves
        /// 두 종류 동시 또는 개별 증가 [미확정]; both is the reading that matches the name.
        /// </summary>
        public static PluginDefinition WeaknessAnalysis() => new PluginDefinition
        {
            ProposalId = "G04",
            Rarity = PluginRarity.Common,
            Trigger = PluginTriggerKind.None,
            Effect = PluginEffectKind.Passive,
            Damage = default,
            Passive = new PluginPassive
            {
                // Flat points of percentage penetration, [초안]. The resolver caps both at 100,
                // where penetration beyond the target's resistance would start healing it.
                StatModifiers = new[]
                {
                    StatModifier.Add(ModifiedStat.PhysicalPenPercent, 12f),
                    StatModifier.Add(ModifiedStat.MagicPenPercent, 12f),
                },
            },
            StatusDurationTicksOverride = -1,
        };

        /// <summary>
        /// R01 처형 알고리즘 - 체력 50% 미만 적에게 피해 15% 증가. Both figures are the
        /// original design's; the snapshot rule its exception column asks for lives in
        /// DamageApplication, where the health is read before any of it is removed.
        /// </summary>
        public static PluginDefinition ExecutionAlgorithm() => new PluginDefinition
        {
            ProposalId = "R01",
            Rarity = PluginRarity.Rare,
            Trigger = PluginTriggerKind.None,
            Effect = PluginEffectKind.Passive,
            Damage = default,
            Passive = new PluginPassive
            {
                StatModifiers = new[]
                {
                    StatModifier.Add(
                        ModifiedStat.LowHealthDamageBonusPercent, StatedExecutionBonusPercent),
                },
            },
            StatusDurationTicksOverride = -1,
        };

        /// <summary>[원안 수치] docs/03 R06 보조 탄환: 발사 탄환 1개 추가.</summary>
        public const int StatedSupportShotCount = 1;

        /// <summary>[원안 수치] docs/03 R06: 추가탄 적중 효과 피해 80%.</summary>
        public const float StatedSupportShotOnHitScale = 0.80f;

        /// <summary>[원안 수치] docs/03 G13 4중 캐시: 적중 효과 4회 후 탄환 4발 추가 연사.</summary>
        public const int StatedCacheHitEffects = 4;

        /// <summary>[원안 수치] docs/03 G13: 탄환 4발.</summary>
        public const int StatedCacheBurstShots = 4;

        /// <summary>[원안 수치] docs/03 G13: 추가탄 적중 효과 피해 70%.</summary>
        public const float StatedCacheBurstOnHitScale = 0.70f;

        /// <summary>
        /// R06 보조 탄환 - 발사 탄환 1개 추가, 추가탄 적중 효과 피해 80%.
        /// <para>
        /// Where the extra shot goes is [초안]: docs/03's exception column says only
        /// multi-shot/레이저와 합성, so the spread angle lives on the attacker as a draft number
        /// and the card states the two figures the document does give.
        /// </para>
        /// </summary>
        public static PluginDefinition SupportRound() => new PluginDefinition
        {
            ProposalId = "R06",
            Rarity = PluginRarity.Rare,
            Trigger = PluginTriggerKind.None,
            Effect = PluginEffectKind.FiringModifier,
            Damage = default,
            Firing = new PluginFiring
            {
                ExtraShotsPerVolley = StatedSupportShotCount,
                ExtraShotOnHitScale = StatedSupportShotOnHitScale,
            },
            StatusDurationTicksOverride = -1,
        };

        /// <summary>
        /// G13 4중 캐시 - 적중 효과 4회 후 탄환 4발 추가 연사, 추가탄 적중 효과 피해 70%.
        /// <para>
        /// 적중 효과 is counted as effects that actually RESOLVED, not as effects offered to the
        /// queue. A refused one - an internal cooldown, a cycle, an occurrence limit - did
        /// nothing, and counting it would let a card charge its burst off effects the rules had
        /// just declined.
        /// </para>
        /// </summary>
        public static PluginDefinition QuadCache() => new PluginDefinition
        {
            ProposalId = "G13",
            Rarity = PluginRarity.Common,
            Trigger = PluginTriggerKind.None,
            Effect = PluginEffectKind.FiringModifier,
            Damage = default,
            Firing = new PluginFiring
            {
                HitEffectsPerBurst = StatedCacheHitEffects,
                BurstShots = StatedCacheBurstShots,
                BurstOnHitScale = StatedCacheBurstOnHitScale,
            },
            StatusDurationTicksOverride = -1,
        };

        /// <summary>
        /// [초안] magic damage per 해체 stack per event. docs/03 R04 gives no number - it says
        /// 시간·성능은 집약형 데이터 and forbids inventing a stack cap, not a magnitude.
        /// </summary>
        public const float DraftDismantleMagnitudePerStack = 2f;

        /// <summary>
        /// R04 해체 프로토콜 - 적중 효과 발생 때마다 상한 없는 스택의 지속 마법 피해.
        /// <para>
        /// <b>No cap, deliberately.</b> docs/03's exception column says 스택 캡 임의 추가 금지
        /// in as many words, and section 7 requires the unbounded stacking to use 하나의 집계
        /// 상태 so the cost stays stable as the count grows. That is the
        /// <see cref="StatusStackPolicy.UnboundedAggregated"/> policy docs/02 names after this
        /// card: one instance per target, one timer, one periodic event, whatever the stack
        /// count is.
        /// </para>
        /// <para>
        /// <b>It reacts to effects, not to hits.</b> 적중 효과 발생 includes 부식 표식 applying a
        /// mark, which removes no health, so a hit trigger could never see it.
        /// </para>
        /// <para>
        /// <b>One stack per resolved effect [D-018, approved].</b> 적중 효과 발생 때마다 taken
        /// literally: every effect that resolves adds a stack, however deep the chain that
        /// produced it. The first version of this allowed the effect once per ancestry path,
        /// which made a three-deep chain worth the same as a one-deep one on that branch - and
        /// dampened the one thing the card is about.
        /// </para>
        /// <para>
        /// The loop that reading invites is closed where it cannot be forgotten rather than
        /// here: R04's own resolution is a 적중 효과 like any other, so a reactor reacting to
        /// itself would feed the card with no hit involved, and
        /// <see cref="ProcTriggerRouter.Drain"/> refuses to offer a reactor its own resolution.
        /// That is the 원형 참조 금지 of docs/03 section 2, stated once.
        /// </para>
        /// <para>
        /// <b>Nothing here caps the stacks.</b> Repeats on one target are allowed, because
        /// inside a lineage two effects really do resolve on the same enemy. The occurrence
        /// bound is the chain depth limit every effect already shares, so it never binds
        /// before the chain itself ends - it exists because the validator requires a number,
        /// not to limit this card. And there is no internal cooldown: the text is 때마다, and a
        /// cooldown would silently turn "every time" into "sometimes".
        /// </para>
        /// </summary>
        public static PluginDefinition DismantleProtocol() => new PluginDefinition
        {
            ProposalId = "R04",
            Rarity = PluginRarity.Rare,
            Trigger = PluginTriggerKind.OnHitEffectResolved,
            Effect = PluginEffectKind.MarkTarget,
            Damage = default,
            Rule = new ProcEffectRule
            {
                EffectId = 4,

                // May appear many times along one lineage, because every resolved effect in it
                // is its own 적중 효과. The router, not this rule, is what stops it reacting to
                // itself.
                CanRetriggerItself = true,

                // Tied to the chain depth limit rather than chosen: a path cannot be longer
                // than that, so this never refuses a stack the chain would have produced. A
                // smaller number here would be the 스택 캡 임의 추가 docs/03 forbids.
                MaxOccurrencesPerChain = ProcChainGuard.DefaultMaxChainDepth,

                // Two effects resolving on the same enemy inside one lineage is the normal
                // case, not a loop. See ProcEffectRule.AllowRepeatOnSameTarget.
                AllowRepeatOnSameTarget = true,

                InternalCooldownTicks = 0,
            },
            AppliedStatus = StatusEffectId.Dismantle,
            StatusMagnitude = DraftDismantleMagnitudePerStack,
            StatusDurationTicksOverride = -1,
        };

        /// <summary>[초안] reach of the dash path in units. docs/03 G16 gives no number.</summary>
        public const float DraftDashBlastRadiusUnits = 1.8f;

        /// <summary>
        /// G16 대시 폭발 - 대시 경로 폭발 피해.
        /// <para>
        /// <b>Once per enemy per dash</b>, which is docs/03's exception column verbatim:
        /// 한 대시에서 중복 접촉. The dash lasts nine ticks in the prototype and the path is
        /// swept on each of them, so an enemy standing in the way would otherwise take this
        /// nine times.
        /// </para>
        /// <para>
        /// <b>폭발 is the damage's flavour here, not a second explosion.</b> The path is the
        /// area - docs/03 says 대시 경로 폭발 피해 - so each enemy in it is struck once, with
        /// <see cref="DamageRequest.IsArea"/> set so the 33% area omnivamp efficiency applies.
        /// Exploding around each touched enemy instead would make a dash through five of them
        /// five overlapping explosions, and a crowd's damage would grow with the square of its
        /// size.
        /// </para>
        /// <para>
        /// <b>환경 영향 is not implemented</b>, the other half of that exception column. There
        /// is no environment system to interact with yet; when there is, a dash blast is one of
        /// the things that has to be asked about rather than assumed.
        /// </para>
        /// </summary>
        public static PluginDefinition DashDetonation() => new PluginDefinition
        {
            ProposalId = "G16",
            Rarity = PluginRarity.Common,
            Trigger = PluginTriggerKind.OnDashContact,
            Effect = PluginEffectKind.DamageTarget,
            Damage = new DamageRequest
            {
                AdRatio = 0.8f,          // [초안]
                Type = DamageType.Magic,
                CanCrit = true,
                IsArea = true,
            },
            RadiusUnits = DraftDashBlastRadiusUnits,

            // SingleUse: the dash's own contact is what fires this, and a blast that could
            // re-enter its own chain would be reacting to itself.
            Rule = ProcEffectRule.SingleUse(16),
            StatusDurationTicksOverride = -1,
        };

        /// <summary>[원안 수치] docs/03 G08 이동 버퍼: 2초 = 120 ticks at 60 Hz.</summary>
        public const int StatedMovementBufferTicks = 120;

        /// <summary>
        /// G08 이동 버퍼 - 돌진/도약/점멸/순간이동 또는 투명 해제 후 2초 이동속도 증가.
        /// <para>
        /// <b>The 스킬 분류 태그 its exception column asks for already existed.</b>
        /// <see cref="SkillDefinition.IsMovementSkill"/> is there because 속박 has to deny
        /// exactly the skills that move the player, so the tag that decides whether 점멸 counts
        /// here is the same one the control rules use. Nothing new had to be classified, and a
        /// skill cannot be a movement skill for one system and not the other.
        /// </para>
        /// <para>
        /// <b>Two of the four sources do not exist.</b> 돌진 is the dash and 점멸/순간이동 is a
        /// movement skill, both of which fire this. There is no 도약 anywhere in the game, and
        /// no invisibility, so 투명 해제 has nothing to be released from - and inventing either
        /// to satisfy a buff's trigger list would be building content nobody asked for. They
        /// are reachable the moment those systems arrive, because the trigger is "a movement
        /// action finished" rather than a list of four.
        /// </para>
        /// <para>
        /// The magnitude is [초안]; docs/03 states the duration and not the amount.
        /// </para>
        /// </summary>
        public static PluginDefinition MovementBuffer() => new PluginDefinition
        {
            ProposalId = "G08",
            Rarity = PluginRarity.Common,
            Trigger = PluginTriggerKind.OnMovementAction,
            Effect = PluginEffectKind.MarkTarget,
            Damage = default,

            // 가속 is the movement speed buff in docs/02's table, and the catalogue's own
            // duration is 3 seconds - overridden here because the card states 2.
            AppliedStatus = StatusEffectId.Haste,
            StatusMagnitude = 0.3f,     // [초안] +30% movement speed
            StatusDurationTicksOverride = StatedMovementBufferTicks,
            Rule = ProcEffectRule.SingleUse(8),
        };

        /// <summary>[초안] AD per kill. docs/03 R02 says 소폭, which is not a number.</summary>
        public const float DraftKillStackAttackDamage = 0.5f;

        /// <summary>
        /// [초안, 미확정] stacks 킬 스택 may reach. docs/03 leaves 누적 상한 미확정 (D-019), so
        /// this is a bound to be decided rather than an approved one.
        /// </summary>
        public const int DraftKillStackMaxStacks = 20;

        /// <summary>
        /// R02 킬 스택 - 적 처치 시 AD 소폭 증가.
        /// <para>
        /// <b>A growing stat contribution, not a status effect.</b> A status needs a duration,
        /// and any finite one silently answers the question docs/03 left open - 임무 종료 해제
        /// 미확정 - with "they decay, this fast". This has no duration: what is decided is only
        /// that the stacks last while the mission does, which is also all that is TRUE, because
        /// nothing persists a run (D-017). Whether a mission ending releases them is still the
        /// open question the document says it is, recorded as D-019.
        /// </para>
        /// <para>
        /// It also leaves docs/02's status catalogue as the document's two tables. 강화 is
        /// AD 증가 and would have been the obvious status to reuse, but its stack bound of 5 and
        /// its 5 second duration are 강화's numbers, and borrowing them would have made this
        /// card inherit two answers it never asked for.
        /// </para>
        /// <para>
        /// <b>Both numbers are [초안] and the bound is 미확정.</b> 소폭 is not a figure, and the
        /// cap is a question docs/03 asks rather than answers. The bound exists because an
        /// unbounded AD gain over a long mission is the shape that breaks balance invisibly -
        /// not because 20 is right.
        /// </para>
        /// </summary>
        public static PluginDefinition KillStack() => new PluginDefinition
        {
            ProposalId = "R02",
            Rarity = PluginRarity.Rare,
            Trigger = PluginTriggerKind.OnKill,
            Effect = PluginEffectKind.GrowingPassive,
            Damage = default,
            Growth = new PluginGrowth
            {
                Stat = ModifiedStat.AttackDamage,
                PerStack = DraftKillStackAttackDamage,
                MaxStacks = DraftKillStackMaxStacks,
            },
            StatusDurationTicksOverride = -1,
        };

        /// <summary>
        /// Every authored card, in a fixed order, so a replay rolls them identically.
        /// <para>
        /// This is the pool <see cref="RewardRoller"/> deals from. It is the catalogue rather
        /// than a separate list because a card that exists and is not offered is a card nobody
        /// can ever get - and the four of the twenty-seven that are not written yet are absent
        /// here by being absent from the file.
        /// </para>
        /// </summary>
        public static IReadOnlyList<PluginDefinition> All() => new[]
        {
            ArcChain(),
            DetonationGlyph(),
            CorrosionMark(),
            RandomPatch(),
            Lightweight(),
            UntouchedAcceleration(),
            ThrottleAmplifier(),
            PeriodicBarrier(),
            ExecutionBarrier(),
            DashBarrier(),
            CrisisRepulse(),
            FeverTime(),
            SkillResponse(),
            SeekerRound(),
            ExpandedWarhead(),
            WeaknessAnalysis(),
            ExecutionAlgorithm(),
            SupportRound(),
            QuadCache(),
            DismantleProtocol(),
            DashDetonation(),
            MovementBuffer(),
            KillStack(),
        };

        /// <summary>The prototype set in a fixed order, so a replay grants them identically.</summary>
        public static IReadOnlyList<PluginDefinition> Prototype() => new[]
        {
            ArcChain(),
            DetonationGlyph(),
            CorrosionMark(),
            RandomPatch(),
            Lightweight(),
            WeaknessAnalysis(),
            ExecutionAlgorithm(),
            SupportRound(),
            QuadCache(),
            DismantleProtocol(),
            DashDetonation(),
            MovementBuffer(),
            KillStack(),
        };
    }
}
