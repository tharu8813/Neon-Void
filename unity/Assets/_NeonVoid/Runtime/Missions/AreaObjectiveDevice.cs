using System;
using NeonVoid.Combat;
using UnityEngine;

namespace NeonVoid.Missions
{
    /// <summary>
    /// A 소목표 in the world: a device the player destroys, a station they hold, or one node of a
    /// chain they walk in order. Which of the three it is comes from
    /// <see cref="AreaObjectives"/>, per hardware.
    /// <para>
    /// <b>It completes exactly once.</b> docs/01 section 6.6: 완료 보상은 반복 호출에도 정확히
    /// 1회. The flag is here and not only in the flow, because the flow counts objectives without
    /// knowing which device each one came from - so two reports from one device would be two
    /// objectives, and an area with three devices could be cleared by destroying one of them
    /// three times if anything ever revived it.
    /// </para>
    /// <para>
    /// A Hold or Sequence objective has no <see cref="CombatActor"/> at all, deliberately: a
    /// station the player can shoot down is an objective they can make impossible, and the area
    /// has no way to put one back. The dwell is ticked by
    /// <see cref="HardwareAreaRuntime"/> on the 60 Hz logic tick, like the 정화 콘솔 and the
    /// 철수 패드 - not from a trigger volume, which fires between ticks and would make the
    /// moment an objective completed depend on the frame rate.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaObjectiveDevice : MonoBehaviour
    {
        [Header("What this objective is")]
        [Tooltip("[초안] Destroy shoots a device; Hold and Sequence are stood on. See AreaObjectives.")]
        [SerializeField] private AreaObjectiveKind kind = AreaObjectiveKind.Destroy;

        [Tooltip("[초안] which of docs/01's objective candidates this is. Display and logs only.")]
        [SerializeField] private string objectiveName = "과열 밸브 차단";

        [Header("Destroy")]
        [Tooltip("The device's actor. Found on this object when left empty. Destroy only.")]
        [SerializeField] private CombatActor device;

        [Header("Hold and Sequence")]
        [Tooltip("Where the player has to stand. Required for Hold and Sequence.")]
        [SerializeField] private Transform station;

        [Tooltip("[초안] how close counts as standing at the station.")]
        [SerializeField, Min(0.1f)] private float holdRadiusUnits = AreaObjectives.DraftHoldRadiusUnits;

        [Tooltip("[초안] ticks of continuous presence the station asks for.")]
        [SerializeField, Min(1)] private int holdTicks = AreaObjectives.DraftHoldTicks;

        [Tooltip("This node's place in the area's chain. Sequence only; -1 otherwise.")]
        [SerializeField] private int chainIndex = -1;

        private DwellGate _dwell;
        private bool _completed;
        private bool _lit;
        private bool _subscribed;

        /// <summary>Raised once, when this objective is completed. The area runtime listens.</summary>
        public event Action<AreaObjectiveDevice> Completed;

        /// <summary>What the player has to do here.</summary>
        public AreaObjectiveKind Kind => kind;

        /// <summary>Whether this objective has been completed. Never goes back to false.</summary>
        public bool IsCompleted => _completed;

        /// <summary>[초안] display name, for the HUD and the log.</summary>
        public string ObjectiveName => objectiveName;

        /// <summary>The actor that has to be destroyed, or null for a Hold or Sequence.</summary>
        public CombatActor Device => device;

        /// <summary>Where the player stands, or null for a Destroy.</summary>
        public Transform Station => station;

        /// <summary>This node's place in the chain, or -1 when it is not a chain node.</summary>
        public int ChainIndex => chainIndex;

        /// <summary>The dwell, for a progress ring. Null for a Destroy.</summary>
        public DwellGate Dwell => _dwell;

        /// <summary>
        /// A chain node that has been held and accepted, but has reported nothing to the flow
        /// yet: the chain only lands when its last node does. Always false for the other kinds.
        /// </summary>
        public bool IsLit => _lit;

        /// <summary>Whether the player is standing here right now. False for a Destroy.</summary>
        public bool IsOccupied => _dwell != null && _dwell.IsOccupied;

        /// <summary>
        /// Names the actor to destroy. The injection seam for a test and for anything that builds
        /// an area at runtime; call it while this object is still deactivated, so
        /// <see cref="OnEnable"/> subscribes to the actor rather than to nothing.
        /// </summary>
        public void Configure(CombatActor deviceActor, string displayName = null)
        {
            kind = AreaObjectiveKind.Destroy;
            device = deviceActor;
            station = null;
            chainIndex = -1;
            if (!string.IsNullOrWhiteSpace(displayName))
                objectiveName = displayName;
        }

        /// <summary>
        /// Makes this a Hold or Sequence objective at <paramref name="at"/>. Call it while the
        /// object is still deactivated, like <see cref="Configure"/>.
        /// </summary>
        public void ConfigureStation(in AreaObjectiveSpec spec, Transform at, float radiusUnits, int ticks)
        {
            if (spec.Kind == AreaObjectiveKind.Destroy)
            {
                throw new ArgumentException(
                    $"{spec.Name} is a Destroy objective, so it needs Configure with an actor.", nameof(spec));
            }

            if (at == null)
                throw new ArgumentNullException(nameof(at), "A station the player cannot find is not an objective.");

            kind = spec.Kind;
            objectiveName = spec.Name;
            chainIndex = spec.ChainIndex;
            station = at;
            device = null;
            holdRadiusUnits = radiusUnits;
            holdTicks = ticks;
            _dwell = new DwellGate(radiusUnits, ticks);
        }

        /// <summary>
        /// Advances a Hold or Sequence dwell by one tick and says whether it <b>filled on this
        /// tick</b>. True once per dwell, as <see cref="DwellGate"/> defines it.
        /// <para>
        /// What happens next is not this component's business: a Hold completes, while a chain
        /// node has to ask the chain whether it was its turn. The area runtime owns that, because
        /// it is the only thing that can see every node of the chain.
        /// </para>
        /// </summary>
        public bool TickDwell(float distanceFromStation)
        {
            if (_dwell == null || _completed)
                return false;

            return _dwell.Tick(distanceFromStation);
        }

        /// <summary>Empties the dwell and puts the light out. Used when a chain is broken.</summary>
        public void ResetDwell()
        {
            if (_completed)
                return;

            _dwell?.Reset();
            _lit = false;
        }

        /// <summary>Marks a chain node as held and accepted, with nothing reported yet.</summary>
        public void Light()
        {
            if (kind != AreaObjectiveKind.Sequence)
            {
                Debug.LogWarning($"{name}: only a chain node can be lit, and this is a {kind}.", this);
                return;
            }

            _lit = true;
        }

        /// <summary>
        /// Reports this objective complete. Called by the area runtime for a Hold, and for every
        /// node of a chain on the tick the chain lands. Idempotent, like the destruction path.
        /// </summary>
        public void Complete()
        {
            if (kind == AreaObjectiveKind.Destroy)
            {
                Debug.LogWarning(
                    $"{name}: a Destroy objective completes when its device dies, not on request.", this);
                return;
            }

            Finish();
        }

        private void Awake()
        {
            if (kind == AreaObjectiveKind.Destroy)
            {
                if (device == null)
                    device = GetComponent<CombatActor>();

                if (device == null)
                {
                    Debug.LogError(
                        $"{name}: a Destroy objective needs a CombatActor to be destroyed, so this " +
                        "objective can never be completed.", this);
                }

                return;
            }

            if (station == null)
            {
                Debug.LogError(
                    $"{name}: a {kind} objective needs a station to stand at, so this objective can " +
                    "never be completed.", this);
                return;
            }

            _dwell ??= new DwellGate(holdRadiusUnits, holdTicks);
        }

        private void OnEnable()
        {
            if (kind != AreaObjectiveKind.Destroy || device == null || _subscribed)
                return;

            device.Died += OnDeviceDestroyed;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (device == null || !_subscribed)
                return;

            device.Died -= OnDeviceDestroyed;
            _subscribed = false;
        }

        private void OnDeviceDestroyed(CombatActor actor) => Finish();

        private void Finish()
        {
            if (_completed)
            {
                Debug.LogWarning(
                    $"{name}: {objectiveName} reported completion twice; ignored. " +
                    "완료 보상은 정확히 1회 (docs/01 section 6.6).", this);
                return;
            }

            _completed = true;
            _lit = false;
            Debug.Log($"Objective complete: {objectiveName}.", this);
            Completed?.Invoke(this);
        }

        public override string ToString()
        {
            string state = _completed ? "done" : _lit ? "lit" : "outstanding";
            return $"{objectiveName} [{kind}] {state}";
        }
    }
}
