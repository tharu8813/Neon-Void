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
    var combat_router: Variant = world.get("combat")
    check(str(combat_router.get("mode")) == "formal",
        "persisted pilot invokes formal B production combat")
    var live_facade: Variant = combat_router.get("live")
    check(live_facade != null and live_facade.get("bridge") != null,
        "A facade owns the B production CPU adapter")
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
    snapshot = Gateway.snapshot(slot_path)
    var checkpoint: Dictionary = snapshot["snapshot"]["vaccine"]["mission"]["environment_rolls"].get(
        "e_checkpoint_v1", {})
    check(checkpoint.has("active_offer") and checkpoint["active_offer"] != null
        and not str(checkpoint.get("rng_state_text", "")).is_empty(),
        "E checkpoints exact C cards and lossless RNG before display")
    var initial_card: Dictionary = checkpoint["active_offer"]["cards"][0].duplicate(true)
    pilot.call("_reroll_card", 1)
    snapshot = Gateway.snapshot(slot_path)
    checkpoint = snapshot["snapshot"]["vaccine"]["mission"]["environment_rolls"].get(
        "e_checkpoint_v1", {})
    check(not bool(pilot.get("frozen"))
        and int(checkpoint.get("rerolls_left", -1)) == 2,
        "single-card reroll is durably saved before UI update")
    check(checkpoint["active_offer"]["cards"][0] == initial_card,
        "reroll kept the other selected slot unchanged")
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
        checkpoint = mission["environment_rolls"].get("e_checkpoint_v1", {})
        check(checkpoint.get("active_offer") == null
            and int(checkpoint.get("rerolls_left", -1)) == 2,
            "E settles card checkpoint and keeps reroll expenditure")
        if not mission["obtained_plugins"].is_empty():
            var gained_id: String = str(mission["obtained_plugins"].keys()[0])
            var authorized: Dictionary = live_facade.get("bridge").effects.active_levels()
            check(int(authorized.get(gained_id, 0)) ==
                int(mission["obtained_plugins"][gained_id]["level"]),
                "B/C combat received plugin level only after E claim receipt")
    check(not bool(pilot.get("frozen")), "no safety halt")
    # Simulate D's confirmed objective facts after the independently tested B
    # live combat path. Here the goal is A's durable extraction/learning UI.
    var run_state: RefCounted = world.get("world_state") as RefCounted
    for objective_id in WorldCatalog.objectives("CPU"):
        check(bool(run_state.call("confirm_objective", "CPU", str(objective_id),
            "pilot:CPU:" + str(objective_id))), "D CPU objective -> E receipt")
    check(bool(run_state.call("confirm_boss_defeated", "CPU", "pilot:CPU:boss")),
        "D CPU cleanse gated by objectives")
    # The skill draft is deliberately deferred until after D's cleanse ACK;
    # give the overlay one idle frame, then answer it like a player.
    await process_frame
    var skill_panel: Control = pilot.get("skill_panel") as Control
    check(skill_panel != null and skill_panel.visible and paused,
        "A offers a draft skill only after E acknowledged hardware_cleansed")
    var draft: Array = pilot.get("skill_candidates")
    check(draft.size() == 3, "skill draft has three deterministic candidates")
    snapshot = Gateway.snapshot(slot_path)
    var known_before: int = snapshot["snapshot"]["vaccine"]["known_skills"].size()
    var chosen_skill: String = str(draft[0].get("id", "")) if draft.size() > 0 else ""
    pilot.call("_on_skill_chosen", chosen_skill)
    check(not bool(pilot.get("frozen")) and not paused,
        "skill choice commits to E and resumes only after B sync")
    snapshot = Gateway.snapshot(slot_path)
    check(bool(snapshot.get("ok", false))
        and snapshot["snapshot"]["vaccine"]["known_skills"].has(chosen_skill)
        and snapshot["snapshot"]["vaccine"]["known_skills"].size() == known_before + 1,
        "learned skill is durable in E before the overlay closes")
    var stored_slots: Dictionary = live_facade.get("bridge").skill_snapshot()
    var stored_ids: Array = []
    for entry in stored_slots.get("slots", []):
        stored_ids.append(str(entry.get("skill_id", "")))
    check(stored_ids.has(chosen_skill),
        "B encounter loadout replaced only after the fresh E revision")
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
    var selectable_count: int = checks.size()
    if selectable_count > 0:
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
        check(snapshot["snapshot"]["vaccine"]["carried"].size() == (1 if selectable_count > 0 else 0),
            "eligible selected plugin preserved in carried inventory")
    var result_panel: PanelContainer = pilot.get("result_panel") as PanelContainer
    check(result_panel != null and result_panel.visible and paused
        and result_panel.get("title_label").text.contains("성공"),
        "real E extraction opens verified success modal before resuming hub")
    var before_close: int = int(snapshot.get("revision", -1))
    result_panel.call("_dismiss")
    check(not result_panel.visible and not paused
        and int(Gateway.snapshot(slot_path).get("revision", -2)) == before_close,
        "closing result cannot replay E settlement or alter saved revision")
    if selectable_count > 0:
        pilot.call("_deposit_carried")
        snapshot = Gateway.snapshot(slot_path)
        check(bool(snapshot.get("ok", false))
            and snapshot["snapshot"]["vaccine"]["archive"].size() == 1,
            "manual hub deposit moves learned plugin into DB")
    pilot.call("_halt", "synthetic_ambiguous_E_commit")
    check(bool(pilot.get("frozen")) and paused,
        "ambiguous durable receipt freezes entire 60Hz B/D gameplay")
    paused = false
    root.remove_child(pilot)
    pilot.queue_free()
    await process_frame
    _clean(slot_path)
    if failures == 0:
        print("REWORK A PILOT PASS: B production CPU, E-checkpointed C cards/rerolls, durable learning/extract/archive")
    quit(1 if failures > 0 else 0)
