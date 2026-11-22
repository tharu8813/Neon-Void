using System;
using System.Collections.Generic;
using NeonVoid.Progression;
using NeonVoid.Progression.Save;

namespace NeonVoid.UI
{
    /// <summary>Which of the death screen's five readings is on show.</summary>
    public enum DeathScreenPhase
    {
        /// <summary>The run is live. Nothing is drawn.</summary>
        None = 0,

        /// <summary>A death is recorded and the save has not answered. No numbers yet.</summary>
        AwaitingCommit = 1,

        /// <summary>The save refused or failed. The same death may be retried.</summary>
        CommitFailed = 2,

        /// <summary>Written and verified; the vaccine survived it.</summary>
        Settled = 3,

        /// <summary>[확정] docs/04 section 2: the fourth death. 최종 파기.</summary>
        Destroyed = 4,
    }

    /// <summary>
    /// What the death screen may say, and - more to the point - what it may not.
    /// <para>
    /// [확정] CLAUDE.md: <i>보상/사망/철수/DB 이동은 저장 커밋 승인 후 UI 및 게임 상태 확정.</i>
    /// So the clone count, the death count and the destruction warning exist on this type only
    /// in <see cref="DeathScreenPhase.Settled"/> and <see cref="DeathScreenPhase.Destroyed"/>,
    /// where an acknowledged write produced them. Before that <see cref="NumbersKnown"/> is
    /// false and there is nothing to draw but the phase.
    /// </para>
    /// <para>
    /// <b>복제 0 is not 파기.</b> [확정] docs/04 section 2: <i>'복제 0'은 죽은 상태가 아니라
    /// 마지막 기회</i>, and docs/05 section 2 names the same edge - 3번째 사망 잔여 0 ≠ 파기,
    /// 4번째만 파기. Destruction is read from permanent state's own answer, never inferred from
    /// the count reaching zero.
    /// </para>
    /// <para>
    /// <b>What is not decided is named, not drawn as zero.</b> docs/05 asks this screen for
    /// 이번 의뢰 손실 and 보존 XP. The settlement range is D-012 and the carried plugins are
    /// D-006/D-007, so neither has a value. "0 XP" would read as a settled loss of everything.
    /// </para>
    /// </summary>
    public readonly struct DeathScreenSnapshot
    {
        private static readonly string[] Nothing = Array.Empty<string>();

        /// <summary>
        /// [미확정] docs/04 section 2 settles 20~70% of the mission's unsettled permanent XP by
        /// progress, and D-012 has not fixed the curve. Stated as unsettled rather than drawn.
        /// </summary>
        public const string UnsettledXp = "보존 XP — 20~70% 범위만 확정, 정산 기준 미확정 (D-012)";

        /// <summary>
        /// [확정] docs/04 section 3 item 5: 사망 시 출전 소지분은 소실. There is nothing to lose
        /// yet, because plugin acquisition is D-006/D-007 and does not exist.
        /// </summary>
        public const string UnsettledCarried = "출전 소지 플러그인 손실 — 획득 자체가 미구현 (D-006/D-007)";

        private DeathScreenSnapshot(
            DeathScreenPhase phase,
            string title,
            string status,
            string detail,
            string nextStep,
            string deathId,
            bool numbersKnown,
            int clonesRemaining,
            int deathCount,
            bool lastChance,
            string[] unsettled)
        {
            Phase = phase;
            Title = title;
            Status = status;
            Detail = detail;
            NextStep = nextStep;
            DeathIdText = deathId ?? string.Empty;
            NumbersKnown = numbersKnown;
            ClonesRemaining = clonesRemaining;
            DeathCount = deathCount;
            LastChance = lastChance;
            _unsettled = unsettled ?? Nothing;
        }

        private readonly string[] _unsettled;

        /// <summary>The run is live and the screen is not drawn.</summary>
        public static readonly DeathScreenSnapshot None = default;

        public DeathScreenPhase Phase { get; }

        /// <summary>Whether the screen is on screen at all.</summary>
        public bool Shown => Phase != DeathScreenPhase.None;

        public string Title { get; }

        /// <summary>The one line that says where this death stands.</summary>
        public string Status { get; }

        /// <summary>What that means for the player, in the save's own terms.</summary>
        public string Detail { get; }

        /// <summary>What happens next, including when the answer is "nothing yet".</summary>
        public string NextStep { get; }

        /// <summary>The death's id, so a failure can be matched against a receipt.</summary>
        public string DeathIdText { get; }

        /// <summary>
        /// True only when an acknowledged write produced the counts. False is not zero: it
        /// means nobody has been charged anything and the screen must not pretend otherwise.
        /// </summary>
        public bool NumbersKnown { get; }

        /// <summary>복제 잔여, once written. Meaningless unless <see cref="NumbersKnown"/>.</summary>
        public int ClonesRemaining { get; }

        /// <summary>누적 사망, once written.</summary>
        public int DeathCount { get; }

        /// <summary>
        /// 복제 0 with the vaccine still alive. [확정] docs/04 section 2 calls this
        /// 마지막 기회, not a destroyed state.
        /// </summary>
        public bool LastChance { get; }

        /// <summary>How many more deaths this vaccine has, once written.</summary>
        public int DeathsLeft =>
            NumbersKnown
                ? Math.Max(0, VaccineSaveState.DesignDeathsBeforeFinalDestruction - DeathCount)
                : 0;

        /// <summary>What docs/05 asks for that no decision has given a value to.</summary>
        public IReadOnlyList<string> Unsettled => _unsettled ?? Nothing;

        /// <summary>
        /// A death the save has not answered. No counts: nothing has been charged, and a clone
        /// number here would be a guess at the result of a write that has not happened.
        /// </summary>
        public static DeathScreenSnapshot Pending(DeathId id, bool commitConnected) =>
            new DeathScreenSnapshot(
                DeathScreenPhase.AwaitingCommit,
                title: "사망",
                status: commitConnected ? "저장 커밋 대기 중" : "저장 미연결",
                detail: commitConnected
                    ? "복제 차감·XP 정산·출전 소지 처리는 아직 아무것도 일어나지 않았습니다."
                    : "이 세션은 기록되지 않습니다. 복제는 차감되지 않았고 이 사망은 확정되지 않습니다.",
                nextStep: commitConnected
                    ? "저장이 답할 때까지 결과는 표시되지 않습니다."
                    : "저장을 켜고 다시 플레이해야 이 사망이 확정됩니다.",
                deathId: id.ToString(),
                numbersKnown: false,
                clonesRemaining: 0,
                deathCount: 0,
                lastChance: false,
                unsettled: Nothing);

        /// <summary>
        /// A write that refused or failed. Still not a result: the same death may be retried,
        /// and [확정] CLAUDE.md forbids 재지급 on a retry.
        /// </summary>
        public static DeathScreenSnapshot Failed(DeathId id, string reason) =>
            new DeathScreenSnapshot(
                DeathScreenPhase.CommitFailed,
                title: "사망",
                status: "저장 커밋 실패",
                detail: string.IsNullOrEmpty(reason) ? "원인이 기록되지 않았습니다." : reason,
                nextStep: "같은 사망 ID로 재시도합니다. 중복 차감은 일어나지 않습니다.",
                deathId: id.ToString(),
                numbersKnown: false,
                clonesRemaining: 0,
                deathCount: 0,
                lastChance: false,
                unsettled: Nothing);

        /// <summary>
        /// A written, verified death the vaccine survived. The only reading that carries
        /// numbers, because it is the only one where numbers were written.
        /// </summary>
        public static DeathScreenSnapshot SettledDeath(DeathId id, int clonesRemaining, int deathCount)
        {
            bool last = clonesRemaining <= 0;

            return new DeathScreenSnapshot(
                DeathScreenPhase.Settled,
                title: "사망",
                status: last
                    ? "복제 0 · 마지막 기회"
                    : $"복제 {clonesRemaining}개 남음",
                detail: last
                    // [확정] docs/04 section 2: '복제 0'은 죽은 상태가 아니라 마지막 기회.
                    ? "복제 0은 파기가 아닙니다. 다음 사망에서 이 백신이 파기됩니다."
                    : "복제를 하나 쓰고 허브에서 재시도합니다. 복제 회복은 어떤 수단으로도 불가합니다.",
                nextStep: "이 의뢰는 실패했습니다. 다음 의뢰는 새 시드·새 적으로 생성됩니다.",
                deathId: id.ToString(),
                numbersKnown: true,
                clonesRemaining: clonesRemaining,
                deathCount: deathCount,
                lastChance: last,
                unsettled: new[] { UnsettledXp, UnsettledCarried });
        }

        /// <summary>
        /// [확정] docs/04 section 2: the fourth death destroys this vaccine's growth, skills,
        /// DB and carried items. Nothing is preserved, so nothing is listed as unsettled.
        /// </summary>
        public static DeathScreenSnapshot FinalDestruction(DeathId id, int deathCount) =>
            new DeathScreenSnapshot(
                DeathScreenPhase.Destroyed,
                title: "최종 파기",
                status: "이 백신은 파기되었습니다",
                detail: "성장·스킬·보관 DB·출전 소지가 모두 파기되었습니다. " +
                        "다른 백신 슬롯과 화면·음량·키 설정은 보존됩니다.",
                nextStep: "백신 선택 화면 — 아직 구현되지 않았습니다 (docs/05 section 2).",
                deathId: id.ToString(),
                numbersKnown: true,
                clonesRemaining: 0,
                deathCount: deathCount,
                lastChance: false,
                unsettled: Nothing);

        /// <summary>
        /// The reading for a ledger phase and what permanent state answered.
        /// <para>
        /// <paramref name="vaccineAlive"/> comes from the write, not from the clone count:
        /// zero clones and a destroyed vaccine are different states and look alike in a
        /// counter.
        /// </para>
        /// </summary>
        public static DeathScreenSnapshot From(
            RunDeathPhase phase,
            DeathId id,
            string failureReason,
            bool commitConnected,
            bool vaccineAlive,
            int clonesRemaining,
            int deathCount)
        {
            switch (phase)
            {
                case RunDeathPhase.AwaitingCommit:
                    return Pending(id, commitConnected);

                case RunDeathPhase.CommitFailed:
                    return Failed(id, failureReason);

                case RunDeathPhase.Committed:
                    return vaccineAlive
                        ? SettledDeath(id, clonesRemaining, deathCount)
                        : FinalDestruction(id, deathCount);

                default:
                    return None;
            }
        }

        public override string ToString() =>
            Shown ? $"{Title} · {Status}" : "alive";
    }
}
