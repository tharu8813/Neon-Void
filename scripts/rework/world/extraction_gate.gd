extends RefCounted
## D -> E prepare/ack/finalize barrier. E's durable receipt takes priority over
## in-memory D results; do not send a second reward command after lost ACK.
signal extraction_prepared(fact: Dictionary)
signal extraction_finalized(fact: Dictionary)
var state: RefCounted
var pending: Dictionary = {}
var finalized: bool = false

func _init(world_state: RefCounted = null) -> void:
    state = world_state

func prepare(region_id: String) -> Dictionary:
    if finalized or state == null or not state.is_extraction_open(region_id):
        return {}
    if not pending.is_empty():
        return pending.duplicate(true) if str(pending.get("region_id", "")) == region_id else {}
    var cleared: Array[String] = state.cleared_regions()
    if cleared.is_empty():
        return {}
    pending = {
        "schema": 1, "type": "mission_extracted",
        "run_id": state.run_id, "region_id": region_id,
        "receipt_id": "world:%s:extraction" % state.run_id,
        "outcome": "success", "cleared_regions": cleared,
        "cleared_count": cleared.size(), "full_clear": cleared.size() == 5
    }
    extraction_prepared.emit(pending.duplicate(true))
    return pending.duplicate(true)

func finalize_after_durable_ack(ack: Dictionary) -> Dictionary:
    if pending.is_empty() or finalized:
        return {}
    if not bool(ack.get("ok", false)) or str(ack.get("receipt_id", "")) != str(pending["receipt_id"]):
        return {}
    var committed: Dictionary = state.request_extraction(str(pending["region_id"]))
    if committed.is_empty() or committed.get("receipt_id", "") != pending["receipt_id"]:
        return {}
    finalized = true
    extraction_finalized.emit(committed.duplicate(true))
    return committed.duplicate(true)
