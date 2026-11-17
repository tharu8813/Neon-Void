extends RefCounted
## Pure contract helper for B-owned attack/event queue, not a damage dispatcher.
static func try_extend_path(parent_path: Array, plugin_id: String) -> Dictionary:
	if plugin_id.is_empty() or parent_path.has(plugin_id):
		return {"allowed": false, "proc_path": parent_path.duplicate()}
	var next_path: Array = parent_path.duplicate()
	next_path.append(plugin_id)
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
