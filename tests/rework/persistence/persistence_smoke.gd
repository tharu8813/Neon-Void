extends SceneTree
## Fault injection uses unique user:// synthetic directory only. No real user saves.
const Model = preload("res://scripts/rework/progression/state_model.gd")
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")
const Gateway = preload("res://scripts/rework/persistence/progression_gateway.gd")
var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func check(condition: bool, note: String) -> void:
    if not condition:
        failures += 1
        printerr("REWORK E PERSISTENCE FAIL: " + note)

func _write(path: String, content: String) -> void:
    var file: FileAccess = FileAccess.open(path, FileAccess.WRITE)
    check(file != null, "synthetic write: " + path.get_file())
    if file != null:
        file.store_string(content)
        file.flush()
        file.close()

func _bytes(path: String) -> PackedByteArray:
    var file: FileAccess = FileAccess.open(path, FileAccess.READ)
    if file == null:
        return PackedByteArray()
    var result: PackedByteArray = file.get_buffer(file.get_length())
    file.close()
    return result

func _remove_tree(path: String) -> void:
    var dir: DirAccess = DirAccess.open(path)
    if dir == null:
        return
    dir.list_dir_begin()
    var entry: String = dir.get_next()
    while not entry.is_empty():
        if entry != "." and entry != "..":
            var child: String = path.path_join(entry)
            if dir.current_is_dir():
                _remove_tree(child)
            else:
                DirAccess.remove_absolute(child)
        entry = dir.get_next()
    dir.list_dir_end()
    DirAccess.remove_absolute(path)

func _run() -> void:
    var suffix: String = str(OS.get_process_id()) + "-" + str(Time.get_ticks_usec())
    var root_dir: String = ProjectSettings.globalize_path("user://rework-e-test-" + suffix)
    var slot: String = root_dir.path_join("rework-slot-synthetic")
    var legacy: String = root_dir.path_join("legacy")
    check(DirAccess.make_dir_recursive_absolute(legacy) == OK, "make synthetic root")
    var legacy_names := ["save.json", "settings.json", "profile.json",
        "antivirus_db.json", "os_checkpoint.json"]
    var original: Dictionary = {}
    for name in legacy_names:
        var path: String = legacy.path_join(name)
        _write(path, "{\"synthetic\":\"" + name + "\"}\n")
        original[name] = _bytes(path)

    var state: Dictionary = Model.new_slot("synthetic", "vaccine-1",
        {"mission_xp_step": 40})
    var initialized: Dictionary = Store.initialize(slot, state)
    check(initialized.get("ok", false), "initialize separate rework v1 slot")
    check(not Store.initialize(slot, state).ok, "existing slot NEVER overwritten")
    if not initialized.get("ok", false):
        _remove_tree(root_dir)
        quit(1)
        return
    check(Store.read(slot).state.balance.mission_xp_step == 40,
        "custom balance survives JSON round trip")
    check(not Store.read(root_dir).ok, "cannot treat legacy folder as rework slot")
    var command: Dictionary = {
        "action": "start_mission", "vaccine_id": "vaccine-1", "generation": 1,
        "run_id": "run-a", "seed": 777
    }
    var started: Dictionary = Gateway.transact_command(slot, command)
    check(started.get("applied", false), "mission start persisted")
    var first_revision: int = int(Store.read(slot).revision)
    check(first_revision == 2, "one revision per committed state")

    var event: Dictionary = {
        "action": "pending_reward", "vaccine_id": "vaccine-1", "generation": 1,
        "run_id": "run-a", "receipt_id": "objective-a", "xp": 100, "fragments": 100
    }
    check(not Gateway.transact_command(slot, event, first_revision + 9).applied,
        "stale optimistic revision rejected")
    check(Store.read(slot).revision == first_revision,
        "revision conflict is strictly read-only")
    check(not Gateway.transact_command(slot, event, -1, "before_backup").applied,
        "crash injected before backup")
    check(Store.read(slot).revision == first_revision,
        "before-backup crash left primary untouched")
    check(not Gateway.transact_command(slot, event, -1, "after_backup").applied,
        "crash injected before primary promotion")
    check(Store.read(slot).revision == first_revision,
        "after-backup crash did not apply award")
    var ack_lost: Dictionary = Gateway.transact_command(slot, event, -1, "after_primary")
    check(not ack_lost.applied and ack_lost.reason == "simulated_ack_lost",
        "simulate process dying after atomic commit before acknowledgement")
    var saved: Dictionary = Store.read(slot)
    check(saved.ok and saved.revision == first_revision + 1             and saved.state.vaccine.mission.uncommitted_vaccine_xp == 100,
        "committed receipt survives acknowledgement loss/restart")
    check(not Gateway.transact_command(slot, event).applied,
        "idempotent replay after crash does not duplicate pending rewards")
    check(Store.read(slot).revision == saved.revision,
        "duplicate replay cannot advance revision")

    var temp: String = slot.path_join("state.json.tmp-abandoned")
    _write(temp, "{uncommitted broken staging file")
    check(Store.read(slot).ok, "abandoned temp is never loaded as committed state")
    DirAccess.remove_absolute(temp)
    var lock: Dictionary = Store._take_lock(slot)
    check(lock.get("ok", false), "synthetic process lock acquired")
    if lock.ok:
        check(not Gateway.transact_command(slot, event).applied, "concurrent writer denied")
        check(not Store.release_orphan_lock_offline(slot, lock.token).ok,
            "lock cannot be stolen without explicit offline confirmation")
        check(Store.release_orphan_lock_offline(slot, lock.token, true).ok,
            "test-only owner token can be released explicitly")

    # Four or five preceding file names remain byte-identical, regardless of failures.
    for name in legacy_names:
        check(_bytes(legacy.path_join(name)) == original[name],
            "legacy synthetic file unchanged: " + name)

    var current: PackedByteArray = _bytes(slot.path_join("state.json"))
    _write(slot.path_join("state.json"), "{bad json")
    check(Store.read(slot).reason == "invalid_json", "corrupt primary rejected")
    check(not Gateway.transact_command(slot, event).applied, "corruption fails closed")
    check(Store.initialize(slot, state).reason == "slot_exists",
        "corrupt existing slot must not silently reset")
    check(not Store.restore_backup(slot).ok, "recovery needs explicit consent")
    var recovered: Dictionary = Store.restore_backup(slot, true)
    check(recovered.ok and recovered.revision == first_revision,
        "manual verified backup recovery rolls back to previous revision")
    check(FileAccess.file_exists(slot.path_join("state.json.bak")),
        "backup remains available after recovery")
    check(_bytes(slot.path_join("state.json")) != current,
        "recovery uses prior verified backup, not newer damaged primary")

    _write(slot.path_join("state.json"), "{\"format\":99}")
    check(Store.read(slot).reason == "future_format", "future save format denied")
    check(Store.restore_backup(slot, true).reason == "future_version_must_not_rollback",
        "future format must never silently downgrade from backup")
    check(not Gateway.transact_command(slot, event).applied,
        "unsupported format never accepts writes")

    # All files cleaned are in this uniquely named synthetic root only.
    _remove_tree(root_dir)
    if failures == 0:
        print("REWORK E PERSISTENCE PASS: synthetic atomicity, restart, recovery and isolation")
    quit(1 if failures > 0 else 0)
