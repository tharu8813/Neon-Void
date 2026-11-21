using NeonVoid.Combat;
using NeonVoid.Combat.Skills;
using NeonVoid.Core;
using NeonVoid.Missions;
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

        [Header("[초안] labels")]
        [Tooltip("[초안] D-009 has not settled the keys; these are the prototype's Q/E/R.")]
        [SerializeField] private string[] skillKeys = { "Q", "E", "R" };

        private UIDocument _document;
        private SimulationRunner _runner;

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

        private VisualElement _areaPanel;
        private Label _areaName;
        private Label _areaObjectives;
        private VisualElement _purificationGauge;
        private ProgressBar _purificationBar;
        private VisualElement _extractionGauge;
        private ProgressBar _extractionBar;

        private bool _bound;

        private const string Hidden = "hidden";
        private const string SkillReady = "skill--ready";
        private const string SkillEmpty = "skill--empty";
        private const string GaugeArmed = "area__gauge--armed";

        /// <summary>The last snapshot drawn. For a test, and for the debug overlay.</summary>
        public HudSnapshot Last { get; private set; }

        int ISimulationTickReceiver.TickOrder => SimulationRunner.Order.Effects + 10;

        /// <summary>Names what the HUD reads. The injection seam for a test.</summary>
        public void Configure(
            CombatActor vaccine, MotherboardDirector board = null, HardwareAreaRuntime areaRuntime = null)
        {
            player = vaccine;
            motherboard = board;
            area = areaRuntime;
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

                if (_skillKeyLabels[i] != null && skillKeys != null && i < skillKeys.Length)
                    _skillKeyLabels[i].text = skillKeys[i];
            }

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
