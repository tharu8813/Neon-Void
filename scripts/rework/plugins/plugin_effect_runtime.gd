extends RefCounted
## C runtime: deterministic game actions backed by B damage/status through an injected sink.
## Authoritative target HP/status changes belong to B, durable rewards to E, UI to A.
const Catalog = preload("res://scripts/rework/plugins/plugin_catalog.gd")
const Policy = preload("res://scripts/rework/plugins/plugin_proc_policy.gd")
const EFFECT_PATH := "res://data/rework/plugins/effects.proposal.v1.json"
const TICKS_PER_SECOND := 60

var levels: Dictionary = {}
var _specs: Dictionary = {}
var _entries: Dictionary = {}
var _rng := RandomNumberGenerator.new()
var _cooldowns: Dictionary = {}
var _seen: Dictionary = {}
var _state: Dictionary = {
	"proc_count": 0, "fever_gauge": 0, "fever_until": -1,
	"sprint_active": true, "random_choices": {}, "kill_power_stacks": 0
}
var validation_errors: Array[String] = []

func _init() -> void:
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(EFFECT_PATH))
	if parsed is Dictionary and parsed.get("plugins", null) is Dictionary:
		_specs = parsed["plugins"].duplicate(true)
	else:
		validation_errors.append("missing_plugin_effect_specs")
	var existing: Array[Dictionary] = Catalog.entries()
	for entry in existing:
		_entries[str(entry["plugin_id"])] = entry.duplicate(true)
		if not _specs.has(str(entry["plugin_id"])):
			validation_errors.append("missing_effect:" + str(entry["plugin_id"]))

func configure(new_levels: Dictionary, seed_value: int = 0) -> bool:
	if not validation_errors.is_empty():
		return false
	for uid in new_levels:
		var entry: Dictionary = _entries.get(str(uid), {})
		if entry.is_empty() or typeof(new_levels[uid]) != TYPE_INT or int(new_levels[uid]) < 0 or int(new_levels[uid]) > int(entry["max_level"]):
			return false
	levels = new_levels.duplicate(true)
	_rng.seed = seed_value
	_cooldowns.clear()
	_seen.clear()
	_state = {"proc_count": 0, "fever_gauge": 0, "fever_until": -1, "sprint_active": true, "random_choices": {}, "kill_power_stacks": 0}
	_ensure_random_choices()
	return true

func set_plugin_level(plugin_id: String, new_level: int) -> bool:
	var entry: Dictionary = _entries.get(plugin_id, {})
	if entry.is_empty() or new_level <= 0 or new_level > int(entry["max_level"]):
		return false
	var old_level: int = int(levels.get(plugin_id, 0))
	if new_level != old_level + 1:
		return false
	levels[plugin_id] = new_level
	_ensure_random_choices()
	return true

func state_snapshot() -> Dictionary:
	return _state.duplicate(true)

func effect_definition(plugin_id: String) -> Dictionary:
	return _specs.get(plugin_id, {}).duplicate(true)

func _value(spec: Dictionary, key: String, level: int, fallback: float = 0.0) -> float:
	var item: Variant = spec.get(key, fallback)
	if item is Array:
		if item.is_empty():
			return fallback
		return float(item[mini(level - 1, item.size() - 1)])
	return float(item)

func _ensure_random_choices() -> void:
	var uid: String = "nv_c_random_stats"
	if not levels.has(uid) or _state["random_choices"].has(uid):
		return
	var pool: Array = _specs[uid].get("stat_pool", []).duplicate()
	var choices: Array = []
	for i in range(mini(int(_specs[uid].get("count", 3)), pool.size())):
		var index: int = _rng.randi_range(0, pool.size() - 1)
		choices.append(pool[index])
		pool.remove_at(index)
	_state["random_choices"][uid] = choices

func derived_stats(b_stats: Dictionary, tick: int = 0) -> Dictionary:
	var result: Dictionary = b_stats.duplicate(true)
	for uid in levels:
		var level: int = int(levels[uid])
		if level < 1:
			continue
		var spec: Dictionary = _specs.get(uid, {})
		match str(spec.get("kind", "")):
			"random_stats":
				for stat_name in _state["random_choices"].get(uid, []):
					result[stat_name] = float(result.get(stat_name, 0.0)) + _value(spec, "amounts", level)
			"passive_stat":
				var stat: String = str(spec.get("stat", ""))
				result[stat] = float(result.get(stat, 0.0)) + _value(spec, "values", level)
			"dual_penetration":
				result["physical_pen_percent"] = float(result.get("physical_pen_percent", 0.0)) + _value(spec, "physical", level)
				result["magic_pen_percent"] = float(result.get("magic_pen_percent", 0.0)) + _value(spec, "magic", level)
			"dual_speed":
				result["attack_speed"] = float(result.get("attack_speed", 0.0)) + _value(spec, "attack_speed", level)
				result["projectile_speed_mult"] = float(result.get("projectile_speed_mult", 1.0)) + _value(spec, "projectile_speed_mult", level)
			"breakable_sprint":
				if bool(_state.get("sprint_active", false)):
					result["move_speed"] = float(result.get("move_speed", 0.0)) * (1.0 + _value(spec, "move_speed_bonus_fraction", level) + _value(spec, "level_bonus", level))
			"kill_power":
				result["attack_damage"] = float(result.get("attack_damage", 0.0)) + int(_state.get("kill_power_stacks", 0)) * _value(spec, "attack_damage_per_kill", level)
			"fever":
				if tick < int(_state.get("fever_until", -1)):
					var multiplier: float = 1.0 + _value(spec, "all_stats_fraction", level)
					for stat in ["max_health", "attack_damage", "ability_power", "armor", "magic_resist", "move_speed", "attack_speed", "projectile_speed_mult", "max_mana", "mana_regen"]:
						result[stat] = float(result.get(stat, 0.0)) * multiplier
	return result

func process(event: Dictionary, world: Dictionary, sink: Callable = Callable()) -> Dictionary:
	## Sink must validate+apply the FULL command batch atomically to B's world,
	## returning {ok:true}. State is committed only after this acknowledgement.
	var event_id: String = str(event.get("event_id", ""))
	var kind: String = str(event.get("kind", ""))
	var actor_id: String = str(event.get("actor_id", ""))
	var target_id: String = str(event.get("target_id", ""))
	var tick: int = int(event.get("tick", -1))
	if event_id.is_empty() or kind.is_empty() or actor_id.is_empty() or tick < 0:
		return {"ok": false, "reason": "invalid_event"}
	if _seen.has(event_id):
		return {"ok": true, "duplicate": true, "commands": []}
	var next_state: Dictionary = _state.duplicate(true)
	var next_cooldowns: Dictionary = _cooldowns.duplicate(true)
	var commands: Array[Dictionary] = []
	var source: Array = event.get("source_tags", [])
	for uid in levels.keys():
		var level: int = int(levels[uid])
		if level < 1:
			continue
		var spec: Dictionary = _specs.get(uid, {})
		var effect_kind: String = str(spec.get("kind", ""))
		var eligible: bool = false
		match effect_kind:
			"periodic_shield":
				eligible = kind == "tick" and tick > 0 and tick % int(spec["interval_ticks"]) == 0
			"skill_shield":
				eligible = kind == "skill_cast"
			"mobility_buff":
				eligible = kind in ["dash", "blink", "leap", "teleport", "reveal"]
			"low_hp_emergency", "low_hp_knockback":
				eligible = kind in ["damage_taken", "health_threshold"] and float(event.get("actor_health_ratio", 1.0)) <= float(spec["trigger_health_ratio"])
			"chain_lightning", "hit_explosion":
				eligible = kind in ["on_hit", "on_crit", "on_hit_proc"] and not target_id.is_empty()
			"breakable_sprint":
				eligible = kind == "damage_taken" and float(event.get("actual_damage", 0.0)) > 0.0
			"armor_shred", "execute":
				eligible = kind == "before_damage" if effect_kind == "execute" else kind == "damage_dealt"
			"proc_volley", "disassembly":
				eligible = kind == "on_hit_proc"
			"skill_followup":
				eligible = kind == "skill_damage"
			"dash_explosion":
				eligible = kind == "dash"
			"grant_rare":
				eligible = kind == "plugin_acquired" and str(event.get("plugin_id", "")) == uid
			"kill_power":
				eligible = kind == "kill"
			"dash_shield":
				eligible = kind in ["dash", "blink"]
			"extra_bullet", "homing", "laser", "boomerang":
				eligible = kind == "basic_attack"
			"fever":
				eligible = kind in ["on_hit_proc", "kill"]
		if not eligible:
			continue
		var entry: Dictionary = _entries.get(uid, {})
		# B supplies per-attack derived permissions. Do not let a derived attack
		# masquerade as a new basic attack and launch an unbounded loop.
		var derived: bool = source.has("plugin_attack") or source.has("derived_attack")
		if derived and kind in ["on_hit", "on_crit", "on_hit_proc"] and not Policy.derived_attack_may_proc(entry):
			continue
		if derived and kind in ["on_hit", "on_crit", "on_hit_proc"] and not event.get("allowed_proc_triggers", []).has("on_hit"):
			continue
		if effect_kind in ["chain_lightning","hit_explosion"] and not source.has("basic_attack") and not (derived and event.get("allowed_proc_triggers",[]).has("on_hit")):
			continue
		var cd_ticks: int = int(spec.get("target_cooldown_ticks", spec.get("cooldown_ticks", 0)))
		var cooldown_key: String = uid + "|" + actor_id
		if str(entry.get("cooldown_scope", "")) == "per_target":
			cooldown_key += "|" + target_id
		if cd_ticks > 0 and tick - int(next_cooldowns.get(cooldown_key, -999999)) < cd_ticks:
			continue
		var command: Dictionary = {}
		match effect_kind:
			"periodic_shield", "skill_shield", "dash_shield":
				command = {"type":"shield","actor_id":actor_id,"amount":_value(spec,"shield_values",level),"plugin_id":uid}
			"mobility_buff":
				command = {"type":"buff","actor_id":actor_id,"stat":"move_speed","value":_value(spec,"move_speed",level),"duration_ticks":int(spec["duration_ticks"]),"plugin_id":uid}
			"low_hp_emergency":
				command = {"type":"shield","actor_id":actor_id,"amount":float(world.get("actor_stats", {}).get("max_health",100.0))*float(spec["shield_health_fraction"]),"plugin_id":uid,"balance_policy":"provisional_shield_not_confirmed_heal"}
				commands.append({"type":"buff","actor_id":actor_id,"stat":"move_speed","value":_value(spec,"move_speed",level),"duration_ticks":int(spec["duration_ticks"]),"plugin_id":uid})
			"low_hp_knockback":
				command = {"type":"knockback","actor_id":actor_id,"force":_value(spec,"force",level),"plugin_id":uid}
			"breakable_sprint":
				next_state["sprint_active"] = false
			"chain_lightning":
				command = _attack_command(uid,actor_id,target_id,tick,event,spec,level,"lightning",_value(spec,"base_damage",level)*_value(spec,"level_multipliers",level))
				if not command.is_empty():
					command["attack"]["ap_ratio"] = _value(spec,"ap_ratio",level) * _value(spec,"level_multipliers",level)
					command["chain_targets"] = int(_value(spec,"target_count",level))
					command["search_distance_mult"] = _value(spec,"search_distance_mult",level)
			"hit_explosion", "dash_explosion":
				command = _attack_command(uid,actor_id,target_id,tick,event,spec,level,"explosion",_value(spec,"base_damage",level))
				if not command.is_empty():
					command["attack"]["is_area"] = true
					command["area_radius"] = _value(spec,"area_radius",level)
			"armor_shred":
				command = {"type":"status","target_id":target_id,"plugin_id":uid,
					"definition":{"id":uid,"source_id":actor_id,"duration_ticks":int(spec["duration_ticks"]),"stack_mode":"stack",
					"max_stacks":int(spec["max_stacks"]),"stat_modifiers":{"armor":-_value(spec,"armor_reduction",level),"magic_resist":-_value(spec,"magic_resist_reduction",level)}}}
			"execute":
				if float(event.get("target_health_ratio",1.0)) < float(spec["target_health_below"]):
					command = {"type":"modify_attack","plugin_id":uid,"damage_multiplier":1.0+_value(spec,"bonus_damage_multiplier",level)}
			"proc_volley":
				next_state["proc_count"] = int(next_state.get("proc_count",0))+1
				if int(next_state["proc_count"]) >= int(spec["procs_required"]):
					next_state["proc_count"] = 0
					command = {"type":"projectiles","plugin_id":uid,"actor_id":actor_id,"count":int(spec["projectile_count"]),"shape":"standard","base_damage":_value(spec,"extra_projectile_damage",level),"on_hit_efficiency":float(spec["on_hit_efficiency"]),"source_tags":["plugin_attack","extra_basic_attack"],"allowed_proc_triggers":["on_hit"]}
			"skill_followup":
				command = {"type":"projectiles","plugin_id":uid,"actor_id":actor_id,"target_id":target_id,"count":int(spec["projectile_count"]),"shape":"homing","base_damage":_value(spec,"base_damage",level),"source_tags":["plugin_attack"],"allowed_proc_triggers":[]}
			"grant_rare":
				command = {"type":"rare_reward_request","plugin_id":uid,"actor_id":actor_id,"count":int(_value(spec,"grant_count",level)),"requires_E_receipt":true}
			"kill_power":
				next_state["kill_power_stacks"] = int(next_state.get("kill_power_stacks",0))+1
			"disassembly":
				command = {"type":"status","target_id":target_id,"plugin_id":uid,"definition":{"id":uid,"source_id":actor_id,"duration_ticks":int(_value(spec,"duration_ticks",level)),"stack_mode":"stack","max_stacks":2147483647,"dot_dps":_value(spec,"dot_dps_per_stack",level),"dot_damage_type":"magic"}}
				if level >= 4:
					command["on_target_death"] = {"type":"explosion","plugin_id":uid,"balance_status":"proposal"}
			"extra_bullet":
				command = {"type":"projectiles","plugin_id":uid,"actor_id":actor_id,"count":int(spec["projectile_count"]),"shape":"standard","base_damage":_value(spec,"damage",level),"on_hit_efficiency":float(spec["on_hit_efficiency"]),"source_tags":["plugin_attack"],"allowed_proc_triggers":["on_hit"]}
			"homing":
				command = {"type":"projectiles","plugin_id":uid,"actor_id":actor_id,"count":int(spec["projectile_count"]),"shape":"homing","base_damage":_value(spec,"base_damage",level),"turn_rate":_value(spec,"turn_rate",level),"source_tags":["plugin_attack"],"allowed_proc_triggers":[]}
			"laser":
				command = {"type":"projectile_transform","plugin_id":uid,"shape":"laser","penetrating":true,"beam_width_mult":_value(spec,"beam_width_mult",level),"on_hit_efficiency":_value(spec,"on_hit_efficiency",level)}
			"boomerang":
				command = {"type":"projectile_transform","plugin_id":uid,"shape":"boomerang","count":int(_value(spec,"projectile_count",level)),"base_damage":_value(spec,"base_damage",level),"area_radius":_value(spec,"area_radius",level),"blocks_attack_until_return":true}
			"fever":
				next_state["fever_gauge"] = int(next_state.get("fever_gauge",0)) + int(spec["gauge_on_kill"] if kind=="kill" else spec["gauge_on_proc"])
				if int(next_state["fever_gauge"]) >= int(_value(spec,"gauge_required",level)):
					next_state["fever_gauge"] = 0
					next_state["fever_until"] = tick + int(_value(spec,"duration_ticks",level))
					command = {"type":"buff","plugin_id":uid,"actor_id":actor_id,"stat":"all_stats_fraction","value":_value(spec,"all_stats_fraction",level),"duration_ticks":int(_value(spec,"duration_ticks",level))}
		if not command.is_empty():
			commands.append(command)
		if cd_ticks > 0 and (not command.is_empty() or effect_kind == "breakable_sprint"):
			next_cooldowns[cooldown_key] = tick
	# Reject before mutation if there is no authoritative application channel.
	if not commands.is_empty():
		if not sink.is_valid():
			return {"ok":false,"reason":"missing_authoritative_sink","commands":commands.duplicate(true)}
		var applied: Variant = sink.call(commands.duplicate(true),world)
		if not (applied is Dictionary) or not bool(applied.get("ok",false)):
			return {"ok":false,"reason":str(applied.get("reason","sink_failed")) if applied is Dictionary else "sink_failed"}
	_state = next_state
	_cooldowns = next_cooldowns
	_seen[event_id] = true
	return {"ok":true,"commands":commands.duplicate(true),"state":_state.duplicate(true)}

func _attack_command(uid:String,actor_id:String,target_id:String,tick:int,event:Dictionary,spec:Dictionary,level:int,shape:String,damage:float) -> Dictionary:
	var path: Dictionary = Policy.try_extend_path(event.get("proc_path",[]),uid,actor_id,target_id)
	if not bool(path.get("allowed",false)):
		return {}
	return {"type":"damage","plugin_id":uid,"actor_id":actor_id,"target_id":target_id,"tick":tick,
		"shape":shape,"proc_path":path["proc_path"],
		"trigger_kind":"on_hit","source_tags":["plugin_attack"],
		"allowed_proc_triggers":["on_hit"] if uid in ["nv_c_chain_lightning","nv_c_hit_explosion"] else [],
		"attack":{"damage_type":str(spec.get("damage_type","magic")),"base_damage":damage,
			"ad_ratio":0.0,"ap_ratio":0.0,"can_crit":false,"is_on_hit":true,"is_area":false,"is_dot":false}}

func derived_attack(attack: Dictionary, event: Dictionary) -> Dictionary:
	## Pre-damage projection for B calculate(); does NOT mutate attacker or target.
	var projected: Dictionary = attack.duplicate(true)
	var ratio: float = float(event.get("target_health_ratio",1.0))
	if levels.has("nv_r_execute") and ratio < 0.5:
		var spec: Dictionary = _specs["nv_r_execute"]
		var multiplier: float = 1.0 + _value(spec,"bonus_damage_multiplier",int(levels["nv_r_execute"]))
		for coefficient in ["base_damage","ad_ratio","ap_ratio"]:
			projected[coefficient] = float(projected.get(coefficient,0.0)) * multiplier
	return projected
