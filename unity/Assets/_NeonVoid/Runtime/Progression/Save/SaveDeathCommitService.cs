using System;

namespace NeonVoid.Progression.Save
{
    /// <summary>
    /// Supplies the figure a death settles into permanent state.
    /// <para>
    /// Separate from everything else because the figure is undecided. docs/04 section 2's
    /// 20~70% rate is [초안] and D-012 is open, and <b>no run tracks 미정산 XP at all yet</b> -
    /// there is no RunXP system to take a share of. An interface here is what CLAUDE.md asks
    /// for in that situation: the policy can be decided later without reopening the code that
    /// writes to disk.
    /// </para>
    /// </summary>
    public interface IDeathSettlementSource
    {
        DeathSettlement SettlementFor(in DeathReport report);
    }

    /// <summary>
    /// Settles nothing, and is the default.
    /// <para>
    /// A death that awards zero is visibly unfinished. A death that awards an invented share of
    /// an invented total is not - it looks like a working settlement, writes a number into
    /// permanent state, and would have to be unpicked from real saves once D-012 is decided.
    /// Zero is the only figure that is true today.
    /// </para>
    /// </summary>
    public sealed class NoDeathSettlement : IDeathSettlementSource
    {
        public static readonly NoDeathSettlement Instance = new NoDeathSettlement();

        public DeathSettlement SettlementFor(in DeathReport report) => DeathSettlement.None;
    }

    /// <summary>
    /// Turns the combat engine's death report into the `RegisterDeath` save command, which is
    /// the seam <see cref="IDeathCommitService"/> was written around and left empty for.
    /// <para>
    /// <b>Ordering is the whole point.</b> docs/04 section 5 step 7: a result reaches the UI,
    /// combat or growth services only after a verified commit. So this raises
    /// <see cref="CommitCompleted"/> from the store's answer and never from its own optimism -
    /// a failed write produces a failure ack, and the ledger stays where it was. docs/04
    /// section 7 lists the forbidden alternative by name: UI 먼저 '보상 획득 완료'.
    /// </para>
    /// <para>
    /// <b>A retry is the same death.</b> The receipt id comes from the
    /// <see cref="DeathId"/>, which is composed from the run, the actor and a counter rather
    /// than minted fresh, so the attempt after a lost acknowledgement - even one made on the
    /// next launch of the game - carries the id the first attempt carried and is answered from
    /// the receipt log instead of charging a second clone.
    /// </para>
    /// <para>
    /// <b>This is deliberately not installed in the prototype scene.</b> docs/07 says that
    /// while the undecided policies are open, 영구 자산 소실/지급과 관련된 부분은 실제 커밋
    /// 연결을 막는다 - and a debug arena where the player dies repeatedly would charge clones
    /// against a real save, which no mechanism in the game can refund. Something has to choose
    /// to construct this with a real store, and nothing in the prototype does.
    /// </para>
    /// </summary>
    public sealed class SaveDeathCommitService : IDeathCommitService
    {
        private readonly SaveStore _store;
        private readonly IDeathSettlementSource _settlement;

        public SaveDeathCommitService(SaveStore store, IDeathSettlementSource settlement = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _settlement = settlement ?? NoDeathSettlement.Instance;
        }

        /// <summary>Raised once per finished attempt, successful or not.</summary>
        public event Action<DeathCommitAck> CommitCompleted;

        /// <summary>
        /// The store's answer to the last attempt, which carries what the receipt says - clones
        /// left, whether the vaccine was destroyed. The ack itself is deliberately only a
        /// success flag, and a recovery screen needs the detail.
        /// </summary>
        public SaveCommitResult LastCommit { get; private set; }

        public void RequestDeathCommit(in DeathReport report)
        {
            DeathSettlement settlement;
            try
            {
                settlement = _settlement.SettlementFor(report);
            }
            catch (Exception e)
            {
                // A settlement policy that throws must not take the death with it. The death
                // happened; refusing to record it would leave the run waiting forever.
                Announce(DeathCommitAck.Failure(report.Id, $"the settlement policy threw: {e.Message}"));
                return;
            }

            SaveCommitResult result = _store.Commit(new RegisterDeathCommand(report.Id, settlement));
            LastCommit = result;

            Announce(result.Acknowledged
                ? DeathCommitAck.Success(report.Id)
                : DeathCommitAck.Failure(report.Id, Describe(result)));
        }

        private void Announce(DeathCommitAck ack) => CommitCompleted?.Invoke(ack);

        private static string Describe(in SaveCommitResult result) =>
            string.IsNullOrEmpty(result.Detail) ? result.Verdict.ToString() : $"{result.Verdict}: {result.Detail}";
    }
}
