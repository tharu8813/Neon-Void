extends "res://scripts/rework/combat/combat_node_bridge.gd"
## B-owned opt-in replacement for D demo_combat_bridge.gd.
## Same D public methods/signals, real 60Hz B runtime and C effect sink.
## D/A only switch the preload; no D/world/root file is edited here.
signal enemy_defeated(actor_id: String, validated_receipt: String)
signal player_died()
signal health_updated(health: float, max_health: float)
signal mana_updated(mana: float, maximum: float)
signal plugin_failure(reason: String)
const Stats = preload("res://scripts/rework/combat/stats.gd")
const Statuses = preload("res://scripts/rework/combat/statuses.gd")
const AI = preload("res://scripts/rework/combat/enemy_ai.gd")
const Maps = preload("res://scripts/rework/world/procedural_map.gd")
const CAdapter = preload("res://scripts/rework/combat/c_effect_adapter.gd")
const Growth = preload("res://scripts/rework/combat/permanent_growth.gd")
const SkillRegistry = preload("res://scripts/rework/skills/skill_registry.gd")
const SkillSlots = preload("res://scripts/rework/skills/skill_slots.gd")
const PLAYER_ID := "VACCINE"

var player: CharacterBody3D = null
var camera: Camera3D = null
var map_plan: Dictionary = {}
var run_id: String = ""
var enemies: Dictionary = {}
var player_stats: Dictionary = {}
var game_over: bool = false
var effects = CAdapter.new()
var last_path_cell: Vector2i = Vector2i(-100, -100)
var cached_distances: PackedInt32Array = PackedInt32Array()
var pending_input: Dictionary = {}
var last_health: float = -1.0
var last_mana: float = -1.0
var last_plugin_fault: String = ""
var last_combat_overflow: int = 0
var progression_error: String = ""
var _baseline_player_stats: Dictionary = {}
var _authorized_progression: Dictionary = {}
var _staged_progression: Dictionary = {}
var _reported_deaths: Dictionary = {}
var _projectile_kinds: Dictionary = {}
var _old_projectiles: Dictionary = {}
var _dash_key_down: bool = false
var _skill_keys_down: Array[bool] = [false, false, false]
var _old_player_movement: bool = true
var _has_player_movement: bool = false
var _has_player_speed: bool = false
var _old_player_speed: float = 5.0
var balance_draft: Dictionary = {
    "seed": 42,
    "player_attack_base_damage": 4.0,
    "projectile_speed": 11.0,
    "dash_distance": 2.0,
    "dash_cooldown_ticks": 60,
    "enemy_health": 20.0,
    "boss_health": 220.0,
    "enemy_move_speed": 2.2,
    "boss_move_speed": 1.6,
    "enemy_attack_speed": 0.9,
    "enemy_attack_range": 1.15,
    "enemy_damage_base": 1.0,
    "boss_control_resistance": 0.5,
    "projectile_radius": 0.09
}

func _ready() -> void:
    combat_actor_died.connect(_on_combat_actor_died)
    combat_notice.connect(_on_combat_notice)
    set_projectile_factory(Callable(self, "_make_bullet_visual"))

func begin_encounter(player_node: CharacterBody3D, camera_node: Camera3D,
        world_map: Dictionary, new_run_id: String,
        config: Dictionary = {}) -> bool:
    ## Backwards-compatible with D old 4-argument demo AND new formal
    ## 5-argument router. A/D can validate a failed encounter initialization.
    var staged: Dictionary = _staged_progression.duplicate(true)
    reset()
    _staged_progression = staged
    if player_node == null or camera_node == null or new_run_id.is_empty():
        return false
    player = player_node
    camera = camera_node
    run_id = new_run_id
    map_plan = world_map.duplicate(true)
    var encounter_seed: int = int(config.get("seed", balance_draft.get("seed", 42))) ^ int(new_run_id.hash())
    var encounter_id: String = str(config.get("encounter_id", new_run_id))
    setup(new_run_id + ":" + encounter_id, encounter_seed, {
        "encounter_id": new_run_id,
        "primary_player_id": PLAYER_ID,
        "default_projectile_speed": float(balance_draft["projectile_speed"]),
        "projectile_radius_scale": 0.01
    })
    var stats: Dictionary = Stats.make(balance_draft.get("player_stats", {}))
    _baseline_player_stats = stats.duplicate(true)
    var attack: Dictionary = {
        "damage_type": "physical",
        "base_damage": float(balance_draft["player_attack_base_damage"]),
        "ad_ratio": 1.0, "ap_ratio": 0.0,
        "is_on_hit": true, "can_crit": true
    }
    if not register_node(PLAYER_ID, "vaccine", player, stats, {
        "player_controlled": true, "external_movement": true,
        "radius": 0.3, "projectile_speed": float(balance_draft["projectile_speed"]),
        "projectile_radius": float(balance_draft["projectile_radius"]),
        "attack_definition": attack, "dash_definition": {
            "distance": float(balance_draft["dash_distance"]),
            "cooldown_ticks": int(balance_draft["dash_cooldown_ticks"])
        }
    }):
        reset()
        return false
    simulation.projectile_segment_filter = Callable(self, "_clip_projectile_to_floor")
    effects.attach(simulation, PLAYER_ID, new_run_id, encounter_seed ^ 0x70726f63)
    _has_player_movement = _has_property(player, "movement_enabled")
    if _has_player_movement:
        _old_player_movement = bool(player.get("movement_enabled"))
    _has_player_speed = _has_property(player, "walk_speed")
    if _has_player_speed:
        _old_player_speed = float(player.get("walk_speed"))
    player_stats = simulation.actors[PLAYER_ID]["stats"]
    game_over = false
    if not _staged_progression.is_empty():
        var approved: Dictionary = _staged_progression.duplicate(true)
        _staged_progression.clear()
        var installed: Dictionary = _install_authorized_progression(approved)
        if not bool(installed.get("ok", false)):
            reset()
            return false
    _emit_resource_changes(true)
    return running and simulation.actors.has(PLAYER_ID)

func _has_property(object: Object, property_name: String) -> bool:
    for descriptor in object.get_property_list():
        if str(descriptor.get("name", "")) == property_name:
            return true
    return false

func reset() -> void:
    running = false
    if player != null and is_instance_valid(player):
        if _has_player_movement:
            player.set("movement_enabled", _old_player_movement)
        if _has_player_speed:
            player.set("walk_speed", _old_player_speed)
    for enemy in enemies.values():
        var body: Variant = enemy.get("body", null)
        if is_instance_valid(body):
            body.queue_free()
    enemies.clear()
    for visual in visual_nodes.values():
        if is_instance_valid(visual):
            visual.queue_free()
    visual_nodes.clear()
    bound_nodes.clear()
    intents.clear()
    _projectile_kinds.clear()
    _old_projectiles.clear()
    _reported_deaths.clear()
    pending_input.clear()
    _dash_key_down = false
    _skill_keys_down = [false, false, false]
    accumulator = 0.0
    last_path_cell = Vector2i(-100, -100)
    cached_distances = PackedInt32Array()
    run_id = ""
    map_plan.clear()
    player = null
    camera = null
    _has_player_movement = false
    _has_player_speed = false
    last_health = -1.0
    last_mana = -1.0
    last_plugin_fault = ""
    last_combat_overflow = 0
    progression_error = ""
    _baseline_player_stats.clear()
    _authorized_progression.clear()
    _staged_progression.clear()
    game_over = false
    effects = CAdapter.new()
    simulation.begin(0, {"encounter_id": "cleared"})
    player_stats = {}

func spawn_hostile(info: Dictionary) -> bool:
    var id: String = str(info.get("actor_id", ""))
    if not running or id.is_empty() or enemies.has(id) or simulation.actors.has(id):
        return false
    var position: Vector3 = info.get("position", Vector3.ZERO)
    if not map_plan.is_empty():
        var tile: Vector2i = _cell(position)
        if not Maps.is_floor(map_plan, tile):
            return false
    var boss: bool = bool(info.get("boss", false))
    var body := CharacterBody3D.new()
    body.name = "Hostile_" + id
    var collider := CollisionShape3D.new()
    var capsule := CapsuleShape3D.new()
    capsule.radius = 0.39 if boss else 0.26
    capsule.height = 1.2 if boss else 0.9
    collider.shape = capsule
    body.add_child(collider)
    var mesh := MeshInstance3D.new()
    var sphere := SphereMesh.new()
    sphere.radius = 0.44 if boss else 0.27
    sphere.height = 0.88 if boss else 0.54
    mesh.mesh = sphere
    var material := StandardMaterial3D.new()
    material.albedo_color = Color(1.0, 0.12, 0.68) if boss else Color(0.92, 0.24, 0.3)
    mesh.material_override = material
    body.add_child(mesh)
    add_child(body)
    body.global_position = position + Vector3.UP * 0.5
    var hp: float = float(info.get("max_health",
        balance_draft["boss_health"] if boss else balance_draft["enemy_health"]))
    var speed: float = float(info.get("move_speed",
        balance_draft["boss_move_speed"] if boss else balance_draft["enemy_move_speed"]))
    var combat_stats: Dictionary = Stats.make({
        "max_health": hp, "health": hp, "move_speed": speed,
        "attack_speed": float(info.get("attack_speed", balance_draft["enemy_attack_speed"])),
        "attack_damage": 9.0 if boss else 3.0,
        "boss_control_resistance": float(balance_draft["boss_control_resistance"]) if boss else 0.0
    })
    var registered: bool = register_node(id, "virus", body, combat_stats, {
        "radius": 0.44 if boss else 0.27,
        "external_movement": true,
        "attack_range": float(info.get("attack_range", balance_draft["enemy_attack_range"])),
        "preferred_min_range": float(info.get("preferred_min_range", 0.0)),
        "preferred_max_range": float(info.get("preferred_max_range", 0.85)),
        "projectile_speed": float(balance_draft["projectile_speed"]),
        "projectile_radius": 0.12,
        "attack_definition": {"damage_type": "physical",
            "base_damage": float(balance_draft["enemy_damage_base"]),
            "ad_ratio": 1.0, "can_crit": false}
    })
    if not registered:
        body.queue_free()
        return false
    enemies[id] = {"body": body, "boss": boss, "speed": speed}
    return true

func _parse_authorized_progression(loaded: Dictionary) -> Dictionary:
    ## Caller A MUST pass an unmodified, successfully read E Gateway.snapshot.
    ## This is a B schema check, not a signature or filesystem authenticity check.
    if loaded.get("ok") != true or not loaded.has("snapshot") or (
            not loaded["snapshot"] is Dictionary):
        return {"ok": false, "reason": "E_snapshot_not_confirmed"}
    var revision: Variant = loaded.get("revision", -1)
    if not (revision is int) or revision < 0:
        return {"ok": false, "reason": "invalid_E_revision"}
    var state: Dictionary = loaded["snapshot"]
    if state.get("selection_required", true) != false:
        return {"ok": false, "reason": "vaccine_selection_pending"}
    var generation: Variant = state.get("generation", -1)
    # Godot JSON roundtrips may represent an exact E integer as float.
    if not (typeof(generation) == TYPE_INT or typeof(generation) == TYPE_FLOAT):
        return {"ok": false, "reason": "invalid_E_generation"}
    if is_nan(float(generation)) or is_inf(float(generation)) or (
            float(generation) != floorf(float(generation))) or (
            float(generation) < 1.0) or float(generation) > 1000000000.0:
        return {"ok": false, "reason": "invalid_E_generation"}
    if not state.get("vaccine") is Dictionary:
        return {"ok": false, "reason": "missing_E_vaccine"}
    var vaccine: Dictionary = state["vaccine"]
    var id: String = str(vaccine.get("vaccine_id", ""))
    if id.is_empty() or vaccine.get("alive") != true:
        return {"ok": false, "reason": "invalid_or_destroyed_vaccine"}
    var stats_check: Dictionary = Growth.validate_allocations(
        vaccine.get("invested_stats", null))
    if not bool(stats_check.get("ok", false)):
        return stats_check
    var skills_check: Dictionary = SkillRegistry.validate_loadout(
        vaccine.get("known_skills", null), vaccine.get("equipped_skills", null))
    if not bool(skills_check.get("ok", false)):
        return skills_check
    var checked: Dictionary = Growth.compile(Stats.make(
        balance_draft.get("player_stats", {})), vaccine["invested_stats"])
    if not bool(checked.get("ok", false)):
        return checked
    return {"ok": true, "vaccine_id": id, "generation": int(generation),
        "revision": revision, "invested_stats": vaccine["invested_stats"].duplicate(true),
        "known_skills": vaccine["known_skills"].duplicate(true),
        "equipped_skills": vaccine["equipped_skills"].duplicate(true)}

func configure_authorized_progression(e_gateway_snapshot: Dictionary) -> Dictionary:
    ## For A only, after durable E readback. No UI draft is ever accepted as a
    ## separate investment input. During a running region, before tick 1 only.
    var checked: Dictionary = _parse_authorized_progression(e_gateway_snapshot)
    if not bool(checked.get("ok", false)):
        progression_error = str(checked.get("reason", "invalid_E_progression"))
        return {"ok": false, "reason": progression_error}
    if running and simulation.fixed_tick > 0:
        progression_error = "in_mission_progression_change_denied"
        return {"ok": false, "reason": progression_error}
    if running:
        return _install_authorized_progression(checked)
    _staged_progression = checked.duplicate(true)
    progression_error = ""
    return {"ok": true, "staged": true}

func _install_authorized_progression(approved: Dictionary) -> Dictionary:
    if not running or not simulation.actors.has(PLAYER_ID) or (
            simulation.fixed_tick > 0):
        return {"ok": false, "reason": "must_apply_before_first_tick"}
    var calculated: Dictionary = Growth.compile(_baseline_player_stats,
        approved["invested_stats"])
    if not bool(calculated.get("ok", false)):
        progression_error = str(calculated.get("reason", "invalid_growth"))
        return calculated
    var validation: Dictionary = SkillRegistry.validate_loadout(
        approved["known_skills"], approved["equipped_skills"])
    if not bool(validation.get("ok", false)):
        progression_error = str(validation.get("reason", "invalid_loadout"))
        return validation
    var next_slots = SkillSlots.new()
    next_slots.configure(calculated["stats"])
    for index in range(approved["equipped_skills"].size()):
        var uid: String = str(approved["equipped_skills"][index])
        if not next_slots.learn(SkillRegistry.definition(uid)) or (
                not next_slots.equip(index, uid, true)):
            progression_error = "skill_install_failed:" + uid
            return {"ok": false, "reason": progression_error}
    var actor: Dictionary = simulation.actors[PLAYER_ID]
    # Replace from immutable encounter baseline, never increment current stats.
    actor["stats"] = calculated["stats"].duplicate(true)
    actor["skills"] = next_slots
    actor["status_state"]["tenacity"] = float(actor["stats"]["tenacity"])
    _authorized_progression = approved.duplicate(true)
    progression_error = ""
    player_stats = actor["stats"]
    _emit_resource_changes(true)
    return {"ok": true, "revision": approved["revision"],
        "vaccine_id": approved["vaccine_id"],
        "generation": approved["generation"]}

func actor_snapshot(actor_id: String = PLAYER_ID) -> Dictionary:
    var state: Dictionary = simulation.snapshot(actor_id)
    if state.is_empty():
        return {}
    if actor_id == PLAYER_ID:
        state["shield"] = effects.shield_value()
        state["stats"]["shield"] = state["shield"]
        state["authorized_progression"] = _authorized_progression.duplicate(true)
    return state

func skill_snapshot() -> Dictionary:
    if not simulation.actors.has(PLAYER_ID):
        return {}
    var actor: Dictionary = simulation.actors[PLAYER_ID]
    return actor["skills"].read_only_state(simulation.fixed_tick,
        int(simulation._effective_stats(actor).get("skill_haste", 0)))

func authorized_growth_snapshot() -> Dictionary:
    return _authorized_progression.duplicate(true)

func configure_shield_policy(policy: Dictionary) -> bool:
    ## A owns the unresolved GDD numbers, B owns deterministic application.
    return effects.configure_shield_policy(policy)

func configure_plugins(levels: Dictionary, seed: int) -> bool:
    ## A calls this from E's durable mission snapshot after confirmed rewards.
    return effects.configure_levels(levels, seed)

func upgrade_plugin_after_ack(plugin_id: String, level: int) -> bool:
    return effects.upgrade_plugin(plugin_id, level)

func learn_active_skill(definition: Dictionary, slot: int,
        progression_authorized: bool) -> bool:
    ## Legacy B regression support. Production E-authorized builds must arrive
    ## through configure_authorized_progression instead.
    if not _authorized_progression.is_empty():
        progression_error = "direct_skill_change_requires_E_readback"
        return false
    return simulation.learn_skill(PLAYER_ID, definition, slot, progression_authorized)

func set_combat_input(command: Dictionary) -> void:
    ## A may override keyboard sampling with camera-correct, remappable input.
    pending_input = command.duplicate(true)

func _key_direction() -> Vector2:
    var direction: Vector2 = Vector2.ZERO
    if Input.is_key_pressed(KEY_A) or Input.is_key_pressed(KEY_LEFT):
        direction.x -= 1.0
    if Input.is_key_pressed(KEY_D) or Input.is_key_pressed(KEY_RIGHT):
        direction.x += 1.0
    if Input.is_key_pressed(KEY_W) or Input.is_key_pressed(KEY_UP):
        direction.y -= 1.0
    if Input.is_key_pressed(KEY_S) or Input.is_key_pressed(KEY_DOWN):
        direction.y += 1.0
    return direction.normalized()

func _player_command() -> Dictionary:
    var aim_world: Vector3 = player.global_position + Vector3.RIGHT * 100.0
    if camera != null and is_instance_valid(camera) and camera.has_method("direction_to_window_pointer"):
        var size: Vector2 = get_viewport().get_visible_rect().size
        var pixel: Vector2 = get_viewport().get_mouse_position()
        var pointing: Variant = camera.call("direction_to_window_pointer",
            player.global_position, pixel, size)
        if pointing is Vector3:
            var vector: Vector3 = pointing
            vector.y = 0.0
            if vector.length_squared() > 0.000001:
                aim_world = player.global_position + vector.normalized() * 100.0
    var movement: Vector2 = _key_direction()
    var held_dash: bool = Input.is_key_pressed(KEY_SPACE)
    var dash_press: bool = held_dash and not _dash_key_down
    _dash_key_down = held_dash
    var command: Dictionary = {
        "move": movement, "aim_world": aim_world,
        "fire": not enemies.is_empty(), "external_movement": true,
        "dash": dash_press
    }
    # Temporary customizable fallback, not a finalized GDD key binding.
    var held_skills: Array[bool] = [
        Input.is_key_pressed(KEY_Q), Input.is_key_pressed(KEY_E),
        Input.is_key_pressed(KEY_C)
    ]
    for index in range(3):
        if held_skills[index] and not _skill_keys_down[index]:
            command["skill_slot"] = index
            break
    _skill_keys_down = held_skills
    command.merge(pending_input, true)
    return command

func _physics_process(delta: float) -> void:
    if not running or player == null or game_over or not is_instance_valid(player):
        return
    accumulator += maxf(0.0, delta)
    var steps: int = 0
    while accumulator >= STEP and steps < 8 and running and not game_over:
        advance_production_tick()
        accumulator -= STEP
        steps += 1

func advance_production_tick() -> Array[Dictionary]:
    ## Testable entry point: exactly one 60Hz gameplay tick, even headless.
    if not running or game_over or player == null:
        return []
    effects.notices.clear()
    var input: Dictionary = _player_command()
    intents[PLAYER_ID] = input
    _old_projectiles.clear()
    for p in simulation.projectiles:
        _old_projectiles[str(p["id"])] = p.duplicate(true)
    var tick_run_id: String = run_id
    var base_notes: Array[Dictionary] = super.advance_one_tick()
    var overflow: int = int(simulation.bus.metrics.get("overflow_rejected", 0))
    if overflow > last_combat_overflow:
        last_combat_overflow = overflow
        # Hard cap is an integrity fault, never an apparently successful run.
        running = false
        last_plugin_fault = "B_event_queue_overflow"
        combat_notice.emit({"kind": "combat_overload", "reason": last_plugin_fault,
            "rejected_events": overflow, "tick": simulation.fixed_tick})
        plugin_failure.emit(last_plugin_fault)
        return base_notes
    # D/A death ACK may synchronously return to hub during signal emission.
    if not running or player == null or run_id != tick_run_id:
        return base_notes
    _move_physical_player()
    _move_physical_enemies()
    _notify_retired_projectiles(base_notes)
    var c_notes: Array[Dictionary] = effects.after_fixed_step(simulation.last_processed_events)
    for note in c_notes:
        combat_notice.emit(note)
        if str(note.get("kind", "")) == "death":
            combat_actor_died.emit(str(note["actor_id"]), str(note.get("killer_id", PLAYER_ID)))
        if str(note.get("kind", "")) == "plugin_error":
            var reason: String = str(note.get("reason", "unknown_plugin_error"))
            if reason != last_plugin_fault:
                last_plugin_fault = reason
                plugin_failure.emit(reason)
    if running and run_id == tick_run_id:
        _emit_resource_changes()
        pending_input.clear()
    var all_notices: Array[Dictionary] = base_notes.duplicate(true)
    all_notices.append_array(c_notes)
    return all_notices

func _move_physical_player() -> void:
    var state: Dictionary = simulation.snapshot(PLAYER_ID)
    if state.is_empty() or not bool(state["alive"]):
        return
    var destination: Vector3 = state["position"]
    var displacement: Vector3 = destination - player.global_position
    if displacement.length_squared() > 0.000001:
        player.move_and_collide(displacement)
    simulation.set_actor_position(PLAYER_ID, player.global_position)
    if _has_player_speed:
        player.set("walk_speed", maxf(0.0,
            float(simulation._effective_stats(simulation.actors[PLAYER_ID]).get("move_speed", _old_player_speed))))
    if _has_player_movement:
        var flags: Dictionary = Statuses.active_flags(state["status_state"])
        player.set("movement_enabled", _old_player_movement and
            not bool(flags.get("stopped", false)) and not bool(flags.get("rooted", false)))

func _move_physical_enemies() -> void:
    if not map_plan.is_empty():
        var tile: Vector2i = _cell(player.global_position)
        if tile != last_path_cell:
            last_path_cell = tile
            cached_distances = Maps.distance_field(map_plan, tile)
    for id in enemies.keys():
        if not simulation.actors.has(id) or not bool(simulation.actors[id]["alive"]):
            continue
        var entity: Dictionary = enemies[id]
        var body: CharacterBody3D = entity["body"]
        if not is_instance_valid(body):
            continue
        var actor: Dictionary = simulation.actors[id]
        var flags: Dictionary = Statuses.active_flags(actor["status_state"])
        var movement: Vector2 = Vector2.ZERO
        if not bool(flags.get("stopped", false)) and not bool(flags.get("rooted", false)):
            var decision: Dictionary = AI.decide(actor, simulation.actors[PLAYER_ID])
            movement = decision["move"]
        var destination: Vector3 = player.global_position
        if not map_plan.is_empty() and movement.length_squared() > 0.0:
            destination = _next_path_target(body.global_position, destination)
            var vector: Vector3 = destination - body.global_position
            movement = Vector2(vector.x, vector.z).normalized() if vector.length_squared() > 0.000001 else Vector2.ZERO
        var speed: float = maxf(0.0, float(simulation._effective_stats(actor).get("move_speed", 0.0)))
        body.velocity = Vector3(movement.x, 0.0, movement.y) * speed
        body.move_and_slide()
        simulation.set_actor_position(str(id), body.global_position)

func _cell(point: Vector3) -> Vector2i:
    return Vector2i(floori(point.x / Maps.TILE_SIZE), floori(point.z / Maps.TILE_SIZE))

func _next_path_target(source: Vector3, fallback: Vector3) -> Vector3:
    var cell: Vector2i = _cell(source)
    if cached_distances.is_empty() or not Maps.is_floor(map_plan, cell):
        return fallback
    var width: int = int(map_plan["width"])
    var candidate: Vector2i = cell
    var score: int = cached_distances[cell.y * width + cell.x]
    for direction in [Vector2i(0,-1), Vector2i(1,0), Vector2i(0,1), Vector2i(-1,0)]:
        var other: Vector2i = cell + direction
        if not Maps.is_floor(map_plan, other):
            continue
        var distance: int = cached_distances[other.y * width + other.x]
        if distance >= 0 and (score < 0 or distance < score):
            score = distance
            candidate = other
    return Maps.cell_to_world(candidate) if candidate != cell else fallback

func _clip_projectile_to_floor(start: Vector3, finish: Vector3,
        _projectile: Dictionary) -> Dictionary:
    if map_plan.is_empty():
        return {"position": finish, "blocked": false}
    # Exact grid DDA over the same immutable D map tile contract. A sampled
    # segment can jump across a thin wall/corner at high projectile speed.
    var cell: Vector2i = _cell(start)
    if not Maps.is_floor(map_plan, cell):
        return {"position": start, "blocked": true}
    var target: Vector2i = _cell(finish)
    var delta: Vector2 = Vector2(finish.x - start.x, finish.z - start.z)
    var sx: int = 1 if delta.x > 0.0 else (-1 if delta.x < 0.0 else 0)
    var sz: int = 1 if delta.y > 0.0 else (-1 if delta.y < 0.0 else 0)
    var tx: float = INF
    var tz: float = INF
    var dtx: float = INF
    var dtz: float = INF
    if sx != 0:
        dtx = Maps.TILE_SIZE / absf(delta.x)
        var boundary_x: float = float(cell.x + (1 if sx > 0 else 0)) * Maps.TILE_SIZE
        tx = maxf(0.0, (boundary_x - start.x) / delta.x)
    if sz != 0:
        dtz = Maps.TILE_SIZE / absf(delta.y)
        var boundary_z: float = float(cell.y + (1 if sz > 0 else 0)) * Maps.TILE_SIZE
        tz = maxf(0.0, (boundary_z - start.z) / delta.y)
    var crossings: int = absi(target.x - cell.x) + absi(target.y - cell.y) + 2
    for index in range(crossings):
        if cell == target:
            return {"position": finish, "blocked": false}
        var t: float = minf(tx, tz)
        if t > 1.0:
            break
        if is_equal_approx(tx, tz):
            # Corner contact cannot slip diagonally through two solid walls.
            if not Maps.is_floor(map_plan, cell + Vector2i(sx, 0)) or (
                    not Maps.is_floor(map_plan, cell + Vector2i(0, sz))):
                return {"position": start.lerp(finish, clampf(t - 0.00001, 0.0, 1.0)),
                    "blocked": true}
            cell += Vector2i(sx, sz)
            tx += dtx
            tz += dtz
        elif tx < tz:
            cell.x += sx
            tx += dtx
        else:
            cell.y += sz
            tz += dtz
        if not Maps.is_floor(map_plan, cell):
            return {"position": start.lerp(finish, clampf(t - 0.00001, 0.0, 1.0)),
                "blocked": true}
    return {"position": finish, "blocked": false}

func _make_bullet_visual(p: Dictionary) -> Node3D:
    var shot := MeshInstance3D.new()
    var mesh := SphereMesh.new()
    mesh.radius = maxf(0.025, float(p.get("radius", 0.09)))
    mesh.height = mesh.radius * 2.0
    shot.mesh = mesh
    var mat := StandardMaterial3D.new()
    mat.albedo_color = Color(0.25, 0.95, 1.0) if str(p.get("team", "")) == "vaccine" else Color(1.0, 0.3, 0.4)
    mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
    shot.material_override = mat
    return shot

func _notify_retired_projectiles(notes: Array[Dictionary]) -> void:
    for event in notes:
        if str(event.get("kind", "")) != "projectile_retire":
            continue
        var id: String = str(event.get("id", ""))
        if _old_projectiles.has(id):
            effects.handle_boomerang_return(_old_projectiles[id])

func _emit_resource_changes(force: bool = false) -> void:
    if not simulation.actors.has(PLAYER_ID):
        return
    var actor: Dictionary = simulation.actors[PLAYER_ID]
    var hp: float = float(actor["stats"]["health"])
    var maximum: float = float(actor["stats"]["max_health"])
    if force or not is_equal_approx(last_health, hp):
        last_health = hp
        health_updated.emit(hp, maximum)
    var skills: Variant = actor["skills"]
    if force or not is_equal_approx(last_mana, skills.mana):
        last_mana = skills.mana
        mana_updated.emit(skills.mana, skills.max_mana)
    player_stats = actor["stats"]

func _on_combat_notice(_event: Dictionary) -> void:
    pass

func _on_combat_actor_died(actor_id: String, _killer_id: String) -> void:
    if _reported_deaths.has(actor_id):
        return
    # No E kill receipts after the authoritative B event queue has overflowed.
    if actor_id != PLAYER_ID and int(simulation.bus.metrics.get(
            "overflow_rejected", 0)) > 0:
        running = false
        if last_plugin_fault != "B_event_queue_overflow":
            last_plugin_fault = "B_event_queue_overflow"
            plugin_failure.emit(last_plugin_fault)
        return
    _reported_deaths[actor_id] = true
    if actor_id == PLAYER_ID:
        game_over = true
        if _has_player_movement and is_instance_valid(player):
            player.set("movement_enabled", false)
        player_died.emit()
        return
    if not enemies.has(actor_id):
        return
    var body: Variant = enemies[actor_id].get("body", null)
    enemies.erase(actor_id)
    bound_nodes.erase(actor_id)
    if is_instance_valid(body):
        body.queue_free()
    enemy_defeated.emit(actor_id, "%s:enemy:%s:death" % [run_id, actor_id])

func _apply_hit(enemy_id: String, hit_key: String) -> void:
    ## D existing headless test compatibility only; not used for live aiming.
    if not running or game_over or not enemies.has(enemy_id):
        return
    simulation.bus.set_tick(simulation.fixed_tick)
    var root: Dictionary = simulation.bus.submit_root({
        "kind": "damage", "actor_id": PLAYER_ID, "target_id": enemy_id,
        "attack_id": "test_hit", "hit_key": hit_key,
        "trigger_kind": "on_hit", "source_tags": ["basic_attack", "projectile_hit"],
        "attack": simulation.actors[PLAYER_ID]["attack_definition"].duplicate(true),
        "allowed_proc_triggers": ["on_hit", "on_crit"]
    })
    if root.is_empty():
        return
    simulation.presentation.clear()
    var processed: Array[Dictionary] = simulation.bus.drain_batch(
        int(simulation.settings.get("event_work_per_tick", 512)),
        Callable(simulation, "_resolve_event"))
    for note in simulation.presentation:
        combat_notice.emit(note)
        if str(note.get("kind", "")) == "death":
            combat_actor_died.emit(str(note["actor_id"]), str(note.get("killer_id", PLAYER_ID)))
    effects.notices.clear()
    var others: Array[Dictionary] = effects.after_fixed_step(processed)
    for note in others:
        combat_notice.emit(note)
        if str(note.get("kind", "")) == "death":
            combat_actor_died.emit(str(note["actor_id"]), str(note.get("killer_id", PLAYER_ID)))
    _emit_resource_changes()

func diagnostics() -> Dictionary:
    return {
        "run_id": run_id, "active": running, "tick": simulation.fixed_tick,
        "actors": simulation.actors.size(), "enemies": enemies.size(),
        "projectiles": simulation.projectiles.size(),
        "combat_counters": simulation.counters.duplicate(true),
        "proc_metrics": simulation.bus.metrics.duplicate(true),
        "pending_C_events": effects.outstanding_events(),
        "C_shields": effects.shield_diagnostics(),
        "progression_error": progression_error,
        "authorized_progression": _authorized_progression.duplicate(true),
        "skill_slots": skill_snapshot(),
        "fixed_tick_profile": simulation.profiling_snapshot(),
        "C_fault": effects.fault
    }
