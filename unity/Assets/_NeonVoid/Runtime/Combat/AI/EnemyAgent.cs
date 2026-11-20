using System.Collections.Generic;
using NeonVoid.Combat.Projectiles;
using NeonVoid.Combat.Status;
using NeonVoid.Core;
using UnityEngine;

namespace NeonVoid.Combat.AI
{
    /// <summary>
    /// Puts <see cref="EnemyBrain"/> in the scene: gathers perception, applies the movement the
    /// brain asked for, and turns an attack intent into a real hit.
    /// <para>
    /// [확정] docs/02 section 8: the attack goes through <c>CombatActor.ReceiveDamage</c>, the
    /// same entry point the player's own shots use - directly for a contact attacker, and
    /// through a projectile for a firing one. That is what makes an enemy hit respect the
    /// player's armour, invulnerability and status exactly as a player hit respects an enemy's.
    /// </para>
    /// <para>
    /// Everything that differs between archetypes is data: <see cref="EnemyArchetype"/> says how
    /// it thinks, how fast it moves and how it attacks. CLAUDE.md forbids treating the five
    /// hardware zones as one copy-pasted map, and that starts with the enemies not all being
    /// the same enemy.
    /// </para>
    /// <para>
    /// Movement runs on the logic tick through <see cref="SimulationRunner"/>, so enemies close
    /// distance at the same speed on every machine.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(CombatActor))]
    [RequireComponent(typeof(CharacterController))]
    public sealed class EnemyAgent : MonoBehaviour, ISimulationTickReceiver
    {
        [Header("Archetype")]
        [Tooltip("Which of docs/01's archetypes this is. All values are [초안].")]
        [SerializeField] private EnemyArchetypeId archetypeId = EnemyArchetypeId.Chaser;

        [Tooltip("Apply the archetype's stat block to this actor on spawn.")]
        [SerializeField] private bool applyArchetypeStats = true;

        [Header("Presentation")]
        [Tooltip("Material for this enemy's projectiles, where it fires any.")]
        [SerializeField] private Material projectileMaterial;

        private CombatActor _self;
        private CharacterController _character;
        private SimulationRunner _runner;
        private EnemyBrainState _state;
        private EnemyArchetype _archetype;
        private readonly List<CombatActor> _knownActors = new List<CombatActor>();
        private PerceivedActor[] _perceptionBuffer = System.Array.Empty<PerceivedActor>();
        private readonly HashSet<int> _supportedThisRound = new HashSet<int>();

        /// <summary>Last decision, for inspection and debugging.</summary>
        public EnemyIntent LastIntent { get; private set; }

        /// <summary>What this enemy is, for tests and for the debug overlay.</summary>
        public EnemyArchetype Archetype => _archetype;

        int ISimulationTickReceiver.TickOrder => SimulationRunner.Order.Actors + 1;

        private void Awake()
        {
            _self = GetComponent<CombatActor>();
            _character = GetComponent<CharacterController>();
            _state = EnemyBrainState.Fresh();

            // Resolved here so the brain config exists before the first tick, but the stats
            // are NOT written yet: Awake order between components on one GameObject is not
            // guaranteed, so CombatActor.Awake could run afterwards and overwrite them from
            // the serialized fields - silently discarding the archetype.
            _archetype = Validated(EnemyArchetypeCatalog.Of(archetypeId));
        }

        /// <summary>Stats are applied here, where every Awake in the scene has already run.</summary>
        private void Start()
        {
            if (applyArchetypeStats && _self != null)
                _self.Configure(_self.TeamId, _archetype.Stats());
        }

        /// <summary>
        /// Becomes an archetype, stats included. Public because a spawner picks the archetype
        /// per mission, and the scene's own enemies are only the prototype's stand-ins. Safe to
        /// call at any point after construction, unlike the serialized path above.
        /// </summary>
        public void ApplyArchetype(in EnemyArchetype archetype)
        {
            _archetype = Validated(archetype);

            if (_self != null)
                _self.Configure(_self.TeamId, _archetype.Stats());
        }

        private EnemyArchetype Validated(in EnemyArchetype archetype)
        {
            try
            {
                archetype.Validate();
                return archetype;
            }
            catch (System.InvalidOperationException error)
            {
                Debug.LogError(
                    $"{name}: archetype rejected ({error.Message}). Falling back to the chaser.", this);
                return EnemyArchetypeCatalog.Chaser();
            }
        }

        private void OnEnable()
        {
            RefreshKnownActors();

            _runner = _runner != null ? _runner : FindFirstObjectByType<SimulationRunner>();
            if (_runner == null)
            {
                Debug.LogError($"{name}: no SimulationRunner in the scene; this enemy will never act.", this);
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
        /// Rebuilds the hostile list. Called on enable rather than every tick: scanning the
        /// scene per enemy per tick is quadratic, and the prototype scene's roster does not
        /// change mid-run. A spawner must call this when it adds an actor.
        /// </summary>
        public void RefreshKnownActors()
        {
            _knownActors.Clear();
            foreach (CombatActor actor in FindObjectsByType<CombatActor>(FindObjectsSortMode.None))
                if (actor != null && _self != null && _self.IsHostileTo(actor))
                    _knownActors.Add(actor);

            // Sorted by actor id so perception order is stable and the decision is reproducible.
            _knownActors.Sort((a, b) => a.ActorId.CompareTo(b.ActorId));

            if (_perceptionBuffer.Length != _knownActors.Count)
                _perceptionBuffer = new PerceivedActor[_knownActors.Count];
        }

        void ISimulationTickReceiver.OnSimulationTick(long tickIndex)
        {
            if (_self == null || !_self.IsAlive)
                return;

            EnemyIntent intent = EnemyBrain.Decide(ref _state, BuildPerception(), _archetype.Brain);
            LastIntent = intent;

            if (intent.MoveXZ.sqrMagnitude > 1e-6f && _archetype.MoveSpeedUnitsPerSecond > 0f)
            {
                Vector2 step = intent.MoveXZ.normalized
                               * (_archetype.MoveSpeedUnitsPerSecond * intent.SpeedMultiplier
                                  * (float)SimulationClock.TickDeltaSeconds);
                _character.Move(new Vector3(step.x, 0f, step.y));
            }

            if (intent.FaceXZ.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(
                    new Vector3(intent.FaceXZ.x, 0f, intent.FaceXZ.y), Vector3.up);

            if (intent.WantsAttack)
                ExecuteAttack(intent.TargetActorId);

            if (intent.WantsSupport)
                SupportAllies();
        }

        /// <summary>
        /// Hands the archetype's buff to every living ally in reach, itself excluded.
        /// <para>
        /// Excluded on purpose. A support that buffed itself would just be a tougher enemy,
        /// while one whose entire contribution is other people's damage makes killing it first
        /// a real decision - which is the 우선 처치 docs/01 asks this archetype to teach.
        /// </para>
        /// <para>
        /// The buff goes through <c>CombatActor.ApplyStatus</c>, the same entry point the
        /// player's skills use, so an ally's own tenacity and the catalogue's stacking policy
        /// apply exactly as they would to anything else.
        /// </para>
        /// </summary>
        private void SupportAllies()
        {
            if (_archetype.SupportStatus == StatusEffectId.None || _archetype.SupportRadiusUnits <= 0f)
                return;

            _supportedThisRound.Clear();
            Collider[] nearby = Physics.OverlapSphere(
                transform.position, _archetype.SupportRadiusUnits, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < nearby.Length; ++i)
            {
                var ally = nearby[i].GetComponentInParent<CombatActor>();
                if (ally == null || ally == _self || !ally.IsAlive)
                    continue;
                if (_self.IsHostileTo(ally))
                    continue;
                if (!_supportedThisRound.Add(ally.ActorId))
                    continue;

                ally.ApplyStatus(_archetype.SupportStatus, _archetype.SupportMagnitude);
            }
        }

        private EnemyPerception BuildPerception()
        {
            bool needsSight = _archetype.Brain.RequiresLineOfSight;
            int written = 0;

            for (int i = 0; i < _knownActors.Count; ++i)
            {
                CombatActor actor = _knownActors[i];
                if (actor == null)
                    continue;

                Vector3 p = actor.transform.position;

                // Only cast when the archetype's decision depends on it. A contact attacker is
                // already limited by having to reach its target, so the ray would be waste.
                bool visible = !needsSight || HasLineOfSight(actor);

                _perceptionBuffer[written++] = new PerceivedActor(
                    actor.ActorId, new Vector2(p.x, p.z), actor.IsAlive, visible);
            }

            // Destroyed actors leave gaps; hand the brain only the part that is filled.
            PerceivedActor[] candidates = _perceptionBuffer;
            if (written != _perceptionBuffer.Length)
            {
                candidates = new PerceivedActor[written];
                System.Array.Copy(_perceptionBuffer, candidates, written);
            }

            StatusContainer status = _self.Status;
            Vector3 self = transform.position;

            return new EnemyPerception(
                new Vector2(self.x, self.z),
                candidates,
                stunned: status.Has(StatusEffectId.Stun),
                rooted: status.Has(StatusEffectId.Root),
                // The same function the player's movement reads, so 구속 cannot come to mean
                // one thing for an enemy and another for the player.
                speedMultiplier: _self.MoveSpeedMultiplier);
        }

        /// <summary>
        /// Whether nothing solid stands between this enemy and the target.
        /// <para>
        /// The ray starts past this enemy's own radius: a ray beginning inside a collider is
        /// not reliably reported, so an enemy would decide it could not see anything, itself
        /// included, and stand there closing in forever.
        /// </para>
        /// </summary>
        private bool HasLineOfSight(CombatActor target)
        {
            Vector3 origin = transform.position;
            Vector3 toTarget = target.transform.position - origin;
            float distance = toTarget.magnitude;
            if (distance <= 1e-3f)
                return true;

            Vector3 direction = toTarget / distance;
            float skip = (_character != null ? _character.radius : 0f) + 0.05f;
            if (distance <= skip)
                return true;

            if (!Physics.Raycast(
                    origin + direction * skip, direction, out RaycastHit hit,
                    distance - skip, ~0, QueryTriggerInteraction.Ignore))
                return true;     // nothing in between at all

            // The first thing the ray meets has to be the target. Another enemy in the way
            // counts as blocking, which is deliberately STRICTER than the projectile itself -
            // a shot passes through friendly actors. An archetype that fired anyway would be
            // shooting past its own, and the brain's answer to a blocked line is to reposition,
            // which is the behaviour worth having.
            var blocker = hit.collider.GetComponentInParent<CombatActor>();
            return blocker != null && blocker.ActorId == target.ActorId;
        }

        /// <summary>
        /// Turns the intent into a hit. The brain decided to attack; it did not decide how much
        /// that costs the target, and nothing here touches health directly either.
        /// </summary>
        private void ExecuteAttack(int targetActorId)
        {
            for (int i = 0; i < _knownActors.Count; ++i)
            {
                CombatActor target = _knownActors[i];
                if (target == null || target.ActorId != targetActorId || !target.IsAlive)
                    continue;

                var request = new DamageRequest
                {
                    AdRatio = _archetype.AdRatio,
                    Type = DamageType.Physical,
                    CanCrit = false,
                };

                if (_archetype.AttackMode == EnemyAttackMode.Projectile)
                    Fire(target, request);
                else
                    Strike(target, request);

                return;
            }
        }

        private void Strike(CombatActor target, in DamageRequest request)
        {
            DamageOutcome outcome = target.ReceiveDamage(request, _self.Stats, _self.RollCrit(request));
            if (outcome.Lifesteal.Total > 0f)
                _self.Heal(outcome.Lifesteal.Total);

            // Enemies report their hits too. Nothing listens yet, but an enemy that could
            // never trigger an effect would be a rule that applies to one side only.
            _self.NotifyHitLanded(target, request, outcome, HitOrigin.Contact);
        }

        /// <summary>
        /// Fires a shot, through the same <see cref="CombatProjectile"/> the player uses - so it
        /// sweeps rather than tunnelling, stops on walls, and cannot hit the same target twice.
        /// <para>
        /// docs/01 also asks for 탄환 pooling on this archetype. Not done: one shot every 90
        /// ticks from a handful of enemies allocates far less than the thousands of enemies
        /// docs/02 section 9 has pooling in mind for, and a pool that is written before anything
        /// measures it is a guess with a lifetime. It is noted, not forgotten.
        /// </para>
        /// </summary>
        private void Fire(CombatActor target, in DamageRequest request)
        {
            Vector3 direction = target.transform.position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-4f)
                return;

            direction.Normalize();

            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = $"NV Shot ({_archetype.DocumentId})";
            orb.transform.position = transform.position + direction * 0.8f;
            orb.transform.localScale = Vector3.one * 0.3f;

            // The visual must not take part in physics: the projectile does its own sweep, and
            // a collider here would shove the shooter and block its own line of sight.
            Object.Destroy(orb.GetComponent<Collider>());

            if (projectileMaterial != null)
            {
                var renderer = orb.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.sharedMaterial = projectileMaterial;
            }

            orb.AddComponent<CombatProjectile>().Launch(
                _self, request, direction,
                _archetype.ProjectileSpeedUnitsPerSecond,
                _archetype.ProjectileLifetimeTicks,
                maxTargets: 1);
        }
    }
}
