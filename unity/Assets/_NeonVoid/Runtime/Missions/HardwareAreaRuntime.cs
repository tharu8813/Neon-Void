using System.Collections.Generic;
using NeonVoid.Core;
using NeonVoid.Player;
using UnityEngine;

namespace NeonVoid.Missions
{
    /// <summary>
    /// One hardware area in the world: its objective devices, its boss, its 정화 콘솔 and its
    /// 철수 패드, joined to <see cref="MissionFlow"/>.
    /// <para>
    /// This is what turns the flow from call-driven into play-driven. The flow holds the [확정]
    /// rules and refuses what the documents forbid; this makes the room agree with them - the
    /// boss is inactive rather than merely uncounted, the console does nothing until the boss is
    /// down, the pad does nothing until something is purified.
    /// </para>
    /// <para>
    /// <b>The objective count comes from the devices that are actually here.</b> It is read off
    /// the array on entry rather than serialized beside it, because the number the flow requires
    /// and the number the player can find must be the same number. A serialized 3 in a room
    /// holding 2 devices is an area that can never be cleared, and nothing would say so until
    /// somebody played it.
    /// </para>
    /// <para>
    /// <b>One tick receiver, not four.</b> The parts are plain components the runtime drives, so
    /// the order in which the boss gate, the console and the pad see a tick is fixed here rather
    /// than depending on which object was enabled first - the same reason
    /// <see cref="ISimulationTickReceiver.TickOrder"/> is a number and not registration order.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HardwareAreaRuntime : MonoBehaviour, ISimulationTickReceiver
    {
        [Header("Which area")]
        [SerializeField] private HardwareId area = HardwareId.Cpu;

        [Tooltip("The mission this area belongs to. Found in the scene when left empty.")]
        [SerializeField] private MissionRunDirector mission;

        [Tooltip("Whose position the console and the pad measure. The player, found when empty.")]
        [SerializeField] private Transform player;

        [Header("Contents")]
        [Tooltip("The 소목표 devices. Their count is what the flow requires. [초안] docs/01: 2~3.")]
        [SerializeField] private AreaObjectiveDevice[] objectives = new AreaObjectiveDevice[0];

        [Tooltip("The area's boss, held inactive until the objectives are done.")]
        [SerializeField] private AreaBossGate bossGate;

        [Tooltip("One per room that holds enemies. Opened when the player walks in.")]
        [SerializeField] private AreaRoomGate[] roomGates = new AreaRoomGate[0];

        [Tooltip("Where 정화 is performed. Stood on once the boss is down.")]
        [SerializeField] private Transform purificationConsole;

        [Tooltip("Where 철수 is performed. Arms once something is purified.")]
        [SerializeField] private Transform extractionPad;

        [Header("[초안] interaction")]
        [Tooltip("[초안] how close counts as standing at the console.")]
        [SerializeField, Min(0.1f)] private float consoleRadiusUnits = 2f;

        [Tooltip("[초안] ticks held at the console. 60 is one second.")]
        [SerializeField, Min(1)] private int consoleDwellTicks = 60;

        [Tooltip("[초안] how close counts as standing on the extraction pad.")]
        [SerializeField, Min(0.1f)] private float padRadiusUnits = 2.5f;

        [Tooltip("[초안] ticks held on the pad before 철수 is offered. 90 is 1.5 seconds.")]
        [SerializeField, Min(1)] private int padDwellTicks = 90;

        [Header("Entry")]
        [Tooltip("Enter the area as soon as this object is enabled. A portal would do it instead.")]
        [SerializeField] private bool enterOnEnable = true;

        private SimulationRunner _runner;
        private DwellGate _console;
        private DwellGate _pad;
        private bool _entered;
        private bool _extractionArmed;

        /// <summary>Which hardware this area is.</summary>
        public HardwareId Area => area;

        /// <summary>The flow this area drives, or null before it is resolved.</summary>
        public MissionFlow Flow => mission != null ? mission.Flow : null;

        /// <summary>Whether <see cref="MissionFlow.TryEnterArea"/> accepted this area.</summary>
        public bool HasEntered => _entered;

        /// <summary>소목표 still outstanding in this room.</summary>
        public int ObjectivesOutstanding
        {
            get
            {
                int outstanding = 0;
                for (int i = 0; i < objectives.Length; ++i)
                {
                    if (objectives[i] != null && !objectives[i].IsCompleted)
                        ++outstanding;
                }

                return outstanding;
            }
        }

        /// <summary>
        /// Whether the player has stood on the pad long enough to be offered 철수.
        /// <para>
        /// Armed, not extracted. 정화 fires on its dwell because it is pure gain, while 철수 ends
        /// the mission and gives up the areas not yet cleared - so it waits for
        /// <see cref="TryConfirmExtraction"/>, which the UI will call. [초안], and the reason it
        /// is not symmetrical is written here rather than left to be rediscovered.
        /// </para>
        /// </summary>
        public bool IsExtractionArmed => _extractionArmed;

        /// <summary>The 정화 dwell, for a progress ring.</summary>
        public DwellGate PurificationDwell => _console;

        /// <summary>The 철수 dwell, for a progress ring.</summary>
        public DwellGate ExtractionDwell => _pad;

        /// <summary>
        /// Fills in what a scene would have serialized. The injection seam for a test and for
        /// anything that builds an area at runtime; call it while the object is still
        /// deactivated, so <see cref="Awake"/> sees the references rather than searching for them.
        /// </summary>
        public void Configure(
            HardwareId areaId,
            MissionRunDirector missionDirector,
            Transform playerTransform,
            AreaObjectiveDevice[] objectiveDevices,
            AreaBossGate boss,
            Transform console,
            Transform pad,
            AreaRoomGate[] rooms = null)
        {
            area = areaId;
            mission = missionDirector;
            player = playerTransform;
            objectives = objectiveDevices ?? new AreaObjectiveDevice[0];
            bossGate = boss;
            purificationConsole = console;
            extractionPad = pad;
            roomGates = rooms ?? new AreaRoomGate[0];
        }

        /// <summary>[초안] overrides the dwell shapes, for a test that does not want to wait.</summary>
        public void ConfigureDwell(float consoleRadius, int consoleTicks, float padRadius, int padTicks)
        {
            consoleRadiusUnits = consoleRadius;
            consoleDwellTicks = consoleTicks;
            padRadiusUnits = padRadius;
            padDwellTicks = padTicks;
            _console = new DwellGate(consoleRadiusUnits, consoleDwellTicks);
            _pad = new DwellGate(padRadiusUnits, padDwellTicks);
        }

        private void Awake()
        {
            _console = new DwellGate(consoleRadiusUnits, consoleDwellTicks);
            _pad = new DwellGate(padRadiusUnits, padDwellTicks);

            if (mission == null)
                mission = FindFirstObjectByType<MissionRunDirector>();
            if (player == null)
            {
                var driver = FindFirstObjectByType<PlayerSimulationDriver>();
                player = driver != null ? driver.transform : null;
            }
        }

        private void OnEnable()
        {
            _runner = _runner != null ? _runner : FindFirstObjectByType<SimulationRunner>();
            if (_runner == null)
            {
                Debug.LogError(
                    $"{name}: no SimulationRunner, so the console and the pad would never tick.", this);
                return;
            }

            _runner.Register(this);

            for (int i = 0; i < objectives.Length; ++i)
            {
                if (objectives[i] != null)
                    objectives[i].Completed += OnObjectiveCompleted;
            }

            if (bossGate != null)
                bossGate.Defeated += OnBossDefeated;

            for (int i = 0; i < roomGates.Length; ++i)
            {
                if (roomGates[i] != null)
                    roomGates[i].EnemyKilled += OnEnemyKilled;
            }

            if (enterOnEnable)
                TryEnter();
        }

        private void OnDisable()
        {
            if (_runner != null)
                _runner.Unregister(this);

            for (int i = 0; i < objectives.Length; ++i)
            {
                if (objectives[i] != null)
                    objectives[i].Completed -= OnObjectiveCompleted;
            }

            if (bossGate != null)
                bossGate.Defeated -= OnBossDefeated;

            for (int i = 0; i < roomGates.Length; ++i)
            {
                if (roomGates[i] != null)
                    roomGates[i].EnemyKilled -= OnEnemyKilled;
            }
        }

        /// <summary>
        /// Enters the area, with the number of objective devices the room holds.
        /// <para>
        /// Public so a motherboard portal can call it instead of entry happening on enable.
        /// </para>
        /// </summary>
        public bool TryEnter()
        {
            if (_entered)
                return true;

            MissionFlow flow = Flow;
            if (flow == null)
            {
                Debug.LogError($"{name}: no MissionRunDirector in the scene, so there is no mission to enter.", this);
                return false;
            }

            int required = CountPlaceableObjectives();
            if (required < 1)
            {
                Debug.LogError(
                    $"{name}: {area} holds no objective device, so its boss and its extraction would be " +
                    "open on entry (docs/01 section 6.6). Not entering.", this);
                return false;
            }

            if (bossGate == null)
            {
                Debug.LogError(
                    $"{name}: {area} has no boss gate. 정화 requires 목표/보스 완료 (docs/04 section 5), " +
                    "so the area could never be purified. Not entering.", this);
                return false;
            }

            if (!flow.TryEnterArea(area, required))
            {
                Debug.LogError($"{name}: {area} refused entry: {flow.LastRefusal}", this);
                return false;
            }

            _entered = true;
            bossGate.Close();
            _console.Reset();
            _pad.Reset();
            _extractionArmed = false;

            Debug.Log($"Entered {area} with {required} objective(s). Boss held until they are done.", this);
            return true;
        }

        /// <summary>
        /// 철수. Called once <see cref="IsExtractionArmed"/>, by whatever offers the choice.
        /// </summary>
        /// <param name="learnedInstanceIds">
        /// 0~2 일반·레어 instances to learn. [확정] R-015. Nothing chooses them yet, so the
        /// caller passes none.
        /// </param>
        public bool TryConfirmExtraction(IReadOnlyList<string> learnedInstanceIds)
        {
            MissionFlow flow = Flow;
            if (flow == null)
                return false;

            if (!_extractionArmed)
            {
                Debug.LogWarning(
                    $"{name}: 철수 was confirmed without the pad being armed; the player has not stood on it.",
                    this);
                return false;
            }

            if (flow.TryExtract(learnedInstanceIds))
            {
                Debug.Log($"철수 complete from {area}. {flow.LastCommitDetail}", this);
                return true;
            }

            // The pad stays armed: the refusal is almost always a save that would not take the
            // write, and docs/04 section 7 asks for the state to be kept so the player can try
            // again rather than replay the area.
            Debug.LogError($"{name}: 철수 refused: {flow.LastRefusal}", this);
            return false;
        }

        int ISimulationTickReceiver.TickOrder => SimulationRunner.Order.Effects + 2;

        void ISimulationTickReceiver.OnSimulationTick(long tickIndex)
        {
            MissionFlow flow = Flow;
            if (flow == null || !_entered)
                return;

            OpenTheRoomThePlayerIsIn();
            SyncBossGate(flow);
            TickPurificationConsole(flow);
            TickExtractionPad(flow);
        }

        /// <summary>
        /// Wakes the room the player has walked into, and no other.
        /// <para>
        /// Checked every tick rather than on a trigger volume: a trigger fires between ticks and
        /// a fight that began on a different tick than the one the simulation thinks it did is
        /// not reproducible from the seed. This reads the position the player's own tick put them
        /// at, which is deterministic.
        /// </para>
        /// </summary>
        private void OpenTheRoomThePlayerIsIn()
        {
            if (player == null)
                return;

            Vector3 position = player.position;
            for (int i = 0; i < roomGates.Length; ++i)
            {
                AreaRoomGate gate = roomGates[i];
                if (gate == null || gate.IsOpen)
                    continue;

                if (gate.Contains(position))
                    gate.Open();
            }
        }

        private void OnEnemyKilled(AreaRoomGate gate, bool elite)
        {
            MissionFlow flow = Flow;
            if (flow == null)
                return;

            if (!flow.NotifyEnemyKilled(elite))
                Debug.LogWarning($"{name}: a kill in {gate.name} paid nothing: {flow.LastRefusal}", this);
        }

        /// <summary>
        /// [확정] docs/01 section 6.6, made true of the room rather than only of the flow.
        /// </summary>
        private void SyncBossGate(MissionFlow flow)
        {
            if (bossGate == null || bossGate.IsDefeated)
                return;

            if (flow.AreaPhase == AreaProgressPhase.Boss)
                bossGate.Open();
            else if (flow.AreaPhase == AreaProgressPhase.Objectives)
                bossGate.Close();
        }

        private void TickPurificationConsole(MissionFlow flow)
        {
            if (purificationConsole == null || flow.AreaPhase != AreaProgressPhase.Purification)
            {
                _console.Reset();
                return;
            }

            if (!_console.Tick(DistanceToPlayer(purificationConsole)))
                return;

            if (flow.TryPurify())
            {
                Debug.Log($"정화 complete in {area}. {flow.LastCommitDetail}", this);
                return;
            }

            // The save would not take it. Emptying the dwell means the player stands there again
            // rather than the game asking the disk sixty times a second.
            _console.Reset();
            Debug.LogError($"{name}: 정화 refused: {flow.LastRefusal}", this);
        }

        private void TickExtractionPad(MissionFlow flow)
        {
            if (!flow.CanExtract)
            {
                // An area that stopped being extractable - a death, or an extraction that went
                // through - must not leave the pad armed behind it.
                _pad.Reset();
                _extractionArmed = false;
                return;
            }

            if (extractionPad == null)
                return;

            if (_pad.Tick(DistanceToPlayer(extractionPad)) && !_extractionArmed)
            {
                _extractionArmed = true;
                Debug.Log(
                    $"철수 available from {area}: {flow.PurifiedAreas.Count} area(s) purified. " +
                    "Waiting for the player to confirm.", this);
            }
        }

        /// <summary>
        /// Distance on the XZ plane. [확정] docs/01 section 1: 주요 이동은 평면 - a console two
        /// units away and three units below is not a console the player is standing at, but
        /// measuring height would make a pad unusable on a ramp.
        /// </summary>
        private float DistanceToPlayer(Transform target)
        {
            if (player == null || target == null)
                return float.NaN;

            Vector3 a = player.position;
            Vector3 b = target.position;
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt((dx * dx) + (dz * dz));
        }

        private int CountPlaceableObjectives()
        {
            int found = 0;
            for (int i = 0; i < objectives.Length; ++i)
            {
                if (objectives[i] != null)
                    ++found;
            }

            return found;
        }

        private void OnObjectiveCompleted(AreaObjectiveDevice device)
        {
            MissionFlow flow = Flow;
            if (flow == null)
                return;

            if (!flow.TryCompleteObjective())
            {
                Debug.LogError(
                    $"{name}: {device.ObjectiveName} was destroyed but the flow refused it: {flow.LastRefusal}",
                    this);
                return;
            }

            Debug.Log(
                $"{area}: {flow.ObjectivesCompletedInArea}/{flow.ObjectivesRequiredInArea} objectives. " +
                $"{ObjectivesOutstanding} device(s) left standing.", this);
        }

        private void OnBossDefeated(AreaBossGate gate)
        {
            MissionFlow flow = Flow;
            if (flow == null)
                return;

            if (!flow.TryDefeatBoss())
                Debug.LogError($"{name}: {gate.BossName} is down but the flow refused it: {flow.LastRefusal}", this);
        }

        /// <summary>One line for the debug overlay.</summary>
        public string StatusLine()
        {
            MissionFlow flow = Flow;
            if (flow == null)
                return $"{area}: no mission";

            string gate = bossGate != null ? bossGate.ToString() : "no boss";
            return $"{area} {flow.AreaPhase} · 목표 {flow.ObjectivesCompletedInArea}/{flow.ObjectivesRequiredInArea} · " +
                   $"{gate} · 정화 {_console.Progress:P0} · 철수 {(_extractionArmed ? "armed" : $"{_pad.Progress:P0}")}";
        }
    }
}
