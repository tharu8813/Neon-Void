using System;
using System.Text;

namespace NeonVoid.Combat.Proc
{
    /// <summary>One step of causality: an effect that fired at a target.</summary>
    public readonly struct ProcChainLink : IEquatable<ProcChainLink>
    {
        public readonly int EffectId;
        public readonly int TargetActorId;

        public ProcChainLink(int effectId, int targetActorId)
        {
            EffectId = effectId;
            TargetActorId = targetActorId;
        }

        public bool Equals(ProcChainLink other) =>
            EffectId == other.EffectId && TargetActorId == other.TargetActorId;

        public override bool Equals(object obj) => obj is ProcChainLink other && Equals(other);

        public override int GetHashCode() => unchecked(EffectId * 397) ^ TargetActorId;

        public override string ToString() => $"{EffectId}@{TargetActorId}";
    }

    /// <summary>
    /// The causal ancestry of a triggered effect: which effects fired, at which targets, to
    /// reach this point. This is the <c>chainPath</c> that docs/02 section 6 requires on every
    /// proc event.
    /// <para>
    /// Immutable, and <see cref="Append"/> returns a new path. That is what makes independent
    /// branches independent: two effects triggered by the same hit each get their own copy, so
    /// one branch can never make the other look like a cycle. A mutable shared path would make
    /// a sibling's history count as this branch's own ancestry, and legal parallel chains would
    /// start blocking each other depending on which one happened to run first.
    /// </para>
    /// </summary>
    public sealed class ProcChainPath
    {
        private static readonly ProcChainLink[] NoLinks = Array.Empty<ProcChainLink>();

        /// <summary>A direct hit with no triggered ancestry.</summary>
        public static readonly ProcChainPath Root = new ProcChainPath(NoLinks);

        private readonly ProcChainLink[] _links;

        private ProcChainPath(ProcChainLink[] links) => _links = links;

        /// <summary>How many effects deep this chain already is. Root is 0.</summary>
        public int Depth => _links.Length;

        /// <summary>The effect that produced this step, or null at the root.</summary>
        public ProcChainLink? CausalParent => _links.Length == 0 ? (ProcChainLink?)null : _links[_links.Length - 1];

        /// <summary>Returns a new path with one more step; this instance is unchanged.</summary>
        public ProcChainPath Append(int effectId, int targetActorId)
        {
            var next = new ProcChainLink[_links.Length + 1];
            Array.Copy(_links, next, _links.Length);
            next[_links.Length] = new ProcChainLink(effectId, targetActorId);
            return new ProcChainPath(next);
        }

        /// <summary>True when this effect already fired anywhere in this chain.</summary>
        public bool ContainsEffect(int effectId)
        {
            for (int i = 0; i < _links.Length; ++i)
                if (_links[i].EffectId == effectId)
                    return true;
            return false;
        }

        /// <summary>True when this effect already fired at this target in this chain.</summary>
        public bool ContainsEffectOnTarget(int effectId, int targetActorId)
        {
            for (int i = 0; i < _links.Length; ++i)
                if (_links[i].EffectId == effectId && _links[i].TargetActorId == targetActorId)
                    return true;
            return false;
        }

        /// <summary>How many times this effect already fired in this chain.</summary>
        public int CountEffect(int effectId)
        {
            int count = 0;
            for (int i = 0; i < _links.Length; ++i)
                if (_links[i].EffectId == effectId)
                    count++;
            return count;
        }

        /// <summary>Readable ancestry for diagnostics, e.g. "1@7 -> 2@7 -> 3@9".</summary>
        public override string ToString()
        {
            if (_links.Length == 0)
                return "(root)";

            var text = new StringBuilder();
            for (int i = 0; i < _links.Length; ++i)
            {
                if (i > 0)
                    text.Append(" -> ");
                text.Append(_links[i].ToString());
            }
            return text.ToString();
        }
    }
}
