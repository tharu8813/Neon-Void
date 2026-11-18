extends RefCounted
## C source tests executed against B's REAL combat_runtime and event FIFO.
## Never count emitted proposal dictionaries as live execution proof.
const Live = preload("res://scripts/rework/plugins/plugin_b_live_runtime.gd")
const Catalog = preload("res://scripts/rework/plugins/plugin_catalog.gd")
var failures: Array[String] = []
var verified: Array[String] = []
var not_verified: Array[String] = []
var dispatch_total: int = 0
var max_pending: int = 0

func check(ok: bool, description: String) -> void:
	if not ok:
		failures.append(description)
		printerr("C B LIVE FAIL: "+description)

func new_encounter(uid: String, level: int = 1):
	var combat = Live.new(419, {"encounter_id":"c-live-"+uid,"event_work_per_tick":512})
	check(combat.register_actor("P","vaccine",Vector3.ZERO,{
		"max_health":100.0,"health":100.0,"attack_damage":5.0,
		"ability_power":10.0,"attack_speed":0.0,
		"move_speed":5.0,"armor":0.0,"magic_resist":0.0},
		{"player_controlled":true,"projectile_speed":120.0,
		"dash_definition":{"distance":2.0,"cooldown_ticks":30}}),"register B vaccine: "+uid)
	check(combat.register_actor("E1","virus",Vector3(2,0,0),
		{"max_health":1000.0,"health":1000.0,"armor":0.0,
		"magic_resist":0.0,"attack_speed":0.0},
		{"attack_range":0.0,"preferred_min_range":0.0}),"register B enemy 1")
	check(combat.register_actor("E2","virus",Vector3(4,0,0),
		{"max_health":1000.0,"health":1000.0,"armor":0.0,
		"magic_resist":0.0,"attack_speed":0.0},
		{"attack_range":0.0,"preferred_min_range":0.0}),"register B enemy 2")
	check(combat.attach_plugins("P",{uid:level},419),"attach C catalog level to B: "+uid)
	combat.bus.set_tick(100)
	combat.fixed_tick=100
	return combat

func fire_event(combat, event_kind: String, tags: Array = ["basic_attack","projectile_hit"], damage: float = 10.0) -> void:
	var args: Dictionary = {"kind":event_kind,"actor_id":"P","target_id":"E1",
		"trigger_kind":"on_hit" if event_kind=="damage" else event_kind,
		"source_tags":tags.duplicate(),
		"allowed_proc_triggers":["on_hit","on_crit","damage_taken","basic_attack",
			"dash","skill_cast","kill","periodic"],"attack":{
			"damage_type":"physical","base_damage":damage,"ad_ratio":0.0,
			"ap_ratio":0.0,"is_on_hit":true,"can_crit":false}}
	combat.bus.submit_root(args)
	# This is the official B event executor, NOT a fake effect sink.
	var processed: Array[Dictionary] = combat.bus.drain_batch(512,Callable(combat,"_resolve_event"))
	dispatch_total+=processed.size()
	max_pending=maxi(max_pending,int(combat.bus.metrics.get("max_pending",0)))


func verify_shield_hp(combat, uid: String) -> bool:
	# Use real B Damage.calculate + damage_absorber + apply_damage.
	# Same provisional shield-first rule as A's c_effect_adapter.
	var before: float = float(combat.actors["P"].get("c_shield_pending",0.0))
	check(before>0.0 and before<100.0,uid+" produced shield from real B event")
	if before<=0.0:
		return false
	combat.bus.submit_root({"kind":"damage","trigger_kind":"on_hit",
		"actor_id":"E1","target_id":"P",
		"source_tags":["hostile_attack"],"allowed_proc_triggers":[],
		"attack":{"damage_type":"true","base_damage":before+5.0,
			"ad_ratio":0.0,"ap_ratio":0.0,"can_crit":false,"is_on_hit":true}})
	combat.bus.drain_batch(512,Callable(combat,"_resolve_event"))
	var hp: float = float(combat.snapshot("P")["stats"]["health"])
	var after: float = float(combat.actors["P"].get("c_shield_pending",0.0))
	check(is_equal_approx(hp,95.0) and is_zero_approx(after),
		uid+" absorbs B damage before HP, then spills exactly 5")
	combat.bus.submit_root({"kind":"damage","trigger_kind":"on_hit",
		"actor_id":"E1","target_id":"P",
		"source_tags":["hostile_attack"],"allowed_proc_triggers":[],
		"attack":{"damage_type":"true","base_damage":8.0,
			"ad_ratio":0.0,"ap_ratio":0.0,"can_crit":false,"is_on_hit":true}})
	combat.bus.drain_batch(512,Callable(combat,"_resolve_event"))
	var hp_after: float = float(combat.snapshot("P")["stats"]["health"])
	check(is_equal_approx(hp_after,87.0) and
		is_zero_approx(float(combat.actors["P"].get("c_shield_pending",0.0))),
		uid+" cannot reuse already consumed shield")
	return is_equal_approx(hp_after,87.0) and is_zero_approx(after)

func run() -> bool:
	var plugins: Array[Dictionary] = Catalog.entries()
	var verified_battle: Dictionary = {}
	for entry in plugins:
		var uid: String = str(entry["plugin_id"])
		var combat = new_encounter(uid)
		var engine = combat.effect_owners["P"]
		var kind: String = str(engine.effect_definition(uid).get("kind",""))
		var source_before: Dictionary = combat._effective_stats(combat.actors["P"])
		var hp_before: float = float(combat.snapshot("E1")["stats"]["health"])
		var other_hp: float = float(combat.snapshot("E2")["stats"]["health"])
		var mutated: bool = false
		match kind:
			"random_stats","passive_stat","dual_penetration","dual_speed","breakable_sprint":
				var vanilla = Live.new(419)
				vanilla.register_actor("P","vaccine",Vector3.ZERO,combat.actors["P"]["stats"],{"player_controlled":true})
				mutated = combat._effective_stats(combat.actors["P"]) != vanilla._effective_stats(vanilla.actors["P"])
			"periodic_shield":
				combat.bus.set_tick(1800)
				combat.fixed_tick=1800
				fire_event(combat,"periodic",["periodic"])
				mutated = verify_shield_hp(combat,uid)
			"skill_shield","dash_shield":
				fire_event(combat,"skill_cast" if kind=="skill_shield" else "dash",
					["skill_cast"] if kind=="skill_shield" else ["dash"])
				mutated = verify_shield_hp(combat,uid)
			"low_hp_emergency":
				combat.actors["P"]["stats"]["health"]=20.0
				fire_event(combat,"damage_taken",["damage_taken"])
				mutated = combat.actors["P"]["status_state"]["statuses"].size()>0
			"low_hp_knockback":
				combat.actors["P"]["stats"]["health"]=20.0
				var old: Vector3 = combat.actors["E1"]["position"]
				fire_event(combat,"damage_taken",["damage_taken"])
				mutated = combat.actors["E1"]["position"] != old
			"mobility_buff":
				fire_event(combat,"dash",["dash"])
				mutated = float(combat._effective_stats(combat.actors["P"]).get("move_speed",0.0))>float(source_before.get("move_speed",0.0))
			"chain_lightning":
				fire_event(combat,"damage")
				mutated = float(combat.snapshot("E2")["stats"]["health"]) < other_hp
			"hit_explosion","dash_explosion":
				fire_event(combat,"dash",["dash"]) if kind=="dash_explosion" else fire_event(combat,"damage")
				mutated = float(combat.snapshot("E2")["stats"]["health"]) < other_hp
			"armor_shred":
				fire_event(combat,"damage")
				mutated = combat.actors["E1"]["status_state"]["statuses"].size()>0
			"execute":
				combat.actors["E1"]["stats"]["health"]=400.0
				fire_event(combat,"damage",["basic_attack","projectile_hit"],10.0)
				mutated = float(combat.snapshot("E1")["stats"]["health"]) < 388.6
			"proc_volley","disassembly":
				for i in range(4 if kind=="proc_volley" else 1):
					fire_event(combat,"damage",["plugin","plugin_attack","proc_damage","projectile_hit"])
				mutated = combat.counters["shots"]>0 if kind=="proc_volley" else combat.actors["E1"]["status_state"]["statuses"].size()>0
			"skill_followup":
				fire_event(combat,"damage",["skill_damage","projectile_hit"])
				mutated = combat.counters["shots"]>0
			"grant_rare":
				mutated = false # Only E transaction can grant this reward.
			"kill_power":
				combat.actors["E1"]["stats"]["health"]=5.0
				fire_event(combat,"damage",["basic_attack","projectile_hit"],10.0)
				mutated = float(combat._effective_stats(combat.actors["P"])["attack_damage"]) > 5.0
			"extra_bullet","homing":
				fire_event(combat,"basic_attack",["basic_attack"])
				mutated = combat.counters["shots"]>0
			"laser","boomerang":
				var shot_id: String = combat._spawn("P",Vector2.RIGHT,{
					"damage_type":"physical","base_damage":5.0,"is_on_hit":true,
					"can_crit":false},["basic_attack","projectile_hit"])
				mutated = not shot_id.is_empty() and combat.projectiles.size()>0 and str(combat.projectiles[0].get("c_shape",""))==kind
			"fever":
				for i in range(10):
					fire_event(combat,"kill",["kill"])
				mutated = float(combat._effective_stats(combat.actors["P"]).get("attack_damage",0.0)) > 5.0
			_:
				check(false,"missing C battle test trigger: "+uid+" / "+kind)
		if mutated:
			verified_battle[uid] = true
		else:
			not_verified.append(uid)
		if kind != "grant_rare":
			check(mutated,"B world effect missing: "+uid+" ["+kind+"]")
		var max_level: int = int(entry.get("max_level",1))
		check(not combat.attach_plugins("P",{uid:max_level+1},10),
			"reject level past cap: "+uid)
	# Main anti-loop + duplicate damage proof with both real physical targets.
	var combo = new_encounter("nv_c_chain_lightning")
	combo.effect_owners["P"].set_plugin_level("nv_c_hit_explosion",1)
	combo.effect_owners["P"].set_plugin_level("nv_r_disassembly",1)
	fire_event(combo,"damage")
	check(combo.bus.pending.is_empty(),"B drains finite three-effect chain")
	check(int(combo.counters["damage_events"]) <= 40,
		"chain cycle protection prevents unbounded extra damage")
	check(float(combo.snapshot("E2")["stats"]["health"])<1000.0,
		"real B lightning/explosion combo damages second enemy")
	var before_repeat: int = int(combo.counters["damage_events"])
	var duplicate = combo.effect_owners["P"]
	var processed_again: Dictionary = duplicate.process({"event_id":"same-root","kind":"on_hit",
		"actor_id":"P","target_id":"E1","tick":combo.fixed_tick+500,
		"source_tags":["basic_attack"],"allowed_proc_triggers":["on_hit"]},
		{},Callable(combo,"_submit_effect_commands"))
	check(not processed_again.get("ok",false),"cannot dispatch without authoritative B parent")
	check(int(combo.counters["damage_events"])==before_repeat,
		"failed direct C dispatch does not double damage")
	# Actual B swept collision: one laser pierces TWO distinct physical enemies.
	var laser_run = new_encounter("nv_l_laser")
	laser_run._spawn("P",Vector2.RIGHT,{"damage_type":"physical",
		"base_damage":11.0,"can_crit":false,"is_on_hit":true},["basic_attack","projectile_hit"])
	for i in range(3):
		laser_run.step_fixed({"P":{"fire":false,"move":Vector2.ZERO}})
	check(float(laser_run.snapshot("E1")["stats"]["health"])<1000.0
		and float(laser_run.snapshot("E2")["stats"]["health"])<1000.0,
		"laser B physical collision actually pierces both targets")
	# Real B projectile homing changes velocity, not just a JSON shape.
	var homing_run = new_encounter("nv_r_homing")
	homing_run._spawn("P",Vector2.UP,{"damage_type":"physical",
		"base_damage":3.0,"can_crit":false,"is_on_hit":true},["basic_attack","projectile_hit"])
	homing_run.step_fixed({"P":{"fire":false,"move":Vector2.ZERO}})
	check(homing_run.projectiles.size()>0
		and float(homing_run.projectiles[0]["velocity"].x)>0.0,
		"homing shot physically steers toward the target on B tick")
	# Boomerang uses B physical projectile lifecycle; returns unlock firing.
	var boom_run = new_encounter("nv_l_boomerang")
	boom_run.set_actor_position("E1",Vector3(70,0,0))
	boom_run.set_actor_position("E2",Vector3(90,0,0))
	boom_run._spawn("P",Vector2.RIGHT,{"damage_type":"physical",
		"base_damage":1.0,"can_crit":false,"is_on_hit":true},["basic_attack","projectile_hit"])
	check(int(boom_run.outstanding_boomerangs.get("P",0))==1,
		"boomerang registered outstanding physical projectile")
	var old_shots: int = int(boom_run.counters["shots"])
	boom_run.actors["P"]["fire_credit"] = 1.0
	boom_run._fire_if_ready("P",Vector2.RIGHT)
	check(int(boom_run.counters["shots"])==old_shots,
		"blocked attack while B boomerang is in flight")
	for i in range(65):
		boom_run.step_fixed({"P":{"fire":false,"move":Vector2.ZERO}})
	check(int(boom_run.outstanding_boomerangs.get("P",0))==0,
		"boomerang B physics return releases attack gate")
	# B statuses must deal actual per-tick damage, not merely be installed.
	var dot_run = new_encounter("nv_r_disassembly")
	fire_event(dot_run,"damage",["plugin","plugin_attack","proc_damage","projectile_hit"])
	var dot_base: float = float(dot_run.snapshot("E1")["stats"]["health"])
	dot_run.step_fixed({"P":{"fire":false,"move":Vector2.ZERO}})
	check(float(dot_run.snapshot("E1")["stats"]["health"])<dot_base,
		"disassembly DoT reduces real B HP on the next fixed tick")
	# Critical root generates on_hit+on_crit, but target ICD must avoid a
	# doubled chain-lightning hit on the same target in that single root.
	var critical_run = new_encounter("nv_c_chain_lightning")
	critical_run.actors["P"]["stats"]["crit_chance"] = 1.0
	critical_run.bus.submit_root({"kind":"damage","trigger_kind":"on_hit",
		"actor_id":"P","target_id":"E1","source_tags":["basic_attack","projectile_hit"],
		"allowed_proc_triggers":["on_hit","on_crit"],"attack":{
			"damage_type":"physical","base_damage":10.0,"can_crit":true}})
	critical_run.bus.drain_batch(512,Callable(critical_run,"_resolve_event"))
	check(is_equal_approx(float(critical_run.snapshot("E2")["stats"]["health"]),986.0),
		"critical on_hit/on_crit source triggers only ONE 14-damage B lightning (12 base + 0.2 AP)")
	# Measured bounded load on a mixed full-catalogue build, no fake sink.
	var crowd = new_encounter("nv_c_move_speed")
	var all: Dictionary = {}
	for plugin in plugins:
		all[str(plugin["plugin_id"])] = 1
	check(crowd.attach_plugins("P",all,419),"all 27 plugins attach at first level")
	crowd.actors["P"]["fire_credit"] = 1.0
	var started_us: int = Time.get_ticks_usec()
	for frame in range(16):
		crowd.step_fixed({"P":{"fire":true,"aim_world":Vector3(5,0,0),
			"move":Vector2.ZERO}})
	var total_us: int = Time.get_ticks_usec()-started_us
	print("C B FULL-LOAD 27: 16 ticks, %d us total, %d B damage, max queued=%d" %
		[total_us,int(crowd.counters["damage_events"]),int(crowd.bus.metrics.get("max_pending",0))])
	check(int(crowd.counters["damage_events"])>0
		and crowd.bus.pending.size()<int(crowd.settings["event_work_per_tick"]),
		"27-equipped real B fight deals damage and avoids unbounded backlog")
	check(dispatch_total<2000 and max_pending<512,
		"bounded real B event throughput 27 catalog tests")
	verified.clear()
	for id in verified_battle.keys():
		verified.append(str(id))
	verified.sort()
	not_verified.sort()
	print("C B LIVE COVERAGE: %d/27 actually changed B combat state; unverified=%s" %
		[verified.size(),str(not_verified)])
	print("C B LIVE WORKLOAD: processed=%d max_pending=%d" % [dispatch_total,max_pending])
	if failures.is_empty():
		print("C B LIVE PASS: B production 60Hz combat integration and event/cycle/level tests")
	return failures.is_empty()
