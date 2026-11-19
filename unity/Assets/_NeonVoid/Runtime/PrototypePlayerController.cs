using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonVoid.Prototype
{
    /// <summary>
    /// Temporary graybox controller. Movement uses Update for a visual demo only;
    /// P0-02 MUST replace gameplay motion with the verified 60 Hz simulation.
    /// The speed and dash duration here are not approved balance constants.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PrototypePlayerController : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float dashSpeed = 14f;
        [SerializeField] private float dashDuration = 0.14f;
        [SerializeField] private float dashCooldown = 0.75f;

        private CharacterController _character;
        private Vector3 _dashDirection;
        private float _dashRemaining;
        private float _dashCooldownRemaining;

        private void Awake()
        {
            _character = GetComponent<CharacterController>();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            float horizontal = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f)
                             - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
            float vertical = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f)
                           - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
            Vector3 direction = new Vector3(horizontal, 0f, vertical).normalized;

            float dt = Time.deltaTime;
            _dashCooldownRemaining = Mathf.Max(0f, _dashCooldownRemaining - dt);

            // Movement-direction dash: mouse position must NEVER choose the dash vector.
            // Stationary dash remains unresolved; prototype chooses no dash when not moving.
            if (keyboard.spaceKey.wasPressedThisFrame && direction.sqrMagnitude > 0.0001f
                && _dashRemaining <= 0f && _dashCooldownRemaining <= 0f)
            {
                _dashDirection = direction;
                _dashRemaining = dashDuration;
                _dashCooldownRemaining = dashCooldown;
            }

            Vector3 velocity;
            if (_dashRemaining > 0f)
            {
                velocity = _dashDirection * dashSpeed;
                _dashRemaining = Mathf.Max(0f, _dashRemaining - dt);
            }
            else
            {
                velocity = direction * moveSpeed;
            }
            _character.Move(velocity * dt);

            // Aim independently of camera follow. Raycast against the XZ ground plane.
            Camera cam = Camera.main;
            Mouse mouse = Mouse.current;
            if (cam == null || mouse == null)
                return;
            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            Plane ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float rayDistance))
            {
                Vector3 aim = ray.GetPoint(rayDistance) - transform.position;
                aim.y = 0f;
                if (aim.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(aim.normalized, Vector3.up);
            }
        }
    }
}
