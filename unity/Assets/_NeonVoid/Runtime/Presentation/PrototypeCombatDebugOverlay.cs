using System.Collections.Generic;
using System.Text;
using NeonVoid.Combat;
using NeonVoid.Combat.Skills;
using NeonVoid.Combat.Status;
using NeonVoid.Core;
using NeonVoid.Plugins;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonVoid.Presentation
{
    /// <summary>
    /// <b>Prototype.</b> Shows the numbers the simulation is already computing, so what the
    /// rules do can be checked by looking at the game.
    /// <para>
    /// This is not the HUD. docs/05 specifies that, in UI Toolkit, and it shows a player what
    /// they need. This shows a developer what the simulation thinks, which is a different job:
    /// health that has no bar, mana that has no gauge, statuses that have no icons, and plugin
    /// effects that have no VFX at all.
    /// </para>
    /// <para>
    /// It exists because of a specific failure. Chain lightning, the explosion glyph and the
    /// armour mark were all implemented, unit tested and wired, and then could not be verified
    /// in play - not because they were broken, but because nothing about them was visible.
    /// Asking someone to confirm an invisible effect is asking them to guess. A passing test
    /// suite does not make a feature observable, and an unobservable feature is unverifiable.
    /// </para>
    /// <para>
    /// IMGUI on purpose: it needs no assets, it cannot be mistaken for finished UI, and it is
    /// deleted rather than migrated when the real HUD arrives.
    /// </para>
    /// </summary>
    public sealed class PrototypeCombatDebugOverlay : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private CombatActor player;
        [SerializeField] private PluginProcDirector procDirector;
        [SerializeField] private SimulationRunner runner;
        [SerializeField] private Camera worldCamera;

        [Header("Display")]
        [Tooltip("Seconds between rescans of the actor list. OnGUI runs several times a frame, " +
                 "so the scan must not happen there.")]
        [SerializeField, Min(0.1f)] private float actorRescanSeconds = 1f;

        [SerializeField] private bool visible = true;

        private readonly List<CombatActor> _actors = new List<CombatActor>();
        private readonly StringBuilder _line = new StringBuilder(96);
        private float _nextRescanTime;
        private GUIStyle _panelLabel;
        private GUIStyle _worldLabel;

        // The statuses worth showing. The catalogue has seventeen; these are the ones the
        // prototype can actually produce, and a list keeps the overlay out of the business of
        // deciding what matters.
        private static readonly StatusEffectId[] Watched =
        {
            StatusEffectId.Slow,
            StatusEffectId.Root,
            StatusEffectId.Stun,
            StatusEffectId.Silence,
            StatusEffectId.FirewallBreach,
            StatusEffectId.Invulnerable,
        };

        private void Awake()
        {
            if (player == null)
                player = GetComponent<CombatActor>();
            if (procDirector == null)
                procDirector = FindFirstObjectByType<PluginProcDirector>();
            if (runner == null)
                runner = FindFirstObjectByType<SimulationRunner>();
            if (worldCamera == null)
                worldCamera = Camera.main;
        }

        private void Update()
        {
            // F1 toggles, so a screenshot of the game without the overlay is still possible.
            // Read through the Input System rather than the legacy Input class: this project
            // has Active Input Handling set to the package alone, where UnityEngine.Input
            // throws on the first read.
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
                visible = !visible;

            if (Time.unscaledTime < _nextRescanTime)
                return;

            _nextRescanTime = Time.unscaledTime + actorRescanSeconds;
            _actors.Clear();
            foreach (CombatActor actor in FindObjectsByType<CombatActor>(FindObjectsSortMode.None))
                if (actor != null)
                    _actors.Add(actor);

            // Sorted by actor id so the list does not reshuffle between scans.
            _actors.Sort((a, b) => a.ActorId.CompareTo(b.ActorId));
        }

        private void OnGUI()
        {
            if (!visible)
                return;

            EnsureStyles();
            DrawPanel();
            DrawWorldLabels();
        }

        private void DrawPanel()
        {
            var box = new Rect(12f, 12f, 330f, 250f);
            GUI.Box(box, GUIContent.none);
            GUILayout.BeginArea(new Rect(box.x + 10f, box.y + 8f, box.width - 20f, box.height - 16f));

            GUILayout.Label("NEON VOID · PROTOTYPE DEBUG (F1)", _panelLabel);

            if (runner != null)
                GUILayout.Label(
                    $"tick {runner.TickIndex}   dropped {runner.DroppedTicks}", _panelLabel);

            if (player != null)
            {
                HealthPool health = player.Health;
                ManaPool mana = player.Mana;
                GUILayout.Label(
                    $"HP {health.Current:0}/{health.Max:0}    MP {mana.Current:0}/{mana.Max:0}", _panelLabel);
                GUILayout.Label($"skills  {SkillCooldowns(player.Skills)}", _panelLabel);

                // Base beside effective, so a buff or debuff is visible as a number changing
                // rather than as a vague feeling that the game got easier.
                CombatStats stats = player.Stats;
                CombatStats baseStats = player.BaseStats;
                GUILayout.Label(
                    $"AD {stats.AttackDamage:0.#}/{baseStats.AttackDamage:0.#}   " +
                    $"AP {stats.AbilityPower:0.#}   " +
                    $"crit {stats.CritChancePercent:0.#}%   " +
                    $"spd x{player.MoveSpeedMultiplier:0.00}", _panelLabel);

                string status = StatusSummary(player.Status);
                GUILayout.Label($"status  {(status.Length == 0 ? "-" : status)}", _panelLabel);
            }

            if (procDirector != null)
                DrawProcCounters();

            GUILayout.EndArea();
        }

        /// <summary>
        /// The plugin counters: the only evidence that an invisible effect happened at all.
        /// Refusals are shown beside them, because an internal cooldown doing its job and a
        /// plugin never firing look identical without them.
        /// </summary>
        private void DrawProcCounters()
        {
            PluginLoadout loadout = procDirector.Loadout;
            if (loadout == null)
                return;

            GUILayout.Space(4f);
            for (int i = 0; i < loadout.Count; ++i)
            {
                PluginDefinition plugin = loadout.At(i);
                GUILayout.Label(
                    $"{plugin.ProposalId}  fired {procDirector.ResolvedCountOf(plugin.EffectId)}", _panelLabel);
            }

            GUILayout.Label(
                $"refused  icd {procDirector.BlockedByInternalCooldown}   " +
                $"chain {procDirector.BlockedByCycleOrLimit}", _panelLabel);
        }

        private string SkillCooldowns(SkillLoadout skills)
        {
            if (skills == null)
                return "-";

            _line.Clear();
            for (int i = 0; i < skills.SlotCount; ++i)
            {
                if (i > 0)
                    _line.Append("  ");

                _line.Append(i == 0 ? "Q" : i == 1 ? "E" : "R").Append(' ');
                if (skills.SlotAt(i).IsEmpty)
                    _line.Append('-');
                else if (skills.IsReady(i))
                    _line.Append("ready");
                else
                    _line.Append((skills.CooldownRemaining(i) / (float)DesignConstants.SimulationTicksPerSecond)
                        .ToString("0.0")).Append('s');
            }

            return _line.ToString();
        }

        private static string StatusSummary(StatusContainer status)
        {
            if (status == null)
                return string.Empty;

            var text = new StringBuilder();
            for (int i = 0; i < Watched.Length; ++i)
            {
                StatusEffectId id = Watched[i];
                if (!status.Has(id))
                    continue;

                if (text.Length > 0)
                    text.Append(", ");

                int stacks = status.StacksOf(id);
                text.Append(id);
                if (stacks > 1)
                    text.Append('x').Append(stacks);
                text.Append(' ').Append((status.RemainingTicksOf(id)
                    / (float)DesignConstants.SimulationTicksPerSecond).ToString("0.0")).Append('s');
            }

            return text.ToString();
        }

        /// <summary>
        /// Health and status over each actor's head. Without this an enemy losing health to an
        /// effect it was not shot by is completely silent.
        /// </summary>
        private void DrawWorldLabels()
        {
            if (worldCamera == null)
                return;

            for (int i = 0; i < _actors.Count; ++i)
            {
                CombatActor actor = _actors[i];
                if (actor == null || actor == player)
                    continue;

                Vector3 screen = worldCamera.WorldToScreenPoint(actor.transform.position + Vector3.up * 1.2f);
                if (screen.z <= 0f)
                    continue;

                HealthPool health = actor.Health;
                string status = StatusSummary(actor.Status);

                // Armour only when something has changed it, which is how 부식 표식 becomes
                // visible: the mark used to be displayed while reducing nothing at all.
                float armor = actor.Stats.PhysicalArmor;
                string armorText = Mathf.Abs(armor - actor.BaseStats.PhysicalArmor) > 0.01f
                    ? $"  arm {armor:0.#}"
                    : string.Empty;

                string text = actor.IsAlive
                    ? $"#{actor.ActorId}  {health.Current:0}/{health.Max:0}{armorText}" +
                      (status.Length > 0 ? $"\n{status}" : string.Empty)
                    : $"#{actor.ActorId}  dead";

                var rect = new Rect(screen.x - 70f, Screen.height - screen.y - 28f, 140f, 44f);
                GUI.Label(rect, text, _worldLabel);
            }
        }

        private void EnsureStyles()
        {
            if (_panelLabel != null)
                return;

            _panelLabel = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = false };
            _panelLabel.normal.textColor = new Color(0.78f, 0.93f, 1f);

            _worldLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.UpperCenter,
                wordWrap = false,
            };
            _worldLabel.normal.textColor = new Color(1f, 0.82f, 0.55f);
        }
    }
}
