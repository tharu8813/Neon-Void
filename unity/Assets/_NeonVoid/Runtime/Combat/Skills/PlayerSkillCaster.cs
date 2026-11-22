using System;
using System.Collections.Generic;
using NeonVoid.Combat.Status;
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

        /// <summary>
        /// Raised once per skill that actually went off, never for a refused one.
        /// <para>
        /// docs/03 writes three cards against casting rather than against hitting - G08 이동
        /// 버퍼 after a 점멸/순간이동, G07 실행 차폐 on 스킬 사용 시, R05 돌진 차폐 after a
        /// 돌진 또는 점멸 - and G07's exception column says 스킬 실패 시 발동하지 않음 outright.
        /// So this fires after the gate, the mana and a blink's destination have all been
        /// settled, which is the only point at which "the skill was used" is true.
        /// </para>
        /// <para>
        /// An event rather than a reference the other way round: the Plugins domain listens to
        /// Combat exactly as it already does for <see cref="CombatActor.HitLanded"/>, and
        /// Combat stays unaware that plugins exist.
        /// </para>
        /// </summary>
        public event Action<SkillDefinition> SkillCast;

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

        /// <summary>
        /// Puts a skill in a slot at runtime and equips it into the actor's loadout.
        /// <para>
        /// The hub screen that lets a player choose their three skills needs exactly this, and
        /// docs/00 has 임무 중 새로 얻은 스킬만 즉시 교체 - skills change mid-mission. So this
        /// is the loadout path, not a test hook, and PlayMode tests use it as such.
        /// </para>
        /// </summary>
        public void Equip(int slotIndex, SkillAsset asset)
        {
            if (slotSkills == null || slotIndex < 0 || slotIndex >= slotSkills.Length)
            {
                Debug.LogError($"{name}: slot {slotIndex} is outside the active slots.", this);
                return;
            }

            slotSkills[slotIndex] = asset;

            if (_actor == null)
                return;

            if (asset == null)
            {
                _actor.Skills.Clear(slotIndex);
                return;
            }

            try
            {
                asset.Validate();
                _actor.Skills.Equip(slotIndex, asset.Definition);
            }
            catch (System.Exception error)
            {
                Debug.LogError($"{name}: skill asset {asset.DocumentKey} rejected ({error.Message}).", asset);
                _actor.Skills.Clear(slotIndex);
            }
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

        /// <summary>
        /// Casts a slot at a point on the ground. This is the real entry point; the tick above
        /// only decides which slot a press means.
        /// <para>
        /// Separating the two is what makes a cast testable in Play mode without simulating a
        /// keyboard: a test aims and casts exactly as the tick does, and the effect it produces
        /// is the effect the game produces.
        /// </para>
        /// </summary>
        public SkillCastResult CastSlot(int slotIndex, Vector2 aimWorldXZ)
        {
            TryCastSlot(slotIndex, new PlayerInputCommand(Vector2.zero, aimWorldXZ));
            return LastResult;
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
                LastResult = SkillCastResult.Refused(
                    SkillCastRejection.NoValidDestination, asset.Definition.SkillId);
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
                case SkillEffectKind.SelfHeal:
                    CastSelfHeal(asset);
                    break;
                case SkillEffectKind.SelfShield:
                    CastSelfShield(asset);
                    break;
                case SkillEffectKind.CleanseControl:
                    CastCleanse(asset);
                    break;
                case SkillEffectKind.SingleTarget:
                    CastSingleTarget(asset);
                    break;
            }

            // After the effect, so a listener that reads the world sees what the skill did -
            // a blink has already moved the player by the time this fires.
            SkillCast?.Invoke(asset.Definition);
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

                DamageOutcome outcome = actor.ReceiveDamage(
                    asset.Definition.Damage, _actor.Stats, _actor.RollCrit(asset.Definition.Damage));
                if (outcome.Lifesteal.Total > 0f)
                    _actor.Heal(outcome.Lifesteal.Total);

                _actor.NotifyHitLanded(actor, asset.Definition.Damage, outcome, HitOrigin.Skill);

                SkillStatusEffect status = asset.AppliedStatus;
                if (status.Applies)
                    actor.ApplyStatus(status.Id, status.Magnitude, 0f, status.DurationTicksOverride);
            }
        }

        /// <summary>
        /// Heals the caster. The amount comes from the same formula the damage does, so a
        /// skill's strength scales with the same stats whichever direction it points.
        /// <para>
        /// A cast at full health is still spent. docs/03 lists 이미 full HP인 상태 as a case to
        /// handle and does not say how: refusing would need a new cast rule, and inventing one
        /// here would put a per-skill exception in the shared gate. So the mana goes, nothing
        /// is healed, and the decision stays open.
        /// </para>
        /// </summary>
        private void CastSelfHeal(SkillAsset asset)
        {
            float amount = DamageMath.RawDamage(asset.Definition.Damage, _actor.Stats, critRolled: false);
            if (amount <= 0f)
                return;

            float healed = _actor.Heal(amount);
            if (healed <= 0f)
                Debug.Log($"{asset.DisplayName}: already at full health; the cast was spent anyway.", this);
        }

        /// <summary>
        /// Gives the caster a 보호막 that lasts the shape's field duration.
        /// <para>
        /// Granted through <see cref="ShieldStack"/> like every other shield, so it obeys
        /// [확정] D-004 without this knowing any of it: it stacks as its own layer beside one a
        /// plugin gave, it is spent before older ones, and it still absorbs on the tick it
        /// expires.
        /// </para>
        /// </summary>
        private void CastSelfShield(SkillAsset asset)
        {
            float amount = DamageMath.RawDamage(asset.Definition.Damage, _actor.Stats, critRolled: false);
            if (amount <= 0f)
                return;

            long now = _runner != null ? _runner.TickIndex : 0L;
            _actor.Shields.Grant(
                asset.Definition.SkillId.ToString(), amount, now, asset.Shape.Field.DurationTicks);
        }

        /// <summary>
        /// Removes the control effects this skill names from the caster.
        /// <para>
        /// [확정 경계] docs/03 flags 사용불가 디버프 경계 for this skill, and the boundary is
        /// real: 정지 and 제한 stop the cast happening at all, so a cleanse can never remove
        /// either of them. It is not listed as removing them, because a listed effect that
        /// could never be reached is the silent kind of lie. What it does reach is 구속 and
        /// 기능고장 - and 속박, since a cleanse is not a movement skill.
        /// </para>
        /// </summary>
        private void CastCleanse(SkillAsset asset)
        {
            StatusEffectId[] cleansed = asset.Shape.CleansedEffects;
            if (cleansed == null)
                return;

            int removed = 0;
            for (int i = 0; i < cleansed.Length; ++i)
                removed += _actor.Status.Remove(cleansed[i]);

            if (removed > 0)
                Debug.Log($"{asset.DisplayName}: cleared {removed} control effect(s).", this);
        }

        /// <summary>
        /// Damages the nearest hostile in range, with no cone to aim. The cursor still chooses
        /// which way the player is looking, but a single-target skill that demanded accurate
        /// aiming would be a worse cone rather than a different skill.
        /// </summary>
        private void CastSingleTarget(SkillAsset asset)
        {
            Vector3 self = transform.position;
            Collider[] candidates = Physics.OverlapSphere(
                self, asset.Shape.RadiusUnits, ~0, QueryTriggerInteraction.Ignore);

            CombatActor best = null;
            float bestDistanceSqr = float.MaxValue;
            int bestId = int.MaxValue;
            _hitThisCast.Clear();

            for (int i = 0; i < candidates.Length; ++i)
            {
                var actor = candidates[i].GetComponentInParent<CombatActor>();
                if (actor == null || !actor.IsAlive || !_actor.IsHostileTo(actor))
                    continue;
                if (!_hitThisCast.Add(actor.ActorId))
                    continue;

                float distanceSqr = (actor.transform.position - self).sqrMagnitude;

                // Ties broken by the lower actor id rather than by collider order, which the
                // physics query does not promise to repeat.
                bool closer = distanceSqr < bestDistanceSqr - 1e-5f;
                bool tiedButLower = Mathf.Abs(distanceSqr - bestDistanceSqr) <= 1e-5f && actor.ActorId < bestId;
                if (!closer && !tiedButLower)
                    continue;

                best = actor;
                bestDistanceSqr = distanceSqr;
                bestId = actor.ActorId;
            }

            if (best == null)
                return;

            DamageOutcome outcome = best.ReceiveDamage(
                asset.Definition.Damage, _actor.Stats, _actor.RollCrit(asset.Definition.Damage));

            if (outcome.Lifesteal.Total > 0f)
                _actor.Heal(outcome.Lifesteal.Total);

            _actor.NotifyHitLanded(best, asset.Definition.Damage, outcome, HitOrigin.Skill);

            SkillStatusEffect status = asset.AppliedStatus;
            if (status.Applies)
                best.ApplyStatus(status.Id, status.Magnitude, 0f, status.DurationTicksOverride);
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
            UnityEngine.Object.Destroy(field.GetComponent<Collider>());

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
