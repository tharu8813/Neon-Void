using System.Collections.Generic;
using System.Text;
using NeonVoid.Combat;
using NeonVoid.Progression.Save;
using UnityEngine;

namespace NeonVoid.Progression
{
    /// <summary>
    /// 백신 성장: spends 영구 능력치 포인트, gives them all back for free, and puts the result on
    /// the player.
    /// <para>
    /// [확정] GDD v1 section 4.3: 레벨업마다 영구 포인트를 지급하고, 허브에서 재분배 비용 없이
    /// 언제든 초기화·재투자한다. Until now the save held <c>unusedStatPoints</c> and there was no
    /// way to spend one, so every level-up added to a number that did nothing.
    /// </para>
    /// <para>
    /// <b>Nothing changes until the save says so.</b> Both operations go through the store's
    /// transaction and the stats are only recomputed after a commit is acknowledged - docs/04
    /// section 5 step 7, and section 7's forbidden outcome is the UI showing the new number
    /// first. A refused purchase leaves the player exactly as they were.
    /// </para>
    /// <para>
    /// <b>Recomputed from the original, never accumulated.</b> docs/02 section 2 asks for
    /// 원본 기반 재계산 by name, so the actor's base block is captured once, before anything is
    /// invested, and every refresh applies the whole allocation to that captured copy. Adding
    /// the increment to the live stats instead would double every investment the second time
    /// anything asked for a refresh.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-120)]
    public sealed class VaccineGrowthDirector : MonoBehaviour
    {
        [Tooltip("Whose stats the investment lands on. The player, found when left empty.")]
        [SerializeField] private CombatActor playerActor;

        [Tooltip("Permanent state. Found in the scene when left empty.")]
        [SerializeField] private VaccineSaveDirector saveDirector;

        private CombatStats _uninvested;
        private bool _captured;
        private int _purchases;

        /// <summary>Why the last refused call was refused. Empty after one that succeeded.</summary>
        public string LastRefusal { get; private set; } = string.Empty;

        /// <summary>True when a purchase would actually be written.</summary>
        public bool CanInvest => saveDirector != null && saveDirector.CanCommit;

        /// <summary>영구 능력치 포인트 not yet spent, or 0 when no save is connected.</summary>
        public int UnusedStatPoints => Vaccine != null ? Vaccine.unusedStatPoints : 0;

        /// <summary>Points this vaccine has in the table.</summary>
        public int PointsSpent => VaccineInvestments.PointsSpent(Vaccine);

        /// <summary>What is invested right now.</summary>
        public IReadOnlyList<VaccineStatInvestment> Allocation => VaccineInvestments.Read(Vaccine);

        /// <summary>The stats the player would have with nothing invested.</summary>
        public CombatStats Uninvested
        {
            get
            {
                Capture();
                return _uninvested;
            }
        }

        private VaccineSaveState Vaccine =>
            saveDirector != null && saveDirector.Store != null && saveDirector.Store.Snapshot != null
                ? saveDirector.Store.Snapshot.vaccine
                : null;

        /// <summary>Fills in what a scene would have serialized. Call before this object wakes.</summary>
        public void Configure(CombatActor actor, VaccineSaveDirector saves)
        {
            playerActor = actor;
            saveDirector = saves;
        }

        private void Awake()
        {
            if (playerActor == null)
            {
                var driver = FindFirstObjectByType<Player.PlayerSimulationDriver>();
                playerActor = driver != null ? driver.GetComponent<CombatActor>() : null;
            }

            if (saveDirector == null)
                saveDirector = FindFirstObjectByType<VaccineSaveDirector>();
        }

        /// <summary>
        /// The capture happens here and not in <c>Awake</c>, and the ordering is the whole
        /// reason: <see cref="CombatActor"/> builds its base block in its own Awake, and this
        /// component runs earlier (it has to, so the hub finds it). Capturing in Awake would
        /// have recorded a stat block of zeroes and then "restored" the player to it.
        /// </summary>
        private void Start()
        {
            Capture();
            Refresh();
        }

        /// <summary>
        /// Spends points on one stat and, once the save has taken it, puts the result on the
        /// player.
        /// </summary>
        public bool TryInvest(InvestableStat stat, int steps = 1)
        {
            if (!Ready(out string why))
                return Refuse(why);

            if (steps < 1)
                return Refuse($"{StatInvestments.For(stat).DisplayName}에 {steps}단계를 투자할 수 없다");

            if (!StatInvestments.IsConnected(stat))
            {
                // Checked here as well as in the command, so a terminal can grey the row out
                // instead of offering a purchase that will be refused.
                return Refuse(
                    $"{StatInvestments.For(stat).DisplayName}: {StatInvestments.WhyNotConnected(stat)}");
            }

            string nonce = $"{saveDirector.SessionNonce}:{_purchases}";
            SaveCommitResult commit = saveDirector.Store.Commit(
                new InvestStatPointsCommand(stat, steps, nonce));

            if (!commit.Acknowledged)
                return Refuse(commit.Detail);

            // Only now. A purchase whose write failed keeps its nonce, so the next attempt is
            // a retry of the same purchase rather than a second one.
            _purchases++;
            Refresh();
            LastRefusal = string.Empty;

            Debug.Log(
                $"영구 투자: {StatInvestments.For(stat).DisplayName} x{steps} · {commit.Result}", this);
            return true;
        }

        /// <summary>
        /// 무료 재분배. [확정] 재분배 비용 없이 언제든 초기화·재투자: every point comes back and
        /// the allocation is emptied.
        /// </summary>
        public bool TryRedistribute()
        {
            if (!Ready(out string why))
                return Refuse(why);

            string nonce = $"{saveDirector.SessionNonce}:{_purchases}";
            SaveCommitResult commit = saveDirector.Store.Commit(new RedistributeStatPointsCommand(nonce));
            if (!commit.Acknowledged)
                return Refuse(commit.Detail);

            _purchases++;
            Refresh();
            LastRefusal = string.Empty;

            Debug.Log($"무료 재분배: {commit.Result}", this);
            return true;
        }

        /// <summary>
        /// Recomputes the player's stats from the captured base and the committed allocation.
        /// <para>
        /// Public because anything that changes permanent state - a level-up inside a death
        /// settlement, an extraction - can leave the allocation the same and the points
        /// different, and because a scene may want to apply the save it just loaded.
        /// </para>
        /// </summary>
        public void Refresh()
        {
            if (playerActor == null)
                return;

            Capture();
            CombatStats invested = StatInvestments.Apply(_uninvested, Allocation);

            // Configure refills health, which is right here and nowhere else: investment
            // happens in the hub, between missions. Anything that called this mid-mission
            // would be handing the player a free heal, so nothing does.
            playerActor.Configure(playerActor.TeamId, invested);
        }

        /// <summary>The whole sheet, for the terminal's panel and for a log line.</summary>
        public string Sheet()
        {
            var text = new StringBuilder();
            text.Append("백신 성장 · 미사용 포인트 ").Append(UnusedStatPoints)
                .Append(" · 투자 ").Append(PointsSpent).Append('p');

            if (!CanInvest)
                text.Append(" · 저장이 커밋하지 않으므로 투자할 수 없다");

            text.Append('\n').Append(VaccineInvestments.Describe(Vaccine));
            return text.ToString();
        }

        /// <summary>One line for the debug overlay.</summary>
        public string StatusLine() =>
            $"성장: 미사용 {UnusedStatPoints}p · 투자 {PointsSpent}p" + (CanInvest ? string.Empty : " (미연결)");

        private void Capture()
        {
            if (_captured || playerActor == null)
                return;

            // Before anything is applied, so this is the block the scene authored rather than
            // one that already has an investment in it.
            _uninvested = playerActor.BaseStats;
            _captured = true;
        }

        private bool Ready(out string why)
        {
            if (saveDirector == null)
            {
                why = "저장 디렉터가 없어 영구 투자를 쓸 수 없다";
                return false;
            }

            if (!saveDirector.CanCommit)
            {
                why = $"저장이 커밋하지 않는다 ({saveDirector.Result.Verdict}); 영구 성장은 쓰이지 않는다";
                return false;
            }

            why = string.Empty;
            return true;
        }

        private bool Refuse(string why)
        {
            LastRefusal = why;
            Debug.Log($"{name}: {why}", this);
            return false;
        }
    }
}
