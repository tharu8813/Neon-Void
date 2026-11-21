using System;
using System.Collections.Generic;

namespace NeonVoid.Combat
{
    /// <summary>One shield: what is left of it, where it came from, and when it runs out.</summary>
    public readonly struct ShieldLayer
    {
        /// <summary>Who granted it. Two shields from one source are what the refresh rule is about.</summary>
        public readonly string SourceId;

        /// <summary>Damage it can still swallow.</summary>
        public readonly float Remaining;

        /// <summary>The tick it stops existing, or <see cref="Never"/>.</summary>
        public readonly long ExpiresAtTick;

        /// <summary>The tick it was granted, which is what "newest" and "oldest" mean.</summary>
        public readonly long GrantedAtTick;

        /// <summary>A shield with no timer.</summary>
        public const long Never = long.MaxValue;

        public ShieldLayer(string sourceId, float remaining, long grantedAtTick, long expiresAtTick)
        {
            SourceId = sourceId;
            Remaining = remaining;
            GrantedAtTick = grantedAtTick;
            ExpiresAtTick = expiresAtTick;
        }

        public ShieldLayer With(float remaining) =>
            new ShieldLayer(SourceId, remaining, GrantedAtTick, ExpiresAtTick);

        public override string ToString() =>
            $"{SourceId} {Remaining:0.##}" + (ExpiresAtTick == Never ? string.Empty : $" to t{ExpiresAtTick}");
    }

    /// <summary>What one hit did to the shields.</summary>
    public readonly struct ShieldAbsorption
    {
        /// <summary>Damage the shields swallowed.</summary>
        public readonly float Absorbed;

        /// <summary>Damage left over for health.</summary>
        public readonly float Remainder;

        /// <summary>How many shields were used up entirely by this hit.</summary>
        public readonly int LayersBroken;

        public ShieldAbsorption(float absorbed, float remainder, int layersBroken)
        {
            Absorbed = absorbed;
            Remainder = remainder;
            LayersBroken = layersBroken;
        }

        public override string ToString() =>
            $"absorbed {Absorbed:0.##}, {Remainder:0.##} through, {LayersBroken} broken";
    }

    /// <summary>
    /// The 보호막 an actor is carrying, and the one place that decides which of them a hit eats.
    /// <para>
    /// <b>This is D-004's strategy module.</b> docs/02 section 7 leaves 흡수 순서, 갱신·합산 and
    /// 만료 판정 [미확정], and docs/08's safe handling is to build exactly this - an independent,
    /// tested strategy - and hold the live connection until the decision is approved. Every
    /// reading the document names is implemented here and covered by tests, so approving one
    /// costs a constant in <see cref="ShieldPolicy"/> rather than a rewrite.
    /// </para>
    /// <para>
    /// Pure C#: no Unity types, no clock of its own. The tick is passed in, because the
    /// simulation owns time and a shield that expired on a different tick than the rules say
    /// would not be reproducible from the seed.
    /// </para>
    /// </summary>
    public sealed class ShieldStack
    {
        private readonly List<ShieldLayer> _layers = new List<ShieldLayer>();
        private ShieldPolicy _policy;

        public ShieldStack(ShieldPolicy policy)
        {
            _policy = policy;
        }

        /// <summary>The policy in force. Settable so a test can drive every reading.</summary>
        public ShieldPolicy Policy
        {
            get => _policy;
            set => _policy = value;
        }

        /// <summary>How many shields are standing.</summary>
        public int LayerCount => _layers.Count;

        /// <summary>Total damage every shield together can still swallow.</summary>
        public float Total
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < _layers.Count; ++i)
                    total += _layers[i].Remaining;

                return total;
            }
        }

        /// <summary>Whether anything is left to absorb with.</summary>
        public bool HasAny => _layers.Count > 0;

        /// <summary>One shield, in the order they were granted.</summary>
        public ShieldLayer LayerAt(int index) => _layers[index];

        /// <summary>
        /// Grants a shield. What happens when one from the same source is already standing is
        /// <see cref="ShieldPolicy.Refresh"/>, which is [미확정].
        /// </summary>
        public void Grant(string sourceId, float amount, long nowTick, int lifetimeTicks)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
                throw new ArgumentException("A shield needs a source, or no refresh rule can apply.", nameof(sourceId));

            if (float.IsNaN(amount) || amount <= 0f)
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "A shield of nothing absorbs nothing.");

            long expires = lifetimeTicks > 0 ? nowTick + lifetimeTicks : ShieldLayer.Never;

            int existing = IndexOfSource(sourceId);
            if (existing < 0 || _policy.Refresh == ShieldRefreshRule.Separate)
            {
                _layers.Add(new ShieldLayer(sourceId, amount, nowTick, expires));
                return;
            }

            ShieldLayer standing = _layers[existing];

            if (_policy.Refresh == ShieldRefreshRule.Replace)
            {
                _layers[existing] = new ShieldLayer(sourceId, amount, nowTick, expires);
                return;
            }

            // Merge: the amounts add and the longer life wins. The grant tick stays the older
            // one, so a card that re-shields every few seconds cannot keep renewing its place
            // at the front of a newest-first queue.
            long longer = expires == ShieldLayer.Never || standing.ExpiresAtTick == ShieldLayer.Never
                ? ShieldLayer.Never
                : Math.Max(expires, standing.ExpiresAtTick);

            _layers[existing] = new ShieldLayer(
                sourceId, standing.Remaining + amount, standing.GrantedAtTick, longer);
        }

        /// <summary>
        /// Drops every shield whose time has run out, and says how many went.
        /// <para>
        /// Whether a shield still absorbs on the tick it expires is
        /// <see cref="ShieldPolicy.AbsorbsOnTheExpiryTick"/>, which is [미확정]. Expiry is a
        /// separate call from absorption rather than something absorption does on the way
        /// past, so a shield cannot quietly outlive its timer by never being hit.
        /// </para>
        /// </summary>
        public int Expire(long nowTick)
        {
            int dropped = 0;
            for (int i = _layers.Count - 1; i >= 0; --i)
            {
                if (!HasExpired(_layers[i], nowTick))
                    continue;

                _layers.RemoveAt(i);
                ++dropped;
            }

            return dropped;
        }

        /// <summary>
        /// Eats a hit. Returns what the shields swallowed and what is left for health, which is
        /// <c>actualHealthDamage</c> in docs/02 section 3's pipeline.
        /// </summary>
        public ShieldAbsorption Absorb(float damage, long nowTick)
        {
            if (float.IsNaN(damage) || damage <= 0f)
                return new ShieldAbsorption(0f, 0f, 0);

            if (_layers.Count == 0)
                return new ShieldAbsorption(0f, damage, 0);

            float remaining = damage;
            float absorbed = 0f;
            int broken = 0;

            while (remaining > 0f)
            {
                int index = NextToAbsorb(nowTick);
                if (index < 0)
                    break;

                ShieldLayer layer = _layers[index];

                if (layer.Remaining > remaining)
                {
                    _layers[index] = layer.With(layer.Remaining - remaining);
                    absorbed += remaining;
                    remaining = 0f;
                    break;
                }

                absorbed += layer.Remaining;
                remaining -= layer.Remaining;
                _layers.RemoveAt(index);
                ++broken;
            }

            return new ShieldAbsorption(absorbed, remaining, broken);
        }

        /// <summary>Removes every shield. A death, or a card being dropped.</summary>
        public void Clear() => _layers.Clear();

        private bool HasExpired(in ShieldLayer layer, long nowTick)
        {
            if (layer.ExpiresAtTick == ShieldLayer.Never)
                return false;

            return _policy.AbsorbsOnTheExpiryTick
                ? nowTick > layer.ExpiresAtTick
                : nowTick >= layer.ExpiresAtTick;
        }

        private int IndexOfSource(string sourceId)
        {
            for (int i = 0; i < _layers.Count; ++i)
            {
                if (string.Equals(_layers[i].SourceId, sourceId, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Which shield this hit eats into, by the policy. Expired shields are skipped rather
        /// than used, so an absorption on the same tick as an expiry agrees with
        /// <see cref="Expire"/>.
        /// <para>
        /// Ties are broken by the earlier position in the list, which is the order the shields
        /// were granted in - two shields granted on the same tick must not be chosen between
        /// by anything that could differ on a replay.
        /// </para>
        /// </summary>
        private int NextToAbsorb(long nowTick)
        {
            int best = -1;

            for (int i = 0; i < _layers.Count; ++i)
            {
                ShieldLayer layer = _layers[i];
                if (HasExpired(layer, nowTick) || layer.Remaining <= 0f)
                    continue;

                if (best < 0)
                {
                    best = i;
                    continue;
                }

                if (IsBetterChoice(layer, _layers[best]))
                    best = i;
            }

            return best;
        }

        private bool IsBetterChoice(in ShieldLayer candidate, in ShieldLayer standing)
        {
            switch (_policy.Order)
            {
                case ShieldAbsorptionOrder.NewestFirst:
                    return candidate.GrantedAtTick > standing.GrantedAtTick;

                case ShieldAbsorptionOrder.OldestFirst:
                    return candidate.GrantedAtTick < standing.GrantedAtTick;

                case ShieldAbsorptionOrder.ExpiringSoonestFirst:
                    return candidate.ExpiresAtTick < standing.ExpiresAtTick;

                default:
                    return false;
            }
        }

        public override string ToString() => $"{_layers.Count} shield(s), {Total:0.##} total";
    }
}
