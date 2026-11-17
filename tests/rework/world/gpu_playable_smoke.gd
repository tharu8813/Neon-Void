extends SceneTree
## Standalone GPU 3D on-foot preview; CPU smoke remains independently registered.
## Enemy HP in this preview is not a production B validation.
const Scene = preload("res://scenes/rework/world/cpu_vertical_slice.tscn")
const Board = preload("res://scripts/rework/world/motherboard_layout.gd")
const Maps = preload("res://scripts/rework/world/procedural_map.gd")
var failures: int = 0

func check(ok: bool, label: String) -> void:
    if not ok:
        failures += 1
        printerr("GPU PLAYABLE FAIL: ", label)

func _initialize() -> void:
    call_deferred("_run")

func _hit_preview(world: Variant, actor_id: String) -> void:
    for n in range(50):
        if not world.combat.enemies.has(actor_id):
            break
        world.combat._apply_hit(actor_id, "gpu-fixture:%s:%d" % [actor_id, n])

func _dodge_tile(plan: Dictionary, anchor: Vector3, horizontal: bool) -> Vector3:
    for y in range(1, int(plan["height"]) - 1):
        for x in range(1, int(plan["width"]) - 1):
            var cell := Vector2i(x, y)
            if not Maps.is_floor(plan, cell):
                continue
            var at: Vector3 = Maps.cell_to_world(cell)
            var offset: Vector2 = Vector2(at.x - anchor.x, at.z - anchor.z)
            if offset.length() <= 3.8 and absf(offset.y if horizontal else offset.x) > 0.65:
                return at + Vector3.UP * 0.5
    return Vector3.INF

func _run() -> void:
    var game: Variant = Scene.instantiate()
    root.add_child(game)
    check(game.begin_preview_mission(42), "start seeded preview")
    for item in Board.INTRO:
        _hit_preview(game, str(item["id"]))
    check(game.board.board.gates_unlocked(), "gates unlocked by real intro fixture")
    game.player.global_position = Board.GATES["GPU"]["position"] + Vector3.UP * 0.5
    var result: Dictionary = game.interact()
    check(result.get("region_id", "") == "GPU", "physical GPU gate requested")
    check(game.current_mode == "gpu" and game.region_session != null, "GPU 3D scene active")
    if game.current_mode != "gpu":
        game.queue_free()
        quit(1)
        return
    var plan: Dictionary = game.hardware.map_plan
    var region: Dictionary = game.mission["regions"]["GPU"]
    check(plan == Maps.generate("GPU", int(region["map_seed"]), region["environment"]),
        "identical seed gives identical GPU map")
    check(Maps.all_floor_reachable(plan), "all GPU floor cells reachable")
    check(game.hardware.get_node_or_null("Geometry/WallCollisions") != null,
        "physical walls constructed")
    check(not game.world_state.is_extraction_open("GPU"), "extraction locked at entry")
    var switch_at: Vector3 = game.hardware.objective_world("disable_emitter")
    game.player.global_position = switch_at + Vector3.UP * 0.5
    for index in range(3):
        check(game.interact().get("objective_id", "") == "disable_emitter",
            "physical emitter switch %d" % index)
    check(bool(game.region_session.objectives.progress["disable_emitter"]["completed"]),
        "three unique emitter controls completed")
    check(game.combat.enemies.size() == 8, "eight encounter enemies spawned")
    var coolant: Vector3 = game.hardware.objective_world("stabilize_coolant")
    game.player.global_position = coolant + Vector3.UP * 0.5
    check(game.interact().get("objective_id", "") == "stabilize_coolant",
        "coolant console activated")
    var near_horizontal: Vector3 = _dodge_tile(plan, coolant, true)
    var near_vertical: Vector3 = _dodge_tile(plan, coolant, false)
    check(near_horizontal.is_finite() and near_vertical.is_finite(),
        "seed map exposes both reachable dodge positions")
    if near_horizontal.is_finite() and near_vertical.is_finite():
        for trial in range(8):
            var tile: Vector3 = near_horizontal if trial % 2 == 0 else near_vertical
            for tick in range(90):
                game.region_session.tick_player(tile)
        check(bool(game.region_session.objectives.progress["stabilize_coolant"]["completed"]),
            "eight actual telegraphed dodge windows completed")
    check(not game.world_state.is_extraction_open("GPU"), "no extraction before boss")
    for i in range(8):
        _hit_preview(game, "GPU-OVERLOAD-%02d" % i)
    check(game.combat.enemies.has("GPU-BOSS"), "boss requires objectives and hostiles")
    _hit_preview(game, "GPU-BOSS")
    check(game.world_state.is_extraction_open("GPU"), "GPU boss receipt cleanses region")
    check(game.hardware.extraction_open, "cleaned GPU shows exit marker")
    game.player.global_position = game.hardware.spawn_world() + Vector3.UP * 0.5
    check(game.interact().get("type", "") == "hardware_board_return_requested",
        "cleansed GPU has separate return pad")
    check(game.current_mode == "motherboard", "return to free-choice motherboard")
    check(game.board.board.gates_unlocked(), "intro remains cleared; no replayed XP")
    check(game.combat.enemies.size() == 0, "no respawned intro enemies")
    check(game.world_state.is_extraction_open("GPU"), "GPU clear persists in active run")
    game.queue_free()
    if failures == 0:
        print("GPU PLAYABLE PREVIEW PASS: map, danger telegraph, objectives, B-preview actors, boss, board")
    quit(0 if failures == 0 else 1)
