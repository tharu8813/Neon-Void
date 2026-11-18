extends Node3D
## Temporary playable bridge using B's actual Stats/Damage/Events mathematics.
## NOT the production B-owned enemy AI/skills/proc/VFX. A+B replace encounter pacing.
signal enemy_defeated(actor_id: String, validated_receipt: String)
signal player_died()
signal health_updated(health: float, max_health: float)
const Stats = preload("res://scripts/rework/combat/stats.gd")
const Damage = preload("res://scripts/rework/combat/damage.gd")
const Events = preload("res://scripts/rework/combat/events.gd")
const Maps = preload("res://scripts/rework/world/procedural_map.gd")
var player: CharacterBody3D
var camera: Camera3D
var map_plan: Dictionary = {}
var run_id: String = ""
var enemies: Dictionary = {}
var shots: Array[Dictionary] = []
var player_stats: Dictionary = {}
var combat_events: Variant = Events.new()
var frame_tick: int = 0
var shot_sequence: int = 0
var firing_clock: float = 0.0
var last_path_cell := Vector2i(-100, -100)
var cached_distances := PackedInt32Array()
var game_over: bool = false

func begin_encounter(player_node: CharacterBody3D, camera_node: Camera3D,
        world_map: Dictionary, new_run_id: String) -> void:
    reset()
    player = player_node
    camera = camera_node
    map_plan = world_map.duplicate(true)
    run_id = new_run_id
    player_stats = Stats.make()
    combat_events.reset()
    game_over = false
    health_updated.emit(float(player_stats["health"]), float(player_stats["max_health"]))

func reset() -> void:
    for enemy in enemies.values():
        if is_instance_valid(enemy.get("body")):
            enemy["body"].queue_free()
    for shot in shots:
        if is_instance_valid(shot.get("node")):
            shot["node"].queue_free()
    enemies.clear()
    shots.clear()
    frame_tick = 0
    shot_sequence = 0
    firing_clock = 0.0
    last_path_cell = Vector2i(-100, -100)
    cached_distances = PackedInt32Array()
    game_over = false
    player = null
    camera = null

func spawn_hostile(info: Dictionary) -> bool:
    var id: String = str(info.get("actor_id", ""))
    if id.is_empty() or enemies.has(id) or player == null:
        return false
    var position: Vector3 = info.get("position", Vector3.ZERO)
    if not map_plan.is_empty():
        var cell := Vector2i(floori(position.x / Maps.TILE_SIZE), floori(position.z / Maps.TILE_SIZE))
        if not Maps.is_floor(map_plan, cell):
            return false
    var boss: bool = bool(info.get("boss", false))
    var body := CharacterBody3D.new()
    body.name = "Hostile_" + id
    body.position = position + Vector3.UP * 0.5
    var shape := CollisionShape3D.new()
    var capsule := CapsuleShape3D.new()
    capsule.radius = 0.26 if not boss else 0.39
    capsule.height = 0.9 if not boss else 1.2
    shape.shape = capsule
    body.add_child(shape)
    var visual := MeshInstance3D.new()
    var sphere := SphereMesh.new()
    sphere.radius = 0.27 if not boss else 0.44
    sphere.height = 0.54 if not boss else 0.88
    visual.mesh = sphere
    var tint := StandardMaterial3D.new()
    tint.albedo_color = Color(0.92, 0.24, 0.3) if not boss else Color(1.0, 0.12, 0.68)
    visual.material_override = tint
    body.add_child(visual)
    add_child(body)
    var health: float = 20.0 if not boss else 220.0
    enemies[id] = {
        "body": body, "stats": Stats.make({
            "max_health": health, "health": health,
            "attack_damage": 3.0 if not boss else 9.0
        }),
        "boss": boss, "attack_clock": 0.0, "speed": 2.2 if not boss else 1.6
    }
    return true

func _physics_process(delta: float) -> void:
    if player == null or camera == null or game_over:
        return
    frame_tick += 1
    combat_events.set_tick(frame_tick)
    _move_hostiles(delta)
    _move_shots(delta)
    _auto_shoot(delta)

func _move_hostiles(delta: float) -> void:
    if not map_plan.is_empty():
        var tile := Vector2i(floori(player.position.x / Maps.TILE_SIZE), floori(player.position.z / Maps.TILE_SIZE))
        if tile != last_path_cell:
            last_path_cell = tile
            cached_distances = Maps.distance_field(map_plan, tile)
    for id in enemies.keys():
        if not enemies.has(id):
            continue
        var info: Dictionary = enemies[id]
        var body: CharacterBody3D = info["body"]
        var chase: Vector3 = player.global_position - body.global_position
        chase.y = 0.0
        var near: float = chase.length()
        if near > 0.85:
            var destination: Vector3 = player.global_position
            if not map_plan.is_empty():
                destination = _next_path_target(body.global_position, player.global_position)
            var movement: Vector3 = destination - body.global_position
            movement.y = 0.0
            if movement.length_squared() > 0.0001:
                body.velocity = movement.normalized() * float(info["speed"])
            else:
                body.velocity = Vector3.ZERO
            body.move_and_slide()
        else:
            body.velocity = Vector3.ZERO
            info["attack_clock"] = float(info["attack_clock"]) + delta
            if float(info["attack_clock"]) >= 1.1:
                info["attack_clock"] = 0.0
                var calc: Dictionary = Damage.calculate(info["stats"], player_stats, {
                    "damage_type": "physical", "base_damage": 1.0, "ad_ratio": 1.0
                })
                var dealt: Dictionary = Damage.apply_damage(player_stats, calc)
                if bool(dealt.get("ok", false)):
                    health_updated.emit(float(player_stats["health"]), float(player_stats["max_health"]))
                if bool(dealt.get("killed", false)) and not game_over:
                    game_over = true
                    call_deferred("_report_player_death")
        enemies[id] = info

func _report_player_death() -> void:
    if game_over:
        player_died.emit()

func _next_path_target(from: Vector3, fallback: Vector3) -> Vector3:
    var current := Vector2i(floori(from.x / Maps.TILE_SIZE), floori(from.z / Maps.TILE_SIZE))
    if cached_distances.is_empty() or not Maps.is_floor(map_plan, current):
        return fallback
    var width: int = int(map_plan["width"])
    var best: Vector2i = current
    var distance: int = cached_distances[current.y * width + current.x]
    for direction in [Vector2i(0, -1), Vector2i(1, 0), Vector2i(0, 1), Vector2i(-1, 0)]:
        var other: Vector2i = current + direction
        if not Maps.is_floor(map_plan, other):
            continue
        var score: int = cached_distances[other.y * width + other.x]
        if score >= 0 and (distance < 0 or score < distance):
            best = other
            distance = score
    if best == current:
        return fallback
    return Maps.cell_to_world(best)

func _auto_shoot(delta: float) -> void:
    firing_clock += delta
    var period: float = 1.0 / maxf(0.2, float(player_stats.get("attack_speed", 2.0)))
    if firing_clock < period or enemies.is_empty():
        return
    firing_clock = fmod(firing_clock, period)
    var window: Vector2 = get_viewport().get_visible_rect().size
    var pixel: Vector2 = get_viewport().get_mouse_position()
    var direction: Vector3 = camera.call("direction_to_window_pointer", player.global_position, pixel, window)
    if direction.length_squared() < 0.00001:
        return
    shot_sequence += 1
    var shot := MeshInstance3D.new()
    var mesh := SphereMesh.new()
    mesh.radius = 0.09
    mesh.height = 0.18
    shot.mesh = mesh
    var tint := StandardMaterial3D.new()
    tint.albedo_color = Color(0.25, 0.95, 1.0)
    tint.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
    shot.material_override = tint
    shot.position = player.global_position + Vector3.UP * 0.12
    add_child(shot)
    shots.append({
        "node": shot, "direction": direction, "time": 0.0,
        "hit_key": "%s:shot:%d" % [run_id, shot_sequence]
    })

func _move_shots(delta: float) -> void:
    for index in range(shots.size() - 1, -1, -1):
        var shot: Dictionary = shots[index]
        var node: MeshInstance3D = shot["node"]
        shot["time"] = float(shot["time"]) + delta
        node.global_position += shot["direction"] * 11.0 * delta
        var expired: bool = float(shot["time"]) >= 1.5
        if not map_plan.is_empty():
            var tile := Vector2i(floori(node.position.x / Maps.TILE_SIZE), floori(node.position.z / Maps.TILE_SIZE))
            if not Maps.is_floor(map_plan, tile):
                expired = true
        if not expired:
            for enemy_id in enemies.keys():
                var info: Dictionary = enemies[enemy_id]
                var body: CharacterBody3D = info["body"]
                var separation := Vector2(body.position.x - node.position.x, body.position.z - node.position.z)
                if separation.length() <= (0.48 if bool(info["boss"]) else 0.35):
                    _apply_hit(enemy_id, str(shot["hit_key"]))
                    expired = true
                    break
        if expired:
            combat_events.retire_hit_key(str(shot["hit_key"]))
            node.queue_free()
            shots.remove_at(index)
        else:
            shots[index] = shot

func _apply_hit(enemy_id: String, hit_key: String) -> void:
    if not enemies.has(enemy_id):
        return
    var received: Dictionary = combat_events.submit_root({
        "kind": "on_hit", "actor_id": "VACCINE", "target_id": enemy_id,
        "hit_key": hit_key, "source_tags": ["basic_attack"],
        "allowed_proc_triggers": ["on_hit", "on_crit"]
    })
    if received.is_empty():
        return
    var info: Dictionary = enemies[enemy_id]
    var critical: bool = false
    var attack: Dictionary = {
        "damage_type": "physical", "base_damage": 4.0, "ad_ratio": 1.0,
        "is_on_hit": true, "can_crit": true
    }
    var calculated: Dictionary = Damage.calculate(player_stats, info["stats"], attack, critical)
    var applied: Dictionary = Damage.apply_damage(info["stats"], calculated)
    combat_events.drain()
    if bool(applied.get("killed", false)):
        var body: CharacterBody3D = info["body"]
        enemies.erase(enemy_id)
        body.queue_free()
        enemy_defeated.emit(enemy_id, "%s:enemy:%s:death" % [run_id, enemy_id])
