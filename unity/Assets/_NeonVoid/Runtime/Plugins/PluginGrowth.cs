using System;
using NeonVoid.Combat.Stats;
using UnityEngine;

namespace NeonVoid.Plugins
{
    /// <summary>
    /// A stat contribution that grows as the mission goes on, rather than being fixed by the
    /// card. docs/03 R02 킬 스택 is 적 처치 시 AD 소폭 증가.
    /// <para>
    /// <b>A stat contribution and not a status effect.</b> A status would have to carry a
    /// duration, and any finite duration silently answers the question docs/03 left open -
    /// 임무 종료 해제 미확정 - with "they decay, this fast". There is no duration here, so what
    /// is decided is only that the stacks last while the mission does, and whether a mission
    /// ending releases them stays the question the document says it is. It also keeps docs/02's
    /// status catalogue as the document's two tables rather than a place to add a row whenever
    /// a card wants one.
    /// </para>
    /// <para>
    /// It enters the [확정] pipeline of docs/02 section 2 at the 플러그인 stage, beside the
    /// fixed passives, and is recomputed from base like everything else - so a stack lost or a
    /// card dropped cannot leave a residue in the snapshot.
    /// </para>
    /// </summary>
    [Serializable]
    public struct PluginGrowth
    {
        [Tooltip("Which stat grows. docs/03 R02 grows AD.")]
        public ModifiedStat Stat;

        [Tooltip("[초안] how much the stat gains per stack. 소폭 is not a number.")]
        public float PerStack;

        [Tooltip(
            "[초안] stacks this may reach. docs/03 R02 leaves 누적 상한 미확정, so this is a " +
            "draft bound to be decided, not an approved one.")]
        public int MaxStacks;

        public bool IsEmpty => Mathf.Approximately(PerStack, 0f) && MaxStacks == 0;

        public static PluginGrowth None => default;

        /// <summary>The contribution at a given stack count, clamped to the bound.</summary>
        public StatModifier ModifierAt(int stacks)
        {
            int effective = Mathf.Clamp(stacks, 0, MaxStacks);
            return StatModifier.Add(Stat, PerStack * effective);
        }

        public void Validate(string proposalId)
        {
            if (!Enum.IsDefined(typeof(ModifiedStat), Stat))
                throw new InvalidOperationException($"{proposalId}: {Stat} is not a known stat.");

            if (float.IsNaN(PerStack) || float.IsInfinity(PerStack) || PerStack <= 0f)
            {
                throw new InvalidOperationException(
                    $"{proposalId}: a growth of {PerStack} per stack would accumulate nothing, " +
                    "which is a card that counts kills and does not reward them.");
            }

            // A bound of zero would be a card whose stacks are always clamped away - the silent
            // no-op shape. The VALUE is undecided; its existence is not optional while the
            // contribution is unbounded in principle.
            if (MaxStacks < 1)
                throw new InvalidOperationException(
                    $"{proposalId}: a stack bound below 1 means the growth can never apply.");
        }
    }
}
