using NeonVoid.Combat;
using NeonVoid.Core;
using UnityEngine;

namespace NeonVoid.Missions
{
    /// <summary>Where a hazard is in its cycle.</summary>
    public enum AreaHazardPhase
    {
        /// <summary>Quiet. Nothing is drawn and nothing can be hurt.</summary>
        Resting = 0,

        /// <summary>The warning is up. Still nothing can be hurt.</summary>
        Telegraphing = 1,

        /// <summary>Dangerous.</summary>
        Active = 2,
    }

    /// <summary>
    /// One 환경 상호작용 standing in a room: it warns, it goes off, it rests, and it does it
    /// again on the 60 Hz tick.
    /// <para>
    /// <b>The warning is a phase, not an effect.</b> [확정] docs/01 section 2: <i>바닥에 예고선이
    /// 먼저 나타나고 실제 레이저 판정은 늦게 발생. 비디오 이펙트만 반짝이고 판정 없는 것 금지</i>,
    /// and the same section's 합격 line asks that the danger read <i>이펙트 품질 최저에서도</i>.
    /// So the telegraph is counted in ticks by this component and the visual is drawn from the
    /// phase - turning every effect off would make the hazard invisible, never harmless, and
    /// never early.
    /// </para>
    /// <para>
    /// <b>It damages on the tick, not on a trigger.</b> A trigger would fire at the physics
    /// rate and hurt more on a faster machine, which is the whole reason this project has a
    /// fixed tick. The sweep is an overlap at the hazard's own position, once per active tick.
    /// </para>
    /// <para>
    /// <b>It does not hurt the thing that owns the room.</b> Enemies stand in these rooms and
    /// docs/01 says nothing about hazards killing them, so it damages the player's team only -
    /// an area that cleared itself while the player watched would be a different game.
    /// </para>
    /// </summary>
    public sealed class AreaHazard : MonoBehaviour, ISimulationTickReceiver
    {
        private AreaHazardShape _shape;
        private SimulationRunner _runner;
        private DeterministicRng _rng;
        private Vector3[] _stations = new Vector3[0];
        private int _station;
        private int _ticksInPhase;
        private bool _configured;

        int ISimulationTickReceiver.TickOrder => SimulationRunner.Order.Effects + 3;

        /// <summary>What it is and how it behaves.</summary>
        public AreaHazardShape Shape => _shape;

        /// <summary>Where it is in its cycle right now.</summary>
        public AreaHazardPhase Phase { get; private set; } = AreaHazardPhase.Resting;

        /// <summary>Ticks spent in the current phase, for a ring or a bar.</summary>
        public int TicksInPhase => _ticksInPhase;

        /// <summary>How far through the current phase it is, 0 to 1. For a telegraph ring.</summary>
        public float PhaseProgress
        {
            get
            {
                int length = LengthOf(Phase);
                return length <= 0 ? 0f : Mathf.Clamp01(_ticksInPhase / (float)length);
            }
        }

        /// <summary>Whether standing here right now costs health.</summary>
        public bool IsDangerous => Phase == AreaHazardPhase.Active;

        /// <summary>How many actors it has hurt, for a test and for a log.</summary>
        public int HitsDealt { get; private set; }

        /// <summary>
        /// Sets it up. <paramref name="stations"/> are the places it may stand; a hazard that
        /// does not move uses the first and ignores the rest.
        /// </summary>
        /// <param name="station">
        /// Which of the stations this one starts at. Given rather than assumed: every hazard in
        /// an area is handed the same station list so a mover has somewhere to go, and a
        /// Configure that put them all at the first one stacked the whole area's hazards into a
        /// single point - which is exactly what it did until this was a parameter.
        /// </param>
        public void Configure(in AreaHazardShape shape, Vector3[] stations, int station, uint seed)
        {
            shape.Validate();

            _shape = shape;
            _stations = stations != null && stations.Length > 0
                ? stations
                : new[] { transform.position };
            _rng = new DeterministicRng(seed == 0u ? 1u : seed);
            _station = station >= 0 && station < _stations.Length ? station : 0;
            transform.position = _stations[_station];

            // Starts resting, and each one starts at a different point in its rest so a room
            // full of hazards does not pulse in unison - which would make a room one big hazard
            // with gaps rather than several to read separately.
            Phase = AreaHazardPhase.Resting;
            _ticksInPhase = shape.RestTicks > 0 ? (int)(_rng.NextUInt() % (uint)shape.RestTicks) : 0;
            _configured = true;
        }

        private void OnEnable()
        {
            _runner = _runner != null ? _runner : FindFirstObjectByType<SimulationRunner>();
            if (_runner != null)
                _runner.Register(this);
        }

        private void OnDisable()
        {
            if (_runner != null)
                _runner.Unregister(this);
        }

        void ISimulationTickReceiver.OnSimulationTick(long tickIndex)
        {
            if (!_configured || _shape.IsNone)
                return;

            if (Phase == AreaHazardPhase.Active)
                Burn();

            if (++_ticksInPhase < LengthOf(Phase))
                return;

            _ticksInPhase = 0;
            Phase = NextAfter(Phase);

            // Moved while it is quiet, never while it is warning: a patch that stepped sideways
            // after drawing its line would be a telegraph that lied.
            if (Phase == AreaHazardPhase.Telegraphing && _shape.MovesBetweenCycles)
                MoveToAnotherStation();
        }

        private static AreaHazardPhase NextAfter(AreaHazardPhase phase)
        {
            switch (phase)
            {
                case AreaHazardPhase.Resting: return AreaHazardPhase.Telegraphing;
                case AreaHazardPhase.Telegraphing: return AreaHazardPhase.Active;
                default: return AreaHazardPhase.Resting;
            }
        }

        private int LengthOf(AreaHazardPhase phase)
        {
            switch (phase)
            {
                case AreaHazardPhase.Telegraphing: return _shape.TelegraphTicks;
                case AreaHazardPhase.Active: return _shape.ActiveTicks;
                default: return Mathf.Max(1, _shape.RestTicks);
            }
        }

        private void MoveToAnotherStation()
        {
            if (_stations.Length < 2)
                return;

            // A different one, so "moves" always means moved. Rolled from the hazard's own
            // stream, so the same area plays the same way twice.
            int next = (int)(_rng.NextUInt() % (uint)(_stations.Length - 1));
            if (next >= _station)
                next++;

            _station = next;
            transform.position = _stations[_station];
        }

        /// <summary>
        /// Damage, once per active tick, to the player's team only.
        /// <para>
        /// True damage: a hazard is the room, not an attacker, and letting 방어 reduce it would
        /// make the same patch survivable on one build and lethal on another for reasons the
        /// player cannot see on the floor.
        /// </para>
        /// </summary>
        private void Burn()
        {
            Collider[] inside = Physics.OverlapSphere(
                transform.position, _shape.RadiusUnits, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < inside.Length; ++i)
            {
                var actor = inside[i].GetComponentInParent<CombatActor>();
                if (actor == null || !actor.IsAlive || actor.TeamId != PlayerTeamId)
                    continue;

                actor.ReceiveDamage(
                    new DamageRequest { Flat = _shape.Damage, Type = DamageType.True },
                    CombatStats.StartingVaccine());
                HitsDealt++;
            }
        }

        /// <summary>
        /// The team a hazard hurts. The player's, and only the player's - see the type's note.
        /// </summary>
        public const int PlayerTeamId = 0;
    }
}
