extends RefCounted
## A-owned adapter across C's short sector IDs and E's durable identifiers.
## Only E's Store/Checkpoint/Progression are allowed to touch disk.
## NEVER opens, rewrites or removes any legacy Python user save file.
const Store = preload("res://scripts/persistence/save_store.gd")
const Validator = preload("res://scripts/persistence/legacy_validator.gd")
const Checkpoint = preload("res://scripts/progression/checkpoint_rules.gd")
const Progression = preload("res://scripts/progression/antivirus_progression.gd")
const Campaign = preload("res://scripts/sectors/sector_campaign.gd")

const SECTOR_TO_SAVE := {
    "CPU": "cpu_core", "MEMORY": "memory_sector",
    "STORAGE": "storage_vault", "NETWORK": "network_grid"
}
const ZONE_TO_SCENE := {
    "CPU": "cpu", "MEMORY": "memory",
    "STORAGE": "storage", "NETWORK": "network"
}

static func sector_to_save(sector_id: String) -> String:
    return str(SECTOR_TO_SAVE.get(sector_id.to_upper(), ""))

static func sector_to_scene(sector_id: String) -> String:
    return str(ZONE_TO_SCENE.get(sector_id.to_upper(), ""))

static func safe_slot_path() -> String:
    return ProjectSettings.globalize_path("user://persistence/slot-v1")

static func ensure_slot(slot: String) -> Dictionary:
    ## Existing corrupt/unrecognized slots fail closed. Never silently reset.
    if DirAccess.dir_exists_absolute(slot) or FileAccess.file_exists(slot):
        var existing: Dictionary = Store.read(slot)
        if not existing.get("ok", false):
            return {"ok": false, "reason": "existing_slot_invalid:" + str(existing.get("reason", "unknown"))}
        return {"ok": true, "state": existing["state"], "revision": existing["revision"], "existing": true}
    if DirAccess.make_dir_recursive_absolute(slot.get_base_dir()) != OK:
        return {"ok": false, "reason": "parent_not_writable"}
    var stage: String = slot + ".new-" + str(OS.get_process_id()) + "-" + str(Time.get_ticks_usec())
    if DirAccess.make_dir_absolute(stage) != OK:
        return {"ok": false, "reason": "stage_unavailable"}
    var initialized: Dictionary = Store.initialize_staging(stage, Store.fresh_state())
    if not initialized.get("ok", false):
        DirAccess.remove_absolute(stage.path_join("state.json"))
        DirAccess.remove_absolute(stage)
        return {"ok": false, "reason": "create_failed:" + str(initialized.get("reason", "unknown"))}
    if DirAccess.dir_exists_absolute(slot) or FileAccess.file_exists(slot):
        DirAccess.remove_absolute(stage.path_join("state.json"))
        DirAccess.remove_absolute(stage)
        return {"ok": false, "reason": "concurrent_slot_creation"}
    if DirAccess.rename_absolute(stage, slot) != OK:
        DirAccess.remove_absolute(stage.path_join("state.json"))
        DirAccess.remove_absolute(stage)
        return {"ok": false, "reason": "atomic_slot_commit_failed"}
    var committed: Dictionary = Store.read(slot)
    if not committed.get("ok", false):
        return {"ok": false, "reason": "committed_slot_failed_validation"}
    return {"ok": true, "state": committed["state"], "revision": committed["revision"], "existing": false}

static func make_checkpoint(campaign: RefCounted, actor: Node, combat: RefCounted,
        software: RefCounted, scene_kind: String, hub_position: Vector2,
        cards: Array[String], purify_uses: int, score: int, kills: int,
        boss_kills: int, deployed_level: int, elapsed_frames: int,
        confirmed_wave: int = 0) -> Dictionary:
    var sid: String = str(campaign.active_sector)
    var persisted_scene: String = scene_kind
    var valid_scene: bool = persisted_scene in ["hub", "play", "upgrade", "dead"]
    if not valid_scene:
        return {}
    var sector: Variant = null
    if persisted_scene != "hub":
        sector = sector_to_scene(sid)
        if str(sector).is_empty():
            return {}
    var waves: Dictionary = campaign.progress.duplicate(true)
    if confirmed_wave > 0:
        if sid.is_empty() or confirmed_wave != int(waves.get(sid, -1)) + 1:
            return {}
        waves[sid] = confirmed_wave
    var spot: Vector2 = actor.get("logical_position")
    if persisted_scene == "hub":
        spot = hub_position
    var current_player: Dictionary = combat.get_player_snapshot()
    var snapshot: Dictionary = {
        "schema_version": 1, "revision": 1,
        "campaign": {"run_id": campaign.run_id, "seed": campaign.seed_value,
            "completed_waves": waves},
        "scene": persisted_scene, "sector": sector,
        "hub_position": [hub_position.x, hub_position.y],
        "player_position": [spot.x, spot.y],
        "deployed_level": deployed_level,
        "player": {
            "hp": maxf(0.0, float(actor.get("hp"))),
            "mana": maxf(0.0, float(actor.get("mana"))),
            "charges": int(actor.get("charges")),
            "fire_cd": maxf(0.0, float(current_player.get("fire_cd", 0.0))),
            "pulse_cd": maxf(0.0, float(current_player.get("pulse_cd", 0.0))),
            "guard_cd": maxf(0.0, float(current_player.get("guard_cd", 0.0))),
            "guard_t": maxf(0.0, float(current_player.get("guard", 0.0)))
        },
        "software_levels": software.levels.duplicate(true),
        "pending_cards": cards.duplicate(),
        "infection": {"player_infection": campaign.infection, "os_integrity": campaign.integrity},
        "purify_uses": purify_uses, "salvage_claimed": false,
        "score": score, "kills": kills, "combo": 0,
        "run_kills": kills, "run_boss_kills": boss_kills,
        "run_best_combo": 0, "elapsed_frames": elapsed_frames,
    }
    return snapshot if Validator.validate("os_checkpoint.json", snapshot).ok else {}

static func persist_checkpoint(slot: String, snapshot: Dictionary, expected_rev: int) -> Dictionary:
    if snapshot.is_empty():
        return {"applied": false, "reason": "invalid_snapshot"}
    return Checkpoint.persist_checkpoint(slot, snapshot, expected_rev)

static func persist_wave(slot: String, snapshot: Dictionary,
        expected_rev: int, run_id: String, sector_id: String, wave_index: int) -> Dictionary:
    var sid: String = sector_to_save(sector_id)
    if sid.is_empty() or snapshot.is_empty():
        return {"applied": false, "reason": "invalid_wave_snapshot"}
    return Checkpoint.persist_confirmed_wave(slot, snapshot, expected_rev, run_id, sid, wave_index)

static func persist_boss_and_sector_rewards(slot: String, run_id: String,
        sector_id: String, os_clean: bool = false) -> Dictionary:
    var sid: String = sector_to_save(sector_id)
    if sid.is_empty():
        return {"ok": false, "reason": "unknown_sector"}
    for event in ["boss_killed", "sector_cleared"]:
        var result: Dictionary = Progression.persist_event(slot, run_id, event, sid)
        if not result.get("applied", false) and result.get("reason", "") != "duplicate":
            return {"ok": false, "reason": str(result.get("reason", "write_failed")), "event": event}
    if os_clean:
        var result: Dictionary = Progression.persist_event(slot, run_id, "os_cleaned")
        if not result.get("applied", false) and result.get("reason", "") != "duplicate":
            return {"ok": false, "reason": str(result.get("reason", "write_failed")), "event": "os_cleaned"}
    return {"ok": true}

static func replay_campaign(state: Dictionary) -> RefCounted:
    var cp: Variant = state.get("os_checkpoint.json")
    if cp == null or not Validator.validate("os_checkpoint.json", cp).ok:
        return null
    var restored: RefCounted = Campaign.new(str(cp.campaign.run_id), int(cp.campaign.seed))
    for zone in Validator.ZONES:
        restored.progress[zone] = int(cp.campaign.completed_waves[zone])
        for wave in range(1, int(restored.progress[zone]) + 1):
            restored.receipts["%s:%s:%d" % [restored.run_id, zone, wave]] = true
    restored.infection = float(cp.infection.player_infection)
    restored.integrity = float(cp.infection.os_integrity)
    restored.failed = restored.integrity <= 0.0
    restored.os_clean_emitted = restored.is_os_clean()
    return restored
