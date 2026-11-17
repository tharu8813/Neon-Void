extends SceneTree
## Runs only with a synthetic, uniquely named rework slot. No legacy files touched.
const PilotScene = preload("res://scenes/rework/integration/rework_pilot.tscn")
const Gateway = preload("res://scripts/rework/persistence/progression_gateway.gd")
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")
var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func check(value: bool, name: String) -> void:
    if not value:
        failures += 1
        printerr("REWORK A PILOT FAIL: " + name)

func _clean(folder: String) -> void:
    var dir: DirAccess = DirAccess.open(folder)
    if dir == null:
        return
    dir.list_dir_begin()
    var filename: String = dir.get_next()
    while not filename.is_empty():
        if filename != "." and filename != "..":
            var fullpath: String = folder.path_join(filename)
            if dir.current_is_dir():
                _clean(fullpath)
            else:
                DirAccess.remove_absolute(fullpath)
        filename = dir.get_next()
    dir.list_dir_end()
    DirAccess.remove_absolute(folder)

func _run() -> void:
    var suffix: String = str(OS.get_process_id()) + "-" + str(Time.get_ticks_usec())
    var slot_id: String = "pilot-smoke-" + suffix
    var slot_path: String = Store.default_slot_path(slot_id)
    var pilot: Node3D = PilotScene.instantiate()
    pilot.set("slot_id", slot_id)
    pilot.set("initial_vaccine_id", "pilot-test-vaccine")
    root.add_child(pilot)
    await process_frame
    check(not bool(pilot.get("frozen")), "synthetic pilot initialized safely")
    var snapshot: Dictionary = Gateway.snapshot(slot_path)
    check(bool(snapshot.get("ok", false)), "new isolated slot persisted")
    var world: Node3D = pilot.get_node("World")
    check(not bool(world.get("demo_mode")), "production pilot explicitly disables fake receipts")
    check(str(world.get("current_mode")) == "hub", "starts in walkable hub")
    pilot.call("_on_mission_requested", {"seed": 42})
    snapshot = Gateway.snapshot(slot_path)
    check(bool(snapshot.get("ok", false)), "mission start persisted")
    if bool(snapshot.get("ok", false)):
        check(snapshot["snapshot"]["vaccine"]["mission"] != null,
            "E has an active run before D opens motherboard")
    check(str(world.get("current_mode")) == "motherboard",
        "D entered motherboard only after E commit")
    for index in range(13):
        pilot.call("_on_enemy_defeated", "smoke-enemy-" + str(index),
            "smoke-kill-" + str(index))
    snapshot = Gateway.snapshot(slot_path)
    check(bool(snapshot.get("ok", false))
        and int(snapshot["snapshot"]["vaccine"]["mission"]["pending_plugin_offers"]) == 1,
        "thirteen enemy receipts generate one durable level-up offer")
    var popup: Node = pilot.get("offer_panel") as Node
    check(popup != null and popup.visible, "C three-card selection shown")
    check(paused, "gameplay paused for reward selection")
    pilot.call("_choose_card", 0)
    check(not bool(pilot.get("frozen")), "C prepare/E durable/C confirm completed")
    check(not paused, "gameplay resumes only after durable plugin commit")
    snapshot = Gateway.snapshot(slot_path)
    check(bool(snapshot.get("ok", false)), "E committed chosen plugin")
    if bool(snapshot.get("ok", false)):
        var mission: Dictionary = snapshot["snapshot"]["vaccine"]["mission"]
        check(int(mission["pending_plugin_offers"]) == 0,
            "plugin claim consumed precisely one pending offer")
        check(mission["obtained_plugins"].size() == 1,
            "plugin saved in E before returning to gameplay")
    check(not bool(pilot.get("frozen")), "no safety halt")
    paused = false
    root.remove_child(pilot)
    pilot.queue_free()
    await process_frame
    _clean(slot_path)
    if failures == 0:
        print("REWORK A PILOT PASS: opt-in hub, durable mission, 13 B-style kill receipts, C/E reward, safe UI")
    quit(1 if failures > 0 else 0)
