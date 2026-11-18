extends SceneTree
## Godot headless playthrough, isolated preview mode only. Not E production ACK.
const Scene = preload("res://scenes/rework/world/cpu_vertical_slice.tscn")
const Board = preload("res://scripts/rework/world/motherboard_layout.gd")
const Maps = preload("res://scripts/rework/world/procedural_map.gd")
var failures: int = 0

func check(ok: bool, reason: String) -> void:
    if not ok:
        failures += 1
        printerr("RAM PLAYABLE FAIL: ", reason)

func _initialize() -> void:
    call_deferred("_run")

func _hit_preview(world: Variant, target: String) -> void:
    for n in range(50):
        if not world.combat.enemies.has(target):
            break
        world.combat._apply_hit(target, "ram-fixture:%s:%d" % [target, n])

func _run() -> void:
    var game: Variant = Scene.instantiate()
    root.add_child(game)
    check(game.begin_preview_mission(42), "start mission")
    for enemy in Board.INTRO:
        _hit_preview(game, str(enemy["id"]))
    check(game.board.board.gates_unlocked(), "intro complete")
    game.player.global_position = Board.GATES["RAM"]["position"] + Vector3.UP * 0.5
    check(game.interact().get("region_id", "") == "RAM", "RAM gate proximity")
    check(game.current_mode == "ram" and game.region_session != null,
        "actual RAM scene and session constructed")
    if game.current_mode != "ram":
        game.queue_free()
        quit(1)
        return
    var map: Dictionary = game.hardware.map_plan
    check(Maps.all_floor_reachable(map), "all RAM floor cells reachable")
    check(game.hardware.get_node_or_null("Geometry/WallCollisions") != null,
        "physical walls in Godot")
    check(not game.world_state.is_extraction_open("RAM"), "cannot extract before objectives")
    check(game.region_session.pads.size() == 3, "three physical checksum replicas")
    game.player.global_position = game.hardware.objective_world("purge_corruption") + Vector3.UP * 0.5
    for i in range(4):
        check(game.interact().get("objective_id", "") == "purge_corruption",
            "corrupted memory purge %d" % i)
    check(bool(game.region_session.objectives.progress["purge_corruption"]["completed"]),
        "four purge interactions complete")
    check(game.region_session.puzzle_holder.visible, "replica puzzle becomes visible")
    var initial_enemy_count: int = game.region_session.known_hostiles.size()
    check(initial_enemy_count >= 8, "environment-scaled actual B combat actors requested")
    var bad: int = (game.region_session.real_slot + 1) % 3
    game.player.global_position = game.region_session.pads[bad]["position"] + Vector3.UP * 0.5
    var failed: Dictionary = game.interact()
    check(failed.get("type", "") == "ram_checksum_tested" and not bool(failed.get("authentic", true)),
        "wrong copy rejected; not counted toward verification")
    check(game.combat.enemies.size() == initial_enemy_count + 1,
        "wrong checksum spawns an actual B-routed decoy")
    check(int(game.region_session.objectives.progress["verify_memory"]["current"]) == 0,
        "bad checksum cannot progress")
    for i in range(2):
        var right: int = game.region_session.real_slot
        game.player.global_position = game.region_session.pads[right]["position"] + Vector3.UP * 0.5
        var verified: Dictionary = game.interact()
        check(bool(verified.get("authentic", false)), "authentic checksum validated in 3D")
    check(bool(game.region_session.objectives.progress["verify_memory"]["completed"]),
        "two independent real checks complete")
    check(not game.combat.enemies.has("RAM-BOSS"), "boss waits for real combat kills")
    for actor in game.region_session.known_hostiles.keys():
        if str(actor) != "RAM-BOSS":
            _hit_preview(game, str(actor))
    check(game.combat.enemies.has("RAM-BOSS"), "memory_phantom requested after B enemy receipts")
    _hit_preview(game, "RAM-BOSS")
    check(game.world_state.is_extraction_open("RAM"), "boss clear opens RAM extraction")
    var events: Array = []
    game.mission_returned.connect(func(fact: Dictionary) -> void: events.append(fact))
    game.player.global_position = game.hardware.extraction_world() + Vector3.UP * 0.5
    check(game.interact().get("type", "") == "hardware_extraction_requested",
        "physical exit accepted only after clear")
    check(game.current_mode == "hub", "preview extraction returned to hub")
    check(events.size() == 1 and int(events[0].get("cleared_count", 0)) == 1,
        "partial one-region terminal fact")
    game.queue_free()
    if failures == 0:
        print("RAM PLAYABLE PREVIEW PASS: 3D checksum, deceptive replica, boss, extraction")
    quit(0 if failures == 0 else 1)
