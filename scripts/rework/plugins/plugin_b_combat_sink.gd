extends RefCounted
## C-owned glue that invokes B's ACTUAL damage/status APIs when installed.
## No copied resistance math, status stacking, or B event queue.
const DAMAGE_PATH := "res://scripts/rework/combat/damage.gd"
const STATUS_PATH := "res://scripts/rework/combat/statuses.gd"

static func available() -> bool:
	return ResourceLoader.exists(DAMAGE_PATH) and ResourceLoader.exists(STATUS_PATH)

static func apply(commands: Array, world: Dictionary) -> Dictionary:
	if not available():
		return {"ok":false,"reason":"B_combat_not_present"}
	# All-or-none dictionary publication. World/render side-effects happen
	# downstream after the authoritative commit, not during this calculation.
	var next: Dictionary = world.duplicate(true)
	if not next.has("targets"):
		next["targets"] = {}
	var damage_api: Script = load(DAMAGE_PATH)
	var statuses_api: Script = load(STATUS_PATH)
	var events: Array = next.get("generated_events", [])
	var projectiles: Array = next.get("projectiles", [])
	var transforms: Array = next.get("projectile_transforms", [])
	var buffs: Array = next.get("buffs", [])
	var knockbacks: Array = next.get("knockbacks", [])
	var rewards: Array = next.get("pending_progression_requests", [])
	var total_damage: float = 0.0
	for command in commands:
		if not command is Dictionary or not command.has("type"):
			return {"ok":false,"reason":"malformed_command"}
		var uid: String = str(command.get("plugin_id", ""))
		match str(command["type"]):
			"damage":
				var impact_id: String = str(command.get("target_id",""))
				if impact_id.is_empty() or not next["targets"].has(impact_id):
					return {"ok":false,"reason":"unknown_target"}
				var targets_hit: Array[String] = []
				var shape: String = str(command.get("shape",""))
				var positions: Dictionary = next.get("target_positions",{})
				var origin: Variant = positions.get(impact_id,null)
				# World supplies current target centers; deterministic ID order
				# breaks equidistant ties without any graphics RNG.
				var nearby: Array[Dictionary] = []
				if origin is Vector2 or origin is Vector3:
					for other_id in next["targets"].keys():
						if str(other_id)==impact_id and shape=="lightning":
							continue
						var destination: Variant = positions.get(other_id,null)
						if typeof(origin) != typeof(destination):
							continue
						var distance: float = origin.distance_to(destination)
						nearby.append({"target_id":str(other_id),"distance":distance})
					nearby.sort_custom(func(left: Dictionary, right: Dictionary) -> bool:
						if is_equal_approx(float(left["distance"]),float(right["distance"])):
							return str(left["target_id"]) < str(right["target_id"])
						return float(left["distance"]) < float(right["distance"]))
				if shape=="lightning":
					var count: int = maxi(0,int(command.get("chain_targets",1)))
					if nearby.is_empty():
						return {"ok":false,"reason":"missing_adjacent_chain_target"}
					var max_reach: float = float(command.get("search_distance_mult",1.0)) * float(next.get("lightning_base_reach",8.0))
					for neighbor in nearby:
						if float(neighbor["distance"]) <= max_reach and targets_hit.size() < count:
							targets_hit.append(str(neighbor["target_id"]))
				elif bool(command.get("attack",{}).get("is_area",false)) and not nearby.is_empty():
					var area_radius: float = float(command.get("area_radius",0.0))
					for neighbor in nearby:
						if float(neighbor["distance"]) <= area_radius:
							targets_hit.append(str(neighbor["target_id"]))
				else:
					targets_hit.append(impact_id)
				for victim_id in targets_hit:
					var defender: Dictionary = next["targets"][victim_id]
					var attacker: Dictionary = next.get("actor_stats", {})
					var attack: Dictionary = command.get("attack", {})
					var status_map: Dictionary = next.get("target_statuses",{})
					var modified_defender: Dictionary = statuses_api.modified_stats(defender,status_map.get(victim_id,{"statuses":[]}))
					var calculated: Dictionary = damage_api.calculate(attacker, modified_defender, attack, false)
					if not calculated.get("ok",false):
						return {"ok":false,"reason":"B_damage_invalid"}
					var hit: Dictionary = damage_api.apply_damage(defender, calculated, bool(defender.get("invulnerable",false)))
					if not hit.get("ok",false):
						return {"ok":false,"reason":"B_damage_apply_failed"}
					next["targets"][victim_id] = defender
					total_damage += float(hit.get("actual_damage",0.0))
					events.append({"kind":"on_hit_proc","actor_id":str(command.get("actor_id","")),"target_id":victim_id,
						"plugin_id":uid,"tick":int(command.get("tick",0)),
						"actual_damage":float(hit.get("actual_damage",0.0)),
						"source_tags":command.get("source_tags",[]),"proc_path":command.get("proc_path",[]),
						"allowed_proc_triggers":command.get("allowed_proc_triggers",[]),
						"killed":bool(hit.get("killed",false))})
			"status":
				var target_id: String = str(command.get("target_id",""))
				if target_id.is_empty() or not next["targets"].has(target_id):
					return {"ok":false,"reason":"unknown_status_target"}
				var statuses: Dictionary = next.get("target_statuses", {})
				var target_status: Dictionary = statuses.get(target_id,{"statuses":[]})
				var applied: Dictionary = statuses_api.apply(target_status,command.get("definition",{}))
				if not applied.get("ok",false):
					return {"ok":false,"reason":"B_status_rejected"}
				statuses[target_id] = target_status
				next["target_statuses"] = statuses
			"shield":
				# A owns shield-vs-damage ordering; this is a real stored barrier
				# charge (not an invented B shield-damage formula).
				var current: float = maxf(0.0,float(next.get("actor_shield",0.0)))
				next["actor_shield"] = current + maxf(0.0,float(command.get("amount",0.0)))
			"buff":
				buffs.append(command.duplicate(true))
			"knockback":
				knockbacks.append(command.duplicate(true))
			"projectiles":
				var count: int = int(command.get("count",0))
				if count < 0 or count > 128:
					return {"ok":false,"reason":"invalid_projectile_count"}
				for i in range(count):
					var projectile: Dictionary = command.duplicate(true)
					projectile["projectile_index"] = i
					projectiles.append(projectile)
			"projectile_transform":
				transforms.append(command.duplicate(true))
			"modify_attack":
				var incoming: Dictionary = next.get("pending_attack", {})
				if incoming.is_empty():
					return {"ok":false,"reason":"missing_pending_attack"}
				for coefficient in ["base_damage","ad_ratio","ap_ratio"]:
					incoming[coefficient] = float(incoming.get(coefficient,0.0)) * float(command.get("damage_multiplier",1.0))
				next["pending_attack"] = incoming
			"rare_reward_request":
				# E must persist a separate receipt before it is displayed.
				rewards.append(command.duplicate(true))
			_:
				return {"ok":false,"reason":"unknown_effect_command"}
	next["generated_events"] = events
	next["projectiles"] = projectiles
	next["projectile_transforms"] = transforms
	next["buffs"] = buffs
	next["knockbacks"] = knockbacks
	next["pending_progression_requests"] = rewards
	world.clear()
	world.merge(next,true)
	return {"ok":true,"damage_done":total_damage,"commands_applied":commands.size()}

static func tick_dots(world: Dictionary) -> Dictionary:
	## Integrates B Statuses.tick request with B Damage.calculate/apply_damage.
	## Use once per authoritative fixed 60Hz frame. World stays unchanged on failure.
	if not available():
		return {"ok":false,"reason":"B_combat_not_present"}
	var damage_api: Script = load(DAMAGE_PATH)
	var statuses_api: Script = load(STATUS_PATH)
	var next: Dictionary = world.duplicate(true)
	var statuses: Dictionary = next.get("target_statuses", {})
	var total_damage: float = 0.0
	for target_id in statuses.keys():
		if not next.get("targets",{}).has(target_id):
			return {"ok":false,"reason":"status_target_missing"}
		var state: Dictionary = statuses[target_id]
		var generated: Dictionary = statuses_api.tick(state)
		statuses[target_id] = state
		var defender: Dictionary = next["targets"][target_id]
		for request in generated.get("damage_requests",[]):
			var calculated: Dictionary = damage_api.calculate(next.get("actor_stats",{}),defender,request,false)
			if not bool(calculated.get("ok",false)):
				return {"ok":false,"reason":"B_dot_damage_invalid"}
			var hit: Dictionary = damage_api.apply_damage(defender,calculated,bool(defender.get("invulnerable",false)))
			if not bool(hit.get("ok",false)):
				return {"ok":false,"reason":"B_dot_damage_failed"}
			total_damage += float(hit.get("actual_damage",0.0))
		next["targets"][target_id] = defender
	next["target_statuses"] = statuses
	world.clear()
	world.merge(next,true)
	return {"ok":true,"damage_done":total_damage}
