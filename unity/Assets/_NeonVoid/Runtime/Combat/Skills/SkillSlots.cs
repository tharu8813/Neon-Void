using System;

namespace NeonVoid.Combat.Skills
{
    /// <summary>How an equip attempt ended.</summary>
    public enum SkillEquipVerdict
    {
        /// <summary>The slot now holds it.</summary>
        Equipped = 0,

        /// <summary>It was already in that slot.</summary>
        AlreadyThere = 1,

        /// <summary>It was in another slot, so the two swapped.</summary>
        Swapped = 2,

        /// <summary>That slot does not exist.</summary>
        NoSuchSlot = 3,

        /// <summary>The slot was emptied.</summary>
        Cleared = 4,
    }

    /// <summary>What an equip produced: the new arrangement and what happened.</summary>
    public readonly struct SkillEquipResult
    {
        internal SkillEquipResult(SkillEquipVerdict verdict, string[] slots, int swappedWith)
        {
            Verdict = verdict;
            Slots = slots ?? new string[0];
            SwappedWith = swappedWith;
        }

        public SkillEquipVerdict Verdict { get; }

        /// <summary>The document keys in slot order. Empty string means an empty slot.</summary>
        public string[] Slots { get; }

        /// <summary>The slot the skill came from on a swap, or -1.</summary>
        public int SwappedWith { get; }

        public bool Changed => Verdict != SkillEquipVerdict.AlreadyThere
                               && Verdict != SkillEquipVerdict.NoSuchSlot;

        public override string ToString() => $"{Verdict}: {string.Join(" / ", Slots)}";
    }

    /// <summary>
    /// The rule for putting a skill in one of the three slots.
    /// <para>
    /// [확정] docs/03 section 6: 별도 3슬롯. [확정] D-009 (2026-11-22): 교체는 허브의 스킬 장착
    /// 터미널에서 한다. Where that happens is the hub's business; <b>what happens</b> is this,
    /// and it is a plain function so the awkward case can be driven without a scene.
    /// </para>
    /// <para>
    /// <b>The awkward case is a skill that is already equipped elsewhere.</b> Putting 점멸 into
    /// slot 1 when it is in slot 3 could leave it in both, which would give the player two
    /// buttons for one skill and one fewer skill than they have slots. It swaps instead: the
    /// skill moves and whatever was in slot 1 goes to slot 3. Nothing is lost, and the player
    /// sees both slots change, which is what they did.
    /// </para>
    /// </summary>
    public static class SkillSlots
    {
        /// <summary>An empty slot. Not null, so an array of them is never a null reference.</summary>
        public const string Empty = "";

        /// <summary>Three empty slots.</summary>
        public static string[] None() => new[] { Empty, Empty, Empty };

        /// <summary>
        /// Puts <paramref name="documentKey"/> in <paramref name="slot"/>, swapping when it is
        /// already in another one. An empty key clears the slot.
        /// </summary>
        public static SkillEquipResult Equip(string[] current, int slot, string documentKey)
        {
            string[] slots = Copy(current);

            if (slot < 0 || slot >= slots.Length)
                return new SkillEquipResult(SkillEquipVerdict.NoSuchSlot, slots, -1);

            string wanted = documentKey ?? Empty;

            if (string.Equals(slots[slot], wanted, StringComparison.Ordinal))
            {
                return new SkillEquipResult(
                    wanted.Length == 0 ? SkillEquipVerdict.Cleared : SkillEquipVerdict.AlreadyThere,
                    slots,
                    -1);
            }

            if (wanted.Length == 0)
            {
                slots[slot] = Empty;
                return new SkillEquipResult(SkillEquipVerdict.Cleared, slots, -1);
            }

            int held = IndexOf(slots, wanted);
            if (held < 0)
            {
                slots[slot] = wanted;
                return new SkillEquipResult(SkillEquipVerdict.Equipped, slots, -1);
            }

            // It is in another slot. Swap rather than duplicate: two buttons for one skill is
            // one fewer skill than the player has slots for.
            slots[held] = slots[slot];
            slots[slot] = wanted;
            return new SkillEquipResult(SkillEquipVerdict.Swapped, slots, held);
        }

        /// <summary>Which slot holds this skill, or -1.</summary>
        public static int IndexOf(string[] slots, string documentKey)
        {
            if (slots == null || string.IsNullOrEmpty(documentKey))
                return -1;

            for (int i = 0; i < slots.Length; ++i)
            {
                if (string.Equals(slots[i], documentKey, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        /// <summary>Whether a skill is in any slot.</summary>
        public static bool Holds(string[] slots, string documentKey) =>
            IndexOf(slots, documentKey) >= 0;

        private static string[] Copy(string[] current)
        {
            int count = current?.Length ?? Core.DesignConstants.ActiveSkillSlots;
            var slots = new string[count];

            for (int i = 0; i < count; ++i)
                slots[i] = current != null && current[i] != null ? current[i] : Empty;

            return slots;
        }
    }
}
