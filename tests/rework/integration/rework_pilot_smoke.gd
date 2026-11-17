extends SceneTree
## Runs only with a synthetic, uniquely named rework slot. No legacy files touched.
const PilotScene = preload("res://scenes/rework/integration/rework_pilot.tscn")
const Gateway = preload("res://scripts/rework/persistence/progression_gateway.gd")
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")
const WorldCatalog = preload("res://scripts/rework/world/hardware_catalog.gd")
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
    var popup: Control = pilot.get("offer_panel") as Control
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
    # Simulate D's confirmed objective facts after the independently tested B
    # live combat path. Here the goal is A's durable extraction/learning UI.
    var run_state: RefCounted = world.get("world_state") as RefCounted
    for objective_id in WorldCatalog.objectives("CPU"):
        check(bool(run_state.call("confirm_objective", "CPU", str(objective_id),
            "pilot:CPU:" + str(objective_id))), "D CPU objective -> E receipt")
    check(bool(run_state.call("confirm_boss_defeated", "CPU", "pilot:CPU:boss")),
        "D CPU cleanse gated by objectives")
    snapshot = Gateway.snapshot(slot_path)
    check(bool(snapshot.get("ok", false))
        and snapshot["snapshot"]["vaccine"]["mission"]["hardware_cleaned_ids"].has("CPU"),
        "E durable CPU clear before extraction")
    var extraction: RefCounted = world.get("extraction_gate") as RefCounted
    var prepared: Dictionary = extraction.call("prepare", "CPU")
    check(not prepared.is_empty(), "D prepares available CPU extraction")
    var learning_panel: Control = pilot.get("learn_panel") as Control
    check(learning_panel.visible and paused,
        "A prompts for 0-2 plugin learning before E one-shot extraction")
    var checks: Array = pilot.get("learn_checks")
    if checks.size() > 0:
        var first: CheckBox = checks[0]
        first.button_pressed = true
    pilot.call("_finish_extraction")
    snapshot = Gateway.snapshot(slot_path)
    check(bool(snapshot.get("ok", false)) and not bool(pilot.get("frozen")),
        "A extraction committed without reconciliation errors")
    if bool(snapshot.get("ok", false)):
        check(snapshot["snapshot"]["vaccine"]["mission"] == null,
            "E closed mission after durable extraction")
        check(str(world.get("current_mode")) == "hub",
            "D returns to hub only after E ACK")
        check(snapshot["snapshot"]["vaccine"]["carried"].size() == checks.size(),
            "eligible selected plugin preserved in carried inventory")
    if checks.size() > 0:
        pilot.call("_deposit_carried")
        snapshot = Gateway.snapshot(slot_path)
        check(bool(snapshot.get("ok", false))
            and snapshot["snapshot"]["vaccine"]["archive"].size() == 1,
            "manual hub deposit moves learned plugin into DB")
    paused = false
    root.remove_child(pilot)
    pilot.queue_free()
    await process_frame
    _clean(slot_path)
    if failures == 0:
        print("REWORK A PILOT PASS: durable hub/missions, C/E reward, D CPU facts, manual learning/extract/archive")
    quit(1 if failures > 0 else 0)
