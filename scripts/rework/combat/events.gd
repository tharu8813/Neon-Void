extends RefCounted
## Deterministic FIFO proc bus. No SceneTree, physics or VFX dependency.
## Each root event starts a fresh ancestry; derived procs carry proc_path.
## A cycle is the SAME effect, source actor and target within one ancestry.
## Legal A(target 1) -> A(target 2), multi-source effects and new roots are permitted.
var pending: Array[Dictionary] = []
var processed_ids: Dictionary = {}
var seen_collision_targets: Dictionary = {}
var last_proc_tick: Dictionary = {}
var next_sequence: int = 0
var current_tick: int = 0
var metrics: Dictionary = {}

func _init() -> void:
    reset()

func reset() -> void:
    pending.clear()
    processed_ids.clear()
    seen_collision_targets.clear()
    last_proc_tick.clear()
    next_sequence = 0
    current_tick = 0
    metrics = {"accepted": 0, "processed": 0, "cycle_blocked": 0,
        "duplicate_hit_blocked": 0, "icd_blocked": 0,
        "permission_blocked": 0, "max_pending": 0}

func set_tick(tick: int) -> void:
    current_tick = maxi(current_tick, tick)

func _new_id() -> String:
    next_sequence += 1
    return "%d:%d" % [current_tick, next_sequence]

func _queue(event: Dictionary) -> Dictionary:
    var hit_key: String = str(event.get("hit_key", ""))
    if not hit_key.is_empty():
        var collision_key: String = hit_key + "|" + str(event.get("target_id", ""))
        if seen_collision_targets.has(collision_key):
            metrics["duplicate_hit_blocked"] += 1
            return {}
        seen_collision_targets[collision_key] = true
    event["event_id"] = _new_id()
    pending.append(event)
    metrics["accepted"] += 1
    metrics["max_pending"] = maxi(int(metrics["max_pending"]), pending.size())
    return event

func submit_root(payload: Dictionary) -> Dictionary:
    ## Required by C/D: actor_id, target_id, kind, source_tags, hit_key.
    var event: Dictionary = payload.duplicate(true)
    event["tick"] = current_tick
    event["proc_path"] = []
    return _queue(event)

func submit_proc(parent: Dictionary, effect_id: String, payload: Dictionary, icd_ticks: int = 0) -> Dictionary:
    if effect_id.is_empty():
        return {}
    var event: Dictionary = payload.duplicate(true)
    var trigger_kind: String = str(event.get("trigger_kind", "on_hit"))
    if parent.has("allowed_proc_triggers"):
        var allowed: Array = parent["allowed_proc_triggers"]
        if not allowed.has(trigger_kind):
            metrics["permission_blocked"] += 1
            return {}
    var target: String = str(event.get("target_id", parent.get("target_id", "")))
    var actor: String = str(event.get("actor_id", parent.get("actor_id", "")))
    var signature: String = effect_id + "|" + actor + "|" + target
    var ancestry: Array = parent.get("proc_path", [])
    if ancestry.has(signature):
        metrics["cycle_blocked"] += 1
        return {}
    var clock: int = int(parent.get("tick", current_tick))
    var icd_key: String = signature
    if icd_ticks > 0 and last_proc_tick.has(icd_key):
        if clock - int(last_proc_tick[icd_key]) < icd_ticks:
            metrics["icd_blocked"] += 1
            return {}
    event["tick"] = clock
    event["actor_id"] = actor
    event["target_id"] = target
    event["parent_event_id"] = str(parent.get("event_id", ""))
    event["effect_id"] = effect_id
    event["source_tags"] = event.get("source_tags", ["plugin"])
    event["proc_path"] = ancestry.duplicate()
    event["proc_path"].append(signature)
    var queued: Dictionary = _queue(event)
    if not queued.is_empty() and icd_ticks > 0:
        last_proc_tick[icd_key] = clock
    return queued

func drain(handler: Callable = Callable()) -> Array[Dictionary]:
    ## handler(event) -> Array[Dictionary] of {"effect_id", "event", "icd_ticks"}
    ## No arbitrary chain-depth cutoff: cycles and duplicate hit IDs are rejected.
    ## Each call fully drains FIFO; caller may monitor metrics for load.
    var processed: Array[Dictionary] = []
    while not pending.is_empty():
        var event: Dictionary = pending.pop_front()
        var event_id: String = str(event.get("event_id", ""))
        if processed_ids.has(event_id):
            continue
        processed_ids[event_id] = true
        processed.append(event)
        metrics["processed"] += 1
        if not handler.is_valid():
            continue
        var requests: Variant = handler.call(event)
        if requests is Array:
            for request in requests:
                if not request is Dictionary:
                    continue
                var entry: Dictionary = request
                submit_proc(event, str(entry.get("effect_id", "")),
                    entry.get("event", {}), int(entry.get("icd_ticks", 0)))
    return processed
