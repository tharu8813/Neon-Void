using System;

namespace NeonVoid.Progression
{
    /// <summary>
    /// What the combat engine knows about a death, and nothing else.
    /// <para>
    /// [확정] docs/02 section "사망·영구 보상·파기는 전투 엔진 내부에서 저장하지 않고 progression
    /// 저장 서비스에 명령한다": combat reports that an actor died and leaves every consequence
    /// to the save service.
    /// </para>
    /// <para>
    /// <b>What is deliberately not in here.</b> No clone count, no settled XP, no plugin loss,
    /// no progress fraction. The 20~70% settlement formula is D-012, still undecided, and the
    /// clone ledger is permanent state the save service owns. A field for any of them would
    /// invite the combat engine to compute it, which is exactly the boundary docs/02 draws.
    /// </para>
    /// </summary>
    [Serializable]
    public readonly struct DeathReport
    {
        /// <summary>Dedupe key. The save service must treat two reports with this id as one death.</summary>
        public readonly DeathId Id;

        /// <summary>Simulation tick the death resolved on, for ordering and for logs.</summary>
        public readonly long TickIndex;

        public DeathReport(DeathId id, long tickIndex)
        {
            if (!id.IsValid)
                throw new ArgumentException("A death report needs a valid DeathId to be deduped by.", nameof(id));
            if (tickIndex < 0L)
                throw new ArgumentOutOfRangeException(nameof(tickIndex), "Ticks start at 0.");

            Id = id;
            TickIndex = tickIndex;
        }
    }
}
