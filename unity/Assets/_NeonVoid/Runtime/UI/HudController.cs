using System.Collections.Generic;
using NeonVoid.Combat;
using NeonVoid.Combat.Skills;
using NeonVoid.Core;
using NeonVoid.Missions;
using NeonVoid.Player;
using NeonVoid.Progression;
using NeonVoid.Progression.Save;
using UnityEngine;
using UnityEngine.UIElements;

namespace NeonVoid.UI
{
    /// <summary>
    /// Draws the combat HUD from the simulation, once per 60 Hz tick.
    /// <para>
    /// <b>On the logic tick, not in Update.</b> Everything shown here is simulation state, and
    /// a panel refreshed on the render frame would show a different number on a 144 Hz machine
    /// than on a 30 Hz one halfway through a tick. It costs nothing to be right: the tick is
    /// where the values change.
    /// </para>
    /// <para>
    /// Values are pushed straight onto the elements rather than bound. UI Toolkit's data
    /// binding is for state that changes predictably and is read in several places; this
    /// changes every tick, which is the case its own documentation says to set directly.
    /// </para>
    /// <para>
    /// <b>What it draws is only what is [확정].</b> Health, mana, 보호막, the three skill slots
    /// ([확정] docs/03 section 6), objective progress, the boss, 정화 and 철수 are all settled.
    /// The card-pick screen and the skill-equip screen are not drawn, because D-006/D-007 and
    /// D-009 have not been decided and a screen is how an undecided rule becomes real.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class HudController : MonoBehaviour, ISimulationTickReceiver
    {
        [Header("What it reads")]
        [Tooltip("The vaccine. Found by its PlayerSimulationDriver when left empty.")]
        [SerializeField] private CombatActor player;

        [Tooltip("The motherboard, which knows which area is standing. Found when left empty.")]
        [SerializeField] private MotherboardDirector motherboard;

        [Tooltip("An area runtime to read directly, for a scene with no motherboard.")]
        [SerializeField] private HardwareAreaRuntime area;

        [Tooltip("The 디펜더 허브, whose terminals the prompt names. Found when left empty.")]
        [SerializeField] private HubRuntime hub;

        [Tooltip(
            "The save, so the HUD can say whether this session is being written down. " +
            "Found when left empty.")]
        [SerializeField] private VaccineSaveDirector save;

        [Tooltip("The death ledger the 사망 화면 reads. Found when left empty.")]
        [SerializeField] private RunDeathDirector deaths;

        [Tooltip(
            "The key bindings, so the HUD prints the key the player actually has. " +
            "Found when left empty.")]
        [SerializeField] private InputBindingDirector bindings;

        [Header("Fallback labels")]
        [Tooltip(
            "Used only when no InputBindingDirector is present. The asset's own keys are the " +
            "real source; these exist so a scene without one still prints something.")]
        [SerializeField] private string[] skillKeys = { "Q", "E", "R" };

        [Tooltip("Fallback for 상호작용 when no InputBindingDirector is present.")]
        [SerializeField] private string interactKey = "F";

        private UIDocument _document;
        private SimulationRunner _runner;
        private PlayerInputRouter _router;

        private VisualElement _bossPanel;
        private Label _bossName;
        private ProgressBar _bossHealthBar;

        private VisualElement _shieldRow;
        private Label _shieldValue;
        private ProgressBar _healthBar;
        private ProgressBar _manaBar;

        private readonly VisualElement[] _skillSlots = new VisualElement[HudSnapshot.SkillSlots];
        private readonly ProgressBar[] _skillBars = new ProgressBar[HudSnapshot.SkillSlots];
        private readonly Label[] _skillKeyLabels = new Label[HudSnapshot.SkillSlots];

        private Label _saveNotice;

        private VisualElement _settingsScreen;
        private Label _settingsMessage;
        private Label _settingsConflicts;
        private ScrollView _settingsRows;
        private Button _settingsReset;
        private Button _settingsClose;
        private string _settingsSignature = string.Empty;

        private VisualElement _growthPanel;
        private Label _growthPoints;
        private Label _growthUnavailable;
        private Button _growthRedistribute;
        private ScrollView _growthRows;
        private Label _growthRefusal;
        private string _growthSignature = string.Empty;

        private VisualElement _deathScreen;
        private Label _deathTitle;
        private Label _deathStatus;
        private Label _deathDetail;
        private VisualElement _deathCounts;
        private Label _deathClones;
        private Label _deathRemaining;
        private VisualElement _deathUnsettled;
        private Label _deathNext;
        private Label _deathId;
        private string _deathSignature = string.Empty;

        private VisualElement _boardPanel;
        private VisualElement _boardRows;

        /// <summary>The rows currently drawn, so an unchanged board is not rebuilt every tick.</summary>
        private string _boardSignature = string.Empty;

        private VisualElement _promptPanel;
        private Label _promptSubject;
        private Label _promptDetail;
        private ProgressBar _promptBar;

        private VisualElement _areaPanel;
        private Label _areaName;
        private Label _areaObjectives;
        private VisualElement _purificationGauge;
        private ProgressBar _purificationBar;
        private VisualElement _extractionGauge;
        private ProgressBar _extractionBar;

        private bool _bound;

        private const string Hidden = "hidden";
        private const string SaveNoticeFailure = "save-notice--failure";
        private const string DeathPending = "death--pending";
        private const string DeathFailed = "death--failed";
        private const string DeathDestroyed = "death--destroyed";
        private const string SettingsMessageFailure = "settings__message--failure";
        private const string KeyCapChanged = "key-row__cap--changed";
        private const string KeyCapListening = "key-row__cap--listening";
        private const string SkillReady = "skill--ready";
        private const string SkillEmpty = "skill--empty";
        private const string GaugeArmed = "area__gauge--armed";
        private const string PromptUnavailable = "prompt--unavailable";
        private const string BoardRowDone = "board-row--done";

        /// <summary>The last snapshot drawn. For a test, and for the debug overlay.</summary>
        public HudSnapshot Last { get; private set; }

        /// <summary>What the player could act on when the HUD last drew. For a test.</summary>
        public HudPrompt Prompt { get; private set; }

        /// <summary>
        /// The 메인보드 as the HUD last read it, or empty when the player is not on it.
        /// docs/01 section 1: 현재 임무 위험도/환경 패널.
        /// </summary>
        public IReadOnlyList<HudBoardRow> Board => _board;

        /// <summary>
        /// What the HUD last said about whether this session is being saved. Public so it can
        /// be read without a panel to draw it on.
        /// </summary>
        public HudSaveNotice SaveNotice { get; private set; }

        /// <summary>
        /// The 사망/파기 화면 as the HUD last read it. Public so what it would say can be
        /// checked without a panel to draw it on.
        /// </summary>
        public DeathScreenSnapshot Death { get; private set; }

        /// <summary>
        /// The 백신 성장 터미널's screen as the HUD last read it. Public so what it would say
        /// can be checked without a panel to draw it on.
        /// </summary>
        public GrowthSheet Growth { get; private set; }

        /// <summary>Whether the 설정 screen is up. docs/05 section 2's 설정/오류 복구.</summary>
        public bool SettingsOpen { get; private set; }

        /// <summary>What the 설정 screen is waiting for, and what it last said.</summary>
        public KeyCapture Capture { get; private set; } = KeyCapture.Idle;

        private readonly List<HudBoardRow> _board = new List<HudBoardRow>();

        int ISimulationTickReceiver.TickOrder => SimulationRunner.Order.Effects + 10;

        /// <summary>Names what the HUD reads. The injection seam for a test.</summary>
        public void Configure(
            CombatActor vaccine,
            MotherboardDirector board = null,
            HardwareAreaRuntime areaRuntime = null,
            HubRuntime hubRuntime = null,
            VaccineSaveDirector saveDirector = null,
            RunDeathDirector deathDirector = null,
            InputBindingDirector bindingDirector = null)
        {
            player = vaccine;
            motherboard = board;
            area = areaRuntime;
            hub = hubRuntime;
            save = saveDirector;
            deaths = deathDirector;
            bindings = bindingDirector;
        }

        private void Awake()
        {
            _document = GetComponent<UIDocument>();

            if (player == null)
            {
                var driver = FindFirstObjectByType<Player.PlayerSimulationDriver>();
                player = driver != null ? driver.GetComponent<CombatActor>() : null;
            }

            if (motherboard == null)
                motherboard = FindFirstObjectByType<MotherboardDirector>();

            if (hub == null)
                hub = FindFirstObjectByType<HubRuntime>();

            if (save == null)
                save = FindFirstObjectByType<VaccineSaveDirector>();

            if (deaths == null)
                deaths = FindFirstObjectByType<RunDeathDirector>();

            if (bindings == null)
                bindings = FindFirstObjectByType<InputBindingDirector>();
        }

        private void OnEnable()
        {
            Bind();

            _runner = _runner != null ? _runner : FindFirstObjectByType<SimulationRunner>();
            if (_runner == null)
            {
                Debug.LogError($"{name}: no SimulationRunner, so the HUD would never update.", this);
                return;
            }

            _runner.Register(this);
        }

        private void OnDisable()
        {
            if (_runner != null)
                _runner.Unregister(this);
        }

        void ISimulationTickReceiver.OnSimulationTick(long tickIndex) => Redraw();

        /// <summary>Gathers the world into a snapshot and writes it to the panel.</summary>
        public void Redraw()
        {
            if (!Bind())
                return;

            HudSnapshot snapshot = Gather();
            Last = snapshot;
            Draw(snapshot);

            SaveNotice = GatherSaveNotice();
            DrawSaveNotice(SaveNotice);

            Death = GatherDeath();
            DrawDeath(Death);

            Growth = GatherGrowth();
            DrawGrowth(Growth);

            DrawSkillKeys();
            DrawSettings();

            HudPrompt prompt = GatherPrompt();
            Prompt = prompt;
            DrawPrompt(prompt);

            GatherBoard();
            DrawBoard();
        }

        /// <summary>
        /// Reads the world. Split from <see cref="Draw"/> so the numbers can be checked
        /// without a panel, and so a panel that never updated would still fail a test of the
        /// numbers rather than passing one.
        /// </summary>
        public HudSnapshot Gather()
        {
            float health = 0f, healthMax = 0f, mana = 0f, manaMax = 0f, shield = 0f;
            int shieldLayers = 0;
            var slots = new HudSkillSlot[HudSnapshot.SkillSlots];

            if (player != null)
            {
                health = player.Health.Current;
                healthMax = player.Health.Max;
                mana = player.Mana.Current;
                manaMax = player.Mana.Max;
                shield = player.Shields.Total;
                shieldLayers = player.Shields.LayerCount;

                SkillLoadout skills = player.Skills;
                for (int i = 0; i < slots.Length; ++i)
                {
                    if (skills == null || i >= skills.SlotCount)
                        continue;

                    SkillDefinition definition = skills.SlotAt(i);
                    bool equipped = definition.SkillId != 0;
                    slots[i] = new HudSkillSlot(
                        equipped, skills.CooldownRemaining(i), definition.BaseCooldownTicks);
                }
            }

            HardwareAreaRuntime standing = CurrentArea();
            if (standing == null)
            {
                return new HudSnapshot(
                    health, healthMax, mana, manaMax, shield, shieldLayers,
                    slots[0], slots[1], slots[2],
                    false, string.Empty, 0, 0,
                    false, string.Empty, 0f, 0f,
                    0f, 0f, false);
            }

            MissionFlow flow = standing.Flow;
            int done = flow != null ? flow.ObjectivesCompletedInArea : 0;
            int required = flow != null ? flow.ObjectivesRequiredInArea : 0;

            AreaBossGate gate = BossGateOf(standing);
            CombatActor boss = gate != null ? gate.Boss : null;
            bool bossUp = gate != null && gate.IsOpen && !gate.IsDefeated
                && boss != null && boss.IsAlive;

            return new HudSnapshot(
                health, healthMax, mana, manaMax, shield, shieldLayers,
                slots[0], slots[1], slots[2],
                true, standing.Area.ToString(), done, required,
                bossUp,
                gate != null ? gate.BossName : string.Empty,
                boss != null ? boss.Health.Current : 0f,
                boss != null ? boss.Health.Max : 0f,
                standing.PurificationDwell != null ? standing.PurificationDwell.Progress : 0f,
                standing.ExtractionDwell != null ? standing.ExtractionDwell.Progress : 0f,
                standing.IsExtractionArmed);
        }

        /// <summary>
        /// What the player could act on where they are standing.
        /// <para>
        /// Asked in the order the loop happens in - the thing you are doing now beats the
        /// thing you could do next - so walking onto the 철수 패드 while the 정화 콘솔 is
        /// still in reach reads as 철수 rather than flickering between the two.
        /// </para>
        /// </summary>
        public HudPrompt GatherPrompt()
        {
            HardwareAreaRuntime standing = CurrentArea();
            if (standing != null)
                return AreaPrompt(standing);

            HudPrompt portal = PortalPrompt();
            if (portal.HasSubject)
                return portal;

            return TerminalPrompt();
        }

        /// <summary>
        /// Reads the mission board while the player is standing on the motherboard, and empties
        /// it everywhere else. Public, like the other two gathers, so what the HUD reads can be
        /// checked without a panel to draw it on.
        /// <para>
        /// Only on the motherboard: the conditions belong to a mission that has begun, and a
        /// board on screen in the hub would be showing the player a roll for a mission they
        /// have not started.
        /// </para>
        /// </summary>
        public IReadOnlyList<HudBoardRow> GatherBoard()
        {
            _board.Clear();

            if (motherboard == null)
                return _board;

            MissionFlow flow = motherboard.Flow;
            if (flow == null || flow.Phase != MissionFlowPhase.Motherboard)
                return _board;

            MissionBoardPlan plan = motherboard.Board;
            IReadOnlyList<HardwareId> purified = flow.PurifiedAreas;

            for (int i = 0; i < plan.Count; ++i)
            {
                AreaConditions conditions = plan[i];

                bool done = false;
                for (int p = 0; p < purified.Count && !done; ++p)
                    done = purified[p] == conditions.Hardware;

                _board.Add(new HudBoardRow(conditions, done));
            }

            return _board;
        }

        /// <summary>
        /// Writes the board, rebuilding its rows only when it has actually changed.
        /// <para>
        /// A panel of five rows rebuilt sixty times a second would allocate five elements a
        /// tick for a list that changes when a mission begins and when an area is purified.
        /// </para>
        /// </summary>
        private void DrawBoard()
        {
            Show(_boardPanel, _board.Count > 0);
            if (_board.Count == 0)
            {
                _boardSignature = string.Empty;
                return;
            }

            string signature = BoardSignature();
            if (signature == _boardSignature)
                return;

            _boardSignature = signature;
            _boardRows.Clear();

            for (int i = 0; i < _board.Count; ++i)
            {
                HudBoardRow row = _board[i];

                var line = new VisualElement();
                line.AddToClassList("board-row");
                if (!row.IsOpen)
                    line.AddToClassList(BoardRowDone);

                var hardware = new Label(row.Hardware.ToString());
                hardware.AddToClassList("board-row__hardware");

                var conditions = new Label(
                    row.IsOpen ? row.ConditionLine : $"{row.ConditionLine} · 정화됨");
                conditions.AddToClassList("board-row__conditions");

                line.Add(hardware);
                line.Add(conditions);
                _boardRows.Add(line);
            }
        }

        private string BoardSignature()
        {
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < _board.Count; ++i)
                text.Append(_board[i]).Append('|');

            return text.ToString();
        }

        private HudPrompt AreaPrompt(HardwareAreaRuntime standing)
        {
            if (standing.IsExtractionArmed)
                return new HudPrompt("철수 패드", "철수 준비 완료", true);

            DwellGate pad = standing.ExtractionDwell;
            if (pad != null && pad.IsOccupied)
                return HudPrompt.Holding("철수 패드", "유지하여 철수", pad.Progress);

            DwellGate console = standing.PurificationDwell;
            if (console != null && console.IsOccupied)
                return HudPrompt.Holding("정화 콘솔", "유지하여 정화", console.Progress);

            return HudPrompt.None;
        }

        private HudPrompt PortalPrompt()
        {
            if (motherboard == null)
                return HudPrompt.None;

            for (int i = 0; i < HardwareIds.All.Length; ++i)
            {
                HardwareId hardware = HardwareIds.All[i];
                DwellGate gate = motherboard.PortalDwell(hardware);
                if (gate == null || !gate.IsOccupied)
                    continue;

                // What is behind the portal, where the choice is actually made. The board
                // panel says the same thing for all five; this says it for the one the player
                // is standing in.
                AreaConditions conditions = motherboard.Board.For(hardware);
                return HudPrompt.Holding(
                    $"{hardware} 포털", $"{conditions.Difficulty} · {Describe(conditions)}", gate.Progress);
            }

            return HudPrompt.None;
        }

        private static string Describe(in AreaConditions conditions) =>
            conditions.Environment == EnvironmentKind.Mutation
                ? $"{conditions.Environment} Lv.{conditions.MutationLevel}"
                : conditions.Environment.ToString();

        private HudPrompt TerminalPrompt()
        {
            if (hub == null)
                return HudPrompt.None;

            for (int i = 0; i < HubTerminals.All.Length; ++i)
            {
                HubTerminalKind kind = HubTerminals.All[i];
                if (!hub.IsInReach(kind))
                    continue;

                string name = HubTerminals.NameOf(kind);

                // A terminal with nothing behind it still gets a prompt. A pillar the player
                // walks up to and gets no answer from reads as a bug rather than as a thing
                // that is not finished - and docs/00 fixes the set of four.
                if (!HubTerminals.IsConnected(kind))
                    return HudPrompt.Unavailable(name, HubTerminals.PendingLabel(kind));

                return new HudPrompt(name, $"{KeyFor("Interact", interactKey)} 키로 사용", true);
            }

            return HudPrompt.None;
        }

        private void DrawPrompt(in HudPrompt prompt)
        {
            Show(_promptPanel, prompt.HasSubject);
            if (!prompt.HasSubject)
                return;

            _promptSubject.text = prompt.Subject;
            _promptDetail.text = prompt.Detail;
            Toggle(_promptPanel, PromptUnavailable, !prompt.Available);

            Show(_promptBar, prompt.IsDwell);
            if (prompt.IsDwell)
                _promptBar.value = prompt.Progress;
        }

        private HardwareAreaRuntime CurrentArea()
        {
            if (area != null)
                return area;

            AreaBuildResult built = motherboard != null ? motherboard.CurrentArea : null;
            return built != null ? built.Runtime : null;
        }

        private static AreaBossGate BossGateOf(HardwareAreaRuntime runtime) =>
            runtime != null ? runtime.GetComponentInParent<AreaBossGate>(true) : null;

        private void Draw(in HudSnapshot snapshot)
        {
            _healthBar.value = snapshot.HealthFraction;
            _healthBar.title = HudSnapshot.Pool(snapshot.Health, snapshot.HealthMax);

            _manaBar.value = snapshot.ManaFraction;
            _manaBar.title = HudSnapshot.Pool(snapshot.Mana, snapshot.ManaMax);

            Show(_shieldRow, snapshot.HasShield);
            if (snapshot.HasShield)
                _shieldValue.text = snapshot.ShieldLine;

            for (int i = 0; i < HudSnapshot.SkillSlots; ++i)
            {
                HudSkillSlot slot = snapshot.SkillAt(i);
                _skillBars[i].value = slot.ReadyFraction;
                _skillBars[i].title = slot.SecondsLeft > 0 ? slot.SecondsLeft.ToString() : string.Empty;

                Toggle(_skillSlots[i], SkillReady, slot.IsReady);
                Toggle(_skillSlots[i], SkillEmpty, !slot.IsEquipped);
            }

            Show(_bossPanel, snapshot.BossUp);
            if (snapshot.BossUp)
            {
                _bossName.text = snapshot.BossName;
                _bossHealthBar.value = snapshot.BossHealthFraction;
                _bossHealthBar.title = HudSnapshot.Pool(snapshot.BossHealth, snapshot.BossHealthMax);
            }

            Show(_areaPanel, snapshot.InArea);
            if (!snapshot.InArea)
                return;

            _areaName.text = snapshot.AreaName;
            _areaObjectives.text = snapshot.ObjectiveLine;

            // The gauges appear only while they can be filled, so an empty bar never sits on
            // screen implying the player should be standing somewhere they cannot yet.
            Show(_purificationGauge, snapshot.PurificationProgress > 0f);
            _purificationBar.value = snapshot.PurificationProgress;

            Show(_extractionGauge, snapshot.ExtractionProgress > 0f || snapshot.ExtractionArmed);
            _extractionBar.value = snapshot.ExtractionArmed ? 1f : snapshot.ExtractionProgress;
            Toggle(_extractionGauge, GaugeArmed, snapshot.ExtractionArmed);
        }

        /// <summary>
        /// Finds the elements once the document has a panel. Returns false until it has one -
        /// a UIDocument's root is null before the panel is attached, and an Awake that assumed
        /// otherwise would silently never draw.
        /// </summary>
        /// <summary>
        /// Opens or closes 설정. Combat input is suspended while it is open: docs/05 section 7
        /// asks for 리바인딩 as something a person does, and a key pressed to rebind it must
        /// not also dash.
        /// </summary>
        public void ToggleSettings()
        {
            if (SettingsOpen)
                CloseSettings();
            else
                OpenSettings();
        }

        public void OpenSettings()
        {
            SettingsOpen = true;
            Capture = KeyCapture.Idle;
            _settingsSignature = string.Empty;
            SuspendCombatInput(true);
        }

        public void CloseSettings()
        {
            SettingsOpen = false;
            Capture = KeyCapture.Idle;
            _settingsSignature = string.Empty;
            SuspendCombatInput(false);
        }

        /// <summary>Starts waiting for a key for one binding.</summary>
        public void BeginCapture(string bindingId)
        {
            if (bindings == null)
                return;

            KeyBindingSlot slot = bindings.Table.Find(bindingId);
            if (!slot.IsRebindable)
            {
                Capture = KeyCapture.Refused(
                    slot.Kind == KeyBindingKind.Pointer ? "조준은 키로 바꿀 수 없습니다" : "방향은 하나씩 바꿉니다");
                return;
            }

            Capture = KeyCapture.Listening(bindingId, slot.Label);
            _settingsSignature = string.Empty;
        }

        /// <summary>
        /// Takes one pressed control while the screen is waiting. Returns true when the press
        /// was consumed by the capture and must not reach anything else.
        /// <para>
        /// Public and taking a path rather than a device, so the rule - Escape cancels, a
        /// conflict refuses, anything else is applied - can be driven without a keyboard.
        /// </para>
        /// </summary>
        public bool OfferCapturedKey(string layoutPath)
        {
            if (!Capture.IsListening)
                return false;

            if (KeyCapturePath.IsCancel(layoutPath))
            {
                Capture = KeyCapture.Cancelled();
                _settingsSignature = string.Empty;
                return true;
            }

            string bindingId = Capture.BindingId;
            string label = Capture.Label;
            RebindResult result = bindings.Rebind(bindingId, layoutPath);

            Capture = result.Ok
                ? KeyCapture.Took(label, KeyBindings.DisplayKey(layoutPath))
                : KeyCapture.Refused(result.Detail);

            _settingsSignature = string.Empty;
            return true;
        }

        /// <summary>[확정] docs/05 section 2's 재설정.</summary>
        public void ResetAllKeys()
        {
            if (bindings == null)
                return;

            Capture = bindings.ResetAll()
                ? KeyCapture.Refused(string.Empty)
                : KeyCapture.Refused(bindings.LastRefusal);

            if (!Capture.HasMessage)
                Capture = KeyCapture.Took("전체", "기본값");

            _settingsSignature = string.Empty;
        }

        private void SuspendCombatInput(bool suspended)
        {
            if (_router == null)
                _router = FindFirstObjectByType<PlayerInputRouter>();

            if (_router != null)
                _router.CombatInputSuspended = suspended;
        }

        private void DrawSettings()
        {
            Show(_settingsScreen, SettingsOpen);
            if (_settingsScreen == null || !SettingsOpen)
                return;

            Show(_settingsMessage, Capture.HasMessage);
            SetText(_settingsMessage, Capture.Message);
            Toggle(_settingsMessage, SettingsMessageFailure, Capture.IsFailure);

            IReadOnlyList<InputBindingConflict> clashes =
                bindings != null ? bindings.Table.Conflicts() : System.Array.Empty<InputBindingConflict>();

            Show(_settingsConflicts, clashes.Count > 0);
            if (clashes.Count > 0)
                SetText(_settingsConflicts, $"키 중복 {clashes.Count}건: {clashes[0]}");

            string signature = Signature(clashes.Count);
            if (signature == _settingsSignature)
                return;

            _settingsSignature = signature;
            RebuildSettingsRows();
        }

        private string Signature(int conflicts)
        {
            if (bindings == null)
                return "none";

            var text = new System.Text.StringBuilder();
            text.Append(Capture.BindingId).Append('|').Append(conflicts).Append('|');

            IReadOnlyList<KeyBindingSlot> slots = bindings.Table.Slots;
            for (int i = 0; i < slots.Count; ++i)
                text.Append(slots[i].CurrentPath).Append(',');

            return text.ToString();
        }

        private void RebuildSettingsRows()
        {
            if (_settingsRows == null)
                return;

            _settingsRows.Clear();
            if (bindings == null)
                return;

            IReadOnlyList<KeyBindingSlot> slots = bindings.Table.Slots;
            for (int i = 0; i < slots.Count; ++i)
            {
                KeyBindingSlot slot = slots[i];

                // A composite's own row is a heading for the four under it, not a key.
                if (slot.Kind == KeyBindingKind.CompositeHeader)
                {
                    var group = new Label(slot.Label);
                    group.AddToClassList("key-group");
                    group.pickingMode = PickingMode.Ignore;
                    _settingsRows.Add(group);
                    continue;
                }

                var line = new VisualElement();
                line.AddToClassList("key-row");

                var name = new Label(slot.Label);
                name.AddToClassList("key-row__name");
                name.pickingMode = PickingMode.Ignore;
                line.Add(name);

                if (slot.Kind == KeyBindingKind.Pointer)
                {
                    // Listed and refused with a reason. [확정] docs/02 section 7: 마우스 조준.
                    var why = new Label($"{slot.DisplayKey} · 변경 불가");
                    why.AddToClassList("key-row__why");
                    why.pickingMode = PickingMode.Ignore;
                    line.Add(why);
                    _settingsRows.Add(line);
                    continue;
                }

                bool listening = Capture.IsListening && Capture.BindingId == slot.BindingId;
                string id = slot.BindingId;
                var cap = new Button(() => BeginCapture(id))
                {
                    text = listening ? "키를 누르세요" : slot.DisplayKey,
                };
                cap.AddToClassList("key-row__cap");
                Toggle(cap, KeyCapChanged, slot.IsOverridden);
                Toggle(cap, KeyCapListening, listening);
                line.Add(cap);

                _settingsRows.Add(line);
            }
        }

        /// <summary>
        /// The key cap to print for an action. The bindings are the source; the serialized
        /// string is only what a scene with no <see cref="InputBindingDirector"/> falls back
        /// to, because a HUD that printed Q while the player had moved 스킬 1 to Z would be
        /// teaching them the wrong key.
        /// </summary>
        public string KeyFor(string actionName, string fallback)
        {
            if (bindings == null)
                return fallback;

            string bound = bindings.DisplayKeyFor(actionName);
            return string.IsNullOrEmpty(bound) ? fallback : bound;
        }

        /// <summary>
        /// Whether this session is being written down. Read every tick rather than once,
        /// because a store that halts mid-session changes the answer and that is exactly the
        /// case the player most needs told.
        /// </summary>
        public HudSaveNotice GatherSaveNotice() =>
            save == null ? HudSaveNotice.None : HudSaveNotice.For(save.Readiness);

        private void DrawSaveNotice(HudSaveNotice notice)
        {
            Show(_saveNotice, notice.Shown);
            if (_saveNotice == null || !notice.Shown)
                return;

            _saveNotice.text = notice.Text;
            Toggle(_saveNotice, SaveNoticeFailure, notice.IsFailure);
        }

        /// <summary>
        /// The 백신 성장 터미널's screen. Open only while the player stands at the terminal in
        /// the hub: docs/00 section 6 makes the hub 실제 3D 공간, so this is what a terminal
        /// shows rather than a menu that can be opened anywhere.
        /// </summary>
        public GrowthSheet GatherGrowth()
        {
            if (hub == null || hub.Growth == null)
                return GrowthSheet.Closed;

            bool inTheHub = hub.Flow == null || hub.Flow.Phase == MissionFlowPhase.Hub;
            return GrowthSheet.From(hub.Growth, inTheHub && hub.IsInReach(HubTerminalKind.Growth));
        }

        private void DrawGrowth(in GrowthSheet sheet)
        {
            Show(_growthPanel, sheet.Shown);
            if (_growthPanel == null || !sheet.Shown)
            {
                _growthSignature = string.Empty;
                return;
            }

            SetText(_growthPoints, $"보유 포인트 {sheet.UnusedPoints}p · 투자 {sheet.PointsSpent}p");

            Show(_growthUnavailable, sheet.Unavailable.Length > 0);
            SetText(_growthUnavailable, sheet.Unavailable);

            Show(_growthRefusal, sheet.Refusal.Length > 0);
            SetText(_growthRefusal, sheet.Refusal);

            if (_growthRedistribute != null)
            {
                _growthRedistribute.text = $"무료 재분배 ({sheet.PointsSpent}p 회수)";
                _growthRedistribute.SetEnabled(sheet.CanInvest && sheet.PointsSpent > 0);
            }

            // The table is twenty-two rows and changes only when a purchase lands. Rebuilding
            // it sixty times a second would be work for nothing.
            string signature =
                $"{sheet.UnusedPoints}|{sheet.PointsSpent}|{sheet.CanInvest}|{sheet.Refusal}";
            if (signature == _growthSignature)
                return;

            _growthSignature = signature;
            RebuildGrowthRows(sheet);
        }

        private void RebuildGrowthRows(in GrowthSheet sheet)
        {
            if (_growthRows == null)
                return;

            _growthRows.Clear();

            for (int i = 0; i < sheet.Rows.Count; ++i)
            {
                GrowthSheetRow row = sheet.Rows[i];

                var line = new VisualElement();
                line.AddToClassList("growth-row");

                var left = new VisualElement();
                left.AddToClassList("growth-row__left");
                left.pickingMode = PickingMode.Ignore;

                var name = new Label(row.Steps > 0 ? $"{row.Name} x{row.Steps}" : row.Name);
                name.AddToClassList("growth-row__name");
                left.Add(name);

                if (row.Connected)
                {
                    left.Add(GrowthValues(row));
                }
                else
                {
                    // The reason goes under the name, across the row's full width. Squeezed
                    // into the right-hand column it wrapped to three lines of unreadable text,
                    // which is a worse answer than no answer.
                    var why = new Label(row.Unavailable);
                    why.AddToClassList("growth-row__reason");
                    why.pickingMode = PickingMode.Ignore;
                    left.Add(why);
                }

                line.Add(left);

                if (row.Affordable)
                {
                    InvestableStat stat = row.Stat;
                    var buy = new Button(() => Buy(stat)) { text = $"+1 · {row.Cost}p" };
                    buy.AddToClassList("growth-row__buy");
                    line.Add(buy);
                }
                else
                {
                    // Said, not hidden. A row the player cannot buy and is given no reason for
                    // reads as a broken button. The unconnected rows said their reason under
                    // the name already, so here they only need the word.
                    var state = new Label(row.Connected ? $"{row.Cost}p 부족" : "미연결");
                    state.AddToClassList("growth-row__why");
                    state.pickingMode = PickingMode.Ignore;
                    line.Add(state);
                }

                _growthRows.Add(line);
            }
        }

        /// <summary>
        /// 구매 전후 값, as two elements. [확정] docs/05 section 2 forbids 저장 전 값 확정 표시,
        /// so the preview is drawn beside the current value and never in place of it.
        /// </summary>
        private static VisualElement GrowthValues(in GrowthSheetRow row)
        {
            var values = new VisualElement();
            values.AddToClassList("growth-row__values");
            values.pickingMode = PickingMode.Ignore;

            var current = new Label(row.Current);
            current.AddToClassList("growth-row__current");
            values.Add(current);

            if (row.HasPreview)
            {
                var arrow = new Label("→");
                arrow.AddToClassList("growth-row__arrow");
                values.Add(arrow);

                var preview = new Label(row.Preview);
                preview.AddToClassList("growth-row__preview");
                values.Add(preview);
            }

            if (row.CurrentReduction.Length > 0)
            {
                // 실제 방어감소율: the number beside 방어력 that says what the point bought.
                string text = row.PreviewReduction.Length > 0
                    ? $"감소 {row.CurrentReduction} → {row.PreviewReduction}"
                    : $"감소 {row.CurrentReduction}";

                var reduction = new Label(text);
                reduction.AddToClassList("growth-row__reduction");
                values.Add(reduction);
            }

            return values;
        }

        /// <summary>
        /// Keeps the key caps on the skill slots current. Written only when the text changes:
        /// a rebind happens in a menu, a handful of times in a session at most.
        /// </summary>
        private void DrawSkillKeys()
        {
            for (int i = 0; i < HudSnapshot.SkillSlots; ++i)
            {
                if (_skillKeyLabels[i] == null)
                    continue;

                string fallback = skillKeys != null && i < skillKeys.Length ? skillKeys[i] : string.Empty;
                string text = KeyFor($"Skill{i + 1}", fallback);

                if (_skillKeyLabels[i].text != text)
                    _skillKeyLabels[i].text = text;
            }
        }

        private void Buy(InvestableStat stat)
        {
            if (hub != null && hub.Growth != null)
                hub.Growth.TryInvest(stat);
        }

        private void Redistribute()
        {
            if (hub != null && hub.Growth != null)
                hub.Growth.TryRedistribute();
        }

        /// <summary>
        /// The 사망 화면's reading, taken from the ledger and from what permanent state
        /// answered - never from the clone counter alone, because 복제 0 and a destroyed
        /// vaccine look identical in it.
        /// </summary>
        public DeathScreenSnapshot GatherDeath()
        {
            if (deaths == null)
                return DeathScreenSnapshot.None;

            RunDeathLedger ledger = deaths.Ledger;
            VaccineSaveState vaccine = save != null && save.Store != null
                ? save.Store.Snapshot.vaccine
                : null;

            return DeathScreenSnapshot.From(
                ledger.Phase,
                ledger.RegisteredDeathId,
                ledger.LastFailureReason,
                commitConnected: deaths.HasCommitService,
                vaccineAlive: vaccine == null || vaccine.alive,
                clonesRemaining: vaccine != null ? vaccine.copiesRemaining : 0,
                deathCount: vaccine != null ? vaccine.deathCount : 0);
        }

        private void DrawDeath(DeathScreenSnapshot death)
        {
            Show(_deathScreen, death.Shown);
            if (_deathScreen == null || !death.Shown)
            {
                _deathSignature = string.Empty;
                return;
            }

            Toggle(_deathScreen, DeathPending, death.Phase == DeathScreenPhase.AwaitingCommit);
            Toggle(_deathScreen, DeathFailed, death.Phase == DeathScreenPhase.CommitFailed);
            Toggle(_deathScreen, DeathDestroyed, death.Phase == DeathScreenPhase.Destroyed);

            // The whole screen changes only when the death does, which is at most four times in
            // a vaccine's life. Rewriting six labels every tick would be work for nothing.
            string signature = death.Phase + "|" + death.DeathIdText + "|" + death.Status;
            if (signature == _deathSignature)
                return;

            _deathSignature = signature;

            SetText(_deathTitle, death.Title);
            SetText(_deathStatus, death.Status);
            SetText(_deathDetail, death.Detail);
            SetText(_deathNext, death.NextStep);
            SetText(_deathId, death.DeathIdText);

            Show(_deathCounts, death.NumbersKnown);
            if (death.NumbersKnown)
            {
                SetText(_deathClones, $"복제 잔여 {death.ClonesRemaining}");
                SetText(_deathRemaining, $"파기까지 {death.DeathsLeft}회");
            }

            Show(_deathUnsettled, death.Unsettled.Count > 0);
            if (_deathUnsettled == null)
                return;

            _deathUnsettled.Clear();
            for (int i = 0; i < death.Unsettled.Count; ++i)
            {
                var line = new Label(death.Unsettled[i]);
                line.AddToClassList("death__unsettled-line");
                line.pickingMode = PickingMode.Ignore;
                _deathUnsettled.Add(line);
            }
        }

        private static void SetText(Label label, string text)
        {
            if (label != null)
                label.text = text;
        }

        private bool Bind()
        {
            if (_bound)
                return true;

            VisualElement root = _document != null ? _document.rootVisualElement : null;
            if (root == null)
                return false;

            _bossPanel = root.Q<VisualElement>("bossPanel");
            _bossName = root.Q<Label>("bossName");
            _bossHealthBar = root.Q<ProgressBar>("bossHealthBar");

            _shieldRow = root.Q<VisualElement>("shieldRow");
            _shieldValue = root.Q<Label>("shieldValue");
            _healthBar = root.Q<ProgressBar>("healthBar");
            _manaBar = root.Q<ProgressBar>("manaBar");

            for (int i = 0; i < HudSnapshot.SkillSlots; ++i)
            {
                _skillSlots[i] = root.Q<VisualElement>($"skillSlot{i}");
                _skillBars[i] = root.Q<ProgressBar>($"skillBar{i}");
                _skillKeyLabels[i] = root.Q<Label>($"skillKey{i}");

                if (_skillKeyLabels[i] == null)
                    continue;

                string fallback = skillKeys != null && i < skillKeys.Length ? skillKeys[i] : string.Empty;
                _skillKeyLabels[i].text = KeyFor($"Skill{i + 1}", fallback);
            }

            _saveNotice = root.Q<Label>("saveNotice");

            _settingsScreen = root.Q<VisualElement>("settingsScreen");
            _settingsMessage = root.Q<Label>("settingsMessage");
            _settingsConflicts = root.Q<Label>("settingsConflicts");
            _settingsRows = root.Q<ScrollView>("settingsRows");
            _settingsReset = root.Q<Button>("settingsReset");
            _settingsClose = root.Q<Button>("settingsClose");

            if (_settingsReset != null)
                _settingsReset.clicked += ResetAllKeys;
            if (_settingsClose != null)
                _settingsClose.clicked += CloseSettings;

            _growthPanel = root.Q<VisualElement>("growthPanel");
            _growthPoints = root.Q<Label>("growthPoints");
            _growthUnavailable = root.Q<Label>("growthUnavailable");
            _growthRedistribute = root.Q<Button>("growthRedistribute");
            _growthRows = root.Q<ScrollView>("growthRows");
            _growthRefusal = root.Q<Label>("growthRefusal");

            if (_growthRedistribute != null)
                _growthRedistribute.clicked += Redistribute;

            _deathScreen = root.Q<VisualElement>("deathScreen");
            _deathTitle = root.Q<Label>("deathTitle");
            _deathStatus = root.Q<Label>("deathStatus");
            _deathDetail = root.Q<Label>("deathDetail");
            _deathCounts = root.Q<VisualElement>("deathCounts");
            _deathClones = root.Q<Label>("deathClones");
            _deathRemaining = root.Q<Label>("deathRemaining");
            _deathUnsettled = root.Q<VisualElement>("deathUnsettled");
            _deathNext = root.Q<Label>("deathNext");
            _deathId = root.Q<Label>("deathId");

            _boardPanel = root.Q<VisualElement>("boardPanel");
            _boardRows = root.Q<VisualElement>("boardRows");

            _promptPanel = root.Q<VisualElement>("promptPanel");
            _promptSubject = root.Q<Label>("promptSubject");
            _promptDetail = root.Q<Label>("promptDetail");
            _promptBar = root.Q<ProgressBar>("promptBar");

            _areaPanel = root.Q<VisualElement>("areaPanel");
            _areaName = root.Q<Label>("areaName");
            _areaObjectives = root.Q<Label>("areaObjectives");
            _purificationGauge = root.Q<VisualElement>("purificationGauge");
            _purificationBar = root.Q<ProgressBar>("purificationBar");
            _extractionGauge = root.Q<VisualElement>("extractionGauge");
            _extractionBar = root.Q<ProgressBar>("extractionBar");

            if (_healthBar == null || _manaBar == null || _areaPanel == null)
            {
                Debug.LogError(
                    $"{name}: the HUD document is missing elements the controller writes to. " +
                    "Is NeonVoidHud.uxml assigned to the UIDocument?", this);
                return false;
            }

            _bound = true;
            return true;
        }

        private static void Show(VisualElement element, bool visible)
        {
            if (element == null)
                return;

            Toggle(element, Hidden, !visible);
        }

        private static void Toggle(VisualElement element, string className, bool on)
        {
            if (element == null)
                return;

            if (on)
                element.AddToClassList(className);
            else
                element.RemoveFromClassList(className);
        }
    }
}
