using NeonVoid.Progression;
using UnityEngine;

namespace NeonVoid.Presentation
{
    /// <summary>
    /// <b>Prototype.</b> The smallest honest on-screen answer to "I died and nothing happened".
    /// <para>
    /// This is not the death screen. docs/05 specifies that screen - this mission's losses,
    /// preserved XP, clones remaining, the final-destruction warning - and every number on it
    /// comes from a committed save, which does not exist yet. Drawing those numbers now would
    /// mean inventing them, and CLAUDE.md forbids a mock that reads as the real thing.
    /// </para>
    /// <para>
    /// So it states the phase and nothing more: the run ended, the save was not committed, and
    /// no permanent state changed. It is IMGUI on purpose - the real HUD is UI Toolkit per
    /// docs/05 and needs assets this prototype scene has no reason to carry, and a debug
    /// overlay that needs no assets cannot be mistaken for finished UI.
    /// </para>
    /// </summary>
    public sealed class PrototypeDeathBanner : MonoBehaviour
    {
        [SerializeField] private RunDeathDirector deathDirector;

        private GUIStyle _titleStyle;
        private GUIStyle _lineStyle;

        private void Awake()
        {
            if (deathDirector == null)
                deathDirector = FindFirstObjectByType<RunDeathDirector>();
        }

        private void OnGUI()
        {
            if (deathDirector == null || !deathDirector.Ledger.HasRegisteredDeath)
                return;

            EnsureStyles();

            RunDeathLedger ledger = deathDirector.Ledger;
            const float width = 560f;
            const float height = 150f;
            var box = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

            GUI.Box(box, GUIContent.none);
            GUILayout.BeginArea(new Rect(box.x + 18f, box.y + 14f, box.width - 36f, box.height - 28f));
            GUILayout.Label("사망 (PROTOTYPE)", _titleStyle);
            GUILayout.Space(6f);
            GUILayout.Label($"상태: {ledger.Phase}   ·   {ledger.RegisteredDeathId}", _lineStyle);
            GUILayout.Label(
                ledger.MayReleaseResults
                    ? "저장이 커밋됐습니다. 결과를 표시할 수 있습니다."
                    : "저장 커밋 대기 중입니다. 복제 차감·XP 정산·플러그인 소실은 아직 아무것도 일어나지 않았습니다.",
                _lineStyle);

            if (!string.IsNullOrEmpty(ledger.LastFailureReason))
                GUILayout.Label($"커밋 실패: {ledger.LastFailureReason}", _lineStyle);

            GUILayout.Label("이 배너는 사망이 관측됐다는 증거일 뿐, docs/05의 사망/파기 화면이 아닙니다.", _lineStyle);
            GUILayout.EndArea();
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null)
                return;

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
            };
            _titleStyle.normal.textColor = new Color(1f, 0.35f, 0.45f);

            _lineStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            _lineStyle.normal.textColor = new Color(0.86f, 0.92f, 1f);
        }
    }
}
