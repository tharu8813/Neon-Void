extends SceneTree
## Run: godot --headless --path . --script res://tests/rework/world/world_foundation_smoke.gd
const Catalog = preload("res://scripts/rework/world/hardware_catalog.gd")
const Planner = preload("res://scripts/rework/world/mission_planner.gd")
const Maps = preload("res://scripts/rework/world/procedural_map.gd")
const Extraction = preload("res://scripts/rework/world/extraction_state.gd")
const Preview = preload("res://scenes/rework/world/procedural_preview.tscn")
var failures: int = 0

func check(ok: bool, message: String) -> void:
    if not ok:
        failures += 1
        printerr("REWORK WORLD FAIL: ", message)

func _initialize() -> void:
    call_deferred("_run")

func _run() -> void:
    check(Catalog.IDS.size() == 5, "exactly five hardware entries")
    check(not Catalog.has_hardware("MEMORY"), "legacy sectors not reused")
    var distinct_ids: Dictionary = {}
    for id in Catalog.IDS:
        check(not distinct_ids.has(id), "unique hardware ID " + id)
        distinct_ids[id] = true
        check(Catalog.objectives(id).size() >= 1, "objective contract " + id)
    for seed in range(100):
        var mission: Dictionary = Planner.create_mission(seed, seed / 10)
        check(mission == Planner.create_mission(seed, seed / 10), "mission replay " + str(seed))
        check(Planner.count_easy(mission) >= 1, "minimum easy " + str(seed))
        check(mission["regions"].size() == 5, "all hardware planned")
        var repeat_with_history: Dictionary = Planner.create_mission(seed, 999)
        for id in Catalog.IDS:
            var region: Dictionary = mission["regions"][id]
            check(region["difficulty"] in Planner.DIFFICULTIES, "valid difficulty " + id)
            check(region["environment"]["id"] in Planner.ENVIRONMENTS, "valid environment " + id)
            check(region["environment"]["id"] == repeat_with_history["regions"][id]["environment"]["id"], "history does not alter mutation frequency")
            if region["environment"]["id"] == "mutation":
                check(int(region["environment"]["mutation_stage"]) >= 1 and int(region["environment"]["mutation_stage"]) <= 5, "mutation stage range")
            if seed > 6:
                continue
            var map_a: Dictionary = Maps.generate(id, region["map_seed"], region["environment"])
            var map_b: Dictionary = Maps.generate(id, region["map_seed"], region["environment"])
            check(map_a == map_b, "map replay %s seed %d" % [id, seed])
            check(Maps.all_floor_reachable(map_a), "connected floor " + id)
            var distances: PackedInt32Array = Maps.distance_field(map_a, map_a["spawn"])
            check(distances[map_a["boss"].y * map_a["width"] + map_a["boss"].x] >= 0, "boss accessible " + id)
            check(Maps.is_floor(map_a, map_a["extraction"]), "extraction accessible " + id)
            check(map_a["objectives"].size() == Catalog.objectives(id).size(), "objective count " + id)
            var seen_cells: Dictionary = {}
            for objective in map_a["objectives"]:
                var point: Vector2i = objective["cell"]
                check(Maps.is_floor(map_a, point), "reachable objective tile " + id)
                check(distances[point.y * map_a["width"] + point.x] >= 0, "objective accessible " + id)
                check(not seen_cells.has(point), "objectives use separate tiles " + id)
                seen_cells[point] = true
    var varied: Dictionary = Planner.create_mission(42)
    var normal: Dictionary = Maps.generate("CPU", varied["regions"]["CPU"]["map_seed"], {"id": "normal"})
    var spread: Dictionary = Maps.generate("CPU", varied["regions"]["CPU"]["map_seed"], {"id": "spread"})
    var initial: Dictionary = Maps.generate("CPU", varied["regions"]["CPU"]["map_seed"], {"id": "initial"})
    check(spread["width"] > normal["width"] and initial["width"] < normal["width"], "map size effects")
    var state = Extraction.new("run-42")
    var events: Array = []
    state.world_fact.connect(func(event: Dictionary) -> void: events.append(event))
    state.extraction_committed.connect(func(event: Dictionary) -> void: events.append(event))
    check(not state.is_extraction_open("CPU"), "locked before clear")
    check(state.request_extraction("CPU").is_empty(), "cannot extract before clear")
    check(not state.confirm_boss_defeated("CPU", "early-boss"), "boss requires objectives")
    var objectives: Array = Catalog.objectives("CPU")
    for i in range(objectives.size()):
        check(state.confirm_objective("CPU", objectives[i], "objective-%d" % i), "complete objective")
        check(not state.confirm_objective("CPU", objectives[i], "duplicate-%d" % i), "duplicate objective ignored")
    check(state.confirm_boss_defeated("CPU", "boss-CPU"), "boss cleared after objectives")
    check(not state.confirm_boss_defeated("CPU", "boss-CPU-2"), "boss once")
    check(state.is_extraction_open("CPU"), "CPU point unlocked")
    check(not state.is_extraction_open("GPU"), "uncleared point stays locked")
    check(state.request_extraction("GPU").is_empty(), "wrong extraction point cannot complete")
    var partial: Dictionary = state.request_extraction("CPU")
    check(partial.get("outcome", "") == "success" and int(partial.get("cleared_count", 0)) == 1, "partial extraction")
    check(not bool(partial.get("full_clear", true)), "partial not full")
    check(state.request_extraction("CPU").is_empty(), "no duplicate reward event")
    check(events.size() == objectives.size() + 3, "exactly once objective, boss, clear, extraction")
    var full = Extraction.new("run-full")
    for id in Catalog.IDS:
        for i in Catalog.objectives(id):
            check(full.confirm_objective(id, i, "full-%s-%s" % [id, i]), "full objective")
        check(full.confirm_boss_defeated(id, "full-boss-" + id), "full boss")
    var completed: Dictionary = full.request_extraction("HDD")
    check(completed.get("full_clear", false) and completed.get("cleared_count", 0) == 5, "full bonus flag")
    var dead = Extraction.new("dead-42")
    check(dead.report_death().get("outcome", "") == "death", "death fact")
    check(dead.report_death().is_empty(), "death only once")
    var scene: Node = Preview.instantiate()
    check(scene is Node3D, "independent preview scene available")
    root.add_child(scene)
    check(scene.get_node_or_null("TileContainer") != null, "procedural preview generated")
    scene.queue_free()
    if failures == 0:
        print("REWORK WORLD FOUNDATION PASS (5 hardware, 100 seeds, reachable maps, extraction)")
    quit(0 if failures == 0 else 1)
