extends SceneTree
## Fault injection uses unique user:// synthetic directory only. No real user saves.
const Model = preload("res://scripts/rework/progression/state_model.gd")
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")
const Gateway = preload("res://scripts/rework/persistence/progression_gateway.gd")
const SkillOffer = preload("res://scripts/rework/skills/skill_offer.gd")
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
        original[name] = _bytes(path).hex_encode().sha256_text()

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
    if not started.get("applied", false) or not Store.read(slot).get("ok", false):
        printerr("REWORK E: abort on failed initial transaction")
        _remove_tree(root_dir)
        quit(1)
        return
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
    check(not FileAccess.file_exists(slot.path_join("state.json.intent")),
        "replay after lost ACK safely removes an already-committed intent")

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
        check(_bytes(legacy.path_join(name)).hex_encode().sha256_text() == original[name],
            "legacy synthetic file unchanged: " + name)

    var current: PackedByteArray = _bytes(slot.path_join("state.json"))
    _write(slot.path_join("state.json"), "{bad json")
    check(Store.read(slot).reason == "invalid_json", "corrupt primary rejected")
    check(not Gateway.transact_command(slot, event).applied, "corruption fails closed")
    check(Store.initialize(slot, state).reason == "slot_exists",
        "corrupt existing slot must not silently reset")
    check(not Store.restore_backup(slot).ok, "recovery needs explicit consent")
    var recovered: Dictionary = Store.restore_backup(slot, true)
    check(recovered.ok and recovered.revision == saved.revision,
        "explicit recovery restores latest committed revision, not previous rollback")
    check(FileAccess.file_exists(slot.path_join("state.json.bak")),
        "backup remains available after recovery")
    check(_bytes(slot.path_join("state.json")) == current,
        "latest committed recovery copy matches the original primary byte-for-byte")

    # Hard storage invariants hold even if a future caller bypasses the gateway.
    var lower_copies: Callable = func(next: Dictionary) -> Dictionary:
        next.vaccine.copies_remaining = 2
        return {"ok": true, "state": next}
    check(Store.transact(slot, lower_copies).reason == "invalid_transition",
        "clone decrement without closed death run is refused")
    check(Gateway.transact_command(slot, {
        "action": "die", "vaccine_id": "vaccine-1", "generation": 1,
        "run_id": "run-a", "receipt_id": "unit-death"
    }).applied, "one death consumes exactly one clone transactionally")
    var recharge: Callable = func(next: Dictionary) -> Dictionary:
        next.vaccine.copies_remaining = 3
        return {"ok": true, "state": next}
    check(Store.transact(slot, recharge).reason == "invalid_transition",
        "save boundary blocks clone restoration even by direct writer")
    check(not Gateway.transact_command(slot, event).applied,
        "restored committed receipt cannot be granted again")
    var drop_receipt: Callable = func(next: Dictionary) -> Dictionary:
        next.vaccine.receipts.clear()
        return {"ok": true, "state": next}
    check(Store.transact(slot, drop_receipt).reason == "invalid_transition",
        "save boundary disallows removal of committed receipt ledger")
    check(not Gateway.transact_command(slot, event).applied, "receipt remains exactly once")

    # The previous mission ended with death; use a NEW live run to exercise
    # the actual backup write failure instead of receiving run_not_active.
    check(Gateway.transact_command(slot, {
        "action": "start_mission", "vaccine_id": "vaccine-1",
        "generation": 1, "run_id": "run-after-death", "seed": 909
    }).applied, "fresh mission for backup permission failure test")

    var backup_name: String = slot.path_join("state.json.bak")
    var parked_backup: String = slot.path_join("state.json.bak.synthetic-held")
    var prior_revision: int = int(Store.read(slot).revision)
    if DirAccess.rename_absolute(backup_name, parked_backup) == OK:
        check(DirAccess.make_dir_absolute(backup_name) == OK,
            "unwritable backup destination fault injected")
        var fresh_event: Dictionary = event.duplicate(true)
        fresh_event.receipt_id = "reward-should-not-commit"
        fresh_event.run_id = "run-after-death"
        var blocked: Dictionary = Gateway.transact_command(slot, fresh_event)
        check(not blocked.applied and blocked.reason == "backup_failed",
            "backup write failure blocks primary commit")
        check(Store.read(slot).revision == prior_revision,
            "unwritable backup preserves primary revision")
        DirAccess.remove_absolute(backup_name)
        check(DirAccess.rename_absolute(parked_backup, backup_name) == OK,
            "restore prior synthetic backup")
    else:
        check(false, "backup file should exist for failure injection")

    var future_state: Dictionary = Store.read(slot).state.duplicate(true)
    future_state.schema_version = 2
    var future_payload: String = JSON.stringify(future_state, "", true, true)
    var future_env := {
        "format": 1, "revision": 999,
        "payload_sha256": future_payload.sha256_text(),
        "payload_json": future_payload
    }
    _write(slot.path_join("state.json"), JSON.stringify(future_env))
    check(Store.read(slot).reason == "future_schema",
        "well-formed checksummed future schema rejected")
    check(Store.restore_backup(slot, true).reason == "future_version_must_not_rollback",
        "future schema cannot be downgraded")
    future_env.payload_sha256 = "0000000000000000000000000000000000000000000000000000000000000000"
    _write(slot.path_join("state.json"), JSON.stringify(future_env))
    check(Store.read(slot).reason == "checksum_mismatch", "tampered payload denied")
    check(not Gateway.transact_command(slot, event).applied,
        "tampered checksum cannot accept writes")

    var second_slot: String = root_dir.path_join("rework-slot-bad-backup")
    check(Store.initialize(second_slot, Model.new_slot("bad-backup", "test-vaccine")).ok,
        "second isolated synthetic slot")
    var another: Dictionary = command.duplicate(true)
    another.vaccine_id = "test-vaccine"
    check(Gateway.transact_command(second_slot, another).applied,
        "second slot write creates safe backup")
    _write(second_slot.path_join("state.json.bak"), "{invalid backup")
    _write(second_slot.path_join("state.json.recovery"), "{invalid latest copy")
    _write(second_slot.path_join("state.json"), "{invalid primary")
    check(not Store.restore_backup(second_slot, true).ok,
        "damaged latest copy and backup cannot silently roll back receipts")
    check(not Store.initialize(second_slot, Model.new_slot("bad-backup", "again")).ok,
        "corrupt existing slot cannot be overwritten")

    _write(slot.path_join("state.json"), "{\"format\":99}")
    check(Store.read(slot).reason == "future_format", "future save format denied")
    check(Store.restore_backup(slot, true).reason == "future_version_must_not_rollback",
        "future format must never silently downgrade from backup")
    check(not Gateway.transact_command(slot, event).applied,
        "unsupported format never accepts writes")

    # Stat purchases must remain valid after JSON parsing turns whole numbers into floats.
    var stat_slot: String = root_dir.path_join("rework-slot-stat-roundtrip")
    check(Store.initialize(stat_slot, Model.new_slot("stat-roundtrip", "stat-vaccine")).ok,
        "isolated stat validation slot")
    var stat_start: Dictionary = command.duplicate(true)
    stat_start.vaccine_id = "stat-vaccine"
    check(Gateway.transact_command(stat_slot, stat_start).applied,
        "stat-roundtrip mission starts")
    var stat_cache: Dictionary = event.duplicate(true)
    stat_cache.vaccine_id = "stat-vaccine"
    stat_cache.fragments = 100
    check(Gateway.transact_command(stat_slot, stat_cache).applied,
        "stat-roundtrip fragments earned")
    check(Gateway.transact_command(stat_slot, {
        "action": "hardware_cleaned", "vaccine_id": "stat-vaccine",
        "generation": 1, "run_id": "run-a",
        "receipt_id": "world:run-a:CPU:clean", "hardware_id": "CPU"
    }).applied, "stat-roundtrip extraction enabled")
    check(Gateway.transact_command(stat_slot, {
        "action": "extract", "vaccine_id": "stat-vaccine",
        "generation": 1, "run_id": "run-a",
        "receipt_id": "world:run-a:extraction", "learn_plugin_ids": []
    }).applied, "stat-roundtrip fragments committed")
    check(Gateway.transact_command(stat_slot, {
        "action": "reallocate_stats", "vaccine_id": "stat-vaccine",
        "generation": 1, "receipt_id": "stat-allocation", "allocations": {"max_hp": 1}
    }).applied, "stat purchase persists successfully")
    var stat_reloaded: Dictionary = Store.read(stat_slot)
    check(stat_reloaded.get("ok", false), "stat purchase can be reloaded after JSON parse")
    if stat_reloaded.get("ok", false):
        check(stat_reloaded.state.vaccine.invested_stats.max_hp == 1,
            "purchased stat survives restart")
        check(stat_reloaded.state.vaccine.unspent_fragments == 100,
            "stat purchase consumes 100 fragments; vaccine level grants 100 more")

    # A crash leaves a lock that must be released only with offline approval
    # and the exact owner token; the previous process PID cannot be the validator.
    var lock_owner: Dictionary = Store._take_lock(slot)
    check(lock_owner.get("ok", false), "synthetic orphan lock acquired")
    if lock_owner.get("ok", false):
        var lock_owner_path: String = slot + ".write-lock/owner.json"
        var lock_meta: Dictionary = JSON.parse_string(_bytes(lock_owner_path).get_string_from_utf8())
        lock_meta.pid = 123456789
        _write(lock_owner_path, JSON.stringify(lock_meta))
        check(not Store._release_lock(slot, lock_owner.token),
            "normal lock release rejects foreign process PID")
        check(Store.release_orphan_lock_offline(slot, lock_owner.token, true).ok,
            "offline token-authorized orphan lock recovery accepts old PID")

    var recovery_fault_slot: String = root_dir.path_join("rework-slot-copy-failure")
    check(Store.initialize(recovery_fault_slot,
        Model.new_slot("copy-failure", "vaccine-1")).ok,
        "initialize recovery-copy failure test slot")
    check(Gateway.transact_command(recovery_fault_slot, command).applied,
        "recovery-copy failure test mission started")
    var latest_path: String = recovery_fault_slot.path_join("state.json.recovery")
    var held_latest: String = latest_path + ".synthetic-held"
    if DirAccess.rename_absolute(latest_path, held_latest) == OK:
        check(DirAccess.make_dir_absolute(latest_path) == OK,
            "inject unwritable latest-copy destination")
        var lost_ack: Dictionary = Gateway.transact_command(recovery_fault_slot, event)
        check(not lost_ack.applied and
            lost_ack.reason == "committed_recovery_copy_pending",
            "primary already committed but recovery-copy failure not acknowledged")
        var recoverable: Dictionary = Store.read(recovery_fault_slot)
        check(recoverable.ok and
            recoverable.state.vaccine.mission.uncommitted_vaccine_xp == 100,
            "pending intent keeps the committed reward visible exactly once")
        DirAccess.remove_absolute(latest_path)
        check(DirAccess.rename_absolute(held_latest, latest_path) == OK,
            "restore original synthetic latest-copy path")
        check(not Gateway.transact_command(recovery_fault_slot, event).applied,
            "ack-lost retry reconciles copy without duplicating receipt")
        check(not FileAccess.file_exists(
            recovery_fault_slot.path_join("state.json.intent")),
            "after recovery-copy repair the pending intent is removed")
        var durable_copy: Dictionary = Store._decode(
            _bytes(recovery_fault_slot.path_join("state.json.recovery")))
        check(durable_copy.ok and durable_copy.state.vaccine.mission.uncommitted_vaccine_xp == 100,
            "latest committed copy carries the correct receipt balance")
    else:
        check(false, "latest committed copy should exist for fault injection")

    var irreversible: String = root_dir.path_join("rework-slot-irreversible")
    check(Store.initialize(irreversible, Model.new_slot("irreversible", "vaccine-1")).ok,
        "initialize fenced clone test slot")
    check(Gateway.transact_command(irreversible, command).applied,
        "fenced test has active mission")
    var death_cmd := {
        "action": "die", "vaccine_id": "vaccine-1", "generation": 1,
        "run_id": "run-a", "receipt_id": "death-finality"
    }
    check(Gateway.transact_command(irreversible, death_cmd).applied,
        "first death and lifetime fence commit")
    check(Store.read(irreversible).state.vaccine.copies_remaining == 2,
        "first spent clone persisted")
    _write(irreversible.path_join("state.json"), "{damaged after clone used")
    var safe_death_restore: Dictionary = Store.restore_backup(irreversible, true)
    check(safe_death_restore.get("ok", false),
        "latest committed recovery restores death without refunding a clone")
    if safe_death_restore.get("ok", false):
        check(safe_death_restore.state.vaccine.copies_remaining == 2,
            "latest recovery copy retains consumed clone")
        check(safe_death_restore.state.vaccine.receipts.has("death-finality"),
            "latest recovery copy retains death receipt")

    var interrupted: String = root_dir.path_join("rework-slot-interrupted")
    check(Store.initialize(interrupted, Model.new_slot("interrupted", "vaccine-1")).ok,
        "initialize interrupted clone test slot")
    check(Gateway.transact_command(interrupted, command).applied,
        "interrupted slot has active mission")
    check(not Gateway.transact_command(interrupted, death_cmd, -1, "before_primary").applied,
        "simulate interruption after clone fence, before primary")
    check(Store.read(interrupted).reason == "lifetime_fence_violation",
        "interrupted irreversible transition fails closed, never refunds clone")
    check(Store.restore_backup(interrupted, true).reason ==
        "intent_requires_reconciliation",
        "never roll back a pending clone-consuming transaction from old backup")
    check(not Store.recover_interrupted_commit(interrupted).ok,
        "interrupted commit recovery requires explicit offline consent")
    var rolled_forward: Dictionary = Store.recover_interrupted_commit(interrupted, true)
    check(rolled_forward.get("ok", false),
        "verified write-ahead death intent rolls forward without refunding a clone")
    if rolled_forward.get("ok", false):
        var after_roll_forward: Dictionary = Store.read(interrupted)
        check(after_roll_forward.ok and after_roll_forward.state.vaccine.copies_remaining == 2,
            "recovered death leaves exactly two clones")
        check(not Gateway.transact_command(interrupted, death_cmd).applied,
            "replaying death after recovery never consumes another copy")
        check(not FileAccess.file_exists(interrupted.path_join("state.json.intent")),
            "recovery cleans only its validated intent file")

    var reward_intent_slot: String = root_dir.path_join("rework-slot-reward-intent")
    check(Store.initialize(reward_intent_slot,
        Model.new_slot("reward-intent", "vaccine-1")).ok,
        "isolated non-death intent slot")
    check(Gateway.transact_command(reward_intent_slot, command).applied,
        "reward intent mission started")
    check(not Gateway.transact_command(reward_intent_slot, event,
        -1, "before_primary").applied,
        "simulate non-death reward crash after intent, before primary")
    check(Store.read(reward_intent_slot).reason == "intent_requires_reconciliation",
        "no stale XP snapshot exposed while an uncommitted receipt is pending")
    check(not Gateway.transact_command(reward_intent_slot, event).applied,
        "new awards blocked until interrupted reward intent is reconciled")
    check(Store.restore_backup(reward_intent_slot, true).reason ==
        "intent_requires_reconciliation",
        "backup cannot erase an interrupted reward receipt")
    var restored_reward: Dictionary = Store.recover_interrupted_commit(
        reward_intent_slot, true)
    check(restored_reward.get("ok", false),
        "pending reward is promoted atomically with its original receipt")
    if restored_reward.get("ok", false):
        var reward_loaded: Dictionary = Store.read(reward_intent_slot)
        check(reward_loaded.ok and
            reward_loaded.state.vaccine.mission.uncommitted_vaccine_xp == 100,
            "restored intent contains exact pre-crash reward")
        check(not Gateway.transact_command(reward_intent_slot, event).applied,
            "reward receipt replay is refused after intentional roll-forward")

    _test_mission_checkpoint(root_dir)
    _test_rare_pair_recovery(root_dir)
    _test_combat_observation(root_dir)
    _test_s01_diagnostics(root_dir)
    _test_s01_durable_normal_flow(root_dir)

    # All files cleaned are in this uniquely named synthetic root only.
    _remove_tree(root_dir)
    if failures == 0:
        print("REWORK E PERSISTENCE PASS: synthetic atomicity, restart, recovery and isolation")
    quit(1 if failures > 0 else 0)


func _test_s01_diagnostics(root_dir: String) -> void:
    # Synthetic fixtures ONLY. The real normal/QA slot IDs are never opened.
    var production_path: String = Store.default_slot_path("nv-rework-main-v1")
    var qa_path: String = Store.default_slot_path("nv-rework-qa-20261118")
    check(not production_path.is_empty() and not qa_path.is_empty() \
        and production_path != qa_path,
        "production and QA scene IDs resolve to physically distinct directories")
    var normal: String = root_dir.path_join("rework-slot-s01-normal")
    var qa: String = root_dir.path_join("rework-slot-s01-qa")
    check(Store.initialize(normal, Model.new_slot("s01-normal", "normal-vaccine")).ok,
        "fresh normal slot initialized without synthetic QA bonuses")
    var qa_state: Dictionary = Model.new_slot("s01-qa", "qa-vaccine")
    qa_state.vaccine.earned_fragments = 500
    qa_state.vaccine.unspent_fragments = 500
    qa_state.vaccine.known_skills = ["nv_skill_arc_pulse"]
    qa_state.vaccine.equipped_skills = ["nv_skill_arc_pulse"]
    check(Store.initialize(qa, qa_state).ok, "separate synthetic QA slot created")
    var pristine: Dictionary = Store.read(normal)
    check(pristine.ok and pristine.state.vaccine.level == 1         and pristine.state.vaccine.xp == 0         and pristine.state.vaccine.unspent_fragments == 0         and pristine.state.vaccine.known_skills.is_empty()         and pristine.state.vaccine.carried.is_empty()         and pristine.state.vaccine.archive.is_empty(),
        "normal player starts with no QA stats, plugins or skills")
    var ready: Dictionary = Gateway.diagnose_slot(normal)
    check(ready.ok and ready.state == "hub" and ready.can_issue_command         and not ready.automatic_repair_permitted         and ready.files.primary.status == "valid",
        "read-only diagnostic exposes clean hub without authorizing auto recovery")
    var prior_bytes: PackedByteArray = _bytes(normal.path_join("state.json"))
    var again: Dictionary = Gateway.diagnose_slot(normal)
    check(again.ok and _bytes(normal.path_join("state.json")) == prior_bytes,
        "diagnose_slot is byte-for-byte read-only")
    var owner: Dictionary = Store._take_lock(normal)
    check(owner.get("ok", false), "synthetic diagnostic lock created")
    if owner.get("ok", false):
        var locked: Dictionary = Gateway.diagnose_slot(normal)
        check(locked.ok and locked.lock_present and not locked.can_issue_command             and locked.lock_owner_status == "metadata_valid_process_not_verified",
            "lock diagnostic never steals an apparently stale process lock")
        check(Store._release_lock(normal, owner.token), "release synthetic lock")
    var start: Dictionary = Gateway.transact_command(normal, {
        "action": "start_mission", "vaccine_id": "normal-vaccine",
        "generation": 1, "run_id": "s01-mission", "seed": 42
    })
    check(start.applied, "S01 fresh normal mission started")
    var interrupted: Dictionary = Gateway.diagnose_slot(normal)
    check(interrupted.ok and interrupted.state == "active_mission_blocked"         and not interrupted.safe_to_resume and not interrupted.can_issue_command         and not interrupted.recovery_policy_approved,
        "restarted active mission does not automatically resume or forfeit")
    var corrupt: String = root_dir.path_join("rework-slot-s01-corrupt")
    check(Store.initialize(corrupt, Model.new_slot("s01-corrupt", "safe-vaccine")).ok,
        "synthetic corrupt-slot fixture")
    _write(corrupt.path_join("state.json"), "{broken save")
    var blocked: Dictionary = Gateway.diagnose_slot(corrupt)
    check(not blocked.ok and blocked.reason == "invalid_json"         and blocked.files.primary.status == "invalid_json"         and blocked.files.recovery.status == "valid"         and not blocked.can_issue_command and not blocked.automatic_repair_permitted,
        "corrupt primary reports verified recovery candidate but NEVER auto-restores")
    check(not Gateway.transact_command(corrupt, {
        "action": "start_mission", "vaccine_id": "safe-vaccine",
        "generation": 1, "run_id": "should-block", "seed": 42
    }).applied, "corrupt save rejects gameplay writes")
    check(Store.read(qa).ok and Store.read(qa).state.vaccine.unspent_fragments == 500,
        "independent QA synthetic data survived all normal-slot failures")

func _test_s01_durable_normal_flow(root_dir: String) -> void:
    var path: String = root_dir.path_join("rework-slot-s01-normal-full-run")
    check(Store.initialize(path, Model.new_slot(
        "s01-normal-full-run", "s01-player")).ok,
        "regular player starts without any QA grants")
    var identity: Dictionary = {"vaccine_id": "s01-player", "generation": 1,
        "run_id": "s01-first-run"}
    var start: Dictionary = identity.duplicate(true)
    start.merge({"action": "start_mission", "seed": 31415}, true)
    check(Gateway.transact_command(path, start).applied,
        "normal fresh run committed to E before world starts")
    var region: Dictionary = identity.duplicate(true)
    region.merge({"action": "checkpoint_region", "receipt_id": "map-first-CPU",
        "region_id": "CPU", "map_seed": 91, "generator_id": "D-procedural-v1"}, true)
    check(Gateway.transact_command(path, region).applied,
        "first region's procedural seed committed")
    var draft: Array[Dictionary] = SkillOffer.candidates(31415, "CPU", [])
    check(draft.size() == 3, "new real player receives three B draft candidates")
    if draft.is_empty():
        return
    var skill_id: String = str(draft[0].get("id", ""))
    var skill: Dictionary = identity.duplicate(true)
    skill.merge({"action": "learn_skill",
        "receipt_id": "skill:s01-first-run:CPU:" + skill_id,
        "skill_id": skill_id, "equip_on_discovery": [skill_id]}, true)
    var learned: Dictionary = Gateway.transact_command(path, skill)
    check(learned.applied, "first region offers one E-verified active skill")
    check(not Gateway.transact_command(path, skill).applied,
        "skill receipt replay cannot grant another skill")
    var saved: Dictionary = Store.read(path)
    check(saved.ok and saved.state.vaccine.known_skills.has(skill_id)         and saved.state.vaccine.equipped_skills.has(skill_id),
        "new skill and its equipment survive fresh disk reread")
    var mission_xp: Dictionary = identity.duplicate(true)
    mission_xp.merge({"action": "mission_xp", "receipt_id": "enemy-kill-s01",
        "amount": 100}, true)
    check(Gateway.transact_command(path, mission_xp).applied,
        "mission-only XP grants plugin offer, not vaccine level")
    var claimed: Dictionary = identity.duplicate(true)
    claimed.merge({"action": "plugin_picked", "receipt_id": "event-pick-s01",
        "offer_id": "event-s01-1", "plugin_id": "nv_c_move_speed",
        "rarity": "common", "level": 1, "offer_source": "event"}, true)
    check(Gateway.transact_command(path, claimed).applied,
        "event plugin acquisition persisted in original run")
    var clean: Dictionary = identity.duplicate(true)
    clean.merge({"action": "hardware_cleaned", "receipt_id": "clean-CPU-s01",
        "hardware_id": "CPU"}, true)
    check(Gateway.transact_command(path, clean).applied,
        "cleaned region acknowledged before extraction")
    var terminal: Dictionary = identity.duplicate(true)
    terminal.merge({"action": "prepare_terminal",
        "receipt_id": "stage-extract-s01",
        "outcome": "extract", "terminal_receipt_id": "extract-s01",
        "learn_plugin_ids": ["nv_c_move_speed"]}, true)
    check(Gateway.transact_command(path, terminal).applied,
        "extraction intent and user learning choice durably staged")
    var extract: Dictionary = identity.duplicate(true)
    extract.merge({"action": "extract", "receipt_id": "extract-s01",
        "learn_plugin_ids": ["nv_c_move_speed"]}, true)
    check(Gateway.transact_command(path, extract).applied,
        "final extraction atomically banks vaccine XP and plugin")
    saved = Store.read(path)
    check(saved.ok and saved.state.vaccine.mission == null         and saved.state.vaccine.total_xp == 120         and saved.state.vaccine.known_skills.has(skill_id)         and saved.state.vaccine.carried.has("nv_c_move_speed")         and saved.state.vaccine.closed_runs.get("s01-first-run") == "extracted",
        "relaunch from disk preserves earned skill, XP, carried plugin, run receipt")
    check(not Gateway.transact_command(path, extract).applied,
        "extraction replay never banks XP twice")
    var deposit: Dictionary = {"action": "archive_plugin",
        "vaccine_id": "s01-player", "generation": 1,
        "receipt_id": "archive-first", "plugin_id": "nv_c_move_speed"}
    check(Gateway.transact_command(path, deposit).applied,
        "manually archived plugin moved out of carried slot")
    saved = Store.read(path)
    check(saved.ok and saved.state.vaccine.archive.has("nv_c_move_speed")         and not saved.state.vaccine.carried.has("nv_c_move_speed"),
        "relaunch preserves exactly one archived plugin instance")
    var second: Dictionary = identity.duplicate(true)
    second.merge({"action": "start_mission", "run_id": "s01-second-run",
        "seed": 8128}, true)
    check(Gateway.transact_command(path, second).applied,
        "next run starts only after previous extraction is closed")
    saved = Store.read(path)
    check(saved.ok and saved.state.vaccine.mission.mission_level == 1         and saved.state.vaccine.mission.mission_xp == 0         and saved.state.vaccine.total_xp == 120         and saved.state.vaccine.archive.has("nv_c_move_speed"),
        "new mission resets temporary XP while preserving vaccine growth and DB")

func _test_mission_checkpoint(root_dir: String) -> void:
    # Real C/A can use the same command shape; everything below is synthetic.
    var slot: String = root_dir.path_join("rework-slot-long-mission")
    check(Store.initialize(slot, Model.new_slot("long-mission", "checkpoint-vaccine")).ok,
        "checkpoint fixture initialized without legacy data")
    var hub: Dictionary = Gateway.inspect_slot(slot)
    check(hub.get("safe_to_resume", false) and hub.state == "hub",
        "hub snapshot is safe to reopen")
    var base := {"vaccine_id": "checkpoint-vaccine", "generation": 1,
        "run_id": "run-checkpoint"}
    var start: Dictionary = base.duplicate(true)
    start.merge({"action": "start_mission", "seed": 9999}, true)
    check(Gateway.transact_command(slot, start).applied,
        "checkpoint run started")
    var old_run: Dictionary = Gateway.inspect_slot(slot)
    check(old_run.ok and not old_run.safe_to_resume \
        and old_run.reason == "checkpoint_missing_requires_approved_policy",
        "legacy uncheckpointed run stays blocked, no auto cancellation")

    var region: Dictionary = base.duplicate(true)
    region.merge({"action": "checkpoint_region", "receipt_id": "map:CPU:1",
        "region_id": "CPU", "map_seed": 9876, "generator_id": "d-cpu-v1"}, true)
    check(Gateway.transact_command(slot, region).applied,
        "world generation seed and version committed")
    var shifted: Dictionary = region.duplicate(true)
    shifted.receipt_id = "map:CPU:2"
    shifted.map_seed = 9877
    check(not Gateway.transact_command(slot, shifted).applied,
        "same CPU region cannot regenerate with different seed")
    check(not Gateway.transact_command(slot, region).applied,
        "world map receipt is exactly once")
    var direct_map_rollback: Callable = func(next: Dictionary) -> Dictionary:
        next.vaccine.mission.generated_map_seeds["CPU"] = 111
        next.vaccine.mission.environment_rolls["e_checkpoint_v1"].regions.CPU.seed = 111
        return {"ok": true, "state": next}
    check(Store.transact(slot, direct_map_rollback).reason == "invalid_transition",
        "direct save writer cannot rewrite committed region seed or generator")


    var xp: Dictionary = base.duplicate(true)
    xp.merge({"action": "mission_xp", "receipt_id": "kill:01", "amount": 100}, true)
    check(Gateway.transact_command(slot, xp).applied,
        "mission XP is banked before displaying cards")

    var offer := {
        "offer_id": "run-checkpoint:1", "active": true, "prepared": {},
        "reconciliation_required": false, "rerolls_remaining": 3,
        "missed_screens": 0, "context": {
            "cleared_hardware": 0, "difficulty_tier": 0, "luck_points": 0.0
        }, "high_seen": false,
        "cards": [
            {"slot": 0, "plugin_id": "plugin_alpha", "rarity": "common", "empty": false},
            {"slot": 1, "plugin_id": "plugin_beta", "rarity": "rare", "empty": false},
            {"slot": 2, "plugin_id": "plugin_gamma", "rarity": "common", "empty": false}
        ]
    }
    var saved_offer: Dictionary = base.duplicate(true)
    saved_offer.merge({"action": "checkpoint_offer", "receipt_id": "offer:01",
        "offer_snapshot": offer, "rng_state_text": "-725412"}, true)
    check(Gateway.transact_command(slot, saved_offer).applied,
        "C three-card offer and entropy cursor committed before UI")
    var after_offer: Dictionary = Gateway.inspect_slot(slot)
    check(after_offer.ok and after_offer.map_offer_checkpoint_ready \
        and not after_offer.technically_restorable \
        and not after_offer.combat_runtime_checkpoint_present \
        and not after_offer.safe_to_resume and after_offer.requires_user_action \
        and after_offer.reason == "resume_or_cancel_policy_unapproved",
        "saved offer is inspectable but does not authorize unapproved resumption")
    check(after_offer.reward_snapshot.offer.cards[1].plugin_id == "plugin_beta",
        "reload displays the same second card, not a fresh draw")
    var ui_copy: Dictionary = after_offer.reward_snapshot.duplicate(true)
    ui_copy.offer.cards[1].plugin_id = "malicious_ui_only"
    check(Gateway.inspect_slot(slot).reward_snapshot.offer.cards[1].plugin_id ==
        "plugin_beta", "UI can never mutate the authoritative saved offer snapshot")

    var missing: Dictionary = Gateway.reconcile_receipt(slot,
        "checkpoint-vaccine", 1, "run-checkpoint", "run-checkpoint:1:1",
        "run-checkpoint:1")
    check(missing.ok and missing.receipt_definitely_absent \
        and missing.can_retry_same_receipt,
        "unselected C card can retry original receipt, not acquire it automatically")

    var reroll_snapshot: Dictionary = offer.duplicate(true)
    reroll_snapshot.rerolls_remaining = 2
    reroll_snapshot.cards[1] = {
        "slot": 1, "plugin_id": "plugin_delta", "rarity": "rare", "empty": false
    }
    var reroll: Dictionary = saved_offer.duplicate(true)
    reroll.receipt_id = "offer:02"
    reroll.offer_snapshot = reroll_snapshot
    reroll.rng_state_text = "-725413"
    var reroll_ack: Dictionary = Gateway.transact_command(slot, reroll)
    check(reroll_ack.get("applied", false),
        "one-card reroll atomically saves remaining count and exact cards: " +
        str(reroll_ack.get("reason", "ok")))
    if not reroll_ack.get("applied", false):
        return # Avoid cascading failed-state assertions and null/property errors.
    var altered: Dictionary = reroll.duplicate(true)
    altered.receipt_id = "offer:03"
    altered.offer_snapshot = reroll_snapshot.duplicate(true)
    altered.offer_snapshot.rerolls_remaining = 1
    altered.offer_snapshot.cards[0].plugin_id = "plugin_changed_a"
    altered.offer_snapshot.cards[2].plugin_id = "plugin_changed_b"
    check(not Gateway.transact_command(slot, altered).applied,
        "a reroll cannot replace two other visible cards")
    var check_state: Dictionary = Gateway.inspect_slot(slot)
    check(check_state.reward_snapshot.rerolls_left == 2 \
        and check_state.reward_snapshot.offer.cards[0].plugin_id == "plugin_alpha" \
        and check_state.reward_snapshot.offer.cards[1].plugin_id == "plugin_delta",
        "a rejected reroll never alters saved cards or reload count")

    var picked: Dictionary = base.duplicate(true)
    picked.merge({"action": "plugin_picked",
        "receipt_id": "run-checkpoint:1:1", "offer_id": "run-checkpoint:1",
        "plugin_id": "plugin_delta", "rarity": "rare", "level": 1,
        "offer_source": "levelup"}, true)
    var forged: Dictionary = picked.duplicate(true)
    forged.plugin_id = "plugin_not_on_card"
    check(not Gateway.transact_command(slot, forged).applied,
        "forged choice cannot replace the durable offer snapshot")
    forged = picked.duplicate(true)
    forged.level = 100
    check(not Gateway.transact_command(slot, forged).applied,
        "saved card reward cannot fabricate a higher plugin level")
    var ack_lost: Dictionary = Gateway.transact_command(slot, picked,
        -1, "after_primary")
    check(not ack_lost.applied and ack_lost.reason == "simulated_ack_lost",
        "simulate C choice persisted before ACK is lost")
    var confirmed: Dictionary = Gateway.reconcile_receipt(slot,
        "checkpoint-vaccine", 1, "run-checkpoint", "run-checkpoint:1:1",
        "run-checkpoint:1")
    check(confirmed.ok and confirmed.receipt_committed \
        and confirmed.reconciliation_complete,
        "read-only C confirmation proves the exact already-committed receipt")
    var other_card: Dictionary = Gateway.reconcile_receipt(slot,
        "checkpoint-vaccine", 1, "run-checkpoint", "run-checkpoint:1:0",
        "run-checkpoint:1")
    check(not other_card.ok \
        and other_card.reason == "offer_claimed_with_different_receipt",
        "second card of already-claimed offer cannot be retried")
    check(not Gateway.transact_command(slot, picked).applied,
        "C replay after ACK loss cannot grant another plugin level")
    var direct_claim_rollback: Callable = func(next: Dictionary) -> Dictionary:
        next.vaccine.mission.claimed_offer_ids.clear()
        return {"ok": true, "state": next}
    check(Store.transact(slot, direct_claim_rollback).reason == "invalid_transition",
        "even direct storage callers cannot erase claimed card screen IDs")
    var direct_plugin_rollback: Callable = func(next: Dictionary) -> Dictionary:
        next.vaccine.mission.obtained_plugins.erase("plugin_delta")
        return {"ok": true, "state": next}
    check(Store.transact(slot, direct_plugin_rollback).reason == "invalid_transition",
        "even direct storage callers cannot erase earned temporary plugins")
    var post_pick: Dictionary = Gateway.inspect_slot(slot)
    check(post_pick.reward_snapshot.offer == null \
        and post_pick.reward_snapshot.pity_counter == 1 \
        and post_pick.reward_snapshot.obtained_plugins.has("plugin_delta"),
        "C/E settlement and legendary pity count survive process restart")

    var hardware: Dictionary = base.duplicate(true)
    hardware.merge({"action": "hardware_cleaned",
        "receipt_id": "clean:CPU:1", "hardware_id": "CPU"}, true)
    check(Gateway.transact_command(slot, hardware).applied,
        "one CPU cleanse committed after reward decision")
    var prepare: Dictionary = base.duplicate(true)
    prepare.merge({"action": "prepare_terminal",
        "receipt_id": "terminal:prepared:1", "outcome": "extract",
        "terminal_receipt_id": "terminal:final:1",
        "learn_plugin_ids": ["plugin_delta"]}, true)
    check(Gateway.transact_command(slot, prepare).applied,
        "pending terminal user learning choice durably staged")
    var wrong_prepare: Dictionary = prepare.duplicate(true)
    wrong_prepare.receipt_id = "terminal:illegal:1"
    wrong_prepare.learn_plugin_ids = ["plugin_not_obtained"]
    check(not Gateway.transact_command(slot, wrong_prepare).applied,
        "unfulfillable extraction learning request is never checkpointed")
    var pending: Dictionary = Gateway.inspect_slot(slot)
    check(pending.reason == "terminal_intent_requires_reconciliation",
        "uncommitted extraction intent never auto-finalized at startup")
    var extract: Dictionary = base.duplicate(true)
    extract.merge({"action": "extract", "receipt_id": "terminal:wrong:1",
        "learn_plugin_ids": ["plugin_delta"]}, true)
    check(not Gateway.transact_command(slot, extract).applied,
        "different extraction receipt cannot bypass staged user choice")
    extract.receipt_id = "terminal:final:1"
    check(Gateway.transact_command(slot, extract).applied,
        "exact staged extraction closes run once")
    var finished: Dictionary = Gateway.inspect_slot(slot)
    check(finished.ok and finished.state == "hub" and finished.safe_to_resume,
        "successful extraction returns to persistent hub")
    check(finished.snapshot.vaccine.carried.plugin_delta.level == 1,
        "learned rare plugin and exact level survive reload")
    check(not Gateway.transact_command(slot, extract).applied,
        "duplicate extraction does not award additional XP or plugins")
    check(Gateway.reconcile_receipt(slot, "checkpoint-vaccine", 1,
        "run-checkpoint", "terminal:final:1").receipt_committed,
        "closed-run extraction receipt remains provable")
    check(not Gateway.reconcile_receipt(slot, "old-vaccine", 1,
        "run-checkpoint", "terminal:final:1").receipt_definitely_absent,
        "wrong vaccine identity never claims a missing receipt")

    var current: Dictionary = finished.snapshot
    for i in range(4):
        var run_id: String = "final-run-" + str(i)
        var command_start: Dictionary = {
            "action": "start_mission", "vaccine_id": "checkpoint-vaccine",
            "generation": 1, "run_id": run_id, "seed": i + 1
        }
        var command_die: Dictionary = {
            "action": "die", "vaccine_id": "checkpoint-vaccine",
            "generation": 1, "run_id": run_id, "receipt_id": "die-" + run_id
        }
        check(Gateway.transact_command(slot, command_start).applied,
            "synthetic fourth-death test run starts: " + run_id)
        check(Gateway.transact_command(slot, command_die).applied,
            "death/durable clone or destruction: " + run_id)
    var destroyed: Dictionary = Gateway.inspect_slot(slot)
    check(destroyed.ok and destroyed.state == "vaccine_selection" \
        and destroyed.snapshot.vaccine == null,
        "final vaccine destruction erases checkpoint and all persistent rewards")
    var stale: Dictionary = Gateway.reconcile_receipt(slot, "checkpoint-vaccine",
        1, "final-run-3", "die-final-run-3")
    check(not stale.receipt_definitely_absent and not stale.reconciliation_complete,
        "destroyed vaccine never allows stale final-death receipt to be replayed")
    _write(slot.path_join("state.json"), "{synthetic damaged primary")
    var invalid: Dictionary = Gateway.inspect_slot(slot)
    check(not invalid.ok and invalid.requires_user_action \
        and not invalid.safe_to_resume and invalid.reason == "invalid_json",
        "damaged primary produces user-action status, never resets to Lv.1")


func _test_rare_pair_recovery(root_dir: String) -> void:
    ## Synthetic E-only slot, no legacy file and no live player save.
    var slot: String = root_dir.path_join("rework-slot-rare-pair-e")
    check(Store.initialize(slot, Model.new_slot("rare-pair-e", "rare-vaccine-e")).ok,
        "E rare-pair fixture initialized independently")
    var base: Dictionary = {
        "vaccine_id": "rare-vaccine-e", "generation": 1,
        "run_id": "run-rare-e"
    }
    var start: Dictionary = base.duplicate(true)
    start.merge({"action": "start_mission", "seed": 2468}, true)
    check(Gateway.transact_command(slot, start).applied,
        "E rare reconciliation mission started")
    var experience: Dictionary = base.duplicate(true)
    experience.merge({"action": "mission_xp", "receipt_id": "kill-rare-e",
        "amount": 100}, true)
    check(Gateway.transact_command(slot, experience).applied,
        "E rare reconciliation offer earned")
    var source: Dictionary = base.duplicate(true)
    source.merge({"action": "plugin_picked",
        "receipt_id": "run-rare-e:1:0", "offer_id": "run-rare-e:1",
        "plugin_id": "nv_c_random_rare", "rarity": "common",
        "level": 1, "offer_source": "levelup"}, true)
    var absent: Dictionary = Gateway.reconcile_rare_grant(slot, "rare-vaccine-e",
        1, "run-rare-e", "run-rare-e:1:0", "run-rare-e:1")
    check(absent.ok and absent.receipt_definitely_absent \
        and not absent.can_retry_same_receipt and not absent.pair_verified,
        "missing source is observable but not a replay authorization")
    var uncertain: Dictionary = Gateway.transact_command(slot, source,
        -1, "after_primary")
    check(not uncertain.applied and uncertain.reason == "simulated_ack_lost",
        "E lost ACK after atomic source+rare primary commit")
    var proof: Dictionary = Gateway.reconcile_rare_grant(slot, "rare-vaccine-e",
        1, "run-rare-e", "run-rare-e:1:0", "run-rare-e:1")
    check(proof.ok and proof.pair_verified and proof.receipt_committed \
        and proof.grant.bonus_receipt == "run-rare-e:1:0:rare:0",
        "read-only proof verifies BOTH source and rare receipts")
    var first: Dictionary = Store.read(slot)
    check(first.ok and first.state.vaccine.receipts.has("run-rare-e:1:0") \
        and first.state.vaccine.receipts.has("run-rare-e:1:0:rare:0"),
        "persisted grant pair survives restart after lost ACK")
    check(not Gateway.transact_command(slot, source).applied,
        "replay cannot pay a second rare award")
    var second: Dictionary = Gateway.reconcile_rare_grant(slot,
        "rare-vaccine-e", 1, "wrong-run", "run-rare-e:1:0")
    check(not second.ok and not second.receipt_committed \
        and not second.receipt_definitely_absent,
        "wrong run must not claim even an existing source receipt")
    var forged: Dictionary = Gateway.reconcile_rare_grant(slot,
        "rare-vaccine-e", 1, "run-rare-e", "run-rare-e:1:0",
        "run-rare-e:2")
    check(not forged.ok and not forged.receipt_committed,
        "source proof cannot be bound to an unclaimed offer")
    var ledger_erase: Callable = func(next: Dictionary) -> Dictionary:
        next.vaccine.mission.environment_rolls["e_rare_bonus_v1"].clear()
        return {"ok": true, "state": next}
    check(Store.transact(slot, ledger_erase).reason == "invalid_transition",
        "direct Store writer cannot erase immutable rare grant history")
    var loaded: Dictionary = Store.read(slot)
    check(loaded.ok and loaded.revision == first.revision \
        and loaded.state.vaccine.mission.environment_rolls["e_rare_bonus_v1"] \
            == first.state.vaccine.mission.environment_rolls["e_rare_bonus_v1"],
        "failed ledger modification did not roll back rare record or revision")


func _test_combat_observation(root_dir: String) -> void:
    var slot: String = root_dir.path_join("rework-slot-combat-observation")
    check(Store.initialize(slot,
        Model.new_slot("combat-observation", "combat-vaccine")).ok,
        "diagnostic observation synthetic slot initialized")
    var base: Dictionary = {"vaccine_id": "combat-vaccine", "generation": 1,
        "run_id": "run-combat-observation"}
    var start: Dictionary = base.duplicate(true)
    start.merge({"action": "start_mission", "seed": 1234}, true)
    check(Gateway.transact_command(slot, start).applied, "runtime diagnostic run started")
    var region: Dictionary = base.duplicate(true)
    region.merge({"action": "checkpoint_region", "receipt_id": "map-cpu-observe",
        "region_id": "CPU", "map_seed": 112233,
        "generator_id": "d-cpu-v1"}, true)
    check(Gateway.transact_command(slot, region).applied,
        "map generator seed/version committed before combat observation")
    var observed: Dictionary = base.duplicate(true)
    observed.merge({
        "action": "checkpoint_combat_observation",
        "receipt_id": "runtime-tick-100", "region_id": "CPU",
        "combat_tick": 100, "b_version": "B-runtime-v1",
        "d_version": "D-world-v1",
        "b_state": {
            "tick": 100, "rng_state_text": "9223372036854770000",
            "player": {"hp": 55, "mana": 30, "position": [3.0, 0.0, 4.0]},
            "enemies": [{"id": "enemy-1", "hp": 15, "position": [10, 0, 6]}],
            "projectiles": [{"id": "bullet-1", "age_ticks": 3}],
            "skills": {"Q": {"cooldown_ticks": 22}},
            "status_effects": [{"id": "poison", "remaining_ticks": 60}]
        },
        "d_state": {
            "tick": 100, "rng_state_text": "-1234567",
            "player_transform": {"position": [3, 0, 4], "rotation": [0, 0, 0]},
            "enemy_transforms": [{"id": "enemy-1", "position": [10, 0, 6]}],
            "world_entities": []
        }
    }, true)
    var committed: Dictionary = Gateway.transact_command(slot, observed)
    check(committed.get("applied", false),
        "B and D observation captured in ONE checksummed E save: " + str(committed))
    var inspected: Dictionary = Gateway.inspect_slot(slot)
    check(inspected.ok and inspected.combat_runtime_checkpoint_present \
        and inspected.combat_observation_snapshot.combat_tick == 100 \
        and not inspected.technically_restorable and not inspected.safe_to_resume,
        "diagnostic battle packet visible but mid-combat recovery stays blocked")
    var original: Dictionary = Store.read(slot)
    check(original.ok and original.state.vaccine.mission.environment_rolls.has(
        "e_combat_observation_v1"), "runtime packet survives a fresh verified read")
    var stale: Dictionary = observed.duplicate(true)
    stale.receipt_id = "runtime-tick-100-duplicate"
    check(not Gateway.transact_command(slot, stale).applied,
        "same combat tick cannot silently replace the B/D packet")
    var skewed: Dictionary = observed.duplicate(true)
    skewed.receipt_id = "runtime-tick-101-skewed"
    skewed.combat_tick = 101
    skewed.b_state.tick = 101
    check(not Gateway.transact_command(slot, skewed).applied,
        "B and D must attest the SAME paused simulation tick")
    var next: Dictionary = observed.duplicate(true)
    next.receipt_id = "runtime-tick-101"
    next.combat_tick = 101
    next.b_state.tick = 101
    next.d_state.tick = 101
    check(Gateway.transact_command(slot, next).applied,
        "next synchronized B/D observation replaces the older diagnostics")
    var rewind: Callable = func(copy: Dictionary) -> Dictionary:
        copy.vaccine.mission.environment_rolls["e_combat_observation_v1"] = \
            original.state.vaccine.mission.environment_rolls[
                "e_combat_observation_v1"].duplicate(true)
        return {"ok": true, "state": copy}
    check(Store.transact(slot, rewind).reason == "invalid_transition",
        "direct Store writer cannot rewind the global combat tick")
    var final_read: Dictionary = Store.read(slot)
    check(final_read.ok and final_read.state.vaccine.mission.environment_rolls[
        "e_combat_observation_v1"].combat_tick == 101,
        "last verified observation is stable after rejected rewind")
