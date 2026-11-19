using NeonVoid.Core;
using UnityEngine;

namespace NeonVoid.Player
{
    /// <summary>
    /// Runs the player's 60 Hz simulation and reconciles it with Unity's collider.
    /// <para>
    /// This is the replacement for <c>PrototypePlayerController</c>, which moved the character
    /// straight from <c>Update</c> with <c>Time.deltaTime</c> and therefore produced a
    /// different result at every frame rate.
    /// </para>
    /// <para>
    /// <b>Why ticks run from Update and not FixedUpdate.</b> FixedUpdate's rate is a project
    /// setting; binding gameplay to it would let that setting change combat results.
    /// <see cref="SimulationClock"/> owns the cadence instead, so the tick length is a constant
    /// no scene or settings file can alter.
    /// </para>
    /// <para>
    /// <b>The one place determinism stops.</b> The rules compute a desired displacement; the
    /// CharacterController resolves it against geometry and may return something else when the
    /// player hits a wall. The achieved position is written back into the simulation state, so
    /// the rules and the transform never drift apart — but position after a collision is
    /// Unity's answer, not a reproducible one. docs/02 section 1 requires stating this rather
    /// than claiming full determinism.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerSimulationDriver : MonoBehaviour
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

        [Tooltip("Ticks a single frame may run while catching up after a stall.")]
        [SerializeField, Min(1)] private int maxTicksPerFrame = 8;

        private CharacterController _character;
        private SimulationClock _clock;
        private PlayerMotionState _state;

        /// <summary>Authoritative movement state as of the last completed tick.</summary>
        public PlayerMotionState State => _state;

        /// <summary>Logic ticks run so far. The simulation's only time axis.</summary>
        public long TickIndex => _clock?.TickIndex ?? 0L;

        /// <summary>Ticks abandoned because a frame exceeded the catch-up budget.</summary>
        public long DroppedTicks => _clock?.DroppedTicks ?? 0L;

        private void Awake()
        {
            _character = GetComponent<CharacterController>();
            _clock = new SimulationClock(maxTicksPerFrame);

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
            _state = PlayerMotionState.Spawn(
                new Vector2(p.x, p.z),
                new Vector2(forward.x, forward.z));

            if (inputRouter == null)
                inputRouter = GetComponent<PlayerInputRouter>();
        }

        private void Update()
        {
            if (inputRouter != null)
                inputRouter.SampleFrame();

            int ticks = _clock.Advance(Time.deltaTime);
            for (int i = 0; i < ticks; ++i)
                RunOneTick();

            ApplyFacingToTransform();
        }

        private void RunOneTick()
        {
            PlayerInputCommand command = inputRouter != null
                ? inputRouter.ConsumeCommandForTick(_state.PositionXZ)
                : PlayerInputCommand.Idle(_state.PositionXZ + Vector2.up);

            _state = PlayerMotionSimulation.Step(_state, command, motionConfig, out Vector2 desired);

            _character.Move(new Vector3(desired.x, 0f, desired.y));

            // Reconcile: take the position the collider actually reached, so a wall stops the
            // simulation too instead of letting the rules walk through it on their own ledger.
            Vector3 resolved = transform.position;
            _state.PositionXZ = new Vector2(resolved.x, resolved.z);
        }

        /// <summary>
        /// Facing is presentation applied once per frame, not once per tick: rotating the
        /// transform several times in one frame would cost work nobody can see.
        /// </summary>
        private void ApplyFacingToTransform()
        {
            Vector2 facing = _state.FacingXZ;
            if (facing.sqrMagnitude <= PlayerMotionSimulation.MovementDeadZoneSqr)
                return;

            transform.rotation = Quaternion.LookRotation(
                new Vector3(facing.x, 0f, facing.y), Vector3.up);
        }
    }
}
