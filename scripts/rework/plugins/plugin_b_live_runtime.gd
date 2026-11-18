extends "res://scripts/rework/combat/combat_runtime.gd"
## C-only opt-in subclass of B production 60 Hz runtime.
## B owns combat physics, damage, statuses, proc queue, and projectile collision.
## A must instantiate this subclass (rather than demo_combat_bridge) to enable it.
const CEffects = preload("res://scripts/rework/plugins/plugin_effect_runtime.gd")
const CSpecs = preload("res://scripts/rework/plugins/plugin_catalog.gd")
const DT_C: float = 1.0 / 60.0
var effect_owners: Dictionary = {}
var b_event_hits: Dictionary = {}
var outstanding_boomerangs: Dictionary = {}
var pending_e_rewards: Array[Dictionary] = [] # Deprecated: no C/B bonus grants.
var plugin_metrics: Dictionary = {"routed":0,"commands":0,"blocked":0,"pending_max":0,"physical_hits":0}
var _active_parent: Dictionary = {}
var _in_effect_dispatch: bool = false

func attach_plugins(actor_id: String, owned_levels: Dictionary, seed_value: int = 0) -> bool:
	if not actors.has(actor_id):
		return false
	# Production A/B already supports a damage_absorber callback (used by
	# B's CAdapter). Running BOTH the subclass and adapter would double procs.
	if stat_provider.is_valid() or attack_provider.is_valid() or fire_gate_provider.is_valid():
		return false
	if damage_absorber.is_valid() and damage_absorber.get_object() != self:
		return false
	var engine = CEffects.new()
	if not engine.configure(owned_levels, seed_value):
		return false
	effect_owners[actor_id] = engine
	# Use B's authoritative pre-HP application hook, never heal after damage.
	# This is the same provisional shield-first policy used by A CAdapter.
	damage_absorber = Callable(self,"_absorb_c_shield")
	# B only checks whether this array is nonempty for periodic event submission.
	# Dummy descriptor has no trigger and cannot duplicate C's attacks.
	actors[actor_id]["plugins"] = [{"plugin_id":"__c_live_marker","trigger":{"event":"disabled"}}] if not owned_levels.is_empty() else []
	return true

func _absorb_c_shield(target_id: String, calculation: Dictionary) -> Dictionary:
	if not actors.has(target_id):
		return calculation
	var pool: float = maxf(0.0,float(actors[target_id].get("c_shield_pending",0.0)))
	var pending_damage: float = maxf(0.0,float(calculation.get("calculated_damage",0.0)))
	var absorbed: float = minf(pool,pending_damage)
	if absorbed <= 0.0:
		return calculation
	actors[target_id]["c_shield_pending"] = pool-absorbed
	calculation["calculated_damage"] = pending_damage-absorbed
	presentation.append({"kind":"shield_absorbed","actor_id":target_id,
		"amount":absorbed,"policy":"provisional_shield_first"})
	return calculation

func acknowledge_plugin_level(actor_id: String, plugin_id: String, level: int) -> bool:
	if not effect_owners.has(actor_id):
		return false
	return effect_owners[actor_id].set_plugin_level(plugin_id,level)

func _effective_stats(actor: Dictionary) -> Dictionary:
	var base: Dictionary = super._effective_stats(actor)
	var actor_id: String = str(actor.get("id",""))
	if effect_owners.has(actor_id):
		return effect_owners[actor_id].derived_stats(base,fixed_tick)
	return base

func _fire_if_ready(actor_id: String, aim: Vector2) -> void:
	if int(outstanding_boomerangs.get(actor_id,0)) > 0:
		actors[actor_id]["fire_credit"] = minf(1.0,float(actors[actor_id]["fire_credit"]))
		return
	super._fire_if_ready(actor_id,aim)

func _spawn(actor_id: String, direction: Vector2, attack: Dictionary,
		tags: Array = ["basic_attack", "projectile_hit"], overrides: Dictionary = {}) -> String:
	var id: String = super._spawn(actor_id,direction,attack,tags,overrides)
	if id.is_empty():
		return id
	var engine = effect_owners.get(actor_id,null)
	var levels: Dictionary = engine.levels if engine != null else {}
	var laser_level: int = int(levels.get("nv_l_laser",0))
	var boomerang_level: int = int(levels.get("nv_l_boomerang",0))
	for p in projectiles:
		if str(p.get("id","")) != id:
			continue
		var shape: String = str(overrides.get("shape","standard"))
		if tags.has("basic_attack") and laser_level>0:
			shape = "laser"
		if tags.has("basic_attack") and boomerang_level>0:
			# Overlapping mutually-exclusive projectile forms must not silently
			# stack. B/A to approve precedence; prefer no fabricated result.
			if shape=="laser":
				p["c_shape_conflict"] = "laser_boomerang_unapproved"
			else:
				shape = "boomerang"
		if shape=="laser":
			p["infinite_pierce"] = true
			p["radius"] = float(p["radius"]) * float(engine.effect_definition("nv_l_laser").get("beam_width_mult",[1.0])[mini(laser_level-1,2)]) if engine != null and laser_level>0 else float(p["radius"])
			p["c_shape"] = "laser"
		elif shape=="homing" or (tags.has("basic_attack") and int(levels.get("nv_r_homing",0))>0):
			p["c_shape"] = "homing"
			p["c_turn_rate"] = float(overrides.get("turn_rate",3.0))
		elif shape=="boomerang":
			p["c_shape"] = "boomerang"
			p["c_return_tick"] = fixed_tick+20
			p["c_returning"] = false
			outstanding_boomerangs[actor_id] = int(outstanding_boomerangs.get(actor_id,0))+1
		break
	return id

func _retire(projectile: Dictionary) -> void:
	if str(projectile.get("c_shape",""))=="boomerang":
		var actor_id: String = str(projectile.get("actor_id",""))
		outstanding_boomerangs[actor_id] = maxi(0,int(outstanding_boomerangs.get(actor_id,0))-1)
	super._retire(projectile)

func _step_projectiles() -> void:
	for p in projectiles:
		var shooter: String = str(p.get("actor_id",""))
		if str(p.get("c_shape",""))=="homing":
			var victim: String = _closest_opponent(shooter, p.get("position",Vector3.ZERO))
			if actors.has(victim):
				var from_pos: Vector3 = p["position"]
				var to_pos: Vector3 = actors[victim]["position"]
				var desired := Vector2(to_pos.x-from_pos.x,to_pos.z-from_pos.z)
				if desired.length_squared()>0.000001:
					var current: Vector2 = p["velocity"]
					if current.length_squared()>0.000001:
						var angle: float = clampf(current.angle_to(desired),-float(p.get("c_turn_rate",3.0))*DT_C,float(p.get("c_turn_rate",3.0))*DT_C)
						p["velocity"] = current.rotated(angle)
		if str(p.get("c_shape",""))=="boomerang" and fixed_tick>=int(p.get("c_return_tick",2147483647)):
			if actors.has(shooter):
				var from_pos: Vector3 = p["position"]
				var to_pos: Vector3 = actors[shooter]["position"]
				var home := Vector2(to_pos.x-from_pos.x,to_pos.z-from_pos.z)
				if home.length_squared()>0.00001:
					p["velocity"] = home.normalized()*maxf(1.0,float(p["velocity"].length()))
					p["c_returning"]=true
	super._step_projectiles()
	for index in range(projectiles.size()-1,-1,-1):
		var p: Dictionary = projectiles[index]
		var id: String = str(p.get("actor_id",""))
		if bool(p.get("c_returning",false)) and actors.has(id):
			var separation: Vector3 = p["position"]-actors[id]["position"]
			if Vector2(separation.x,separation.z).length_squared() < 0.25:
				_retire(p)
				projectiles.remove_at(index)

func _resolve_event(event: Dictionary) -> Array:
	var actor_id: String = str(event.get("actor_id",""))
	var target_id: String = str(event.get("target_id",""))
	var is_damage: bool = str(event.get("kind",""))=="damage"
	# Execute must scale BEFORE B calculates armor, damage, lifesteal and death.
	if is_damage and effect_owners.has(actor_id) and actors.has(target_id):
		var victim: Dictionary = actors[target_id]["stats"]
		var ratio: float = float(victim["health"])/maxf(1.0,float(victim["max_health"]))
		if event.has("attack") and not event.has("command"):
			event["attack"] = effect_owners[actor_id].derived_attack(event["attack"],{"target_health_ratio":ratio})
	var original: Dictionary = event.duplicate(true)
	var base_requests: Array = super._resolve_event(event)
	if not effect_owners.has(actor_id):
		return base_requests
	var kind: String = str(event.get("kind",""))
	var mapped: Array[String] = []
	var tags: Array = event.get("source_tags",[])
	if kind=="damage" and float(event.get("actual_damage",0.0))>0.0:
		mapped.append("on_hit")
		mapped.append("damage_dealt")
		if bool(event.get("critical",false)):
			mapped.append("on_crit")
		if tags.has("skill_damage"):
			mapped.append("skill_damage")
		if tags.has("proc_damage") or tags.has("plugin_attack") or tags.has("plugin"):
			mapped.append("on_hit_proc")
	elif kind=="periodic":
		mapped.append("tick")
	elif kind in ["basic_attack","dash","skill_cast","kill","damage_taken"]:
		mapped.append(kind)
	for translated in mapped:
		var enriched: Dictionary = event.duplicate(true)
		enriched["event_id"] = str(event.get("event_id",""))+":C:"+translated
		enriched["kind"] = translated
		enriched["tick"] = int(event.get("tick",fixed_tick))
		if kind=="damage" and actors.has(target_id):
			var health: Dictionary = actors[target_id]["stats"]
			enriched["target_health_ratio"] = float(health["health"])/maxf(1.0,float(health["max_health"]))
		if kind=="damage_taken" and actors.has(actor_id):
			var health: Dictionary = actors[actor_id]["stats"]
			enriched["actor_health_ratio"] = float(health["health"])/maxf(1.0,float(health["max_health"]))
			enriched["actual_damage"] = float(event.get("actual_damage",1.0))
		if not event.get("proc_path",[]).is_empty() or tags.has("plugin"):
			var safe_tags: Array = tags.duplicate()
			if not safe_tags.has("plugin_attack"):
				safe_tags.append("plugin_attack")
			enriched["source_tags"] = safe_tags
		_active_parent = event
		var result: Dictionary = effect_owners[actor_id].process(enriched,
			{"actor_stats":_effective_stats(actors[actor_id])},
			Callable(self,"_submit_effect_commands"))
		_active_parent = {}
		if not bool(result.get("ok",false)):
			plugin_metrics["blocked"] += 1
		else:
			plugin_metrics["routed"] += 1
			plugin_metrics["commands"] += result.get("commands",[]).size()
	return base_requests

func _submit_effect_commands(commands: Array, _world: Dictionary) -> Dictionary:
	# The C runtime commits cooldown and counters only if this dispatch accepts.
	# B itself applies all damage/status/projectile commands from its FIFO queue.
	if _active_parent.is_empty():
		return {"ok":false,"reason":"missing_B_parent"}
	var actor_id: String = str(_active_parent.get("actor_id",""))
	if not actors.has(actor_id):
		return {"ok":false,"reason":"missing_actor"}
	var before_pending: Array[Dictionary] = bus.pending.duplicate(true)
	var before_last: Dictionary = bus.last_proc_tick.duplicate(true)
	var before_collisions: Dictionary = bus.seen_collision_targets.duplicate(true)
	var before_metrics: Dictionary = bus.metrics.duplicate(true)
	var before_sequence: int = bus.next_sequence
	var proposals: Array[Dictionary] = []
	var delayed_mutations: Array[Dictionary] = []
	for command in commands:
		if not command is Dictionary:
			return {"ok":false,"reason":"invalid_command"}
		var typ: String = str(command.get("type",""))
		var uid: String = str(command.get("plugin_id",""))
		var targets: Array[String] = []
		var target_id: String = str(command.get("target_id",_active_parent.get("target_id","")))
		if typ=="damage":
			if not actors.has(target_id):
				return {"ok":false,"reason":"missing_damage_target"}
			if str(command.get("shape",""))=="lightning":
				var possibles: Array[Dictionary] = []
				var center: Vector3 = actors[target_id]["position"]
				for oid in _ids():
					if oid==target_id or actors[oid]["team"]==actors[actor_id]["team"] or not actors[oid]["alive"]:
						continue
					var dest: Vector3 = actors[oid]["position"]
					var distance: float = Vector2(dest.x-center.x,dest.z-center.z).length()
					if distance <= float(command.get("search_distance_mult",1.0))*8.0:
						possibles.append({"id":oid,"distance":distance})
				possibles.sort_custom(func(a: Dictionary,b: Dictionary)->bool:
					return float(a["distance"])<float(b["distance"]) if not is_equal_approx(float(a["distance"]),float(b["distance"])) else str(a["id"])<str(b["id"]))
				for i in range(mini(int(command.get("chain_targets",1)),possibles.size())):
					targets.append(str(possibles[i]["id"]))
			elif bool(command.get("attack",{}).get("is_area",false)):
				var origin: Vector3 = actors[target_id]["position"]
				for oid in _ids():
					if actors[oid]["team"]==actors[actor_id]["team"] or not actors[oid]["alive"]:
						continue
					var point: Vector3 = actors[oid]["position"]
					if Vector2(point.x-origin.x,point.z-origin.z).length() <= float(command.get("area_radius",0.0))+float(actors[oid].get("radius",0.0)):
						targets.append(str(oid))
			else:
				targets.append(target_id)
			for tid in targets:
				proposals.append({"effect":uid,"kind":"damage","target":tid,"trigger":str(_active_parent.get("trigger_kind","on_hit")),"attack":command["attack"],"tags":["plugin","plugin_attack","proc_damage"],"allowed":command.get("allowed_proc_triggers",[])})
		elif typ=="status":
			proposals.append({"effect":uid,"kind":"status","target":target_id,"trigger":str(_active_parent.get("trigger_kind","on_hit")),"command":{"status":command["definition"],"target_policy":"current"},"tags":["plugin"],"allowed":[]})
		elif typ=="buff":
			var field: String = str(command.get("stat",""))
			if field!="all_stats_fraction":
				proposals.append({"effect":uid,"kind":"stat_modifier","target":actor_id,"trigger":str(_active_parent.get("trigger_kind","skill_cast")),"command":{"target_policy":"self","duration_ticks":int(command.get("duration_ticks",60)),"stats":{field:float(command.get("value",0.0))}},"tags":["plugin"],"allowed":[]})
		elif typ=="projectiles":
			var shot: Dictionary = {"damage_type":"physical","base_damage":float(command.get("base_damage",0.0)),
				"ad_ratio":0.0,"ap_ratio":0.0,"can_crit":false,"is_on_hit":true}
			proposals.append({"effect":uid,"kind":"spawn_projectile","target":target_id,
				"trigger":str(_active_parent.get("trigger_kind","basic_attack")),
				"command":{"count":int(command.get("count",1)),"attack":shot,
					"shape":command.get("shape","standard"),"speed":float(command.get("speed",18.0)),
					"turn_rate":float(command.get("turn_rate",3.0))},"tags":["plugin","plugin_attack"],"allowed":command.get("allowed_proc_triggers",[]),"efficiency":float(command.get("on_hit_efficiency",1.0))})
		elif typ=="projectile_transform":
			# Existing shot is modified by _spawn; don't double-spawn it.
			continue
		elif typ=="rare_reward_notice":
			# An unverified acquisition event must never request another E payout.
			continue
		elif typ=="shield":
			# A-approved integration prototype uses B damage_absorber for
			# real shield-first HP mitigation; stacking/expiry are provisional.
			delayed_mutations.append(command.duplicate(true))
		elif typ=="knockback":
			var victim: String = target_id
			if actors.has(victim) and victim!=actor_id:
				delayed_mutations.append(command.duplicate(true))
		elif typ=="modify_attack":
			# Execute is applied before B Damage.calculate by derived_attack.
			continue
		else:
			return {"ok":false,"reason":"unsupported_C_command:"+typ}
	# Publish to B queue, reverting any partial queue on rejection.
	for index in range(proposals.size()):
		var p: Dictionary = proposals[index]
		var data: Dictionary = {"kind":p["kind"],"actor_id":actor_id,"target_id":p["target"],
			"trigger_kind":p["trigger"],"source_tags":p["tags"],
			"allowed_proc_triggers":p["allowed"]}
		if p.has("attack"):
			data["attack"] = p["attack"]
		if p.has("command"):
			data["command"] = p["command"]
		if p.has("efficiency"):
			data["on_hit_damage_efficiency"] = p["efficiency"]
		if bus.submit_proc(_active_parent,str(p["effect"])+":"+str(index),data,0).is_empty():
			bus.pending = before_pending
			bus.last_proc_tick = before_last
			bus.seen_collision_targets = before_collisions
			bus.metrics = before_metrics
			bus.next_sequence = before_sequence
			return {"ok":false,"reason":"B_proc_rejected"}
	# C side effects must NEVER survive a rejected B proc batch.
	for item in delayed_mutations:
		match str(item.get("type","")):
			"rare_reward_notice":
				pass # E is sole authority; no C-side payout queue.
			"shield":
				actors[actor_id]["c_shield_pending"] = float(actors[actor_id].get("c_shield_pending",0.0))+float(item.get("amount",0.0))
			"knockback":
				var victim: String = str(item.get("target_id",_active_parent.get("target_id","")))
				if actors.has(victim) and victim!=actor_id:
					var displacement: Vector3 = actors[victim]["position"]-actors[actor_id]["position"]
					if displacement.length_squared()>0.000001:
						actors[victim]["position"] += displacement.normalized()*float(item.get("force",0.0))
	return {"ok":true,"queued":proposals.size()}
