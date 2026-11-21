using System.Collections.Generic;
using NeonVoid.Combat;
using NeonVoid.Combat.Proc;
using NeonVoid.Combat.Projectiles;
using NeonVoid.Combat.Skills;
using NeonVoid.Combat.Stats;
using NeonVoid.Core;
using NeonVoid.Missions;
using UnityEngine;

namespace NeonVoid.Plugins
{
    /// <summary>
    /// Runs the plugin procs in the scene: listens to the owner's landed hits, lets the queue
    /// decide, and applies what the queue allowed.
    /// <para>
    /// The queue is drained once per logic tick, after projectiles and effects have run, so
    /// every hit landed this tick is considered together and in enqueue order. Resolving
    /// immediately on each hit would make the order depend on which projectile happened to
    /// move first.
    /// </para>
    /// <para>
    /// [확정] docs/02 section 6: simulation changes happen in one event queue, and presentation
    /// never creates damage. Everything here is simulation, and every refusal is counted by the
    /// queue rather than by this class.
    /// </para>
    /// <para>
    /// The three prototype plugins are granted at startup. That is <b>not</b> how plugins are
    /// obtained - docs/03 gives them to the player through the three-card reward screen, which
    /// needs the reward pool and the save commit behind it. This grants them so the proc path
    /// can be exercised at all.
    /// </para>
    /// </summary>
    public sealed class PluginProcDirector : MonoBehaviour, ISimulationTickReceiver
    {
        [Header("Wiring")]
        [Tooltip("The actor whose hits trigger these plugins.")]
        [SerializeField] private CombatActor owner;

        [Tooltip("Prototype: grant the draft plugins when the mission starts.")]
        [SerializeField] private bool grantPrototypePluginsOnStart = true;

        [Tooltip("Where the reward seed comes from. Found automatically when left empty.")]
        [SerializeField] private MissionRunDirector missionDirector;

        [Tooltip("Fallback reward seed, used only when no MissionRunDirector is in the scene.")]
        [SerializeField] private uint fallbackRewardSeed = 20261120u;

        private ProcChainGuard _guard;
        private ProcQueue _queue;
        private ProcTriggerRouter _router;
        private SimulationRunner _runner;
        private readonly Dictionary<int, CombatActor> _actorsById = new Dictionary<int, CombatActor>();
        private readonly HashSet<int> _damagedByExplosion = new HashSet<int>();

        private readonly Dictionary<int, int> _resolvedByEffectId = new Dictionary<int, int>();
        private readonly List<StatModifier> _passiveBuffer = new List<StatModifier>();
        private readonly List<ExtraShotSpec> _extraShotBuffer = new List<ExtraShotSpec>();
        private readonly List<PluginDefinition> _burstBuffer = new List<PluginDefinition>();
        private readonly List<PluginDefinition> _dashBuffer = new List<PluginDefinition>();
        private readonly List<PluginDefinition> _growthBuffer = new List<PluginDefinition>();
        private readonly List<PluginDefinition> _conditionBuffer = new List<PluginDefinition>();
        private readonly List<PluginDefinition> _chargeBuffer = new List<PluginDefinition>();
        private readonly List<PluginDefinition> _timerBuffer = new List<PluginDefinition>();

        /// <summary>Gauge held per charged card, by its proposal id. L03 피버타임.</summary>
        private readonly Dictionary<string, float> _charge = new Dictionary<string, float>();

        /// <summary>Ticks left on each card's open window, by proposal id.</summary>
        private readonly Dictionary<string, int> _feverTicks = new Dictionary<string, int>();

        private readonly List<string> _prunedFeverKeys = new List<string>();

        /// <summary>Ticks each OnTimer card has waited, by proposal id. G05 주기 차폐막.</summary>
        private readonly Dictionary<string, int> _timers = new Dictionary<string, int>();

        private readonly List<string> _prunedTimerKeys = new List<string>();

        /// <summary>
        /// Ticks since the owner last took damage, for G11 무피격 가속.
        /// <para>
        /// Starts at <see cref="int.MaxValue"/> and not at zero: a carrier who has never been
        /// hurt has gone an unbounded time unhurt, so 무피격 가속 is live from the start rather
        /// than needing a first hit before it begins counting. Saturated rather than wrapped,
        /// because a counter that overflowed would switch the card off after about a year of
        /// play for no reason anyone could find.
        /// </para>
        /// </summary>
        private int _ticksSinceDamage = int.MaxValue;

        /// <summary>Whether the gates were open on the last publish, so a flip can republish.</summary>
        private bool _conditionsWereMet = true;

        /// <summary>
        /// Counters behind the growing passives, keyed on the card's proposal id.
        /// <para>
        /// Here rather than on the card, because the card is data that is copied by value - a
        /// counter on it would be incremented on a copy and lost. Mission scoped by having
        /// nowhere else to live: nothing persists a run (D-017), so these go when the run does,
        /// which is the half of docs/03 R02's 임무 종료 해제 that is settled.
        /// </para>
        /// </summary>
        private readonly Dictionary<string, int> _growthStacks = new Dictionary<string, int>();
        private readonly List<string> _prunedGrowthKeys = new List<string>();
        private AutoAttacker _attacker;
        private IDashState _dash;
        private PlayerSkillCaster _caster;
        private DeterministicRng _rewardRng;

        /// <summary>
        /// Enemies this dash has already touched. Cleared when a dash begins, never during one.
        /// <para>
        /// docs/03 G16's exception column says 한 대시에서 중복 접촉 in as many words. A dash
        /// lasts several ticks and the path is swept on each of them, so without this an enemy
        /// standing in the way takes the damage once per tick - which at the prototype's 9 tick
        /// dash is nine times the damage the card describes.
        /// </para>
        /// </summary>
        private readonly HashSet<int> _touchedByThisDash = new HashSet<int>();

        private bool _wasDashing;

        /// <summary>
        /// Resolved hit effects counted towards each burst card's next burst, keyed on the
        /// card's effect id.
        /// <para>
        /// Per card, because two burst cards count independently - G13 asks for 적중 효과 4회
        /// and a second such card would have its own threshold. A single shared counter would
        /// make them race for the same effects.
        /// </para>
        /// </summary>
        private readonly Dictionary<string, int> _hitEffectsTowardsBurst = new Dictionary<string, int>();

        /// <summary>The last drain's report, for inspection and for the HUD later.</summary>
        public ProcDrainReport LastDrain { get; private set; }

        /// <summary>
        /// How many times each effect has actually resolved this session.
        /// <para>
        /// This exists because the three prototype plugins are invisible. Chain lightning,
        /// an explosion and an armour mark all deal their damage without a single VFX, there
        /// are no health bars and no damage numbers, so "did the plugin fire?" had no answer
        /// at all - which made the proc path impossible to verify in play, however many unit
        /// tests passed. A count is the smallest honest answer.
        /// </para>
        /// </summary>
        public int ResolvedCountOf(int effectId) =>
            _resolvedByEffectId.TryGetValue(effectId, out int count) ? count : 0;

        /// <summary>
        /// Names what this director should ask about dashing, instead of looking for it on the
        /// owner.
        /// <para>
        /// There are two reasons for the seam. A dash does not have to live on the same object
        /// as the actor - a mount or a vehicle would own it - and a test has no way to press
        /// the dash button, so without this the whole dash path could only be exercised by
        /// driving the Input System.
        /// </para>
        /// </summary>
        public void SetDashState(IDashState dashState)
        {
            _dash = dashState;
            _wasDashing = false;
            _touchedByThisDash.Clear();
        }

        /// <summary>Enemies the current dash has already touched. For tests and the HUD.</summary>
        public int TouchedByThisDash => _touchedByThisDash.Count;

        /// <summary>Hit effects counted towards a burst card's next burst. For tests and the HUD.</summary>
        public int HitEffectsTowardsBurst(string proposalId) =>
            proposalId != null && _hitEffectsTowardsBurst.TryGetValue(proposalId, out int counted) ? counted : 0;

        /// <summary>Refusals accumulated over the whole session, by reason.</summary>
        public int BlockedByInternalCooldown { get; private set; }

        /// <summary>Refusals accumulated over the whole session, by reason.</summary>
        public int BlockedByCycleOrLimit { get; private set; }

        public PluginLoadout Loadout => _router?.Loadout;

        int ISimulationTickReceiver.TickOrder => SimulationRunner.Order.Effects + 1;

        private void Awake()
        {
            if (owner == null)
                owner = GetComponent<CombatActor>();

            _guard = new ProcChainGuard();
            if (missionDirector == null)
                missionDirector = FindFirstObjectByType<MissionRunDirector>();
            _queue = new ProcQueue(_guard);
            _router = new ProcTriggerRouter(new PluginLoadout(), _guard, _queue);
        }

        private void Start()
        {
            RefreshActorIndex();

            // Created here rather than in Awake so the mission has certainly begun. docs/06
            // section 4 keeps mission, combat and reward randomness in separate streams, and a
            // plugin roll is reward randomness - taken from THIS run's reward stream, so two
            // runs roll different stats instead of every run rolling the same ones.
            _rewardRng = missionDirector != null
                ? missionDirector.CreateRng(RandomStream.Reward)
                : new DeterministicRng(fallbackRewardSeed);

            if (!grantPrototypePluginsOnStart)
                return;

            IReadOnlyList<PluginDefinition> prototype = PluginCatalog.Prototype();
            for (int i = 0; i < prototype.Count; ++i)
            {
                PluginGrantVerdict verdict = _router.TryGrant(prototype[i], _rewardRng, out string reason);
                if (verdict == PluginGrantVerdict.Granted)
                    Debug.Log($"Plugin {prototype[i].ProposalId} carried.", this);
                else
                    Debug.LogWarning($"Plugin {prototype[i].ProposalId} not carried: {verdict} - {reason}", this);
            }

            PushPassivesToOwner();
        }

        private void OnEnable()
        {
            if (owner == null)
            {
                Debug.LogError($"{name}: no owner CombatActor; no plugin will ever trigger.", this);
                return;
            }
            owner.HitLanded += OnOwnerHitLanded;
            owner.DamageTaken += OnOwnerDamaged;

            // 점멸/순간이동 후 for G08. The caster raises this only for a skill that actually
            // went off, which is the half of the card a dash cannot cover.
            _caster = _caster != null ? _caster : owner.GetComponent<PlayerSkillCaster>();
            if (_caster != null)
                _caster.SkillCast += OnOwnerSkillCast;

            _runner = _runner != null ? _runner : FindFirstObjectByType<SimulationRunner>();
            if (_runner == null)
            {
                Debug.LogError($"{name}: no SimulationRunner; queued procs would never resolve.", this);
                return;
            }
            _runner.Register(this);
        }

        private void OnDisable()
        {
            if (owner != null)
            {
                owner.HitLanded -= OnOwnerHitLanded;
                owner.DamageTaken -= OnOwnerDamaged;
            }

            if (_caster != null)
                _caster.SkillCast -= OnOwnerSkillCast;
            if (_runner != null)
                _runner.Unregister(this);
        }

        /// <summary>
        /// Rebuilds the id lookup. Called at startup rather than per tick, like the enemy
        /// agents' own roster; a spawner must call this when it adds an actor.
        /// </summary>
        public void RefreshActorIndex()
        {
            _actorsById.Clear();
            foreach (CombatActor actor in FindObjectsByType<CombatActor>(FindObjectsSortMode.None))
                if (actor != null)
                    _actorsById[actor.ActorId] = actor;
        }

        /// <summary>
        /// Hands the owner everything its carried plugins contribute to the stat snapshot.
        /// Called whenever the loadout changes; a plugin that was granted and never pushed
        /// would sit in the list doing nothing, which is the failure this project has already
        /// shipped twice in other shapes.
        /// </summary>
        public void PushPassivesToOwner()
        {
            if (owner == null || _router == null)
                return;

            _passiveBuffer.Clear();
            _router.Loadout.CollectPassiveModifiers(_passiveBuffer);
            AppendGrowthModifiers(_passiveBuffer);
            float moveSpeed = _router.Loadout.PassiveMoveSpeedFraction()
                + AppendMetConditionals(_passiveBuffer)
                + AppendOpenFevers(_passiveBuffer);
            owner.SetPassiveModifiers(_passiveBuffer, moveSpeed);

            PushFiringToAttacker();
        }

        /// <summary>
        /// Adds each growing passive's current contribution, and forgets the counters of cards
        /// that are no longer carried.
        /// <para>
        /// The pruning matters: a card dropped and granted again would otherwise come back with
        /// the kills it had before, which would read as the stacks surviving something they
        /// should not.
        /// </para>
        /// </summary>
        /// <summary>
        /// Counts the ticks since the owner was last hurt and republishes the stat snapshot on
        /// the tick a gate opens or closes - not every tick.
        /// <para>
        /// Republishing every tick would be correct and would also recompute every carried
        /// card's contribution sixty times a second for a value that changes twice a fight.
        /// Publishing only on a change means the snapshot can be wrong for no tick at all,
        /// because the change is what triggers the publish.
        /// </para>
        /// </summary>
        /// <summary>
        /// Fills every carried gauge, and opens a window the moment one is full.
        /// <para>
        /// <b>A gauge does not fill while its own buff is running [초안].</b> docs/03 leaves
        /// 충전 단위 미확정 and says nothing either way, and the alternative is a card that
        /// charges itself back up during the strongest seconds of the fight - which is how a
        /// 피버타임 stops being a 타임 and becomes a permanent buff on anybody killing fast
        /// enough. Choosing the bounded reading leaves the decision reversible.
        /// </para>
        /// </summary>
        private void ChargeFeverGauges(bool killed)
        {
            _chargeBuffer.Clear();
            _router.Loadout.CollectChargedPassives(_chargeBuffer);
            if (_chargeBuffer.Count == 0)
                return;

            bool started = false;
            for (int i = 0; i < _chargeBuffer.Count; ++i)
            {
                PluginDefinition card = _chargeBuffer[i];

                _feverTicks.TryGetValue(card.ProposalId, out int running);
                if (running > 0)
                    continue;

                _charge.TryGetValue(card.ProposalId, out float charge);
                charge = card.Charge.Add(charge, killed);

                if (!card.Charge.IsFull(charge))
                {
                    _charge[card.ProposalId] = charge;
                    continue;
                }

                // Spent in full rather than reduced by the requirement: banking the overflow
                // would let a long fight hold a second 피버타임 ready the instant the first
                // ended, which is the same permanent-buff shape by another route.
                _charge[card.ProposalId] = 0f;
                _feverTicks[card.ProposalId] = card.Charge.ActiveTicks;
                started = true;

                Debug.Log($"{card.ProposalId} 피버타임: {card.Charge.ActiveTicks} ticks.", this);
            }

            if (started)
                PushPassivesToOwner();
        }

        /// <summary>
        /// Counts down every open window and republishes on the tick one closes.
        /// <para>
        /// Over a snapshot of the keys, because a dictionary cannot be written while it is
        /// being walked - and the whole point of this pass is to remove the ones that ran out.
        /// </para>
        /// </summary>
        private void TickFeverWindows()
        {
            if (_feverTicks.Count == 0)
                return;

            _prunedFeverKeys.Clear();
            foreach (string card in _feverTicks.Keys)
                _prunedFeverKeys.Add(card);

            bool ended = false;
            for (int i = 0; i < _prunedFeverKeys.Count; ++i)
            {
                string card = _prunedFeverKeys[i];
                int left = _feverTicks[card] - 1;

                if (left > 0)
                {
                    _feverTicks[card] = left;
                    continue;
                }

                _feverTicks.Remove(card);
                ended = true;
            }

            if (ended)
                PushPassivesToOwner();
        }

        /// <summary>Gauge held by a charged card, 0 to 1. For a HUD and for a test.</summary>
        public float FeverCharge(string proposalId)
        {
            _chargeBuffer.Clear();
            _router.Loadout.CollectChargedPassives(_chargeBuffer);

            for (int i = 0; i < _chargeBuffer.Count; ++i)
            {
                if (_chargeBuffer[i].ProposalId != proposalId)
                    continue;

                _charge.TryGetValue(proposalId, out float charge);
                return _chargeBuffer[i].Charge.Fraction(charge);
            }

            return 0f;
        }

        /// <summary>Ticks left on a card's open window, or 0 when it is not running.</summary>
        public int FeverTicksLeft(string proposalId) =>
            _feverTicks.TryGetValue(proposalId, out int left) ? left : 0;

        /// <summary>Adds the contribution of every charged card whose window is open.</summary>
        private float AppendOpenFevers(List<StatModifier> into)
        {
            if (_feverTicks.Count == 0)
                return 0f;

            _chargeBuffer.Clear();
            _router.Loadout.CollectChargedPassives(_chargeBuffer);

            float moveSpeed = 0f;
            for (int i = 0; i < _chargeBuffer.Count; ++i)
            {
                PluginDefinition card = _chargeBuffer[i];
                if (!_feverTicks.ContainsKey(card.ProposalId))
                    continue;

                moveSpeed += card.Passive.MoveSpeedFraction;

                StatModifier[] modifiers = card.Passive.StatModifiers;
                if (modifiers == null)
                    continue;

                for (int m = 0; m < modifiers.Length; ++m)
                    into.Add(modifiers[m]);
            }

            return moveSpeed;
        }

        /// <summary>
        /// Runs each OnTimer card's own clock. G05 주기 차폐막 is 30초 주기, which docs/02
        /// section 7 states as [확정].
        /// <para>
        /// A clock per card rather than one shared one, because two cards with different
        /// periods would otherwise have to agree on a tick, and the counters of cards that are
        /// no longer carried are dropped so a card re-granted later starts its period fresh
        /// rather than firing instantly off a stale count.
        /// </para>
        /// </summary>
        private void TickCardTimers(long tickIndex)
        {
            _timerBuffer.Clear();
            _router.Loadout.CollectTriggeredByTimer(_timerBuffer);

            if (_timerBuffer.Count == 0)
            {
                _timers.Clear();
                return;
            }

            if (_timers.Count > 0)
            {
                _prunedTimerKeys.Clear();
                foreach (string counted in _timers.Keys)
                {
                    bool stillCarried = false;
                    for (int i = 0; i < _timerBuffer.Count && !stillCarried; ++i)
                        stillCarried = _timerBuffer[i].ProposalId == counted;

                    if (!stillCarried)
                        _prunedTimerKeys.Add(counted);
                }

                for (int i = 0; i < _prunedTimerKeys.Count; ++i)
                    _timers.Remove(_prunedTimerKeys[i]);
            }

            for (int i = 0; i < _timerBuffer.Count; ++i)
            {
                PluginDefinition card = _timerBuffer[i];
                _timers.TryGetValue(card.ProposalId, out int waited);
                ++waited;

                if (waited < card.TimerPeriodTicks)
                {
                    _timers[card.ProposalId] = waited;
                    continue;
                }

                _timers[card.ProposalId] = 0;
                _router.OnTimerElapsed(
                    owner != null ? owner.ActorId : 0, tickIndex, card.ProposalId);
            }
        }

        /// <summary>Ticks a card's clock has waited. For a HUD and for a test.</summary>
        public int TimerTicks(string proposalId) =>
            _timers.TryGetValue(proposalId, out int waited) ? waited : 0;

        /// <summary>
        /// Gives the carrier the 보호막 a card granted.
        /// <para>
        /// The source id is the card's, which is what <see cref="ShieldPolicy.Refresh"/> keys
        /// on - two shields from one card are the refresh question, two from different cards
        /// never are.
        /// </para>
        /// </summary>
        private void RaiseShield(in PluginDefinition plugin)
        {
            if (owner == null)
                return;

            long tick = _runner != null ? _runner.TickIndex : 0L;
            owner.Shields.Grant(
                plugin.ProposalId, plugin.Shield.Amount, tick, plugin.Shield.LifetimeTicks);

            Debug.Log(
                $"{plugin.ProposalId} 보호막 {plugin.Shield.Amount:0.#} " +
                $"({owner.Shields.LayerCount} standing, {owner.Shields.Total:0.#} total).", this);
        }

        private void TickConditions()
        {
            if (_ticksSinceDamage < int.MaxValue)
                ++_ticksSinceDamage;

            bool met = EveryConditionIsMet();
            if (met == _conditionsWereMet)
                return;

            _conditionsWereMet = met;
            PushPassivesToOwner();
        }

        private bool EveryConditionIsMet()
        {
            if (_router == null)
                return true;

            _conditionBuffer.Clear();
            _router.Loadout.CollectConditionalPassives(_conditionBuffer);

            for (int i = 0; i < _conditionBuffer.Count; ++i)
            {
                if (!_conditionBuffer[i].Condition.IsMet(_ticksSinceDamage))
                    return false;
            }

            return true;
        }

        private void OnOwnerDamaged(CombatActor actor, DamageOutcome outcome)
        {
            OfferLowHealthCards();

            // Any damage at all, including a hit that was fully absorbed: docs/03 says
            // 피해를 받으면 비활성화, and a card that stayed on through a blocked hit would be a
            // different card. The resolver only raises this when a hit actually resolved.
            _ticksSinceDamage = 0;

            if (!_conditionsWereMet)
                return;

            _conditionsWereMet = false;
            PushPassivesToOwner();
        }

        /// <summary>
        /// Adds the stat contributions of every gated card whose gate is open, and returns the
        /// movement they add. Cards whose gate is shut contribute nothing at all - not a
        /// reduced amount, which would be a card that is always partly on.
        /// </summary>
        private float AppendMetConditionals(List<StatModifier> into)
        {
            _conditionBuffer.Clear();
            _router.Loadout.CollectConditionalPassives(_conditionBuffer);

            float moveSpeed = 0f;
            for (int i = 0; i < _conditionBuffer.Count; ++i)
            {
                PluginDefinition card = _conditionBuffer[i];
                if (!card.Condition.IsMet(_ticksSinceDamage))
                    continue;

                moveSpeed += card.Passive.MoveSpeedFraction;

                StatModifier[] modifiers = card.Passive.StatModifiers;
                if (modifiers == null)
                    continue;

                for (int m = 0; m < modifiers.Length; ++m)
                    into.Add(modifiers[m]);
            }

            return moveSpeed;
        }

        private void AppendGrowthModifiers(List<StatModifier> into)
        {
            _growthBuffer.Clear();
            _router.Loadout.CollectKillGrowthPlugins(_growthBuffer);

            if (_growthStacks.Count > 0)
            {
                _prunedGrowthKeys.Clear();
                foreach (string countedCard in _growthStacks.Keys)
                {
                    bool stillCarried = false;
                    for (int i = 0; i < _growthBuffer.Count && !stillCarried; ++i)
                        stillCarried = _growthBuffer[i].ProposalId == countedCard;

                    if (!stillCarried)
                        _prunedGrowthKeys.Add(countedCard);
                }

                for (int i = 0; i < _prunedGrowthKeys.Count; ++i)
                    _growthStacks.Remove(_prunedGrowthKeys[i]);
            }

            for (int i = 0; i < _growthBuffer.Count; ++i)
            {
                PluginDefinition card = _growthBuffer[i];
                _growthStacks.TryGetValue(card.ProposalId, out int stacks);
                if (stacks > 0)
                    into.Add(card.Growth.ModifierAt(stacks));
            }
        }

        /// <summary>
        /// Counts a kill towards every growing passive, and republishes the stats if any of
        /// them moved.
        /// <para>
        /// Republished rather than left for the next push, because the stat snapshot is what
        /// the next attack resolves against: a stack counted and not pushed is the
        /// "applied but has no effect" failure this project keeps finding.
        /// </para>
        /// </summary>
        private void CountKillTowardsGrowth()
        {
            _growthBuffer.Clear();
            _router.Loadout.CollectKillGrowthPlugins(_growthBuffer);
            if (_growthBuffer.Count == 0)
                return;

            bool changed = false;
            for (int i = 0; i < _growthBuffer.Count; ++i)
            {
                PluginDefinition card = _growthBuffer[i];
                _growthStacks.TryGetValue(card.ProposalId, out int stacks);

                // Clamped at the bound rather than counted past it, so the number the HUD
                // shows is the number that is contributing.
                if (stacks >= card.Growth.MaxStacks)
                    continue;

                _growthStacks[card.ProposalId] = stacks + 1;
                changed = true;
            }

            if (changed)
                PushPassivesToOwner();
        }

        /// <summary>Stacks a growing passive has accumulated. For tests and the HUD.</summary>
        public int GrowthStacks(string proposalId) =>
            proposalId != null && _growthStacks.TryGetValue(proposalId, out int stacks) ? stacks : 0;

        /// <summary>
        /// Hands the attacker the extra projectiles its carried plugins grant.
        /// <para>
        /// Pushed, like the passives and for the same layering reason: Combat must not reference
        /// Plugins, so <see cref="AutoAttacker"/> is told what to fire rather than asking what
        /// is carried. A card granted and never pushed would sit in the loadout firing nothing,
        /// which is the shape of failure this project keeps finding.
        /// </para>
        /// </summary>
        /// <summary>
        /// 위기 반발: offers every low-health card, which the router then filters by its own
        /// threshold and bounds by its internal cooldown.
        /// </summary>
        private void OfferLowHealthCards()
        {
            if (owner == null || _router == null || owner.Health.Max <= 0f)
                return;

            float fraction = owner.Health.Current / owner.Health.Max;
            _router.OnOwnerLowHealth(
                owner.ActorId, _runner != null ? _runner.TickIndex : 0L, fraction);
        }

        /// <summary>
        /// R03 위기 반발: shoves every hostile near the carrier away from them.
        /// <para>
        /// <b>보스 저항 [초안]: a boss is not moved.</b> docs/03 leaves the policy open and
        /// docs/01 section 3 says 강한 CC는 강인함·저항/회피창 검증 없이는 넣지 않는다 -
        /// shoving a boss out of a telegraphed pattern is the strongest form of that, and none
        /// of the verification that rule asks for has happened. The cautious reading can be
        /// loosened later; a boss fight already built around being pushable cannot.
        /// </para>
        /// <para>
        /// Applied through each enemy's own CharacterController at Effects + 1, which is after
        /// the agents have moved at Actors + 1 - so the shove is the last word on where an
        /// enemy stands this tick rather than something their own movement overwrites. The
        /// order the physics query returns them in does not matter, because each enemy is
        /// pushed away from the carrier independently of the others.
        /// </para>
        /// </summary>
        private void ShoveEverybodyBack(in PluginDefinition plugin)
        {
            if (owner == null)
                return;

            Vector3 centre = owner.transform.position;
            Collider[] near = Physics.OverlapSphere(
                centre, plugin.RadiusUnits, ~0, QueryTriggerInteraction.Ignore);

            int shoved = 0;
            for (int i = 0; i < near.Length; ++i)
            {
                var actor = near[i].GetComponentInParent<CombatActor>();
                if (actor == null || !actor.IsAlive || !owner.IsHostileTo(actor))
                    continue;

                if (actor.GetComponentInParent<Combat.AI.AreaBossBrain>() != null)
                    continue;

                Vector3 away = actor.transform.position - centre;
                away.y = 0f;
                if (away.sqrMagnitude < 1e-6f)
                    continue;

                Vector3 step = away.normalized * plugin.KnockbackUnits;

                var character = actor.GetComponent<CharacterController>();
                if (character != null && character.enabled)
                    character.Move(step);
                else
                    actor.transform.position += step;

                ++shoved;
            }

            if (shoved > 0)
                Debug.Log($"{plugin.ProposalId} 위기 반발: {shoved} enemy/enemies shoved back.", this);
        }

        /// <summary>
        /// G14 추적 응답: the basic attack goes off because a skill landed.
        /// <para>
        /// <b>스킬의 자기 발동/루프 방지</b>, which docs/03 lists against this card, holds for
        /// two reasons rather than a guard written here. The shot is a basic attack, so what it
        /// hits carries <see cref="HitOrigin"/> of a basic attack and can never satisfy
        /// <see cref="PluginTriggerKind.OnSkillHit"/> again - the loop has no second step. And
        /// the queue's own chain rules bound the effect anyway, the same as every other proc.
        /// </para>
        /// <para>
        /// Queued rather than fired inside the resolve: a shot spawned mid-drain would be a
        /// projectile created while the queue is walking its own list, and the attacker is the
        /// thing that knows which way the carrier is facing on this tick.
        /// </para>
        /// </summary>
        private void FireRespondingShot(in PluginDefinition plugin)
        {
            if (_attacker == null && owner != null)
                _attacker = owner.GetComponent<AutoAttacker>();

            if (_attacker == null)
            {
                Debug.LogWarning(
                    $"{name}: {plugin.ProposalId} fired on a skill hit and there is no AutoAttacker " +
                    "to fire from, so nothing was shot.", this);
                return;
            }

            // Full effect strength. R06 and G13 cut theirs to 80% and 70% because docs/03
            // says so for those two; it says nothing of the kind for G14, and inventing a
            // reduction would be a nerf nobody wrote down.
            _attacker.QueueBurst(plugin.ProjectilesFired, onHitEffectScale: 1f);
        }

        private void PushFiringToAttacker()
        {
            if (_attacker == null && owner != null)
                _attacker = owner.GetComponent<AutoAttacker>();

            if (_attacker == null)
                return;

            _extraShotBuffer.Clear();
            _router.Loadout.CollectExtraShots(_extraShotBuffer);
            _attacker.SetExtraShots(_extraShotBuffer);

            // G06 스로틀 증폭 and G03 확장 탄두 change the shape of the basic attack rather than
            // a stat, so they go the same way the extra shots do: pushed to the attacker, never
            // read from Plugins by Combat.
            _attacker.SetPassiveShotShape(
                _router.Loadout.PassiveAttackSpeedFraction(),
                _router.Loadout.PassiveProjectileSpeedFraction(),
                _router.Loadout.PassiveProjectileSizeFraction());
        }

        /// <summary>
        /// Counts one resolved hit effect towards every burst card, and queues a burst for each
        /// that has reached its threshold.
        /// <para>
        /// Only RESOLVED effects count. An effect refused by an internal cooldown, a cycle rule
        /// or an occurrence limit did nothing, and charging a burst off it would let a card turn
        /// the rules' refusals into damage.
        /// </para>
        /// </summary>
        private void CountHitEffectTowardsBursts()
        {
            if (_attacker == null || _router == null)
                return;

            _burstBuffer.Clear();
            _router.Loadout.CollectBurstPlugins(_burstBuffer);

            for (int i = 0; i < _burstBuffer.Count; ++i)
            {
                PluginDefinition plugin = _burstBuffer[i];
                string key = plugin.ProposalId;
                _hitEffectsTowardsBurst.TryGetValue(key, out int counted);
                counted++;

                if (counted < plugin.Firing.HitEffectsPerBurst)
                {
                    _hitEffectsTowardsBurst[key] = counted;
                    continue;
                }

                // Reset rather than subtract: the count is "effects since the last burst", and
                // leaving a remainder would make the second burst arrive early.
                _hitEffectsTowardsBurst[key] = 0;
                _attacker.QueueBurst(plugin.Firing.BurstShots, plugin.Firing.BurstOnHitScale);
            }
        }

        /// <summary>Grants a plugin at runtime and republishes the passives.</summary>
        public PluginGrantVerdict Grant(in PluginDefinition definition, out string reason)
        {
            PluginGrantVerdict verdict = _router.TryGrant(definition, _rewardRng, out reason);
            if (verdict == PluginGrantVerdict.Granted)
                PushPassivesToOwner();

            return verdict;
        }

        /// <summary>
        /// A skill that went off. Only a movement skill is a movement action: docs/03 G08's
        /// list is 돌진/도약/점멸/순간이동, and the data already marks which skills move the
        /// player - <see cref="SkillDefinition.IsMovementSkill"/>, which exists because 속박
        /// has to deny exactly those. That is the 스킬 분류 태그 the card's exception column
        /// asks for, and it was already there.
        /// </summary>
        private void OnOwnerSkillCast(SkillDefinition definition)
        {
            if (owner == null)
                return;

            long tick = _runner != null ? _runner.TickIndex : 0L;

            // G07 실행 차폐 fires for any skill that went off. The caster raises this event
            // only for a successful cast, which is 스킬 실패 시 발동하지 않음.
            _router.OnSkillCast(owner.ActorId, tick);

            if (!definition.IsMovementSkill)
                return;

            _router.OnMovementAction(owner.ActorId, tick);
        }

        private void OnOwnerHitLanded(HitReport report)
        {
            long tick = _runner != null ? _runner.TickIndex : 0L;
            _router.OnHitLanded(report, tick);

            // 적 처치 시 for R02. HitReport.Killed has been carried since the damage pipeline
            // was written and nothing read it until now.
            if (report.Killed)
                CountKillTowardsGrowth();

            // 적중 또는 처치로 게이지 for L03.
            ChargeFeverGauges(report.Killed);
        }

        void ISimulationTickReceiver.OnSimulationTick(long tickIndex)
        {
            // Before the drain, so a contact found this tick resolves this tick. This runs at
            // Effects + 1, after the player has already moved at Player order, so the position
            // swept is where the dash actually is.
            TickConditions();
            TickFeverWindows();
            TickCardTimers(tickIndex);

            SweepDashPath(tickIndex);

            if (_router.PendingCount == 0)
                return;

            ProcDrainReport report = _router.Drain(Resolve);
            LastDrain = report;

            BlockedByInternalCooldown += report.BlockedByInternalCooldown;
            BlockedByCycleOrLimit +=
                report.BlockedByCycle + report.BlockedByOccurrenceLimit + report.BlockedByRepeatOnSameTarget;

            // docs/02 section 6 forbids losing rules quietly. A chain that ended because the
            // rules said so is healthy; one that ended because the budget ran out is not, and
            // the player would have no way of telling the difference.
            if (!report.CompletedWithinBudget)
                Debug.LogError(
                    $"{name}: proc drain hit a resource limit on tick {tickIndex} ({report}). " +
                    "Effects the rules allowed did not happen.", this);
        }

        /// <summary>
        /// Offers every enemy the dash is touching to the dash-triggered cards, once each per
        /// dash.
        /// <para>
        /// An overlap at the dasher's position each tick rather than a swept capsule, which is
        /// enough here and says why: the prototype dash covers about 0.23 units per tick and
        /// the card's reach is several times that, so consecutive overlaps leave no gap for an
        /// enemy to sit in. A faster dash or a smaller reach would need the sweep, and
        /// <see cref="PluginDefinition.Validate"/> is where that requirement would belong.
        /// </para>
        /// </summary>
        private void SweepDashPath(long tickIndex)
        {
            if (_dash == null && owner != null)
                _dash = owner.GetComponent<IDashState>();   // the usual case: it is the player

            if (_dash == null || owner == null || !owner.IsAlive)
                return;

            bool dashing = _dash.IsDashing;
            if (dashing && !_wasDashing)
                _touchedByThisDash.Clear();

            // 돌진 후: the tick the dash ends. docs/03 G08 says 후, and a buff handed out while
            // the dash was still running would be spent on ticks the dash already moved fast
            // through - the player would feel nothing.
            if (!dashing && _wasDashing)
                _router.OnMovementAction(owner.ActorId, tickIndex);

            _wasDashing = dashing;
            if (!dashing)
                return;

            _dashBuffer.Clear();
            _router.Loadout.CollectTriggeredByDashContact(_dashBuffer);
            if (_dashBuffer.Count == 0)
                return;

            // The widest reach any carried dash card asks for, so the path is swept once rather
            // than once per card.
            float reach = 0f;
            for (int i = 0; i < _dashBuffer.Count; ++i)
                reach = Mathf.Max(reach, _dashBuffer[i].RadiusUnits);

            if (reach <= 0f)
                return;

            Collider[] touching = Physics.OverlapSphere(
                owner.transform.position, reach, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < touching.Length; ++i)
            {
                var actor = touching[i].GetComponentInParent<CombatActor>();
                if (actor == null || !actor.IsAlive || !owner.IsHostileTo(actor))
                    continue;

                // 한 대시에서 중복 접촉: the first contact of this dash is the only one.
                if (!_touchedByThisDash.Add(actor.ActorId))
                    continue;

                _router.OnDashContact(owner.ActorId, actor.ActorId, tickIndex);
            }
        }

        private void Resolve(ProcEvent evt, PluginDefinition plugin)
        {
            if (!_actorsById.TryGetValue(evt.TargetActorId, out CombatActor target) || target == null)
                return;

            _resolvedByEffectId.TryGetValue(evt.EffectId, out int resolved);
            _resolvedByEffectId[evt.EffectId] = resolved + 1;

            // 적중 효과 발생 for G13's counter, from the one place an effect is known to have
            // been allowed through rather than from where effects are merely offered to the
            // queue. An effect the rules refused did nothing, and letting it charge a burst
            // would turn refusals into damage.
            //
            // R04 is NOT offered from here. The router does that itself once this returns, so
            // the rule that a reactor never reacts to itself cannot be lost by a caller.
            CountHitEffectTowardsBursts();

            switch (plugin.Effect)
            {
                case PluginEffectKind.ChainToNearbyEnemy:
                    ChainOnward(evt, plugin, target);
                    break;
                case PluginEffectKind.ExplodeAroundTarget:
                    Explode(plugin, target);
                    break;
                case PluginEffectKind.MarkTarget:
                    Mark(plugin, target);
                    break;
                case PluginEffectKind.DamageTarget:
                    Strike(target, plugin, HitOrigin.Skill);
                    break;
                case PluginEffectKind.FireProjectile:
                    FireRespondingShot(plugin);
                    break;
                case PluginEffectKind.KnockbackAroundOwner:
                    ShoveEverybodyBack(plugin);
                    break;
                case PluginEffectKind.GrantShield:
                    RaiseShield(plugin);
                    break;
            }
        }

        /// <summary>
        /// G10: hits the nearest other hostile. The hop is enqueued again by the hit it lands,
        /// with this event's ancestry attached, so the guard can refuse a hop back onto an enemy
        /// already in the chain.
        /// </summary>
        private void ChainOnward(ProcEvent evt, in PluginDefinition plugin, CombatActor from)
        {
            CombatActor next = NearestHostileOtherThan(from, plugin.RadiusUnits, evt.TargetActorId);
            if (next == null)
                return;

            Strike(next, plugin, HitOrigin.Skill);
        }

        /// <summary>G17: everything hostile around the target, the target included.</summary>
        private void Explode(in PluginDefinition plugin, CombatActor at)
        {
            _damagedByExplosion.Clear();
            Collider[] inside = Physics.OverlapSphere(
                at.transform.position, plugin.RadiusUnits, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < inside.Length; ++i)
            {
                var actor = inside[i].GetComponentInParent<CombatActor>();
                if (actor == null || !actor.IsAlive || owner == null || !owner.IsHostileTo(actor))
                    continue;
                if (!_damagedByExplosion.Add(actor.ActorId))
                    continue;

                Strike(actor, plugin, HitOrigin.Skill);
            }
        }

        /// <summary>G12: the mark itself, applied through the target so its tenacity applies.</summary>
        private void Mark(in PluginDefinition plugin, CombatActor target)
        {
            if (!target.IsAlive)
                return;

            target.ApplyStatus(
                plugin.AppliedStatus, plugin.StatusMagnitude, 0f, plugin.StatusDurationTicksOverride);
        }

        private void Strike(CombatActor target, in PluginDefinition plugin, HitOrigin origin)
        {
            if (owner == null || !target.IsAlive)
                return;

            // 적중 효과 피해: the shot that woke this effect decides how much of it lands.
            // Taken from the router, which holds the resolving event's fraction, so the card's
            // own data stays the full-strength version and every effect path gets the reduction
            // without each one remembering to apply it.
            DamageRequest request = plugin.Damage.Scaled(_router.ActiveOnHitEffectScale);

            DamageOutcome outcome = target.ReceiveDamage(
                request, owner.Stats, owner.RollCrit(request), isOnHitEvent: false);

            if (outcome.Lifesteal.Total > 0f)
                owner.Heal(outcome.Lifesteal.Total);

            // Reported like any other hit, which is what lets a plugin effect trigger another
            // plugin - and what makes the ancestry carried by the router load-bearing rather
            // than decorative. No scale on the report: the router is already holding this
            // chain's fraction, and passing it again would square it.
            owner.NotifyHitLanded(target, request, outcome, origin);
        }

        private CombatActor NearestHostileOtherThan(CombatActor from, float radiusUnits, int excludeActorId)
        {
            Collider[] nearby = Physics.OverlapSphere(
                from.transform.position, radiusUnits, ~0, QueryTriggerInteraction.Ignore);

            CombatActor best = null;
            float bestDistanceSqr = float.MaxValue;
            int bestId = int.MaxValue;

            for (int i = 0; i < nearby.Length; ++i)
            {
                var actor = nearby[i].GetComponentInParent<CombatActor>();
                if (actor == null || !actor.IsAlive || actor.ActorId == excludeActorId)
                    continue;
                if (owner == null || !owner.IsHostileTo(actor))
                    continue;

                float distanceSqr = (actor.transform.position - from.transform.position).sqrMagnitude;

                // Ties broken by the lower actor id rather than by collider order, which the
                // physics query does not promise to repeat.
                bool closer = distanceSqr < bestDistanceSqr - 1e-5f;
                bool tiedButLowerId = Mathf.Abs(distanceSqr - bestDistanceSqr) <= 1e-5f && actor.ActorId < bestId;
                if (!closer && !tiedButLowerId)
                    continue;

                best = actor;
                bestDistanceSqr = distanceSqr;
                bestId = actor.ActorId;
            }

            return best;
        }
    }
}
