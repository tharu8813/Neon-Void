extends SceneTree
## Real Godot scene + B damage math + CPU objective/extraction lifecycle, no user saves.
## Run: godot --headless --path . --script res://tests/rework/world/cpu_vertical_slice_smoke.gd
const Scene = preload("res://scenes/rework/world/cpu_vertical_slice.tscn")
const FormalCases = preload("res://tests/rework/world/formal_cpu_combat_cases.gd")
const PilotScene = preload("res://scenes/rework/integration/rework_pilot.tscn")
const EGateway = preload("res://scripts/rework/persistence/progression_gateway.gd")
const EStore = preload("res://scripts/rework/persistence/rework_save_store.gd")
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

func _kill_formal_actor(world_combat: Variant, actor_id: String,
        player_node: Node3D, max_ticks: int = 220) -> bool:
    ## Never calls the D legacy _apply_hit fixture; every HP transition goes
    ## through B's 60Hz projectile collision and damage/event pipeline.
    if not world_combat.enemies.has(actor_id):
        return false
    for tick in range(max_ticks):
        if not world_combat.enemies.has(actor_id):
            return true
        var target: Vector3 = world_combat.enemies[actor_id]["node"].global_position
        var commands: Dictionary = {
            "VACCINE": {"move": Vector2.ZERO, "aim_world": target, "fire": true}
        }
        for hostile_id in world_combat.enemies.keys():
            commands[str(hostile_id)] = {"move": Vector2.ZERO,
                "aim_world": player_node.global_position, "fire": false}
        world_combat.live.advance_test_tick(commands)
    return not world_combat.enemies.has(actor_id)

func _delete_only_fixture(folder: String) -> void:
    var directory: DirAccess = DirAccess.open(folder)
    if directory == null:
        return
    directory.list_dir_begin()
    var filename: String = directory.get_next()
    while not filename.is_empty():
        if filename != "." and filename != "..":
            var path: String = folder.path_join(filename)
            if directory.current_is_dir():
                _delete_only_fixture(path)
            else:
                DirAccess.remove_absolute(path)
        filename = directory.get_next()
    directory.list_dir_end()
    DirAccess.remove_absolute(folder)

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
    # Same registered smoke is executed by A's 13-script rework Linux/Windows CI.
    # This exercises B production Runtime without D's _apply_hit demo helper.
    var suite: Variant = FormalCases.new()
    for problem in suite.execute(root):
        check(false, "B formal CPU: " + str(problem))

    # Live B -> A's actual opt-in saved pilot -> E receipt/XP. Unique synthetic
    # slot only; no test-only world approval, no direct B HP mutation.
    var fixture_id: String = "d-b-e-contract-%d-%d" % [
        OS.get_process_id(), Time.get_ticks_usec()]
    var fixture_path: String = EStore.default_slot_path(fixture_id)
    check(not DirAccess.dir_exists_absolute(fixture_path),
        "unique E test slot does not pre-exist")
    var pilot: Node3D = PilotScene.instantiate()
    pilot.set("slot_id", fixture_id)
    pilot.set("initial_vaccine_id", "d-b-e-fixture")
    root.add_child(pilot)
    await process_frame
    check(not bool(pilot.get("frozen")), "A opt-in persisted pilot safely initialized")
    var saved: Dictionary = EGateway.snapshot(fixture_path)
    check(bool(saved.get("ok", false)), "E created isolated durable slot")
    var persisted_world: Node3D = pilot.get_node("World")
    check(not bool(persisted_world.get("demo_mode")),
        "A uses persisted D world, never demo approval")
    pilot.call("_on_mission_requested", {"seed": 42})
    saved = EGateway.snapshot(fixture_path)
    check(bool(saved.get("ok", false)) and
        saved.get("snapshot", {}).get("vaccine", {}).get("mission") != null,
        "E committed mission start before B runtime was activated")
    var production: Variant = persisted_world.get("combat")
    check(production.mode == "formal", "A pilot selects B authoritative combat")
    check(production.live.bridge.simulation.actors.has("VACCINE"),
        "B runtime owns actual player HP/status/mana")
    check(production.enemies.has("intro-0"),
        "B runtime owns initial motherboard enemies")
    var initial_revision: int = int(saved.get("revision", -1))
    if production.enemies.has("intro-0"):
        var target_world: Vector3 = production.enemies["intro-0"]["node"].global_position
        for tick in range(240):
            if not production.enemies.has("intro-0"):
                break
            var inputs: Dictionary = {
                "VACCINE": {
                    "move": Vector2.ZERO, "aim_world": target_world,
                    "fire": true},
                "intro-0": {"move": Vector2.ZERO,
                    "aim_world": persisted_world.player.global_position, "fire": false},
                "intro-1": {"move": Vector2.ZERO,
                    "aim_world": persisted_world.player.global_position, "fire": false},
                "intro-2": {"move": Vector2.ZERO,
                    "aim_world": persisted_world.player.global_position, "fire": false}
            }
            production.live.advance_test_tick(inputs)
    check(not production.enemies.has("intro-0"),
        "actual B projectile death reached D world and A pilot")
    saved = EGateway.snapshot(fixture_path)
    check(bool(saved.get("ok", false)) and int(saved.get("revision", -1)) >
        initial_revision, "B kill fact triggered durable E XP transaction")
    var saved_xp: int = -1
    if bool(saved.get("ok", false)):
        var mission_snapshot: Variant = saved["snapshot"]["vaccine"]["mission"]
        if mission_snapshot is Dictionary:
            saved_xp = int(mission_snapshot.get("mission_xp", -1))
    check(saved_xp == 8, "exactly one B intro kill credits exactly 8 XP")
    for tick in range(5):
        production.live.advance_test_tick({
            "VACCINE": {"move": Vector2.ZERO, "fire": false}})
    var saved_again: Dictionary = EGateway.snapshot(fixture_path)
    if bool(saved_again.get("ok", false)):
        check(int(saved_again["snapshot"]["vaccine"]["mission"]["mission_xp"]) ==
            saved_xp, "B death is not replayed into E reward ledger")
    # Complete the actual B -> D CPU objectives -> E clean/extract path, with
    # a test-only high-AD B actor to avoid 8-12 minute balance duration in CI.
    # It changes fixture B statistics, NEVER the game combat/damage formula.
    var b_actor_stats: Dictionary = production.live.bridge.simulation.actors["VACCINE"]["stats"]
    b_actor_stats["attack_damage"] = 150.0
    b_actor_stats["attack_speed"] = 20.0
    for actor_id in ["intro-1", "intro-2"]:
        check(_kill_formal_actor(production, actor_id, persisted_world.player),
            "formal B intro enemy death " + actor_id)
    check(persisted_world.board.board.gates_unlocked(),
        "actual B deaths unlock motherboard gates")
    persisted_world.player.global_position = Board.GATES["CPU"]["position"] + Vector3.UP * 0.5
    check(persisted_world.interact().get("region_id", "") == "CPU",
        "A persisted pilot enters CPU through physical gate")
    check(str(persisted_world.get("current_mode")) == "cpu",
        "B formal runtime recreated for CPU encounter")
    var core_position: Vector3 = persisted_world.hardware.objective_world("secure_core")
    var intrusion_position: Vector3 = persisted_world.hardware.objective_world("stop_intruders")
    persisted_world.player.global_position = core_position + Vector3.UP * 0.5
    check(persisted_world.interact().get("objective_id", "") == "secure_core",
        "formal CPU core objective physically activated")
    for tick in range(2400):
        persisted_world.cpu_session.tick_player(core_position)
    check(bool(persisted_world.world_state.progress["CPU"]["objectives"]["secure_core"]),
        "E-bound CPU defense objective completed after actual ticks")
    check(not persisted_world.world_state.is_extraction_open("CPU"),
        "boss and extraction still locked")
    persisted_world.player.global_position = intrusion_position + Vector3.UP * 0.5
    check(persisted_world.interact().get("objective_id", "") == "stop_intruders",
        "formal CPU invasion objective physically activated")
    check(production.enemies.size() == 10,
        "ten CPU virus actors registered in real B encounter")
    # Reconfigure this test actor after the CPU scene registered a new B run.
    b_actor_stats = production.live.bridge.simulation.actors["VACCINE"]["stats"]
    b_actor_stats["attack_damage"] = 150.0
    b_actor_stats["attack_speed"] = 20.0
    for index in range(10):
        var actor_id: String = "CPU-VIRUS-%02d" % index
        check(_kill_formal_actor(production, actor_id, persisted_world.player),
            "formal CPU B projectile verified death " + actor_id)
        var offer_panel: Control = pilot.get("offer_panel") as Control
        if offer_panel != null and offer_panel.visible:
            pilot.call("_choose_card", 0)
            check(not bool(pilot.get("frozen")),
                "A C/E plugin reward committed after real B kill")
    check(persisted_world.cpu_session.objectives.all_objectives_completed(),
        "both CPU objectives verified by real ticks and B kills")
    check(production.enemies.has("CPU-BOSS"), "B boss spawned after all objectives")
    check(not persisted_world.world_state.is_extraction_open("CPU"),
        "CPU cannot extract while real boss alive")
    check(_kill_formal_actor(production, "CPU-BOSS", persisted_world.player, 360),
        "B authoritative boss death (no synthetic _apply_hit)")
    check(persisted_world.world_state.is_extraction_open("CPU"),
        "real B boss death marks CPU clean")
    check(persisted_world.hardware.extraction_open, "CPU extraction physically opens")
    saved = EGateway.snapshot(fixture_path)
    check(bool(saved.get("ok", false)) and
        saved["snapshot"]["vaccine"]["mission"]["hardware_cleaned_ids"].has("CPU"),
        "E persists D CPU cleanse emitted from B boss death")
    persisted_world.player.global_position = persisted_world.hardware.extraction_world() + Vector3.UP * 0.5
    check(persisted_world.interact().get("type", "") == "hardware_extraction_requested",
        "real clean-world extraction requires on-foot interaction")
    var learn_panel: Control = pilot.get("learn_panel") as Control
    check(learn_panel.visible and paused,
        "A presents durable 0-2 learning selection before extraction")
    pilot.call("_finish_extraction")
    saved = EGateway.snapshot(fixture_path)
    check(bool(saved.get("ok", false)) and not bool(pilot.get("frozen")),
        "real CPU extraction E transaction succeeded")
    if bool(saved.get("ok", false)):
        check(saved["snapshot"]["vaccine"]["mission"] == null,
            "E closes real B-derived CPU mission only once")
        check(str(persisted_world.get("current_mode")) == "hub",
            "A E-acknowledged CPU extraction returns to real hub")
    paused = false
    root.remove_child(pilot)
    pilot.queue_free()
    await process_frame
    _delete_only_fixture(fixture_path)
    if failures == 0:
        print("CPU VERTICAL SLICE PASS: hub -> real B combat intro -> CPU walkable maze -> two goals -> boss -> unlock -> ACK -> hub")
    quit(0 if failures == 0 else 1)
