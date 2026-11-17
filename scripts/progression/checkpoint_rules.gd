extends RefCounted
## Checkpoint: only committed campaign waves. No bullets or enemies are loaded.
const Store = preload("res://scripts/persistence/save_store.gd")
const Validator = preload("res://scripts/persistence/legacy_validator.gd")
const Progression = preload("res://scripts/progression/antivirus_progression.gd")
const ZONE_BY_SECTOR := {"cpu": "CPU", "memory": "MEMORY", "storage": "STORAGE", "network": "NETWORK"}

static func resume(state: Dictionary) -> Dictionary:
    var cp: Variant = state.get("os_checkpoint.json")
    if cp == null:
        return {"ok": true, "resume_available": false}
    if not Validator.validate("os_checkpoint.json", cp).ok:
        return {"ok": false, "reason": "invalid_checkpoint"}
    var next_waves: Dictionary = {}
    for zone in Validator.ZONES:
        var cleared: int = int(cp.campaign.completed_waves[zone])
        next_waves[zone] = cleared + 1 if cleared < 5 else 0
    return {"ok": true, "resume_available": true,
        "run_id": cp.campaign.run_id, "scene": cp.scene,
        "sector": cp.sector, "next_waves": next_waves,
        "confirmed_completed_waves": cp.campaign.completed_waves.duplicate(true),
        "note": "active_enemies_and_projectiles_are_not_restored"}

static func update_checkpoint(state: Dictionary, snapshot: Dictionary,
        expected_cp_revision: int) -> Dictionary:
    var current: Variant = state.get("os_checkpoint.json")
    var revision: int = int(current.revision) if current != null else 0
    if revision != expected_cp_revision:
        return {"ok": false, "reason": "checkpoint_conflict"}
    var cp: Dictionary = snapshot.duplicate(true)
    cp["schema_version"] = 1
    cp["revision"] = revision + 1
    if not Validator.validate("os_checkpoint.json", cp).ok:
        return {"ok": false, "reason": "invalid_checkpoint"}
    if current != null:
        var previous_run: String = str(current.campaign.run_id)
        if previous_run != str(cp.campaign.run_id):
            var db: Dictionary = state["antivirus_db.json"]
            if not db.closed_runs.has(previous_run):
                return {"ok": false, "reason": "previous_os_not_closed"}
            # A newly deployed OS starts its own monotonic revision history.
            cp.revision = 1
        else:
            for zone in Validator.ZONES:
                var old_wave: int = int(current.campaign.completed_waves[zone])
                var new_wave: int = int(cp.campaign.completed_waves[zone])
                if new_wave < old_wave or new_wave > old_wave + 1:
                    return {"ok": false, "reason": "wave_progress_regressed_or_skipped"}
    var updated: Dictionary = state.duplicate(true)
    updated["os_checkpoint.json"] = cp
    return {"ok": true, "changed": true, "reason": "checkpoint_saved", "state": updated,
        "details": {"checkpoint_revision": cp.revision, "run_id": cp.campaign.run_id}}

static func persist_checkpoint(slot: String, snapshot: Dictionary,
        expected_cp_revision: int) -> Dictionary:
    return Store.transact(slot, func(state): return update_checkpoint(state, snapshot, expected_cp_revision))

static func persist_confirmed_wave(slot: String, snapshot: Dictionary,
        expected_cp_revision: int, run_id: String, sector_id: String, wave_index: int) -> Dictionary:
    ## A single atomic envelope couples the wave's first-clear XP receipt
    ## to the matching checkpoint. Never save one without the other.
    return Store.transact(slot, func(state): return _commit_wave(state, snapshot,
        expected_cp_revision, run_id, sector_id, wave_index))

static func _commit_wave(state: Dictionary, snapshot: Dictionary,
        expected_cp_revision: int, run_id: String, sector_id: String, wave_index: int) -> Dictionary:
    var candidate: Dictionary = snapshot.duplicate(true)
    if not Validator.SECTORS.has(sector_id):
        return {"ok": false, "reason": "invalid_sector"}
    var zones := {"cpu_core": "CPU", "memory_sector": "MEMORY",
        "storage_vault": "STORAGE", "network_grid": "NETWORK"}
    if candidate.get("campaign", {}).get("run_id") != run_id:
        return {"ok": false, "reason": "run_id_mismatch"}
    if int(candidate.get("campaign", {}).get("completed_waves", {}).get(zones[sector_id], -1)) != wave_index:
        return {"ok": false, "reason": "checkpoint_wave_mismatch"}
    var reward: Dictionary = Progression.award_event(state, run_id, "wave_cleared", sector_id, wave_index)
    if not reward.ok:
        return reward
    if not reward.changed and reward.reason != "duplicate":
        return reward
    var after_reward: Dictionary = reward.state if reward.changed else state
    var checkpoint_result: Dictionary = update_checkpoint(after_reward, candidate, expected_cp_revision)
    if not checkpoint_result.ok:
        return checkpoint_result
    checkpoint_result.details["xp_awarded"] = int(reward.details.amount) if reward.changed else 0
    checkpoint_result.details["xp_receipt"] = reward.details.receipt if reward.changed else "duplicate"
    return checkpoint_result

static func clear_after_run(slot: String, expected_cp_revision: int) -> Dictionary:
    return Store.transact(slot, func(state): return _clear_if_closed(state, expected_cp_revision))

static func _clear_if_closed(state: Dictionary, expected_cp_revision: int) -> Dictionary:
    var cp: Variant = state.get("os_checkpoint.json")
    if cp == null:
        return {"ok": true, "changed": false, "reason": "no_checkpoint"}
    if int(cp.revision) != expected_cp_revision:
        return {"ok": false, "reason": "checkpoint_conflict"}
    if not state["antivirus_db.json"].closed_runs.has(str(cp.campaign.run_id)):
        return {"ok": false, "reason": "run_not_closed"}
    var updated: Dictionary = state.duplicate(true)
    updated["os_checkpoint.json"] = null
    return {"ok": true, "changed": true, "reason": "checkpoint_cleared", "state": updated}
