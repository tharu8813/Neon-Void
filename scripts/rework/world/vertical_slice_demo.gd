extends Node3D
## Isolated D-only runnable preview: Hub -> motherboard -> CPU -> boss -> extract.
## Uses B's deterministic damage and D terrain. Production A/E provides durable
## mission/extraction ACKs; demo_mode=true uses visibly non-durable local ACKs.
signal terminal_requested(fact: Dictionary)
signal mission_start_requested(fact: Dictionary)
signal world_fact_requested(fact: Dictionary)
signal extraction_commit_requested(fact: Dictionary)
signal death_commit_requested(fact: Dictionary)
signal mission_returned(fact: Dictionary)
signal unavailable_hardware_requested(region_id: String)
signal integration_error(reason: String)
const HubScene = preload("res://scenes/rework/world/defender_hub.tscn")
const BoardScene = preload("res://scenes/rework/world/motherboard.tscn")
const HubLayout = preload("res://scripts/rework/world/hub_layout.gd")
const BoardLayout = preload("res://scripts/rework/world/motherboard_layout.gd")
const Planner = preload("res://scripts/rework/world/mission_planner.gd")
const Maps = preload("res://scripts/rework/world/procedural_map.gd")
const Geometry = preload("res://scripts/rework/world/hardware_geometry.gd")
const ObjectiveSession = preload("res://scripts/rework/world/cpu_objective_session.gd")
const PlayerMotor = preload("res://scripts/rework/world/preview_player_motor.gd")
const CombatPreview = preload("res://scripts/rework/world/demo_combat_bridge.gd")
const CameraRig = preload("res://scripts/rework/camera/look_ahead_camera.gd")
const WorldState = preload("res://scripts/rework/world/extraction_state.gd")
const Gate = preload("res://scripts/rework/world/extraction_gate.gd")

@export var demo_mode: bool = true
@export var mission_seed: int = 42
var current_mode: String = ""
var current_run_id: String = ""
var mission: Dictionary = {}
var world_state: RefCounted
var extraction_gate: RefCounted
var active_level: Node3D
var board: Node3D
var hardware: Node3D
var cpu_session: Node
var combat: Node3D
var player: CharacterBody3D
var camera: Camera3D
var progression_bridge: RefCounted
var pending_world_receipts: Dictionary = {}
var pending_death: Dictionary = {}
var last_error: String = ""
var preview_counter: int = 0

func _ready() -> void:
    _build_player()
    camera = CameraRig.new()
    camera.name = "LookAheadCamera"
    add_child(camera)
    camera.current = true
    camera.global_position = Vector3(13, 14, 9)
    combat = CombatPreview.new()
    combat.name = "BCombatPreviewBridge"
    add_child(combat)
    combat.enemy_defeated.connect(_on_enemy_defeated)
    combat.player_died.connect(_on_player_died)
    enter_hub()

func _build_player() -> void:
    player = PlayerMotor.new()
    player.name = "VaccinePlayerPreview"
    var shape := CollisionShape3D.new()
    var capsule := CapsuleShape3D.new()
    capsule.radius = 0.30
    capsule.height = 1.0
    shape.shape = capsule
    player.add_child(shape)
    var model := MeshInstance3D.new()
    var sphere := SphereMesh.new()
    sphere.radius = 0.29
    sphere.height = 0.58
    model.mesh = sphere
    var material := StandardMaterial3D.new()
    material.albedo_color = Color(0.2, 0.88, 0.97)
    model.material_override = material
    player.add_child(model)
    add_child(player)

func _physics_process(delta: float) -> void:
    if player == null or camera == null:
        return
    var viewport_size: Vector2 = get_viewport().get_visible_rect().size
    var mouse: Vector2 = get_viewport().get_mouse_position()
    var logical: Dictionary = CameraRigMath.window_to_logical(mouse, viewport_size)
    var pointer: Vector2 = logical.get("logical", Vector2(640, 360))
    camera.call("follow", player.global_position, pointer, delta)
    if current_mode == "cpu" and cpu_session != null:
        cpu_session.tick_player(player.global_position)

const CameraRigMath = preload("res://scripts/rework/camera/look_ahead_math.gd")

func _unhandled_input(event: InputEvent) -> void:
    if event is InputEventKey and event.pressed and not event.echo and event.keycode == KEY_F:
        interact()

func interact() -> Dictionary:
    match current_mode:
        "hub":
            active_level.request_interaction(player.global_position)
        "motherboard":
            var result: Dictionary = board.request_gate_near_player(player.global_position)
            if result.has("region_id"):
                if str(result["region_id"]) == "CPU":
                    _enter_cpu(result["region_plan"])
                else:
                    board.return_to_board(str(result["region_id"]))
                    unavailable_hardware_requested.emit(str(result["region_id"]))
            return result
        "cpu":
            return hardware.interact_at(player.global_position)
    return {}

func attach_progression_bridge(bridge: RefCounted) -> void:
    ## A supplies the E-backed bridge; for real mode it must be attached BEFORE
    ## confirm_mission_started. D never opens or edits a save file.
    progression_bridge = bridge

func enter_hub() -> void:
    _remove_active_level()
    current_mode = "hub"
    board = null
    hardware = null
    cpu_session = null
    pending_world_receipts.clear()
    pending_death.clear()
    combat.reset()
    active_level = HubScene.instantiate()
    active_level.name = "ActiveDefenderHub"
    add_child(active_level)
    active_level.terminal_requested.connect(_on_hub_terminal)
    player.global_position = HubLayout.SPAWN + Vector3.UP * 0.5
    camera.world_bounds = HubLayout.BOUNDS
    camera.global_position = Vector3(HubLayout.SPAWN.x, 14, HubLayout.SPAWN.z)
    player.movement_enabled = true

func _on_hub_terminal(fact: Dictionary) -> void:
    terminal_requested.emit(fact.duplicate(true))
    if str(fact.get("action", "")) != "start_mission":
        return
    if demo_mode:
        begin_preview_mission(mission_seed)
    else:
        mission_start_requested.emit({"schema": 1, "type": "mission_start_requested", "seed": mission_seed})

func begin_preview_mission(seed: int) -> bool:
    if not demo_mode or current_mode != "hub" or seed < 0:
        return false
    preview_counter += 1
    return confirm_mission_started("preview-%d-%d" % [seed, preview_counter], seed)

func confirm_mission_started(run_id: String, seed: int) -> bool:
    if current_mode != "hub" or run_id.is_empty() or seed < 0:
        return false
    if not demo_mode and progression_bridge == null:
        integration_error.emit("missing_E_bridge_for_durable_mission")
        return false
    var planned: Dictionary = Planner.create_mission(seed)
    if planned.is_empty() or Planner.count_easy(planned) < 1:
        return false
    current_run_id = run_id
    mission = planned
    world_state = WorldState.new(run_id)
    world_state.world_fact.connect(_on_world_fact)
    extraction_gate = Gate.new(world_state)
    extraction_gate.extraction_prepared.connect(_on_extraction_prepared)
    return _enter_motherboard()

func _enter_motherboard() -> bool:
    _remove_active_level()
    current_mode = "motherboard"
    hardware = null
    cpu_session = null
    board = BoardScene.instantiate()
    board.name = "ActiveMotherboard"
    active_level = board
    add_child(board)
    board.setup_run(current_run_id, mission)
    player.global_position = BoardLayout.SPAWN + Vector3.UP * 0.5
    camera.world_bounds = BoardLayout.BOUNDS
    camera.global_position = Vector3(BoardLayout.SPAWN.x, 14, BoardLayout.SPAWN.z)
    combat.begin_encounter(player, camera, {}, current_run_id)
    for enemy in BoardLayout.INTRO:
        if not combat.spawn_hostile({
            "actor_id": str(enemy["id"]), "position": enemy["position"], "boss": false
        }):
            integration_error.emit("intro_enemy_spawn_failed")
            return false
    return true

func _enter_cpu(region_plan: Dictionary) -> bool:
    var planned_map: Dictionary = Maps.generate("CPU",
        int(region_plan.get("map_seed", 0)), region_plan.get("environment", {}))
    if planned_map.is_empty():
        integration_error.emit("cpu_map_generation_failed")
        return false
    var instance: Node3D = Geometry.new()
    if not instance.configure(current_run_id, "CPU", planned_map):
        instance.queue_free()
        integration_error.emit("cpu_map_geometry_failed")
        return false
    _remove_active_level()
    current_mode = "cpu"
    hardware = instance
    board = null
    active_level = hardware
    add_child(hardware)
    cpu_session = ObjectiveSession.new()
    cpu_session.name = "CPUObjectiveSession"
    add_child(cpu_session)
    if not cpu_session.configure(current_run_id, region_plan, world_state, hardware):
        integration_error.emit("cpu_objective_setup_failed")
        return false
    hardware.interaction_requested.connect(cpu_session.on_world_interaction)
    cpu_session.enemy_spawn_requested.connect(combat.spawn_hostile)
    cpu_session.extraction_interaction_requested.connect(_on_cpu_exit_interacted)
    player.global_position = hardware.spawn_world() + Vector3.UP * 0.5
    camera.world_bounds = planned_map["world_bounds"]
    camera.global_position = Vector3(player.position.x, 14, player.position.z)
    combat.begin_encounter(player, camera, planned_map, current_run_id)
    return true

func _on_enemy_defeated(actor_id: String, receipt_id: String) -> void:
    if current_mode == "motherboard" and board != null:
        board.confirm_intro_enemy_defeated(actor_id, receipt_id)
    elif current_mode == "cpu" and cpu_session != null:
        cpu_session.confirm_enemy_defeated(actor_id, receipt_id)
        if world_state.is_extraction_open("CPU"):
            cpu_session.acknowledge_cleansed()

func _on_world_fact(fact: Dictionary) -> void:
    if str(fact.get("type", "")) == "boss_defeated":
        world_fact_requested.emit(fact.duplicate(true))
        return
    var receipt: String = str(fact.get("receipt_id", ""))
    if receipt.is_empty():
        _freeze_with_error("empty_world_receipt")
        return
    pending_world_receipts[receipt] = fact.duplicate(true)
    world_fact_requested.emit(fact.duplicate(true))
    if progression_bridge != null:
        var ack: Dictionary = progression_bridge.handle_world_fact(fact)
        acknowledge_world_fact(receipt, ack)
    elif demo_mode:
        acknowledge_world_fact(receipt, {"ok": true})

func acknowledge_world_fact(receipt_id: String, ack: Dictionary) -> bool:
    if not pending_world_receipts.has(receipt_id):
        return false
    if not bool(ack.get("ok", false)):
        _freeze_with_error("E_world_fact_commit_failed:" + receipt_id)
        return false
    pending_world_receipts.erase(receipt_id)
    if pending_world_receipts.is_empty() and extraction_gate != null and not extraction_gate.pending.is_empty():
        _on_extraction_prepared(extraction_gate.pending)
    return true

func _on_cpu_exit_interacted(_fact: Dictionary) -> void:
    if current_mode != "cpu" or not world_state.is_extraction_open("CPU"):
        return
    extraction_gate.prepare("CPU")

func _on_extraction_prepared(fact: Dictionary) -> void:
    if not pending_world_receipts.is_empty():
        return
    if demo_mode:
        # Explicit non-durable playtest ACK; production requires E transaction.
        acknowledge_extraction({"ok": true, "receipt_id": str(fact["receipt_id"])})
        return
    if progression_bridge != null:
        acknowledge_extraction(progression_bridge.commit_terminal_fact(fact))
        return
    extraction_commit_requested.emit(fact.duplicate(true))

func acknowledge_extraction(ack: Dictionary) -> bool:
    if extraction_gate == null or not pending_world_receipts.is_empty():
        return false
    var finished: Dictionary = extraction_gate.finalize_after_durable_ack(ack)
    if finished.is_empty():
        _freeze_with_error("E_extraction_ack_missing_or_failed")
        return false
    mission_returned.emit(finished.duplicate(true))
    enter_hub()
    return true

func _on_player_died() -> void:
    if current_mode == "hub" or not pending_death.is_empty():
        return
    pending_death = {
        "schema": 1, "type": "mission_failed", "run_id": current_run_id,
        "receipt_id": "world:%s:death" % current_run_id, "outcome": "death"
    }
    player.movement_enabled = false
    if demo_mode:
        acknowledge_death({"ok": true, "receipt_id": str(pending_death["receipt_id"])})
    elif progression_bridge != null:
        acknowledge_death(progression_bridge.commit_terminal_fact(pending_death))
    else:
        death_commit_requested.emit(pending_death.duplicate(true))

func acknowledge_death(ack: Dictionary) -> bool:
    if pending_death.is_empty() or not bool(ack.get("ok", false)):
        _freeze_with_error("E_death_commit_failed")
        return false
    if str(ack.get("receipt_id", "")) != str(pending_death["receipt_id"]):
        _freeze_with_error("E_death_receipt_mismatch")
        return false
    var result: Dictionary = world_state.report_death()
    if result.is_empty():
        return false
    mission_returned.emit(result.duplicate(true))
    enter_hub()
    return true

func _remove_active_level() -> void:
    if active_level != null:
        remove_child(active_level)
        active_level.queue_free()
        active_level = null
    if cpu_session != null:
        remove_child(cpu_session)
        cpu_session.queue_free()
        cpu_session = null

func _freeze_with_error(reason: String) -> void:
    last_error = reason
    player.movement_enabled = false
    integration_error.emit(reason)
