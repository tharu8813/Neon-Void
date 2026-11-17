extends Node3D
## Read-only P9 visuals for Python view.py::_render_screen_fx / draw_world.
## Source of truth remains C CombatSim. No collision, spawn, RNG or save writes.
const Catalog = preload("res://scripts/enemies/enemy_catalog.gd")
const SCALE: float = 0.01
const MAX_FRIENDLY: int = 1024
const MAX_HOSTILE: int = 320
const MAX_WARNINGS: int = 96
const MAX_BURSTS: int = 12
const MAX_FLOAT_TEXT: int = 40
const MAX_SHARD_VISUALS: int = 128
const MAX_DRONE_VISUALS: int = 8
const MAX_CHAIN_ARCS: int = 24
const FRIENDLY := Color("#ccff4d")
const HOSTILE := Color("#ff9fbb")
const WARN := Color("#e8ae50")
const DANGER := Color("#ff4d6d")
var friendly_trails: MultiMeshInstance3D
var hostile_trails: MultiMeshInstance3D
var shard_batch: MultiMeshInstance3D
var drone_nodes: Array[MeshInstance3D] = []
var chain_pool: Array[MeshInstance3D] = []
var chain_arcs: Array[Dictionary] = []
var warning_pool: Array[MeshInstance3D] = []
var burst_pool: Array[MeshInstance3D] = []
var bursts: Array[Dictionary] = []
var damage_pool: Array[Label3D] = []
var damage_labels: Array[Dictionary] = []
var warning_safe_material: StandardMaterial3D
var warning_danger_material: StandardMaterial3D
var last_warning_count: int = 0

func _ready() -> void:
    friendly_trails = _create_trail_batch("FriendlyStreaks", MAX_FRIENDLY, FRIENDLY)
    hostile_trails = _create_trail_batch("HostileStreaks", MAX_HOSTILE, HOSTILE)
    shard_batch = _create_trail_batch("ScoreShards", MAX_SHARD_VISUALS, Color("#7ef0ff"))
    for i in range(MAX_DRONE_VISUALS):
        var satellite := MeshInstance3D.new()
        satellite.name = "OrbitingDrone%d" % i
        var body := BoxMesh.new()
        body.size = Vector3(0.080, 0.035, 0.080)
        satellite.mesh = body
        satellite.material_override = _glow(Color("#6ff2ff"))
        satellite.visible = false
        add_child(satellite)
        drone_nodes.append(satellite)
    for i in range(MAX_CHAIN_ARCS):
        var beam := MeshInstance3D.new()
        beam.name = "ChainLightning%d" % i
        var line_mesh := BoxMesh.new()
        line_mesh.size = Vector3.ONE
        beam.mesh = line_mesh
        beam.material_override = _glow(Color("#9fe8ff"))
        beam.visible = false
        add_child(beam)
        chain_pool.append(beam)
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
    # Legacy game.py's FText pool is also capped at 40.
    for i in range(MAX_FLOAT_TEXT):
        var number := Label3D.new()
        number.name = "DamageNumber%d" % i
        number.font_size = 18
        number.pixel_size = 0.005
        number.rotation.x = -PI / 2.0
        number.no_depth_test = true
        number.visible = false
        add_child(number)
        damage_pool.append(number)

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

func _render_shards(shards: Array) -> void:
    var mesh: MultiMesh = shard_batch.multimesh
    var count: int = mini(mesh.instance_count, shards.size())
    for i in range(count):
        var shard: Dictionary = shards[i]
        var p: Vector2 = shard["pos"]
        var life: float = float(shard.get("life", 14.0))
        var radius: float = 0.032 if life >= 3.0 else 0.025
        var basis := Basis().scaled(Vector3(radius * 2.0, 0.016, radius * 2.0))
        mesh.set_instance_transform(i, Transform3D(basis,
            Vector3(p.x * SCALE, 0.12, p.y * SCALE)))
        mesh.set_instance_color(i, Color("#7ef0ff") if life > 3.0 else Color("#3d6b78"))
    mesh.visible_instance_count = count

func sync_drones(player_location: Vector2, count: int, angle: float) -> void:
    var shown: int = mini(MAX_DRONE_VISUALS, maxi(0, count))
    for i in range(MAX_DRONE_VISUALS):
        var drone: MeshInstance3D = drone_nodes[i]
        drone.visible = i < shown
        if not drone.visible:
            continue
        var offset: Vector2 = Vector2.RIGHT.rotated(angle + TAU * float(i) / float(shown)) * 52.0
        drone.position = Vector3((player_location.x + offset.x) * SCALE,
            0.18, (player_location.y + offset.y) * SCALE)

func sync_battle(friendly: Array, hostile: Array, telegraphs: Array,
        enemies: Array, reduced: bool = false, shards: Array = []) -> void:
    # Mirrors the Python friendly velocity streak (32 ms) and soft enemy tail.
    _render_trails(friendly_trails, friendly, FRIENDLY, 0.032, reduced)
    _render_trails(hostile_trails, hostile, HOSTILE, 0.015, reduced)
    _render_shards(shards)
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
    _advance_damage(1.0 / 60.0, reduced)
    _advance_chain_arcs(1.0 / 60.0)

func show_critical(logical_position: Vector2) -> void:
    if damage_labels.size() >= MAX_FLOAT_TEXT:
        return
    damage_labels.append({
        "pos": logical_position + Vector2(0.0, -24.0),
        "text": "CRIT!", "remaining": 0.52,
        "tint": Color("#fff5ad"),
    })

func add_chain_arc(source: Vector2, target: Vector2) -> void:
    if chain_arcs.size() >= MAX_CHAIN_ARCS:
        return
    chain_arcs.append({
        "start": source, "end": target, "remaining": 0.12,
    })

func _advance_chain_arcs(delta: float) -> void:
    var kept: Array[Dictionary] = []
    for entry in chain_arcs:
        entry["remaining"] = float(entry["remaining"]) - delta
        if float(entry["remaining"]) > 0.0:
            kept.append(entry)
    chain_arcs = kept
    for i in range(MAX_CHAIN_ARCS):
        var line: MeshInstance3D = chain_pool[i]
        line.visible = i < chain_arcs.size()
        if not line.visible:
            continue
        var item: Dictionary = chain_arcs[i]
        var start: Vector2 = item["start"]
        var target: Vector2 = item["end"]
        var delta_2d: Vector2 = target - start
        var center: Vector2 = (start + target) * 0.5
        line.position = Vector3(center.x * SCALE, 0.23, center.y * SCALE)
        line.rotation.y = atan2(delta_2d.x, delta_2d.y)
        line.scale = Vector3(0.024, 0.018, maxf(0.002, delta_2d.length() * SCALE))

func add_explosion(logical_position: Vector2, radius: float) -> void:
    if radius <= 0.0 or bursts.size() >= MAX_BURSTS:
        return
    add_burst(logical_position, "explosion")
    bursts[bursts.size() - 1]["radius"] = radius

func show_damage(logical_position: Vector2, amount: float) -> void:
    if amount <= 0.0 or damage_labels.size() >= MAX_FLOAT_TEXT:
        return
    damage_labels.append({
        "pos": logical_position, "text": "%d" % int(roundf(amount)),
        "remaining": 0.68, "tint": Color("#ffd166"),
    })

func show_pickup(logical_position: Vector2, value: int) -> void:
    if value <= 0 or damage_labels.size() >= MAX_FLOAT_TEXT:
        return
    damage_labels.append({
        "pos": logical_position + Vector2(0.0, -26.0),
        "text": "+%d" % value, "remaining": 0.68,
        "tint": Color("#7ef0ff"),
    })

func _advance_damage(delta: float, reduced: bool) -> void:
    var retained: Array[Dictionary] = []
    for entry in damage_labels:
        var remaining: float = float(entry["remaining"]) - delta
        if remaining <= 0.0:
            continue
        entry["remaining"] = remaining
        retained.append(entry)
    damage_labels = retained
    for i in range(MAX_FLOAT_TEXT):
        var label: Label3D = damage_pool[i]
        label.visible = i < damage_labels.size()
        if not label.visible:
            continue
        var entry: Dictionary = damage_labels[i]
        var p: Vector2 = entry["pos"]
        var remaining: float = float(entry["remaining"])
        label.text = str(entry["text"])
        label.position = Vector3(p.x * SCALE, 0.37, (p.y - (1.0 - remaining / 0.68) * (0.0 if reduced else 38.0)) * SCALE)
        var tint: Color = entry.get("tint", Color("#ffd166"))
        tint.a = minf(1.0, remaining / 0.28)
        label.modulate = tint

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
        "shard":
            color = Color("#7ef0ff")
            radius = 13.0
            duration = 0.20
        "explosion":
            color = Color("#ffd166")
            radius = 48.0
            duration = 0.28
    var instance: MeshInstance3D = burst_pool[bursts.size()]
    instance.material_override = _glow(color)
    instance.position = Vector3(logical_position.x * SCALE, 0.055, logical_position.y * SCALE)
    instance.visible = true
    bursts.append({"node": instance, "pos": logical_position, "tint": color,
        "elapsed": 0.0, "duration": duration, "radius": radius})

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
    # Re-index the pool after expiration using the *stored* coordinates.
    # Reusing the previous mesh's position would teleport surviving pulses.
    bursts = surviving
    for i in range(MAX_BURSTS):
        var mesh: MeshInstance3D = burst_pool[i]
        mesh.visible = i < bursts.size()
        if not mesh.visible:
            continue
        var value: Dictionary = bursts[i]
        var progress: float = float(value["elapsed"]) / maxf(0.01, float(value["duration"]))
        var radius: float = float(value["radius"]) * (1.0 + (0.15 if reduced else 0.75) * progress) * SCALE
        var pos: Vector2 = value["pos"]
        mesh.position = Vector3(pos.x * SCALE, 0.055, pos.y * SCALE)
        mesh.scale = Vector3(radius, 1.0, radius)
        mesh.material_override = _glow(value["tint"])
        bursts[i]["node"] = mesh

func clear_battle() -> void:
    friendly_trails.multimesh.visible_instance_count = 0
    hostile_trails.multimesh.visible_instance_count = 0
    shard_batch.multimesh.visible_instance_count = 0
    for drone in drone_nodes:
        drone.visible = false
    for beam in chain_pool:
        beam.visible = false
    chain_arcs.clear()
    for mark in warning_pool:
        mark.visible = false
    for ring in burst_pool:
        ring.visible = false
    bursts.clear()
    for label in damage_pool:
        label.visible = false
    damage_labels.clear()
    last_warning_count = 0
