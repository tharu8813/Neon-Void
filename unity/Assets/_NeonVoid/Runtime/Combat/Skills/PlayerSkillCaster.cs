using System.Collections.Generic;
using NeonVoid.Core;
using NeonVoid.Player;
using UnityEngine;

namespace NeonVoid.Combat.Skills
{
    /// <summary>
    /// Turns Q/E/R into casts and casts into effects.
    /// <para>
    /// The decision whether a cast may happen is NOT made here: it belongs to
    /// <see cref="SkillLoadout.TryCast"/>, which checks the control gate, the cooldown and the
    /// mana in one tested order. This component only asks, and builds the effect afterwards.
    /// Re-checking any of it here is how a build ends up where one path lets a silenced player
    /// cast.
    /// </para>
    /// <para>
    /// [미확정] D-009 leaves the keys open; Q/E/R are the document's own placeholder and the
    /// bindings live in the input asset, so changing them is not a code change.
    /// </para>
    /// <para>
    /// Ticks after the movement driver so a cast resolves against the position the player
    /// actually reached this tick, and reads the command the driver already consumed rather
    /// than consuming its own - the latches are cleared on consumption, so two consumers would
    /// each swallow presses meant for the other.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(CombatActor))]
    public sealed class PlayerSkillCaster : MonoBehaviour, ISimulationTickReceiver
    {
        [Header("Wiring")]
        [SerializeField] private PlayerInputRouter inputRouter;
        [SerializeField] private PlayerSimulationDriver motionDriver;

        [Tooltip("One asset per active slot, in Q/E/R order. Empty slots are allowed.")]
        [SerializeField] private SkillAsset[] slotSkills = new SkillAsset[DesignConstants.ActiveSkillSlots];

        [Header("Presentation")]
        [SerializeField] private Material fieldMaterial;

        private CombatActor _actor;
        private SimulationRunner _runner;
        private readonly HashSet<int> _hitThisCast = new HashSet<int>();

        /// <summary>The last cast attempt, successful or refused, for the HUD and for tests.</summary>
        public SkillCastResult LastResult { get; private set; }

        int ISimulationTickReceiver.TickOrder => SimulationRunner.Order.Player + 1;

        private void Awake()
        {
            _actor = GetComponent<CombatActor>();
            if (inputRouter == null)
                inputRouter = GetComponent<PlayerInputRouter>();
            if (motionDriver == null)
                motionDriver = GetComponent<PlayerSimulationDriver>();
        }

        /// <summary>
        /// Equipping happens in Start rather than OnEnable: the loadout it writes into is
        /// created in CombatActor.Awake, and Start is the first callback guaranteed to run
        /// after every Awake in the scene.
        /// </summary>
        private void Start() => EquipConfiguredSkills();

        private void OnEnable()
        {
            _runner = _runner != null ? _runner : FindFirstObjectByType<SimulationRunner>();
            if (_runner == null)
            {
                Debug.LogError($"{name}: no SimulationRunner; skills will never be castable.", this);
                return;
            }
            _runner.Register(this);
        }

        private void OnDisable()
        {
            if (_runner != null)
                _runner.Unregister(this);
        }

        private void EquipConfiguredSkills()
        {
            if (_actor == null || slotSkills == null)
                return;

            int slots = Mathf.Min(slotSkills.Length, _actor.Skills.SlotCount);
            for (int i = 0; i < slots; ++i)
            {
                SkillAsset asset = slotSkills[i];
                if (asset == null)
                {
                    _actor.Skills.Clear(i);
                    continue;
                }

                try
                {
                    asset.Validate();
                    _actor.Skills.Equip(i, asset.Definition);
                }
                catch (System.Exception error)
                {
                    // Named at the asset, not at the effect that would misbehave later.
                    Debug.LogError(
                        $"{name}: skill asset {asset.DocumentKey} rejected and left unequipped ({error.Message}).", asset);
                    _actor.Skills.Clear(i);
                }
            }
        }

        void ISimulationTickReceiver.OnSimulationTick(long tickIndex)
        {
            if (_actor == null || !_actor.IsAlive || inputRouter == null)
                return;

            PlayerInputCommand command = inputRouter.LastConsumedCommand;

            if (command.Skill1Pressed)
                TryCastSlot(0, command);
            if (command.Skill2Pressed)
                TryCastSlot(1, command);
            if (command.Skill3Pressed)
                TryCastSlot(2, command);
        }

        private void TryCastSlot(int slotIndex, in PlayerInputCommand command)
        {
            SkillAsset asset = slotSkills != null && slotIndex >= 0 && slotIndex < slotSkills.Length
                ? slotSkills[slotIndex]
                : null;

            // A teleport that cannot land must not be charged for. Geometry is checked before
            // the cast, because 벽 속 이동 금지 should feel like a skill that was not cast
            // rather than one that was spent and did nothing.
            Vector3 blinkDestination = default;
            bool isBlink = asset != null && asset.Shape.Kind == SkillEffectKind.Teleport;
            if (isBlink && !TryFindBlinkDestination(asset, command, out blinkDestination))
            {
                Debug.Log($"{asset.DisplayName}: nowhere to blink to; the cast was not spent.", this);
                return;
            }

            SkillCastResult result = _actor.TryCastSkill(slotIndex);
            LastResult = result;

            if (!result.Succeeded)
            {
                // The only feedback a prototype has. docs/05's HUD is what eventually shows it.
                Debug.Log($"Skill slot {slotIndex + 1} refused: {result.Rejection}.", this);
                return;
            }

            if (asset == null)
                return;

            switch (asset.Shape.Kind)
            {
                case SkillEffectKind.ConeDamage:
                    CastCone(asset, command);
                    break;
                case SkillEffectKind.PersistentField:
                    CastField(asset);
                    break;
                case SkillEffectKind.Teleport:
                    if (motionDriver != null)
                        motionDriver.TeleportTo(new Vector2(blinkDestination.x, blinkDestination.z));
                    break;
            }
        }

        private void CastCone(SkillAsset asset, in PlayerInputCommand command)
        {
            Vector3 self = transform.position;
            var selfXZ = new Vector2(self.x, self.z);

            // Cursor aiming, which is docs/03's verification point for this skill: the cone
            // points at the cursor, not at the direction the body happens to face.
            Vector2 facing = command.AimWorldXZ - selfXZ;
            if (facing.sqrMagnitude <= 1e-6f)
                facing = new Vector2(transform.forward.x, transform.forward.z);

            SkillShape shape = asset.Shape;
            _hitThisCast.Clear();
            Collider[] candidates = Physics.OverlapSphere(
                self, shape.RadiusUnits, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < candidates.Length; ++i)
            {
                var actor = candidates[i].GetComponentInParent<CombatActor>();
                if (actor == null || !actor.IsAlive || !_actor.IsHostileTo(actor))
                    continue;
                if (!_hitThisCast.Add(actor.ActorId))
                    continue;

                Vector3 p = actor.transform.position;
                if (!ConeTargeting.IsInsideCone(
                        selfXZ, facing, new Vector2(p.x, p.z), shape.RadiusUnits, shape.ConeHalfAngleDegrees))
                    continue;

                DamageOutcome outcome = actor.ReceiveDamage(asset.Definition.Damage, _actor.Stats);
                if (outcome.Lifesteal.Total > 0f)
                    _actor.Heal(outcome.Lifesteal.Total);

                SkillStatusEffect status = asset.AppliedStatus;
                if (status.Applies)
                    actor.ApplyStatus(status.Id, status.Magnitude, 0f, status.DurationTicksOverride);
            }
        }

        private void CastField(SkillAsset asset)
        {
            SkillShape shape = asset.Shape;

            var field = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            field.name = $"NV Field ({asset.DocumentKey})";
            field.transform.position = new Vector3(transform.position.x, 0.03f, transform.position.z);
            // A cylinder primitive is 2 units tall, so 0.02 makes a disc on the floor.
            field.transform.localScale = new Vector3(shape.RadiusUnits * 2f, 0.02f, shape.RadiusUnits * 2f);

            // The visual must not take part in physics: the field does its own overlap query,
            // and a collider here would shove the player and stop their shots.
            Object.Destroy(field.GetComponent<Collider>());

            if (fieldMaterial != null)
            {
                var renderer = field.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.sharedMaterial = fieldMaterial;
            }

            field.AddComponent<PersistentDamageField>().Begin(
                _actor, asset.Definition.Damage, asset.AppliedStatus, shape.RadiusUnits, shape.Field);
        }

        /// <summary>
        /// Finds a free spot along the blink path, from the furthest point back towards the
        /// caster. Walking the path backwards means a blink at a wall lands against it instead
        /// of being refused outright, while a blink with no room at all is refused.
        /// </summary>
        private bool TryFindBlinkDestination(
            SkillAsset asset, in PlayerInputCommand command, out Vector3 destination)
        {
            Vector3 self = transform.position;
            var selfXZ = new Vector2(self.x, self.z);
            Vector2 wanted = TeleportMath.Resolve(
                selfXZ, command.AimWorldXZ, asset.Shape.TeleportMaxDistanceUnits);

            destination = self;
            Vector2 offset = wanted - selfXZ;
            float distance = offset.magnitude;
            if (distance <= 1e-5f)
                return false;

            Vector2 direction = offset / distance;
            const float stepUnits = 0.5f;
            const float probeRadius = 0.4f;

            for (float travelled = distance; travelled >= stepUnits; travelled -= stepUnits)
            {
                Vector2 candidateXZ = selfXZ + direction * travelled;
                var candidate = new Vector3(candidateXZ.x, self.y, candidateXZ.y);

                if (IsBlocked(candidate, probeRadius))
                    continue;

                destination = candidate;
                return true;
            }

            return false;
        }

        private bool IsBlocked(Vector3 point, float radius)
        {
            Collider[] overlaps = Physics.OverlapSphere(point, radius, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < overlaps.Length; ++i)
            {
                // The caster's own body is not an obstacle to the caster.
                if (overlaps[i].transform == transform || overlaps[i].transform.IsChildOf(transform))
                    continue;
                return true;
            }
            return false;
        }
    }
}
