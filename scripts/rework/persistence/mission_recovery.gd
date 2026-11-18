extends RefCounted
## Read-only authoritative slot / receipt reconciliation for A and C.
## No automatic mission resume, cancellation, reroll, clone refund or save repair.
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")
const Model = preload("res://scripts/rework/progression/state_model.gd")
const Checkpoint = preload("res://scripts/rework/progression/mission_checkpoint.gd")
const RareBonus = preload("res://scripts/rework/progression/rare_bonus.gd")
const Observation = preload("res://scripts/rework/progression/combat_observation.gd")

static func _blocked(reason: String, revision: int = -1) -> Dictionary:
    return {"ok": false, "revision": revision, "reason": reason,
        "safe_to_resume": false, "requires_user_action": true,
        "reconciliation_complete": false}


## User-facing metadata only. Never expose recovery bytes or lock owner tokens.
## A must not infer safe continuation merely from "primary valid": maps and
## card RNG are NOT complete B/D combat state.
static func diagnose_slot(slot_path: String) -> Dictionary:
    var result: Dictionary = {
        "ok": false, "reason": "unverified", "revision": -1,
        "state": "unreadable", "can_issue_command": false,
        "safe_to_resume": false, "requires_user_action": true,
        "automatic_repair_permitted": false, "recovery_policy_approved": false,
        "lock_present": false, "lock_owner_status": "absent",
        "files": {}, "fence_status": "unverified",
        "offline_recovery_hint": "inspect_manually"
    }
    if not Store._valid_slot(slot_path):
        result.reason = "invalid_slot_path"
        return result
    var files: Dictionary = {
        "primary": Store.STATE_FILE,
        "recovery": Store.RECOVERY_FILE,
        "backup": Store.BACKUP_FILE,
        "intent": Store.INTENT_FILE
    }
    for kind in files:
        var bytes_result: Dictionary = Store._read_bytes(slot_path.path_join(files[kind]))
        if not bytes_result.get("ok", false):
            result.files[kind] = {
                "status": str(bytes_result.get("reason", "read_failed")),
                "revision": -1
            }
            continue
        var decoded: Dictionary = Store._decode(bytes_result.bytes)
        if not decoded.get("ok", false):
            result.files[kind] = {
                "status": str(decoded.get("reason", "invalid_envelope")),
                "revision": -1
            }
            continue
        if str(decoded.state.slot_id) != slot_path.get_file().trim_prefix("rework-slot-"):
            result.files[kind] = {"status": "slot_mismatch", "revision": -1}
            continue
        result.files[kind] = {"status": "valid", "revision": int(decoded.revision)}
    var fence: Dictionary = Store._read_fence(slot_path)
    result.fence_status = "valid" if fence.get("ok", false)         else str(fence.get("reason", "fence_unreadable"))
    var lock_path: String = slot_path + ".write-lock"
    result.lock_present = DirAccess.dir_exists_absolute(lock_path)
    if result.lock_present:
        result.lock_owner_status = "missing_or_unreadable"
        var lock_bytes: Dictionary = Store._read_bytes(lock_path.path_join("owner.json"))
        if lock_bytes.get("ok", false):
            var parsed := JSON.new()
            var owner_text: String = lock_bytes.bytes.get_string_from_utf8()
            if owner_text.to_utf8_buffer() == lock_bytes.bytes                     and parsed.parse(owner_text) == OK                     and typeof(parsed.data) == TYPE_DICTIONARY:
                var owner: Dictionary = parsed.data
                if Model.natural(owner.get("pid"), 2147483647)                         and Model.natural(owner.get("created_unix"))                         and typeof(owner.get("token")) == TYPE_STRING                         and str(owner.token).length() == 32:
                    result.lock_owner_status = "metadata_valid_process_not_verified"
    var loaded: Dictionary = Store.read(slot_path)
    if not loaded.get("ok", false):
        result.reason = str(loaded.get("reason", "save_unreadable"))
        if result.lock_present:
            result.offline_recovery_hint = "inspect_lock_and_save_offline"
        elif result.files.intent.status != "not_found":
            result.offline_recovery_hint = "reconcile_intent_offline_with_consent"
        elif result.files.recovery.status == "valid":
            result.offline_recovery_hint = "inspect_recovery_copy_offline_with_consent"
        return result
    result.ok = true
    result.revision = int(loaded.revision)
    result.reason = "slot_verified"
    result.offline_recovery_hint = "none"
    var mission: Variant = loaded.state.vaccine.get("mission")         if loaded.state.vaccine != null else null
    if loaded.state.vaccine == null:
        result.state = "vaccine_selection"
        result.reason = "vaccine_selection_required"
    elif mission != null:
        result.state = "active_mission_blocked"
        result.reason = str(inspect_slot(slot_path).get("reason",
            "resume_or_cancel_policy_unapproved"))
    else:
        result.state = "hub"
    if result.lock_present:
        result.reason = "lock_present_requires_offline_verification"
        result.offline_recovery_hint = "inspect_lock_offline"
    elif result.files.intent.status != "not_found":
        result.reason = "committed_intent_cleanup_pending"
        result.offline_recovery_hint = "inspect_intent_offline"
    elif result.state == "hub":
        result.can_issue_command = true
        result.requires_user_action = false
    return result

static func inspect_slot(slot_path: String) -> Dictionary:
    var loaded: Dictionary = Store.read(slot_path)
    if not loaded.get("ok", false):
        return _blocked(str(loaded.get("reason", "save_unreadable")))
    var data: Dictionary = loaded.state
    var response: Dictionary = {
        "ok": true, "revision": loaded.revision, "slot_id": data.slot_id,
        "generation": data.generation, "state": "hub",
        "snapshot": data.duplicate(true),
        "safe_to_resume": true, "requires_user_action": false,
        "reason": "hub_ready", "technically_restorable": false,
        "mission_snapshot": null, "reward_snapshot": null,
        "terminal_snapshot": null, "checkpoint_present": false,
        "map_offer_checkpoint_ready": false,
        "combat_runtime_checkpoint_present": false,
        "combat_observation_snapshot": null
    }
    if data.vaccine == null:
        response.state = "vaccine_selection"
        response.reason = "vaccine_destroyed_selection_required"
        response.requires_user_action = true
        response.safe_to_resume = false
        return response
    var mission: Variant = data.vaccine.mission
    if mission == null:
        return response
    response.state = "active_mission_blocked"
    response.safe_to_resume = false
    response.requires_user_action = true
    response.mission_snapshot = mission.duplicate(true)
    response.combat_runtime_checkpoint_present = mission.environment_rolls.has(Observation.KEY)
    if response.combat_runtime_checkpoint_present:
        response.combat_observation_snapshot = mission.environment_rolls[Observation.KEY].duplicate(true)
    var checkpoint: Dictionary = Checkpoint.view(mission)
    if not checkpoint.get("ok", false):
        response.reason = "invalid_checkpoint"
        return response
    response.checkpoint_present = checkpoint.checkpoint_present
    var cp: Dictionary = checkpoint.checkpoint
    response.reward_snapshot = {
        "offer": cp.active_offer,
        "offer_sequence": cp.offer_sequence,
        "rerolls_left": mission.rerolls_left,
        "pity_counter": mission.pity_counter,
        "rng_state_text": cp.rng_state_text,
        "pending_offer_count": mission.pending_plugin_offers,
        "claimed_offer_ids": mission.claimed_offer_ids.duplicate(true),
        "obtained_plugins": mission.obtained_plugins.duplicate(true),
        "rare_grants": mission.environment_rolls.get(RareBonus.KEY, {}).duplicate(true),
        "pending_vaccine_xp": mission.uncommitted_vaccine_xp,
        "pending_point_fragments": mission.uncommitted_point_fragments
    }
    response.terminal_snapshot = cp.terminal_pending
    response.map_offer_checkpoint_ready = checkpoint.checkpoint_present \
        and checkpoint.has_rng_cursor and checkpoint.regions_saved > 0 \
        and cp.terminal_pending == null
    # Maps/cards are NOT full B/D combat state. Never signal battle restoration.
    response.technically_restorable = false
    if not checkpoint.checkpoint_present:
        response.reason = "checkpoint_missing_requires_approved_policy"
    elif cp.terminal_pending != null:
        response.reason = "terminal_intent_requires_reconciliation"
    elif cp.active_offer != null and cp.rng_state_text.is_empty():
        response.reason = "rng_cursor_missing_cannot_reroll"
    elif cp.regions.is_empty():
        response.reason = "map_generator_checkpoint_missing"
    else:
        # Even complete data must not authorize a resume policy on its own.
        response.reason = "resume_or_cancel_policy_unapproved"
    return response

static func reconcile_receipt(slot_path: String, vaccine_id: String,
        generation: int, run_id: String, receipt_id: String,
        offer_id: String = "") -> Dictionary:
    var result: Dictionary = {
        "ok": false, "reason": "unverified", "revision": -1,
        "receipt_committed": false, "receipt_definitely_absent": false,
        "reconciliation_complete": false, "requires_user_action": true,
        "can_retry_same_receipt": false
    }
    if not Model.valid_id(vaccine_id) or not Model.natural(generation, 1000000000) \
            or not Model.valid_id(run_id) or not Model.valid_id(receipt_id) \
            or (not offer_id.is_empty() and not Model.valid_id(offer_id)):
        result.reason = "invalid_reconciliation_identity"
        return result
    var loaded: Dictionary = Store.read(slot_path)
    if not loaded.get("ok", false):
        result.reason = str(loaded.get("reason", "save_unreadable"))
        return result
    result.revision = loaded.revision
    var state: Dictionary = loaded.state
    if int(state.generation) != generation or state.vaccine == null \
            or state.vaccine.vaccine_id != vaccine_id:
        result.reason = "stale_generation_or_destroyed_vaccine"
        return result
    var vaccine: Dictionary = state.vaccine
    if vaccine.receipts.has(receipt_id):
        result.ok = true
        result.reason = "receipt_committed"
        result.receipt_committed = true
        result.reconciliation_complete = true
        result.requires_user_action = false
        return result
    # A different slot might already have won the same 3-card offer.
    if not offer_id.is_empty() and vaccine.mission != null \
            and vaccine.mission.claimed_offer_ids.has(offer_id):
        result.reason = "offer_claimed_with_different_receipt"
        return result
    if vaccine.closed_runs.has(run_id) or vaccine.mission == null \
            or vaccine.mission.run_id != run_id:
        result.reason = "run_closed_or_not_active"
        return result
    # Store.read() verified checksum, primary, intent consistency and fence.
    # A missing receipt is authoritative only for the current live generation.
    result.ok = true
    result.reason = "receipt_not_committed"
    result.receipt_definitely_absent = true
    result.reconciliation_complete = true
    result.requires_user_action = false
    result.can_retry_same_receipt = true
    return result


static func reconcile_rare_grant(slot_path: String, vaccine_id: String,
        generation: int, run_id: String, source_receipt: String,
        offer_id: String = "") -> Dictionary:
    ## Stronger than a generic receipt lookup: require an active matching run,
    ## both durable receipts AND the immutable rare grant record. Never retries.
    var answer: Dictionary = {
        "ok": false, "reason": "unverified", "revision": -1,
        "pair_verified": false, "receipt_committed": false,
        "receipt_definitely_absent": false, "reconciliation_complete": false,
        "requires_user_action": true, "can_retry_same_receipt": false,
        "grant": {}
    }
    if not Model.valid_id(vaccine_id) or not Model.natural(generation, 1000000000) \
            or not Model.valid_id(run_id) or not Model.valid_id(source_receipt) \
            or (not offer_id.is_empty() and not Model.valid_id(offer_id)):
        answer.reason = "invalid_rare_reconciliation_identity"
        return answer
    var loaded: Dictionary = Store.read(slot_path)
    if not loaded.get("ok", false):
        answer.reason = str(loaded.get("reason", "save_unreadable"))
        return answer
    answer.revision = int(loaded.revision)
    var state: Dictionary = loaded.state
    if int(state.generation) != generation or state.vaccine == null \
            or str(state.vaccine.vaccine_id) != vaccine_id:
        answer.reason = "stale_generation_or_destroyed_vaccine"
        return answer
    var vaccine: Dictionary = state.vaccine
    if vaccine.mission == null or str(vaccine.mission.run_id) != run_id:
        answer.reason = "rare_grant_run_not_active_or_closed"
        return answer
    var mission: Dictionary = vaccine.mission
    if not vaccine.receipts.has(source_receipt):
        if not offer_id.is_empty() and mission.claimed_offer_ids.has(offer_id):
            answer.reason = "offer_already_claimed"
            return answer
        answer.ok = true
        answer.reason = "source_receipt_definitely_absent"
        answer.receipt_definitely_absent = true
        answer.reconciliation_complete = true
        # An absent receipt is NOT authorization for an automatic reroll.
        return answer
    var proof: Dictionary = RareBonus.verify_committed_pair(
        mission, vaccine.receipts, source_receipt)
    if not proof.get("ok", false):
        answer.reason = str(proof.get("reason", "rare_pair_unverified"))
        return answer
    if not offer_id.is_empty() and not mission.claimed_offer_ids.has(offer_id):
        answer.reason = "source_offer_not_claimed"
        return answer
    answer.ok = true
    answer.reason = "rare_pair_committed"
    answer.pair_verified = true
    answer.receipt_committed = true
    answer.reconciliation_complete = true
    answer.requires_user_action = false
    answer.grant = proof.duplicate(true)
    return answer
