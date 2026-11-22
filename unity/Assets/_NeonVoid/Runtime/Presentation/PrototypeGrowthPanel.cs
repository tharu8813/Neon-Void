using System.Collections.Generic;
using NeonVoid.Missions;
using NeonVoid.Progression;
using UnityEngine;

namespace NeonVoid.Presentation
{
    /// <summary>
    /// The 백신 성장 터미널's screen: 영구 능력치 포인트 spent on the table, and 무료 재분배.
    /// <para>
    /// Shown only while the player is standing at the terminal, because docs/00 section 6 makes
    /// the hub 실제 3D 공간 rather than a menu list - the panel is what the terminal shows you,
    /// not a screen you can open anywhere.
    /// </para>
    /// <para>
    /// <b>Prototype, in IMGUI, on purpose.</b> docs/05 gives 보상 선택·정교한 수치 비교 to a 2D
    /// overlay and this is not it: no layout, no art, no comparison of what a point would buy.
    /// What it is for is that the [확정] rule - 허브에서 재분배 비용 없이 언제든 초기화·재투자 -
    /// can be exercised by a person rather than only by a test, which is what the 8~12분 실측
    /// needs. It needs no assets, it cannot be mistaken for finished UI, and it is deleted
    /// rather than migrated when the real terminal arrives.
    /// </para>
    /// <para>
    /// Every button goes through <see cref="VaccineGrowthDirector"/>, so nothing on screen
    /// changes until permanent state has taken the change - docs/04 section 5 step 7.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PrototypeGrowthPanel : MonoBehaviour
    {
        [Tooltip("The hub whose terminal this belongs to. Found in the scene when left empty.")]
        [SerializeField] private HubRuntime hub;

        [Tooltip("Shown even when the player is nowhere near the terminal. For debugging.")]
        [SerializeField] private bool alwaysVisible;

        [Tooltip(
            "Disables this panel in a scene that has the UI Toolkit 성장 터미널 화면, so the " +
            "two do not stack on top of each other.")]
        [SerializeField] private bool standDownForTheRealScreen = true;

        private GUIStyle _label;
        private GUIStyle _header;
        private Vector2 _scroll;

        private void Awake()
        {
            if (hub == null)
                hub = FindFirstObjectByType<HubRuntime>();

            // The loop scene has the real terminal screen now, with 구매 전후 값 and
            // 실제 방어감소율 that this one never had.
            if (standDownForTheRealScreen && FindFirstObjectByType<NeonVoid.UI.HudController>() != null)
                enabled = false;
        }

        /// <summary>Whether the panel would be drawn right now.</summary>
        public bool IsOpen =>
            hub != null
            && hub.Growth != null
            && (alwaysVisible || hub.IsInReach(HubTerminalKind.Growth))
            && (hub.Flow == null || hub.Flow.Phase == MissionFlowPhase.Hub);

        private void OnGUI()
        {
            if (!IsOpen)
                return;

            EnsureStyles();
            VaccineGrowthDirector growth = hub.Growth;

            var box = new Rect(Screen.width - 392f, 12f, 380f, Mathf.Min(Screen.height - 24f, 620f));
            GUI.Box(box, GUIContent.none);
            GUILayout.BeginArea(new Rect(box.x + 10f, box.y + 8f, box.width - 20f, box.height - 16f));

            GUILayout.Label(HubTerminals.NameOf(HubTerminalKind.Growth), _header);
            GUILayout.Label(growth.Sheet(), _label);

            if (!growth.CanInvest)
            {
                // Said rather than hidden: a terminal that silently did nothing would look
                // broken, and "the save is not committing" is the actual reason.
                GUILayout.Label(growth.LastRefusal.Length > 0 ? growth.LastRefusal : "투자 불가", _label);
                GUILayout.EndArea();
                return;
            }

            if (GUILayout.Button($"무료 재분배 ({growth.PointsSpent}p 회수)"))
                growth.TryRedistribute();

            _scroll = GUILayout.BeginScrollView(_scroll);
            DrawRows(growth);
            GUILayout.EndScrollView();

            if (growth.LastRefusal.Length > 0)
                GUILayout.Label(growth.LastRefusal, _label);

            GUILayout.EndArea();
        }

        /// <summary>
        /// One row per entry in GDD v1 section 13.1, including the ones nothing applies - they
        /// are in the table, and a panel that hid them would make the table look shorter than
        /// it is. Those rows say why instead of offering a button.
        /// </summary>
        private static void DrawRows(VaccineGrowthDirector growth)
        {
            IReadOnlyList<VaccineStatInvestment> allocation = growth.Allocation;
            StatInvestmentStep[] rows = StatInvestments.All;

            for (int i = 0; i < rows.Length; ++i)
            {
                StatInvestmentStep row = rows[i];
                int steps = StepsIn(allocation, row.Stat);

                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    $"{row.DisplayName}  +{row.DraftIncrementPerStep}  ({row.DraftPointCost}p)" +
                    (steps > 0 ? $"  x{steps}" : string.Empty),
                    GUILayout.Width(250f));

                if (!row.IsConnected)
                    GUILayout.Label("미연결", GUILayout.Width(80f));
                else if (GUILayout.Button("+1", GUILayout.Width(80f)))
                    growth.TryInvest(row.Stat);

                GUILayout.EndHorizontal();
            }
        }

        private static int StepsIn(IReadOnlyList<VaccineStatInvestment> allocation, InvestableStat stat)
        {
            for (int i = 0; i < allocation.Count; ++i)
            {
                if (allocation[i].Stat == stat)
                    return allocation[i].Steps;
            }

            return 0;
        }

        private void EnsureStyles()
        {
            if (_label != null)
                return;

            _label = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            _header = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
        }
    }
}
