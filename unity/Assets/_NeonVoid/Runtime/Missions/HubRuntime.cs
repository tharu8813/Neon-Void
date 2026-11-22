using System;
using System.Collections.Generic;
using NeonVoid.Core;
using NeonVoid.Player;
using UnityEngine;

namespace NeonVoid.Missions
{
    /// <summary>
    /// 디펜더 허브: the space the player comes back to, and the only place a mission starts.
    /// <para>
    /// This is what closes the loop. <see cref="MissionFlow"/> has always had a
    /// <see cref="MissionFlowPhase.Hub"/> and a way back to it, but
    /// <see cref="MissionRunDirector"/> began a mission in <c>Awake</c> and nothing ever
    /// returned - so a session was one mission, and the extraction the save committed was the
    /// last thing that happened. [확정] docs/00 section 1: 허브에서 의뢰 시작 … 철수·사망 정산 →
    /// 허브. Both halves are here.
    /// </para>
    /// <para>
    /// <b>Each mission gets its own run id and its own seed.</b> Not this component's doing -
    /// <see cref="MissionRunDirector.TryBeginNextMission"/> and <see cref="MissionSeries"/> own
    /// it - but it is the reason a hub could not simply call the flow twice: [확정] docs/04
    /// section 5 forbids 같은 runId 중복 시작, and the second mission's first death would
    /// otherwise be refused as one already settled.
    /// </para>
    /// <para>
    /// <b>Prototype</b>: docs/00 section 6 wants the hub to be 실제 3D 공간 with terminals you
    /// walk to, reachable in 10~20초, and docs/05 gives each one a 2D overlay. What is here is a
    /// floor, four posts and 상호작용 - the geography and the state transitions, with no panels.
    /// Three of the four posts say what they are waiting for rather than doing nothing silently;
    /// see <see cref="HubTerminals.WhyNotConnected"/>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-140)]
    public sealed class HubRuntime : MonoBehaviour, ISimulationTickReceiver
    {
        [Header("Who")]
        [Tooltip("The mission this hub starts. Found in the scene when left empty.")]
        [SerializeField] private MissionRunDirector mission;

        [Tooltip("Where 의뢰 시작 sends the player. Found in the scene when left empty.")]
        [SerializeField] private MotherboardDirector motherboard;

        [Tooltip("Whose position the terminals measure. The player, found when empty.")]
        [SerializeField] private Transform player;

        [Tooltip("Reads 상호작용. Found in the scene when left empty; a hub without one is driven by code.")]
        [SerializeField] private PlayerInputRouter input;

        [Tooltip("영구 능력치 포인트 투자와 무료 재분배. Found in the scene when left empty.")]
        [SerializeField] private NeonVoid.Progression.VaccineGrowthDirector growth;

        [Header("Geometry [초안]")]
        [Tooltip("[초안] how far the four terminals stand from the middle of the hub.")]
        [SerializeField, Min(2f)] private float terminalRingRadiusUnits = 7f;

        [Tooltip("[초안] how close the player has to be to use a terminal.")]
        [SerializeField, Min(0.5f)] private float terminalReachUnits = 2.5f;

        [Tooltip("Build the hub's own floor and terminal posts. Off when a scene owns them.")]
        [SerializeField] private bool buildPrototypeFloor = true;

        private readonly Dictionary<HubTerminalKind, Transform> _terminals =
            new Dictionary<HubTerminalKind, Transform>();

        private readonly HashSet<HubTerminalKind> _explained = new HashSet<HubTerminalKind>();

        private SimulationRunner _runner;
        private GameObject _prototypeRoot;
        private Vector3 _hubStanding;
        private bool _interactRequested;

        /// <summary>Raised when a mission has begun from the 의뢰 터미널.</summary>
        public event Action<RunIdentity> MissionBegun;

        /// <summary>Raised when a mission has ended and the player is back in the hub.</summary>
        public event Action<MissionPhase> ReturnedToHub;

        /// <summary>The flow this hub drives, or null before it is resolved.</summary>
        public MissionFlow Flow => mission != null ? mission.Flow : null;

        /// <summary>
        /// 백신 성장, or null in a scene without one. Exposed so a panel can offer the table
        /// without going looking for it, and so the reach check stays this component's job.
        /// </summary>
        public NeonVoid.Progression.VaccineGrowthDirector Growth => growth;

        /// <summary>Where a terminal stands, or null when none was built.</summary>
        public Transform Terminal(HubTerminalKind kind) =>
            _terminals.TryGetValue(kind, out Transform terminal) ? terminal : null;

        /// <summary>Whether the player is close enough to use this terminal right now.</summary>
        public bool IsInReach(HubTerminalKind kind)
        {
            Transform terminal = Terminal(kind);
            if (terminal == null || player == null)
                return false;

            float dx = player.position.x - terminal.position.x;
            float dz = player.position.z - terminal.position.z;
            return (dx * dx) + (dz * dz) <= terminalReachUnits * terminalReachUnits;
        }

        /// <summary>Fills in what a scene would have serialized. Call before this object wakes.</summary>
        public void Configure(
            MissionRunDirector missionDirector, MotherboardDirector board, Transform playerTransform)
        {
            mission = missionDirector;
            motherboard = board;
            player = playerTransform;
        }

        /// <summary>[초안] overrides the hub's shape, for a test or a scene with its own layout.</summary>
        public void ConfigureGeometry(float ringRadius, float reach)
        {
            terminalRingRadiusUnits = ringRadius;
            terminalReachUnits = reach;
            PlaceTerminals();
        }

        /// <summary>
        /// 상호작용, from something other than the device: a terminal panel's button, or a test.
        /// <para>
        /// Latched rather than acted on immediately, and consumed by the next tick, so a press
        /// from a UI frame and a press from the input router reach the hub the same way - and so
        /// a terminal cannot fire in the middle of someone else's tick.
        /// </para>
        /// </summary>
        public void RequestInteract() => _interactRequested = true;

        private void Awake()
        {
            if (mission == null)
                mission = FindFirstObjectByType<MissionRunDirector>();
            if (motherboard == null)
                motherboard = FindFirstObjectByType<MotherboardDirector>();
            if (input == null)
                input = FindFirstObjectByType<PlayerInputRouter>();
            if (growth == null)
                growth = FindFirstObjectByType<NeonVoid.Progression.VaccineGrowthDirector>();
            if (player == null)
            {
                var driver = FindFirstObjectByType<PlayerSimulationDriver>();
                player = driver != null ? driver.transform : null;
            }

            _hubStanding = transform.position + (Vector3.up * AreaWorldBuilder.PlayerStandingLiftUnits);

            if (buildPrototypeFloor)
                BuildPrototypeHub();

            PlaceTerminals();
        }

        private void OnEnable()
        {
            _runner = _runner != null ? _runner : FindFirstObjectByType<SimulationRunner>();
            if (_runner == null)
            {
                Debug.LogError($"{name}: no SimulationRunner, so the terminals would never tick.", this);
                return;
            }

            _runner.Register(this);
        }

        private void OnDisable()
        {
            if (_runner != null)
                _runner.Unregister(this);
        }

        private void OnDestroy()
        {
            if (_prototypeRoot != null)
                Destroy(_prototypeRoot);
        }

        /// <summary>
        /// After the motherboard (Effects+3), so that on the tick a mission ends the area has
        /// already been taken away and the player put back on the board before this moves them
        /// into the hub. The other order would leave them standing in the hub with an area still
        /// built around where they used to be.
        /// </summary>
        int ISimulationTickReceiver.TickOrder => SimulationRunner.Order.Effects + 4;

        void ISimulationTickReceiver.OnSimulationTick(long tickIndex)
        {
            bool interacted = TakeInteract();

            MissionFlow flow = Flow;
            if (flow == null)
                return;

            // 철수·사망 정산 → 허브. Done here rather than by whatever ended the mission, so
            // there is one place that knows the player is standing in the hub.
            if (flow.Phase == MissionFlowPhase.Extracted || flow.Phase == MissionFlowPhase.Failed)
            {
                BringThePlayerHome(flow);
                return;
            }

            if (flow.Phase != MissionFlowPhase.Hub || !interacted)
                return;

            UseWhateverIsInReach();
        }

        /// <summary>
        /// Uses a terminal, without the reach check - for a panel the player already opened, or
        /// a test that does not want to walk.
        /// </summary>
        /// <summary>
        /// Raised when the player uses the 스킬 장착 터미널. [확정] D-009 (2026-11-22): 교체는
        /// 허브에서만, so the hub is what opens the screen and the HUD only draws it.
        /// </summary>
        public event System.Action SkillTerminalUsed;

        /// <summary>
        /// Raised when 상호작용 happens at the 플러그인 데이터베이스. The hub does not own the
        /// screen - it owns the place in the world - so what opens is whatever is listening.
        /// </summary>
        public event System.Action DatabaseTerminalUsed;

        public bool TryUseTerminal(HubTerminalKind kind)
        {
            if (!HubTerminals.IsConnected(kind))
            {
                Explain(kind);
                return false;
            }

            MissionFlow flow = Flow;
            if (flow == null)
            {
                Debug.LogError($"{name}: no mission director, so {HubTerminals.NameOf(kind)} does nothing.", this);
                return false;
            }

            if (flow.Phase != MissionFlowPhase.Hub)
            {
                Debug.Log(
                    $"{name}: {HubTerminals.NameOf(kind)} is only reachable from the hub, not from " +
                    $"{flow.Phase}.", this);
                return false;
            }

            switch (kind)
            {
                case HubTerminalKind.Mission:
                    return BeginMission();

                case HubTerminalKind.Growth:
                    return ReadTheGrowthSheet();

                case HubTerminalKind.Database:
                    DatabaseTerminalUsed?.Invoke();
                    return true;

                case HubTerminalKind.Skills:
                    SkillTerminalUsed?.Invoke();
                    return true;

                default:
                    // Unreachable while Mission is the only connected kind; here so that
                    // connecting another one is a compile-time decision rather than a silent no.
                    Explain(kind);
                    return false;
            }
        }

        private bool BeginMission()
        {
            if (mission == null || !mission.TryBeginNextMission())
                return false;

            // 의뢰 시작 → 메인보드 도착 ([확정] docs/00 section 1). The flow is already on the
            // motherboard; this is the player catching up with it.
            if (motherboard != null)
            {
                motherboard.PlacePlayerOnBoard();
            }
            else
            {
                Debug.LogWarning(
                    $"{name}: the mission began but there is no motherboard in the scene, so the player " +
                    "is standing in the hub while the flow says 메인보드.", this);
            }

            RunIdentity identity = mission.Identity;
            Debug.Log($"의뢰 시작: {identity} (mission {mission.MissionsBegun} of this session).", this);
            MissionBegun?.Invoke(identity);
            return true;
        }

        /// <summary>
        /// What 상호작용 at the 백신 성장 터미널 does: says where the vaccine's points stand.
        /// <para>
        /// <b>It does not choose a stat.</b> docs/05 gives 수치 비교 to a 2D 오버레이 and the
        /// table has twenty-two rows; a keypress cannot pick one. Spending goes through
        /// <see cref="Growth"/>, which the panel calls - this is the part of the terminal that
        /// is a place in the world rather than a screen.
        /// </para>
        /// </summary>
        private bool ReadTheGrowthSheet()
        {
            if (growth == null)
            {
                Debug.LogWarning(
                    $"{name}: {HubTerminals.NameOf(HubTerminalKind.Growth)} has no " +
                    "VaccineGrowthDirector in the scene, so there is nothing to invest with.", this);
                return false;
            }

            Debug.Log(growth.Sheet(), this);
            return true;
        }

        private void BringThePlayerHome(MissionFlow flow)
        {
            MissionPhase ended = flow.Run.Phase;
            string outcome = flow.Phase == MissionFlowPhase.Extracted
                ? $"철수 성공 · 정화 {flow.PurifiedAreas.Count}곳 · 주요 목표 {flow.ObjectivesCompletedInMission}개"
                : "의뢰 실패";

            if (!flow.TryReturnToHub())
            {
                Debug.LogError($"{name}: the hub refused the return: {flow.LastRefusal}", this);
                return;
            }

            MovePlayer(_hubStanding);
            _explained.Clear();

            Debug.Log($"허브 복귀: {outcome}.", this);
            ReturnedToHub?.Invoke(ended);
        }

        private void UseWhateverIsInReach()
        {
            for (int i = 0; i < HubTerminals.All.Length; ++i)
            {
                HubTerminalKind kind = HubTerminals.All[i];
                if (!IsInReach(kind))
                    continue;

                TryUseTerminal(kind);
                return;
            }
        }

        /// <summary>
        /// Takes this tick's 상호작용 from either source. The router's command is read rather
        /// than consumed - <see cref="PlayerInputRouter.LastConsumedCommand"/> documents why -
        /// and the input slot ticks long before this one, so the press has already been latched
        /// into it.
        /// </summary>
        private bool TakeInteract()
        {
            bool requested = _interactRequested;
            _interactRequested = false;

            bool pressed = input != null && input.LastConsumedCommand.InteractPressed;
            return requested || pressed;
        }

        private void Explain(HubTerminalKind kind)
        {
            if (!_explained.Add(kind))
                return;

            // Said once per visit, not once per tick: the player holding 상호작용 at an
            // unfinished terminal should not fill the console.
            Debug.Log($"{HubTerminals.NameOf(kind)}: {HubTerminals.WhyNotConnected(kind)}.", this);
        }

        /// <summary>
        /// Puts the four terminals in a ring. Positions rather than a scene layout, so the loop
        /// can be walked before anybody has built a hub - and so a test can reach one.
        /// </summary>
        private void PlaceTerminals()
        {
            for (int i = 0; i < HubTerminals.All.Length; ++i)
            {
                HubTerminalKind kind = HubTerminals.All[i];
                float angle = (i / (float)HubTerminals.All.Length) * Mathf.PI * 2f;
                Vector3 at = transform.position
                             + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * terminalRingRadiusUnits;

                if (!_terminals.TryGetValue(kind, out Transform terminal) || terminal == null)
                {
                    var created = new GameObject(HubTerminals.NameOf(kind));
                    created.transform.SetParent(
                        _prototypeRoot != null ? _prototypeRoot.transform : transform, false);
                    terminal = created.transform;
                    _terminals[kind] = terminal;
                }

                terminal.position = at;
                terminal.name = HubTerminals.IsConnected(kind)
                    ? HubTerminals.NameOf(kind)
                    : $"{HubTerminals.NameOf(kind)} (미연결)";
            }
        }

        /// <summary>
        /// A floor to stand on and a post per terminal. <b>Prototype</b>: see this type's note.
        /// </summary>
        private void BuildPrototypeHub()
        {
            _prototypeRoot = new GameObject("Defender Hub (Prototype)");
            _prototypeRoot.transform.SetParent(transform, worldPositionStays: false);

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Hub Floor";
            floor.transform.SetParent(_prototypeRoot.transform, worldPositionStays: false);
            floor.transform.position = transform.position + (Vector3.down * 0.25f);
            floor.transform.localScale = new Vector3(
                (terminalRingRadiusUnits + terminalReachUnits) * 2.5f,
                0.5f,
                (terminalRingRadiusUnits + terminalReachUnits) * 2.5f);
        }

        private void MovePlayer(Vector3 destination)
        {
            if (player == null)
                return;

            var driver = player.GetComponent<PlayerSimulationDriver>();
            if (driver == null)
            {
                player.position = destination;
                return;
            }

            // The rules keep their own position ledger; see AreaWorldBuilder's note on why
            // writing the transform alone puts the player back where they were.
            player.position = new Vector3(player.position.x, destination.y, player.position.z);
            driver.TeleportTo(new Vector2(destination.x, destination.z));
        }

        /// <summary>One line for the debug overlay.</summary>
        public string StatusLine()
        {
            MissionFlow flow = Flow;
            if (flow == null)
                return "허브: no mission director";

            int missions = mission != null ? mission.MissionsBegun : 0;
            return $"허브 · {flow.Phase} · 의뢰 {missions}회 시작";
        }
    }
}
