extends Node3D
## RAM: interactively purge four corrupted blocks and locate the authentic
## checksum pad among replicas twice. Hostiles/boss are B actors, never D HP.
signal enemy_spawn_requested(spawn: Dictionary)
signal objective_updated(snapshot: Dictionary)
signal extraction_interaction_requested(fact: Dictionary)
signal boss_spawned(actor_id: String)
const Maps = preload("res://scripts/rework/world/procedural_map.gd")
const Objectives = preload("res://scripts/rework/world/hardware_objectives.gd")
const Catalog = preload("res://scripts/rework/world/hardware_catalog.gd")
const Environment = preload("res://scripts/rework/world/environment_rules.gd")
var run_id: String = ""
var world_state: Variant
var region_plan: Dictionary = {}
var geometry: Variant
var objectives: Variant
var encounter_rng := RandomNumberGenerator.new()
var known_hostiles: Dictionary = {}
var defeated_hostiles: Dictionary = {}
var wave_started: bool = false
var boss_spawned_once: bool = false
var action_number: int = 0
var verification_round: int = 0
var real_slot: int = -1
var wrong_choices: Dictionary = {}
var pads: Array[Dictionary] = []
var puzzle_holder: Node3D

func configure(new_run_id: String, planned: Dictionary, new_state: RefCounted,
        new_geometry: Node3D) -> bool:
    if new_run_id.is_empty() or new_state == null or new_geometry == null:
        return false
    if str(planned.get("id", "")) != "RAM" or int(planned.get("encounter_seed", 0)) <= 0:
        return false
    run_id = new_run_id
    region_plan = planned.duplicate(true)
    world_state = new_state
    geometry = new_geometry
    objectives = Objectives.new(run_id, "RAM")
    objectives.objective_completed.connect(_on_objective_completed)
    objectives.progress_changed.connect(_on_progress_changed)
    encounter_rng.seed = int(region_plan["encounter_seed"])
    if not _build_puzzle():
        return false
    _change_real_slot()
    return true

func _build_puzzle() -> bool:
    var plan: Dictionary = geometry.map_plan
    var verifier: Vector3 = geometry.objective_world("verify_memory")
    var candidates: Array[Dictionary] = []
    for y in range(1, int(plan["height"]) - 1):
        for x in range(1, int(plan["width"]) - 1):
            var cell := Vector2i(x, y)
            if not Maps.is_floor(plan, cell) or cell == plan["boss"] or cell == plan["spawn"]:
                continue
            var position: Vector3 = Maps.cell_to_world(cell)
            var distance: float = position.distance_to(verifier)
            if distance < 1.5 or distance > 6.5:
                continue
            var reserved: bool = false
            for objective in plan["objectives"]:
                reserved = reserved or objective["cell"] == cell
            if not reserved:
                candidates.append({"cell": cell, "position": position, "distance": distance})
    if candidates.size() < 3:
        return false
    candidates.sort_custom(func(a: Dictionary, b: Dictionary) -> bool:
        return float(a["distance"]) < float(b["distance"]))
    puzzle_holder = Node3D.new()
    puzzle_holder.name = "MemoryChecksumReplicas"
    add_child(puzzle_holder)
    for i in range(3):
        var item: Dictionary = candidates[i]
        var pad := MeshInstance3D.new()
        pad.name = "ChecksumPad_%d" % i
        var mesh := CylinderMesh.new()
        mesh.top_radius = 0.34
        mesh.bottom_radius = 0.35
        mesh.height = 0.14
        pad.mesh = mesh
        pad.position = item["position"] + Vector3.UP * 0.10
        puzzle_holder.add_child(pad)
        var label := Label3D.new()
        label.name = "ChecksumLabel_%d" % i
        label.font_size = 42
        label.pixel_size = 0.006
        label.rotation_degrees = Vector3(-90.0, 0.0, 0.0)
        label.position = item["position"] + Vector3.UP * 0.70
        puzzle_holder.add_child(label)
        pads.append({"position": item["position"], "visual": pad, "label": label})
    puzzle_holder.visible = false
    return true

func _change_real_slot() -> void:
    real_slot = encounter_rng.randi_range(0, pads.size() - 1)
    wrong_choices.clear()
    for i in range(pads.size()):
        var real: bool = i == real_slot
        var mat := StandardMaterial3D.new()
        mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
        mat.albedo_color = Color(0.22, 0.95, 0.48) if real else Color(0.55, 0.22, 0.9)
        pads[i]["visual"].material_override = mat
        pads[i]["label"].text = "VALID CRC  [F]" if real else "CORRUPT CRC  [F]"

func on_world_interaction(fact: Dictionary) -> void:
    if str(fact.get("run_id", "")) != run_id or str(fact.get("region_id", "")) != "RAM":
        return
    match str(fact.get("type", "")):
        "hardware_objective_activated":
            if str(fact.get("objective_id", "")) == "purge_corruption":
                if not wave_started:
                    _spawn_mirrors()
                action_number += 1
                objectives.apply_verified_event("purge_corruption", "purge",
                    "%s:RAM:purge:%d" % [run_id, action_number])
        "hardware_extraction_requested":
            if world_state.is_extraction_open("RAM"):
                extraction_interaction_requested.emit(fact.duplicate(true))

func try_verify(player_world: Vector3) -> Dictionary:
    if not bool(objectives.progress["purge_corruption"]["completed"])             or bool(objectives.progress["verify_memory"]["completed"]):
        return {}
    for i in range(pads.size()):
        var position: Vector3 = pads[i]["position"]
        if Vector2(position.x - player_world.x, position.z - player_world.z).length() > 0.75:
            continue
        var valid: bool = i == real_slot
        if valid:
            verification_round += 1
            objectives.apply_verified_event("verify_memory", "verify",
                "%s:RAM:verify:%d" % [run_id, verification_round])
            if not bool(objectives.progress["verify_memory"]["completed"]):
                _change_real_slot()
        elif not wrong_choices.has(i):
            wrong_choices[i] = true
            _spawn_decoy(i)
        return {
            "schema": 1, "type": "ram_checksum_tested", "run_id": run_id,
            "region_id": "RAM", "slot": i, "authentic": valid,
            "verification_round": verification_round
        }
    return {}

func _spawn_mirrors() -> void:
    if wave_started:
        return
    var plan: Dictionary = geometry.map_plan
    var field: PackedInt32Array = Maps.distance_field(plan, plan["spawn"])
    var cells: Array[Vector2i] = []
    for y in range(1, int(plan["height"]) - 1):
        for x in range(1, int(plan["width"]) - 1):
            var cell := Vector2i(x, y)
            if Maps.is_floor(plan, cell) and field[y * int(plan["width"]) + x] >= 5                     and cell != plan["boss"]:
                cells.append(cell)
    var descriptor: Dictionary = Environment.describe(
        region_plan, [], int(region_plan["encounter_seed"]))
    var wave_count: int = ceili(8.0 * float(descriptor.get("enemy_spawn_multiplier", 1.0)))
    if cells.size() < wave_count:
        push_error("RAM: no valid distinct combat spawn cells")
        return
    for i in range(cells.size() - 1, 0, -1):
        var j: int = encounter_rng.randi_range(0, i)
        var temp: Vector2i = cells[i]
        cells[i] = cells[j]
        cells[j] = temp
    wave_started = true
    for i in range(wave_count):
        _emit_hostile("RAM-MIRROR-%02d" % i, "mirror" if i % 2 == 0 else "duplicator",
            Maps.cell_to_world(cells[i]), false)

func _spawn_decoy(slot: int) -> void:
    var actor_id: String = "RAM-DECOY-%d-%d" % [verification_round, slot]
    _emit_hostile(actor_id, "duplicator", pads[slot]["position"], false)

func _emit_hostile(actor_id: String, role: String, place: Vector3, boss: bool) -> void:
    known_hostiles[actor_id] = true
    enemy_spawn_requested.emit({
        "schema": 1, "run_id": run_id, "region_id": "RAM",
        "encounter_id": "RAM:checksum" if not boss else "RAM:boss",
        "actor_id": actor_id, "role": role, "position": place,
        "boss": boss, "seed": int(region_plan["encounter_seed"]),
        "difficulty": region_plan["difficulty"],
        "environment": region_plan["environment"].duplicate(true)
    })

func confirm_enemy_defeated(actor_id: String, receipt_id: String) -> bool:
    if not known_hostiles.has(actor_id) or receipt_id.is_empty() or defeated_hostiles.has(actor_id):
        return false
    if actor_id == "RAM-BOSS" and not objectives.all_objectives_completed():
        return false
    defeated_hostiles[actor_id] = receipt_id
    if actor_id == "RAM-BOSS":
        return world_state.confirm_boss_defeated("RAM", receipt_id)
    _maybe_spawn_boss()
    return true

func _on_objective_completed(fact: Dictionary) -> void:
    var id: String = str(fact.get("objective_id", ""))
    if world_state.confirm_objective("RAM", id, str(fact.get("receipt_id", ""))):
        geometry.set_objective_complete(id)
    if id == "purge_corruption":
        puzzle_holder.visible = true
    _maybe_spawn_boss()

func _on_progress_changed(snapshot: Dictionary) -> void:
    objective_updated.emit(snapshot.duplicate(true))

func _maybe_spawn_boss() -> void:
    if boss_spawned_once or not wave_started or not objectives.all_objectives_completed():
        return
    # Every mirror AND every spawned decoy must have a B-certified death.
    if defeated_hostiles.size() != known_hostiles.size() or not geometry.set_boss_available():
        return
    boss_spawned_once = true
    _emit_hostile("RAM-BOSS", str(Catalog.get_hardware("RAM")["boss_role"]),
        geometry.boss_world(), true)
    boss_spawned.emit("RAM-BOSS")

func acknowledge_cleansed() -> bool:
    if geometry == null or not world_state.is_extraction_open("RAM"):
        return false
    return geometry.set_extraction_open()
