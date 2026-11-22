using System;

namespace NeonVoid.UI
{
    /// <summary>
    /// The 설정 screen while it is waiting for the player to press a key.
    /// <para>
    /// A state rather than a callback because the screen has to look different while it waits -
    /// docs/05 section 7 asks for 리바인딩·키 중복 확인 as a thing a person does - and because
    /// the two rules that matter here are about what a key press means, not about input:
    /// Escape cancels rather than binds, and combat does not act on a key pressed to rebind it.
    /// </para>
    /// </summary>
    public readonly struct KeyCapture
    {
        private readonly string _bindingId;
        private readonly string _label;
        private readonly string _message;

        private KeyCapture(string bindingId, string label, string message, bool isFailure)
        {
            _bindingId = bindingId;
            _label = label;
            _message = message;
            IsFailure = isFailure;
        }

        /// <summary>Not waiting for anything.</summary>
        public static readonly KeyCapture Idle = default;

        // Null-coalesced on the way out rather than on the way in: Idle is a default struct,
        // whose fields no constructor ever touched.

        /// <summary>The binding being changed, or empty when not listening.</summary>
        public string BindingId => _bindingId ?? string.Empty;

        /// <summary>What that binding is called, for the prompt.</summary>
        public string Label => _label ?? string.Empty;

        /// <summary>What to tell the player: a prompt while listening, a result after.</summary>
        public string Message => _message ?? string.Empty;

        /// <summary>True when <see cref="Message"/> is something that went wrong.</summary>
        public bool IsFailure { get; }

        /// <summary>True while a key press should be taken as a rebind and not as play.</summary>
        public bool IsListening => BindingId.Length > 0;

        /// <summary>Whether anything should be drawn at all.</summary>
        public bool HasMessage => Message.Length > 0;

        /// <summary>Starts waiting for a key for one binding.</summary>
        public static KeyCapture Listening(string bindingId, string label) =>
            new KeyCapture(
                bindingId,
                label,
                string.IsNullOrEmpty(label) ? "키를 누르세요 · Esc 취소" : $"{label}: 키를 누르세요 · Esc 취소",
                isFailure: false);

        /// <summary>Stops waiting and says why nothing changed.</summary>
        public static KeyCapture Refused(string message) =>
            new KeyCapture(string.Empty, string.Empty, message, isFailure: true);

        /// <summary>Stops waiting and says what changed.</summary>
        public static KeyCapture Took(string label, string key) =>
            new KeyCapture(string.Empty, string.Empty, $"{label} → {key}", isFailure: false);

        /// <summary>Stops waiting with nothing to say.</summary>
        public static KeyCapture Cancelled() =>
            new KeyCapture(string.Empty, string.Empty, "취소했습니다", isFailure: false);

        public override string ToString() =>
            IsListening ? $"listening for {Label}" : (HasMessage ? Message : "idle");
    }

    /// <summary>
    /// Turning a pressed control into something a binding can be stored as.
    /// <para>
    /// A pressed control's own path names the device instance - <c>/Keyboard/q</c> - and an
    /// override stored like that would stop working the moment the player plugged in a second
    /// keyboard, because it names <i>that</i> keyboard. The layout form, <c>&lt;Keyboard&gt;/q</c>,
    /// is what the asset uses and what survives.
    /// </para>
    /// </summary>
    public static class KeyCapturePath
    {
        /// <summary>
        /// [초안] docs/02 section 7 drafts Escape for 메뉴; whatever it is bound to, a capture
        /// has to be cancellable by a key that is never itself captured, or a player who opened
        /// the prompt by mistake has to bind something to get out.
        /// </summary>
        public const string CancelPath = "<Keyboard>/escape";

        /// <summary>True when this press means "stop waiting" rather than "use this key".</summary>
        public static bool IsCancel(string layoutPath) =>
            string.Equals(layoutPath, CancelPath, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The layout path for a control, or empty when the three parts do not describe one.
        /// </summary>
        /// <param name="deviceLayout">The device's layout name, e.g. <c>Keyboard</c>.</param>
        /// <param name="devicePath">The device's own path, e.g. <c>/Keyboard</c>.</param>
        /// <param name="controlPath">The control's path, e.g. <c>/Keyboard/q</c>.</param>
        public static string LayoutPath(string deviceLayout, string devicePath, string controlPath)
        {
            if (string.IsNullOrWhiteSpace(deviceLayout)
                || string.IsNullOrWhiteSpace(devicePath)
                || string.IsNullOrWhiteSpace(controlPath))
            {
                return string.Empty;
            }

            if (!controlPath.StartsWith(devicePath, StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            string leaf = controlPath.Substring(devicePath.Length).TrimStart('/');
            if (leaf.Length == 0)
                return string.Empty;

            return $"<{deviceLayout}>/{leaf}";
        }
    }
}
