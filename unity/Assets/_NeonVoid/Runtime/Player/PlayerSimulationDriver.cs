using NeonVoid.Combat;
using NeonVoid.Core;
using UnityEngine;

namespace NeonVoid.Player
{
    /// <summary>
    /// Runs the player's movement rules and reconciles them with Unity's collider.
    /// <para>
    /// This replaced <c>PrototypePlayerController</c>, which moved the character straight from
    /// Update with Time.deltaTime and therefore produced a different result at every frame rate.
    /// </para>
    /// <para>
    /// <b>Why there is no clock here any more.</b> This component used to own a private
    /// SimulationClock. That was fine while movement was the only thing simulated, but combat
    /// ticks too, and two private accumulators drift apart: each carries its own rounding, so
    /// movement and status durations stop agreeing about what tick it is. Ticks now arrive from
    /// the single <see cref="SimulationRunner"/> that docs/06 section 4 calls for.
    /// </para>
    /// <para>
    /// <b>The one place determinism stops.</b> The rules compute a desired displacement; the
    /// CharacterController resolves it against geometry and may return something else at a wall.
    /// The achieved position is written back into the state, so the rules and the transform
    /// never drift apart, but a position after collision is Unity's answer rather than a
    /// reproducible one. docs/02 section 1 requires stating that rather than assuming it away.
    /// </para>
    /// <para>
    /// <b>A dead player stops moving but keeps consuming input.</b> Dropping the tick outright
    /// would leave the router's latches holding whatever was pressed at the moment of death, so
    /// the first tick after any revive would spend a dash nobody asked for. The command is read
    /// and discarded instead.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerSimulationDriver : MonoBehaviour, ISimulationTickReceiver, IDashState
    {
        [SerializeField] private PlayerInputRouter inputRouter;

        [Tooltip("All values are [초안]; none is approved balance.")]
        [SerializeField] private PlayerMotionConfig motionConfig = new PlayerMotionConfig
        {
            MoveSpeedUnitsPerSecond = 5f,
            DashSpeedUnitsPerSecond = 14f,
            DashDurationTicks = 9,
            DashCooldownTicks = 45,
        };

        private CharacterController _character;
        private PlayerMotionState _state;
        private bool _bankedDash;
        private Vector2 _bankedDashDirection;
        private SimulationRunner _runner;

        // Optional: movement does not require combat. A driver with no actor beside it is
        // always treated as alive, so a movement-only test scene still works.
        private CombatActor _actor;

        /// <summary>Authoritative movement state as of the last completed tick.</summary>
        public PlayerMotionState State => _state;

        /// <summary>
        /// <see cref="IDashState"/>, so effects written against dashing can ask without this
        /// domain knowing they exist. It is the same flag <see cref="State"/> already carries.
        /// </summary>
        public bool IsDashing => _state.IsDashing;

        /// <summary>
        /// Raised on the tick a 대시 begins, for whatever wants to react to it.
        /// <para>
        /// An event rather than a flag a listener polls on the render frame: a poll samples
        /// <see cref="IsDashing"/> once per frame, and on a slow frame a dash that started and
        /// ended between two of them would never be noticed. The tick is where it happens.
        /// </para>
        /// <para>
        /// Raised after the state is written, so a listener reading <see cref="State"/> sees
        /// the dash that just started rather than the one before it.
        /// </para>
        /// </summary>
        public event System.Action<PlayerMotionState> DashStarted;

        /// <summary>
        /// Banks a 대시 for the next tick, in the direction the player is moving, as if the key
        /// had been pressed.
        /// <para>
        /// It exists because a key press is not the only thing that should be able to move the
        /// player - a skill, a plugin or a boss shove all end here - and because a dash the
        /// router is the only source of cannot be driven without a keyboard. The banked press
        /// is consumed by one tick, the same as a real one, so the cooldown and the duration
        /// still decide whether it actually happens.
        /// </para>
        /// </summary>
        public void RequestDash(Vector2 directionXZ)
        {
            // The direction comes with it because [확정] docs/08 C05 makes a dash follow the
            // movement vector and never the cursor - a banked press with no direction would
            // either do nothing or have to invent one.
            _bankedDash = true;
            _bankedDashDirection = directionXZ;
        }

        int ISimulationTickReceiver.TickOrder => SimulationRunner.Order.Player;

        private void Awake()
        {
            _character = GetComponent<CharacterController>();
            _actor = GetComponent<CombatActor>();

            try
            {
                motionConfig.Validate();
            }
            catch (System.InvalidOperationException error)
            {
                Debug.LogError($"{nameof(PlayerSimulationDriver)}: motion config rejected ({error.Message}). Falling back to the draft values.", this);
                motionConfig = PlayerMotionConfig.Draft();
            }

            Vector3 p = transform.position;
            Vector3 forward = transform.forward;
            _state = PlayerMotionState.Spawn(new Vector2(p.x, p.z), new Vector2(forward.x, forward.z));

            if (inputRouter == null)
                inputRouter = GetComponent<PlayerInputRouter>();
        }

        private void OnEnable()
        {
            _runner = _runner != null ? _runner : FindFirstObjectByType<SimulationRunner>();
            if (_runner == null)
            {
                Debug.LogError($"{name}: no SimulationRunner in the scene; the player will not move.", this);
                return;
            }
            _runner.Register(this);
        }

        private void OnDisable()
        {
            if (_runner != null)
                _runner.Unregister(this);
        }

        /// <summary>
        /// Devices are polled once per render frame, which is the only thing that still belongs
        /// in Update: a press must be latched before the ticks of this frame consume it.
        /// </summary>
        private void Update()
        {
            if (inputRouter != null)
                inputRouter.SampleFrame();

            ApplyFacingToTransform();
        }

        void ISimulationTickReceiver.OnSimulationTick(long tickIndex)
        {
            PlayerInputCommand command = inputRouter != null
                ? inputRouter.ConsumeCommandForTick(_state.PositionXZ)
                : PlayerInputCommand.Idle(_state.PositionXZ + Vector2.up);

            // Dead: the command above was read so no press is banked, and nothing else runs.
            // The CharacterController is disabled by ActorDeathReaction at this point, so
            // calling Move would warn as well as being wrong.
            if (_actor != null && !_actor.IsAlive)
                return;

            // The player obeys 구속 / 속박 / 정지 through the same function the enemies use.
            // Until this was passed, enemies respected a slow and the player did not.
            float speedMultiplier = _actor != null ? _actor.MoveSpeedMultiplier : 1f;

            if (_bankedDash)
            {
                command = command.WithDash(_bankedDashDirection);
                _bankedDash = false;
            }

            bool wasDashing = _state.IsDashing;
            _state = PlayerMotionSimulation.Step(
                _state, command, motionConfig, out Vector2 desired, speedMultiplier);

            // The rising edge. A dash that is still running is not a new one.
            bool dashBegan = _state.IsDashing && !wasDashing;

            _character.Move(new Vector3(desired.x, 0f, desired.y));

            // Reconcile: take the position the collider actually reached, so a wall stops the
            // simulation too instead of letting the rules walk through it on their own ledger.
            Vector3 resolved = transform.position;
            _state.PositionXZ = new Vector2(resolved.x, resolved.z);

            // Last, so the state a listener reads is the reconciled one.
            if (dashBegan)
                DashStarted?.Invoke(_state);
        }

        /// <summary>
        /// Moves the player to a position without travelling through what is in between, for a
        /// blink or teleport. The caller is responsible for the destination being free.
        /// <para>
        /// The simulation state is written as well as the transform, which is the whole reason
        /// this is a method rather than the caller setting the position itself. The rules keep
        /// their own position ledger, and leaving it behind would make the next tick resolve
        /// facing and the dash dead zone against where the player used to be.
        /// </para>
        /// <para>
        /// The CharacterController is switched off across the move: it resolves movement
        /// against geometry, and asking it to jump a distance in one frame is how a teleport
        /// ends up refused, halved, or inside a wall.
        /// </para>
        /// </summary>
        public void TeleportTo(Vector2 positionXZ)
        {
            if (float.IsNaN(positionXZ.x) || float.IsNaN(positionXZ.y))
                return;

            bool wasEnabled = _character != null && _character.enabled;
            if (_character != null)
                _character.enabled = false;

            Vector3 position = transform.position;
            transform.position = new Vector3(positionXZ.x, position.y, positionXZ.y);

            if (_character != null)
                _character.enabled = wasEnabled;

            _state.PositionXZ = positionXZ;
        }

        /// <summary>
        /// Facing is presentation, applied once per frame rather than once per tick: rotating
        /// the transform several times in one frame costs work nobody can see.
        /// </summary>
        private void ApplyFacingToTransform()
        {
            Vector2 facing = _state.FacingXZ;
            if (facing.sqrMagnitude <= PlayerMotionSimulation.MovementDeadZoneSqr)
                return;

            transform.rotation = Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.y), Vector3.up);
        }
    }
}
