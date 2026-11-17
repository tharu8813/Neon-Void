extends SceneTree
## Run: godot --headless --path . --script res://tests/rework/world/hub_motherboard_smoke.gd
const HubLayout = preload("res://scripts/rework/world/hub_layout.gd")
const Motherboard = preload("res://scripts/rework/world/motherboard_layout.gd")
const HubScene = preload("res://scenes/rework/world/defender_hub.tscn")
const BoardScene = preload("res://scenes/rework/world/motherboard.tscn")
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
    if failures == 0:
        print("REWORK HUB/MAINBOARD PASS (terminals, intro gate lock, arbitrary hardware order)")
    quit(0 if failures == 0 else 1)
