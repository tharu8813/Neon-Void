extends Node3D
## Read-only P9 visuals for Python view.py::_render_screen_fx / draw_world.
## Source of truth remains C CombatSim. No collision, spawn, RNG or save writes.
const Catalog = preload("res://scripts/enemies/enemy_catalog.gd")
const SCALE: float = 0.01
const MAX_FRIENDLY: int = 1024
const MAX_HOSTILE: int = 320
const MAX_WARNINGS: int = 96
const MAX_BURSTS: int = 12
const FRIENDLY := Color("#ccff4d")
const HOSTILE := Color("#ff9fbb")
const WARN := Color("#e8ae50")
const DANGER := Color("#ff4d6d")
var friendly_trails: MultiMeshInstance3D
var hostile_trails: MultiMeshInstance3D
var warning_pool: Array[MeshInstance3D] = []
var burst_pool: Array[MeshInstance3D] = []
var bursts: Array[Dictionary] = []
var warning_safe_material: StandardMaterial3D
var warning_danger_material: StandardMaterial3D
var last_warning_count: int = 0

func _ready() -> void:
    friendly_trails = _create_trail_batch("FriendlyStreaks", MAX_FRIENDLY, FRIENDLY)
    hostile_trails = _create_trail_batch("HostileStreaks", MAX_HOSTILE, HOSTILE)
    warning_safe_material = _glow(WARN)
    warning_danger_material = _glow(DANGER)
    var ring := TorusMesh.new()
    ring.inner_radius = 0.965
    ring.outer_radius = 1.0
    ring.rings = 48
    ring.ring_segments = 5
    for i in range(MAX_WARNINGS):
        var node := MeshInstance3D.new()
        node.name = "AttackWarning%d" % i
        node.mesh = ring
        node.material_override = warning_safe_material
        node.visible = false
        add_child(node)
        warning_pool.append(node)
    for i in range(MAX_BURSTS):
        var pulse := MeshInstance3D.new()
        pulse.name = "TransientRing%d" % i
        pulse.mesh = ring
        pulse.material_override = _glow(Color("#6ff2ff"))
        pulse.visible = false
        add_child(pulse)
        burst_pool.append(pulse)

func _glow(tint: Color) -> StandardMaterial3D:
    var surface := StandardMaterial3D.new()
    surface.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
    surface.albedo_color = tint
    surface.emission_enabled = true
    surface.emission = tint
    surface.emission_energy_multiplier = 0.85
    surface.cull_mode = BaseMaterial3D.CULL_DISABLED
    return surface

func _create_trail_batch(name: String, capacity: int, tint: Color) -> MultiMeshInstance3D:
    var instance := MultiMeshInstance3D.new()
    instance.name = name
    var mesh := BoxMesh.new()
    mesh.size = Vector3.ONE
    var material := _glow(tint)
    material.vertex_color_use_as_albedo = true
    mesh.material = material
    var batch := MultiMesh.new()
    batch.transform_format = MultiMesh.TRANSFORM_3D
    batch.use_colors = true
    batch.mesh = mesh
    batch.instance_count = capacity
    batch.visible_instance_count = 0
    instance.multimesh = batch
    # Explicit 3D visibility bound for offscreen projectiles at spawn edges.
    instance.custom_aabb = AABB(Vector3(-2.0, -1.0, -2.0), Vector3(18.0, 4.0, 12.0))
    add_child(instance)
    return instance

func _render_trails(instance: MultiMeshInstance3D, shots: Array,
        tint: Color, lifetime: float, reduced: bool) -> void:
    var batch: MultiMesh = instance.multimesh
    var count: int = mini(batch.instance_count, shots.size())
    if reduced:
        batch.visible_instance_count = 0
        return
    for i in range(count):
        var shot: Dictionary = shots[i]
        var p: Vector2 = shot["pos"]
        var velocity: Vector2 = shot["vel"]
        var delta: Vector2 = velocity * lifetime
        var yaw: float = atan2(delta.x, delta.y)
        var length_world: float = maxf(0.005, delta.length() * SCALE)
        var centre: Vector2 = p - delta * 0.5
        var origin := Vector3(centre.x * SCALE, 0.145, centre.y * SCALE)
        var width: float = 0.030 if tint == FRIENDLY else 0.023
        var basis := Basis(Vector3.UP, yaw).scaled(Vector3(width, 0.014, length_world))
        batch.set_instance_transform(i, Transform3D(basis, origin))
        batch.set_instance_color(i, tint)
    batch.visible_instance_count = count

func sync_battle(friendly: Array, hostile: Array, telegraphs: Array,
        enemies: Array, reduced: bool = false) -> void:
    # Mirrors the Python friendly velocity streak (32 ms) and soft enemy tail.
    _render_trails(friendly_trails, friendly, FRIENDLY, 0.032, reduced)
    _render_trails(hostile_trails, hostile, HOSTILE, 0.015, reduced)
    var positions: Dictionary = {}
    for enemy in enemies:
        positions[int(enemy["id"])] = enemy
    var displayed: int = 0
    for warning in telegraphs:
        if displayed >= MAX_WARNINGS:
            break
        var owner: Dictionary = positions.get(int(warning["source_id"]), {})
        if owner.is_empty():
            continue
        var action: Dictionary = warning.get("action", {})
        var radius: float = Catalog.PULSE_RADIUS if str(action.get("behavior", "")) == "encryption_pulse" else float(owner["radius"]) + 20.0
        var pos: Vector2 = owner["pos"]
        var marker: MeshInstance3D = warning_pool[displayed]
        marker.visible = true
        marker.position = Vector3(pos.x * SCALE, 0.042, pos.y * SCALE)
        marker.scale = Vector3(radius * SCALE, 1.0, radius * SCALE)
        marker.material_override = warning_danger_material if float(warning["remaining"]) <= 0.18 and not reduced else warning_safe_material
        displayed += 1
    for i in range(displayed, MAX_WARNINGS):
        warning_pool[i].visible = false
    last_warning_count = displayed
    _advance_bursts(1.0 / 60.0, reduced)

func add_burst(logical_position: Vector2, kind: String = "impact") -> void:
    if bursts.size() >= MAX_BURSTS:
        return
    var color: Color = Color("#6ff2ff")
    var radius: float = 22.0
    var duration: float = 0.22
    match kind:
        "dash":
            radius = 27.0
        "kill":
            color = Color("#ff9fbb")
            radius = 18.0
        "boss":
            color = Color("#ff5cf0")
            radius = 52.0
            duration = 0.44
        "purify":
            color = Color("#3ff5a0")
            radius = 72.0
            duration = 0.46
        "pulse":
            color = Color("#6ff2ff")
            radius = 155.0
            duration = 0.25
        "hurt":
            color = DANGER
            radius = 26.0
    var instance: MeshInstance3D = burst_pool[bursts.size()]
    instance.material_override = _glow(color)
    instance.position = Vector3(logical_position.x * SCALE, 0.055, logical_position.y * SCALE)
    instance.visible = true
    bursts.append({"node": instance, "elapsed": 0.0, "duration": duration, "radius": radius})

func _advance_bursts(delta: float, reduced: bool) -> void:
    var surviving: Array[Dictionary] = []
    var total: int = bursts.size()
    for i in range(total):
        var burst: Dictionary = bursts[i]
        var age: float = float(burst["elapsed"]) + delta
        if age >= float(burst["duration"]):
            continue
        burst["elapsed"] = age
        surviving.append(burst)
    # Reassign pooled nodes only here, so removals never duplicate a node
    # or keep the oldest index occupied indefinitely.
    bursts = surviving
    for i in range(MAX_BURSTS):
        var mesh: MeshInstance3D = burst_pool[i]
        if i >= bursts.size():
            mesh.visible = false
            continue
        var value: Dictionary = bursts[i]
        var node: MeshInstance3D = value["node"]
        if node != mesh:
            # Preserve each pulse's color when shifting the pool after expiry.
            var material: Material = node.material_override
            mesh.material_override = material
            bursts[i]["node"] = mesh
        var progress: float = float(value["elapsed"]) / maxf(0.01, float(value["duration"]))
        var px: float = float(value["radius"]) * (1.0 + (0.15 if reduced else 0.75) * progress) * SCALE
        mesh.position = Vector3(node.position.x, 0.055, node.position.z)
        mesh.scale = Vector3(px, 1.0, px)
        mesh.visible = true

func clear_battle() -> void:
    friendly_trails.multimesh.visible_instance_count = 0
    hostile_trails.multimesh.visible_instance_count = 0
    for mark in warning_pool:
        mark.visible = false
    for ring in burst_pool:
        ring.visible = false
    bursts.clear()
    last_warning_count = 0
