using System;
using NeonVoid.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace NeonVoid.UI
{
    /// <summary>
    /// Opens 설정 and feeds it the key the player presses.
    /// <para>
    /// Separate from <see cref="HudController"/> so the screen's rules - Escape cancels, a
    /// conflict refuses, combat does not act on a key pressed to rebind it - stay in a type
    /// with no device in it. This is the only part that needs a real keyboard, and it is about
    /// twenty lines.
    /// </para>
    /// <para>
    /// <b>Read on the render frame, not the logic tick.</b> Opening a menu is not simulation:
    /// it changes nothing the replay depends on, and waiting up to a tick to notice a key press
    /// would make the screen feel late for no gain.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HudController))]
    public sealed class SettingsScreenInput : MonoBehaviour
    {
        [Tooltip("The HUD whose 설정 screen this opens. Found on this object when left empty.")]
        [SerializeField] private HudController hud;

        [Tooltip("The same asset the router reads, for the 메뉴 action. Found when left empty.")]
        [SerializeField] private InputActionAsset actions;

        private InputAction _menu;
        private IDisposable _listening;

        /// <summary>What the last captured press resolved to. For a test and the log.</summary>
        public string LastCapturedPath { get; private set; } = string.Empty;

        /// <summary>Fills in what a scene would have serialized.</summary>
        public void Configure(HudController controller, InputActionAsset asset)
        {
            hud = controller;
            actions = asset;
        }

        private void Awake()
        {
            if (hud == null)
                hud = GetComponent<HudController>();

            if (actions == null)
            {
                var director = FindFirstObjectByType<InputBindingDirector>();
                actions = director != null ? director.Actions : null;
            }
        }

        private void OnEnable()
        {
            InputActionMap map = actions != null
                ? actions.FindActionMap(KeyBindings.MapName, throwIfNotFound: false)
                : null;

            _menu = map != null ? map.FindAction("Menu", throwIfNotFound: false) : null;

            // Enabled here rather than relied on: the router enables the map it uses, and a
            // scene that has a HUD but no router would otherwise have no way into 설정.
            _menu?.Enable();

            if (_menu == null)
                Debug.LogWarning($"{name}: no 메뉴 action, so 설정 cannot be opened with a key.", this);
        }

        private void OnDisable() => StopListening();

        private void Update()
        {
            if (hud == null)
                return;

            // While the screen waits for a key, Escape means cancel. Letting the 메뉴 action
            // also fire would close the whole screen on a keypress the player meant as "not
            // that one".
            if (_menu != null && !hud.Capture.IsListening && _menu.WasPressedThisFrame())
                hud.ToggleSettings();

            if (hud.Capture.IsListening && _listening == null)
                _listening = InputSystem.onAnyButtonPress.Call(OnPressed);
            else if (!hud.Capture.IsListening && _listening != null)
                StopListening();
        }

        private void OnPressed(InputControl control)
        {
            if (hud == null || !hud.Capture.IsListening || control == null)
                return;

            // Keyboard only. A mouse button would be captured from the very click that opened
            // the prompt, and [확정] docs/02 section 7 keeps the mouse for 조준 and 자동 사격
            // anyway - nothing in the game is bound to a mouse button to begin with.
            if (!(control.device is Keyboard))
                return;

            string path = KeyCapturePath.LayoutPath(
                control.device.layout, control.device.path, control.path);

            if (string.IsNullOrEmpty(path))
                return;

            LastCapturedPath = path;
            hud.OfferCapturedKey(path);
            StopListening();
        }

        private void StopListening()
        {
            _listening?.Dispose();
            _listening = null;
        }
    }
}
