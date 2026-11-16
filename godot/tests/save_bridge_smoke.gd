extends SceneTree
## Synthetic P11 save bridge regression. Does not inspect existing legacy saves.
const Bridge = preload("res://scripts/integration/save_bridge.gd")
const Store = preload("res://scripts/persistence/save_store.gd")
const Validator = preload("res://scripts/persistence/legacy_validator.gd")
const Campaign = preload("res://scripts/sectors/sector_campaign.gd")
const Software = preload("res://scripts/software/software_system.gd")
const Combat = preload("res://scripts/combat/combat_sim.gd")
var failures: int = 0

func _initialize() -> void:
    create_timer(50.0).timeout.connect(func(): printerr("P11 SAVE TIMEOUT"); quit(1))
    call_deferred("_run")

func check(ok: bool, why: String) -> void:
    if not ok:
        failures += 1
        printerr("P11 SAVE FAIL: " + why)

func _run() -> void:
    var tmp: String = ProjectSettings.globalize_path("user://p11-bridge-" + str(OS.get_process_id()) + "-" + str(Time.get_ticks_usec()))
    var slot: String = tmp.path_join("slot")
    var loaded: Dictionary = Bridge.ensure_slot(slot)
    check(bool(loaded.get("ok", false)) and not bool(loaded.get("existing", true)), "new unique slot created")
    check(Bridge.sector_to_save("CPU") == "cpu_core" and Bridge.sector_to_save("MEMORY") == "memory_sector" and Bridge.sector_to_save("STORAGE") == "storage_vault" and Bridge.sector_to_save("NETWORK") == "network_grid", "explicit C/E sector IDs")
    check(Bridge.sector_to_scene("CPU") == "cpu" and Bridge.sector_to_save("UNKNOWN").is_empty(), "checkpoint IDs and unknown refusal")
    var again: Dictionary = Bridge.ensure_slot(slot)
    check(bool(again.get("ok", false)) and bool(again.get("existing", false)) and int(again.get("revision", 0)) == 1, "existing valid slot reused")
    var root_scene: PackedScene = load("res://scenes/main.tscn")
    var game: Node = root_scene.instantiate()
    root.add_child(game)
    check(game.call("start_new_run"), "real actor and run created")
    var campaign: RefCounted = game.get("campaign")
    var actor: Node = game.get_node("LegacyPlayer")
    var combat: RefCounted = game.get("sim")
    var software: RefCounted = game.get("software")
    var empty_cards: Array[String] = []
    var cp: Dictionary = Bridge.make_checkpoint(campaign, actor, combat, software, "hub",
        Vector2(1200, 780), empty_cards, 3, 0, 0, 0, 1, 0)
    check(not cp.is_empty() and Validator.validate("os_checkpoint.json", cp).ok, "first deployment checkpoint shape")
    var first: Dictionary = Bridge.persist_checkpoint(slot, cp, 0)
    check(first.get("applied", false), "first checkpoint committed")
    check(game.call("enter_sector", "CPU"), "enter CPU for first wave")
    var cards: Array[String] = software.roll_offers(3, "CPU", combat.rng)
    var future: Dictionary = Bridge.make_checkpoint(campaign, actor, combat, software, "upgrade",
        Vector2(1200, 780), cards, 3, 0, 0, 0, 1, 1, 1)
    check(not future.is_empty(), "future completed wave receipt has durable upgrade cards")
    check(int(campaign.progress["CPU"]) == 0, "in-memory world uncommitted before E transaction")
    var accepted: Dictionary = Bridge.persist_wave(slot, future, 1, str(campaign.run_id), "CPU", 1)
    check(accepted.get("applied", false), "wave XP and checkpoint committed atomically")
    check(int(campaign.progress["CPU"]) == 0, "world still unchanged before save acknowledgment")
    check(campaign.confirm_wave(0, 0, false), "world acknowledges only after durable receipt")
    var stored: Dictionary = Store.read(slot)
    check(stored.get("ok", false) and int(stored.get("revision", 0)) == 3, "transaction revisions monotonic")
    if stored.get("ok", false):
        check(int(stored.state["antivirus_db.json"].xp) == 20, "exactly one wave XP")
        check(stored.state["os_checkpoint.json"].pending_cards == cards, "three cards survive restart")
        check(stored.state["os_checkpoint.json"].scene == "upgrade", "recoverable reward mode")
        var restored: RefCounted = Bridge.replay_campaign(stored.state)
        check(restored != null and int(restored.progress["CPU"]) == 1, "C campaign replay from E checkpoint")
    var duplicate: Dictionary = Bridge.persist_wave(slot, future, 1, str(campaign.run_id), "CPU", 1)
    check(not duplicate.get("applied", false), "duplicate stale revision blocked")
    var after: Dictionary = Store.read(slot)
    check(after.get("ok", false) and int(after.state["antivirus_db.json"].xp) == 20, "replay never duplicates XP")
    campaign.progress["CPU"] = 4
    campaign.active_sector = "CPU"
    campaign.active_wave = 5
    var boss_cards: Array[String] = software.roll_offers(3, "CPU", combat.rng)
    var boss_cp: Dictionary = Bridge.make_checkpoint(campaign, actor, combat, software, "upgrade",
        Vector2(1200, 780), boss_cards, 3, 0, 0, 0, 1, 100, 5)
    check(not boss_cp.is_empty(), "boss wave five has recoverable card choice")
    campaign.progress = {"CPU": 4, "MEMORY": 5, "STORAGE": 5, "NETWORK": 5}
    var last_cp: Dictionary = Bridge.make_checkpoint(campaign, actor, combat, software, "upgrade",
        Vector2(1200, 780), boss_cards, 3, 0, 0, 0, 1, 100, 5)
    check(not last_cp.is_empty(), "fourth sector final boss choice survives OS CLEAN")
    var corrupt: String = tmp.path_join("corrupt-slot")
    check(DirAccess.make_dir_absolute(corrupt) == OK, "make corrupt fixture")
    var file: FileAccess = FileAccess.open(corrupt.path_join("state.json"), FileAccess.WRITE)
    if file != null:
        file.store_string("{broken")
        file.close()
        var malformed: Dictionary = Bridge.ensure_slot(corrupt)
        check(not malformed.get("ok", true), "corrupt pre-existing slot never silently reset")
        var verify: FileAccess = FileAccess.open(corrupt.path_join("state.json"), FileAccess.READ)
        check(verify != null and verify.get_as_text() == "{broken", "corrupt original bytes remain untouched")
        if verify != null:
            verify.close()
    game.queue_free()
    if failures == 0:
        print("P11 SAVE BRIDGE PASS (atomic wave XP, recoverable cards, fail-closed existing slot)")
    quit(1 if failures > 0 else 0)
