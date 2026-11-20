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
    public sealed class PlayerSimulationDriver : MonoBehaviour, ISimulationTickReceiver
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
        private SimulationRunner _runner;

        // Optional: movement does not require combat. A driver with no actor beside it is
        // always treated as alive, so a movement-only test scene still works.
        private CombatActor _actor;

        /// <summary>Authoritative movement state as of the last completed tick.</summary>
        public PlayerMotionState State => _state;

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

            _state = PlayerMotionSimulation.Step(_state, command, motionConfig, out Vector2 desired);

            _character.Move(new Vector3(desired.x, 0f, desired.y));

            // Reconcile: take the position the collider actually reached, so a wall stops the
            // simulation too instead of letting the rules walk through it on their own ledger.
            Vector3 resolved = transform.position;
            _state.PositionXZ = new Vector2(resolved.x, resolved.z);
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
