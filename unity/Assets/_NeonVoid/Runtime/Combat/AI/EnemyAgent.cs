using System.Collections.Generic;
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
    /// same entry point the player's own shots use. That is what makes an enemy hit respect the
    /// player's armour, invulnerability and status exactly as a player hit respects an enemy's.
    /// An enemy that subtracted health directly would silently bypass all of it.
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
        [Header("Behaviour")]
        [Tooltip("All values are [초안]; none is approved balance.")]
        [SerializeField] private EnemyBrainConfig brain = new EnemyBrainConfig
        {
            AggroRadius = 12f,
            LeashRadius = 18f,
            PreferredRange = 1.8f,
            RangeToleranceUnits = 0.4f,
            AttackIntervalTicks = 60,
            AttackRange = 2.4f,
        };

        [Tooltip("[초안] units per second.")]
        [SerializeField, Min(0f)] private float moveSpeedUnitsPerSecond = 2.6f;

        [Header("Attack")]
        [Tooltip("[초안] AD ratio of the contact attack.")]
        [SerializeField] private float adRatio = 1f;

        private CombatActor _self;
        private CharacterController _character;
        private SimulationRunner _runner;
        private EnemyBrainState _state;
        private readonly List<CombatActor> _knownActors = new List<CombatActor>();
        private PerceivedActor[] _perceptionBuffer = System.Array.Empty<PerceivedActor>();

        /// <summary>Last decision, for inspection and debugging.</summary>
        public EnemyIntent LastIntent { get; private set; }

        int ISimulationTickReceiver.TickOrder => SimulationRunner.Order.Actors + 1;

        private void Awake()
        {
            _self = GetComponent<CombatActor>();
            _character = GetComponent<CharacterController>();
            _state = EnemyBrainState.Fresh();

            try
            {
                brain.Validate();
            }
            catch (System.InvalidOperationException error)
            {
                Debug.LogError($"{name}: brain config rejected ({error.Message}). Falling back to the chaser defaults.", this);
                brain = EnemyBrainConfig.Chaser();
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

            EnemyIntent intent = EnemyBrain.Decide(ref _state, BuildPerception(), brain);
            LastIntent = intent;

            if (intent.MoveXZ.sqrMagnitude > 1e-6f && moveSpeedUnitsPerSecond > 0f)
            {
                Vector2 step = intent.MoveXZ.normalized
                               * (moveSpeedUnitsPerSecond * intent.SpeedMultiplier
                                  * (float)SimulationClock.TickDeltaSeconds);
                _character.Move(new Vector3(step.x, 0f, step.y));
            }

            if (intent.FaceXZ.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(
                    new Vector3(intent.FaceXZ.x, 0f, intent.FaceXZ.y), Vector3.up);

            if (intent.WantsAttack)
                ExecuteAttack(intent.TargetActorId);
        }

        private EnemyPerception BuildPerception()
        {
            int written = 0;
            for (int i = 0; i < _knownActors.Count; ++i)
            {
                CombatActor actor = _knownActors[i];
                if (actor == null)
                    continue;

                Vector3 p = actor.transform.position;
                _perceptionBuffer[written++] = new PerceivedActor(
                    actor.ActorId, new Vector2(p.x, p.z), actor.IsAlive);
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
        /// Turns the intent into a hit through the shared damage entry point. The brain decided
        /// to attack; it did not decide how much that costs the target, and nothing here
        /// touches health directly either.
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
                    AdRatio = adRatio,
                    Type = DamageType.Physical,
                    CanCrit = false,
                };

                DamageOutcome outcome = target.ReceiveDamage(request, _self.Stats, critRolled: false);
                if (outcome.Lifesteal.Total > 0f)
                    _self.Heal(outcome.Lifesteal.Total);

                // Enemies report their hits too. Nothing listens yet, but an enemy that could
                // never trigger an effect would be a rule that applies to one side only.
                _self.NotifyHitLanded(target, request, outcome, HitOrigin.Contact);
                return;
            }
        }
    }
}
