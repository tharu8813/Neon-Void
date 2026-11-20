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
            return PluginGrantVerdict.Granted;
        }

        /// <summary>
        /// Appends every carried plugin's passive stat contributions, in carry order.
        /// <para>
        /// Passives are collected separately from triggered effects because they reach a
        /// different place: the stat snapshot, not the proc queue.
        /// </para>
        /// </summary>
        public void CollectPassiveModifiers(List<StatModifier> into)
        {
            if (into == null)
                return;

            for (int i = 0; i < _carried.Count; ++i)
            {
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
                total += _carried[i].Passive.MoveSpeedFraction;

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
                    into.Add(new ExtraShotSpec(firing.ExtraShotsPerVolley, firing.ExtraShotOnHitScale));
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

                // OnHitEffectResolved lands here on purpose: a hit is not an effect resolving,
                // and an R04 that also fired on every hit would stack far faster than the card
                // says.
                _ => false,
            };
        }
    }
}
