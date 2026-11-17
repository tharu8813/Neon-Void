extends RefCounted
## Owned exclusively by the Godot port. Never opens legacy paths for writing.
## One checksummed state envelope is committed atomically, avoiding a torn
## DB/checkpoint pair. A previous verified snapshot lives at state.json.bak.
const Validator = preload("res://scripts/persistence/legacy_validator.gd")
const MAX_STATE_BYTES: int = 16 * 1024 * 1024
const STATE_NAME := "state.json"

static func fresh_state() -> Dictionary:
    return {
        "save.json": {"best_score": 0, "best_wave": 0, "runs": 0},
        "settings.json": {"schema_version": 5, "volume": 0.9, "muted": false, "show_fps": false,
            "reduced_effects": false, "display_mode": "windowed", "window_width": 1280,
            "window_height": 720, "bindings": Validator.DEFAULT_KEYS.duplicate(true)},
        "profile.json": {"schema_version": 2, "total_kills": 0, "boss_kills": 0,
            "total_score": 0, "completed_runs": 0, "abandoned_runs": 0,
            "extracted_runs": 0, "best_combo": 0, "recent_runs": []},
        "antivirus_db.json": {"schema_version": 1, "antivirus_level": 1, "xp": 0,
            "software_archive": {}, "awarded_xp_ids": {}, "processed_extractions": {}, "closed_runs": {}},
        "os_checkpoint.json": null
    }

static func validate_state(state: Variant) -> bool:
    if typeof(state) != TYPE_DICTIONARY or state.size() != 5:
        return false
    for name in fresh_state().keys():
        if not state.has(name):
            return false
        if name == "os_checkpoint.json" and state[name] == null:
            continue
        if not Validator.validate(name, state[name]).ok:
            return false
    return true

static func _envelope(state: Dictionary, revision: int) -> String:
    var canonical: String = JSON.stringify(state, "", true, true)
    return JSON.stringify({"format": 1, "revision": revision,
        "payload_sha256": canonical.sha256_text(), "payload_json": canonical}, "", true, true) + "\n"

static func _encoded_state(state: Dictionary, revision: int) -> Dictionary:
    ## Guard BEFORE the backup or primary is touched. The nested JSON
    ## envelope can be substantially larger than its logical state payload.
    if not validate_state(state) or revision < 1 or revision > 1000000000:
        return {"ok": false, "reason": "invalid_transition"}
    var encoded: PackedByteArray = _envelope(state, revision).to_utf8_buffer()
    if encoded.size() > MAX_STATE_BYTES:
        return {"ok": false, "reason": "state_too_large"}
    return {"ok": true, "bytes": encoded}


static func _read_bytes(path: String, limit: int = MAX_STATE_BYTES) -> Dictionary:
    if not FileAccess.file_exists(path):
        return {"ok": false, "reason": "not_found"}
    var f: FileAccess = FileAccess.open(path, FileAccess.READ)
    if f == null:
        return {"ok": false, "reason": "read_failed"}
    var length: int = f.get_length()
    if length > limit:
        f.close()
        return {"ok": false, "reason": "oversized"}
    var bytes: PackedByteArray = f.get_buffer(length)
    var failed: bool = f.get_error() != OK and f.get_error() != ERR_FILE_EOF
    f.close()
    if failed or bytes.size() != length:
        return {"ok": false, "reason": "short_read"}
    return {"ok": true, "bytes": bytes}

static func _parse(bytes: PackedByteArray) -> Dictionary:
    var source: String = bytes.get_string_from_utf8()
    if source.to_utf8_buffer() != bytes:
        return {"ok": false, "reason": "non_utf8"}
    var json := JSON.new()
    if json.parse(source) != OK:
        return {"ok": false, "reason": "invalid_json"}
    return {"ok": true, "data": json.data}

static func read(slot_path: String) -> Dictionary:
    var raw: Dictionary = _read_bytes(slot_path.path_join(STATE_NAME))
    if not raw.ok:
        return raw
    var parsed: Dictionary = _parse(raw.bytes)
    if not parsed.ok:
        return parsed
    var env: Variant = parsed.data
    if typeof(env) != TYPE_DICTIONARY or env.size() != 4:
        return {"ok": false, "reason": "invalid_envelope"}
    if not (env.has("format") and env.has("revision") and env.has("payload_sha256") and env.has("payload_json")):
        return {"ok": false, "reason": "invalid_envelope"}
    if not Validator._integer(env.format, 1, 1) or not Validator._integer(env.revision, 1, 1000000000):
        return {"ok": false, "reason": "unsupported_version"}
    if typeof(env.payload_sha256) != TYPE_STRING or env.payload_sha256.length() != 64 or typeof(env.payload_json) != TYPE_STRING:
        return {"ok": false, "reason": "bad_digest"}
    if env.payload_json.sha256_text() != env.payload_sha256:
        return {"ok": false, "reason": "checksum_mismatch"}
    var unpacked: Dictionary = _parse(env.payload_json.to_utf8_buffer())
    if not unpacked.ok or not validate_state(unpacked.get("data")):
        return {"ok": false, "reason": "invalid_payload"}
    return {"ok": true, "revision": int(env.revision), "state": unpacked.data}

static func _atomic_write(path: String, data: PackedByteArray) -> bool:
    var temp: String = path + ".tmp-" + str(OS.get_process_id()) + "-" + str(Time.get_ticks_usec())
    var f: FileAccess = FileAccess.open(temp, FileAccess.WRITE)
    if f == null:
        return false
    f.store_buffer(data)
    f.flush()
    var good: bool = f.get_error() == OK
    f.close()
    if not good:
        DirAccess.remove_absolute(temp)
        return false
    if DirAccess.rename_absolute(temp, path) != OK:
        DirAccess.remove_absolute(temp)
        return false
    return true

static func initialize_staging(stage: String, state: Dictionary) -> Dictionary:
    if not validate_state(state) or not DirAccess.dir_exists_absolute(stage):
        return {"ok": false, "reason": "invalid_stage"}
    var file: String = stage.path_join(STATE_NAME)
    if FileAccess.file_exists(file):
        return {"ok": false, "reason": "already_initialized"}
    var prepared: Dictionary = _encoded_state(state, 1)
    if not prepared.ok:
        return prepared
    if not _atomic_write(file, prepared.bytes):
        return {"ok": false, "reason": "save_failed"}
    return read(stage)

static func _take_lock(slot_path: String) -> bool:
    return DirAccess.make_dir_absolute(slot_path + ".write-lock") == OK

static func _unlock(slot_path: String) -> void:
    DirAccess.remove_absolute(slot_path + ".write-lock")

static func transact(slot_path: String, operation: Callable) -> Dictionary:
    ## operation(state) -> {changed: bool, reason: String, state: Dictionary, ...}.
    ## The caller MUST NOT award success until this returns applied=true.
    if not _take_lock(slot_path):
        return {"applied": false, "reason": "locked"}
    var result: Dictionary = _transact_locked(slot_path, operation)
    _unlock(slot_path)
    return result

static func _transact_locked(slot_path: String, operation: Callable) -> Dictionary:
    var before: Dictionary = read(slot_path)
    if not before.ok:
        return {"applied": false, "reason": before.reason}
    if not operation.is_valid():
        return {"applied": false, "reason": "invalid_operation"}
    var outcome: Variant = operation.call(before.state.duplicate(true))
    if typeof(outcome) != TYPE_DICTIONARY or not outcome.get("ok", false):
        return {"applied": false, "reason": outcome.get("reason", "invalid_result") if typeof(outcome) == TYPE_DICTIONARY else "invalid_result"}
    if not outcome.get("changed", false):
        return {"applied": false, "reason": outcome.get("reason", "duplicate"), "revision": before.revision}
    if not validate_state(outcome.get("state")) or before.revision >= 1000000000:
        return {"applied": false, "reason": "invalid_transition"}
    var prepared: Dictionary = _encoded_state(outcome.state, int(before.revision) + 1)
    if not prepared.ok:
        return {"applied": false, "reason": prepared.reason}
    var file: String = slot_path.path_join(STATE_NAME)
    var old: Dictionary = _read_bytes(file)
    if not old.ok:
        return {"applied": false, "reason": old.reason}
    # Never overwrite a corrupt or mismatched primary. The lock covers the
    # revision check, backup write and replacement.
    if not _atomic_write(file + ".bak", old.bytes):
        return {"applied": false, "reason": "backup_failed"}
    if not _atomic_write(file, prepared.bytes):
        return {"applied": false, "reason": "save_failed"}
    var verified: Dictionary = read(slot_path)
    if not verified.ok or verified.revision != before.revision + 1:
        return {"applied": false, "reason": "commit_verify_failed"}
    return {"applied": true, "reason": outcome.get("reason", "committed"),
        "revision": verified.revision, "details": outcome.get("details", {})}

static func recover_from_backup(slot_path: String, explicit_consent: bool = false) -> Dictionary:
    ## Manual recovery only. Keep the damaged current bytes for investigation.
    if not explicit_consent:
        return {"ok": false, "reason": "explicit_consent_required"}
    if not _take_lock(slot_path):
        return {"ok": false, "reason": "locked"}
    var result: Dictionary = _recover_locked(slot_path)
    _unlock(slot_path)
    return result

static func _recover_locked(slot_path: String) -> Dictionary:
    ## Recovery is for an unreadable/corrupted primary, NOT a way to
    ## roll back healthy XP receipts or bypass an unsupported future format.
    var primary: Dictionary = read(slot_path)
    if primary.ok:
        return {"ok": false, "reason": "primary_is_valid"}
    if not primary.reason in ["not_found", "invalid_json", "non_utf8",
            "checksum_mismatch", "invalid_envelope", "bad_digest", "invalid_payload"]:
        return {"ok": false, "reason": "primary_requires_manual_review"}
    var file: String = slot_path.path_join(STATE_NAME)
    var backup: Dictionary = _read_bytes(file + ".bak")
    if not backup.ok:
        return {"ok": false, "reason": "backup_unavailable"}
    var staged: String = slot_path.path_join(".recovery-validation-" + str(Time.get_ticks_usec()))
    if DirAccess.make_dir_absolute(staged) != OK:
        return {"ok": false, "reason": "recovery_stage_failed"}
    var success: bool = _atomic_write(staged.path_join(STATE_NAME), backup.bytes)
    var valid: Dictionary = read(staged) if success else {"ok": false}
    DirAccess.remove_absolute(staged.path_join(STATE_NAME))
    DirAccess.remove_absolute(staged)
    if not valid.ok:
        return {"ok": false, "reason": "backup_invalid"}
    if FileAccess.file_exists(file):
        var current: Dictionary = _read_bytes(file)
        if not current.ok:
            return {"ok": false, "reason": "primary_unreadable"}
        var quarantine: String = file + ".before-recovery-" + str(Time.get_ticks_usec())
        if not _atomic_write(quarantine, current.bytes):
            return {"ok": false, "reason": "quarantine_failed"}
        var confirmed: Dictionary = _read_bytes(quarantine)
        if not confirmed.ok or confirmed.bytes != current.bytes:
            return {"ok": false, "reason": "quarantine_verify_failed"}
    if not _atomic_write(file, backup.bytes):
        return {"ok": false, "reason": "restore_failed"}
    return {"ok": true, "reason": "restored", "revision": valid.revision}

static func persist_document(slot_path: String, filename: String, document: Dictionary, expected_revision: int) -> Dictionary:
    ## UI-safe settings / statistics writer. Database and checkpoints can
    ## ONLY be changed by receipt / revision guarded progression APIs.
    if not filename in ["save.json", "settings.json", "profile.json"]:
        return {"applied": false, "reason": "reserved_document"}
    var converted: Dictionary = Validator.convert(filename, document)
    if not converted.ok:
        return {"applied": false, "reason": "invalid_document"}
    return transact(slot_path, func(state):
        var current: Dictionary = read(slot_path)
        if not current.ok or current.revision != expected_revision:
            return {"ok": false, "reason": "revision_conflict"}
        if JSON.stringify(state[filename], "", true, true) == JSON.stringify(converted.value, "", true, true):
            return {"ok": true, "changed": false, "reason": "unchanged"}
        var updated: Dictionary = state.duplicate(true)
        updated[filename] = converted.value
        return {"ok": true, "changed": true, "reason": "document_saved", "state": updated}
    )
