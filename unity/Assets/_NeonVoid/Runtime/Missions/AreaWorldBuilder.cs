using System;
using System.Collections.Generic;
using NeonVoid.Combat;
using NeonVoid.Missions.Generation;
using UnityEngine;

namespace NeonVoid.Missions
{
    /// <summary>What was built, so a caller can drive it and a test can assert about it.</summary>
    public sealed class AreaBuildResult
    {
        public AreaBuildResult(
            GameObject root,
            AreaLayout layout,
            HardwareAreaRuntime runtime,
            AreaObjectiveDevice[] devices,
            AreaBossGate bossGate,
            Transform purificationConsole,
            Transform extractionPad,
            int floorSlabs,
            int wallSlabs)
        {
            Root = root;
            Layout = layout;
            Runtime = runtime;
            Devices = devices;
            BossGate = bossGate;
            PurificationConsole = purificationConsole;
            ExtractionPad = extractionPad;
            FloorSlabs = floorSlabs;
            WallSlabs = wallSlabs;
        }

        /// <summary>Everything built, under one object, so it can be torn down in one call.</summary>
        public GameObject Root { get; }

        public AreaLayout Layout { get; }

        public HardwareAreaRuntime Runtime { get; }

        public AreaObjectiveDevice[] Devices { get; }

        public AreaBossGate BossGate { get; }

        public Transform PurificationConsole { get; }

        public Transform ExtractionPad { get; }

        /// <summary>Boxes the floor was built from, after merging. For the performance report.</summary>
        public int FloorSlabs { get; }

        /// <summary>Boxes the wall was built from, after merging.</summary>
        public int WallSlabs { get; }

        public override string ToString() =>
            $"{Layout.Spec.Hardware}: {Devices.Length} objectives, {FloorSlabs} floor + {WallSlabs} wall slabs";
    }

    /// <summary>
    /// Builds a playable room from an <see cref="AreaLayout"/>: floor, walls, the objective
    /// devices, the boss behind its gate, the 정화 콘솔, the 철수 패드, and the
    /// <see cref="HardwareAreaRuntime"/> that joins them to the mission.
    /// <para>
    /// This is the stage docs/01 section 6 puts after the graph: 최종 3D 프리팹/조명/벽은
    /// 통과한 그래프 위에 꾸미는 표현 단계. It closes the chain - seed → layout → room → play →
    /// flow → save - and it decides nothing about the map. If the room is wrong, the layout was
    /// wrong, and the layout has its own tests.
    /// </para>
    /// <para>
    /// <b>Prefabs where there are prefabs, primitives where there are not.</b> A device and a
    /// boss are content: what they look like, what they are worth, how the boss fights. Where a
    /// prefab is given this places it; where none is, it builds a marked
    /// <c>Prototype</c> primitive from serialized stat blocks, so the loop can be played and
    /// measured before the content exists. The stats are serialized data rather than numbers in
    /// code, per CLAUDE.md, and they are [초안] - D-015 owns what a CPU area actually contains.
    /// </para>
    /// <para>
    /// <b>No enemies, and no boss fight.</b> docs/01 section 3 asks each boss for 최소 3개의
    /// 형태가 다른 패턴 + 1개 지역 기믹 + 1개 페이즈 변화, and section 2 gives every area its own
    /// 적 성향. None of that is here: this boss can be shot and can die, which is what the flow
    /// needs to be driven end to end. The fight goes behind the gate that already holds it.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaWorldBuilder : MonoBehaviour
    {
        [Header("Where")]
        [Tooltip("Added to every position, so an area can be built clear of anything else in the scene.")]
        [SerializeField] private Vector3 worldOrigin = Vector3.zero;

        [Header("Terrain [초안]")]
        [Tooltip("Build the floor and the walls. Off when something else owns the geometry.")]
        [SerializeField] private bool buildTerrain = true;

        [Tooltip("[초안] floor thickness. The top surface sits at the origin's height.")]
        [SerializeField, Min(0.05f)] private float floorThicknessUnits = 0.5f;

        [Tooltip("[초안] wall height. Tall enough to stop a shot, short enough to see over.")]
        [SerializeField, Min(0.5f)] private float wallHeightUnits = 3f;

        [Header("Contents")]
        [Tooltip("Objective device prefab. A Prototype primitive is built when empty.")]
        [SerializeField] private GameObject objectiveDevicePrefab;

        [Tooltip("Boss prefab. A Prototype primitive is built when empty.")]
        [SerializeField] private GameObject bossPrefab;

        [Tooltip("Team the devices and the boss belong to, so the player's fire can reach them.")]
        [SerializeField] private int hostileTeamId = 1;

        [Header("Prototype stats [초안] - used only without a prefab")]
        [Tooltip("[초안] what an objective device is made of. D-015 owns the real answer.")]
        [SerializeField] private CombatStats prototypeDeviceStats = PrototypeDeviceStats();

        [Tooltip("[초안] what the area boss is made of.")]
        [SerializeField] private CombatStats prototypeBossStats = PrototypeBossStats();

        [Tooltip("[초안] how wide a Prototype device or boss stands, in units.")]
        [SerializeField, Min(0.2f)] private float prototypeBodyWidthUnits = 1.6f;

        /// <summary>
        /// [초안] a device sturdy enough to need a few seconds of fire and not so sturdy that it
        /// is the area's real fight. Replaced by a prefab, and by D-015 before that.
        /// </summary>
        public static CombatStats PrototypeDeviceStats()
        {
            CombatStats stats = CombatStats.StartingVaccine();
            stats.MaxHealth = 240f;
            stats.AttackDamage = 0f;
            return stats;
        }

        /// <summary>[초안] a boss that takes longer than a device and does nothing else yet.</summary>
        public static CombatStats PrototypeBossStats()
        {
            CombatStats stats = CombatStats.StartingVaccine();
            stats.MaxHealth = 1200f;
            stats.PhysicalArmor = 10f;
            return stats;
        }

        /// <summary>Where the area's cell (0,0) begins in world space.</summary>
        public Vector3 WorldOrigin
        {
            get => worldOrigin;
            set => worldOrigin = value;
        }

        /// <summary>
        /// Builds the area and moves the player to its entrance.
        /// </summary>
        /// <param name="layout">A layout that has passed <see cref="AreaLayoutValidator"/>.</param>
        /// <param name="mission">The mission the area reports to.</param>
        /// <param name="player">
        /// Moved to the entrance. Null leaves the player where they are, which is only useful to
        /// a test that is about the geometry.
        /// </param>
        public AreaBuildResult Build(AreaLayout layout, MissionRunDirector mission, Transform player)
        {
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));

            // A map that has not passed cannot be built: docs/01 section 6's checks exist so a
            // player is never handed an area they cannot finish, and building one anyway would
            // make the checks advisory.
            AreaLayoutVerdict verdict = AreaLayoutValidator.Validate(layout);
            if (!verdict.IsValid)
                throw new ArgumentException($"that layout did not pass validation: {verdict.Reason}", nameof(layout));

            var root = new GameObject($"Area {layout.Spec.Hardware}");
            root.transform.position = Vector3.zero;

            int floorSlabs = 0;
            int wallSlabs = 0;
            if (buildTerrain)
            {
                floorSlabs = BuildFloor(layout, root.transform);
                wallSlabs = BuildWalls(layout, root.transform);
            }

            var devices = new AreaObjectiveDevice[layout.Objectives.Count];
            for (int i = 0; i < devices.Length; ++i)
            {
                CombatActor actor = SpawnBody(
                    objectiveDevicePrefab, prototypeDeviceStats, PrimitiveType.Cube,
                    $"과열 밸브 {i + 1}", layout, layout.Objectives[i], root.transform);

                devices[i] = AddAsleep<AreaObjectiveDevice>(
                    $"Objective {i + 1}", root.transform,
                    d => d.Configure(actor, $"과열 밸브 {i + 1}"));
            }

            CombatActor bossActor = SpawnBody(
                bossPrefab, prototypeBossStats, PrimitiveType.Capsule,
                "코어 워처", layout, layout.Boss, root.transform);

            // The gate goes on its own object: it deactivates what it holds, and a gate that
            // deactivated itself could not be told to open again.
            AreaBossGate bossGate = AddAsleep<AreaBossGate>(
                "Boss Gate", root.transform,
                g => g.Configure(bossActor, bossActor.gameObject, "코어 워처"));

            Transform console = Marker("정화 콘솔", layout, layout.Purification, root.transform);
            Transform pad = Marker("철수 패드", layout, layout.Extraction, root.transform);

            if (player != null)
                player.position = PositionOf(layout, layout.Start) + (Vector3.up * PlayerStandingLiftUnits);

            HardwareAreaRuntime runtime = AddAsleep<HardwareAreaRuntime>(
                "Area Runtime", root.transform,
                r => r.Configure(layout.Spec.Hardware, mission, player, devices, bossGate, console, pad));

            Debug.Log(
                $"Built {layout.Spec.Hardware} from {layout.ReceiptLine()}: " +
                $"{floorSlabs} floor slab(s), {wallSlabs} wall slab(s), {devices.Length} objective(s).",
                root);

            return new AreaBuildResult(
                root, layout, runtime, devices, bossGate, console, pad, floorSlabs, wallSlabs);
        }

        /// <summary>
        /// [초안] how far above the floor the player is placed. A CharacterController spawned
        /// exactly on the surface starts the frame intersecting it and is pushed somewhere.
        /// </summary>
        public const float PlayerStandingLiftUnits = 1.1f;

        private int BuildFloor(AreaLayout layout, Transform parent)
        {
            List<AreaRoom> slabs = AreaTerrain.FloorSlabs(layout.Grid);
            for (int i = 0; i < slabs.Count; ++i)
            {
                // The top surface at the origin's height, so the floor is where the player
                // stands rather than where they are half buried.
                Box($"Floor {i}", layout, slabs[i], floorThicknessUnits, -floorThicknessUnits * 0.5f, parent);
            }

            return slabs.Count;
        }

        private int BuildWalls(AreaLayout layout, Transform parent)
        {
            List<AreaRoom> slabs = AreaTerrain.WallSlabs(layout.Grid);
            for (int i = 0; i < slabs.Count; ++i)
                Box($"Wall {i}", layout, slabs[i], wallHeightUnits, wallHeightUnits * 0.5f, parent);

            return slabs.Count;
        }

        /// <summary>One merged rectangle as a box, centred on the cells it covers.</summary>
        private void Box(string name, AreaLayout layout, in AreaRoom slab, float height, float centreY, Transform parent)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, worldPositionStays: false);

            float cell = layout.CellSizeUnits;
            Vector3 min = PositionOf(layout, new AreaCell(slab.MinX, slab.MinY));
            Vector3 max = PositionOf(layout, new AreaCell(slab.MaxX, slab.MaxY));

            box.transform.position = new Vector3((min.x + max.x) * 0.5f, worldOrigin.y + centreY, (min.z + max.z) * 0.5f);
            box.transform.localScale = new Vector3(slab.Width * cell, height, slab.Height * cell);
        }

        /// <summary>An empty transform the dwell gates measure against.</summary>
        private Transform Marker(string name, AreaLayout layout, AreaCell cell, Transform parent)
        {
            var marker = new GameObject(name);
            marker.transform.SetParent(parent, worldPositionStays: false);
            marker.transform.position = PositionOf(layout, cell);
            return marker.transform;
        }

        /// <summary>
        /// The prefab, or a Prototype primitive when there is none. Either way the result is a
        /// <see cref="CombatActor"/> standing on the given cell, hostile to the player so their
        /// fire can reach it.
        /// </summary>
        private CombatActor SpawnBody(
            GameObject prefab,
            in CombatStats prototypeStats,
            PrimitiveType prototypeShape,
            string name,
            AreaLayout layout,
            AreaCell cell,
            Transform parent)
        {
            Vector3 position = PositionOf(layout, cell);

            if (prefab != null)
            {
                GameObject spawned = Instantiate(prefab, position, Quaternion.identity, parent);
                spawned.name = name;

                var fromPrefab = spawned.GetComponentInChildren<CombatActor>();
                if (fromPrefab == null)
                {
                    // Nothing can destroy it and nothing can kill it, so the area would be
                    // unclearable. Loud, and not silently replaced with a primitive: a prefab
                    // was given and it is the wrong prefab.
                    throw new InvalidOperationException(
                        $"{prefab.name} has no CombatActor, so {name} could never be destroyed.");
                }

                return fromPrefab;
            }

            GameObject body = GameObject.CreatePrimitive(prototypeShape);
            body.name = $"{name} (Prototype)";
            body.transform.SetParent(parent, worldPositionStays: false);
            body.transform.position = position + (Vector3.up * (prototypeBodyWidthUnits * 0.5f));
            body.transform.localScale = Vector3.one * prototypeBodyWidthUnits;

            // The primitive keeps its own collider: that is what a shot's SphereCastAll finds,
            // and CombatActor is looked up from it with GetComponentInParent.
            var actor = body.AddComponent<CombatActor>();
            body.AddComponent<ActorDeathReaction>();
            actor.Configure(hostileTeamId, prototypeStats);
            return actor;
        }

        /// <summary>
        /// Adds a component to a deactivated child and then wakes it, so its Awake sees the
        /// references it was configured with instead of searching the scene for them.
        /// </summary>
        private static T AddAsleep<T>(string name, Transform parent, Action<T> configure)
            where T : Component
        {
            var host = new GameObject(name);
            host.SetActive(false);
            host.transform.SetParent(parent, worldPositionStays: false);

            T component = host.AddComponent<T>();
            configure(component);
            host.SetActive(true);
            return component;
        }

        private Vector3 PositionOf(AreaLayout layout, AreaCell cell) => worldOrigin + layout.WorldPositionOf(cell);
    }
}
