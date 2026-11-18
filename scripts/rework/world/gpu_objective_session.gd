extends Node3D
## D hardware gameplay: live 3D GPU overload controls, telegraphed beam dodges,
## B-authoritative hostile kills and boss, E-owned persistence and XP.
signal enemy_spawn_requested(spawn: Dictionary)
signal objective_updated(snapshot: Dictionary)
signal extraction_interaction_requested(fact: Dictionary)
signal boss_spawned(actor_id: String)
## Requests B authority; a contact is NOT an applied hit or status.
signal environment_contact_requested(fact: Dictionary)
const Maps = preload("res://scripts/rework/world/procedural_map.gd")
const Objectives = preload("res://scripts/rework/world/hardware_objectives.gd")
const Catalog = preload("res://scripts/rework/world/hardware_catalog.gd")
const EnvironmentRules = preload("res://scripts/rework/world/environment_rules.gd")
const AccessAudit = preload("res://scripts/rework/world/hardware_access_audit.gd")
var run_id: String = ""
var world_state: Variant
var region_plan: Dictionary = {}
var geometry: Variant
var objectives: Variant
var encounter_rng := RandomNumberGenerator.new()
var known_hostiles: Dictionary = {}
var defeated_hostiles: Dictionary = {}
var wave_started: bool = false
var wave_target_count: int = 8
var boss_spawned_once: bool = false
var coolant_started: bool = false
var physics_tick: int = 0
var action_number: int = 0
var beam_tick: int = 0
var beam: MeshInstance3D
var beam_orientations: Array[bool] = []

func configure(new_run_id: String, planned: Dictionary, new_state: RefCounted,
        new_geometry: Node3D) -> bool:
    if new_run_id.is_empty() or new_state == null or new_geometry == null:
        return false
    if str(planned.get("id", "")) != "GPU" or int(planned.get("encounter_seed", 0)) <= 0:
        return false
    run_id = new_run_id
    region_plan = planned.duplicate(true)
    world_state = new_state
    geometry = new_geometry
    objectives = Objectives.new(run_id, "GPU")
    objectives.objective_completed.connect(_on_objective_completed)
    objectives.progress_changed.connect(_on_progress_changed)
    encounter_rng.seed = int(region_plan["encounter_seed"])
    beam_orientations = AccessAudit.gpu_dodge_orientations(geometry.map_plan)
    if beam_orientations.is_empty():
        return false
    _build_beam()
    return true

func _build_beam() -> void:
    beam = MeshInstance3D.new()
    beam.name = "TelegraphedOverloadBeam"
    var mesh := BoxMesh.new()
    mesh.size = Vector3(9.0, 0.06, 0.7)
    beam.mesh = mesh
    var mat := StandardMaterial3D.new()
    mat.albedo_color = Color(1.0, 0.25, 0.10, 0.55)
    mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
    mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
    beam.material_override = mat
    beam.position = geometry.objective_world("stabilize_coolant") + Vector3.UP * 0.15
    beam.visible = false
    add_child(beam)

func on_world_interaction(fact: Dictionary) -> void:
    if str(fact.get("run_id", "")) != run_id or str(fact.get("region_id", "")) != "GPU":
        return
    match str(fact.get("type", "")):
        "hardware_objective_activated":
            var goal_id: String = str(fact.get("objective_id", ""))
            if goal_id == "disable_emitter":
                if not wave_started:
                    _spawn_overload_wave()
                action_number += 1
                objectives.apply_verified_event(goal_id, "switch",
                    "%s:GPU:emitter:%d" % [run_id, action_number])
            elif goal_id == "stabilize_coolant":
                coolant_started = true
        "hardware_extraction_requested":
            if world_state.is_extraction_open("GPU"):
                extraction_interaction_requested.emit(fact.duplicate(true))

func tick_player(player_world: Vector3) -> void:
    if geometry == null or not coolant_started or objectives.all_objectives_completed():
        return
    physics_tick += 1
    beam_tick += 1
    # 45/90 fixed 60-Hz frames warn of the coming strike. F on the coolant
    # device starts the sequence; walking out of the beam earns each dodge.
    var trial: int = int(objectives.progress["stabilize_coolant"]["current"])
    # A narrow corridor may not admit a legal escape for both axes. Rotate
    # ONLY through physically dodgeable beam orientations, never a softlock.
    var horizontal: bool = beam_orientations[trial % beam_orientations.size()]
    beam.rotation.y = 0.0 if horizontal else PI / 2.0
    beam.visible = beam_tick >= 45
    if beam_tick < 90:
        return
    beam_tick = 0
    beam.visible = false
    var center: Vector3 = geometry.objective_world("stabilize_coolant")
    var offset: Vector2 = Vector2(player_world.x - center.x, player_world.z - center.z)
    # Standing too far away is NOT a dodge; the player has to engage the device.
    var in_arena: bool = offset.length() <= 3.8
    # BoxMesh half-width 0.35m + 0.30m player capsule radius.
    # Both the visual and reported contact use the same physical threshold.
    var in_beam: bool = absf(offset.y if horizontal else offset.x) <= 0.65
    if in_arena and in_beam and geometry.can_walk(player_world):
        var cycle: int = floori(float(physics_tick - 1) / 90.0)
        environment_contact_requested.emit({
            "schema": 1, "type": "world_hazard_contact",
            "run_id": run_id, "region_id": "GPU", "actor_id": "VACCINE",
            "hazard_kind": "coolant_overload_beam", "hazard_id": "GPU:beam:%d" % cycle,
            "cycle": cycle, "tick": physics_tick,
            "player_world": player_world, "source_world": center,
            "orientation": "horizontal" if horizontal else "vertical",
            "contact_radius": 0.65, "damage_type": "magic"
        })
    if in_arena and not in_beam:
        objectives.apply_verified_event("stabilize_coolant", "dodge_hazard",
            "%s:GPU:dodge:%d" % [run_id, physics_tick])
    # Do NOT alter HP here. A/B must acknowledge via B fixed-tick world-hazard
    # entrypoint; no UI or reward may call this event a successful damage hit.

func _spawn_overload_wave() -> void:
    if wave_started:
        return
    var plan: Dictionary = geometry.map_plan
    var candidates: Array[Vector2i] = []
    var field: PackedInt32Array = Maps.distance_field(plan, plan["spawn"])
    for y in range(1, int(plan["height"]) - 1):
        for x in range(1, int(plan["width"]) - 1):
            var cell := Vector2i(x, y)
            if not Maps.is_floor(plan, cell) or cell == plan["boss"]:
                continue
            if field[y * int(plan["width"]) + x] < 6:
                continue
            var reserved: bool = false
            for objective in plan["objectives"]:
                reserved = reserved or objective["cell"] == cell
            if not reserved:
                candidates.append(cell)
    var descriptor: Dictionary = EnvironmentRules.describe(
        region_plan, [], int(region_plan["encounter_seed"]))
    wave_target_count = ceili(8.0 * float(descriptor.get("enemy_spawn_multiplier", 1.0)))
    if candidates.size() < wave_target_count:
        push_error("GPU map lacks required distinct enemy spawn cells")
        return
    for i in range(candidates.size() - 1, 0, -1):
        var j: int = encounter_rng.randi_range(0, i)
        var tmp: Vector2i = candidates[i]
        candidates[i] = candidates[j]
        candidates[j] = tmp
    wave_started = true
    for i in range(wave_target_count):
        var actor_id: String = "GPU-OVERLOAD-%02d" % i
        known_hostiles[actor_id] = true
        enemy_spawn_requested.emit({
            "schema": 1, "run_id": run_id, "region_id": "GPU",
            "encounter_id": "GPU:overload", "actor_id": actor_id,
            "role": "beam_caster" if i % 2 == 0 else "hazard_warden",
            "position": Maps.cell_to_world(candidates[i]), "boss": false,
            "seed": int(region_plan["encounter_seed"]),
            "difficulty": region_plan["difficulty"],
            "environment": region_plan["environment"].duplicate(true)
        })

func confirm_enemy_defeated(actor_id: String, receipt_id: String) -> bool:
    if not known_hostiles.has(actor_id) or receipt_id.is_empty() or defeated_hostiles.has(actor_id):
        return false
    if actor_id == "GPU-BOSS" and not objectives.all_objectives_completed():
        return false
    defeated_hostiles[actor_id] = receipt_id
    if actor_id == "GPU-BOSS":
        return world_state.confirm_boss_defeated("GPU", receipt_id)
    _maybe_spawn_boss()
    return true

func _on_objective_completed(fact: Dictionary) -> void:
    var id: String = str(fact.get("objective_id", ""))
    if world_state.confirm_objective("GPU", id, str(fact.get("receipt_id", ""))):
        geometry.set_objective_complete(id)
    _maybe_spawn_boss()

func _on_progress_changed(snapshot: Dictionary) -> void:
    objective_updated.emit(snapshot.duplicate(true))

func _maybe_spawn_boss() -> void:
    if boss_spawned_once or not wave_started or not objectives.all_objectives_completed():
        return
    if defeated_hostiles.size() < wave_target_count or not geometry.set_boss_available():
        return
    boss_spawned_once = true
    known_hostiles["GPU-BOSS"] = true
    enemy_spawn_requested.emit({
        "schema": 1, "run_id": run_id, "region_id": "GPU",
        "encounter_id": "GPU:boss", "actor_id": "GPU-BOSS",
        "role": str(Catalog.get_hardware("GPU")["boss_role"]),
        "position": geometry.boss_world(), "boss": true,
        "seed": int(region_plan["encounter_seed"]),
        "difficulty": region_plan["difficulty"],
        "environment": region_plan["environment"].duplicate(true)
    })
    boss_spawned.emit("GPU-BOSS")

func acknowledge_cleansed() -> bool:
    if geometry == null or not world_state.is_extraction_open("GPU"):
        return false
    return geometry.set_extraction_open()
