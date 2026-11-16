extends RefCounted
## One-time, explicitly authorized import. Originals are opened READ-only.
## A complete staged directory is renamed into user://; an existing slot
## is never replaced. Backups of source bytes remain inside the new slot.
const Validator = preload("res://scripts/persistence/legacy_validator.gd")
const Store = preload("res://scripts/persistence/save_store.gd")
const FILES := ["save.json", "settings.json", "profile.json", "antivirus_db.json", "os_checkpoint.json"]
const MAX_LEGACY_BYTES: int = 8 * 1024 * 1024

static func _digest(bytes: PackedByteArray) -> String:
    var h := HashingContext.new()
    h.start(HashingContext.HASH_SHA256)
    h.update(bytes)
    return h.finish().hex_encode()

static func _delete_owned_tree(path: String) -> void:
    var dir: DirAccess = DirAccess.open(path)
    if dir == null:
        return
    for filename in dir.get_files():
        DirAccess.remove_absolute(path.path_join(filename))
    for folder in dir.get_directories():
        _delete_owned_tree(path.path_join(folder))
    DirAccess.remove_absolute(path)

static func import_legacy(source_directory: String, target_slot: String = "user://persistence/slot-v1",
        explicit_consent: bool = false) -> Dictionary:
    if not explicit_consent:
        return {"ok": false, "reason": "explicit_consent_required"}
    var source: String = ProjectSettings.globalize_path(source_directory).simplify_path()
    var target: String = ProjectSettings.globalize_path(target_slot).simplify_path()
    var user_root: String = ProjectSettings.globalize_path("user://").simplify_path()
    if not target.begins_with(user_root + "/") or target == source:
        return {"ok": false, "reason": "destination_must_be_separate_user_directory"}
    if target.begins_with(source + "/") or source.begins_with(target + "/"):
        return {"ok": false, "reason": "legacy_and_target_paths_overlap"}
    if not DirAccess.dir_exists_absolute(source):
        return {"ok": false, "reason": "source_directory_not_found"}
    if DirAccess.dir_exists_absolute(target) or FileAccess.file_exists(target):
        return {"ok": false, "reason": "slot_already_exists"}

    var state: Dictionary = Store.fresh_state()
    var originals := {}
    var provenance := {}
    for name in FILES:
        var file_path: String = source.path_join(name)
        if not FileAccess.file_exists(file_path):
            provenance[name] = {"present": false}
            continue
        var raw: Dictionary = Store._read_bytes(file_path, 128 * 1024 if name == "os_checkpoint.json" else MAX_LEGACY_BYTES)
        if not raw.ok:
            return {"ok": false, "reason": "source_" + name + "_" + raw.reason}
        var parsed: Dictionary = Store._parse(raw.bytes)
        if not parsed.ok:
            return {"ok": false, "reason": "source_" + name + "_" + parsed.reason}
        var result: Dictionary = Validator.validate(name, parsed.data)
        if not result.ok:
            return {"ok": false, "reason": "source_" + name + "_" + result.reason}
        var converted: Dictionary = Validator.convert(name, parsed.data)
        if not converted.ok:
            return {"ok": false, "reason": "conversion_" + name + "_" + converted.reason}
        state[name] = converted.value
        originals[name] = raw.bytes
        provenance[name] = {"present": true, "sha256": _digest(raw.bytes),
            "bytes": raw.bytes.size(), "schema_version": parsed.data.get("schema_version", 0)}
    if originals.is_empty():
        return {"ok": false, "reason": "no_legacy_files"}
    if not Store.validate_state(state):
        return {"ok": false, "reason": "invalid_converted_state"}
    var parent: String = target.get_base_dir()
    if DirAccess.make_dir_recursive_absolute(parent) != OK:
        return {"ok": false, "reason": "destination_unwritable"}
    var stage: String = target + ".importing-" + str(OS.get_process_id()) + "-" + str(Time.get_ticks_usec())
    if DirAccess.make_dir_absolute(stage) != OK:
        return {"ok": false, "reason": "staging_unavailable"}
    var outcome: Dictionary = _populate_staging(stage, source, state, originals, provenance)
    if not outcome.ok:
        _delete_owned_tree(stage)
        return outcome
    # Another process may have won the import race while we were staging.
    if DirAccess.dir_exists_absolute(target) or FileAccess.file_exists(target):
        _delete_owned_tree(stage)
        return {"ok": false, "reason": "concurrent_import_or_existing_slot"}
    if DirAccess.rename_absolute(stage, target) != OK:
        _delete_owned_tree(stage)
        return {"ok": false, "reason": "atomic_import_commit_failed"}
    return {"ok": true, "reason": "imported", "slot": target, "files": provenance}

static func _populate_staging(stage: String, source: String, state: Dictionary,
        originals: Dictionary, provenance: Dictionary) -> Dictionary:
    var backups: String = stage.path_join("legacy_originals")
    if DirAccess.make_dir_absolute(backups) != OK:
        return {"ok": false, "reason": "backup_directory_failed"}
    for name in originals:
        var copy_path: String = backups.path_join(name)
        if not Store._atomic_write(copy_path, originals[name]):
            return {"ok": false, "reason": "backup_write_failed"}
        var verify: Dictionary = Store._read_bytes(copy_path)
        if not verify.ok or _digest(verify.bytes) != provenance[name].sha256:
            return {"ok": false, "reason": "backup_integrity_failed"}
    var manifest := {"migration_version": 1, "source_policy": "read_only",
        "source_location_for_audit": source, "sources": provenance}
    var manifest_path: String = stage.path_join("migration_manifest.json")
    if not Store._atomic_write(manifest_path, (JSON.stringify(manifest, "  ", true) + "\n").to_utf8_buffer()):
        return {"ok": false, "reason": "manifest_write_failed"}
    var initial: Dictionary = Store.initialize_staging(stage, state)
    if not initial.ok or initial.revision != 1:
        return {"ok": false, "reason": "converted_payload_invalid:" + str(initial.get("reason", "revision"))}
    # Catch a source modified while the import was running: never advertise an
    # outdated migration as the latest state.
    for name in originals:
        var reread: Dictionary = Store._read_bytes(source.path_join(name), 128 * 1024 if name == "os_checkpoint.json" else MAX_LEGACY_BYTES)
        if not reread.ok or _digest(reread.bytes) != provenance[name].sha256:
            return {"ok": false, "reason": "source_modified_during_import"}
    return {"ok": true}
