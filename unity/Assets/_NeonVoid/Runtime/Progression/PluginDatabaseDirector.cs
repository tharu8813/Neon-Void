using NeonVoid.Progression.Save;
using NeonVoid.UI;
using UnityEngine;

namespace NeonVoid.Progression
{
    /// <summary>
    /// The 플러그인 데이터베이스 터미널's half that touches permanent state: it reads the owned
    /// instances and commits a move between 보관 DB and 출전.
    /// <para>
    /// <b>Why a director and not the screen.</b> A move is a permanent write, and [확정] docs/04
    /// section 5 step 7 is explicit that the UI reflects it only after the commit is
    /// acknowledged. Putting the commit behind this, the way
    /// <see cref="VaccineGrowthDirector"/> does for 영구 투자, keeps a screen from being able to
    /// show a move that was never written.
    /// </para>
    /// <para>
    /// <b>The nonce counts moves, not attempts.</b> An instance may legitimately go out of the
    /// archive and back, so the instance and the direction do not identify the command -
    /// <see cref="MovePluginCommand"/> says so. A refused write keeps its nonce, so the next
    /// attempt is a retry of the same move rather than a second one.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class PluginDatabaseDirector : MonoBehaviour
    {
        [Tooltip("Found automatically when left empty.")]
        [SerializeField] private VaccineSaveDirector saveDirector;

        private int _moves;

        /// <summary>Why the last move was refused, or empty.</summary>
        public string LastRefusal { get; private set; } = string.Empty;

        /// <summary>Whether a move would be written at all.</summary>
        public bool CanMove => saveDirector != null && saveDirector.CanCommit;

        /// <summary>Wires it up without a scene, for a test.</summary>
        public void Configure(VaccineSaveDirector saves) => saveDirector = saves;

        private void Awake()
        {
            if (saveDirector == null)
                saveDirector = FindFirstObjectByType<VaccineSaveDirector>();
        }

        /// <summary>
        /// What the terminal draws, built from the committed snapshot each time it is asked.
        /// <para>
        /// Rebuilt rather than cached: the snapshot changes under it whenever anything else
        /// commits - an extraction learning a card, a death clearing the loadout - and a cached
        /// list would show the player a database they no longer have.
        /// </para>
        /// </summary>
        public PluginDatabaseSheet Sheet
        {
            get
            {
                if (saveDirector == null || saveDirector.Store == null)
                    return PluginDatabaseSheet.Closed;

                return PluginDatabaseSheet.Build(
                    saveDirector.Store.Snapshot.vaccine, CanMove, WhyNotAvailable());
            }
        }

        /// <summary>
        /// Moves one instance to the other side. [확정] docs/04 section 3.4: it leaves where it
        /// was. The command and the document's own validation make the forbidden state -
        /// DB와 출전에 동시에 - unreachable rather than merely unwanted.
        /// </summary>
        public bool TryMove(string instanceId, PluginHolding from, PluginHolding to)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                return Refuse("이동할 인스턴스가 지정되지 않았다");

            if (from == to)
                return Refuse("같은 곳으로는 옮길 수 없다");

            if (saveDirector == null || saveDirector.Store == null)
                return Refuse("저장이 연결되지 않아 이동을 기록할 수 없다");

            if (!saveDirector.CanCommit)
                return Refuse(saveDirector.Result.Detail);

            string nonce = $"{saveDirector.SessionNonce}:{_moves}";
            SaveCommitResult commit = saveDirector.Store.Commit(
                new MovePluginCommand(instanceId, from, to, nonce));

            if (!commit.Acknowledged)
                return Refuse(commit.Detail);

            // Only now: a move whose write failed keeps its nonce.
            _moves++;
            LastRefusal = string.Empty;

            Debug.Log($"플러그인 이동: {instanceId} {from} → {to} · {commit.Result}", this);
            return true;
        }

        /// <summary>Moves a row to wherever it is not. What the terminal's one button does.</summary>
        public bool TryMove(in PluginDatabaseRow row) =>
            TryMove(row.InstanceId, row.Holding, row.MovesTo);

        private string WhyNotAvailable()
        {
            if (saveDirector == null || saveDirector.Store == null)
                return "저장이 연결되지 않아 이동이 기록되지 않습니다";

            if (!saveDirector.CanCommit)
                return saveDirector.Result.Detail;

            return string.Empty;
        }

        private bool Refuse(string why)
        {
            LastRefusal = why ?? string.Empty;
            Debug.LogWarning($"{name}: 이동 거부 - {LastRefusal}", this);
            return false;
        }
    }
}
