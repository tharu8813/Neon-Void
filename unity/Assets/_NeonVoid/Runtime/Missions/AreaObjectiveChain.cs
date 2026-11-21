using System;

namespace NeonVoid.Missions
{
    /// <summary>What a chain did with a node that reported itself held.</summary>
    public enum AreaChainOffer
    {
        /// <summary>Taken, and the chain is waiting for the next node.</summary>
        Accepted = 0,

        /// <summary>Taken, and that was the last one: the whole chain is done.</summary>
        Completed = 1,

        /// <summary>Not this node's turn. The chain is back at the start.</summary>
        OutOfOrder = 2,

        /// <summary>The chain was already done. Nothing changed.</summary>
        AlreadyComplete = 3,

        /// <summary>This node is already lit, so holding it again says nothing.</summary>
        AlreadyLit = 4,
    }

    /// <summary>
    /// The order <see cref="AreaObjectiveKind.Sequence"/> nodes have to be walked in, and the
    /// clock the walk is against. [초안] - docs/01 section 2 gives SSD 레일을 짧은 시간에 연결 and
    /// GPU 렌더 큐 정렬, and nothing else about either.
    /// <para>
    /// <b>A node reports nothing to the flow until the whole chain lands.</b> That is the reason
    /// this type exists instead of a counter on the devices. docs/01 section 6.6 fixes
    /// 완료 보상은 반복 호출에도 정확히 1회, so an objective that completed cannot un-complete -
    /// and a chain that is walked out of order has to go back to the start. The two can only
    /// both be true if a node that is merely lit has told the flow nothing yet.
    /// </para>
    /// <para>
    /// Pure C#: a tick count and an index, no Unity types and no clock of its own, so the whole
    /// behaviour is reproducible from the tick stream.
    /// </para>
    /// </summary>
    public sealed class AreaObjectiveChain
    {
        /// <summary>A chain with no clock, for <see cref="WindowTicks"/>.</summary>
        public const int NoWindow = AreaObjectives.NoChainWindow;

        private readonly int _length;
        private readonly int _windowTicks;

        public AreaObjectiveChain(int length, int windowTicks = NoWindow)
        {
            if (length < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(length), length, "A chain of no nodes would be complete before it began.");
            }

            if (windowTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(windowTicks), windowTicks, "A window cannot be negative.");

            _length = length;
            _windowTicks = windowTicks;
        }

        /// <summary>How many nodes make the chain.</summary>
        public int Length => _length;

        /// <summary>Ticks allowed from the first node to the last, or <see cref="NoWindow"/>.</summary>
        public int WindowTicks => _windowTicks;

        /// <summary>Whether this chain is against a clock at all.</summary>
        public bool HasWindow => _windowTicks > NoWindow;

        /// <summary>How many nodes are lit, which is also the index of the one expected next.</summary>
        public int Lit { get; private set; }

        /// <summary>Whether every node has been held in order.</summary>
        public bool IsComplete => Lit >= _length;

        /// <summary>Whether the walk has started and not finished.</summary>
        public bool IsRunning => Lit > 0 && !IsComplete;

        /// <summary>
        /// Ticks left before the window expires, or -1 when there is no window or no walk
        /// in progress. For a HUD gauge.
        /// </summary>
        public int TicksLeft { get; private set; } = -1;

        /// <summary>0 to 1 across the nodes, for a progress read-out.</summary>
        public float Progress => (float)Lit / _length;

        /// <summary>
        /// Takes a node that has just been held. The verdict says what the caller should show -
        /// and on <see cref="AreaChainOffer.OutOfOrder"/> the caller has to put every node's own
        /// dwell back, because the chain no longer counts any of them.
        /// </summary>
        public AreaChainOffer Offer(int index)
        {
            if (index < 0 || index >= _length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index), index, $"This chain has {_length} node(s).");
            }

            if (IsComplete)
                return AreaChainOffer.AlreadyComplete;

            if (index < Lit)
                return AreaChainOffer.AlreadyLit;

            if (index > Lit)
            {
                Reset();
                return AreaChainOffer.OutOfOrder;
            }

            ++Lit;

            if (IsComplete)
            {
                TicksLeft = -1;
                return AreaChainOffer.Completed;
            }

            // The clock starts on the first node, not on entering the area: docs/01 asks for a
            // short connection, not a short area.
            if (HasWindow && Lit == 1)
                TicksLeft = _windowTicks;

            return AreaChainOffer.Accepted;
        }

        /// <summary>
        /// Advances the window one tick. Returns true <b>on the tick it expires</b>, once, with
        /// the chain already back at the start - so the caller resets the nodes it had lit.
        /// </summary>
        public bool Tick()
        {
            if (!HasWindow || !IsRunning || TicksLeft < 0)
                return false;

            --TicksLeft;
            if (TicksLeft > 0)
                return false;

            Reset();
            return true;
        }

        /// <summary>Back to no nodes lit and no clock running.</summary>
        public void Reset()
        {
            Lit = 0;
            TicksLeft = -1;
        }

        public override string ToString() =>
            HasWindow
                ? $"chain {Lit}/{_length}, {(TicksLeft < 0 ? "clock idle" : TicksLeft + "t left")}"
                : $"chain {Lit}/{_length}";
    }
}
