extends SceneTree
## Run: godot --headless --path . --script res://tests/rework/world/hub_motherboard_smoke.gd
const HubLayout = preload("res://scripts/rework/world/hub_layout.gd")
const Motherboard = preload("res://scripts/rework/world/motherboard_layout.gd")
const HubScene = preload("res://scenes/rework/world/defender_hub.tscn")
const BoardScene = preload("res://scenes/rework/world/motherboard.tscn")
const CPUScene = preload("res://scenes/rework/world/cpu_vertical_slice.tscn")
var failures: int = 0

func check(ok: bool, why: String) -> void:
    if not ok:
        failures += 1
        printerr("REWORK HUB/BOARD FAIL: ", why)

func _initialize() -> void:
    call_deferred("_run")

func _run() -> void:
    check(HubLayout.is_inside(HubLayout.SPAWN), "valid spawn")
    check(HubLayout.TERMINALS.size() >= 3, "required hub terminals")
    for id in HubLayout.TERMINALS:
        var position: Vector3 = HubLayout.TERMINALS[id]["position"]
        check(HubLayout.is_inside(position), "terminal inside hub " + id)
        var request: Dictionary = HubLayout.request_terminal(id, position)
        check(request.get("terminal_id", "") == id, "local terminal reachable " + id)
        check(HubLayout.request_terminal(id, HubLayout.SPAWN).is_empty(), "terminal proximity enforced " + id)
    check(HubLayout.nearest_terminal(Vector3(19, 0, 13))["action"] == "start_mission", "dispatch on foot")
    var hub: Node3D = HubScene.instantiate()
    root.add_child(hub)
    check(hub.get_node_or_null("WorldGeometry/HubFloor") != null, "hub 3D floor")
    check(hub.get_node_or_null("WorldGeometry/HubFloorCollider") != null, "hub collision floor")
    var requests: Array = []
    hub.terminal_requested.connect(func(event: Dictionary) -> void: requests.append(event))
    check(not hub.request_interaction(Vector3(-20, 0, -20)), "outside hub no remote requests")
    check(hub.request_named_terminal("dispatch", Vector3(19, 0, 13)), "dispatch terminal event")
    check(requests.size() == 1 and requests[0]["action"] == "start_mission", "dispatch only delegates")
    hub.queue_free()
    check(Motherboard.GATES.size() == 5 and Motherboard.INTRO.size() == 3, "five hardware and three intro enemies")
    var snapshot: Dictionary = {"regions": {}}
    for id in Motherboard.GATES:
        snapshot["regions"][id] = {"difficulty": "easy", "map_seed": 42}
    var board = Motherboard.new("run-hub-smoke", snapshot)
    var events: Array = []
    board.board_event.connect(func(event: Dictionary) -> void: events.append(event))
    check(not board.gate_available("CPU"), "intro locks gates")
    check(board.request_gate("CPU", Motherboard.GATES["CPU"]["position"]).is_empty(), "entry locked")
    check(not board.confirm_intro_defeat("wrong_enemy", "receipt-0"), "unknown enemy rejected")
    for enemy in Motherboard.INTRO:
        var id: String = enemy["id"]
        check(board.confirm_intro_defeat(id, "kill-" + id), "enemy death accepted once")
        check(not board.confirm_intro_defeat(id, "other-" + id), "duplicate death rejected")
    check(board.gates_unlocked(), "gates unlocked after intro")
    check(board.intro_remaining() == 0, "all kills accounted for")
    check(events.size() == 4, "three kills and one gate unlock fact")
    for id in Motherboard.GATES:
        check(board.gate_available(id), "free order " + id)
    check(board.request_gate("GPU", Vector3(0, 0, 0)).is_empty(), "no remote gate teleport")
    check(board.request_gate("RAM", Motherboard.GATES["RAM"]["position"]).get("region_id", "") == "RAM", "arbitrary first choice")
    check(not board.gate_available("CPU"), "cannot enter two hardware at once")
    check(board.return_to_board("RAM"), "return to motherboard")
    check(not board.return_to_board("RAM"), "duplicate return rejected")
    check(board.request_gate("CPU", Motherboard.GATES["CPU"]["position"]).get("region_id", "") == "CPU", "next hardware in any order")
    var board_scene: Node3D = BoardScene.instantiate()
    root.add_child(board_scene)
    check(board_scene.get_node_or_null("BoardGeometry/MainboardFloor") != null, "3D motherboard floor")
    board_scene.setup_run("run-hub-smoke", snapshot)
    check(board_scene.request_gate_near_player(Motherboard.GATES["CPU"]["position"]).is_empty(), "scene starts locked")
    for enemy in Motherboard.INTRO:
        check(board_scene.confirm_intro_enemy_defeated(enemy["id"], "scene-"+str(enemy["id"])), "scene B integration hook")
    check(board_scene.request_gate_near_player(Motherboard.GATES["HDD"]["position"]).get("region_id", "") == "HDD", "scene gate dispatch")
    board_scene.queue_free()
    # Executed by A's existing Linux/Windows rework CI list, even before A
    # explicitly registers cpu_vertical_slice_smoke.gd as a separate job.
    var live: Variant = CPUScene.instantiate()
    root.add_child(live)
    check(live.current_mode == "hub", "CPU preview enters walkable hub")
    check(live.begin_preview_mission(42), "CPU dispatch creates valid seeded mission")
    check(not live.board.board.gates_unlocked(), "live board locked before B hits")
    for enemy in Motherboard.INTRO:
        var actor: String = str(enemy["id"])
        for hit in range(3):
            if live.combat.enemies.has(actor):
                live.combat._apply_hit(actor, "intro:%s:%d" % [actor, hit])
        check(not live.combat.enemies.has(actor), "B damage defeats intro actor " + actor)
    check(live.board.board.gates_unlocked(), "B combat unlocks all hardware")
    live.player.global_position = Motherboard.GATES["CPU"]["position"] + Vector3.UP * 0.5
    check(live.interact().get("region_id", "") == "CPU", "live CPU gate entered")
    check(live.current_mode == "cpu", "physical CPU map loaded")
    check(live.hardware.get_node_or_null("Geometry/WallCollisions") != null, "CPU wall collisions exist")
    check(not live.world_state.is_extraction_open("CPU"), "live extraction initially locked")
    live.player.global_position = live.hardware.objective_world("secure_core") + Vector3.UP * 0.5
    check(live.interact().get("objective_id", "") == "secure_core", "physical core activation")
    for i in range(2400):
        live.cpu_session.tick_player(live.player.global_position)
    check(live.cpu_session.objectives.progress["secure_core"]["completed"], "verified defense ticks")
    live.player.global_position = live.hardware.objective_world("stop_intruders") + Vector3.UP * 0.5
    check(live.interact().get("objective_id", "") == "stop_intruders", "physical intrusion activation")
    check(live.combat.enemies.size() == 10, "ten spawned CPU actors")
    for index in range(10):
        var actor: String = "CPU-VIRUS-%02d" % index
        for hit in range(3):
            if live.combat.enemies.has(actor):
                live.combat._apply_hit(actor, "cpu:%s:%d" % [actor, hit])
        check(not live.combat.enemies.has(actor), "B damage confirms CPU enemy " + actor)
    check(live.cpu_session.objectives.all_objectives_completed(), "two CPU objectives verified")
    check(live.combat.enemies.has("CPU-BOSS"), "boss gated by both goals")
    check(not live.world_state.is_extraction_open("CPU"), "exit stays locked before boss defeat")
    for hit in range(25):
        if live.combat.enemies.has("CPU-BOSS"):
            live.combat._apply_hit("CPU-BOSS", "boss:%d" % hit)
    check(live.world_state.is_extraction_open("CPU"), "B boss death clears CPU")
    check(live.hardware.extraction_open, "physical extraction unlocked")
    live.player.global_position = live.hardware.extraction_world() + Vector3.UP * 0.5
    check(live.interact().get("type", "") == "hardware_extraction_requested", "physical exit activated")
    check(live.current_mode == "hub", "demo ACK returns player to hub")
    live.queue_free()
    if failures == 0:
        print("REWORK HUB/MAINBOARD PASS (terminals, intro gate lock, arbitrary hardware order)")
    quit(0 if failures == 0 else 1)
