extends SceneTree
## HDD independent playable 3D preview test; no persistent save writes.
const Scene = preload("res://scenes/rework/world/cpu_vertical_slice.tscn")
const Board = preload("res://scripts/rework/world/motherboard_layout.gd")
const Maps = preload("res://scripts/rework/world/procedural_map.gd")
var failures: int = 0

func check(ok: bool, name: String) -> void:
    if not ok:
        failures += 1
        printerr("HDD PLAYABLE FAIL: ", name)

func _initialize() -> void:
    call_deferred("_run")

func _hit_preview(game: Variant, id: String) -> void:
    for n in range(50):
        if not game.combat.enemies.has(id):
            break
        game.combat._apply_hit(id, "hdd-fixture:%s:%d" % [id, n])

func _run() -> void:
    var game: Variant = Scene.instantiate()
    root.add_child(game)
    check(game.begin_preview_mission(44), "HDD seeded mission")
    for intro in Board.INTRO:
        _hit_preview(game, str(intro["id"]))
    game.player.global_position = Board.GATES["HDD"]["position"] + Vector3.UP * 0.5
    check(game.interact().get("region_id", "") == "HDD", "HDD physical motherboard gate")
    check(game.current_mode == "hdd" and game.region_session != null,
        "HDD map 3D geometry and storage controller")
    if game.current_mode != "hdd":
        game.queue_free()
        quit(1)
        return
    var plan: Dictionary = game.hardware.map_plan
    check(Maps.all_floor_reachable(plan), "HDD procedural reachability")
    check(game.hardware.get_node_or_null("Geometry/WallCollisions") != null, "real wall physics")
    check(game.region_session.targets.size() == 7, "4 platter + 3 index stations")
    check(not game.world_state.is_extraction_open("HDD"), "no pre-clear extraction")
    var positions: Dictionary = {}
    for i in range(7):
        var item: Dictionary = game.region_session.targets[i]
        check(Maps.is_floor(plan, item["cell"]), "all targets on walkable cells")
        check(not positions.has(item["cell"]), "all repair stations have distinct cells")
        positions[item["cell"]] = true
        game.player.global_position = item["position"] + Vector3.UP * 0.5
        var action: Dictionary = game.interact()
        check(action.get("type", "") == "storage_node_activated",
            "actual F interaction on HDD station %d" % i)
        if i == 3:
            check(bool(game.region_session.objectives.progress["repair_platter"]["completed"]),
                "four platter stations required before index")
    check(bool(game.region_session.objectives.progress["restore_index"]["completed"]),
        "three index restorations finished")
    check(game.combat.enemies.size() == 12, "12 B-routed disk defenders")
    check(not game.combat.enemies.has("HDD-BOSS"), "B boss waits for defenders")
    for i in range(12):
        _hit_preview(game, "HDD-HOSTILE-%02d" % i)
    check(game.combat.enemies.has("HDD-BOSS"), "disk_leviathan boss B actor")
    _hit_preview(game, "HDD-BOSS")
    check(game.world_state.is_extraction_open("HDD"), "HDD hardware cleared by boss receipt")
    var events: Array = []
    game.mission_returned.connect(func(fact: Dictionary) -> void: events.append(fact))
    game.player.global_position = game.hardware.extraction_world() + Vector3.UP * 0.5
    check(game.interact().get("type", "") == "hardware_extraction_requested",
        "only cleared HDD offers F extraction")
    check(game.current_mode == "hub", "successful preview exits back to hub")
    check(events.size() == 1 and int(events[0].get("cleared_count", 0)) == 1,
        "single HDD partial extraction fact is one-shot")
    game.queue_free()
    if failures == 0:
        print("HDD PLAYABLE PREVIEW PASS: 7 remote stations, defenders, boss, extraction")
    quit(0 if failures == 0 else 1)
