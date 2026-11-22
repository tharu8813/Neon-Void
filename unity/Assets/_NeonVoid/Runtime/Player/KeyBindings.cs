using System;
using System.Collections.Generic;

namespace NeonVoid.Player
{
    /// <summary>Why a control cannot be rebound, or <see cref="Rebindable"/> when it can.</summary>
    public enum KeyBindingKind
    {
        /// <summary>A key the player may change.</summary>
        Rebindable = 0,

        /// <summary>
        /// The composite's own row (2DVector), which is not a control at all - its parts are.
        /// Listed so the four directions read as one thing rather than four loose keys.
        /// </summary>
        CompositeHeader = 1,

        /// <summary>
        /// A pointer rather than a key. 조준 is the mouse position, and docs/02 section 7 makes
        /// 마우스 조준 [확정]; there is no other control to move it to.
        /// </summary>
        Pointer = 2,
    }

    /// <summary>One line of the key settings screen.</summary>
    public readonly struct KeyBindingSlot
    {
        public KeyBindingSlot(
            string bindingId,
            string actionName,
            string label,
            string defaultPath,
            string currentPath,
            KeyBindingKind kind)
        {
            BindingId = bindingId ?? string.Empty;
            ActionName = actionName ?? string.Empty;
            Label = label ?? string.Empty;
            DefaultPath = defaultPath ?? string.Empty;
            CurrentPath = currentPath ?? string.Empty;
            Kind = kind;
        }

        /// <summary>
        /// The binding's own GUID from the asset. The key the override is stored under, because
        /// an index would move when a binding is added and silently rebind something else.
        /// </summary>
        public string BindingId { get; }

        public string ActionName { get; }

        /// <summary>What the settings screen calls it, in the document's words.</summary>
        public string Label { get; }

        public string DefaultPath { get; }

        public string CurrentPath { get; }

        public KeyBindingKind Kind { get; }

        public bool IsRebindable => Kind == KeyBindingKind.Rebindable;

        /// <summary>True when the player has moved this one off its default.</summary>
        public bool IsOverridden =>
            IsRebindable
            && !string.Equals(CurrentPath, DefaultPath, StringComparison.OrdinalIgnoreCase);

        /// <summary>The key as a player reads it.</summary>
        public string DisplayKey => KeyBindings.DisplayKey(CurrentPath);

        public KeyBindingSlot With(string path) =>
            new KeyBindingSlot(BindingId, ActionName, Label, DefaultPath, path, Kind);

        public override string ToString() => $"{Label}: {DisplayKey}";
    }

    /// <summary>How a rebind attempt ended.</summary>
    public enum RebindVerdict
    {
        /// <summary>Taken.</summary>
        Applied = 0,

        /// <summary>No such binding on the sheet.</summary>
        UnknownBinding = 1,

        /// <summary>A composite header or the pointer. Nothing to move.</summary>
        NotRebindable = 2,

        /// <summary>The path was empty or not a control path.</summary>
        NotAControl = 3,

        /// <summary>Another action already holds this key. Refused; the player decides.</summary>
        Conflict = 4,

        /// <summary>It was already on this key.</summary>
        Unchanged = 5,
    }

    /// <summary>The answer to a rebind, with the holder named when one refused it.</summary>
    public readonly struct RebindResult
    {
        private RebindResult(RebindVerdict verdict, KeyBindingTable table, string detail, string heldBy)
        {
            Verdict = verdict;
            Table = table;
            Detail = detail ?? string.Empty;
            HeldBy = heldBy ?? string.Empty;
        }

        public RebindVerdict Verdict { get; }

        /// <summary>The table after the change, or the one passed in when nothing changed.</summary>
        public KeyBindingTable Table { get; }

        public string Detail { get; }

        /// <summary>The label of the action already holding the key, on a conflict.</summary>
        public string HeldBy { get; }

        public bool Ok => Verdict == RebindVerdict.Applied;

        internal static RebindResult Applied(KeyBindingTable table) =>
            new RebindResult(RebindVerdict.Applied, table, string.Empty, string.Empty);

        internal static RebindResult Refused(
            RebindVerdict verdict, KeyBindingTable table, string detail, string heldBy = null) =>
            new RebindResult(verdict, table, detail, heldBy);

        public override string ToString() => Ok ? "applied" : $"{Verdict}: {Detail}";
    }

    /// <summary>
    /// The key settings sheet: every binding, its default, and what the player moved it to.
    /// <para>
    /// Immutable and free of Unity input types on purpose. docs/06 section 3 asks for conflict
    /// detection and docs/05 section 2 puts 키 on the 설정 screen; both of those are rules about
    /// which key belongs to which action, and a rule that can only be exercised by pressing a
    /// physical key on a real device is a rule nothing checks.
    /// </para>
    /// <para>
    /// <b>A conflict refuses rather than resolves.</b> Which of two actions keeps a key is the
    /// player's decision. Silently unbinding the other one is the behaviour that loses someone
    /// their movement keys and tells them nothing.
    /// </para>
    /// </summary>
    public sealed class KeyBindingTable
    {
        private readonly KeyBindingSlot[] _slots;

        public KeyBindingTable(IReadOnlyList<KeyBindingSlot> slots)
        {
            int count = slots?.Count ?? 0;
            _slots = new KeyBindingSlot[count];
            for (int i = 0; i < count; ++i)
                _slots[i] = slots[i];
        }

        public IReadOnlyList<KeyBindingSlot> Slots => _slots;

        /// <summary>The binding with this id, or a default slot when there is none.</summary>
        public KeyBindingSlot Find(string bindingId)
        {
            for (int i = 0; i < _slots.Length; ++i)
            {
                if (string.Equals(_slots[i].BindingId, bindingId, StringComparison.OrdinalIgnoreCase))
                    return _slots[i];
            }

            return default;
        }

        /// <summary>
        /// The key currently bound to an action, as a player reads it, for a HUD that would
        /// otherwise print a hardcoded letter. The first rebindable binding of that action.
        /// </summary>
        public string DisplayKeyFor(string actionName)
        {
            for (int i = 0; i < _slots.Length; ++i)
            {
                if (_slots[i].IsRebindable
                    && string.Equals(_slots[i].ActionName, actionName, StringComparison.OrdinalIgnoreCase))
                {
                    return _slots[i].DisplayKey;
                }
            }

            return string.Empty;
        }

        /// <summary>Controls claimed by two different actions. Empty when the sheet is sound.</summary>
        public IReadOnlyList<InputBindingConflict> Conflicts()
        {
            var pairs = new List<KeyValuePair<string, string>>(_slots.Length);
            for (int i = 0; i < _slots.Length; ++i)
            {
                if (_slots[i].IsRebindable)
                    pairs.Add(new KeyValuePair<string, string>(_slots[i].ActionName, _slots[i].CurrentPath));
            }

            return InputBindingConflicts.Find(pairs);
        }

        /// <summary>Only what the player changed. This is what gets written to disk.</summary>
        public IReadOnlyList<KeyBindingSlot> Overrides()
        {
            var changed = new List<KeyBindingSlot>();
            for (int i = 0; i < _slots.Length; ++i)
            {
                if (_slots[i].IsOverridden)
                    changed.Add(_slots[i]);
            }

            return changed;
        }

        /// <summary>Moves one binding to a control, refusing a key another action holds.</summary>
        public RebindResult Rebind(string bindingId, string controlPath)
        {
            int index = IndexOf(bindingId);
            if (index < 0)
                return RebindResult.Refused(RebindVerdict.UnknownBinding, this, $"'{bindingId}' is not on the sheet");

            KeyBindingSlot slot = _slots[index];
            if (!slot.IsRebindable)
            {
                return RebindResult.Refused(
                    RebindVerdict.NotRebindable, this,
                    slot.Kind == KeyBindingKind.Pointer
                        ? $"{slot.Label}은 마우스이며 [확정] docs/02 section 7의 마우스 조준이다"
                        : $"{slot.Label}은 네 방향의 묶음이며 각 방향을 따로 바꾼다");
            }

            if (!KeyBindings.IsControlPath(controlPath))
                return RebindResult.Refused(RebindVerdict.NotAControl, this, $"'{controlPath}' is not a control path");

            if (string.Equals(slot.CurrentPath, controlPath, StringComparison.OrdinalIgnoreCase))
                return RebindResult.Refused(RebindVerdict.Unchanged, this, $"{slot.Label} already uses it");

            for (int i = 0; i < _slots.Length; ++i)
            {
                if (i == index || !_slots[i].IsRebindable)
                    continue;

                if (!string.Equals(_slots[i].CurrentPath, controlPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                // The same action on two keys is an alternate binding, not a clash: WASD and the
                // arrow keys both move, and refusing that would make the asset's own defaults
                // illegal.
                if (string.Equals(_slots[i].ActionName, slot.ActionName, StringComparison.OrdinalIgnoreCase))
                    continue;

                return RebindResult.Refused(
                    RebindVerdict.Conflict, this,
                    $"{KeyBindings.DisplayKey(controlPath)}은(는) 이미 {_slots[i].Label}이(가) 쓰고 있습니다",
                    _slots[i].Label);
            }

            return RebindResult.Applied(Replace(index, slot.With(controlPath)));
        }

        /// <summary>Puts one binding back on the asset's own key.</summary>
        public KeyBindingTable Reset(string bindingId)
        {
            int index = IndexOf(bindingId);
            if (index < 0)
                return this;

            return Replace(index, _slots[index].With(_slots[index].DefaultPath));
        }

        /// <summary>Puts every binding back. [확정] docs/05 section 2's 재설정.</summary>
        public KeyBindingTable ResetAll()
        {
            var reset = new KeyBindingSlot[_slots.Length];
            for (int i = 0; i < _slots.Length; ++i)
                reset[i] = _slots[i].With(_slots[i].DefaultPath);

            return new KeyBindingTable(reset);
        }

        /// <summary>Applies stored overrides onto a freshly read sheet, ignoring unknown ids.</summary>
        public KeyBindingTable WithOverrides(IReadOnlyList<KeyValuePair<string, string>> overrides)
        {
            if (overrides == null || overrides.Count == 0)
                return this;

            var applied = new KeyBindingSlot[_slots.Length];
            Array.Copy(_slots, applied, _slots.Length);

            for (int i = 0; i < overrides.Count; ++i)
            {
                int index = IndexOf(overrides[i].Key);

                // An override for a binding this build no longer has is dropped rather than
                // refused: the settings file outlives any one version of the action asset.
                if (index < 0 || !applied[index].IsRebindable)
                    continue;

                if (!KeyBindings.IsControlPath(overrides[i].Value))
                    continue;

                applied[index] = applied[index].With(overrides[i].Value);
            }

            return new KeyBindingTable(applied);
        }

        private KeyBindingTable Replace(int index, in KeyBindingSlot slot)
        {
            var next = new KeyBindingSlot[_slots.Length];
            Array.Copy(_slots, next, _slots.Length);
            next[index] = slot;
            return new KeyBindingTable(next);
        }

        private int IndexOf(string bindingId)
        {
            if (string.IsNullOrWhiteSpace(bindingId))
                return -1;

            for (int i = 0; i < _slots.Length; ++i)
            {
                if (string.Equals(_slots[i].BindingId, bindingId, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        public override string ToString() => $"{_slots.Length} bindings, {Overrides().Count} changed";
    }

    /// <summary>
    /// Names: what each action is called on the settings screen, and what a control path reads
    /// as when it is printed on a key cap.
    /// </summary>
    public static class KeyBindings
    {
        /// <summary>The action map everything here lives in.</summary>
        public const string MapName = "Gameplay";

        /// <summary>
        /// What the settings screen calls each action. [초안]: the wording, not the set - the
        /// actions themselves come from the asset, so an action with no entry here still gets a
        /// row rather than disappearing from the screen.
        /// </summary>
        private static readonly Dictionary<string, string> ActionLabels =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Move", "이동" },
                { "Aim", "조준" },
                { "Dash", "대시" },
                { "Skill1", "스킬 1" },
                { "Skill2", "스킬 2" },
                { "Skill3", "스킬 3" },
                { "Interact", "상호작용" },
                { "Inspect", "정보" },
                { "Menu", "메뉴" },
                { "Confirm", "확인" },
                { "Cancel", "취소" },
            };

        /// <summary>Composite part names as a player reads them.</summary>
        private static readonly Dictionary<string, string> PartLabels =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "up", "위" },
                { "down", "아래" },
                { "left", "왼쪽" },
                { "right", "오른쪽" },
            };

        /// <summary>Keys whose path does not read as a key cap on its own.</summary>
        private static readonly Dictionary<string, string> KeyNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "space", "Space" },
                { "enter", "Enter" },
                { "escape", "Esc" },
                { "backspace", "Backspace" },
                { "tab", "Tab" },
                { "leftShift", "L-Shift" },
                { "rightShift", "R-Shift" },
                { "leftCtrl", "L-Ctrl" },
                { "rightCtrl", "R-Ctrl" },
                { "leftAlt", "L-Alt" },
                { "rightAlt", "R-Alt" },
                { "upArrow", "↑" },
                { "downArrow", "↓" },
                { "leftArrow", "←" },
                { "rightArrow", "→" },
                { "leftButton", "마우스 좌" },
                { "rightButton", "마우스 우" },
                { "middleButton", "마우스 휠" },
                { "position", "마우스 위치" },
            };

        /// <summary>The settings screen's name for an action, or the action's own name.</summary>
        public static string LabelFor(string actionName) =>
            !string.IsNullOrEmpty(actionName) && ActionLabels.TryGetValue(actionName, out string label)
                ? label
                : actionName ?? string.Empty;

        /// <summary>The name for one row, folding a composite part into its action's label.</summary>
        public static string LabelFor(string actionName, string compositePartName)
        {
            string action = LabelFor(actionName);
            if (string.IsNullOrEmpty(compositePartName))
                return action;

            return PartLabels.TryGetValue(compositePartName, out string part)
                ? $"{action} {part}"
                : $"{action} {compositePartName}";
        }

        /// <summary>
        /// A control path as a key cap: <c>&lt;Keyboard&gt;/q</c> reads as <c>Q</c>. Unknown
        /// paths come back as their last segment rather than as an empty string, so a key this
        /// table has never heard of still prints something a player can match to their keyboard.
        /// </summary>
        public static string DisplayKey(string controlPath)
        {
            if (string.IsNullOrWhiteSpace(controlPath))
                return string.Empty;

            int slash = controlPath.LastIndexOf('/');
            string leaf = slash >= 0 && slash + 1 < controlPath.Length
                ? controlPath.Substring(slash + 1)
                : controlPath;

            if (KeyNames.TryGetValue(leaf, out string name))
                return name;

            // Single letters and digits read as themselves, upper case, the way a key cap does.
            return leaf.Length == 1 ? leaf.ToUpperInvariant() : leaf;
        }

        /// <summary>
        /// Whether a string is a control path at all. Deliberately narrow: this is what a
        /// settings file is allowed to put into the input system, and a path that is not a
        /// control would be stored and then silently do nothing.
        /// </summary>
        public static bool IsControlPath(string path) =>
            !string.IsNullOrWhiteSpace(path)
            && path.StartsWith("<", StringComparison.Ordinal)
            && path.IndexOf(">/", StringComparison.Ordinal) > 0;
    }
}
