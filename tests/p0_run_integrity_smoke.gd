extends SceneTree
## P0 game-integrity regression. All data is synthetic and under a unique
## user:// directory; never reads/mutates real player save or Python sources.
const Main = preload("res://scenes/main.tscn")
const Store = preload("res://scripts/persistence/save_store.gd")
const Bridge = preload("res://scripts/integration/save_bridge.gd")
var failures: int = 0
var slot: String = ""

func _initialize() -> void:
    create_timer(65.0).timeout.connect(func(): printerr("P0 RUN INTEGRITY TIMEOUT"); quit(1))
    call_deferred("_run")

func check(ok: bool, label: String) -> void:
    if not ok:
        failures += 1
        printerr("P0 RUN INTEGRITY FAIL: " + label)

func game_instance() -> Node:
    var game: Node = Main.instantiate()
    root.add_child(game)
    game.set("persistence_enabled", true)
    game.set("save_slot", slot)
    check(game.call("_reload_save"), "load synthetic slot")
    return game

func close_game(game: Node) -> void:
    game.get_parent().remove_child(game)
    game.free()

func _run() -> void:
    var tmp: String = ProjectSettings.globalize_path(
        "user://p0-integrity-" + str(OS.get_process_id()) + "-" + str(Time.get_ticks_usec()))
    slot = tmp.path_join("slot")
    check(Bridge.ensure_slot(slot).get("ok", false), "initialize isolated slot")
    var game: Node = game_instance()
    check(game.call("start_new_run", 42), "explicit test seed creates a run")
    check(int(game.get("campaign").seed_value) == 42, "fixed seed remains testable")
    check(int(game.get("sim").rng.seed) == 42, "combat and campaign share seed")
    var state: Dictionary = Store.read(slot)
    check(state.get("ok", false) and int(state.state["os_checkpoint.json"].campaign.seed) == 42,
        "seed persisted for deterministic resume")
    check(game.call("enter_sector", "CPU"), "start CPU wave")
    for n in range(10):
        game.call("_on_enemy_died", 9000 + n, "drifter")
    check(int(game.get("run_best_combo")) == 10, "10 kills produce 10 peak combo")
    var actor: Node = game.get_node("LegacyPlayer")
    actor.set("charges", 0)
    actor.set("charge_t", 0.43)
    actor.set("dash_t", 0.11)
    actor.set("invulnerability_t", 0.20)
    game.call("retreat_to_hub")
    state = Store.read(slot)
    check(str(game.get("mode")) == "hub" and state.get("ok", false),
        "retreat persists safe hub checkpoint")
    check(int(state.state["os_checkpoint.json"].run_best_combo) == 10,
        "peak combo is saved across sector transition")
    check(int(state.state["os_checkpoint.json"].player.charges) == 0,
        "remaining dash charges are saved")
    close_game(game)
    game = game_instance()
    check(game.call("resume_checkpoint"), "saved OS resumes after relaunch")
    check(int(game.get("run_best_combo")) == 10, "peak combo survives reload")
    check(int(game.get("campaign").seed_value) == 42, "same seed survives reload")
    actor = game.get_node("LegacyPlayer")
    check(int(actor.get("charges")) == 0, "resume preserves spent dash charges")
    check(is_zero_approx(float(actor.get("dash_t"))), "resume has no stale dash active")
    check(is_zero_approx(float(actor.get("invulnerability_t"))), "resume has no stale immunity")
    check(game.call("abandon_checkpoint"), "end original run safely")
    state = Store.read(slot)
    check(int(state.state["profile.json"].best_combo) == 10,
        "profile stores real best combo, not a zero placeholder")
    check(int(state.state["profile.json"].recent_runs[0].best_combo) == 10,
        "recent run stores real best combo")
    actor.set("charges", 0)
    actor.set("dash_t", 0.11)
    actor.set("charge_t", 0.45)
    actor.set("invulnerability_t", 0.22)
    check(game.call("start_new_run", 1357), "second run starts with injected seed")
    check(int(game.get("campaign").seed_value) == 1357,
        "new run uses supplied seed, not 42")
    check(int(actor.get("charges")) == int(actor.get("dash_max")),
        "new run refills dash charges")
    check(is_zero_approx(float(actor.get("dash_t"))) and
        is_zero_approx(float(actor.get("charge_t"))) and
        is_zero_approx(float(actor.get("invulnerability_t"))),
        "new run clears dash, recharge and immunity timers")
    check(int(game.get("run_best_combo")) == 0 and int(game.get("run_kills")) == 0,
        "new run resets transient score state")
    state = Store.read(slot)
    check(int(state.state["os_checkpoint.json"].campaign.seed) == 1357,
        "second run seed committed")
    check(game.call("abandon_checkpoint"), "close second OS")
    check(game.call("start_new_run"), "ordinary new run obtains random seed")
    var run_seed: int = int(game.get("campaign").seed_value)
    check(run_seed >= 0 and run_seed <= 2147483647 and
        int(game.get("sim").rng.seed) == run_seed,
        "random seed range and combat sync")
    state = Store.read(slot)
    check(int(state.state["os_checkpoint.json"].campaign.seed) == run_seed,
        "random seed survives checkpoint")
    check(game.call("abandon_checkpoint"), "close third OS")
    close_game(game)

    # Simulate an in-flight active writer with an owned lock.
    var normal_lock: Dictionary = Store._take_lock(slot)
    check(normal_lock.get("ok", false), "acquire owner-tagged lock")
    if normal_lock.get("ok", false):
        var current_owner: Dictionary = Store._lock_owner(slot)
        check(current_owner.get("ok", false) and int(current_owner.pid) == OS.get_process_id(),
            "owner metadata names current process")
        check(not Store.recover_stale_lock_offline(slot, str(normal_lock.token), true).get("ok", false),
            "never quarantine a lock owned by this active process")
        check(Store._unlock(slot, str(normal_lock.token)), "owner can unlock")
    var primary_before: Dictionary = Store._read_bytes(slot.path_join("state.json"))
    var backup_before: Dictionary = Store._read_bytes(slot.path_join("state.json.bak"))
    var lock_dir: String = slot + ".write-lock"
    check(DirAccess.make_dir_absolute(lock_dir) == OK, "simulate orphaned lock")
    var orphan_token: String = "0123456789abcdef0123456789abcdef"
    var owner_file: FileAccess = FileAccess.open(lock_dir.path_join("owner.json"), FileAccess.WRITE)
    check(owner_file != null, "create synthetic owner metadata")
    if owner_file != null:
        owner_file.store_string(JSON.stringify({
            "format": 1, "pid": 2147483647,
            "created_unix": int(Time.get_unix_time_from_system()), "token": orphan_token}))
        owner_file.close()
    check(not Store.recover_stale_lock_offline(slot, orphan_token, false).get("ok", false),
        "offline confirmation is mandatory")
    check(not Store.recover_stale_lock_offline(slot, "ffffffffffffffffffffffffffffffff", true).get("ok", false),
        "cannot bypass owner nonce")
    var failed_write: Dictionary = Store.transact(slot, func(snapshot: Dictionary):
        return {"ok": true, "changed": true, "state": snapshot})
    check(not failed_write.get("applied", false) and failed_write.get("reason") == "locked",
        "regular writer fails closed rather than deleting an orphaned lock")
    var rescue: Dictionary = Store.recover_stale_lock_offline(slot, orphan_token, true)
    check(rescue.get("ok", false), "explicit offline quarantine of synthetic orphan")
    check(DirAccess.dir_exists_absolute(str(rescue.get("quarantine", ""))),
        "quarantined lock retained for forensic inspection")
    check(not DirAccess.dir_exists_absolute(lock_dir), "old lock no longer blocks a future writer")
    var primary_after: Dictionary = Store._read_bytes(slot.path_join("state.json"))
    var backup_after: Dictionary = Store._read_bytes(slot.path_join("state.json.bak"))
    check(primary_before.get("ok", false) and primary_after.get("ok", false)
        and primary_before.bytes == primary_after.bytes,
        "primary bytes unchanged by lock quarantine")
    check(backup_before.get("ok", false) and backup_after.get("ok", false)
        and backup_before.bytes == backup_after.bytes,
        "backup bytes unchanged by lock quarantine")
    state = Store.read(slot)
    var opts: Dictionary = state.state["settings.json"].duplicate(true)
    opts["volume"] = 0.61
    check(Store.persist_document(slot, "settings.json", opts, int(state.revision)).get("applied", false),
        "ordinary durable writes work after audited quarantine")
    if failures == 0:
        print("P0 RUN AND LOCK INTEGRITY PASS (new OS, resume, combo, offline lock quarantine)")
    quit(1 if failures > 0 else 0)
