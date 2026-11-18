extends SceneTree
## P0 Windows QA: a REAL CharacterBody3D must be able to WALK across each
## of the five hub console pads and F must open the correct A/E management
## tab, or dispatch exactly one E-authorized mission. No teleport-to-contact
## substitute and no mutations by merely inspecting a terminal.
const PilotScene = preload("res://scenes/rework/integration/rework_pilot.tscn")
const Layout = preload("res://scripts/rework/world/hub_layout.gd")
const Gateway = preload("res://scripts/rework/persistence/progression_gateway.gd")
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")
const EXPECTED_TABS := {
    "status": 0, "vaccine": 1, "database": 2, "skills": 3}
var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func check(ok: bool, description: String) -> void:
    if not ok:
        failures += 1
        printerr("REWORK A HUB F ACCESS FAIL: ", description)

func _clean_test_slot(path: String) -> void:
    if not path.contains("a-hub-terminal-"):
        check(false, "refused deletion outside A synthetic E fixture")
        return
    var directory: DirAccess = DirAccess.open(path)
    if directory == null:
        return
    directory.list_dir_begin()
    var entry: String = directory.get_next()
    while not entry.is_empty():
        var full: String = path.path_join(entry)
        if directory.current_is_dir():
            _clean_test_slot(full)
        else:
            DirAccess.remove_absolute(full)
        entry = directory.get_next()
    directory.list_dir_end()
    DirAccess.remove_absolute(path)

func _press_f(world: Node3D) -> void:
    var event := InputEventKey.new()
    event.pressed = true
    event.echo = false
    event.keycode = KEY_F
    # Hit D's exact production input dispatch, not a bypassed A UI callback.
    world._unhandled_input(event)

func _run() -> void:
    var slot_id: String = "a-hub-terminal-%d-%d" % [
        OS.get_process_id(), Time.get_ticks_usec()]
    var slot_path: String = Store.default_slot_path(slot_id)
    check(not DirAccess.dir_exists_absolute(slot_path), "fresh E isolated slot")
    var pilot: Node3D = PilotScene.instantiate()
    pilot.set("slot_id", slot_id)
    pilot.set("initial_vaccine_id", "a-hub-terminal-vaccine")
    root.add_child(pilot)
    await physics_frame
    var world: Node3D = pilot.get_node("World") as Node3D
    var player: CharacterBody3D = world.get("player") as CharacterBody3D
    var panel: PanelContainer = pilot.get("hub_panel") as PanelContainer
    var geometry: Node3D = world.get("active_level").get_node("WorldGeometry")
    check(not bool(pilot.get("frozen")) and str(world.get("current_mode")) == "hub",
        "real E-backed pilot loads walkable hub")
    check(geometry.get_node_or_null("HubFloorCollider") != null,
        "hub retains real floor collider")
    for id in Layout.TERMINALS:
        var terminal_id: String = str(id)
        var marker_name: String = terminal_id.capitalize() + "Terminal"
        var marker: MeshInstance3D = geometry.get_node_or_null(marker_name)
        check(marker != null and marker.mesh != null,
            terminal_id + " still has visible F interaction marker")
        check(geometry.get_node_or_null(terminal_id + "Collider") == null,
            terminal_id + " has NO blocking static terminal collider")
    var start_revision: int = int(Gateway.snapshot(slot_path).get("revision", -1))
    check(start_revision > 0, "A E slot persisted before world input")
    _press_f(world)
    check(not panel.visible and str(world.get("current_mode")) == "hub",
        "out of terminal range F cannot open UI or dispatch")
    for id in EXPECTED_TABS:
        var terminal_id: String = str(id)
        var center: Vector3 = Layout.TERMINALS[terminal_id]["position"]
        var desired: Vector3 = center + Vector3.UP * 0.5
        # Use physics movement from real spawn, including a 0.3m capsule.
        # The original 1.2m console box used to block this path.
        player.global_position = Layout.SPAWN + Vector3.UP * 0.5
        await physics_frame
        var collision: KinematicCollision3D = player.move_and_collide(
            desired - player.global_position)
        var horizontal: float = Vector2(
            player.global_position.x - center.x,
            player.global_position.z - center.z).length()
        check(collision == null and horizontal < 0.15,
            terminal_id + " reachable by real CharacterBody from spawn; distance=" +
            str(horizontal) + " collision=" + str(collision))
        if horizontal > 0.15:
            continue
        var before: int = int(Gateway.snapshot(slot_path).get("revision", -1))
        _press_f(world)
        check(panel.visible and paused and panel.tabs.current_tab == EXPECTED_TABS[id],
            terminal_id + " F opens correct A management tab (not just D event)")
        check(int(Gateway.snapshot(slot_path).get("revision", -1)) == before,
            terminal_id + " F inspection never grants E rewards or changes save")
        pilot.call("_close_management")
        check(not paused and not panel.visible,
            terminal_id + " panel closes and normal movement resumes")
    # This fifth F pad is different: it MUST issue just one actual E mission
    # transaction instead of opening management.
    var dispatch: Vector3 = Layout.TERMINALS["dispatch"]["position"]
    player.global_position = Layout.SPAWN + Vector3.UP * 0.5
    await physics_frame
    var dispatch_hit: KinematicCollision3D = player.move_and_collide(
        dispatch + Vector3.UP * 0.5 - player.global_position)
    check(dispatch_hit == null and player.global_position.distance_to(
        dispatch + Vector3.UP * 0.5) < 0.15,
        "DISPATCH F pad is reachable without collision")
    _press_f(world)
    var after: Dictionary = Gateway.snapshot(slot_path)
    var mission: Variant = after.get("snapshot", {}).get("vaccine", {}).get(
        "mission", null)
    check(str(world.get("current_mode")) == "motherboard"
        and not bool(pilot.get("frozen")) and mission is Dictionary
        and str(mission.get("run_id", "")) == str(world.get("current_run_id"))
        and not panel.visible,
        "DISPATCH F issues ONE E-backed real mission, no orphaned UI")
    paused = false
    root.remove_child(pilot)
    pilot.queue_free()
    await process_frame
    _clean_test_slot(slot_path)
    if failures == 0:
        print("REWORK A HUB F ACCESS PASS: 5 nonblocking console pads, real CharacterBody approach, 4 E UI tabs and 1 E mission dispatch")
    quit(0 if failures == 0 else 1)
