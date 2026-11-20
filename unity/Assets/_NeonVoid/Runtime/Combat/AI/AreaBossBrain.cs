using System;
using System.Collections.Generic;
using NeonVoid.Core;
using UnityEngine;

namespace NeonVoid.Combat.AI
{
    /// <summary>A telegraph that is up, for a HUD to draw.</summary>
    public readonly struct BossTelegraph
    {
        public readonly BossPattern Pattern;

        /// <summary>Where the shape is anchored, on the XZ plane.</summary>
        public readonly Vector2 OriginXZ;

        /// <summary>Which way it is aimed. Zero for a ring.</summary>
        public readonly Vector2 FacingXZ;

        /// <summary>0 to 1 through the warning.</summary>
        public readonly float Progress;

        /// <summary>Whether this is the 지역 기믹 rather than one of the boss's moves.</summary>
        public readonly bool IsGimmick;

        public BossTelegraph(in BossPattern pattern, Vector2 originXZ, Vector2 facingXZ, float progress, bool isGimmick)
        {
            Pattern = pattern;
            OriginXZ = originXZ;
            FacingXZ = facingXZ;
            Progress = progress;
            IsGimmick = isGimmick;
        }

        /// <summary>
        /// Whether a point is inside what this telegraph is showing. <b>The same call the
        /// resolution makes</b>, which is how docs/01's 전조 시각 표시와 실제 충돌 볼륨이 일치
        /// stops being a promise.
        /// </summary>
        public bool Covers(Vector2 pointXZ) => Pattern.Covers(OriginXZ, FacingXZ, pointXZ);
    }

    /// <summary>
    /// Runs a boss's <see cref="BossPatternCycle"/> in the scene: aims the telegraphs, resolves
    /// the attacks through the normal damage pipeline, and reports what is coming.
    /// <para>
    /// <b>Aimed when the telegraph starts, not when it lands.</b> docs/01 section 3 asks for a
    /// 피할 수 있는 텔레그래프, and an attack that re-aimed at the player on the tick it resolved
    /// would be unavoidable no matter how long its warning was - the warning would be showing one
    /// place and hitting another. So the origin and the facing are captured at the start of the
    /// telegraph and held, which also makes the stored <see cref="CurrentTelegraph"/> the exact
    /// volume that will resolve.
    /// </para>
    /// <para>
    /// <b>No VFX, and the judgement does not want any.</b> docs/01 requires that removing a boss's
    /// effects and sounds leave the combat judgement identical. Here there is nothing to remove:
    /// the shape is <see cref="BossPattern"/>'s numbers and the resolution reads them.
    /// </para>
    /// <para>
    /// The cycle itself is pure and tested on its own. This adds the subscription, the targeting
    /// and the logging - the thin layer, kept thin on purpose.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaBossBrain : MonoBehaviour, ISimulationTickReceiver
    {
        [Header("Who")]
        [Tooltip("The boss. Found on this object when left empty.")]
        [SerializeField] private CombatActor boss;

        [Tooltip("What its patterns aim at. The nearest hostile actor when left empty.")]
        [SerializeField] private CombatActor target;

        [Header("Reach")]
        [Tooltip("[초안] how far the boss looks for a target to aim at.")]
        [SerializeField, Min(1f)] private float aimRangeUnits = 30f;

        private readonly List<CombatActor> _hostiles = new List<CombatActor>();

        private SimulationRunner _runner;
        private BossPatternCycle _cycle;
        private BossTelegraph _telegraph;
        private BossTelegraph _gimmickTelegraph;
        private bool _hasTelegraph;
        private bool _hasGimmickTelegraph;

        /// <summary>Raised when a pattern begins announcing itself. Presentation only.</summary>
        public event Action<BossTelegraph> TelegraphBegan;

        /// <summary>Raised on the tick an attack lands, after the damage. Presentation only.</summary>
        public event Action<BossTelegraph> AttackResolved;

        /// <summary>Raised when the fight changes phase, for an announcement.</summary>
        public event Action<BossPhase> PhaseChanged;

        /// <summary>The fight's clock, or null before it is given one.</summary>
        public BossPatternCycle Cycle => _cycle;

        /// <summary>The boss's own actor.</summary>
        public CombatActor Boss => boss;

        /// <summary>The telegraph now showing, if any.</summary>
        public bool TryGetTelegraph(out BossTelegraph telegraph)
        {
            telegraph = _telegraph;
            return _hasTelegraph;
        }

        /// <summary>The 지역 기믹's telegraph, if it is announcing itself.</summary>
        public bool TryGetGimmickTelegraph(out BossTelegraph telegraph)
        {
            telegraph = _gimmickTelegraph;
            return _hasGimmickTelegraph;
        }

        /// <summary>
        /// Gives this boss its patterns. The injection seam: which boss belongs to which hardware
        /// area is the Missions domain's to decide, so <see cref="BossCatalog"/> does not.
        /// </summary>
        public void UseCycle(BossPatternCycle cycle)
        {
            _cycle = cycle ?? throw new ArgumentNullException(nameof(cycle));
            _hasTelegraph = false;
            _hasGimmickTelegraph = false;
        }

        /// <summary>Names the boss and what it aims at, before Awake.</summary>
        public void Configure(CombatActor bossActor, CombatActor aimAt = null)
        {
            boss = bossActor;
            target = aimAt;
        }

        private void Awake()
        {
            if (boss == null)
                boss = GetComponent<CombatActor>();

            if (boss == null)
            {
                Debug.LogError(
                    $"{name}: a boss brain needs a CombatActor; it cannot aim, take damage or die.", this);
            }
        }

        private void OnEnable()
        {
            _runner = _runner != null ? _runner : FindFirstObjectByType<SimulationRunner>();
            if (_runner == null)
            {
                Debug.LogError($"{name}: no SimulationRunner, so this boss would never act.", this);
                return;
            }

            _runner.Register(this);
            RefreshHostiles();
        }

        private void OnDisable()
        {
            if (_runner != null)
                _runner.Unregister(this);
        }

        /// <summary>
        /// Rebuilds the list of actors this boss may hit. On enable rather than per tick, like
        /// <see cref="EnemyAgent.RefreshKnownActors"/>: scanning the scene per boss per tick is
        /// quadratic, and the roster of a boss room does not change mid-fight.
        /// </summary>
        public void RefreshHostiles()
        {
            _hostiles.Clear();
            if (boss == null)
                return;

            CombatActor[] everyActor = FindObjectsByType<CombatActor>(FindObjectsSortMode.None);
            for (int i = 0; i < everyActor.Length; ++i)
            {
                if (boss.IsHostileTo(everyActor[i]))
                    _hostiles.Add(everyActor[i]);
            }
        }

        int ISimulationTickReceiver.TickOrder => SimulationRunner.Order.Actors;

        void ISimulationTickReceiver.OnSimulationTick(long tickIndex)
        {
            if (_cycle == null || boss == null)
                return;

            if (!boss.IsAlive)
            {
                if (_cycle.Activity != BossActivity.Finished)
                {
                    _cycle.Finish();
                    _hasTelegraph = false;
                    _hasGimmickTelegraph = false;
                }

                return;
            }

            // Fed in rather than read by the cycle, which has no Unity types to read it with.
            if (_cycle.ReportHealthFraction(boss.Health.Max > 0f ? boss.Health.Current / boss.Health.Max : 1f))
            {
                Debug.Log($"{name}: {_cycle.Phase}. 오버클럭 중 속도 증가, 대신 냉각 창.", this);
                PhaseChanged?.Invoke(_cycle.Phase);
            }

            BossActivity before = _cycle.Activity;
            bool gimmickWasTelegraphing = _cycle.IsGimmickTelegraphing;

            BossTickOutcome outcome = _cycle.Tick();

            if (outcome.PhaseChanged)
            {
                Debug.Log($"{name}: {_cycle.Phase}.", this);
                PhaseChanged?.Invoke(_cycle.Phase);
            }

            // A telegraph that has just gone up is aimed now and never again - see the type's
            // note on why.
            if (_cycle.IsTelegraphing && (before != BossActivity.Telegraphing || !_hasTelegraph))
            {
                _telegraph = AimPattern(_cycle.CurrentPattern, isGimmick: false);
                _hasTelegraph = true;
                TelegraphBegan?.Invoke(_telegraph);
            }
            else if (_cycle.IsTelegraphing)
            {
                _telegraph = new BossTelegraph(
                    _telegraph.Pattern, _telegraph.OriginXZ, _telegraph.FacingXZ,
                    _cycle.TelegraphProgress, false);
            }
            else
            {
                _hasTelegraph = false;
            }

            if (_cycle.IsGimmickTelegraphing && !gimmickWasTelegraphing)
            {
                _gimmickTelegraph = AimPattern(_cycle.Gimmick, isGimmick: true);
                _hasGimmickTelegraph = true;
                TelegraphBegan?.Invoke(_gimmickTelegraph);
            }
            else if (!_cycle.IsGimmickTelegraphing)
            {
                _hasGimmickTelegraph = false;
            }

            if (outcome.ResolvedPatternIndex >= 0)
                Resolve(_telegraph);

            if (outcome.ResolvedGimmick)
                Resolve(_gimmickTelegraph);
        }

        /// <summary>
        /// Captures where a shape stands and which way it points. A ring is given no facing: it
        /// has none, and pretending otherwise would let a bug in the aiming change a ring's
        /// coverage without anything looking wrong.
        /// </summary>
        private BossTelegraph AimPattern(in BossPattern pattern, bool isGimmick)
        {
            Vector3 position = transform.position;
            var origin = new Vector2(position.x, position.z);

            if (pattern.Shape == BossPatternShape.Ring)
                return new BossTelegraph(pattern, origin, Vector2.zero, 0f, isGimmick);

            CombatActor aimAt = target != null && target.IsAlive ? target : NearestHostile(origin);
            Vector2 facing;
            if (aimAt != null)
            {
                Vector3 towards = aimAt.transform.position;
                facing = new Vector2(towards.x - origin.x, towards.z - origin.y);
                if (facing.sqrMagnitude <= 1e-8f)
                    facing = new Vector2(transform.forward.x, transform.forward.z);
            }
            else
            {
                facing = new Vector2(transform.forward.x, transform.forward.z);
            }

            return new BossTelegraph(pattern, origin, facing, 0f, isGimmick);
        }

        private CombatActor NearestHostile(Vector2 originXZ)
        {
            CombatActor nearest = null;
            float nearestSqr = aimRangeUnits * aimRangeUnits;

            for (int i = 0; i < _hostiles.Count; ++i)
            {
                CombatActor candidate = _hostiles[i];
                if (candidate == null || !candidate.IsAlive)
                    continue;

                Vector3 at = candidate.transform.position;
                float distanceSqr = new Vector2(at.x - originXZ.x, at.z - originXZ.y).sqrMagnitude;
                if (distanceSqr > nearestSqr)
                    continue;

                nearestSqr = distanceSqr;
                nearest = candidate;
            }

            return nearest;
        }

        /// <summary>
        /// Hits everything the telegraph covered. Through <see cref="CombatActor.ReceiveDamage"/>
        /// and <see cref="CombatActor.NotifyHitLanded"/> like any other attack, so armour,
        /// resistance, status and the plugins that react to being hit all apply - a boss that
        /// subtracted health directly would be the one attacker in the game the rules did not
        /// cover.
        /// </summary>
        private void Resolve(in BossTelegraph telegraph)
        {
            int hits = 0;
            for (int i = 0; i < _hostiles.Count; ++i)
            {
                CombatActor victim = _hostiles[i];
                if (victim == null || !victim.IsAlive)
                    continue;

                Vector3 at = victim.transform.position;
                if (!telegraph.Covers(new Vector2(at.x, at.z)))
                    continue;

                DamageOutcome result = victim.ReceiveDamage(telegraph.Pattern.Damage, boss.Stats, critRolled: false);
                boss.NotifyHitLanded(victim, telegraph.Pattern.Damage, result, HitOrigin.Skill);
                ++hits;
            }

            Debug.Log($"{name}: {telegraph.Pattern.Name} resolved, {hits} hit(s).", this);
            AttackResolved?.Invoke(telegraph);
        }
    }
}
