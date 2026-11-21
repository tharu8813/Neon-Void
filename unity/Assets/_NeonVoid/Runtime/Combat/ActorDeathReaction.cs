using UnityEngine;

namespace NeonVoid.Combat
{
    /// <summary>
    /// What happens to a body in the scene when its actor dies: it stops taking part in
    /// physics, and it looks dead.
    /// <para>
    /// <b>Why the corpse stops colliding.</b> Until this existed a dead enemy kept its
    /// CharacterController, so it stayed a solid, invisible wall: the player could be cornered
    /// by something that could no longer be killed or pushed, and shots stopped on it. The
    /// rules said it was dead and the scene disagreed.
    /// </para>
    /// <para>
    /// <b>Why the body is not destroyed.</b> What a dead enemy leaves behind - a drop, a
    /// plugin card, nothing at all - is reward policy, and docs/04 puts rewards behind an
    /// approved save commit. Destroying the object here would quietly settle that question. The
    /// body is parked instead, which decides nothing.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(CombatActor))]
    public sealed class ActorDeathReaction : MonoBehaviour
    {
        [Header("Corpse")]
        [Tooltip("[초안] how flat the body is squashed. Presentation only.")]
        [SerializeField, Range(0.05f, 1f)] private float corpseHeightScale = 0.25f;

        [Tooltip("Optional. Assigned instead of the living material, as a shared material " +
                 "so no per-corpse instance is allocated.")]
        [SerializeField] private Material corpseMaterial;

        private CombatActor _actor;
        private bool _reacted;
        private Vector3 _livingScale;
        private Material _livingMaterial;
        private bool _remembered;

        private void Awake() => _actor = GetComponent<CombatActor>();

        private void OnEnable()
        {
            if (_actor == null)
                return;

            _actor.Died += OnDied;
            _actor.Revived += OnRevived;
        }

        private void OnDisable()
        {
            if (_actor == null)
                return;

            _actor.Died -= OnDied;
            _actor.Revived -= OnRevived;
        }

        private void OnDied(CombatActor actor)
        {
            // Died is documented as raised once, but a body may only be parked once in any
            // case: a second pass would squash an already squashed corpse.
            if (_reacted)
                return;
            _reacted = true;

            Remember();
            StopTakingPartInPhysics();
            LookDead();
        }

        /// <summary>
        /// Puts the body back: physics on, the living shape and material returned.
        /// <para>
        /// The living scale and material are remembered when the body is parked rather than
        /// read at Awake, because whatever set them may have done so after Awake - a generated
        /// area sizes its bodies as it spawns them - and a revive that restored a prefab's
        /// scale would resurrect the player at the wrong size.
        /// </para>
        /// </summary>
        private void OnRevived(CombatActor actor)
        {
            if (!_reacted)
                return;

            TakePartInPhysicsAgain();
            LookAliveAgain();
            _reacted = false;
        }

        private void Remember()
        {
            if (_remembered)
                return;

            _livingScale = transform.localScale;

            var renderer = GetComponent<Renderer>();
            _livingMaterial = renderer != null ? renderer.sharedMaterial : null;
            _remembered = true;
        }

        private void TakePartInPhysicsAgain()
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(includeInactive: true);
            for (int i = 0; i < colliders.Length; ++i)
                colliders[i].enabled = true;

            // Last, and after the colliders: a CharacterController that is enabled while its
            // own collider is off spends a frame able to walk through the world.
            var controller = GetComponent<CharacterController>();
            if (controller != null)
                controller.enabled = true;
        }

        private void LookAliveAgain()
        {
            if (!_remembered)
                return;

            Vector3 squashed = transform.localScale;
            Vector3 position = transform.position;
            position.y += _livingScale.y - squashed.y;
            transform.position = position;
            transform.localScale = _livingScale;

            if (_livingMaterial == null)
                return;

            var renderer = GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = _livingMaterial;
        }

        /// <summary>
        /// Disables the controller and every collider on the body. The CharacterController is a
        /// collider too, so leaving it enabled is what made corpses solid.
        /// </summary>
        private void StopTakingPartInPhysics()
        {
            var controller = GetComponent<CharacterController>();
            if (controller != null)
                controller.enabled = false;

            Collider[] colliders = GetComponentsInChildren<Collider>(includeInactive: true);
            for (int i = 0; i < colliders.Length; ++i)
                colliders[i].enabled = false;
        }

        private void LookDead()
        {
            Vector3 scale = transform.localScale;
            float livingHeight = scale.y;
            scale.y *= corpseHeightScale;
            transform.localScale = scale;

            // Squashing a centred primitive leaves it floating; drop it by what it lost.
            Vector3 position = transform.position;
            position.y -= livingHeight - scale.y;
            transform.position = position;

            if (corpseMaterial == null)
                return;

            var renderer = GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = corpseMaterial;
        }
    }
}
