using System;
using System.Collections.Generic;

namespace NeonVoid.Progression.Save
{
    /// <summary>The two places a plugin instance can be held.</summary>
    public enum PluginHolding
    {
        /// <summary>보관 DB. [확정] docs/04 section 3.3: capacity is unlimited.</summary>
        Archive = 0,

        /// <summary>
        /// 출전/현재 소지. [확정] docs/04 section 3.5: lost on death, and never archived
        /// automatically on a successful extraction.
        /// </summary>
        Loadout = 1,
    }

    /// <summary>
    /// `MovePlugin(instanceId, src, dst)` from docs/04 section 5, which exists to make one
    /// forbidden state unreachable.
    /// <para>
    /// [확정] docs/04 section 3.4: <i>보관 DB에서 출전 목록으로 꺼내면 DB에서 사라진다. 둘 사이에서
    /// '복사'하는 것이 아니라 동일 소유 인스턴스의 이동이다.</i> Section 7 names the forbidden
    /// result: DB+소지 중복 잔존. So this removes before it adds, inside one transaction, and
    /// the document's own validation refuses a state where an instance sits in both lists -
    /// a duplicated plugin is worse than a lost one, because afterwards nothing can tell which
    /// copy is real.
    /// </para>
    /// <para>
    /// <b>No capacity check, deliberately.</b> Section 3.3 says 보관 DB 용량 무제한 and 출전
    /// 플러그인의 개수 제한 없음. A limit is the kind of thing that gets added later as a
    /// sensible default, so a test holds the absence of one.
    /// </para>
    /// <para>
    /// <b>What this command cannot check.</b> The table's precondition is 동일 소유 인스턴스,
    /// 허브 상호작용. Ownership it checks - the instance has to be in the list it is being moved
    /// out of. Being in the hub it cannot: the save document holds no run state (D-017), so
    /// there is nothing here to ask. That precondition belongs to whatever offers the move, and
    /// is named here rather than quietly dropped.
    /// </para>
    /// <para>
    /// <b>Why the instance is an opaque id.</b> An instance record would want to carry what the
    /// plugin is and what level it is at - docs/04 section 3.1 says 학습할 때 레벨을 유지한다 -
    /// but docs/03's plugin ids are proposal ids for development and the document is explicit
    /// that a stable id must be approved before anything saves them. Writing G10 into a save
    /// file would make that proposal id permanent by accident. So the save holds instance
    /// identity and ownership, which is all this [확정] rule needs, and what an instance IS
    /// waits for the approved id.
    /// </para>
    /// </summary>
    public sealed class MovePluginCommand : ISaveCommand
    {
        private readonly string _instanceId;
        private readonly PluginHolding _from;
        private readonly PluginHolding _to;
        private readonly string _nonce;

        /// <param name="nonce">
        /// Distinguishes this move from the next one. An instance may legitimately be moved out
        /// of the archive and back again, so the instance and the direction are not enough to
        /// identify the command - docs/04 section 5 lists commandNonce among the keys for
        /// exactly this. The caller supplies one per hub interaction, so its retry carries the
        /// same one and a genuinely new move carries a new one.
        /// </param>
        public MovePluginCommand(string instanceId, PluginHolding from, PluginHolding to, string nonce)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                throw new ArgumentException("A move needs the instance it is moving.", nameof(instanceId));
            if (string.IsNullOrWhiteSpace(nonce))
                throw new ArgumentException(
                    "A move needs a nonce, or a retry could not be told apart from moving it back.", nameof(nonce));
            if (from == to)
                throw new ArgumentException($"Moving from {from} to itself is not a move.", nameof(to));

            _instanceId = instanceId;
            _from = from;
            _to = to;
            _nonce = nonce;
        }

        public static string ReceiptIdFor(
            string instanceId, PluginHolding from, PluginHolding to, string nonce) =>
            $"move:{instanceId}:{from}->{to}:{nonce}";

        public string ReceiptId => ReceiptIdFor(_instanceId, _from, _to, _nonce);

        public string Name => "MovePlugin";

        public SaveCommandOutcome Apply(SaveDocument next)
        {
            if (next == null)
                throw new ArgumentNullException(nameof(next));

            VaccineSaveState vaccine = next.vaccine;
            if (!vaccine.alive)
            {
                // 4번째 사망에서 해당 백신에 속한 DB까지 파기 (section 3.6): there is nothing
                // left to move, and a move that silently created an instance would be worse.
                return SaveCommandOutcome.Reject($"vaccine {vaccine.id} was destroyed, along with its database");
            }

            List<string> source = ListFor(vaccine, _from);
            List<string> destination = ListFor(vaccine, _to);

            if (!source.Contains(_instanceId))
            {
                return SaveCommandOutcome.Reject(
                    destination.Contains(_instanceId)
                        ? $"{_instanceId} is already in the {_to}"
                        : $"{_instanceId} is not in the {_from}");
            }

            if (destination.Contains(_instanceId))
            {
                // Unreachable through the store, because SaveDocument.Validate refuses a
                // document holding one instance twice. Checked anyway: this is the one state
                // the command exists to prevent, and the cost of the check is nothing.
                return SaveCommandOutcome.Reject(
                    $"{_instanceId} is in both the archive and the loadout; the save is inconsistent");
            }

            // Removed first, so there is no instant at which the next state holds two.
            source.Remove(_instanceId);
            destination.Add(_instanceId);

            return SaveCommandOutcome.Accept(
                $"instance={_instanceId};from={_from};to={_to};" +
                $"archive={vaccine.archivePluginInstanceIds.Count};loadout={vaccine.carriedPluginInstanceIds.Count}");
        }

        private static List<string> ListFor(VaccineSaveState vaccine, PluginHolding holding) =>
            holding == PluginHolding.Archive
                ? vaccine.archivePluginInstanceIds
                : vaccine.carriedPluginInstanceIds;
    }
}
