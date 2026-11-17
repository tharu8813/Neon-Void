extends SceneTree
## Real Godot scene + B damage math + CPU objective/extraction lifecycle, no user saves.
## Run: godot --headless --path . --script res://tests/rework/world/cpu_vertical_slice_smoke.gd
const Scene = preload("res://scenes/rework/world/cpu_vertical_slice.tscn")
const Board = preload("res://scripts/rework/world/motherboard_layout.gd")
const Hub = preload("res://scripts/rework/world/hub_layout.gd")
const Maps = preload("res://scripts/rework/world/procedural_map.gd")
var failures: int = 0

func check(condition: bool, message: String) -> void:
    if not condition:
        failures += 1
        printerr("CPU VERTICAL SLICE FAIL: ", message)

func _initialize() -> void:
    call_deferred("_run")

func _hit_to_death(world: Variant, enemy_id: String, max_hits: int = 40) -> bool:
    for i in range(max_hits):
        if not world.combat.enemies.has(enemy_id):
            return true
        world.combat._apply_hit(enemy_id, "smoke:%s:%d" % [enemy_id, i])
    return not world.combat.enemies.has(enemy_id)

func _run() -> void:
    var game: Variant = Scene.instantiate()
    root.add_child(game)
    check(game.current_mode == "hub", "standalone boots in on-foot Defender Hub")
    check(game.active_level.get_node_or_null("WorldGeometry/HubFloorCollider") != null,
        "hub has physical collision floor")
    check(Hub.TERMINALS.has("status"), "vaccine status terminal present")
    check(game.active_level.request_named_terminal("status", Hub.TERMINALS["status"]["position"]),
        "status terminal reachable with real proximity check")
    check(game.demo_mode, "fixture uses explicitly non-persistent preview")
    check(game.begin_preview_mission(42), "preview mission launches after dispatch")
    check(game.current_mode == "motherboard", "board scene entered")
    check(not game.board.board.gates_unlocked(), "gates locked until intro combat")
    check(game.combat.enemies.size() == 3, "B-derived damage targets spawned on motherboard")
    check(game.board.request_gate_near_player(Board.GATES["CPU"]["position"]).is_empty(),
        "CPU inaccessible before B kills")
    for intro in Board.INTRO:
        check(_hit_to_death(game, str(intro["id"])), "intro enemy killed with B damage " + str(intro["id"]))
    check(game.board.board.gates_unlocked(), "three real damage-confirmed defeats unlock gates")
    check(game.board.board.intro_remaining() == 0, "no intro enemies remaining")
    var region_plan: Dictionary = game.mission["regions"]["CPU"]
    var expected_map: Dictionary = Maps.generate("CPU", int(region_plan["map_seed"]), region_plan["environment"])
    game.player.global_position = Board.GATES["CPU"]["position"] + Vector3.UP * 0.5
    var gate: Dictionary = game.interact()
    check(gate.get("region_id", "") == "CPU", "CPU gate dispatch")
    check(game.current_mode == "cpu", "CPU gameplay scene entered")
    check(game.hardware != null and game.cpu_session != null, "CPU scene/controller loaded")
    check(game.hardware.map_plan == expected_map, "live CPU map matches deterministic seed")
    check(Maps.all_floor_reachable(game.hardware.map_plan), "CPU is fully reachable")
    check(game.hardware.get_node_or_null("Geometry/WallCollisions") != null,
        "walkable 3D map has physical wall collision")
    check(game.hardware.get_node_or_null("Geometry/FloorCollider") != null,
        "walkable 3D map has physical floor collision")
    check(not game.world_state.is_extraction_open("CPU"), "extraction locked on arrival")
    var core: Vector3 = game.hardware.objective_world("secure_core")
    var intrusion: Vector3 = game.hardware.objective_world("stop_intruders")
    check(core != intrusion, "objectives placed on separate tiles")
    game.player.global_position = game.hardware.extraction_world() + Vector3.UP * 0.5
    check(game.hardware.interact_at(game.player.global_position).is_empty(),
        "no early extraction at boss marker")
    game.player.global_position = core + Vector3.UP * 0.5
    check(game.interact().get("objective_id", "") == "secure_core",
        "CPU defense requires physical console interaction")
    for tick in range(2400):
        game.cpu_session.tick_player(core + Vector3.UP * 0.5)
    check(game.cpu_session.objectives.progress["secure_core"]["completed"],
        "core defense complete after verified 60Hz ticks")
    check(not game.world_state.is_extraction_open("CPU"),
        "defense alone cannot unlock boss/extraction")
    game.player.global_position = intrusion + Vector3.UP * 0.5
    check(game.interact().get("objective_id", "") == "stop_intruders",
        "intrusion console starts combat wave")
    check(game.combat.enemies.size() == 10, "ten distinct enemy spawn requests reached B damage bridge")
    for i in range(10):
        var enemy_id: String = "CPU-VIRUS-%02d" % i
        check(_hit_to_death(game, enemy_id), "CPU verified defeat " + enemy_id)
    check(game.cpu_session.objectives.all_objectives_completed(),
        "B kills and physical defense satisfy both CPU objectives")
    check(game.combat.enemies.has("CPU-BOSS"),
        "CPU boss spawns only after all objectives complete")
    check(not game.world_state.is_extraction_open("CPU"), "extraction locked while boss alive")
    check(_hit_to_death(game, "CPU-BOSS"), "B damage kills CPU boss")
    check(game.world_state.is_extraction_open("CPU"), "boss death triggers CPU cleansing")
    check(game.hardware.extraction_open, "cleaned CPU unlocks 3D exit marker")
    check(game.pending_world_receipts.is_empty(),
        "preview E ACK path does not leave uncommitted world facts")
    var return_events: Array = []
    game.mission_returned.connect(func(fact: Dictionary) -> void: return_events.append(fact))
    game.player.global_position = game.hardware.extraction_world() + Vector3.UP * 0.5
    check(game.interact().get("type", "") == "hardware_extraction_requested",
        "player must physically interact with unlocked exit")
    check(game.current_mode == "hub", "preview E ACK finalizes extraction and returns to hub")
    check(return_events.size() == 1, "exactly one extraction terminal fact")
    if return_events.size() == 1:
        check(int(return_events[0]["cleared_count"]) == 1
            and not bool(return_events[0]["full_clear"]), "partial one-region extraction receipt")
    check(not game.acknowledge_extraction({"ok": true, "receipt_id": "bad"}),
        "duplicate extraction ack rejected")
    check(game.begin_preview_mission(43), "next dispatch starts a new independent seeded mission")
    check(game.current_mode == "motherboard", "new dispatch returns to board")
    game.queue_free()
    if failures == 0:
        print("CPU VERTICAL SLICE PASS: hub -> real B combat intro -> CPU walkable maze -> two goals -> boss -> unlock -> ACK -> hub")
    quit(0 if failures == 0 else 1)
