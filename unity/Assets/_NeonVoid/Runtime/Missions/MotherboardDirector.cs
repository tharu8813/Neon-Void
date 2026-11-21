using System;
using System.Collections.Generic;
using NeonVoid.Core;
using NeonVoid.Missions.Generation;
using NeonVoid.Player;
using UnityEngine;

namespace NeonVoid.Missions
{
    /// <summary>
    /// 메인보드: the space between the hub and the hardware. Shows what the mission offers, lets
    /// the player walk into an area, and takes the area away again when they leave it.
    /// <para>
    /// This is the piece that makes the loop <b>walked</b> rather than called.
    /// <see cref="MissionFlow"/> has always been able to enter an area and come back; nothing in
    /// the world could do either, so a mission was a sequence of method calls in a test.
    /// </para>
    /// <para>
    /// <b>One scene, built and destroyed.</b> A hardware area is generated when the player walks
    /// into its portal and destroyed when they leave it, at an offset far from the motherboard.
    /// Additive scene loading would be the real answer and is its own piece of work; what matters
    /// for the loop is that entering and leaving are things that happen, that the area is built
    /// from a seed the mission already fixed, and that nothing of the previous area is still
    /// standing when the next one goes up.
    /// </para>
    /// <para>
    /// <b>Re-entering an area is refused, not rebuilt.</b> The flow already refuses a purified
    /// area (중복 정화 금지) and refuses leaving an unpurified one while D-010 is open, so there is
    /// no case where an area is entered twice - and <see cref="AreaSeeds"/> would rebuild it
    /// identically if there were.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-150)]
    public sealed class MotherboardDirector : MonoBehaviour, ISimulationTickReceiver
    {
        [Header("Who")]
        [Tooltip("The mission this motherboard belongs to. Found in the scene when left empty.")]
        [SerializeField] private MissionRunDirector mission;

        [Tooltip("Builds the areas. Found in the scene, or added here, when left empty.")]
        [SerializeField] private AreaWorldBuilder builder;

        [Tooltip("Whose position the portals measure. The player, found when empty.")]
        [SerializeField] private Transform player;

        [Header("Geometry [초안]")]
        [Tooltip("[초안] how far the five portals stand from the middle of the motherboard.")]
        [SerializeField, Min(2f)] private float portalRingRadiusUnits = 9f;

        [Tooltip("[초안] how close counts as standing in a portal.")]
        [SerializeField, Min(0.5f)] private float portalRadiusUnits = 2.5f;

        [Tooltip("[초안] ticks held in a portal before entering. 90 is 1.5 seconds.")]
        [SerializeField, Min(1)] private int portalDwellTicks = 90;

        [Tooltip(
            "Where an area is built, relative to the motherboard. Far enough that neither " +
            "floor stands in the other's way.")]
        [SerializeField] private Vector3 areaBuildOffset = new Vector3(500f, 0f, 500f);

        [Tooltip("Build the motherboard's own floor and portal markers. Off when a scene owns them.")]
        [SerializeField] private bool buildPrototypeFloor = true;

        private readonly Dictionary<HardwareId, Transform> _portals = new Dictionary<HardwareId, Transform>();
        private readonly Dictionary<HardwareId, DwellGate> _dwells = new Dictionary<HardwareId, DwellGate>();

        private SimulationRunner _runner;
        private GameObject _prototypeRoot;
        private AreaBuildResult _area;
        private Vector3 _motherboardStanding;
        private bool _boardTaken;
        private long _boardRunId;
        private MissionBoardPlan _board;

        /// <summary>Raised when an area has been built and entered.</summary>
        public event Action<AreaBuildResult> AreaEntered;

        /// <summary>Raised after an area has been taken away.</summary>
        public event Action<HardwareId> AreaLeft;

        /// <summary>
        /// What this mission offers. Fixed by the run's seed - and re-selected when the run
        /// changes, because a hub the player comes back to plays more than one mission and the
        /// second one's difficulties are its own.
        /// </summary>
        public MissionBoardPlan Board
        {
            get
            {
                if (EnsureBoard())
                    NamePortals();
                return _board;
            }
        }

        /// <summary>The area standing right now, or null.</summary>
        public AreaBuildResult CurrentArea => _area;

        /// <summary>The flow this motherboard drives, or null before it is resolved.</summary>
        public MissionFlow Flow => mission != null ? mission.Flow : null;

        /// <summary>The dwell at one hardware's portal, for a progress ring.</summary>
        public DwellGate PortalDwell(HardwareId hardware) =>
            _dwells.TryGetValue(hardware, out DwellGate gate) ? gate : null;

        /// <summary>Where a hardware's portal stands, or null when none was built.</summary>
        public Transform Portal(HardwareId hardware) =>
            _portals.TryGetValue(hardware, out Transform portal) ? portal : null;

        /// <summary>Fills in what a scene would have serialized. Call before this object wakes.</summary>
        public void Configure(MissionRunDirector missionDirector, AreaWorldBuilder areaBuilder, Transform playerTransform)
        {
            mission = missionDirector;
            builder = areaBuilder;
            player = playerTransform;
        }

        /// <summary>[초안] overrides the portal shape, for a test that does not want to wait.</summary>
        public void ConfigurePortals(float ringRadius, float radius, int dwellTicks)
        {
            portalRingRadiusUnits = ringRadius;
            portalRadiusUnits = radius;
            portalDwellTicks = dwellTicks;

            RebuildDwells();
            PlacePortals();
        }

        private void Awake()
        {
            if (mission == null)
                mission = FindFirstObjectByType<MissionRunDirector>();
            if (player == null)
            {
                var driver = FindFirstObjectByType<PlayerSimulationDriver>();
                player = driver != null ? driver.transform : null;
            }

            if (builder == null)
            {
                builder = FindFirstObjectByType<AreaWorldBuilder>();
                if (builder == null)
                    builder = gameObject.AddComponent<AreaWorldBuilder>();
            }

            _motherboardStanding = transform.position + (Vector3.up * AreaWorldBuilder.PlayerStandingLiftUnits);

            if (buildPrototypeFloor)
                BuildPrototypeMotherboard();

            RebuildDwells();
            PlacePortals();
        }

        private void OnEnable()
        {
            _runner = _runner != null ? _runner : FindFirstObjectByType<SimulationRunner>();
            if (_runner == null)
            {
                Debug.LogError($"{name}: no SimulationRunner, so the portals would never tick.", this);
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
            TakeTheAreaAway();
            if (_prototypeRoot != null)
                Destroy(_prototypeRoot);
        }

        int ISimulationTickReceiver.TickOrder => SimulationRunner.Order.Effects + 3;

        void ISimulationTickReceiver.OnSimulationTick(long tickIndex)
        {
            MissionFlow flow = Flow;
            if (flow == null || player == null)
                return;

            // An area that the flow has left - purified and returned from, extracted, or failed -
            // is scenery nobody is in. Taken away here rather than by whatever caused the
            // transition, so there is one place that knows an area is standing.
            if (_area != null && flow.Phase != MissionFlowPhase.InArea)
                TakeTheAreaAway();

            if (flow.Phase != MissionFlowPhase.Motherboard)
            {
                ResetDwells();
                return;
            }

            // The board belongs to the run, and a hub session plays several. Asked here rather
            // than only in Awake, where a scene with a hub has no mission yet and would have
            // fixed every mission's difficulties to the absent run's seed.
            if (EnsureBoard())
                NamePortals();

            TickPortals(flow);
        }

        /// <summary>
        /// Builds a hardware area and walks the player into it. Public so a terminal or a test
        /// can do it without the dwell.
        /// </summary>
        public bool TryEnter(HardwareId hardware)
        {
            MissionFlow flow = Flow;
            if (flow == null)
                return false;

            if (_area != null)
            {
                Debug.LogWarning($"{name}: {_area.Layout.Spec.Hardware} is still standing; not entering {hardware}.", this);
                return false;
            }

            if (flow.Phase != MissionFlowPhase.Motherboard)
            {
                Debug.LogWarning($"{name}: an area is entered from the motherboard, not from {flow.Phase}.", this);
                return false;
            }

            if (flow.IsPurified(hardware))
            {
                // 이미 정화한 하드웨어의 완료 상태는 런 동안 유지 + 중복 정화 금지.
                Debug.Log($"{name}: {hardware} is already purified in this mission.", this);
                return false;
            }

            uint missionSeed = flow.Run.SeedFor(RandomStream.Mission);
            AreaSpec spec = AreaSeeds.DraftSpecFor(missionSeed, hardware);

            if (!AreaGenerator.TryGenerate(spec, out AreaLayout layout, out string failure))
            {
                // docs/01 section 6 check 5 allows a replacement seed and the generator already
                // tried twelve. Refusing to open the portal is the only honest answer left:
                // building an unvalidated map would hand the player an area they cannot finish.
                Debug.LogError($"{name}: {hardware} could not be generated, so its portal stays shut. {failure}", this);
                return false;
            }

            builder.WorldOrigin = transform.position + areaBuildOffset;

            // The board's conditions for this hardware, which is what makes 변이발생 anything
            // more than a word on a portal.
            _area = builder.Build(layout, mission, player, _board.For(hardware));

            if (!_area.Runtime.HasEntered)
            {
                Debug.LogError($"{name}: {hardware} was built but the flow refused it: {flow.LastRefusal}", this);
                TakeTheAreaAway();
                return false;
            }

            ResetDwells();
            Debug.Log($"Entered {hardware}: {_board.For(hardware).GateLine()} · {layout.ReceiptLine()}", this);
            AreaEntered?.Invoke(_area);
            return true;
        }

        /// <summary>
        /// Destroys the area that is standing and puts the player back on the motherboard.
        /// Nothing of it is kept: the next area has to be the only one in the world.
        /// </summary>
        public void TakeTheAreaAway()
        {
            if (_area == null)
                return;

            HardwareId was = _area.Layout.Spec.Hardware;
            GameObject root = _area.Root;
            _area = null;

            if (root != null)
                Destroy(root);

            if (player != null)
                MovePlayer(_motherboardStanding);

            ResetDwells();
            Debug.Log($"Left {was}; back on the motherboard.", this);
            AreaLeft?.Invoke(was);
        }

        private void TickPortals(MissionFlow flow)
        {
            Vector3 at = player.position;

            for (int i = 0; i < HardwareIds.All.Length; ++i)
            {
                HardwareId hardware = HardwareIds.All[i];
                if (!_portals.TryGetValue(hardware, out Transform portal) || portal == null)
                    continue;
                if (!_dwells.TryGetValue(hardware, out DwellGate gate))
                    continue;

                // A purified area's portal is dead rather than refusing on arrival: the player
                // should not be able to stand in something for a second and a half to be told no.
                if (flow.IsPurified(hardware))
                {
                    gate.Reset();
                    continue;
                }

                float dx = at.x - portal.position.x;
                float dz = at.z - portal.position.z;
                if (!gate.Tick(Mathf.Sqrt((dx * dx) + (dz * dz))))
                    continue;

                if (!TryEnter(hardware))
                    gate.Reset();

                return;
            }
        }

        /// <summary>
        /// Selects the board for whichever run is current, once per run. Returns whether it
        /// changed, so the caller can relabel the portals.
        /// <para>
        /// Keyed on the run id rather than a one-shot flag: the first mission of a hub scene is
        /// begun after this component wakes, so a board taken in Awake would be the one for the
        /// absent run - 쉬움 보장 included - and every later mission would inherit it.
        /// </para>
        /// </summary>
        private bool EnsureBoard()
        {
            MissionFlow flow = Flow;
            long runId = flow != null ? flow.Run.Identity.RunId : 0L;
            if (_boardTaken && _boardRunId == runId)
                return false;

            uint seed = flow != null ? flow.Run.SeedFor(RandomStream.Mission) : 0u;
            _board = MissionBoard.Select(seed);
            _boardTaken = true;
            _boardRunId = runId;

            Debug.Log($"Mission board for run {runId}: {_board}", this);
            return true;
        }

        private void RebuildDwells()
        {
            _dwells.Clear();
            for (int i = 0; i < HardwareIds.All.Length; ++i)
                _dwells[HardwareIds.All[i]] = new DwellGate(portalRadiusUnits, portalDwellTicks);
        }

        private void ResetDwells()
        {
            foreach (KeyValuePair<HardwareId, DwellGate> pair in _dwells)
                pair.Value.Reset();
        }

        /// <summary>
        /// Puts the five portals in a ring. Positions rather than a scene layout, so the loop can
        /// be walked before anybody has built a motherboard - and so a test can reach one.
        /// </summary>
        private void PlacePortals()
        {
            EnsureBoard();

            for (int i = 0; i < HardwareIds.All.Length; ++i)
            {
                HardwareId hardware = HardwareIds.All[i];
                float angle = (i / (float)HardwareIds.All.Length) * Mathf.PI * 2f;
                Vector3 at = transform.position
                             + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * portalRingRadiusUnits;

                if (!_portals.TryGetValue(hardware, out Transform portal) || portal == null)
                {
                    var created = new GameObject($"Portal {hardware}");
                    created.transform.SetParent(_prototypeRoot != null ? _prototypeRoot.transform : transform, false);
                    portal = created.transform;
                    _portals[hardware] = portal;
                }

                portal.position = at;
            }

            NamePortals();
        }

        /// <summary>
        /// Puts this mission's 난이도·환경 on each portal's name, so the board is readable in the
        /// hierarchy before a panel exists to show it. Separate from placing them because the
        /// board changes between missions and the positions do not.
        /// </summary>
        private void NamePortals()
        {
            foreach (KeyValuePair<HardwareId, Transform> pair in _portals)
            {
                if (pair.Value != null)
                    pair.Value.name = $"Portal {_board.For(pair.Key).GateLine()}";
            }
        }

        /// <summary>
        /// A floor to stand on and a marker per portal. <b>Prototype</b>: docs/01 wants 메인보드 to
        /// be 5개 하드웨어 포털/연결 회로 with a 위험도/환경 패널, and this is a disc with five
        /// posts on it.
        /// </summary>
        private void BuildPrototypeMotherboard()
        {
            _prototypeRoot = new GameObject("Motherboard (Prototype)");
            _prototypeRoot.transform.SetParent(transform, worldPositionStays: false);

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Motherboard Floor";
            floor.transform.SetParent(_prototypeRoot.transform, worldPositionStays: false);
            floor.transform.position = transform.position + (Vector3.down * 0.25f);
            floor.transform.localScale = new Vector3(
                (portalRingRadiusUnits + portalRadiusUnits) * 2.5f,
                0.5f,
                (portalRingRadiusUnits + portalRadiusUnits) * 2.5f);
        }

        /// <summary>
        /// Stands the player on the motherboard. Called by the hub when a mission begins: the
        /// motherboard is where 의뢰 시작 puts them ([확정] docs/00 section 1's loop), and this
        /// component is the only thing that knows where its own middle is.
        /// </summary>
        public void PlacePlayerOnBoard()
        {
            if (player == null)
            {
                Debug.LogWarning($"{name}: no player to stand on the motherboard.", this);
                return;
            }

            MovePlayer(_motherboardStanding);
        }

        private void MovePlayer(Vector3 destination)
        {
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
                return "motherboard: no mission";

            string standing = _area != null ? _area.Layout.Spec.Hardware.ToString() : "none";
            return $"메인보드 · {flow.Phase} · area={standing} · purified={flow.PurifiedAreas.Count}";
        }
    }
}
