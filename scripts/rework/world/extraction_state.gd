extends RefCounted
## Pure in-memory authoritative facts. E owns rewards and persistence.
## emit world_fact to B/A/E; extraction_committed is delivered once per instance.
signal world_fact(fact: Dictionary)
signal extraction_committed(fact: Dictionary)

const Catalog = preload("res://scripts/rework/world/hardware_catalog.gd")
var run_id: String = ""
var progress: Dictionary = {}
var seen_receipts: Dictionary = {}
var extraction_finished: bool = false

func _init(new_run_id: String = "") -> void:
    run_id = new_run_id
    for region_id in Catalog.IDS:
        var flags: Dictionary = {}
        for objective_id in Catalog.objectives(region_id):
            flags[str(objective_id)] = false
        progress[region_id] = {"objectives": flags, "boss_defeated": false, "cleared": false}

func _emit(type: String, region_id: String, objective_id: String, receipt_id: String) -> void:
    world_fact.emit({
        "schema": 1, "type": type, "run_id": run_id, "region_id": region_id,
        "objective_id": objective_id, "receipt_id": receipt_id
    })

func _valid_receipt(receipt_id: String) -> bool:
    return not receipt_id.is_empty() and not seen_receipts.has(receipt_id)

func confirm_objective(region_id: String, objective_id: String, receipt_id: String) -> bool:
    if extraction_finished or not progress.has(region_id) or not _valid_receipt(receipt_id):
        return false
    var flags: Dictionary = progress[region_id]["objectives"]
    if not flags.has(objective_id) or bool(flags[objective_id]):
        return false
    flags[objective_id] = true
    seen_receipts[receipt_id] = true
    _emit("objective_completed", region_id, objective_id, receipt_id)
    return true

func confirm_boss_defeated(region_id: String, receipt_id: String) -> bool:
    if extraction_finished or not progress.has(region_id) or not _valid_receipt(receipt_id):
        return false
    var state: Dictionary = progress[region_id]
    if bool(state["boss_defeated"]):
        return false
    for completed in state["objectives"].values():
        if not bool(completed):
            return false
    state["boss_defeated"] = true
    state["cleared"] = true
    seen_receipts[receipt_id] = true
    _emit("boss_defeated", region_id, "", receipt_id)
    _emit("hardware_cleansed", region_id, "", "world:%s:%s:clean" % [run_id, region_id])
    return true

func is_extraction_open(region_id: String) -> bool:
    return not extraction_finished and progress.has(region_id) and bool(progress[region_id]["cleared"])

func cleared_regions() -> Array[String]:
    var result: Array[String] = []
    for region_id in Catalog.IDS:
        if bool(progress[region_id]["cleared"]):
            result.append(region_id)
    return result

func request_extraction(region_id: String) -> Dictionary:
    if not is_extraction_open(region_id):
        return {}
    var cleared: Array[String] = cleared_regions()
    extraction_finished = true
    var fact: Dictionary = {
        "schema": 1, "type": "mission_extracted", "run_id": run_id, "region_id": region_id,
        "receipt_id": "world:%s:extraction" % run_id,
        "outcome": "success", "cleared_regions": cleared, "cleared_count": cleared.size(),
        "full_clear": cleared.size() == Catalog.IDS.size()
    }
    extraction_committed.emit(fact.duplicate(true))
    return fact.duplicate(true)

func report_death() -> Dictionary:
    if extraction_finished or run_id.is_empty():
        return {}
    extraction_finished = true
    var cleared: Array[String] = cleared_regions()
    var fact: Dictionary = {
        "schema": 1, "type": "mission_failed", "run_id": run_id,
        "receipt_id": "world:%s:death" % run_id, "outcome": "death",
        "cleared_regions": cleared, "cleared_count": cleared.size()
    }
    extraction_committed.emit(fact.duplicate(true))
    return fact.duplicate(true)

func snapshot() -> Dictionary:
    return {
        "schema": 1, "run_id": run_id, "progress": progress.duplicate(true),
        "extraction_finished": extraction_finished
    }
