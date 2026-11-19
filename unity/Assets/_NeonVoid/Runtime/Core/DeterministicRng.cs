using System;

namespace NeonVoid.Core
{
    /// <summary>
    /// Small deterministic local generator for prototypes and seed/replay tests.
    /// This is NOT cryptographic and must not be replaced without save-version review.
    /// Production needs independently versioned RNG streams for runs, combat and rewards.
    /// </summary>
    public sealed class DeterministicRng
    {
        private uint _state;

        public DeterministicRng(uint seed)
        {
            _state = seed == 0 ? 0x9E3779B9u : seed;
        }

        public uint State => _state;

        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            uint range = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt() % range);
        }
    }
}
