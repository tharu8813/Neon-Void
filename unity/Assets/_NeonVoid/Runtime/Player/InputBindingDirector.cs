using System.Collections.Generic;
using NeonVoid.Progression.Save;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonVoid.Player
{
    /// <summary>
    /// 키 바인딩: reads the action asset into a sheet, applies what the player changed, and
    /// keeps it in 전역 설정.
    /// <para>
    /// [확정] docs/04 section 1 puts 키 바인딩 in 전역 설정 - 백신과 독립, 백신 최종 파기에서도
    /// 보존 - so this writes to <see cref="GlobalSettingsStore"/> beside the slots and never
    /// into one. docs/06 section 3 asks for conflict detection, which
    /// <see cref="KeyBindingTable"/> does; docs/05 section 2 puts 키 on the 설정 screen, which
    /// does not exist yet, so for now the sheet is what the HUD reads its key caps from.
    /// </para>
    /// <para>
    /// <b>Nothing is applied to the input system until it is in the file.</b> A rebind that
    /// took effect and then failed to save would come back on the next launch, which is the
    /// same shape of lie docs/04 section 7 forbids for rewards: the screen says it happened and
    /// permanent state disagrees.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-400)]
    public sealed class InputBindingDirector : MonoBehaviour
    {
        [Tooltip("The same asset PlayerInputRouter reads. Required.")]
        [SerializeField] private InputActionAsset actions;

        [Tooltip(
            "Writes key bindings to the real settings file under Application.persistentDataPath. " +
            "Settings are not a vaccine's and cost nothing to rewrite, so this is on; a test " +
            "injects its own store instead.")]
        [SerializeField] private bool useRealSettingsFile = true;

        private GlobalSettingsStore _settings;
        private bool _prepared;

        /// <summary>The sheet, as the asset reads with the player's changes applied.</summary>
        public KeyBindingTable Table { get; private set; } = new KeyBindingTable(null);

        /// <summary>Why the last refused rebind was refused. Empty after one that took.</summary>
        public string LastRefusal { get; private set; } = string.Empty;

        /// <summary>The settings file, or null when none could be opened.</summary>
        public GlobalSettingsStore Settings => _settings;

        /// <summary>The asset these bindings are on, so the 설정 screen reads the same one.</summary>
        public InputActionAsset Actions => actions;

        /// <summary>True when a rebind would actually be written down.</summary>
        public bool CanPersist => _settings != null;

        /// <summary>Points this at an injected store. Call before the object wakes.</summary>
        public void UseSettings(GlobalSettingsStore settings)
        {
            _settings = settings;
            _prepared = false;
            EnsurePrepared();
        }

        /// <summary>Fills in what a scene would have serialized.</summary>
        public void Configure(InputActionAsset asset) => actions = asset;

        private void Awake() => EnsurePrepared();

        private void EnsurePrepared()
        {
            if (_prepared)
                return;

            _prepared = true;

            if (actions == null)
            {
                Debug.LogError(
                    $"{name}: no InputActionAsset, so there are no key bindings to read.", this);
                return;
            }

            if (_settings == null && useRealSettingsFile)
                _settings = OpenRealSettings();

            Table = ReadSheet();

            IReadOnlyList<KeyValuePair<string, string>> stored =
                _settings != null ? _settings.Document.Overrides() : null;

            if (stored == null || stored.Count == 0)
                return;

            KeyBindingTable withOverrides = Table.WithOverrides(stored);

            // A settings file that would put two actions on one key is not applied. It can only
            // get that way by being edited by hand or written by another build, and the result
            // in game is a key that does two things with no way to tell which.
            IReadOnlyList<InputBindingConflict> conflicts = withOverrides.Conflicts();
            if (conflicts.Count > 0)
            {
                Debug.LogWarning(
                    $"{name}: the saved key bindings conflict ({conflicts[0]}), so the asset's " +
                    "own keys are used for this session. Nothing was overwritten.", this);
                return;
            }

            Table = withOverrides;
            PushToAsset(Table);
        }

        private GlobalSettingsStore OpenRealSettings()
        {
            try
            {
                var store = new GlobalSettingsStore(
                    new DiskSaveFileSystem(), GlobalSettingsLayout.Under(UnitySaveRoot.Resolve()));

                GlobalSettingsVerdict verdict = store.Open();
                if (verdict == GlobalSettingsVerdict.Unreadable)
                {
                    // Said, and left alone. The bytes are the only copy of whatever the player
                    // configured, and rewriting them at startup would destroy it.
                    Debug.LogWarning(
                        $"{name}: the settings file could not be read ({store.Detail}). The " +
                        "asset's own keys are used and the file is left untouched.", this);
                }

                return store;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"{name}: no settings file, so key changes will not be kept. {e.Message}", this);
                return null;
            }
        }

        /// <summary>
        /// Moves one binding to a control and keeps it. The order is deliberate: the sheet
        /// decides whether it is allowed, the file takes it, and only then does the input
        /// system change.
        /// </summary>
        public RebindResult Rebind(string bindingId, string controlPath)
        {
            RebindResult result = Table.Rebind(bindingId, controlPath);
            if (!result.Ok)
            {
                LastRefusal = result.Detail;
                return result;
            }

            if (!Persist(result.Table, out string detail))
            {
                LastRefusal = detail;
                return RebindResult.Refused(RebindVerdict.NotRebindable, Table, detail);
            }

            Table = result.Table;
            PushToAsset(Table);
            LastRefusal = string.Empty;
            return result;
        }

        /// <summary>[확정] docs/05 section 2's 재설정: every key back to the asset's own.</summary>
        public bool ResetAll()
        {
            KeyBindingTable reset = Table.ResetAll();
            if (!Persist(reset, out string detail))
            {
                LastRefusal = detail;
                return false;
            }

            Table = reset;
            PushToAsset(Table);
            LastRefusal = string.Empty;
            return true;
        }

        /// <summary>The key a HUD should print for an action, or empty when there is none.</summary>
        public string DisplayKeyFor(string actionName) => Table.DisplayKeyFor(actionName);

        private bool Persist(KeyBindingTable table, out string detail)
        {
            detail = string.Empty;
            if (_settings == null)
            {
                detail = "설정 파일이 없어 키 변경을 기록할 수 없습니다";
                return false;
            }

            IReadOnlyList<KeyBindingSlot> changed = table.Overrides();
            var pairs = new List<KeyValuePair<string, string>>(changed.Count);
            for (int i = 0; i < changed.Count; ++i)
                pairs.Add(new KeyValuePair<string, string>(changed[i].BindingId, changed[i].CurrentPath));

            _settings.Document.SetOverrides(pairs);
            return _settings.Save(out detail);
        }

        private KeyBindingTable ReadSheet()
        {
            KeyBindingTable sheet = ReadSheet(actions);
            if (sheet.Slots.Count == 0)
                Debug.LogError($"{name}: the asset has no '{KeyBindings.MapName}' map.", this);

            return sheet;
        }

        /// <summary>
        /// Reads an asset as it ships: every binding on its own default path, with whatever
        /// overrides are already on it as the current one.
        /// <para>
        /// Static and public so the shipped asset can be checked without a scene - the claim
        /// that the HUD prints Q because the asset says Q is only worth something if something
        /// reads the asset.
        /// </para>
        /// </summary>
        public static KeyBindingTable ReadSheet(InputActionAsset asset)
        {
            InputActionMap map = asset == null
                ? null
                : asset.FindActionMap(KeyBindings.MapName, throwIfNotFound: false);

            if (map == null)
                return new KeyBindingTable(null);

            var slots = new List<KeyBindingSlot>(map.bindings.Count);
            for (int i = 0; i < map.bindings.Count; ++i)
            {
                InputBinding binding = map.bindings[i];
                InputAction action = map.FindAction(binding.action, throwIfNotFound: false);

                KeyBindingKind kind;
                if (binding.isComposite)
                    kind = KeyBindingKind.CompositeHeader;
                else if (!binding.isPartOfComposite && IsPointer(action))
                    kind = KeyBindingKind.Pointer;
                else
                    kind = KeyBindingKind.Rebindable;

                string label = binding.isPartOfComposite
                    ? KeyBindings.LabelFor(binding.action, binding.name)
                    : KeyBindings.LabelFor(binding.action);

                if (binding.isComposite && !string.IsNullOrEmpty(binding.name))
                    label = $"{KeyBindings.LabelFor(binding.action)} ({binding.name})";

                slots.Add(new KeyBindingSlot(
                    binding.id.ToString(),
                    binding.action,
                    label,
                    // binding.path is what the asset ships with; an override lives beside it, so
                    // the default survives being changed and 재설정 has something to go back to.
                    binding.path,
                    binding.effectivePath,
                    kind));
            }

            return new KeyBindingTable(slots);
        }

        /// <summary>
        /// A control that is aimed rather than pressed. [확정] docs/02 section 7: 마우스 조준.
        /// Read off the action's expected type rather than off the path, so it stays true for a
        /// stick as well as a mouse.
        /// </summary>
        private static bool IsPointer(InputAction action) =>
            action != null && string.Equals(action.expectedControlType, "Vector2", System.StringComparison.Ordinal);

        /// <summary>Writes the sheet's paths onto the asset, as overrides, never as paths.</summary>
        private void PushToAsset(KeyBindingTable table)
        {
            InputActionMap map = actions.FindActionMap(KeyBindings.MapName, throwIfNotFound: false);
            if (map == null)
                return;

            bool wasEnabled = actions.enabled;
            if (wasEnabled)
                actions.Disable();

            for (int i = 0; i < map.bindings.Count; ++i)
            {
                InputBinding binding = map.bindings[i];
                KeyBindingSlot slot = table.Find(binding.id.ToString());

                if (!slot.IsRebindable)
                    continue;

                bool isDefault = string.Equals(
                    slot.CurrentPath, binding.path, System.StringComparison.OrdinalIgnoreCase);

                // An empty overridePath clears the override, which is the map-level way to say
                // RemoveBindingOverride - that extension only exists on InputAction.
                map.ApplyBindingOverride(
                    i, new InputBinding { overridePath = isDefault ? null : slot.CurrentPath });
            }

            if (wasEnabled)
                actions.Enable();
        }
    }
}
