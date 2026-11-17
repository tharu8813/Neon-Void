extends SceneTree
## Read-only recovery/UI contract: unique synthetic slot, never user data.
const PilotScene = preload("res://scenes/rework/integration/rework_pilot.tscn")
const View = preload("res://scripts/rework/integration/pilot_recovery_view.gd")
const Gateway = preload("res://scripts/rework/persistence/progression_gateway.gd")
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")
const Rare = preload("res://scripts/rework/progression/rare_bonus.gd")
var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func check(ok: bool, message: String) -> void:
    if not ok:
        failures += 1
        printerr("REWORK A READONLY RECOVERY FAIL: " + message)

func _clean(folder: String) -> void:
    var dir: DirAccess = DirAccess.open(folder)
    if dir == null:
        return
    dir.list_dir_begin()
    var child: String = dir.get_next()
    while not child.is_empty():
        if child != "." and child != "..":
            var path: String = folder.path_join(child)
            if dir.current_is_dir():
                _clean(path)
            else:
                DirAccess.remove_absolute(path)
        child = dir.get_next()
    dir.list_dir_end()
    DirAccess.remove_absolute(folder)

func _run() -> void:
    var suffix: String = str(OS.get_process_id()) + "-" + str(Time.get_ticks_usec())
    var slot_id: String = "readonly-recovery-" + suffix
    var slot_path: String = Store.default_slot_path(slot_id)
    var vaccine_id: String = "readonly-vaccine"
    var run_id: String = "readonly-run"
    var created: Dictionary = Gateway.initialize(slot_id, vaccine_id)
    check(bool(created.get("ok", false)), "synthetic E slot created")
    if not bool(created.get("ok", false)):
        quit(1)
        return
    var hub: Dictionary = View.inspect(slot_path)
    check(bool(hub.get("readable", false)) and hub.get("state") == "hub"
        and not hub.get("safe_to_resume", true), "healthy hub inspection is read-only")
    var start: Dictionary = Gateway.transact_command(slot_path, {
        "action": "start_mission", "vaccine_id": vaccine_id,
        "generation": 1, "run_id": run_id, "seed": 2468
    })
    check(bool(start.get("applied", false)), "synthetic interrupted mission started")
    var before: Dictionary = Gateway.snapshot(slot_path)
    var report: Dictionary = View.inspect(slot_path)
    check(bool(report.get("readable", false))
        and report.get("state") == "active_mission_blocked"
        and not report.get("safe_to_resume", true)
        and not report.get("can_retry", true),
        "interrupted mission never authorizes an automatic restart")

    # A new session must display a visible read-only inspection, remain paused
    # and not mutate the already-present E slot.
    var pilot: Node3D = PilotScene.instantiate()
    pilot.set("slot_id", slot_id)
    pilot.set("initial_vaccine_id", vaccine_id)
    root.add_child(pilot)
    await process_frame
    var panel: Variant = pilot.get("recovery_panel")
    var recovery_label: Variant = pilot.get("recovery_label")
    check(bool(pilot.get("frozen")) and paused, "reopened live mission freezes world")
    check(panel is PanelContainer and panel.visible
        and recovery_label is Label
        and recovery_label.text.contains("읽기 전용 저장 진단"),
        "pilot exposes visible E read-only state while paused")
    pilot.call("_show_recovery_report")
    var after_inspection: Dictionary = Gateway.snapshot(slot_path)
    check(bool(after_inspection.get("ok", false))
        and int(after_inspection.get("revision", -1)) == int(before.get("revision", -2)),
        "opening/refeshing inspector never writes E state")
    paused = false
    root.remove_child(pilot)
    pilot.queue_free()
    await process_frame

    var identity: Dictionary = {
        "vaccine_id": vaccine_id, "generation": 1, "run_id": run_id
    }
    var xp: Dictionary = identity.duplicate(true)
    xp.merge({"action": "mission_xp", "receipt_id": "read-kill-1", "amount": 100}, true)
    check(bool(Gateway.transact_command(slot_path, xp).get("applied", false)),
        "E durable reward slot created")
    var choice: Dictionary = identity.duplicate(true)
    choice.merge({
        "action": "plugin_picked", "receipt_id": "readonly-run:1:0",
        "offer_id": "readonly-run:1", "plugin_id": Rare.SOURCE_ID,
        "rarity": "common", "level": 1, "offer_source": "levelup"
    }, true)
    var lost_ack: Dictionary = Gateway.transact_command(slot_path, choice, -1, "after_primary")
    check(not bool(lost_ack.get("applied", false)) and
        str(lost_ack.get("reason", "")) == "simulated_ack_lost",
        "durable source+rare grant can lose response after E primary save")
    var revision_before_view: Dictionary = Gateway.snapshot(slot_path)
    var committed: Dictionary = View.inspect(slot_path, choice)
    check(committed.get("receipt_status") == "committed"
        and bool(committed.get("rare_bonus_verified", false))
        and committed.get("text", "").contains("다시 지급하지 않아"),
        "read-only incident identifies both durable E rare receipts")
    check(not committed.get("can_retry", true)
        and not committed.get("safe_to_resume", true),
        "committed receipt does not silently resume B/C or grant again")
    var missing: Dictionary = choice.duplicate(true)
    missing["receipt_id"] = "readonly-run:999:0"
    missing["offer_id"] = "readonly-run:999"
    var absent: Dictionary = View.inspect(slot_path, missing)
    check(absent.get("receipt_status") == "absent"
        and not absent.get("can_retry", true),
        "verified absent receipt remains manual-only and does not regrant")
    var revision_after_view: Dictionary = Gateway.snapshot(slot_path)
    check(int(revision_before_view.get("revision", -1)) ==
        int(revision_after_view.get("revision", -2)),
        "committed, absent and hub reports do not change save revision")
    var nonexistent: Dictionary = View.inspect(slot_path + "-never-created")
    check(not nonexistent.get("readable", true) and
        not nonexistent.get("can_retry", true),
        "unreadable slot reports failure without initializing anything")
    _clean(slot_path)
    if failures == 0:
        print("REWORK A READONLY RECOVERY PASS: safe pause/UI, rare ACK inspection, no E writes")
    quit(1 if failures > 0 else 0)
