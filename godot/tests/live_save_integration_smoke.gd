extends SceneTree
## Simulates C-approved wave completion and proves the actual A main UI/E store
## survives a restart, finishes all four zones and preserves exactly one module.
## Every file stays under a synthetic unique user://p11-live-test-* directory.
const Store = preload("res://scripts/persistence/save_store.gd")
const Bridge = preload("res://scripts/integration/save_bridge.gd")
var failures: int = 0
var slot: String = ""

func _initialize() -> void:
    create_timer(70.0).timeout.connect(func(): printerr("P11 LIVE SAVE TIMEOUT"); quit(1))
    call_deferred("_test")

func require(ok: bool, why: String) -> void:
    if not ok:
        failures += 1
        printerr("P11 LIVE SAVE FAIL: " + why)

func instance_game() -> Node:
    var scene: PackedScene = load("res://scenes/main.tscn")
    var game: Node = scene.instantiate()
    root.add_child(game)
    # Headless Main._ready disables user persistence by design. Enable ONLY
    # this generated synthetic test slot after it is fully initialized.
    game.set("persistence_enabled", true)
    game.set("save_slot", slot)
    game.call("_reload_save")
    return game

func approve_wave(game: Node, index: int) -> void:
    var sim: RefCounted = game.get("sim")
    sim.enemies.clear()
    sim.spawn_queue.clear()
    game.call("_on_wave_ready", index == 5)
    var campaign: RefCounted = game.get("campaign")
    require(int(campaign.progress[campaign.active_sector]) == index, "durably committed wave " + str(index))
    require(str(game.get("mode")) == "reward", "card offer state")
    require(game.get_node("UIShell")._offer.visible, "three card chooser")
    game.call("_on_card_selected", 0)
    require(str(game.get("mode")) == ("clean" if index == 5 else "between"), "durable card selection")

func _test() -> void:
    var rootdir: String = ProjectSettings.globalize_path(
        "user://p11-live-test-" + str(OS.get_process_id()) + "-" + str(Time.get_ticks_usec()))
    slot = rootdir.path_join("slot")
    var initialized: Dictionary = Bridge.ensure_slot(slot)
    require(initialized.get("ok", false), "new synthetic slot only")
    if not initialized.get("ok", false):
        quit(1)
        return
    var game: Node = instance_game()
    require(game.call("start_new_run"), "new OS creates a durable first checkpoint")
    var first_run_id: String = str(game.get("campaign").run_id)
    var state: Dictionary = Store.read(slot)
    require(state.get("ok", false) and state.state["os_checkpoint.json"] != null, "deployment stored")
    require(game.call("enter_sector", "CPU"), "enter first sector")
    approve_wave(game, 1)
    state = Store.read(slot)
    require(int(state.state["os_checkpoint.json"].campaign.completed_waves.CPU) == 1, "saved wave one")
    require(int(state.state["antivirus_db.json"].xp) == 20, "wave XP once")
    require(state.state["os_checkpoint.json"].pending_cards.is_empty(), "card claimed exactly once")
    game.get_parent().remove_child(game)
    game.free()

    game = instance_game()
    require(game.call("resume_checkpoint"), "resume after simulated application relaunch")
    require(str(game.get("mode")) == "between", "recovered between-wave state")
    require(int(game.get("campaign").progress["CPU"]) == 1, "recovered C's progress")
    require(game.call("start_next_wave"), "resume pending wave two")
    for n in range(2, 6):
        approve_wave(game, n)
        if n < 5:
            require(game.call("start_next_wave"), "progress to CPU next wave")
    game.call("retreat_to_hub")
    for sid in ["MEMORY", "NETWORK", "STORAGE"]:
        require(game.call("enter_sector", sid), "enter " + sid)
        for n in range(1, 6):
            approve_wave(game, n)
            if n < 5:
                require(game.call("start_next_wave"), sid + " next wave")
        game.call("retreat_to_hub")
    require(game.get("campaign").is_os_clean(), "all 4x5 sectors cleaned")
    state = Store.read(slot)
    require(state.get("ok", false), "final game checkpoint readable")
    if state.get("ok", false):
        var db: Dictionary = state.state["antivirus_db.json"]
        require(int(db.xp) == 1100, "20 waves + 4 boss + 4 sector + OS CLEAN XP = 1100")
        require(db.awarded_xp_ids.size() == 29, "29 unique XP receipts (20+4+4+1)")
        require(state.state["os_checkpoint.json"].scene == "hub", "last selected card resolved to hub")
    var equipped: Dictionary = game.get("software").levels
    require(not equipped.is_empty(), "software available to permanently keep")
    if not equipped.is_empty():
        var chosen: String = str(equipped.keys()[0])
        require(game.call("extract_software", chosen), "extract exactly one archive module")
        state = Store.read(slot)
        require(state.state["os_checkpoint.json"] == null, "closed run checkpoint removed")
        require(int(state.state["antivirus_db.json"].software_archive.get(chosen, 0)) == 1,
            "one permanent software archived")
        require(state.state["antivirus_db.json"].processed_extractions.size() == 1,
            "extraction idempotency receipt exists")
        require(int(state.state["profile.json"].extracted_runs) == 1
            and state.state["profile.json"].recent_runs[0].status == "extracted",
            "run profile and extraction receipt committed together")
        require(int(state.state["save.json"].runs) == 1 and int(state.state["save.json"].best_wave) == 5,
            "historical score and best wave committed with archive")
    require(str(game.get("mode")) == "title", "ready for next OS after extraction")
    if not equipped.is_empty():
        var archived_choice: String = str(equipped.keys()[0])
        require(game.call("select_archived_software", archived_choice), "select archived starting software")
    require(game.call("start_new_run"), "new OS deployment after safe extraction")
    require(str(game.get("campaign").run_id) != first_run_id,
        "different process instance cannot reuse a finished OS run ID")
    if not equipped.is_empty():
        require(int(game.get("software").levels.get(str(equipped.keys()[0]), 0)) == 1,
            "archived software equipped in the next OS")
    game.call("_on_setting_change", "volume", 0.55)
    game.call("_on_binding_change", "dash", "z")
    state = Store.read(slot)
    require(state.get("ok", false) and is_equal_approx(float(state.state["settings.json"].volume), 0.55),
        "changed volume persisted through E settings transaction")
    require(str(state.state["settings.json"].bindings.dash) == "z", "rebound dash saved without overwriting game progress")
    require(str(game.get_node("InputController").bindings.dash) == "z", "B input map updated after safe save")
    require(game.call("abandon_checkpoint"), "explicit abandon closes run and clears checkpoint")
    require(game.call("start_new_run"), "create fresh third OS for safety regression")
    game.get("campaign").apply_infection(80.0)
    require(game.call("purify"), "safe hub purification applies")
    state = Store.read(slot)
    require(state.get("ok", false) and int(state.state["os_checkpoint.json"].purify_uses) == 2,
        "purification use committed before UI acknowledges it")
    require(is_equal_approx(float(state.state["os_checkpoint.json"].infection.player_infection), 48.0),
        "saved hub infection matches purified campaign")
    var valid_slot: String = str(game.get("save_slot"))
    game.set("save_slot", rootdir.path_join("unreachable-slot"))
    var used_before: int = int(game.get("purify_uses"))
    var infection_before: float = float(game.get("campaign").infection)
    require(not game.call("purify"), "unwritable slot rejects purification")
    require(int(game.get("purify_uses")) == used_before and
        is_equal_approx(float(game.get("campaign").infection), infection_before),
        "failed save restores charge and infection without loss")
    game.set("save_slot", valid_slot)
    game.call("_resume_gameplay")
    require(game.call("enter_sector", "CPU"), "enter unconfirmed fourth OS wave")
    game.call("retreat_to_hub")
    require(str(game.get("mode")) == "hub", "retreat after unconfirmed wave returns hub")
    state = Store.read(slot)
    require(state.get("ok", false) and state.state["os_checkpoint.json"].scene == "hub",
        "retreat saves hub location before updating live world")
    require(int(state.state["os_checkpoint.json"].campaign.completed_waves.CPU) == 0,
        "unfinished wave never saved as cleared")
    var failed_id: String = str(game.get("campaign").run_id)
    require(game.call("_close_failed_run", "dead"), "death closes OS and cleans checkpoint")
    state = Store.read(slot)
    require(state.get("ok", false) and state.state["os_checkpoint.json"] == null,
        "dead OS has no resumable checkpoint")
    require(str(state.state["antivirus_db.json"].closed_runs.get(failed_id, "")) == "dead",
        "death receipt is durable")
    require(int(state.state["profile.json"].completed_runs) == 1,
        "death recorded exactly once in versioned profile")
    require(int(state.state["save.json"].runs) == 3,
        "extraction, manual abandon and death each produce one history entry (actual %d, archived %d, abandoned %d, dead %d)" % [
        int(state.state["save.json"].runs), int(state.state["profile.json"].extracted_runs),
        int(state.state["profile.json"].abandoned_runs), int(state.state["profile.json"].completed_runs)])
    require(game.call("start_new_run"), "new OS can deploy after real death closure")
    require(game.call("abandon_checkpoint"), "final run safely cleaned")
    game.get_parent().remove_child(game)
    game.free()
    if failures == 0:
        print("P11 END-TO-END SAVE INTEGRATION PASS (4x5 waves, restart, archive, abandon, synthetic state)")
    quit(1 if failures > 0 else 0)
