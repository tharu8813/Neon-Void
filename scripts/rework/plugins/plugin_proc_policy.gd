extends RefCounted
## B-compatible source/target-aware ancestry. B is authoritative for FIFO, ICD and hits.
## Signature exactly matches B PR #100 events.gd effect_id|actor_id|target_id.
static func try_extend_path(parent_path: Array, effect_id: String, actor_id: String, target_id: String) -> Dictionary:
	if effect_id.is_empty() or actor_id.is_empty() or target_id.is_empty():
		return {"allowed": false, "proc_path": parent_path.duplicate()}
	var signature: String = effect_id + "|" + actor_id + "|" + target_id
	if parent_path.has(signature):
		return {"allowed": false, "proc_path": parent_path.duplicate()}
	var next_path: Array = parent_path.duplicate()
	next_path.append(signature)
	return {"allowed": true, "proc_path": next_path}

static func may_emit_tag(entry: Dictionary, emitted_tag: String) -> bool:
	var permissions: Dictionary = entry.get("proc_permissions", {})
	var allowed_tags: Array = permissions.get("emit_tags", [])
	return allowed_tags.has(emitted_tag)

static func may_trigger_from_source(entry: Dictionary, event_source_tags: Array) -> bool:
	var trigger: Dictionary = entry.get("trigger", {})
	var required: Array = trigger.get("source_tags", [])
	if required.is_empty():
		return true
	for tag in required:
		if event_source_tags.has(tag):
			return true
	return false

static func derived_attack_may_proc(entry: Dictionary) -> bool:
	return bool(entry.get("proc_permissions", {}).get("can_trigger_on_hit_effects", false))
