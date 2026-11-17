extends RefCounted
## Faithful port of neonvoid.metaprogression and neonvoid.extraction.
## All reward/terminal status changes go through SaveStore.transact and are
## authoritative ONLY after the durable transaction returns applied=true.
const Store = preload("res://scripts/persistence/save_store.gd")
const Validator = preload("res://scripts/persistence/legacy_validator.gd")
const XP_REWARDS := {"wave_cleared": 20, "boss_killed": 75, "sector_cleared": 50, "os_cleaned": 200}

static func base_stats_for_level(level: int) -> Dictionary:
    if level < 1 or level > 30:
        return {}
    var advances := level - 1
    return {"maxhp": float(100 + 2 * advances), "dmg": snappedf(10.0 + 0.2 * advances, 0.000001),
        "armor": snappedf(4.0 + 0.15 * advances, 0.000001), "maxmana": float(100 + advances)}

static func xp_progress(db: Dictionary) -> Dictionary:
    if not Validator.validate("antivirus_db.json", db).ok:
        return {"ok": false, "reason": "invalid_db"}
    var spent := 0
    for level in range(1, int(db.antivirus_level)):
        spent += 100 + 50 * (level - 1)
    if int(db.antivirus_level) == 30:
        return {"ok": true, "current": 0, "required": 0}
    return {"ok": true, "current": int(db.xp) - spent, "required": 100 + 50 * (int(db.antivirus_level) - 1)}

static func _failure(reason: String) -> Dictionary:
    return {"ok": true, "changed": false, "reason": reason}

static func _reject(reason: String) -> Dictionary:
    return {"ok": false, "changed": false, "reason": reason}

static func _receipt(run_id: String, event: String, sector_id: String, wave: int) -> String:
    if event == "os_cleaned":
        return run_id + ":os_cleaned"
    if event == "wave_cleared":
        return run_id + ":" + sector_id + ":wave:" + str(wave)
    return run_id + ":" + sector_id + ":" + event

static func _all_waves(receipts: Dictionary, run_id: String, sector_id: String) -> bool:
    for wave in range(1, 6):
        if not receipts.has(_receipt(run_id, "wave_cleared", sector_id, wave)):
            return false
    return true

static func _all_clean(receipts: Dictionary, run_id: String) -> bool:
    for sid in Validator.SECTORS:
        if not _all_waves(receipts, run_id, sid):
            return false
        if not receipts.has(_receipt(run_id, "boss_killed", sid, 0)):
            return false
        if not receipts.has(_receipt(run_id, "sector_cleared", sid, 0)):
            return false
    return receipts.has(_receipt(run_id, "os_cleaned", "", 0))

static func award_event(state: Dictionary, run_id: String, event: String,
        sector_id: String = "", wave_index: int = 0) -> Dictionary:
    if not Validator._id(run_id, 128) or not XP_REWARDS.has(event):
        return _reject("invalid_event")
    if event == "os_cleaned":
        if sector_id != "" or wave_index != 0:
            return _reject("invalid_event")
    elif not sector_id in Validator.SECTORS:
        return _reject("invalid_sector")
    elif event == "wave_cleared":
        if wave_index < 1 or wave_index > 5:
            return _reject("invalid_wave")
    elif wave_index != 0:
        return _reject("invalid_wave")
    var db: Dictionary = state.get("antivirus_db.json", {})
    if not Validator.validate("antivirus_db.json", db).ok:
        return _reject("invalid_db")
    if db.closed_runs.has(run_id):
        return _failure("run_closed")
    var key := _receipt(run_id, event, sector_id, wave_index)
    if db.awarded_xp_ids.has(key):
        return _failure("duplicate")
    if event == "wave_cleared":
        for w in range(1, wave_index):
            if not db.awarded_xp_ids.has(_receipt(run_id, "wave_cleared", sector_id, w)):
                return _failure("prerequisites_missing")
    elif event == "boss_killed" or event == "sector_cleared":
        if not _all_waves(db.awarded_xp_ids, run_id, sector_id):
            return _failure("prerequisites_missing")
        if event == "sector_cleared" and not db.awarded_xp_ids.has(_receipt(run_id, "boss_killed", sector_id, 0)):
            return _failure("prerequisites_missing")
    elif event == "os_cleaned":
        for sid in Validator.SECTORS:
            if not db.awarded_xp_ids.has(_receipt(run_id, "sector_cleared", sid, 0)):
                return _failure("prerequisites_missing")
    var amount: int = XP_REWARDS[event]
    if float(db.xp) + amount > Validator.MAX_SAFE_INT:
        return _reject("xp_range_unsupported")
    var updated: Dictionary = state.duplicate(true)
    var new_db: Dictionary = updated["antivirus_db.json"]
    var before: int = int(new_db.antivirus_level)
    new_db.xp = int(new_db.xp) + amount
    new_db.antivirus_level = Validator._level(float(new_db.xp))
    new_db.awarded_xp_ids[key] = amount
    if not Validator.validate("antivirus_db.json", new_db).ok:
        return _reject("invalid_transition")
    return {"ok": true, "changed": true, "reason": "granted", "state": updated,
        "details": {"amount": amount, "level_before": before, "level_after": new_db.antivirus_level, "receipt": key}}

static func close_run(state: Dictionary, run_id: String, status: String) -> Dictionary:
    if not Validator._id(run_id, 128) or not status in ["dead", "abandoned"]:
        return _reject("invalid_run_status")
    var db: Dictionary = state.get("antivirus_db.json", {})
    if not Validator.validate("antivirus_db.json", db).ok:
        return _reject("invalid_db")
    if db.closed_runs.has(run_id):
        return _failure("run_closed")
    var updated: Dictionary = state.duplicate(true)
    updated["antivirus_db.json"].closed_runs[run_id] = status
    return {"ok": true, "changed": true, "reason": "run_closed", "state": updated}

static func eligible_for_extraction(state: Dictionary, run_id: String,
        clean_sector_ids: Array, software_levels: Dictionary,
        was_defeated: bool = false, abandoned: bool = false) -> Dictionary:
    if not Validator._id(run_id, 128):
        return _reject("invalid_run_id")
    if not Validator._software_map(software_levels, true):
        return _reject("invalid_software_levels")
    var db: Dictionary = state.get("antivirus_db.json", {})
    if not Validator.validate("antivirus_db.json", db).ok:
        return _reject("invalid_db")
    if db.processed_extractions.has(run_id):
        return _failure("already_extracted")
    if was_defeated or abandoned or db.closed_runs.has(run_id):
        return _failure("run_failed_or_closed")
    for sid in Validator.SECTORS:
        if not clean_sector_ids.has(sid):
            return _failure("sectors_not_clean")
    if not _all_clean(db.awarded_xp_ids, run_id):
        return _failure("sectors_not_clean")
    var eligible: Array = []
    for uid in software_levels:
        if int(software_levels[uid]) >= 1 and int(db.software_archive.get(uid, 0)) < Validator.SOFTWARE_CAPS[uid]:
            eligible.append(uid)
    eligible.sort()
    if eligible.is_empty():
        return _failure("no_software_or_archive_full")
    return {"ok": true, "changed": false, "reason": "ready", "eligible": eligible}

static func extract_once(state: Dictionary, run_id: String, clean_sector_ids: Array,
        software_levels: Dictionary, chosen_id: String,
        was_defeated: bool = false, abandoned: bool = false) -> Dictionary:
    var offer: Dictionary = eligible_for_extraction(state, run_id, clean_sector_ids,
        software_levels, was_defeated, abandoned)
    if not offer.ok or offer.reason != "ready":
        return offer
    if not offer.eligible.has(chosen_id):
        return _failure("invalid_selection")
    var updated: Dictionary = state.duplicate(true)
    var db: Dictionary = updated["antivirus_db.json"]
    db.software_archive[chosen_id] = int(db.software_archive.get(chosen_id, 0)) + 1
    db.processed_extractions[run_id] = chosen_id
    db.closed_runs[run_id] = "extracted"
    if not Validator.validate("antivirus_db.json", db).ok:
        return _reject("invalid_transition")
    return {"ok": true, "changed": true, "reason": "extracted",
        "state": updated, "details": {"software_id": chosen_id}}

static func starting_software(db: Dictionary, selected_id: String = "") -> Dictionary:
    if not Validator.validate("antivirus_db.json", db).ok:
        return {"ok": false, "reason": "invalid_db"}
    if selected_id == "":
        return {"ok": true, "levels": {}}
    if not Validator.SOFTWARE_CAPS.has(selected_id) or int(db.software_archive.get(selected_id, 0)) < 1:
        return {"ok": false, "reason": "not_archived"}
    return {"ok": true, "levels": {selected_id: 1}}

## Public async-safe boundary for C sector events and D UI. Every call reloads
## under the same filesystem lock and never returns applied=true before write.
static func persist_event(slot: String, run_id: String, event: String,
        sector_id: String = "", wave_index: int = 0) -> Dictionary:
    return Store.transact(slot, func(state): return award_event(state, run_id, event, sector_id, wave_index))

static func persist_close_run(slot: String, run_id: String, status: String) -> Dictionary:
    return Store.transact(slot, func(state): return close_run(state, run_id, status))

static func persist_extraction(slot: String, run_id: String, clean_sector_ids: Array,
        software_levels: Dictionary, chosen_id: String,
        was_defeated: bool = false, abandoned: bool = false) -> Dictionary:
    return Store.transact(slot, func(state): return extract_once(state, run_id, clean_sector_ids,
        software_levels, chosen_id, was_defeated, abandoned))

static func load_database(slot: String) -> Dictionary:
    var loaded: Dictionary = Store.read(slot)
    if not loaded.ok:
        return loaded
    return {"ok": true, "db": loaded.state["antivirus_db.json"], "revision": loaded.revision}
