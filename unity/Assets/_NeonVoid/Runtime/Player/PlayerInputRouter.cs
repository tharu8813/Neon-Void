using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonVoid.Player
{
    /// <summary>
    /// The only place devices are read. Turns the Input System actions into the structured
    /// intent the simulation consumes, and bridges the render frame to the logic tick.
    /// <para>
    /// <b>Why button presses are latched.</b> Devices are polled once per render frame, while
    /// the simulation runs on its own 60 Hz tick. At 144 fps most frames run no tick at all,
    /// so a Space press read in <see cref="Update"/> would be gone before the next tick asked
    /// for it. Each press is therefore latched here and cleared only when a tick actually
    /// consumes it, so a dash can be dropped by neither a fast nor a slow frame rate.
    /// </para>
    /// <para>
    /// A latch is consumed by exactly one tick. When a slow frame runs several ticks at once,
    /// the later ticks see the button as released, so one press can never become two dashes.
    /// </para>
    /// Aim is sampled rather than latched: it is a continuous value, and the freshest reading
    /// is always the correct one.
    /// </summary>
    public sealed class PlayerInputRouter : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("NeonVoidControls.inputactions. Action names follow docs/06 section 3.")]
        [SerializeField] private InputActionAsset actions;

        [Tooltip("Action map to enable for gameplay.")]
        [SerializeField] private string actionMapName = "Gameplay";

        [Header("Aim")]
        [Tooltip("Camera used to project the pointer onto the ground plane. Defaults to Camera.main.")]
        [SerializeField] private Camera aimCamera;

        [Tooltip("Height of the XZ plane the pointer is projected onto.")]
        [SerializeField] private float groundPlaneY;

        private InputActionMap _map;
        private InputAction _move, _aim, _dash, _skill1, _skill2, _skill3, _interact;

        private bool _dashLatched, _skill1Latched, _skill2Latched, _skill3Latched, _interactLatched;
        private Vector2 _lastAimWorldXZ;
        private bool _hasAim;

        /// <summary>True when the action asset resolved and gameplay input is live.</summary>
        public bool IsReady => _map != null;

        /// <summary>
        /// Set by UI code while a menu owns the keyboard. Movement and buttons are then
        /// reported as neutral, which is the docs/02 section 1 rule that combat input is
        /// blocked while UI is focused. Aim keeps updating so the character does not snap
        /// when the menu closes.
        /// </summary>
        public bool CombatInputSuspended { get; set; }

        private void Awake()
        {
            if (actions == null)
            {
                Debug.LogError($"{nameof(PlayerInputRouter)} has no InputActionAsset assigned; the player will not respond to input.", this);
                return;
            }

            _map = actions.FindActionMap(actionMapName, throwIfNotFound: false);
            if (_map == null)
            {
                Debug.LogError($"{nameof(PlayerInputRouter)}: action map '{actionMapName}' not found in {actions.name}.", this);
                return;
            }

            _move = _map.FindAction("Move", throwIfNotFound: false);
            _aim = _map.FindAction("Aim", throwIfNotFound: false);
            _dash = _map.FindAction("Dash", throwIfNotFound: false);
            _skill1 = _map.FindAction("Skill1", throwIfNotFound: false);
            _skill2 = _map.FindAction("Skill2", throwIfNotFound: false);
            _skill3 = _map.FindAction("Skill3", throwIfNotFound: false);
            _interact = _map.FindAction("Interact", throwIfNotFound: false);
        }

        private void OnEnable() => _map?.Enable();

        private void OnDisable() => _map?.Disable();

        /// <summary>
        /// Polls devices and latches any press. Call once per render frame, before ticks run.
        /// </summary>
        public void SampleFrame()
        {
            if (_map == null || CombatInputSuspended)
                return;

            _dashLatched |= _dash != null && _dash.WasPressedThisFrame();
            _skill1Latched |= _skill1 != null && _skill1.WasPressedThisFrame();
            _skill2Latched |= _skill2 != null && _skill2.WasPressedThisFrame();
            _skill3Latched |= _skill3 != null && _skill3.WasPressedThisFrame();
            _interactLatched |= _interact != null && _interact.WasPressedThisFrame();
        }

        /// <summary>
        /// The command handed out for the current tick, for anything that acts on the same
        /// input after movement has run.
        /// <para>
        /// A second caller must never call <see cref="ConsumeCommandForTick"/> itself: the
        /// latches are cleared there, so whichever caller arrived first would eat the press and
        /// the other would see nothing. One consumer builds the command, everyone else reads it.
        /// </para>
        /// </summary>
        public PlayerInputCommand LastConsumedCommand { get; private set; }

        /// <summary>
        /// Builds the command for one tick and consumes the latched presses.
        /// </summary>
        /// <param name="playerPositionXZ">Used only as the aim fallback when projection fails.</param>
        public PlayerInputCommand ConsumeCommandForTick(Vector2 playerPositionXZ)
        {
            Vector2 aim = ResolveAimWorldXZ(playerPositionXZ);

            if (_map == null || CombatInputSuspended)
            {
                PlayerInputCommand idle = PlayerInputCommand.Idle(aim);
                LastConsumedCommand = idle;
                return idle;
            }

            Vector2 move = _move != null ? _move.ReadValue<Vector2>() : Vector2.zero;

            var command = new PlayerInputCommand(
                moveXZ: move,
                aimWorldXZ: aim,
                dashPressed: _dashLatched,
                skill1Pressed: _skill1Latched,
                skill2Pressed: _skill2Latched,
                skill3Pressed: _skill3Latched,
                interactPressed: _interactLatched);

            _dashLatched = _skill1Latched = _skill2Latched = _skill3Latched = _interactLatched = false;
            LastConsumedCommand = command;
            return command;
        }

        /// <summary>
        /// Projects the pointer onto the XZ ground plane.
        /// <para>
        /// When the ray is parallel to the plane, or there is no camera or pointer, the last
        /// good aim is reused instead of producing a far-away or NaN point that would whip the
        /// character around for one tick.
        /// </para>
        /// </summary>
        private Vector2 ResolveAimWorldXZ(Vector2 playerPositionXZ)
        {
            Camera cam = aimCamera != null ? aimCamera : Camera.main;
            Vector2 pointer = _aim != null ? _aim.ReadValue<Vector2>() : Vector2.zero;

            if (cam != null && _aim != null)
            {
                Ray ray = cam.ScreenPointToRay(new Vector3(pointer.x, pointer.y, 0f));
                var ground = new Plane(Vector3.up, new Vector3(0f, groundPlaneY, 0f));
                if (ground.Raycast(ray, out float distance) && distance > 0f)
                {
                    Vector3 hit = ray.GetPoint(distance);
                    if (!float.IsNaN(hit.x) && !float.IsNaN(hit.z))
                    {
                        _lastAimWorldXZ = new Vector2(hit.x, hit.z);
                        _hasAim = true;
                    }
                }
            }

            return _hasAim ? _lastAimWorldXZ : playerPositionXZ + Vector2.up;
        }
    }
}
