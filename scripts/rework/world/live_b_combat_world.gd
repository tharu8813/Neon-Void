extends Node3D
## D-owned scene integration for B's authoritative 60Hz combat runtime.
## No duplicate damage/status/skill/proc calculations. B is the sole HP source.
## A's E-ack pilot connects through WorldCombatRouter's stable outward signals.
signal enemy_defeated(actor_id: String, receipt_id: String)
signal player_died()
signal health_updated(health: float, maximum: float)
signal combat_notice(notice: Dictionary)
signal integration_failed(reason: String)

const BBridge = preload("res://scripts/rework/combat/combat_node_bridge.gd")
const BAI = preload("res://scripts/rework/combat/enemy_ai.gd")
const BStats = preload("res://scripts/rework/combat/stats.gd")
const BStatuses = preload("res://scripts/rework/combat/statuses.gd")
const CEffects = preload("res://scripts/rework/plugins/plugin_effect_runtime.gd")
const Maps = preload("res://scripts/rework/world/procedural_map.gd")
const PLAYER_ID := "VACCINE"

var bridge: Variant
var player: CharacterBody3D
var camera: Camera3D
var run_id: String = ""
var map_plan: Dictionary = {}
var enemies: Dictionary = {}
var seen_deaths: Dictionary = {}
var last_player_hp: float = -1.0
var active: bool = false
var stopped: bool = false
var plugin_levels: Dictionary = {}
var plugin_instances: Array = []
var authorized_skills: Array[Dictionary] = []
var key_latches: Dictionary = {}
var prior_projectile_positions: Dictionary = {}
var terrain_rejections: int = 0
var combat_tick: int = 0
var region_seed: int = 42

func _ready() -> void:
    bridge = BBridge.new()
    bridge.name = "BCombatNodeBridge"
    add_child(bridge)
    bridge.drive_actor_nodes = true
    bridge.combat_notice.connect(_on_b_notice)
    bridge.combat_actor_died.connect(_on_b_death)
    bridge.set_projectile_factory(Callable(self, "_make_projectile_visual"))
    bridge.running = false

func configure_authorized_build(levels: Dictionary, instances: Array,
        skills: Array = []) -> bool:
    ## A invokes only after an E-authorized inventory/skill snapshot or ACK.
    ## C computes numerical derived stats; B executes provided numeric procs.
    ## Never turn a proposed localized plugin description into combat numbers.
    var c: Variant = CEffects.new()
    if not c.configure(levels, region_seed):
        return false
    for instance in instances:
        if not instance is Dictionary or str(instance.get("plugin_id", "")).is_empty():
            return false
    for skill in skills:
        if not skill is Dictionary or str(skill.get("id", "")).is_empty():
            return false
    plugin_levels = levels.duplicate(true)
    plugin_instances = instances.duplicate(true)
    authorized_skills.clear()
    for skill in skills:
        authorized_skills.append(skill.duplicate(true))
    if active:
        # Safe mid-run activation for already validated B effect_commands. B
        # does not yet expose base stat refresh; C passives take effect on next
        # encounter. A/B must approve a transactional live-stat update API.
        return bridge.simulation.set_actor_plugins(PLAYER_ID, plugin_instances)
    return true

func begin_encounter(player_node: CharacterBody3D, camera_node: Camera3D,
        world_map: Dictionary, new_run_id: String, config: Dictionary = {}) -> bool:
    if player_node == null or camera_node == null or new_run_id.is_empty():
        return false
    reset()
    player = player_node
    camera = camera_node
    map_plan = world_map.duplicate(true)
    run_id = new_run_id
    region_seed = maxi(1, int(config.get("seed", 42)))
    var encounter_id: String = str(config.get("encounter_id", "motherboard"))
    bridge.setup("%s:%s" % [run_id, encounter_id], region_seed,
        {"grid_cell_size": Maps.TILE_SIZE})
    bridge.drive_actor_nodes = true
    var stats: Dictionary = BStats.make(config.get("player_stats", {}))
    if not plugin_levels.is_empty():
        var c: Variant = CEffects.new()
        if not c.configure(plugin_levels, region_seed):
            reset()
            integration_failed.emit("C_effect_configuration_rejected")
            return false
        stats = c.derived_stats(stats)
    if not bridge.register_node(PLAYER_ID, "player", player, stats, {
        "player_controlled": true, "radius": 0.3,
        "attack_range": 18.0, "projectile_speed": 18.0
    }):
        reset()
        return false
    if not plugin_instances.is_empty():
        if not bridge.simulation.set_actor_plugins(PLAYER_ID, plugin_instances):
            reset()
            return false
    for i in range(authorized_skills.size()):
        if not bridge.simulation.learn_skill(PLAYER_ID, authorized_skills[i],
                i if i < 3 else -1, true):
            reset()
            return false
    active = true
    stopped = false
    _emit_player_hp()
    return true

func reset() -> void:
    active = false
    stopped = false
    if bridge != null:
        bridge.running = false
        for actor_id in enemies.keys():
            bridge.unregister_node(str(actor_id))
    for item in enemies.values():
        if is_instance_valid(item.get("node")):
            item["node"].queue_free()
    enemies.clear()
    seen_deaths.clear()
    prior_projectile_positions.clear()
    terrain_rejections = 0
    combat_tick = 0
    last_player_hp = -1.0
    key_latches.clear()
    player = null
    camera = null

func spawn_hostile(info: Dictionary) -> bool:
    if not active or bool(info.get("boss", false)) and str(info.get("actor_id", "")) == PLAYER_ID:
        return false
    var actor_id: String = str(info.get("actor_id", ""))
    if actor_id.is_empty() or actor_id == PLAYER_ID or enemies.has(actor_id):
        return false
    var position: Vector3 = info.get("position", Vector3.ZERO)
    if not map_plan.is_empty() and not Maps.is_floor(map_plan, _cell(position)):
        return false
    var boss: bool = bool(info.get("boss", false))
    var body := CharacterBody3D.new()
    body.name = "BActor_" + actor_id
    body.position = position + Vector3.UP * 0.5
    var coll := CollisionShape3D.new()
    var capsule := CapsuleShape3D.new()
    capsule.radius = 0.40 if boss else 0.26
    capsule.height = 1.3 if boss else 0.9
    coll.shape = capsule
    body.add_child(coll)
    var visible := MeshInstance3D.new()
    var sphere := SphereMesh.new()
    sphere.radius = 0.40 if boss else 0.26
    sphere.height = 0.8 if boss else 0.52
    visible.mesh = sphere
    var material := StandardMaterial3D.new()
    material.albedo_color = Color(0.95, 0.18, 0.65) if boss else Color(0.93, 0.20, 0.31)
    visible.material_override = material
    body.add_child(visible)
    add_child(body)
    var stats: Dictionary = BStats.make({
        "max_health": 220.0 if boss else 20.0,
        "health": 220.0 if boss else 20.0,
        "attack_damage": 9.0 if boss else 3.0,
        "attack_speed": 0.8 if boss else 1.0,
        "move_speed": 1.7 if boss else 2.25
    })
    # B's runtime owns attack damage and status application. Enemy intent is
    # controlled externally only to let D's BFS navigate through maze walls.
    var options: Dictionary = {
        "player_controlled": true,
        "radius": 0.40 if boss else 0.26,
        "projectile_speed": 12.0,
        "attack_range": 8.0 if boss else 6.0,
        "preferred_max_range": 5.0 if boss else 3.0,
        "attack_definition": {
            "damage_type": "physical", "base_damage": 0.0,
            "ad_ratio": 1.0, "ap_ratio": 0.0, "can_crit": false,
            "is_on_hit": true
        }
    }
    if not bridge.register_node(actor_id, "enemy", body, stats, options):
        body.queue_free()
        return false
    enemies[actor_id] = {
        "node": body, "boss": boss, "preferred_min_range": 0.0,
        "preferred_max_range": options["preferred_max_range"],
        "attack_range": options["attack_range"]
    }
    return true

func _physics_process(_delta: float) -> void:
    if not active or stopped or player == null or camera == null:
        return
    var window: Vector2 = get_viewport().get_visible_rect().size
    var mouse: Vector2 = get_viewport().get_mouse_position()
    var pointer: Dictionary = camera.call("pointer_ground_from_window", mouse, window)
    var aim: Vector3 = pointer.get("world", player.global_position + Vector3.RIGHT)
    var player_state: Dictionary = bridge.simulation.snapshot(PLAYER_ID)
    var flags: Dictionary = BStatuses.active_flags(player_state.get("status_state", {}))
    var restricted: bool = bool(flags.get("rooted", false)) or bool(flags.get("stopped", false))
    player.movement_enabled = not restricted
    var input: Dictionary = {
        "move": Vector2.ZERO, "aim_world": aim,
        "fire": bool(pointer.get("valid", false))
    }
    if _pressed_once(KEY_SPACE):
        input["dash"] = true
    if _pressed_once(KEY_Q):
        input["skill_slot"] = 0
    elif _pressed_once(KEY_E):
        input["skill_slot"] = 1
    elif _pressed_once(KEY_R):
        input["skill_slot"] = 2
    bridge.set_input(PLAYER_ID, input)
    _set_enemy_commands()

func _pressed_once(code: Key) -> bool:
    var now: bool = Input.is_key_pressed(code)
    var was: bool = bool(key_latches.get(code, false))
    key_latches[code] = now
    return now and not was

func _set_enemy_commands() -> void:
    var player_state: Dictionary = bridge.simulation.snapshot(PLAYER_ID)
    if not bool(player_state.get("alive", false)):
        return
    var player_position: Vector3 = player_state["position"]
    var distances := PackedInt32Array()
    if not map_plan.is_empty():
        distances = Maps.distance_field(map_plan, _cell(player_position))
    for id in enemies.keys():
        var snap: Dictionary = bridge.simulation.snapshot(str(id))
        if not bool(snap.get("alive", false)):
            continue
        var meta: Dictionary = enemies[id]
        var enemy: Dictionary = {
            "alive": true, "position": snap["position"],
            "preferred_min_range": meta["preferred_min_range"],
            "preferred_max_range": meta["preferred_max_range"],
            "attack_range": meta["attack_range"]
        }
        var decision: Dictionary = BAI.decide(enemy, player_state)
        var movement: Vector2 = decision["move"]
        if not map_plan.is_empty() and distances.size() > 0:
            movement = _maze_steering(snap["position"], player_position, distances, movement)
        var can_fire: bool = bool(decision["shoot"]) and _line_of_sight(
            snap["position"], player_position)
        bridge.set_input(str(id), {
            "move": movement, "aim_world": player_position, "fire": can_fire
        })

func _cell(point: Vector3) -> Vector2i:
    return Vector2i(floori(point.x / Maps.TILE_SIZE), floori(point.z / Maps.TILE_SIZE))

func _maze_steering(origin: Vector3, target: Vector3, distances: PackedInt32Array,
        fallback: Vector2) -> Vector2:
    var cell: Vector2i = _cell(origin)
    if not Maps.is_floor(map_plan, cell):
        return Vector2.ZERO
    var width: int = int(map_plan["width"])
    var best: Vector2i = cell
    var score: int = distances[cell.y * width + cell.x]
    for step in [Vector2i.UP, Vector2i.RIGHT, Vector2i.DOWN, Vector2i.LEFT]:
        var other: Vector2i = cell + step
        if not Maps.is_floor(map_plan, other):
            continue
        var d: int = distances[other.y * width + other.x]
        if d >= 0 and (score < 0 or d < score):
            best = other
            score = d
    if best == cell:
        return fallback
    var waypoint: Vector3 = Maps.cell_to_world(best)
    var vector := Vector2(waypoint.x - origin.x, waypoint.z - origin.z)
    return vector.normalized() if vector.length_squared() > 0.0001 else Vector2.ZERO

func _line_of_sight(origin: Vector3, target: Vector3) -> bool:
    if map_plan.is_empty():
        return true
    var p := Vector2(origin.x, origin.z)
    var q := Vector2(target.x, target.z)
    var distance: float = p.distance_to(q)
    var samples: int = maxi(1, ceili(distance / (Maps.TILE_SIZE * 0.25)))
    for index in range(samples + 1):
        var point: Vector2 = p.lerp(q, float(index) / float(samples))
        if not Maps.is_floor(map_plan, Vector2i(
                floori(point.x / Maps.TILE_SIZE), floori(point.y / Maps.TILE_SIZE))):
            return false
    return true

func _make_projectile_visual(_snapshot: Dictionary) -> Node3D:
    var node := MeshInstance3D.new()
    var sphere := SphereMesh.new()
    sphere.radius = 0.09
    sphere.height = 0.18
    node.mesh = sphere
    var material := StandardMaterial3D.new()
    material.albedo_color = Color(0.18, 0.86, 1.0)
    material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
    node.material_override = material
    return node

func _on_b_notice(note: Dictionary) -> void:
    combat_tick = int(bridge.simulation.fixed_tick)
    combat_notice.emit(note.duplicate(true))
    if str(note.get("kind", "")) == "hit" and str(note.get("target_id", "")) == PLAYER_ID:
        _emit_player_hp()

func _emit_player_hp() -> void:
    if bridge == null:
        return
    var state: Dictionary = bridge.simulation.snapshot(PLAYER_ID)
    if state.is_empty():
        return
    var hp: float = float(state["stats"]["health"])
    if hp != last_player_hp:
        last_player_hp = hp
        health_updated.emit(hp, float(state["stats"]["max_health"]))

func _on_b_death(actor_id: String, _killer_id: String) -> void:
    if seen_deaths.has(actor_id) or not active:
        return
    seen_deaths[actor_id] = true
    if actor_id == PLAYER_ID:
        stopped = true
        player_died.emit()
        return
    if not enemies.has(actor_id):
        integration_failed.emit("unknown_B_death_actor:" + actor_id)
        return
    var item: Dictionary = enemies[actor_id]
    if is_instance_valid(item["node"]):
        item["node"].queue_free()
    bridge.bound_nodes.erase(actor_id)
    enemies.erase(actor_id)
    # Stable D world receipt; B's volatile event_id is NEVER an E reward key.
    enemy_defeated.emit(actor_id, "world:%s:%s:death" % [run_id, actor_id])

func actor_snapshot(actor_id: String) -> Dictionary:
    return bridge.simulation.snapshot(actor_id) if bridge != null else {}

func advance_test_tick(inputs: Dictionary = {}) -> Array[Dictionary]:
    ## Tests can drive B's exact 60Hz step without fake D damage helpers.
    if bridge == null or not active:
        return []
    for id in inputs:
        bridge.set_input(str(id), inputs[id])
    return bridge.advance_one_tick()
