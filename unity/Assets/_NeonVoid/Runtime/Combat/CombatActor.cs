using System;
using NeonVoid.Combat.Skills;
using NeonVoid.Combat.Status;
using NeonVoid.Core;
using UnityEngine;

namespace NeonVoid.Combat
{
    /// <summary>
    /// Anything that can deal or take damage: the player, an enemy, a boss.
    /// <para>
    /// This is the seam between the tested rules and the scene. It owns no combat arithmetic of
    /// its own - every number comes from <see cref="DamageApplication"/>,
    /// <see cref="StatusContainer"/>, <see cref="ManaPool"/> and <see cref="SkillLoadout"/>, all
    /// of which are covered by EditMode tests. What it adds is identity, lifetime and the
    /// Unity-side plumbing those pure types deliberately do not have.
    /// </para>
    /// <para>
    /// Ticking happens through <see cref="SimulationRunner"/>, never in Update, so status
    /// durations, mana and cooldowns advance on the same 60 Hz axis as movement.
    /// </para>
    /// </summary>
    public sealed class CombatActor : MonoBehaviour, ISimulationTickReceiver
    {
        // Actor ids must be unique for the lifetime of the session, including across pooled
        // respawns: ProjectileHitLedger decides "already hit" by id, so a recycled id would
        // make a projectile pass through a fresh enemy. Reset on load because Unity can skip
        // the domain reload between play sessions.
        private static ActorIdAllocator _allocator = new ActorIdAllocator();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetAllocator() => _allocator = new ActorIdAllocator();

        [Header("Identity")]
        [Tooltip("Faction tag. Actors only damage actors of a different team.")]
        [SerializeField] private int teamId;

        [Header("Stats")]
        [Tooltip("All values are [초안]; none is approved balance.")]
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float attackDamage = 5f;
        [SerializeField] private float abilityPower;
        [SerializeField] private float physicalArmor;
        [SerializeField] private float magicResistance;
        [SerializeField] private float omnivampPercent;
        [SerializeField] private float onHitLifestealPercent;
        [SerializeField] private float skillHaste;
        [SerializeField] private float tenacityPercent;

        private CombatStats _stats;
        private HealthPool _health;
        private StatusContainer _status;
        private ManaPool _mana;
        private SkillLoadout _skills;
        private SimulationRunner _runner;
        private LifestealPolicy _lifestealPolicy;

        public int ActorId { get; private set; }
        public int TeamId => teamId;
        public bool IsAlive => _health.IsAlive;
        public HealthPool Health => _health;
        public ManaPool Mana => _mana;
        public CombatStats Stats => _stats;
        public StatusContainer Status => _status;
        public SkillLoadout Skills => _skills;

        /// <summary>Raised once, after health reaches zero.</summary>
        public event Action<CombatActor> Died;

        /// <summary>Raised for every landed hit, for HUD and VFX. Read-only information.</summary>
        public event Action<CombatActor, DamageOutcome> DamageTaken;

        /// <summary>
        /// Raised on the <b>attacker</b> when one of its hits lands, which is what plugin
        /// triggers are written against: docs/03 phrases them as 탄환 적중 시 and 타격 시,
        /// both from the attacker's point of view.
        /// <para>
        /// Raised by the code that delivered the hit rather than by ReceiveDamage, because only
        /// the delivering code knows what delivered it - a projectile, a skill, a field tick or
        /// a contact attack - and that classification is what the triggers match on.
        /// </para>
        /// </summary>
        public event Action<HitReport> HitLanded;

        int ISimulationTickReceiver.TickOrder => SimulationRunner.Order.Actors;

        private void Awake()
        {
            ActorId = _allocator.Next();

            _stats = CombatStats.StartingVaccine();
            _stats.MaxHealth = maxHealth;
            _stats.AttackDamage = attackDamage;
            _stats.AbilityPower = abilityPower;
            _stats.PhysicalArmor = physicalArmor;
            _stats.MagicResistance = magicResistance;
            _stats.OmnivampPercent = omnivampPercent;
            _stats.OnHitLifestealPercent = onHitLifestealPercent;
            _stats.SkillHaste = skillHaste;
            _stats.TenacityPercent = tenacityPercent;

            try
            {
                _stats.Validate();
            }
            catch (InvalidOperationException error)
            {
                Debug.LogError($"{name}: combat stats rejected ({error.Message}). Falling back to the starting block.", this);
                _stats = CombatStats.StartingVaccine();
            }

            _health = HealthPool.Full(_stats.MaxHealth);
            _status = new StatusContainer(StatusCatalog.Draft());
            _mana = ManaPool.Starting();
            _skills = new SkillLoadout();
            _lifestealPolicy = LifestealPolicy.Draft();
        }

        private void OnEnable()
        {
            _runner = _runner != null ? _runner : FindFirstObjectByType<SimulationRunner>();
            if (_runner == null)
            {
                Debug.LogError($"{name}: no SimulationRunner in the scene; status, mana and cooldowns will never advance.", this);
                return;
            }
            _runner.Register(this);
        }

        private void OnDisable()
        {
            if (_runner != null)
                _runner.Unregister(this);
        }

        void ISimulationTickReceiver.OnSimulationTick(long tickIndex)
        {
            if (!IsAlive)
                return;

            _status.Tick();
            _mana.Tick();
            _skills.Tick();
        }

        /// <summary>
        /// Applies a hit from <paramref name="attacker"/>. Returns what happened so the caller
        /// can pay lifesteal and raise presentation events.
        /// </summary>
        public DamageOutcome ReceiveDamage(
            in DamageRequest request,
            in CombatStats attackerStats,
            bool critRolled = false,
            bool isOnHitEvent = true)
        {
            if (!IsAlive)
                return default;

            bool invulnerable = _status.Has(StatusEffectId.Invulnerable);

            DamageOutcome outcome = DamageApplication.Apply(
                request, attackerStats, _stats, ref _health, invulnerable,
                _lifestealPolicy, critRolled, isOnHitEvent);

            DamageTaken?.Invoke(this, outcome);

            if (outcome.Killed)
                Died?.Invoke(this);

            return outcome;
        }

        /// <summary>
        /// Attempts to cast the skill in a slot, spending this actor's own mana.
        /// <para>
        /// The mana lives here, so the attempt has to happen here too: handing a copy of the
        /// pool to a caster and taking it back afterwards is a lost update waiting to happen -
        /// two casts in one tick would each start from the same pre-cast balance and the second
        /// would overwrite the first one's spend.
        /// </para>
        /// <para>
        /// Every rule about whether the cast is allowed stays in <see cref="SkillLoadout"/>;
        /// this adds only the liveness check, which no gate covers.
        /// </para>
        /// </summary>
        public SkillCastResult TryCastSkill(int slotIndex)
        {
            if (!IsAlive)
                return SkillCastResult.Refused(SkillCastRejection.CasterIsDead);

            return _skills.TryCast(slotIndex, ref _mana, _stats, SkillGate.FromStatus(_status));
        }

        /// <summary>
        /// Reports that this actor landed a hit, for plugin triggers and presentation.
        /// <para>
        /// A hit that removed no health is still reported; whether it may trigger anything is
        /// decided by <see cref="HitReport.Landed"/>, in one place, rather than by each caller
        /// remembering to check.
        /// </para>
        /// </summary>
        public void NotifyHitLanded(in HitReport report) => HitLanded?.Invoke(report);

        /// <summary>
        /// Builds and raises the report for a hit this actor just delivered, so the four places
        /// that deal damage cannot each assemble it slightly differently.
        /// </summary>
        public void NotifyHitLanded(
            CombatActor target, in DamageRequest request, in DamageOutcome outcome, HitOrigin origin)
        {
            if (target == null)
                return;

            NotifyHitLanded(new HitReport(
                ActorId, target.ActorId, origin, request.Type,
                request.IsArea, request.IsPeriodic, outcome.Resolution.IsCritical,
                outcome.Killed, outcome.HealthRemoved));
        }

        /// <summary>Heals this actor, e.g. from its own lifesteal.</summary>
        public float Heal(float amount) => IsAlive ? _health.Heal(amount) : 0f;

        /// <summary>
        /// Applies a status effect, honouring this actor's tenacity. Control effects are
        /// shortened; damage over time and stat reductions are not (see TenacityMath).
        /// </summary>
        public StatusApplyOutcome ApplyStatus(
            StatusEffectId id, float magnitude, float controlResistancePercent = 0f, int durationTicksOverride = -1) =>
            _status.Apply(id, magnitude, _stats.TenacityPercent, controlResistancePercent, durationTicksOverride);

        /// <summary>True when this actor may damage the other one.</summary>
        public bool IsHostileTo(CombatActor other) => other != null && other.teamId != teamId;
    }
}
