extends SceneTree
## Synthetic fixtures ONLY. This suite never looks for existing Python user saves.
const Store = preload("res://scripts/persistence/save_store.gd")
const Migration = preload("res://scripts/persistence/migration_service.gd")
const Progression = preload("res://scripts/progression/antivirus_progression.gd")
const Checkpoint = preload("res://scripts/progression/checkpoint_rules.gd")
const Validator = preload("res://scripts/persistence/legacy_validator.gd")
var failed: int = 0

func _initialize() -> void:
    create_timer(75.0).timeout.connect(func(): printerr("P8 TIMEOUT: headless persistence script did not exit"); quit(1))
    call_deferred("_run")

func expect(condition: bool, description: String) -> void:
    if not condition:
        printerr("P8 FAIL: " + description)
        failed += 1

func write_json(path: String, obj: Variant) -> bool:
    var file: FileAccess = FileAccess.open(path, FileAccess.WRITE)
    if file == null:
        return false
    file.store_string(JSON.stringify(obj, "  ", true) + "\n")
    file.flush()
    var ok: bool = file.get_error() == OK
    file.close()
    return ok

func checkpoint_fixture(run_id: String) -> Dictionary:
    return {
        "schema_version": 1, "revision": 1,
        "campaign": {"run_id": run_id, "seed": 12,
            "completed_waves": {"CPU": 0, "MEMORY": 0, "STORAGE": 0, "NETWORK": 0}},
        "scene": "hub", "sector": null,
        "hub_position": [1200, 780], "player_position": [1200, 780],
        "deployed_level": 1, "player": {"hp": 100, "mana": 100,
            "charges": 1, "fire_cd": 0, "pulse_cd": 0, "guard_cd": 0, "guard_t": 0},
        "software_levels": {}, "pending_cards": [],
        "infection": {"player_infection": 0, "os_integrity": 100},
        "purify_uses": 3, "salvage_claimed": false,
        "score": 0, "kills": 0, "combo": 0, "run_kills": 0,
        "run_boss_kills": 0, "run_best_combo": 0, "elapsed_frames": 0
    }

func _run() -> void:
    var suffix: String = str(OS.get_process_id()) + "-" + str(Time.get_ticks_usec())
    var root_dir: String = ProjectSettings.globalize_path("user://p8-test-" + suffix)
    var legacy: String = root_dir.path_join("legacy")
    var slot: String = root_dir.path_join("port-slot")
    expect(DirAccess.make_dir_recursive_absolute(legacy) == OK, "make isolated fixture directory")
    var old_settings := {"schema_version": 2, "volume": -0.25,
        "bindings": {"up": "w", "down": "s", "left": "a", "right": "d", "dash": "space", "pause": "p"}}
    var v2_settings: Dictionary = Validator.convert("settings.json", old_settings)
    expect(v2_settings.ok, "legacy settings v2 accepted and normalized")
    if v2_settings.ok:
        expect(v2_settings.value.schema_version == 5 and v2_settings.value.volume == 0.0,
            "settings version advanced with Python's volume clamp")
        expect(v2_settings.value.bindings.skill_pulse == "q" and v2_settings.value.bindings.skill_guard == "e",
            "legacy v2 key layout extended without conflict")
    var old_score: Dictionary = Validator.convert("save.json",
        {"best_score": "007", "best_wave": 4.9, "runs": 2})
    expect(old_score.ok and old_score.value.best_score == 7 and old_score.value.best_wave == 4,
        "legacy Python int-coercible score and wave survive migration")
    var old_profile: Dictionary = {"schema_version": 1, "total_score": 42, "recent_runs": []}
    var v1_profile: Dictionary = Validator.convert("profile.json", old_profile)
    expect(v1_profile.ok and v1_profile.value.schema_version == 2
        and v1_profile.value.extracted_runs == 0 and v1_profile.value.total_score == 42,
        "profile v1 normalized to v2 without changing totals")
    var state: Dictionary = Store.fresh_state()
    state["os_checkpoint.json"] = checkpoint_fixture("os-test")
    var originals := {}
    for name in Migration.FILES:
        expect(write_json(legacy.path_join(name), state[name]), "synthetic source: " + name)
        originals[name] = Store._read_bytes(legacy.path_join(name)).bytes
    expect(Validator.validate("os_checkpoint.json", state["os_checkpoint.json"]).ok, "legacy checkpoint fixture")
    expect(not Migration.import_legacy(legacy, slot).ok, "import requires explicit consent")
    var imported: Dictionary = Migration.import_legacy(legacy, slot, true)
    expect(imported.ok, "explicit migration commits all five JSONs: " + str(imported.get("reason", "")))
    if not imported.ok:
        Migration._delete_owned_tree(root_dir)
        quit(1)
        return
    expect(not Migration.import_legacy(legacy, slot, true).ok, "repeated migration cannot overwrite")
    var loaded: Dictionary = Store.read(slot)
    expect(loaded.ok and loaded.revision == 1, "checksum envelope loaded: " + str(loaded.get("reason", "")))
    if not loaded.ok:
        Migration._delete_owned_tree(root_dir)
        quit(1)
        return
    if loaded.ok:
        expect(loaded.state["antivirus_db.json"].xp == 0, "DB is XP authority")
        expect(loaded.state["profile.json"].total_score == 0, "profile not mixed with XP")
    var ui_settings: Dictionary = state["settings.json"].duplicate(true)
    ui_settings.show_fps = true
    expect(Store.persist_document(slot, "settings.json", ui_settings).applied,
        "D settings UI can write through guarded API")
    expect(not Store.persist_document(slot, "antivirus_db.json", {}).applied,
        "UI cannot bypass XP receipt authority")
    expect(Store.read(slot).state["settings.json"].show_fps, "changed settings durable")
    for name in Migration.FILES:
        var source_copy: Dictionary = Store._read_bytes(legacy.path_join(name))
        var backup: Dictionary = Store._read_bytes(slot.path_join("legacy_originals").path_join(name))
        expect(source_copy.ok and source_copy.bytes == originals[name], "original unchanged: " + name)
        expect(backup.ok and backup.bytes == originals[name], "lossless legacy backup: " + name)
    var temp_cp: Dictionary = checkpoint_fixture("os-test")
    temp_cp.scene = "play"
    temp_cp.sector = "cpu"
    temp_cp.player_position = [200, 360]
    temp_cp.campaign.completed_waves.CPU = 1
    var atom: Dictionary = Checkpoint.persist_confirmed_wave(slot, temp_cp, 1, "os-test", "cpu_core", 1)
    expect(atom.applied, "wave completion and first-clear XP in one commit")
    var duplicated: Dictionary = Progression.persist_event(slot, "os-test", "wave_cleared", "cpu_core", 1)
    expect(not duplicated.applied and duplicated.reason == "duplicate", "same wave XP never doubled")
    loaded = Store.read(slot)
    expect(loaded.ok and loaded.state["antivirus_db.json"].xp == 20, "XP persisted exactly once")
    expect(loaded.ok and loaded.state["os_checkpoint.json"].campaign.completed_waves.CPU == 1, "checkpoint persisted alongside XP")
    var resumed: Dictionary = Checkpoint.resume(loaded.state)
    expect(resumed.ok and resumed.next_waves.CPU == 2, "unfinished wave restarts, no live entities")
    expect(not Checkpoint.persist_checkpoint(slot, temp_cp, 1).applied, "stale checkpoint revision rejected")
    expect(not Progression.persist_event(slot, "fresh-run", "boss_killed", "cpu_core").applied,
        "boss reward blocked without all waves")
    for sector in Validator.SECTORS:
        for wave in range(1, 6):
            var receipt: Dictionary = Progression.persist_event(slot, "clean-run", "wave_cleared", sector, wave)
            expect(receipt.applied, "ordered first-clear " + sector + " " + str(wave))
        expect(Progression.persist_event(slot, "clean-run", "boss_killed", sector).applied,
            "boss receipt after five waves")
        expect(Progression.persist_event(slot, "clean-run", "sector_cleared", sector).applied,
            "sector clean receipt")
    expect(Progression.persist_event(slot, "clean-run", "os_cleaned").applied, "OS CLEAN receipt after four sectors")
    loaded = Store.read(slot)
    expect(loaded.ok and loaded.state["antivirus_db.json"].xp == 1120,
        "four sectors reward 1100 XP plus independent earlier wave 20")
    var clean_sectors: Array = Validator.SECTORS.duplicate()
    var extracted: Dictionary = Progression.persist_extraction(slot, "clean-run", clean_sectors, {"rapid": 2}, "rapid")
    expect(extracted.applied, "extract exactly one owned software")
    var extracted_twice: Dictionary = Progression.persist_extraction(slot, "clean-run", clean_sectors, {"rapid": 2}, "rapid")
    expect(not extracted_twice.applied, "extraction cannot be repeated")
    loaded = Store.read(slot)
    expect(loaded.ok and loaded.state["antivirus_db.json"].software_archive.rapid == 1,
        "archive gained exactly one copy")
    expect(Progression.starting_software(loaded.state["antivirus_db.json"], "rapid").levels == {"rapid": 1},
        "starting OS may install one archived software at level one")
    expect(not Progression.starting_software(loaded.state["antivirus_db.json"], "power").ok,
        "unarchived software cannot be selected")
    expect(Progression.persist_close_run(slot, "dead-run", "dead").applied, "close failed run")
    expect(not Progression.persist_event(slot, "dead-run", "wave_cleared", "cpu_core", 1).applied,
        "closed run cannot earn XP")
    var primary_backup: String = slot.path_join("state.json.bak")
    var preserved_backup: String = slot.path_join("state.json.bak.test-preserved")
    expect(DirAccess.rename_absolute(primary_backup, preserved_backup) == OK,
        "preserve pre-existing synthetic backup before failure injection")
    expect(DirAccess.make_dir_absolute(primary_backup) == OK,
        "inject unwritable backup destination")
    var before_write_failure: Dictionary = Store.read(slot)
    var blocked_write: Dictionary = Progression.persist_event(slot, "disk-failure-run",
        "wave_cleared", "cpu_core", 1)
    expect(not blocked_write.applied and blocked_write.reason == "backup_failed",
        "failed backup cannot grant permanent XP")
    expect(Store.read(slot).revision == before_write_failure.revision,
        "write failure leaves primary unmodified")
    DirAccess.remove_absolute(primary_backup)
    expect(DirAccess.rename_absolute(preserved_backup, primary_backup) == OK,
        "restore synthetic previous snapshot after failure test")
    var before: Dictionary = Store.read(slot)
    var invalid: Dictionary = Store.transact(slot, func(payload):
        payload["antivirus_db.json"].xp = 777777
        return {"ok": true, "changed": true, "state": payload, "reason": "bad"}
    )
    expect(not invalid.applied and invalid.reason == "invalid_transition", "invalid DB transaction rejected")
    expect(Store.read(slot).revision == before.revision, "invalid transaction preserves revision")
    expect(DirAccess.make_dir_absolute(slot + ".write-lock") == OK, "simulate second writer lock")
    expect(not Progression.persist_event(slot, "other-run", "wave_cleared", "cpu_core", 1).applied,
        "another writer prevents a new reward")
    DirAccess.remove_absolute(slot + ".write-lock")
    var corruption: String = slot.path_join("state.json")
    var broken: FileAccess = FileAccess.open(corruption, FileAccess.WRITE)
    if broken != null:
        broken.store_string("{corrupt")
        broken.close()
    expect(not Store.read(slot).ok, "bad checksum or malformed primary fails closed")
    expect(not Store.recover_from_backup(slot).ok, "recovery requires explicit consent")
    expect(Store.recover_from_backup(slot, true).ok, "explicit previous snapshot recovery")
    expect(Store.read(slot).ok, "recovery restores validated snapshot")
    var invalid_legacy: String = root_dir.path_join("invalid-legacy")
    expect(DirAccess.make_dir_absolute(invalid_legacy) == OK, "create corrupt fixture")
    var badfile: FileAccess = FileAccess.open(invalid_legacy.path_join("antivirus_db.json"), FileAccess.WRITE)
    if badfile != null:
        badfile.store_string("{bad-json")
        badfile.close()
    var bad_target: String = root_dir.path_join("bad-slot")
    expect(not Migration.import_legacy(invalid_legacy, bad_target, true).ok,
        "corrupt JSON must abort import")
    expect(not DirAccess.dir_exists_absolute(bad_target), "failed import leaves no slot")
    expect(FileAccess.file_exists(invalid_legacy.path_join("antivirus_db.json")),
        "corrupt source never quarantined or deleted")
    var future: Dictionary = Store.fresh_state()["antivirus_db.json"]
    future.schema_version = 2
    expect(write_json(invalid_legacy.path_join("antivirus_db.json"), future), "write future fixture")
    expect(not Migration.import_legacy(invalid_legacy, bad_target, true).ok,
        "unknown future schema refused")
    Migration._delete_owned_tree(root_dir)
    if failed == 0:
        print("P8 PERSISTENCE TEST PASS (fixtures, backups, idempotence, recovery, XP)")
    quit(1 if failed > 0 else 0)
