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
    /// Three of the original 27, chosen because each one needs a different guarantee from the
    /// proc queue that nothing was exercising: G10 needs a per-target internal cooldown and a
    /// bounded self-chain, G17 needs an internal cooldown on an area effect, and G12 needs a
    /// status applied through the target's own tenacity path.
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
        };
    }
}
