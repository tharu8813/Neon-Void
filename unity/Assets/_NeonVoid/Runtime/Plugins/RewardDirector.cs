using NeonVoid.Missions;
using UnityEngine;

namespace NeonVoid.Plugins
{
    /// <summary>
    /// Opens a 보상 화면 when an area is purified, and hands the picked card to the loadout.
    /// <para>
    /// [확정] docs/01: a reward follows a 정화. The flow raises
    /// <see cref="MissionFlow.AreaPurified"/> after the purification is committed, so a screen
    /// is never shown for one that was not written.
    /// </para>
    /// <para>
    /// <b>Nothing here touches permanent state.</b> [확정] docs/04 section 1 puts 임시 플러그인
    /// in 의뢰 상태 and section 3 item 5 loses them on death, so a card taken mid-mission lives
    /// in <see cref="PluginLoadout"/> and becomes permanent only when 철수 commits it.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-150)]
    public sealed class RewardDirector : MonoBehaviour
    {
        [Tooltip("The mission whose 정화 opens a screen. Found when left empty.")]
        [SerializeField] private MissionRunDirector missionDirector;

        [Tooltip("The motherboard, for the area's 난이도. Found when left empty.")]
        [SerializeField] private MotherboardDirector motherboard;

        [Tooltip("Whose loadout the picked card goes to. Found when left empty.")]
        [SerializeField] private PluginProcDirector procs;

        [Tooltip(
            "[미확정] 행운 is an investable stat that nothing reads yet (D-011), so the roll " +
            "uses this until it is connected.")]
        [SerializeField] private int draftLuck;

        private MissionFlow _flow;
        private int _missionsSeen = -1;

        /// <summary>The screens this mission has shown and what they offered.</summary>
        public RewardSession Session { get; private set; }

        /// <summary>Fills in what a scene would have serialized.</summary>
        public void Configure(
            MissionRunDirector mission, MotherboardDirector board, PluginProcDirector procDirector)
        {
            missionDirector = mission;
            motherboard = board;
            procs = procDirector;
        }

        private void Awake()
        {
            if (missionDirector == null)
                missionDirector = FindFirstObjectByType<MissionRunDirector>();

            if (motherboard == null)
                motherboard = FindFirstObjectByType<MotherboardDirector>();

            if (procs == null)
                procs = FindFirstObjectByType<PluginProcDirector>();

            // A function, not the loadout: PluginProcDirector builds its router in its own
            // Awake and this component runs first, so reading it here captured null and every
            // pick then granted nothing while the screen closed as if it had worked.
            Session = RewardSession.Approved(() => procs != null ? procs.Loadout : null);
        }

        private void OnEnable()
        {
            _flow = missionDirector != null ? missionDirector.Flow : null;
            if (_flow == null)
            {
                Debug.LogWarning($"{name}: no mission flow, so no 보상 화면 will ever open.", this);
                return;
            }

            _flow.AreaPurified += OnAreaPurified;
            _flow.MissionEnded += OnMissionEnded;
        }

        private void OnDisable()
        {
            if (_flow == null)
                return;

            _flow.AreaPurified -= OnAreaPurified;
            _flow.MissionEnded -= OnMissionEnded;
        }

        private void Update()
        {
            if (missionDirector == null || Session == null)
                return;

            // The rerolls refill per 의뢰. Watched rather than pushed because the flow has no
            // "a mission began" event and the director counts them already.
            if (_missionsSeen == missionDirector.MissionsBegun)
                return;

            _missionsSeen = missionDirector.MissionsBegun;
            Session.BeginMission();
        }

        private void OnAreaPurified(HardwareId hardware)
        {
            if (Session == null || missionDirector == null)
                return;

            // The purification that just happened is already counted, so this screen is dealt
            // at the progress the player actually has.
            int purified = _flow != null ? _flow.PurifiedCount : 0;

            var conditions = new RewardConditions(
                DifficultyOf(hardware), purified, Mathf.Max(0, draftLuck));

            RewardScreen screen = Session.Open(
                missionDirector.Identity.RunId, conditions, missionDirector.Identity.Seed);

            Debug.Log($"보상 화면: {hardware} 정화 · {conditions} · {screen}", this);
        }

        private void OnMissionEnded()
        {
            // A mission that ended takes its screen with it. A card picked from a screen that
            // outlived its mission would be a reward for a run that is over.
            Session?.Dismiss();
        }

        private RewardDifficulty DifficultyOf(HardwareId hardware)
        {
            if (motherboard == null)
                return RewardDifficulty.Normal;

            switch (motherboard.Board.For(hardware).Difficulty)
            {
                case HardwareDifficulty.Easy: return RewardDifficulty.Easy;
                case HardwareDifficulty.Hard: return RewardDifficulty.Hard;
                default: return RewardDifficulty.Normal;
            }
        }

        /// <summary>One line for the debug overlay.</summary>
        public string StatusLine() => Session == null ? "보상: 없음" : $"보상: {Session}";
    }
}
