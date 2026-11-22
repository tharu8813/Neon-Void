using System;
using System.Collections.Generic;
using NeonVoid.Core;
using NeonVoid.Progression;

namespace NeonVoid.Missions
{
    /// <summary>Where the player is in the mission. [확정] docs/01 section 1's geography.</summary>
    public enum MissionFlowPhase
    {
        /// <summary>디펜더 허브. No mission running; the vaccine is equipped from here.</summary>
        Hub = 0,

        /// <summary>메인보드. A mission is running and no area is entered.</summary>
        Motherboard = 1,

        /// <summary>하드웨어 내부.</summary>
        InArea = 2,

        /// <summary>성공 철수 committed. Waiting to walk back into the hub.</summary>
        Extracted = 3,

        /// <summary>The player died. [확정] 사망은 해당 의뢰 실패.</summary>
        Failed = 4,
    }

    /// <summary>
    /// How far through an area the player is. [확정] docs/01 section 1:
    /// 시작 → 2~3개 소목표 → 보스 → 정화 → 철수 또는 메인보드 귀환, with section 6.6's rule that
    /// 목표가 완료되기 전에 보스·철수가 활성화되지 않음.
    /// </summary>
    public enum AreaProgressPhase
    {
        /// <summary>Not in an area.</summary>
        None = 0,

        /// <summary>소목표 outstanding. The boss is shut.</summary>
        Objectives = 1,

        /// <summary>Objectives done; the boss is open.</summary>
        Boss = 2,

        /// <summary>Boss down; 정화 is open.</summary>
        Purification = 3,

        /// <summary>정화 committed. 철수 or 메인보드 귀환.</summary>
        Purified = 4,
    }

    /// <summary>
    /// The mission as a state machine: 허브 → 메인보드 → 하드웨어 구역 → 목표 → 보스 → 정화 →
    /// 철수 → 허브.
    /// <para>
    /// <b>No scenes, no 3D, no content.</b> This is the outer loop docs/01 fixes as [확정],
    /// expressed as phases and guards, so that the map generator, the enemies, the boss patterns
    /// and the art are built onto a loop that already runs end to end rather than the other way
    /// round. Every rule it holds is one the documents state:
    /// </para>
    /// <list type="bullet">
    /// <item>입장 순서는 자유 - any area that is not already purified, in any order.</item>
    /// <item>이미 정화한 하드웨어의 완료 상태는 런 동안 유지, and 중복 정화 금지 - a purified area
    /// cannot be entered again.</item>
    /// <item>목표가 완료되기 전에 보스·철수가 활성화되지 않음 (section 6.6).</item>
    /// <item>임무 종료는 1개 이상 정화 뒤 철수 성공, and 5개 완주 강요 금지.</item>
    /// <item>성공 철수 시 일반·레어 신규 학습 최대 2개 ([확정] R-015).</item>
    /// <item>사망은 해당 의뢰 실패, one-way.</item>
    /// </list>
    /// <para>
    /// <b>Permanent transitions ask the gate first.</b> 정화 and 철수 are save commands, and this
    /// does not advance past either until <see cref="IMissionCommitGate"/> says permanent state
    /// took the change - docs/04 section 5 step 7. The ordering lives here, where a test can
    /// drive a refusing gate, instead of in a MonoBehaviour where it cannot.
    /// </para>
    /// <para>
    /// <b>What it deliberately does not do.</b> Leaving an area that has not been purified is
    /// refused rather than implemented: that is a 재방문, and docs/01 section 1 leaves
    /// 같은 하드웨어 재방문과 철수 지점 연결 규칙 [미확정] (D-010) with the instruction
    /// 반복 보상 지급·중복 정화 금지. Refusing is the restrictive reading, and the refusal names
    /// the decision so the rule is visible rather than quietly chosen.
    /// </para>
    /// Pure C#: no Unity types, no storage, no clock.
    /// </summary>
    public sealed class MissionFlow
    {
        private readonly MissionRunState _run;
        private readonly IMissionCommitGate _commits;
        private readonly List<HardwareId> _purified = new List<HardwareId>();

        private HardwareId _area = HardwareId.None;
        private int _objectivesInArea;
        private int _objectivesRequired;

        public MissionFlow(MissionRunState run, IMissionCommitGate commits)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _commits = commits ?? throw new ArgumentNullException(nameof(commits));
        }

        public MissionFlowPhase Phase { get; private set; } = MissionFlowPhase.Hub;

        /// <summary>
        /// Raised once when a mission stops running, whichever way it ended.
        /// <para>
        /// It exists for state that belongs to one 의뢰 and to nothing else - [확정] D-019's
        /// R02 kill stacks are the first. Those used to end only by accident, when the card
        /// stopped being carried, so a session that played a second mission carried the first
        /// one's stacks into it.
        /// </para>
        /// <para>
        /// Raised on 철수 and on 사망 alike, and not on the walk back to the hub: the mission is
        /// over at the moment it succeeded or failed, and a player standing on the death screen
        /// with +10 AD still showing would be reading a bonus they no longer have.
        /// </para>
        /// </summary>
        public event System.Action MissionEnded;

        /// <summary>
        /// Raised once per area, after its 정화 is committed. [확정] docs/01 ties a 보상 화면
        /// to a purification, so this is where one opens.
        /// <para>
        /// Raised <b>after</b> the commit, never before: docs/04 section 7's forbidden outcome
        /// is a reward shown for a purification that was not written.
        /// </para>
        /// </summary>
        public event System.Action<HardwareId> AreaPurified;

        /// <summary>How many areas this mission has purified. Raises the upper card grades.</summary>
        public int PurifiedCount => _purified.Count;

        public AreaProgressPhase AreaPhase { get; private set; } = AreaProgressPhase.None;

        /// <summary>The live mission. Identity, 의뢰 레벨, purification count.</summary>
        public MissionRunState Run => _run;

        /// <summary>The area being played, or <see cref="HardwareId.None"/>.</summary>
        public HardwareId CurrentArea => _area;

        /// <summary>소목표 completed in the current area.</summary>
        public int ObjectivesCompletedInArea => _objectivesInArea;

        /// <summary>소목표 the current area asks for.</summary>
        public int ObjectivesRequiredInArea => _objectivesRequired;

        /// <summary>
        /// 주요 목표 달성 수 across the whole mission. The extraction settlement is computed from
        /// this, so it counts objectives in areas that were purified <i>and</i> the one in
        /// progress - a player who completed an objective did complete it.
        /// </summary>
        public int ObjectivesCompletedInMission { get; private set; }

        /// <summary>Normal enemies killed this mission, for the pacing report.</summary>
        public int EnemiesKilledInMission { get; private set; }

        /// <summary>정예 killed this mission.</summary>
        public int ElitesKilledInMission { get; private set; }

        /// <summary>Areas purified in this mission, in the order they were.</summary>
        public IReadOnlyList<HardwareId> PurifiedAreas => _purified;

        /// <summary>Why the last refused call was refused. Empty after a call that succeeded.</summary>
        public string LastRefusal { get; private set; } = string.Empty;

        /// <summary>What the gate said about the last permanent transition it was asked about.</summary>
        public string LastCommitDetail { get; private set; } = string.Empty;

        /// <summary>Whether this area is already purified in this mission.</summary>
        public bool IsPurified(HardwareId area) => _purified.Contains(area);

        /// <summary>
        /// Whether 철수 is available: [확정] 1개 이상 정화, and standing somewhere it can be done
        /// from - the motherboard, or an area that has just been purified (docs/01 section 1
        /// offers 철수 또는 메인보드 귀환 at that point, and section 6 puts an extractionSpawn in
        /// the area).
        /// </summary>
        public bool CanExtract =>
            _purified.Count >= 1
            && (Phase == MissionFlowPhase.Motherboard
                || (Phase == MissionFlowPhase.InArea && AreaPhase == AreaProgressPhase.Purified));

        // --- 허브 → 메인보드 ----------------------------------------------------------------

        /// <summary>Begins a mission and steps onto the motherboard.</summary>
        public bool TryBeginMission(in RunIdentity identity, long onTick)
        {
            if (Phase != MissionFlowPhase.Hub)
                return Refuse($"a mission can only begin from the hub, not from {Phase}");
            if (!_run.TryBegin(identity, onTick))
                return Refuse($"the run state refused {identity} at tick {onTick}");

            _purified.Clear();
            ObjectivesCompletedInMission = 0;
            EnemiesKilledInMission = 0;
            ElitesKilledInMission = 0;
            ClearArea();
            Phase = MissionFlowPhase.Motherboard;
            return Accept();
        }

        // --- 메인보드 → 하드웨어 구역 --------------------------------------------------------

        /// <summary>
        /// Enters a hardware area. [확정] 입장 순서는 자유, so any area that has not been purified
        /// in this mission is a legal choice.
        /// </summary>
        /// <param name="objectivesRequired">
        /// 소목표 this area asks for. [초안] docs/01 gives 2~3; the number comes from whatever
        /// plans the area, and zero is refused because section 6.6 requires the boss to stay shut
        /// until the objectives are done.
        /// </param>
        public bool TryEnterArea(HardwareId area, int objectivesRequired)
        {
            if (Phase != MissionFlowPhase.Motherboard)
                return Refuse($"an area is entered from the motherboard, not from {Phase}");
            if (!HardwareIds.IsArea(area))
                return Refuse($"{area} is not one of the {DesignConstants.HardwareCount} hardware areas");
            if (IsPurified(area))
            {
                // 이미 정화한 하드웨어의 완료 상태는 런 동안 유지 + 중복 정화 금지.
                return Refuse($"{area} is already purified in this mission");
            }

            if (objectivesRequired < 1)
            {
                return Refuse(
                    $"{area} was planned with {objectivesRequired} objectives; with none, the boss and the " +
                    "extraction would open on entry (docs/01 section 6.6)");
            }

            _area = area;
            _objectivesInArea = 0;
            _objectivesRequired = objectivesRequired;
            AreaPhase = AreaProgressPhase.Objectives;
            Phase = MissionFlowPhase.InArea;
            return Accept();
        }

        // --- 구역 안 ------------------------------------------------------------------------

        /// <summary>
        /// Completes one 소목표. The boss opens on the last one.
        /// </summary>
        public bool TryCompleteObjective()
        {
            if (Phase != MissionFlowPhase.InArea)
                return Refuse($"there is no objective to complete from {Phase}");
            if (AreaPhase != AreaProgressPhase.Objectives)
                return Refuse($"{_area}'s objectives are already done ({AreaPhase})");

            _objectivesInArea++;
            ObjectivesCompletedInMission++;

            // 주요 목표 달성 40 XP (GDD v1 section 5.4). Awarded per cause rather than as a lump
            // on purification: the document gives the area a table of 지급 원인 that adds up to
            // its budget, so an area half-cleared is worth less than one cleared out, which is
            // the point of having a table.
            _run.TryAwardMissionXp(MissionXpBudget.XpForObjective(_purified.Count));

            if (_objectivesInArea >= _objectivesRequired)
                AreaPhase = AreaProgressPhase.Boss;

            return Accept();
        }

        /// <summary>
        /// One of the area's enemies died. Awards its 의뢰 XP and nothing else.
        /// <para>
        /// Not a <c>Try</c>: a kill has already happened and cannot be refused. What this can do
        /// is decline to <i>pay</i> for one - after a death, or outside an area - which is why it
        /// reports whether the XP was awarded.
        /// </para>
        /// </summary>
        /// <param name="elite">
        /// Whether it counted as a 정예. GDD v1 section 5.4 pays 32 for one and 8 for a normal.
        /// </param>
        public bool NotifyEnemyKilled(bool elite)
        {
            if (Phase != MissionFlowPhase.InArea)
                return Refuse($"an enemy died outside an area ({Phase}), so nothing was paid for it");

            long xp = elite
                ? MissionXpBudget.XpForEliteKill(_purified.Count)
                : MissionXpBudget.XpForNormalKill(_purified.Count);

            if (!_run.TryAwardMissionXp(xp))
                return Refuse("the mission is not running, so the kill paid nothing");

            if (elite)
                ElitesKilledInMission++;
            else
                EnemiesKilledInMission++;

            return Accept();
        }

        /// <summary>
        /// Puts the area's boss down, which opens 정화. Refused while any objective is
        /// outstanding - [확정] docs/01 section 6.6.
        /// </summary>
        public bool TryDefeatBoss()
        {
            if (Phase != MissionFlowPhase.InArea)
                return Refuse($"there is no boss to defeat from {Phase}");
            if (AreaPhase == AreaProgressPhase.Objectives)
            {
                return Refuse(
                    $"{_area} has {_objectivesRequired - _objectivesInArea} objective(s) outstanding, so its " +
                    "boss is not active (docs/01 section 6.6)");
            }

            if (AreaPhase != AreaProgressPhase.Boss)
                return Refuse($"{_area}'s boss is already down ({AreaPhase})");

            // 보스 처치 120 XP (GDD v1 section 5.4).
            _run.TryAwardMissionXp(MissionXpBudget.XpForBoss(_purified.Count));

            AreaPhase = AreaProgressPhase.Purification;
            return Accept();
        }

        /// <summary>
        /// 정화. Commits the purification to permanent state and only then counts it.
        /// <para>
        /// <b>No 의뢰 XP of its own.</b> GDD v1 section 5.4 breaks an area's budget into kills,
        /// objectives and the boss, and has no line for the purification - the XP is what the
        /// player did on the way here, and it has already been paid. A lump on top would double
        /// every area's budget.
        /// </para>
        /// </summary>
        public bool TryPurify()
        {
            if (Phase != MissionFlowPhase.InArea)
                return Refuse($"there is nothing to purify from {Phase}");
            if (AreaPhase == AreaProgressPhase.Objectives || AreaPhase == AreaProgressPhase.Boss)
                return Refuse($"{_area}'s boss is still standing, so 정화 is not active");
            if (AreaPhase != AreaProgressPhase.Purification)
                return Refuse($"{_area} is already purified");

            MissionCommitResult commit = _commits.CommitPurification(
                _run.Identity.RunId, HardwareIds.SaveKeyFor(_area));
            LastCommitDetail = commit.Detail;
            if (!commit.Committed)
            {
                // docs/04 section 7: 저장 실패 → 보상 미확정·상태 유지. The area stays at
                // Purification, so the player may try again without replaying the boss.
                return Refuse($"{_area}'s purification was not committed: {commit.Detail}");
            }

            _purified.Add(_area);
            _run.TryRecordPurification();
            AreaPhase = AreaProgressPhase.Purified;

            // After the commit and after the state, so a listener that opens a reward screen
            // is reading a purification that is already true.
            AreaPurified?.Invoke(_area);
            return Accept();
        }

        /// <summary>
        /// 메인보드 귀환. Only from a purified area - see the type's note on D-010.
        /// </summary>
        public bool TryReturnToMotherboard()
        {
            if (Phase != MissionFlowPhase.InArea)
                return Refuse($"there is no area to leave from {Phase}");
            if (AreaPhase != AreaProgressPhase.Purified)
            {
                return Refuse(
                    $"{_area} is not purified, and leaving an unpurified area is a 재방문 - docs/01 leaves " +
                    "같은 하드웨어 재방문 규칙 [미확정] (D-010), so it is refused rather than guessed");
            }

            ClearArea();
            Phase = MissionFlowPhase.Motherboard;
            return Accept();
        }

        // --- 철수 --------------------------------------------------------------------------

        /// <summary>
        /// 성공 철수. Commits the extraction and ends the mission a success.
        /// </summary>
        /// <param name="learnedInstanceIds">
        /// 0~2 일반·레어 instances to learn. [확정] R-015. Which plugins may be learned - 전설·
        /// 신화는 학습 불가 - is the caller's to check; neither this nor the save command holds
        /// what a plugin instance is.
        /// </param>
        public bool TryExtract(IReadOnlyList<string> learnedInstanceIds)
        {
            if (Phase != MissionFlowPhase.Motherboard && Phase != MissionFlowPhase.InArea)
                return Refuse($"there is no mission to extract from ({Phase})");
            if (Phase == MissionFlowPhase.InArea && AreaPhase != AreaProgressPhase.Purified)
                return Refuse($"{_area} must be purified before extracting from it");
            if (_purified.Count < 1)
            {
                // [확정] docs/04 section 7: 정화 0개 철수 → 성공 불가, 금지: 보상 지급.
                return Refuse("성공 철수는 최소 1개 정화를 요구한다 (docs/01 section 1, R-004)");
            }

            int learned = learnedInstanceIds?.Count ?? 0;
            if (learned > DesignConstants.LearnablePluginsPerSuccessfulRun)
            {
                return Refuse(
                    $"성공 철수 시 신규 학습은 최대 {DesignConstants.LearnablePluginsPerSuccessfulRun}개이다; " +
                    $"{learned} were offered");
            }

            MissionCommitResult commit = _commits.CommitExtraction(
                _run.Identity.RunId, _purified.Count, ObjectivesCompletedInMission, learnedInstanceIds);
            LastCommitDetail = commit.Detail;
            if (!commit.Committed)
            {
                // The mission stays exactly where it was. docs/04 section 7 again: a failed save
                // must leave the state alone, never show 보상 획득 완료 first.
                return Refuse($"the extraction was not committed: {commit.Detail}");
            }

            if (!_run.TryComplete())
            {
                // Unreachable: a running mission with a purification is what was just checked.
                // Kept because the alternative is a flow that says Extracted over a run state
                // that still says InProgress, and the next mission would then be refused.
                return Refuse("the run state refused to complete even though the extraction committed");
            }

            ClearArea();
            Phase = MissionFlowPhase.Extracted;
            MissionEnded?.Invoke();
            return Accept();
        }

        // --- 사망과 허브 복귀 ---------------------------------------------------------------

        /// <summary>
        /// [확정] 사망은 해당 의뢰 실패. One-way, and it commits nothing: what a death costs is
        /// <see cref="Progression.IDeathCommitService"/>'s to write, and nothing implements that.
        /// </summary>
        public bool TryFail()
        {
            if (Phase != MissionFlowPhase.Motherboard && Phase != MissionFlowPhase.InArea)
                return Refuse($"there is no running mission to fail ({Phase})");

            _run.TryFail();
            ClearArea();
            Phase = MissionFlowPhase.Failed;
            MissionEnded?.Invoke();
            return Accept();
        }

        /// <summary>
        /// Back to the hub, from either end of a mission. [확정] docs/04 section 2: 허브에서
        /// 재시도 after a death, and docs/01: 허브 복귀 after a success.
        /// </summary>
        public bool TryReturnToHub()
        {
            if (Phase != MissionFlowPhase.Extracted && Phase != MissionFlowPhase.Failed)
                return Refuse($"the hub is reached when a mission ends, not from {Phase}");

            ClearArea();
            Phase = MissionFlowPhase.Hub;
            return Accept();
        }

        public override string ToString() =>
            Phase == MissionFlowPhase.InArea
                ? $"{Phase}/{_area}/{AreaPhase} objectives={_objectivesInArea}/{_objectivesRequired} " +
                  $"purified={_purified.Count}"
                : $"{Phase} purified={_purified.Count} objectives={ObjectivesCompletedInMission}";

        private void ClearArea()
        {
            _area = HardwareId.None;
            _objectivesInArea = 0;
            _objectivesRequired = 0;
            AreaPhase = AreaProgressPhase.None;
        }

        private bool Accept()
        {
            LastRefusal = string.Empty;
            return true;
        }

        private bool Refuse(string reason)
        {
            LastRefusal = reason;
            return false;
        }
    }
}
