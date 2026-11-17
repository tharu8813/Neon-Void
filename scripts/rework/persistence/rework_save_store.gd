extends RefCounted
## Rework-only save namespace. Never imports or writes Python or existing P8 files.
## Durable unit: one revisioned, checksummed envelope (all receipts and growth together).
const Model = preload("res://scripts/rework/progression/state_model.gd")
const Fence = preload("res://scripts/rework/persistence/lifetime_fence.gd")
const STATE_FILE := "state.json"
const BACKUP_FILE := "state.json.bak"
const RECOVERY_FILE := "state.json.recovery"
const INTENT_FILE := "state.json.intent"
const FORMAT: int = 1
const MAX_BYTES: int = 16 * 1024 * 1024

static func default_slot_path(slot_id: String) -> String:
    if not Model.valid_slot_id(slot_id):
        return ""
    return ProjectSettings.globalize_path(
        "user://rework/v1/rework-slot-" + slot_id)

static func _valid_slot(path: String) -> bool:
    return not path.is_empty() and path.is_absolute_path()         and path.get_file().begins_with("rework-slot-") and path.get_file().length() > 12

static func _read_bytes(path: String) -> Dictionary:
    if not FileAccess.file_exists(path):
        return {"ok": false, "reason": "not_found"}
    var handle: FileAccess = FileAccess.open(path, FileAccess.READ)
    if handle == null:
        return {"ok": false, "reason": "read_failed"}
    var size: int = handle.get_length()
    if size <= 0 or size > MAX_BYTES:
        handle.close()
        return {"ok": false, "reason": "invalid_length"}
    var data: PackedByteArray = handle.get_buffer(size)
    var error: Error = handle.get_error()
    handle.close()
    if data.size() != size or (error != OK and error != ERR_FILE_EOF):
        return {"ok": false, "reason": "short_read"}
    return {"ok": true, "bytes": data}

static func _decode(data: PackedByteArray) -> Dictionary:
    var json_text: String = data.get_string_from_utf8()
    if json_text.to_utf8_buffer() != data:
        return {"ok": false, "reason": "bad_utf8"}
    var parser := JSON.new()
    if parser.parse(json_text) != OK or typeof(parser.data) != TYPE_DICTIONARY:
        return {"ok": false, "reason": "invalid_json"}
    var env: Dictionary = parser.data
    if not Model.natural(env.get("format"), 1000000):
        return {"ok": false, "reason": "invalid_format"}
    if int(env.format) > FORMAT:
        return {"ok": false, "reason": "future_format"}
    if int(env.format) != FORMAT or env.size() != 4             or not Model.natural(env.get("revision"), 1000000000) or int(env.revision) < 1:
        return {"ok": false, "reason": "invalid_envelope"}
    if typeof(env.get("payload_json")) != TYPE_STRING             or typeof(env.get("payload_sha256")) != TYPE_STRING             or env.payload_sha256.length() != 64             or env.payload_json.sha256_text() != env.payload_sha256:
        return {"ok": false, "reason": "checksum_mismatch"}
    var payload_parser := JSON.new()
    if payload_parser.parse(env.payload_json) != OK             or typeof(payload_parser.data) != TYPE_DICTIONARY:
        return {"ok": false, "reason": "invalid_payload_json"}
    var state: Dictionary = payload_parser.data
    if Model.natural(state.get("schema_version"), 1000000)             and int(state.schema_version) > Model.VERSION:
        return {"ok": false, "reason": "future_schema"}
    if not Model.validate(state):
        return {"ok": false, "reason": "invalid_state"}
    return {"ok": true, "revision": int(env.revision), "state": state}

static func _encode(state: Dictionary, revision: int) -> Dictionary:
    if not Model.validate(state) or revision < 1 or revision > 1000000000:
        return {"ok": false, "reason": "invalid_state"}
    var payload: String = JSON.stringify(state, "", true, true)
    var env := {
        "format": FORMAT, "revision": revision,
        "payload_sha256": payload.sha256_text(), "payload_json": payload
    }
    var bytes: PackedByteArray = (JSON.stringify(env, "", true, true) + "\n").to_utf8_buffer()
    if bytes.size() > MAX_BYTES:
        return {"ok": false, "reason": "state_too_large"}
    return {"ok": true, "bytes": bytes}

static func _write_atomic(path: String, content: PackedByteArray) -> bool:
    var token: String = Crypto.new().generate_random_bytes(16).hex_encode()
    if token.length() != 32:
        return false
    var temp: String = path + ".tmp-" + token
    var handle: FileAccess = FileAccess.open(temp, FileAccess.WRITE)
    if handle == null:
        return false
    handle.store_buffer(content)
    handle.flush()
    var good: bool = handle.get_error() == OK
    handle.close()
    if not good:
        DirAccess.remove_absolute(temp)
        return false
    if DirAccess.rename_absolute(temp, path) != OK:
        DirAccess.remove_absolute(temp)
        return false
    return true

static func _read_fence(slot_path: String) -> Dictionary:
    var raw: Dictionary = _read_bytes(slot_path.path_join(Fence.FILENAME))
    if not raw.ok:
        return {"ok": false, "reason": "lifetime_fence_missing_or_unreadable"}
    return Fence.parse(raw.bytes)

static func _read_intent(slot_path: String) -> Dictionary:
    var raw: Dictionary = _read_bytes(slot_path.path_join(INTENT_FILE))
    if not raw.ok:
        return {"ok": false, "reason": "intent_missing_or_unreadable"}
    var parsed: Dictionary = _decode(raw.bytes)
    if not parsed.ok:
        return {"ok": false, "reason": "intent_" + str(parsed.reason)}
    if parsed.state.slot_id != slot_path.get_file().trim_prefix("rework-slot-"):
        return {"ok": false, "reason": "intent_slot_mismatch"}
    parsed["bytes"] = raw.bytes
    return parsed

static func _same_snapshot(first: Dictionary, second: Dictionary) -> bool:
    return int(first.revision) == int(second.revision) \
        and JSON.stringify(first.state, "", true, true) == \
            JSON.stringify(second.state, "", true, true)

static func _cleanup_committed_intent(slot_path: String) -> Dictionary:
    if not FileAccess.file_exists(slot_path.path_join(INTENT_FILE)):
        return {"ok": true}
    var pending: Dictionary = _read_intent(slot_path)
    if not pending.ok:
        return pending
    # A missing acknowledgment does not mean the state failed to commit.
    # Only a byte-for-byte equivalent current state can clear the intent.
    var current: Dictionary = read(slot_path)
    if not current.ok or not _same_snapshot(current, pending):
        return {"ok": false, "reason": "intent_requires_reconciliation"}
    if not _write_atomic(slot_path.path_join(RECOVERY_FILE), pending.bytes):
        return {"ok": false, "reason": "recovery_copy_write_failed"}
    if DirAccess.remove_absolute(slot_path.path_join(INTENT_FILE)) != OK:
        return {"ok": false, "reason": "intent_cleanup_failed"}
    return {"ok": true}

static func read(slot_path: String) -> Dictionary:
    if not _valid_slot(slot_path):
        return {"ok": false, "reason": "invalid_slot_path"}
    var raw: Dictionary = _read_bytes(slot_path.path_join(STATE_FILE))
    if not raw.ok:
        return raw
    var result: Dictionary = _decode(raw.bytes)
    if result.ok and result.state.slot_id != slot_path.get_file().trim_prefix("rework-slot-"):
        return {"ok": false, "reason": "slot_mismatch"}
    if result.ok:
        var sealed: Dictionary = _read_fence(slot_path)
        if not sealed.ok:
            return sealed
        if not Fence.allows(result.state, sealed.fence):
            return {"ok": false, "reason": "lifetime_fence_violation"}
        if FileAccess.file_exists(slot_path.path_join(INTENT_FILE)):
            var pending: Dictionary = _read_intent(slot_path)
            if not pending.ok:
                return pending
            if not _same_snapshot(result, pending):
                return {"ok": false, "reason": "intent_requires_reconciliation"}
    return result

static func initialize(slot_path: String, state: Dictionary) -> Dictionary:
    ## Target must not exist: old slots and even corrupt slots are NEVER reset.
    if not _valid_slot(slot_path) or not Model.validate(state)             or state.slot_id != slot_path.get_file().trim_prefix("rework-slot-"):
        return {"ok": false, "reason": "invalid_target"}
    if DirAccess.dir_exists_absolute(slot_path) or FileAccess.file_exists(slot_path):
        return {"ok": false, "reason": "slot_exists"}
    var parent: String = slot_path.get_base_dir()
    if DirAccess.make_dir_recursive_absolute(parent) != OK:
        return {"ok": false, "reason": "parent_failed"}
    var token: String = Crypto.new().generate_random_bytes(16).hex_encode()
    if token.length() != 32:
        return {"ok": false, "reason": "random_failed"}
    var staging: String = slot_path + ".initializing-" + token
    if DirAccess.make_dir_absolute(staging) != OK:
        return {"ok": false, "reason": "staging_failed"}
    var encoded: Dictionary = _encode(state, 1)
    if not encoded.ok:
        DirAccess.remove_absolute(staging)
        return encoded
    if not _write_atomic(staging.path_join(STATE_FILE), encoded.bytes):
        ## A failed staging write is not an initialized save; never touch existing slots.
        DirAccess.remove_absolute(staging)
        return {"ok": false, "reason": "initial_write_failed"}
    # Complete, same-revision recovery copy. The older .bak remains diagnostic
    # only; recovering it could roll back awarded receipts.
    if not _write_atomic(staging.path_join(RECOVERY_FILE), encoded.bytes):
        return {"ok": false, "reason": "initial_recovery_write_failed"}
    var initial_fence: PackedByteArray = Fence.encode(Fence.from_state(state))
    if initial_fence.is_empty() or not _write_atomic(
            staging.path_join(Fence.FILENAME), initial_fence):
        # Leave incomplete staging for offline inspection; the final slot is absent.
        return {"ok": false, "reason": "initial_fence_write_failed"}
    if DirAccess.rename_absolute(staging, slot_path) != OK:
        # Leave the failed staging directory for offline inspection.
        return {"ok": false, "reason": "initial_commit_failed"}
    return read(slot_path)

static func _take_lock(slot_path: String) -> Dictionary:
    var lock_path: String = slot_path + ".write-lock"
    if DirAccess.make_dir_absolute(lock_path) != OK:
        return {"ok": false, "reason": "locked_or_unavailable"}
    var token: String = Crypto.new().generate_random_bytes(16).hex_encode()
    var owner: Dictionary = {
        "format": 1, "pid": OS.get_process_id(), "token": token,
        "created_unix": int(Time.get_unix_time_from_system())
    }
    if token.length() != 32 or not _write_atomic(lock_path.path_join("owner.json"),
            (JSON.stringify(owner) + "\n").to_utf8_buffer()):
        # Preserve ambiguous lock ownership; offline recovery requires explicit consent.
        return {"ok": false, "reason": "lock_owner_failed"}
    return {"ok": true, "token": token}

static func _release_lock(slot_path: String, token: String, require_same_pid: bool = true) -> bool:
    var lock_path: String = slot_path + ".write-lock"
    var raw: Dictionary = _read_bytes(lock_path.path_join("owner.json"))
    if not raw.ok:
        return false
    var parser := JSON.new()
    if parser.parse(raw.bytes.get_string_from_utf8()) != OK             or typeof(parser.data) != TYPE_DICTIONARY             or parser.data.get("token") != token \
            or (require_same_pid and parser.data.get("pid") != OS.get_process_id()):
        return false
    if DirAccess.remove_absolute(lock_path.path_join("owner.json")) != OK:
        return false
    return DirAccess.remove_absolute(lock_path) == OK

static func transact(slot_path: String, transition: Callable,
        expected_revision: int = -1, fault_at: String = "") -> Dictionary:
    ## Fault hooks are for synthetic tests only: before_backup, after_backup,
    ## before_primary, after_primary (ack lost AFTER committed).
    if not _valid_slot(slot_path) or not DirAccess.dir_exists_absolute(slot_path):
        return {"applied": false, "reason": "invalid_slot"}
    var locked: Dictionary = _take_lock(slot_path)
    if not locked.ok:
        return {"applied": false, "reason": locked.reason}
    var result: Dictionary = _transact_locked(slot_path, transition, expected_revision, fault_at)
    if not _release_lock(slot_path, locked.token):
        result["lock_release_failed"] = true
    return result

static func _monotonic_transition(previous: Dictionary, upcoming: Dictionary) -> bool:
    ## Even a future integration caller using Store.transact() directly cannot
    ## replenish clones or silently erase receipts/banked progression.
    if upcoming.generation < previous.generation or upcoming.generation > previous.generation + 1:
        return false
    var before: Variant = previous.vaccine
    var after: Variant = upcoming.vaccine
    if before == null:
        if after == null:
            return upcoming.generation == previous.generation
        return previous.selection_required and upcoming.generation == previous.generation + 1 \
            and after.level == 1 and after.total_xp == 0 \
            and after.earned_fragments == 0 and after.unspent_fragments == 0 \
            and after.copies_remaining == 3 and after.archive.is_empty() \
            and after.carried.is_empty() and after.known_skills.is_empty() \
            and after.receipts.is_empty() and after.closed_runs.is_empty() \
            and after.mission == null
    if upcoming.generation != previous.generation:
        return false
    if after == null:
        return before.copies_remaining == 0 and before.mission != null \
            and upcoming.selection_required
    if after.vaccine_id != before.vaccine_id \
            or after.copies_remaining > before.copies_remaining \
            or after.total_xp < before.total_xp \
            or after.level < before.level \
            or after.earned_fragments < before.earned_fragments:
        return false
    for key in before.receipts:
        if not after.receipts.has(key):
            return false
    for key in before.closed_runs:
        if after.closed_runs.get(key) != before.closed_runs[key]:
            return false
    for skill in before.known_skills:
        if not after.known_skills.has(skill):
            return false
    if before.mission != null and after.mission == null \
            and not after.closed_runs.has(before.mission.run_id):
        return false
    return true

static func _transact_locked(slot_path: String, transition: Callable,
        expected_revision: int, fault_at: String) -> Dictionary:
    var pending_cleanup: Dictionary = _cleanup_committed_intent(slot_path)
    if not pending_cleanup.ok:
        return {"applied": false, "reason": pending_cleanup.reason}
    var loaded: Dictionary = read(slot_path)
    if not loaded.ok:
        return {"applied": false, "reason": loaded.reason}
    if expected_revision >= 0 and loaded.revision != expected_revision:
        return {"applied": false, "reason": "revision_conflict", "revision": loaded.revision}
    if loaded.revision >= 1000000000:
        return {"applied": false, "reason": "revision_exhausted"}
    var changed: Variant = transition.call(loaded.state.duplicate(true))
    if typeof(changed) != TYPE_DICTIONARY or not changed.get("ok", false):
        return {"applied": false, "reason": changed.get("reason", "transition_rejected")             if typeof(changed) == TYPE_DICTIONARY else "transition_rejected"}
    var next_state: Variant = changed.get("state")
    if not Model.validate(next_state) or next_state.slot_id != loaded.state.slot_id             or not _monotonic_transition(loaded.state, next_state):
        return {"applied": false, "reason": "invalid_transition"}
    var encoded: Dictionary = _encode(next_state, loaded.revision + 1)
    if not encoded.ok:
        return {"applied": false, "reason": encoded.reason}
    if fault_at == "before_backup":
        return {"applied": false, "reason": "simulated_before_backup"}
    var old: Dictionary = _read_bytes(slot_path.path_join(STATE_FILE))
    if not old.ok:
        return {"applied": false, "reason": "primary_read_failed"}
    if not _write_atomic(slot_path.path_join(BACKUP_FILE), old.bytes):
        return {"applied": false, "reason": "backup_failed"}
    if fault_at == "after_backup":
        return {"applied": false, "reason": "simulated_before_primary"}
    # The exact intended next state is durable before the irreversible
    # lifetime fence. Recovery can roll FORWARD, never refund a consumed clone.
    if not _write_atomic(slot_path.path_join(INTENT_FILE), encoded.bytes):
        return {"applied": false, "reason": "intent_write_failed"}
    var fence_before: Dictionary = _read_fence(slot_path)
    if not fence_before.ok:
        return {"applied": false, "reason": fence_before.reason}
    var fence_after: Dictionary = Fence.next_fence(loaded.state, next_state, fence_before.fence)
    if fence_after.is_empty():
        return {"applied": false, "reason": "invalid_lifetime_transition"}
    if fence_after != fence_before.fence:
        if not _write_atomic(slot_path.path_join(Fence.FILENAME), Fence.encode(fence_after)):
            return {"applied": false, "reason": "lifetime_fence_write_failed"}
    if fault_at == "before_primary":
        return {"applied": false, "reason": "simulated_before_primary"}
    if not _write_atomic(slot_path.path_join(STATE_FILE), encoded.bytes):
        return {"applied": false, "reason": "primary_write_failed"}
    if fault_at == "after_primary":
        return {"applied": false, "reason": "simulated_ack_lost"}
    var checked: Dictionary = read(slot_path)
    if not checked.ok or int(checked.revision) != int(loaded.revision) + 1:
        return {"applied": false, "reason": "post_write_verification_failed"}
    if not _write_atomic(slot_path.path_join(RECOVERY_FILE), encoded.bytes):
        return {"applied": false, "reason": "committed_recovery_copy_pending"}
    if DirAccess.remove_absolute(slot_path.path_join(INTENT_FILE)) != OK:
        # State is already committed; caller may retry same receipt, which
        # remains in the committed envelope and cannot be awarded twice.
        return {"applied": false, "reason": "committed_intent_cleanup_pending"}
    return {"applied": true, "revision": checked.revision,
        "event": changed.get("event", "updated")}

static func restore_backup(slot_path: String, explicit_consent: bool = false) -> Dictionary:
    ## Refuses rollback of a healthy primary, even if backup has valid older XP.
    ## Operator must stop all game processes; no automatic rollback on load.
    if not explicit_consent or not _valid_slot(slot_path):
        return {"ok": false, "reason": "explicit_consent_required"}
    var locked: Dictionary = _take_lock(slot_path)
    if not locked.ok:
        return locked
    var result: Dictionary = _restore_locked(slot_path)
    if not _release_lock(slot_path, locked.token):
        result["lock_release_failed"] = true
    return result

static func _restore_locked(slot_path: String) -> Dictionary:
    # Pending intent must be reconciled before restoring the primary.
    if FileAccess.file_exists(slot_path.path_join(INTENT_FILE)):
        return {"ok": false, "reason": "intent_requires_reconciliation"}
    var primary: Dictionary = read(slot_path)
    if primary.ok:
        return {"ok": false, "reason": "primary_is_valid"}
    if ["future_format", "future_schema"].has(primary.get("reason", "")):
        return {"ok": false, "reason": "future_version_must_not_rollback"}
    # NEVER silently restore state.json.bak: it is one receipt/revision old.
    # The verified recovery copy has the most recent acknowledged receipts.
    var copy_bytes: Dictionary = _read_bytes(slot_path.path_join(RECOVERY_FILE))
    if not copy_bytes.ok:
        return {"ok": false, "reason": "last_committed_copy_unavailable"}
    var copy: Dictionary = _decode(copy_bytes.bytes)
    if not copy.ok or copy.state.slot_id != slot_path.get_file().trim_prefix("rework-slot-"):
        return {"ok": false, "reason": "last_committed_copy_invalid"}
    var fenced: Dictionary = _read_fence(slot_path)
    if not fenced.ok:
        return fenced
    if not Fence.allows(copy.state, fenced.fence):
        return {"ok": false, "reason": "copy_would_refund_consumed_clone"}
    var damaged: Dictionary = _read_bytes(slot_path.path_join(STATE_FILE))
    if damaged.ok:
        var archive_path: String = slot_path.path_join(
            "state.invalid-" + str(Time.get_ticks_usec()) + ".json")
        if not _write_atomic(archive_path, damaged.bytes):
            return {"ok": false, "reason": "failed_to_preserve_damaged_primary"}
    if not _write_atomic(slot_path.path_join(STATE_FILE), copy_bytes.bytes):
        return {"ok": false, "reason": "restore_write_failed"}
    var confirmed: Dictionary = read(slot_path)
    if not confirmed.ok or not _same_snapshot(confirmed, copy):
        return {"ok": false, "reason": "post_restore_validation_failed"}
    return confirmed

static func recover_interrupted_commit(slot_path: String,
        explicit_consent: bool = false) -> Dictionary:
    ## Offline, opt-in roll-forward. Never recover from Python/legacy paths.
    ## All instances must be stopped by the operator before consent.
    if not explicit_consent or not _valid_slot(slot_path):
        return {"ok": false, "reason": "offline_consent_required"}
    var locked: Dictionary = _take_lock(slot_path)
    if not locked.ok:
        return locked
    var result: Dictionary = _recover_intent_locked(slot_path)
    if not _release_lock(slot_path, locked.token):
        result["lock_release_failed"] = true
    return result

static func _recover_intent_locked(slot_path: String) -> Dictionary:
    var pending: Dictionary = _read_intent(slot_path)
    if not pending.ok:
        return pending
    var saved_backup: Dictionary = _read_bytes(slot_path.path_join(BACKUP_FILE))
    if not saved_backup.ok:
        return {"ok": false, "reason": "intent_parent_backup_missing"}
    var base: Dictionary = _decode(saved_backup.bytes)
    if not base.ok or base.state.slot_id != pending.state.slot_id \
            or int(base.revision) + 1 != int(pending.revision) \
            or not _monotonic_transition(base.state, pending.state):
        return {"ok": false, "reason": "intent_parent_mismatch"}
    var fenced: Dictionary = _read_fence(slot_path)
    if not fenced.ok:
        return fenced
    if not Fence.allows(pending.state, fenced.fence):
        return {"ok": false, "reason": "intent_lifetime_mismatch"}
    var primary_bytes: Dictionary = _read_bytes(slot_path.path_join(STATE_FILE))
    var primary: Dictionary = {}
    if primary_bytes.ok:
        primary = _decode(primary_bytes.bytes)
        if primary.get("reason", "") in ["future_format", "future_schema"]:
            return {"ok": false, "reason": "future_version_must_not_rollback"}
    var already_committed: bool = primary.get("ok", false) \
        and _same_snapshot(primary, pending)
    var prior_valid: bool = primary.get("ok", false) \
        and _same_snapshot(primary, base)
    if not already_committed and primary.get("ok", false) and not prior_valid:
        return {"ok": false, "reason": "intent_primary_conflict"}
    if not already_committed:
        if primary_bytes.ok and not prior_valid:
            var original_path: String = slot_path.path_join(
                "state.invalid-" + str(Time.get_ticks_usec()) + ".json")
            if not _write_atomic(original_path, primary_bytes.bytes):
                return {"ok": false, "reason": "intent_original_preservation_failed"}
        if not _write_atomic(slot_path.path_join(STATE_FILE), pending.bytes):
            return {"ok": false, "reason": "intent_roll_forward_failed"}
    var checked: Dictionary = read(slot_path)
    if not checked.ok or not _same_snapshot(checked, pending):
        return {"ok": false, "reason": "intent_post_recovery_mismatch"}
    if not _write_atomic(slot_path.path_join(RECOVERY_FILE), pending.bytes):
        return {"ok": false, "reason": "recovery_copy_write_failed"}
    if DirAccess.remove_absolute(slot_path.path_join(INTENT_FILE)) != OK:
        return {"ok": false, "reason": "intent_cleanup_failed"}
    return {"ok": true, "revision": checked.revision,
        "recovered": not already_committed}

static func release_orphan_lock_offline(slot_path: String, token: String,
        explicit_all_processes_closed: bool = false) -> Dictionary:
    ## Never silently steal a lock; operator inspects owner.json and confirms
    ## all instances are closed BEFORE calling with its exact owner token.
    if not explicit_all_processes_closed or not _valid_slot(slot_path):
        return {"ok": false, "reason": "offline_consent_required"}
    if not _release_lock(slot_path, token, false):
        return {"ok": false, "reason": "owner_token_mismatch"}
    return {"ok": true}
