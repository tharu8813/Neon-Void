extends SceneTree
## SSD preview gameplay flow: routed 3D captures, defended 30s 60Hz seal, B preview boss.
const Scene = preload("res://scenes/rework/world/cpu_vertical_slice.tscn")
const Board = preload("res://scripts/rework/world/motherboard_layout.gd")
const Maps = preload("res://scripts/rework/world/procedural_map.gd")
var failures: int = 0

func check(ok: bool, why: String) -> void:
    if not ok:
        failures += 1
        printerr("SSD PLAYABLE FAIL: ", why)

func _initialize() -> void:
    call_deferred("_run")

func hit_preview(game: Variant, target: String) -> void:
    for n in range(50):
        if not game.combat.enemies.has(target):
            break
        game.combat._apply_hit(target, "ssd-fixture:%s:%d" % [target, n])

func _run() -> void:
    var game: Variant = Scene.instantiate()
    root.add_child(game)
    check(game.begin_preview_mission(43), "mission started")
    for intro in Board.INTRO:
        hit_preview(game, str(intro["id"]))
    game.player.global_position = Board.GATES["SSD"]["position"] + Vector3.UP * 0.5
    check(game.interact().get("region_id", "") == "SSD", "SSD gate reached on foot")
    check(game.current_mode == "ssd" and game.region_session != null,
        "real SSD geometry and storage controller active")
    if game.current_mode != "ssd":
        game.queue_free()
        quit(1)
        return
    check(Maps.all_floor_reachable(game.hardware.map_plan), "connected SSD map")
    check(game.region_session.targets.size() == 3, "3 physical routed data nodes")
    check(not game.world_state.is_extraction_open("SSD"), "exit initially locked")
    for i in range(3):
        var target: Dictionary = game.region_session.targets[i]
        check(Maps.is_floor(game.hardware.map_plan, target["cell"]),
            "capture target on accessible cell")
        game.player.global_position = target["position"] + Vector3.UP * 0.5
        check(game.interact().get("type", "") == "storage_node_activated",
            "on-foot data capture " + str(i))
    check(bool(game.region_session.objectives.progress["capture_node"]["completed"]),
        "all capture receipts credited once")
    var expected_enemies: int = game.region_session.known_hostiles.size()
    check(game.combat.enemies.size() == expected_enemies and expected_enemies >= 10,
        "SSD stack-overflow-scaled defenders spawned")
    var seal: Vector3 = game.hardware.objective_world("seal_transfer")
    game.player.global_position = seal + Vector3.UP * 0.5
    check(game.interact().get("objective_id", "") == "seal_transfer",
        "data transfer terminal activated physically")
    for tick in range(1800):
        game.region_session.tick_player(seal + Vector3.UP * 0.5)
    check(bool(game.region_session.objectives.progress["seal_transfer"]["completed"]),
        "transfer needs full 1800 defended 60Hz ticks")
    check(not game.combat.enemies.has("SSD-BOSS"), "boss requires all B deaths")
    for i in range(expected_enemies):
        hit_preview(game, "SSD-HOSTILE-%02d" % i)
    check(game.combat.enemies.has("SSD-BOSS"), "flash_daemon is a B actor")
    hit_preview(game, "SSD-BOSS")
    check(game.world_state.is_extraction_open("SSD"), "cleanse after boss")
    game.player.global_position = game.hardware.spawn_world() + Vector3.UP * 0.5
    check(game.interact().get("type", "") == "hardware_board_return_requested",
        "separate board return without forced extraction")
    check(game.current_mode == "motherboard", "can freely select another zone")
    check(game.board.board.gates_unlocked(), "no intro replay")
    game.queue_free()
    if failures == 0:
        print("SSD PLAYABLE PREVIEW PASS: 3 nodes, 1800 ticks, boss, free board return")
    quit(0 if failures == 0 else 1)
