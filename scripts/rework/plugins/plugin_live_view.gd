extends RefCounted
## C-owned, read-only DTO for A's HUD. It never grants, equips or saves.
## A/E must pass levels from an E-verified revision, never from a pending card.
const Catalog = preload("res://scripts/rework/plugins/plugin_catalog.gd")
const Effect = preload("res://scripts/rework/plugins/plugin_effect_runtime.gd")
const PASSIVES := ["random_stats", "passive_stat", "dual_penetration", "dual_speed"]

static func _validate_levels(levels: Dictionary, by_id: Dictionary) -> String:
	for key in levels:
		var uid: String = str(key)
		if not by_id.has(uid):
			return "unknown_plugin:" + uid
		var level: Variant = levels[key]
		if typeof(level) != TYPE_INT or int(level) < 0 or int(level) > int(by_id[uid]["max_level"]):
			return "invalid_plugin_level:" + uid
	return ""

static func describe(confirmed_E_levels: Dictionary, deployed_B_levels: Dictionary,
		runtime_state: Dictionary = {}, tick: int = 0) -> Dictionary:
	## Fail closed on a B level exceeding E, or unknown plugin IDs.
	## An E confirmation is a precondition, not something this DTO can forge.
	if tick < 0:
		return {"ok":false,"reason":"invalid_tick","plugins":[]}
	var entries: Array[Dictionary] = Catalog.entries()
	var by_id: Dictionary = {}
	for entry in entries:
		by_id[str(entry["plugin_id"])] = entry
	var failure: String = _validate_levels(confirmed_E_levels, by_id)
	if not failure.is_empty():
		return {"ok":false,"reason":"invalid_E_levels:" + failure,"plugins":[]}
	failure = _validate_levels(deployed_B_levels, by_id)
	if not failure.is_empty():
		return {"ok":false,"reason":"invalid_B_levels:" + failure,"plugins":[]}
	for uid in deployed_B_levels:
		if int(deployed_B_levels[uid]) > int(confirmed_E_levels.get(uid,0)):
			return {"ok":false,"reason":"B_exceeds_E:" + str(uid),"plugins":[]}
	var effect = Effect.new()
	if not effect.validation_errors.is_empty():
		return {"ok":false,"reason":"invalid_effect_catalog","plugins":[]}
	var result: Array[Dictionary] = []
	var equipped: int = 0
	var pending: int = 0
	for entry in entries:
		var uid: String = str(entry["plugin_id"])
		var e_level: int = int(confirmed_E_levels.get(uid,0))
		var b_level: int = int(deployed_B_levels.get(uid,0))
		var kind: String = str(effect.effect_definition(uid).get("kind",""))
		var status: String = "not_owned"
		if e_level > 0 and b_level != e_level:
			status = "waiting_for_B_sync"
			pending += 1
		elif e_level > 0:
			equipped += 1
			status = "armed_event_trigger"
			if kind in PASSIVES:
				status = "passive_applied"
			elif kind == "breakable_sprint":
				status = "passive_applied" if bool(runtime_state.get("sprint_active",false)) else "broken_until_reactivation"
			elif kind == "fever":
				status = "timed_buff_active" if tick < int(runtime_state.get("fever_until",-1)) else "charging"
			elif kind == "kill_power":
				status = "stacked_passive_active" if int(runtime_state.get("kill_power_stacks",0)) > 0 else "awaiting_kill"
			elif kind == "grant_rare":
				status = "E_receipt_only_no_combat_proc"
		var level_effects: Array = entry.get("level_effects",[])
		var current_text: String = ""
		var next_text: String = ""
		if e_level > 0 and e_level <= level_effects.size():
			current_text = str(level_effects[e_level-1].get("summary_ko",""))
		if e_level < level_effects.size():
			next_text = str(level_effects[e_level].get("summary_ko",""))
		result.append({
			"plugin_id":uid, "name":str(entry.get("name_ko","")),
			"rarity":str(entry.get("rarity","")), "kind":kind,
			"description":str(entry.get("description_ko","")),
			"trigger":entry.get("trigger",{}).duplicate(true),
			"cooldown_scope":str(entry.get("cooldown_scope","")),
			"cooldown_seconds":float(entry.get("internal_cooldown_seconds",0.0)),
			"confirmed_level":e_level, "deployed_level":b_level,
			"max_level":int(entry.get("max_level",0)),
			"next_level":e_level+1 if e_level < int(entry.get("max_level",0)) else 0,
			"current_effect":current_text, "next_effect":next_text,
			"owned_by_E":e_level>0, "applied_to_B":e_level>0 and b_level==e_level,
			"status":status
		})
	return {"ok":true,"schema":1,"source":"E_verified_levels_required",
		"plugins":result,"total_catalog":result.size(),
		"equipped":equipped,"awaiting_sync":pending,"tick":tick}
