using System;
using NeonVoid.Core;

namespace NeonVoid.Combat.Skills
{
    /// <summary>
    /// [확정] The three active skill slots from docs/02 section 7, with their cooldowns.
    /// <para>
    /// The cast decision lives here as one function so every refusal reason is produced in one
    /// place and in a fixed order. Spreading the checks across callers is how a build ends up
    /// where a stunned player can still cast by pressing the key at the right moment.
    /// </para>
    /// Pure C#: no MonoBehaviour, no input, no Unity types.
    /// </summary>
    public sealed class SkillLoadout
    {
        private readonly SkillDefinition[] _slots;
        private readonly int[] _cooldownRemainingTicks;

        public SkillLoadout()
        {
            _slots = new SkillDefinition[DesignConstants.ActiveSkillSlots];
            _cooldownRemainingTicks = new int[DesignConstants.ActiveSkillSlots];
        }

        public int SlotCount => _slots.Length;

        public SkillDefinition SlotAt(int index) =>
            IsValidSlot(index) ? _slots[index] : SkillDefinition.Empty;

        public int CooldownRemaining(int index) =>
            IsValidSlot(index) ? _cooldownRemainingTicks[index] : 0;

        public bool IsReady(int index) =>
            IsValidSlot(index) && !_slots[index].IsEmpty && _cooldownRemainingTicks[index] <= 0;

        /// <summary>Puts a skill in a slot and clears that slot's cooldown.</summary>
        public void Equip(int index, SkillDefinition definition)
        {
            if (!IsValidSlot(index))
                throw new ArgumentOutOfRangeException(nameof(index),
                    $"There are exactly {DesignConstants.ActiveSkillSlots} active slots.");

            definition.Validate();
            _slots[index] = definition;
            _cooldownRemainingTicks[index] = 0;
        }

        public void Clear(int index)
        {
            if (!IsValidSlot(index))
                return;
            _slots[index] = SkillDefinition.Empty;
            _cooldownRemainingTicks[index] = 0;
        }

        /// <summary>Advances every slot's cooldown by one logic tick.</summary>
        public void Tick()
        {
            for (int i = 0; i < _cooldownRemainingTicks.Length; ++i)
                if (_cooldownRemainingTicks[i] > 0)
                    _cooldownRemainingTicks[i]--;
        }

        /// <summary>
        /// Attempts a cast. On success the mana is spent and the slot enters cooldown; on any
        /// refusal nothing changes at all, so a refused cast can never half-charge the player.
        /// </summary>
        /// <param name="slotIndex">0 to 2.</param>
        /// <param name="mana">Spent in place on success.</param>
        /// <param name="stats">Supplies skill haste.</param>
        /// <param name="gate">Control effects currently denying a cast.</param>
        public SkillCastResult TryCast(int slotIndex, ref ManaPool mana, in CombatStats stats, in SkillGate gate)
        {
            if (!IsValidSlot(slotIndex))
                return SkillCastResult.Refused(SkillCastRejection.NoSuchSlot);

            SkillDefinition skill = _slots[slotIndex];
            if (skill.IsEmpty)
                return SkillCastResult.Refused(SkillCastRejection.EmptySlot);

            // Control effects first: being stunned is the more useful thing to tell a player
            // than being out of mana, and it is also the stricter rule.
            SkillCastRejection denial = gate.Evaluate(skill.IsMovementSkill);
            if (denial != SkillCastRejection.None)
                return SkillCastResult.Refused(denial, skill.SkillId);

            if (_cooldownRemainingTicks[slotIndex] > 0)
                return SkillCastResult.Refused(SkillCastRejection.OnCooldown, skill.SkillId);

            if (!mana.CanAfford(skill.ManaCost))
                return SkillCastResult.Refused(SkillCastRejection.NotEnoughMana, skill.SkillId);

            if (!mana.TrySpend(skill.ManaCost))
                return SkillCastResult.Refused(SkillCastRejection.NotEnoughMana, skill.SkillId);

            int cooldown = CooldownMath.EffectiveCooldownTicks(skill.BaseCooldownTicks, stats.SkillHaste);
            _cooldownRemainingTicks[slotIndex] = cooldown;

            return SkillCastResult.Cast(skill.SkillId, skill.ManaCost, cooldown);
        }

        /// <summary>Clears every cooldown, e.g. when a new mission starts.</summary>
        public void ResetCooldowns()
        {
            for (int i = 0; i < _cooldownRemainingTicks.Length; ++i)
                _cooldownRemainingTicks[i] = 0;
        }

        private bool IsValidSlot(int index) => index >= 0 && index < _slots.Length;
    }
}
