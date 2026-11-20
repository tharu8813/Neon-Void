using System.Collections.Generic;
using NeonVoid.Combat;
using NeonVoid.Combat.Proc;
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
            StatusMagnitude = 0.08f,       // [초안] per stack; 중첩 상한 is [미확정]
            StatusDurationTicksOverride = StatedCorrosionDurationTicks,
        };

        /// <summary>The three in a fixed order, so a replay grants them identically.</summary>
        public static IReadOnlyList<PluginDefinition> Prototype() => new[]
        {
            ArcChain(),
            DetonationGlyph(),
            CorrosionMark(),
        };
    }
}
