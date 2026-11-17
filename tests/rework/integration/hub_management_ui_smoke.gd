extends SceneTree
## A/E hub UI regression: visible controls -> real E commands -> durable read-back.
## Uses a unique synthetic test slot only. Never accesses existing user saves.
const PilotScene = preload("res://scenes/rework/integration/rework_pilot.tscn")
const State = preload("res://scripts/rework/progression/state_model.gd")
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")
const Gateway = preload("res://scripts/rework/persistence/progression_gateway.gd")
const Balance = preload("res://scripts/rework/progression/balance_config.gd")
var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func check(ok: bool, message: String) -> void:
    if not ok:
        failures += 1
        printerr("REWORK A HUB UI FAIL: ", message)

func _clean_test_only(folder: String) -> void:
    if not folder.contains("hub-ui-smoke-"):
        check(false, "refuse non-fixture cleanup")
        return
    var directory: DirAccess = DirAccess.open(folder)
    if directory == null:
        return
    directory.list_dir_begin()
    var filename: String = directory.get_next()
    while not filename.is_empty():
        var fullpath: String = folder.path_join(filename)
        if directory.current_is_dir():
            _clean_test_only(fullpath)
        else:
            DirAccess.remove_absolute(fullpath)
        filename = directory.get_next()
    directory.list_dir_end()
    DirAccess.remove_absolute(folder)

func _state(path: String) -> Dictionary:
    var snap: Dictionary = Gateway.snapshot(path)
    check(bool(snap.get("ok", false)), "E authoritative snapshot readable")
    return snap

func _run() -> void:
    var id: String = "hub-ui-smoke-%d-%d" % [
        OS.get_process_id(), Time.get_ticks_usec()]
    var path: String = Store.default_slot_path(id)
    check(not DirAccess.dir_exists_absolute(path), "unique test slot initially absent")
    var seed: Dictionary = State.new_slot(id, "vaccine-hub-ui")
    seed["vaccine"]["earned_fragments"] = 500
    seed["vaccine"]["unspent_fragments"] = 500
    seed["vaccine"]["known_skills"] = ["skill_alpha", "skill_beta"]
    seed["vaccine"]["equipped_skills"] = ["skill_alpha"]
    seed["vaccine"]["archive"] = {
        "nv_c_move_speed": {"rarity": "common", "level": 2}
    }
    check(State.validate(seed), "synthetic E fixture is valid v1 save")
    var init: Dictionary = Store.initialize(path, seed)
    check(bool(init.get("ok", false)), "E synthetic slot initialized once")
    if not bool(init.get("ok", false)):
        _clean_test_only(path)
        quit(1)
        return
    var pilot: Node3D = PilotScene.instantiate()
    pilot.set("slot_id", id)
    pilot.set("initial_vaccine_id", "vaccine-hub-ui")
    root.add_child(pilot)
    await process_frame
    check(not bool(pilot.get("frozen")), "hub pilot loaded without mutation")
    var initial: Dictionary = _state(path)
    var first_revision: int = int(initial.get("revision", -1))
    var panel: PanelContainer = pilot.get("hub_panel") as PanelContainer
    var hud: PanelContainer = pilot.get("tactical_hud") as PanelContainer
    check(panel != null and hud != null, "hub panel and verified tactical HUD mounted")
    pilot.call("_toggle_management")
    check(panel.visible and paused and bool(panel.get("can_edit")),
        "Tab management pauses game and allows editing in hub")
    var visible_tabs: TabContainer = panel.get("tabs") as TabContainer
    check(visible_tabs != null and visible_tabs.get_tab_count() == 4,
        "four-tab management UI is built")
    var draft: Dictionary = panel.get("allocation_draft")
    check(draft.is_empty() and int(initial["snapshot"]["vaccine"]["unspent_fragments"]) == 500,
        "stat draft begins from E, not fabricated UI points")
    panel.call("_change_stat", "attack_damage", 2)
    draft = panel.get("allocation_draft")
    check(int(draft.get("attack_damage", 0)) == 2,
        "two points staged without altering E yet")
    check(int(_state(path).get("revision", -1)) == first_revision,
        "staging a stat draft cannot mutate E")
    panel.call("_request_stats")
    var invested: Dictionary = _state(path)
    var v: Dictionary = invested["snapshot"]["vaccine"]
    check(int(v["invested_stats"].get("attack_damage", 0)) == 2
        and int(v["unspent_fragments"]) == 300
        and not bool(pilot.get("frozen")),
        "E commits +2 AD investments at 2P and leaves exactly 3P")
    var rev: int = int(invested.get("revision", 0))
    pilot.call("_on_hub_allocate", {"max_hp": 999}, rev - 1)
    check(int(_state(path).get("revision", -1)) == rev,
        "stale/overbudget UI request cannot advance E revision")
    panel.call("_request_move", "withdraw_plugin", "nv_c_move_speed")
    var withdrawn: Dictionary = _state(path)
    v = withdrawn["snapshot"]["vaccine"]
    check(v["carried"].has("nv_c_move_speed")
        and not v["archive"].has("nv_c_move_speed")
        and int(v["carried"]["nv_c_move_speed"]["level"]) == 2,
        "DB withdrawal is one E transaction with level preserved")
    panel.call("_request_move", "archive_plugin", "nv_c_move_speed")
    var stored: Dictionary = _state(path)
    v = stored["snapshot"]["vaccine"]
    check(v["archive"].has("nv_c_move_speed") and v["carried"].is_empty(),
        "single plugin returned to protected DB via E")
    panel.call("_select_skill", 2, 0)
    panel.call("_select_skill", 1, 1)
    panel.call("_select_skill", 0, 2)
    check(panel.get("selected_skills") == ["skill_beta", "skill_alpha", ""],
        "slot selector maintains correct B/E skill IDs")
    panel.call("_request_skills")
    var equipped: Dictionary = _state(path)
    v = equipped["snapshot"]["vaccine"]
    check(v["equipped_skills"] == ["skill_beta", "skill_alpha"],
        "three-slot E save mismatch: " + str(v["equipped_skills"]) + " / local=" +
            str(panel.get("selected_skills")) + " / message=" +
            str(pilot.get("message_label").text) + " / frozen=" + str(pilot.get("frozen")))
    var rev_equipped: int = int(equipped["revision"])
    panel.call("_select_skill", 1, 0)
    panel.call("_select_skill", 1, 1)
    panel.call("_request_skills")
    check(int(_state(path).get("revision", -1)) == rev_equipped,
        "duplicate skill slots rejected before E request")
    # All draft changes can be reset FREE; zero allocation is an explicit E ACK.
    panel.call("_change_stat", "attack_damage", -1)
    panel.call("_change_stat", "attack_damage", -1)
    panel.call("_request_stats")
    var reset: Dictionary = _state(path)
    v = reset["snapshot"]["vaccine"]
    check(v["invested_stats"].is_empty() and int(v["unspent_fragments"]) == 500,
        "free reallocation returns exactly 5P with no duplication")
    pilot.call("_close_management")
    check(not panel.visible and not paused, "close unpauses only normal gameplay")
    pilot.call("_on_mission_requested", {"seed": 42})
    var mission: Dictionary = _state(path)
    check(mission["snapshot"]["vaccine"]["mission"] != null,
        "a saved real mission exists before verifying read-only management")
    pilot.call("_toggle_management")
    check(panel.visible and not bool(panel.get("can_edit")),
        "in-mission management panel is read-only")
    var prior: int = int(mission["revision"])
    pilot.call("_on_hub_equip_skills", ["skill_alpha"], prior)
    pilot.call("_on_hub_plugin_move", "withdraw_plugin", "nv_c_move_speed", prior)
    pilot.call("_on_hub_allocate", {"max_hp": 1}, prior)
    check(int(_state(path).get("revision", -1)) == prior,
        "mid-mission direct UI action cannot alter E or bypass permissions")
    pilot.call("_halt", "synthetic_E_uncertainty")
    check(bool(pilot.get("frozen")) and paused and not panel.visible,
        "E incident safety stop closes edit UI and cannot resume mission")
    paused = false
    root.remove_child(pilot)
    pilot.queue_free()
    await process_frame
    _clean_test_only(path)
    if failures == 0:
        print("REWORK A HUB UI PASS: stat draft/free respec, E DB transfers, skill slots, revision guards, mission read-only, safe halt")
    quit(0 if failures == 0 else 1)
