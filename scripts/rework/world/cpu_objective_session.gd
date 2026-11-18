extends Node
## CPU on-foot objective orchestration. B validates every real enemy defeat.
## E receives world_state facts; this node never grants XP or saves.
signal enemy_spawn_requested(spawn: Dictionary)
signal objective_updated(snapshot: Dictionary)
signal extraction_interaction_requested(fact: Dictionary)
signal boss_spawned(actor_id: String)
const Maps = preload("res://scripts/rework/world/procedural_map.gd")
const Objectives = preload("res://scripts/rework/world/hardware_objectives.gd")
var run_id: String = ""
var world_state: Variant
var region_plan: Dictionary = {}
var geometry: Variant
var objectives: Variant
var known_hostiles: Dictionary = {}
var defeated_hostiles: Dictionary = {}
var boss_spawned_once: bool = false
var physics_tick: int = 0
var encounter_rng := RandomNumberGenerator.new()

func configure(new_run_id: String, planned: Dictionary, new_state: RefCounted,
        new_geometry: Node3D) -> bool:
    if new_run_id.is_empty() or new_state == null or new_geometry == null:
        return false
    if str(planned.get("id", "")) != "CPU" or int(planned.get("encounter_seed", 0)) <= 0:
        return false
    run_id = new_run_id
    region_plan = planned.duplicate(true)
    world_state = new_state
    geometry = new_geometry
    known_hostiles.clear()
    defeated_hostiles.clear()
    boss_spawned_once = false
    physics_tick = 0
    objectives = Objectives.new(run_id, "CPU")
    objectives.objective_completed.connect(_on_objective_completed)
    objectives.progress_changed.connect(_on_progress_changed)
    encounter_rng.seed = int(region_plan["encounter_seed"])
    return true

func on_world_interaction(fact: Dictionary) -> void:
    if str(fact.get("run_id", "")) != run_id or str(fact.get("region_id", "")) != "CPU":
        return
    match str(fact.get("type", "")):
        "hardware_objective_activated":
            if str(fact.get("objective_id", "")) == "stop_intruders":
                _spawn_intrusion_wave()
        "hardware_extraction_requested":
            if world_state.is_extraction_open("CPU"):
                extraction_interaction_requested.emit(fact.duplicate(true))

func tick_player(player_world: Vector3) -> void:
    if geometry == null or objectives == null:
        return
    physics_tick += 1
    if bool(geometry.goal_activated.get("secure_core", false)) and not bool(geometry.goal_completed.get("secure_core", false)):
        var place: Vector3 = geometry.objective_world("secure_core")
        if Vector2(place.x, place.z).distance_to(Vector2(player_world.x, player_world.z)) <= 1.8:
            objectives.apply_verified_event("secure_core", "defense_ticks",
                "%s:CPU:defense:%d" % [run_id, physics_tick], 1)

func confirm_enemy_defeated(actor_id: String, b_receipt_id: String) -> bool:
    if actor_id.is_empty() or b_receipt_id.is_empty() or not known_hostiles.has(actor_id):
        return false
    if defeated_hostiles.has(actor_id):
        return false
    if actor_id == "CPU-BOSS" and not objectives.all_objectives_completed():
        return false
    defeated_hostiles[actor_id] = b_receipt_id
    if actor_id == "CPU-BOSS":
        return world_state.confirm_boss_defeated("CPU", b_receipt_id)
    if bool(geometry.goal_activated.get("stop_intruders", false)):
        return objectives.apply_verified_event("stop_intruders", "kill", b_receipt_id, 1)
    return true

func _spawn_intrusion_wave() -> void:
    if not known_hostiles.is_empty():
        return
    var map_plan: Dictionary = geometry.map_plan
    var spawn_points: Array[Vector2i] = []
    var distances := Maps.distance_field(map_plan, geometry.world_to_cell(geometry.objective_world("stop_intruders")))
    for y in range(1, int(map_plan["height"]) - 1):
        for x in range(1, int(map_plan["width"]) - 1):
            var cell := Vector2i(x, y)
            if not Maps.is_floor(map_plan, cell) or cell == map_plan["spawn"] or cell == map_plan["boss"]:
                continue
            if distances[y * int(map_plan["width"]) + x] < 4:
                continue
            var is_objective: bool = false
            for item in map_plan["objectives"]:
                if item["cell"] == cell:
                    is_objective = true
            if not is_objective:
                spawn_points.append(cell)
    for index in range(spawn_points.size() - 1, 0, -1):
        var other: int = encounter_rng.randi_range(0, index)
        var tmp: Vector2i = spawn_points[index]
        spawn_points[index] = spawn_points[other]
        spawn_points[other] = tmp
    # The level must fail closed if it does not have enough independent spawns.
    if spawn_points.size() < 10:
        push_error("CPU procgen lacks 10 accessible distinct enemy spawn tiles")
        return
    for i in range(10):
        var actor_id: String = "CPU-VIRUS-%02d" % i
        known_hostiles[actor_id] = true
        enemy_spawn_requested.emit({
            "schema": 1, "run_id": run_id, "region_id": "CPU",
            "encounter_id": "CPU:intrusion", "actor_id": actor_id,
            "role": "process_rusher" if i % 2 == 0 else "overheater",
            "position": Maps.cell_to_world(spawn_points[i]), "boss": false,
            "seed": int(region_plan["encounter_seed"]), "difficulty": region_plan["difficulty"],
            "environment": region_plan["environment"].duplicate(true)
        })

func _on_objective_completed(fact: Dictionary) -> void:
    var objective_id: String = str(fact.get("objective_id", ""))
    if world_state.confirm_objective("CPU", objective_id, str(fact.get("receipt_id", ""))):
        geometry.set_objective_complete(objective_id)
    if objectives.all_objectives_completed() and geometry.set_boss_available():
        _spawn_boss()

func _on_progress_changed(snapshot: Dictionary) -> void:
    objective_updated.emit(snapshot.duplicate(true))

func _spawn_boss() -> void:
    if boss_spawned_once or not objectives.all_objectives_completed():
        return
    boss_spawned_once = true
    known_hostiles["CPU-BOSS"] = true
    enemy_spawn_requested.emit({
        "schema": 1, "run_id": run_id, "region_id": "CPU",
        "encounter_id": "CPU:boss", "actor_id": "CPU-BOSS",
        "role": "kernel_overseer", "position": geometry.boss_world(),
        "boss": true, "seed": int(region_plan["encounter_seed"]),
        "difficulty": region_plan["difficulty"], "environment": region_plan["environment"].duplicate(true)
    })
    boss_spawned.emit("CPU-BOSS")

func acknowledge_cleansed() -> bool:
    if world_state == null or geometry == null or not world_state.is_extraction_open("CPU"):
        return false
    return geometry.set_extraction_open()
