using System;
using System.Collections.Generic;
using NeonVoid.Combat.Skills;
using NeonVoid.Combat.Stats;
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
        private static void ResetAllocator()
        {
            _allocator = new ActorIdAllocator();
            _runCombatSeed = DefaultRunCombatSeed;
        }

        /// <summary>Used until a mission supplies the run's own combat seed.</summary>
        private const uint DefaultRunCombatSeed = 0x9E3779B9u;

        // The run's combat stream seed, which every actor mixes with its own id. Static for the
        // same reason the allocator is: it belongs to the run rather than to any one actor, and
        // it is reset on load because Unity can skip the domain reload between play sessions.
        private static uint _runCombatSeed = DefaultRunCombatSeed;

        /// <summary>
        /// Hands every actor created from now on the run's combat randomness.
        /// <para>
        /// [확정] docs/06 section 4: combat randomness is its own stream. Each actor derives a
        /// generator from this and its actor id, so one enemy's attacks cannot shift where the
        /// player's crit sequence has got to - which would make a fight depend on how many
        /// times something else happened to swing.
        /// </para>
        /// </summary>
        public static void SetRunCombatSeed(uint seed) => _runCombatSeed = seed;

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
        [SerializeField] private float critChancePercent;
        [SerializeField] private float tenacityPercent;

        private DeterministicRng _combatRng;
        private CombatStats _baseStats;
        private CombatStats _stats;
        private HealthPool _health;
        private StatusContainer _status;
        private ManaPool _mana;
        private SkillLoadout _skills;
        private SimulationRunner _runner;
        private LifestealPolicy _lifestealPolicy;
        private StatCaps _caps;
        private readonly List<StatModifier> _temporaryModifiers = new List<StatModifier>();
        private readonly List<StatModifier> _passiveModifiers = new List<StatModifier>();
        private readonly List<StatusPeriodicEvent> _periodicEvents = new List<StatusPeriodicEvent>();
        private float _passiveMoveSpeedFraction;
        private bool _statsDirty = true;

        public int ActorId { get; private set; }
        public int TeamId => teamId;
        public bool IsAlive => _health.IsAlive;
        public HealthPool Health => _health;
        public ManaPool Mana => _mana;
        public StatusContainer Status => _status;
        public SkillLoadout Skills => _skills;

        /// <summary>The stat block before any buff, debuff or plugin. Never modified in place.</summary>
        public CombatStats BaseStats => _baseStats;

        /// <summary>
        /// The combat snapshot everything resolves against: base stats with every current
        /// contribution applied, in the order docs/02 section 2 fixes.
        /// <para>
        /// Recomputed from <see cref="BaseStats"/> whenever something changed, never adjusted
        /// in place. docs/02 requires 원본 기반 재계산 so the same contribution cannot land
        /// twice; a snapshot that was edited incrementally would be right once and drift
        /// upward from then on.
        /// </para>
        /// </summary>
        public CombatStats Stats
        {
            get
            {
                if (_statsDirty)
                    RecomputeStats();
                return _stats;
            }
        }

        /// <summary>
        /// Fraction of base movement speed this actor may use right now: 1 unimpeded, 0 while
        /// stunned or rooted. Read by the player's movement and by the enemy agents, so 구속
        /// means the same thing on both sides.
        /// </summary>
        public float MoveSpeedMultiplier
        {
            get
            {
                float fromStatus = StatusStatContribution.MoveSpeedMultiplier(_status);

                // A stop stays a stop. Adding a movement passive on top of 정지 or 속박 would
                // let a plugin walk out of the effect that is holding the actor still.
                if (fromStatus <= 0f)
                    return 0f;

                float total = fromStatus + _passiveMoveSpeedFraction;
                return total < 0f ? 0f : total;
            }
        }

        /// <summary>
        /// Replaces the contributions this actor carries from outside combat - plugins, and
        /// later approved permanent investment.
        /// <para>
        /// Pushed in rather than pulled: Combat must not reference Plugins, or the damage rules
        /// would depend on the content that uses them. Whoever owns the loadout calls this when
        /// it changes.
        /// </para>
        /// </summary>
        public void SetPassiveModifiers(IReadOnlyList<StatModifier> modifiers, float moveSpeedFraction = 0f)
        {
            _passiveModifiers.Clear();
            if (modifiers != null)
                for (int i = 0; i < modifiers.Count; ++i)
                    _passiveModifiers.Add(modifiers[i]);

            _passiveMoveSpeedFraction =
                float.IsNaN(moveSpeedFraction) || float.IsInfinity(moveSpeedFraction) ? 0f : moveSpeedFraction;

            _statsDirty = true;
        }

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

            _baseStats = CombatStats.StartingVaccine();
            _baseStats.MaxHealth = maxHealth;
            _baseStats.AttackDamage = attackDamage;
            _baseStats.AbilityPower = abilityPower;
            _baseStats.PhysicalArmor = physicalArmor;
            _baseStats.MagicResistance = magicResistance;
            _baseStats.OmnivampPercent = omnivampPercent;
            _baseStats.OnHitLifestealPercent = onHitLifestealPercent;
            _baseStats.SkillHaste = skillHaste;
            _baseStats.CritChancePercent = critChancePercent;
            _baseStats.TenacityPercent = tenacityPercent;

            try
            {
                _baseStats.Validate();
            }
            catch (InvalidOperationException error)
            {
                Debug.LogError($"{name}: combat stats rejected ({error.Message}). Falling back to the starting block.", this);
                _baseStats = CombatStats.StartingVaccine();
            }

            // Mixed with the actor id so two actors never share a position in the stream, and
            // so the same run gives the same actor the same sequence on a replay.
            unchecked
            {
                uint mixed = _runCombatSeed ^ ((uint)ActorId * 2654435761u);
                _combatRng = new DeterministicRng(mixed);
            }

            _caps = StatCaps.Draft();
            _health = HealthPool.Full(_baseStats.MaxHealth);
            _status = new StatusContainer(StatusCatalog.Draft());
            _mana = ManaPool.Starting();
            _skills = new SkillLoadout();
            _lifestealPolicy = LifestealPolicy.Draft();
            _statsDirty = true;
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

            // An expiry changes what the stat snapshot should say, so the next read recomputes.
            _periodicEvents.Clear();
            IReadOnlyList<StatusEffectId> expired = _status.Tick(_periodicEvents);
            if (expired != null && expired.Count > 0)
                _statsDirty = true;

            ApplyPeriodicStatusEvents();

            _mana.Tick();
            _skills.Tick();
        }

        /// <summary>
        /// Rebuilds the combat snapshot from the base stats. docs/02 section 2's order:
        /// permanent investment first, then plugins and buffs or debuffs, then caps.
        /// <para>
        /// Permanent investment is passed as null: it is approved progression state and no save
        /// exists to approve any yet. The parameter is there so the order cannot be got wrong
        /// later by inserting it in the wrong place.
        /// </para>
        /// </summary>
        private void RecomputeStats()
        {
            _temporaryModifiers.Clear();
            // One stage in docs/02's order: 플러그인/버프·디버프. Every fraction is of the base,
            // so which of the two is added first cannot change the answer.
            _temporaryModifiers.AddRange(_passiveModifiers);
            StatusStatContribution.Collect(_status, _temporaryModifiers);

            _stats = CombatStatResolution.Resolve(
                _baseStats, null, _temporaryModifiers, _caps, out int rejected);
            _statsDirty = false;

            if (rejected > 0)
                Debug.LogError(
                    $"{name}: {rejected} stat contribution(s) rejected as unusable (NaN, infinite or unknown stat).", this);
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

            // Stats, not _stats: the defender's armour and magic resistance have to be the
            // current ones. Reading the stale snapshot is what made 방화벽 차단 inert - the
            // mark was applied, tracked and displayed, and the mitigation never saw it.
            DamageOutcome outcome = DamageApplication.Apply(
                request, attackerStats, Stats, ref _health, invulnerable,
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

            return _skills.TryCast(slotIndex, ref _mana, Stats, SkillGate.FromStatus(_status));
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
            CombatActor target, in DamageRequest request, in DamageOutcome outcome, HitOrigin origin,
            float onHitEffectScale = 1f)
        {
            if (target == null)
                return;

            NotifyHitLanded(new HitReport(
                ActorId, target.ActorId, origin, request.Type,
                request.IsArea, request.IsPeriodic, outcome.Resolution.IsCritical,
                outcome.Killed, outcome.HealthRemoved, onHitEffectScale));
        }

        /// <summary>
        /// Heals or damages this actor for the status effects that fired this tick.
        /// <para>
        /// 재생 and 무결성 공격 were tracked with a duration, a stack count and a magnitude that
        /// nothing read, so applying either changed no health whatsoever. This is where they
        /// finally do something.
        /// </para>
        /// <para>
        /// The damage goes through <see cref="ReceiveDamage"/> like everything else, so
        /// resistance, 무적 and the death path all apply. It carries no attacker: nobody is
        /// credited, so there is no lifesteal to pay and no crit to roll - the magnitude on the
        /// instance IS the damage. A tick that tried to credit its applier would have to keep a
        /// reference to an actor that may be dead by then, which is the stale-reference problem
        /// HealthPool exists to avoid.
        /// </para>
        /// </summary>
        private void ApplyPeriodicStatusEvents()
        {
            for (int i = 0; i < _periodicEvents.Count; ++i)
            {
                StatusPeriodicEvent fired = _periodicEvents[i];
                if (fired.Magnitude <= 0f)
                    continue;

                switch (fired.Id)
                {
                    case StatusEffectId.Regeneration:
                        Heal(fired.Magnitude);
                        break;

                    // 해체 and 무결성 공격 both tick as magic damage over time. One case, two
                    // ids: they differ in how they stack, which is the container's business,
                    // not this switch's.
                    case StatusEffectId.Dismantle:
                    case StatusEffectId.IntegrityAttack:
                        ReceiveDamage(
                            new DamageRequest
                            {
                                Flat = fired.Magnitude,
                                Type = DamageType.Magic,
                                CanCrit = false,
                                IsPeriodic = true,
                            },
                            // A neutral attacker: the magnitude is the damage, and no stat of
                            // anybody else's scales it. Resistance still applies, which is why
                            // this is not true damage.
                            CombatStats.StartingVaccine(),
                            critRolled: false,
                            isOnHitEvent: false);
                        break;
                }
            }
        }

        /// <summary>
        /// Rolls this actor's chance to crit with a given attack, from its own combat stream.
        /// <para>
        /// The attacker rolls, because the chance is the attacker's stat. Callers pass the
        /// result into <see cref="ReceiveDamage"/>; nothing rolls on the defender's behalf.
        /// </para>
        /// </summary>
        public bool RollCrit(in DamageRequest request) => CritRoll.Roll(request, Stats, _combatRng);

        /// <summary>
        /// Rolls a percentage chance on this actor's own combat stream, for effects whose
        /// outcome has to be reproducible - 기능고장 deciding whether a shot comes out at all.
        /// <para>
        /// A decided chance draws no number, for the same reason a decided crit does not: the
        /// stream must not advance for an outcome that was never in doubt.
        /// </para>
        /// </summary>
        public bool RollChance(float percent)
        {
            if (float.IsNaN(percent) || percent <= 0f)
                return false;
            if (percent >= 100f)
                return true;
            if (_combatRng == null)
                return false;

            int threshold = (int)(percent * (CritRoll.Resolution / 100f));
            return _combatRng.NextInt(0, CritRoll.Resolution) < threshold;
        }

        /// <summary>Heals this actor, e.g. from its own lifesteal.</summary>
        public float Heal(float amount) => IsAlive ? _health.Heal(amount) : 0f;

        /// <summary>
        /// Applies a status effect, honouring this actor's tenacity. Control effects are
        /// shortened; damage over time and stat reductions are not (see TenacityMath).
        /// </summary>
        public StatusApplyOutcome ApplyStatus(
            StatusEffectId id, float magnitude, float controlResistancePercent = 0f, int durationTicksOverride = -1)
        {
            StatusApplyOutcome outcome = _status.Apply(
                id, magnitude, Stats.TenacityPercent, controlResistancePercent, durationTicksOverride);

            // A status that changed anything may change the stat snapshot, so the next read of
            // it recomputes from the base. Marking unconditionally is deliberate: deciding here
            // which statuses are "only" control effects would put that knowledge in two places.
            _statsDirty = true;
            return outcome;
        }

        /// <summary>
        /// Sets this actor's team and base stats at runtime, refilling its health.
        /// <para>
        /// The serialized fields only describe an actor placed in a scene by hand. Anything
        /// spawned - every enemy in a generated mission, per archetype - has to be configured
        /// after it exists, so this is the spawn path rather than a test hook. It is what
        /// PlayMode tests use, for the same reason.
        /// </para>
        /// </summary>
        public void Configure(int newTeamId, in CombatStats newBaseStats)
        {
            teamId = newTeamId;
            _baseStats = newBaseStats;

            try
            {
                _baseStats.Validate();
            }
            catch (InvalidOperationException error)
            {
                Debug.LogError($"{name}: configured stats rejected ({error.Message}). Falling back to the starting block.", this);
                _baseStats = CombatStats.StartingVaccine();
            }

            _health = HealthPool.Full(_baseStats.MaxHealth);
            _statsDirty = true;
        }

        /// <summary>True when this actor may damage the other one.</summary>
        public bool IsHostileTo(CombatActor other) => other != null && other.teamId != teamId;
    }
}
