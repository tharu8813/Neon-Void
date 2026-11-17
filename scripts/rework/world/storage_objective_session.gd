extends Node3D
## Real 3D on-foot storage interaction routes. SSD: three distant data-node
## captures + 1800 defended transfer ticks. HDD: four platter repairs and
## three index restorations across distinct distant reachable cells.
## B exclusively owns enemy HP/attacks. E exclusively approves durable facts.
signal enemy_spawn_requested(spawn: Dictionary)
signal objective_updated(snapshot: Dictionary)
signal extraction_interaction_requested(fact: Dictionary)
signal boss_spawned(actor_id: String)
const Maps = preload("res://scripts/rework/world/procedural_map.gd")
const Objectives = preload("res://scripts/rework/world/hardware_objectives.gd")
const Catalog = preload("res://scripts/rework/world/hardware_catalog.gd")
const Environment = preload("res://scripts/rework/world/environment_rules.gd")
var run_id: String = ""
var region_id: String = ""
var world_state: Variant
var region_plan: Dictionary = {}
var geometry: Variant
var objectives: Variant
var encounter_rng := RandomNumberGenerator.new()
var targets: Array[Dictionary] = []
var known_hostiles: Dictionary = {}
var defeated_hostiles: Dictionary = {}
var wave_started: bool = false
var boss_spawned_once: bool = false
var transfer_started: bool = false
var physics_tick: int = 0

func configure(new_run_id: String, planned: Dictionary, new_state: RefCounted,
        new_geometry: Node3D) -> bool:
    var id: String = str(planned.get("id", ""))
    if new_run_id.is_empty() or not ["SSD", "HDD"].has(id) or new_state == null             or new_geometry == null or int(planned.get("encounter_seed", 0)) <= 0:
        return false
    run_id = new_run_id
    region_id = id
    region_plan = planned.duplicate(true)
    world_state = new_state
    geometry = new_geometry
    objectives = Objectives.new(run_id, region_id)
    objectives.objective_completed.connect(_on_objective_completed)
    objectives.progress_changed.connect(_on_progress_changed)
    encounter_rng.seed = int(region_plan["encounter_seed"])
    return _build_targets()

func _build_targets() -> bool:
    var plan: Dictionary = geometry.map_plan
    var distances: PackedInt32Array = Maps.distance_field(plan, plan["spawn"])
    var pool: Array[Dictionary] = []
    for y in range(1, int(plan["height"]) - 1):
        for x in range(1, int(plan["width"]) - 1):
            var cell := Vector2i(x, y)
            if not Maps.is_floor(plan, cell) or cell == plan["spawn"] or cell == plan["boss"]:
                continue
            var reserved: bool = false
            for goal in plan["objectives"]:
                reserved = reserved or goal["cell"] == cell
            if reserved:
                continue
            var distance: int = distances[y * int(plan["width"]) + x]
            if distance >= 3:
                pool.append({"cell": cell, "distance": distance})
    var needed: int = 3 if region_id == "SSD" else 7
    if pool.size() < needed:
        return false
    pool.sort_custom(func(a: Dictionary, b: Dictionary) -> bool:
        return int(a["distance"]) > int(b["distance"]))
    var chosen: Array[Vector2i] = []
    # Widely spaced distant rooms first, then a no-duplicates fallback for
    # smaller 'initial' seed maps. Every selected node is walkable and BFS-reachable.
    for separation in [4, 3, 2, 1]:
        for item in pool:
            var cell: Vector2i = item["cell"]
            if chosen.has(cell):
                continue
            var far_enough: bool = true
            for prior in chosen:
                if absi(prior.x - cell.x) + absi(prior.y - cell.y) < separation:
                    far_enough = false
                    break
            if far_enough:
                chosen.append(cell)
                if chosen.size() == needed:
                    break
        if chosen.size() == needed:
            break
    if chosen.size() != needed:
        return false
    var holder := Node3D.new()
    holder.name = "StorageObjectives"
    add_child(holder)
    for i in range(chosen.size()):
        var objective_id: String = "capture_node" if region_id == "SSD" else             ("repair_platter" if i < 4 else "restore_index")
        var at: Vector3 = Maps.cell_to_world(chosen[i])
        var marker := MeshInstance3D.new()
        marker.name = "StorageNode_%d" % i
        var mesh := CylinderMesh.new()
        mesh.top_radius = 0.32
        mesh.bottom_radius = 0.32
        mesh.height = 0.18
        marker.mesh = mesh
        marker.position = at + Vector3.UP * 0.10
        holder.add_child(marker)
        var label := Label3D.new()
        label.name = "StorageLabel_%d" % i
        label.font_size = 38
        label.pixel_size = 0.006
        label.rotation_degrees = Vector3(-90, 0, 0)
        label.position = at + Vector3.UP * 0.68
        holder.add_child(label)
        targets.append({
            "id": objective_id, "position": at, "cell": chosen[i],
            "index": i if region_id == "SSD" else (i if i < 4 else i - 4),
            "mesh": marker, "label": label, "done": false
        })
    _refresh_markers()
    return true

func _unlocked(objective_id: String) -> bool:
    if region_id == "HDD" and objective_id == "restore_index":
        return bool(objectives.progress["repair_platter"]["completed"])
    return true

func _refresh_markers() -> void:
    for item in targets:
        var goal_id: String = str(item["id"])
        var active: bool = _unlocked(goal_id) and             not bool(item["done"]) and             int(item["index"]) == int(objectives.progress[goal_id]["current"])
        var mat := StandardMaterial3D.new()
        mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
        mat.albedo_color = Color(0.15, 0.28, 0.29) if bool(item["done"]) else             (Color(0.20, 0.99, 0.74) if active else Color(0.42, 0.29, 0.15))
        item["mesh"].material_override = mat
        item["label"].text = ("%s %d  [F]" % [goal_id.to_upper(), int(item["index"]) + 1])             if active else ("COMPLETE" if bool(item["done"]) else "LOCKED")

func try_interact(player_world: Vector3) -> Dictionary:
    for i in range(targets.size()):
        var item: Dictionary = targets[i]
        var at: Vector3 = item["position"]
        if Vector2(at.x - player_world.x, at.z - player_world.z).length() > 0.78:
            continue
        var goal_id: String = str(item["id"])
        if bool(item["done"]) or not _unlocked(goal_id) or                 int(item["index"]) != int(objectives.progress[goal_id]["current"]):
            return {"type": "storage_node_locked", "region_id": region_id, "index": i}
        if not wave_started:
            _spawn_defenders()
        var kind: String = str(objectives.progress[goal_id]["kind"])
        var receipt: String = "%s:%s:%s:%d" % [
            run_id, region_id, goal_id, int(item["index"])]
        if not objectives.apply_verified_event(goal_id, kind, receipt):
            return {}
        item["done"] = true
        _refresh_markers()
        return {
            "schema": 1, "type": "storage_node_activated", "run_id": run_id,
            "region_id": region_id, "objective_id": goal_id,
            "node": i, "receipt_id": receipt
        }
    return {}

func on_world_interaction(fact: Dictionary) -> void:
    if str(fact.get("run_id", "")) != run_id or str(fact.get("region_id", "")) != region_id:
        return
    match str(fact.get("type", "")):
        "hardware_objective_activated":
            if region_id == "SSD" and str(fact.get("objective_id", "")) == "seal_transfer"                     and bool(objectives.progress["capture_node"]["completed"]):
                transfer_started = true
        "hardware_extraction_requested":
            if world_state.is_extraction_open(region_id):
                extraction_interaction_requested.emit(fact.duplicate(true))

func tick_player(player_world: Vector3) -> void:
    if region_id != "SSD" or not transfer_started             or bool(objectives.progress["seal_transfer"]["completed"]):
        return
    physics_tick += 1
    var anchor: Vector3 = geometry.objective_world("seal_transfer")
    if Vector2(anchor.x - player_world.x, anchor.z - player_world.z).length() <= 1.8:
        objectives.apply_verified_event("seal_transfer", "transfer_ticks",
            "%s:SSD:transfer:%d" % [run_id, physics_tick])

func _spawn_defenders() -> void:
    if wave_started:
        return
    var plan: Dictionary = geometry.map_plan
    var field: PackedInt32Array = Maps.distance_field(plan, plan["spawn"])
    var cells: Array[Vector2i] = []
    for y in range(1, int(plan["height"]) - 1):
        for x in range(1, int(plan["width"]) - 1):
            var cell := Vector2i(x, y)
            if not Maps.is_floor(plan, cell) or cell == plan["boss"]:
                continue
            if field[y * int(plan["width"]) + x] >= 5:
                cells.append(cell)
    var descriptor: Dictionary = Environment.describe(
        region_plan, [], int(region_plan["encounter_seed"]))
    var base_count: int = 10 if region_id == "SSD" else 12
    var count: int = ceili(float(base_count) *
        float(descriptor.get("enemy_spawn_multiplier", 1.0)))
    if cells.size() < count:
        push_error("Storage map lacks distinct B spawn cells: " + region_id)
        return
    for i in range(cells.size() - 1, 0, -1):
        var index: int = encounter_rng.randi_range(0, i)
        var temp: Vector2i = cells[i]
        cells[i] = cells[index]
        cells[index] = temp
    wave_started = true
    var roles: Array = Catalog.get_hardware(region_id)["enemy_roles"]
    for i in range(count):
        _emit_hostile("%s-HOSTILE-%02d" % [region_id, i],
            str(roles[i % roles.size()]), Maps.cell_to_world(cells[i]), false)

func _emit_hostile(actor_id: String, role: String, position: Vector3, boss: bool) -> void:
    known_hostiles[actor_id] = true
    enemy_spawn_requested.emit({
        "schema": 1, "run_id": run_id, "region_id": region_id,
        "encounter_id": region_id + (":boss" if boss else ":defenders"),
        "actor_id": actor_id, "role": role, "position": position, "boss": boss,
        "seed": int(region_plan["encounter_seed"]),
        "difficulty": region_plan["difficulty"],
        "environment": region_plan["environment"].duplicate(true)
    })

func confirm_enemy_defeated(actor_id: String, receipt_id: String) -> bool:
    if not known_hostiles.has(actor_id) or receipt_id.is_empty()             or defeated_hostiles.has(actor_id):
        return false
    var boss_id: String = region_id + "-BOSS"
    if actor_id == boss_id and not objectives.all_objectives_completed():
        return false
    defeated_hostiles[actor_id] = receipt_id
    if actor_id == boss_id:
        return world_state.confirm_boss_defeated(region_id, receipt_id)
    _maybe_spawn_boss()
    return true

func _on_objective_completed(fact: Dictionary) -> void:
    var goal_id: String = str(fact.get("objective_id", ""))
    if world_state.confirm_objective(region_id, goal_id, str(fact.get("receipt_id", ""))):
        geometry.set_objective_complete(goal_id)
    _refresh_markers()
    _maybe_spawn_boss()

func _on_progress_changed(snapshot: Dictionary) -> void:
    objective_updated.emit(snapshot.duplicate(true))

func _maybe_spawn_boss() -> void:
    if boss_spawned_once or not wave_started or not objectives.all_objectives_completed()             or defeated_hostiles.size() != known_hostiles.size()             or not geometry.set_boss_available():
        return
    boss_spawned_once = true
    _emit_hostile(region_id + "-BOSS",
        str(Catalog.get_hardware(region_id)["boss_role"]), geometry.boss_world(), true)
    boss_spawned.emit(region_id + "-BOSS")

func acknowledge_cleansed() -> bool:
    if geometry == null or not world_state.is_extraction_open(region_id):
        return false
    return geometry.set_extraction_open()
