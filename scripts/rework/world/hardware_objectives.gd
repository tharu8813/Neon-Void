extends RefCounted
## Independent objective progress. B validates authoritative gameplay events;
## E owns all XP/points and persistent receipts; D only reports a completed fact.
signal objective_completed(fact: Dictionary)
signal progress_changed(snapshot: Dictionary)

const SPECS := {
    "CPU": {
        "secure_core": {"kind": "defense_ticks", "required": 2400},
        "stop_intruders": {"kind": "kill", "required": 10}
    },
    "GPU": {
        "disable_emitter": {"kind": "switch", "required": 3},
        "stabilize_coolant": {"kind": "dodge_hazard", "required": 8}
    },
    "RAM": {
        "purge_corruption": {"kind": "purge", "required": 4},
        "verify_memory": {"kind": "verify", "required": 2}
    },
    "SSD": {
        "capture_node": {"kind": "capture", "required": 3},
        "seal_transfer": {"kind": "transfer_ticks", "required": 1800}
    },
    "HDD": {
        "repair_platter": {"kind": "repair", "required": 4},
        "restore_index": {"kind": "index", "required": 3}
    }
}
var run_id: String = ""
var region_id: String = ""
var progress: Dictionary = {}
var seen_receipts: Dictionary = {}

func _init(new_run_id: String = "", new_region_id: String = "") -> void:
    run_id = new_run_id
    region_id = new_region_id
    if SPECS.has(region_id):
        for objective_id in SPECS[region_id]:
            var spec: Dictionary = SPECS[region_id][objective_id]
            progress[objective_id] = {
                "kind": str(spec["kind"]), "required": int(spec["required"]),
                "current": 0, "completed": false
            }

func apply_verified_event(objective_id: String, kind: String, event_receipt: String, amount: int = 1) -> bool:
    # Returns true if an event was accepted, not necessarily if it finished a goal.
    if run_id.is_empty() or event_receipt.is_empty() or amount <= 0:
        return false
    if seen_receipts.has(event_receipt) or not progress.has(objective_id):
        return false
    var state: Dictionary = progress[objective_id]
    if bool(state["completed"]) or kind != str(state["kind"]):
        return false
    seen_receipts[event_receipt] = true
    state["current"] = mini(int(state["required"]), int(state["current"]) + amount)
    if int(state["current"]) >= int(state["required"]):
        state["completed"] = true
        objective_completed.emit({
            "schema": 1, "type": "objective_completed",
            "run_id": run_id, "region_id": region_id,
            "objective_id": objective_id,
            "receipt_id": "world:%s:%s:%s:done" % [run_id, region_id, objective_id]
        })
    progress_changed.emit(snapshot())
    return true

func all_objectives_completed() -> bool:
    if progress.is_empty():
        return false
    for objective_id in progress:
        if not bool(progress[objective_id]["completed"]):
            return false
    return true

func snapshot() -> Dictionary:
    return {
        "schema": 1, "run_id": run_id, "region_id": region_id,
        "progress": progress.duplicate(true), "boss_unlocked": all_objectives_completed()
    }
