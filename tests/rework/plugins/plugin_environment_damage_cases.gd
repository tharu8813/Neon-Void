extends RefCounted
## C S01 environment-hit contract against B's ACTUAL damage/HP + production
## c_effect_adapter. A synthetic ENV attacker stands in for D's not-yet-connected
## GPU contact event. This does NOT claim a real-world GPU beam damages HP.
const B = preload("res://scripts/rework/combat/combat_runtime.gd")
const CAdapter = preload("res://scripts/rework/combat/c_effect_adapter.gd")
const Catalog = preload("res://scripts/rework/plugins/plugin_catalog.gd")
const Effects = preload("res://scripts/rework/plugins/plugin_effect_runtime.gd")
const SHIELDS := ["nv_c_periodic_shield","nv_c_skill_shield","nv_r_dash_shield"]
var failures: Array[String] = []
var shield_cases: int = 0
var trigger_cases: int = 0

func check(ok: bool, description: String) -> void:
	if not ok:
		failures.append(description)
		printerr("C ENVIRONMENT HIT FAIL: " + description)

func encounter(levels: Dictionary, seed: int, player_health: float = 100.0) -> Dictionary:
	var b = B.new(seed,{"encounter_id":"c-env-"+str(seed)})
	if not b.register_actor("VACCINE","vaccine",Vector3.ZERO,{
		"max_health":100.0,"health":player_health,"attack_speed":0.0,
		"attack_damage":5.0,"ability_power":10.0,"move_speed":5.0},{
		"player_controlled":true,"dash_definition":{
			"distance":2.0,"cooldown_ticks":60}}):
		return {}
	# A genuine B actor for the test ONLY. D/B must decide the production
	# hazard entrypoint; never register a fake actor in a real save/session.
	if not b.register_actor("ENV_CONTACT","environment",Vector3(2,0,0),{
		"max_health":1000.0,"health":1000.0,"attack_speed":0.0},{
		"player_controlled":true}):
		return {}
	if not b.register_actor("VIRUS","virus",Vector3(3,0,0),{
		"max_health":100.0,"health":100.0,"attack_speed":0.0},{
		"player_controlled":true}):
		return {}
	var adapter = CAdapter.new()
	if not adapter.attach(b,"VACCINE","c-env-"+str(seed),seed):
		return {}
	if not adapter.configure_levels(levels,seed):
		return {}
	return {"b":b,"c":adapter}

func fixed_step(state: Dictionary, player_input: Dictionary = {}) -> Array[Dictionary]:
	var b = state["b"]
	var input: Dictionary = {"fire":false,"move":Vector2.ZERO}
	input.merge(player_input,true)
	b.step_fixed({"VACCINE":input,"ENV_CONTACT":{"fire":false},"VIRUS":{"fire":false}})
	return state["c"].after_fixed_step(b.last_processed_events)

func arm_shield(state: Dictionary, uid: String) -> void:
	var b = state["b"]
	var inputs: Dictionary = {}
	if uid == "nv_c_periodic_shield":
		b.fixed_tick = 1799
		b.bus.set_tick(1799)
	elif uid == "nv_c_skill_shield":
		check(b.learn_skill("VACCINE",{
			"id":"c-env-shield","mana_cost":5.0,"cooldown_ticks":60,
			"attack":{}},0,true),uid+" equip B skill")
		inputs["skill_slot"] = 0
	elif uid == "nv_r_dash_shield":
		inputs["dash"] = true
		inputs["move"] = Vector2.RIGHT
	fixed_step(state,inputs)

func impact(state: Dictionary, amount: float, collision_id: String) -> Dictionary:
	var b = state["b"]
	var before_hp: float = float(b.snapshot("VACCINE")["stats"]["health"])
	var before_shield: float = state["c"].shield_value()
	# One authoritative B damage input, NOT C.process() nor UI simulated HP.
	# ENV is an explicit non-vaccine actor: never counts as player on_hit.
	var submission: String = b.bus.submit_root({
		"kind":"damage","trigger_kind":"on_hit",
		"actor_id":"ENV_CONTACT","target_id":"VACCINE",
		"collision_id":collision_id,
		"source_tags":["environment_damage","hazard_contact"],
		"allowed_proc_triggers":[],
		"attack":{"damage_type":"true","base_damage":amount,
			"ad_ratio":0.0,"ap_ratio":0.0,
			"can_crit":false,"is_on_hit":false}
	})
	check(not submission.is_empty(),collision_id+" B accepted synthetic contact")
	var notices: Array[Dictionary] = fixed_step(state)
	var damage_events: int = 0
	var player_hits: int = 0
	for event in b.last_processed_events:
		if str(event.get("kind","")) == "damage_taken" and str(event.get("actor_id","")) == "VACCINE":
			damage_events += 1
		if str(event.get("kind","")) == "damage" and str(event.get("actor_id","")) == "VACCINE":
			player_hits += 1
	return {"before_hp":before_hp,"after_hp":float(b.snapshot("VACCINE")["stats"]["health"]),
		"before_shield":before_shield,"after_shield":state["c"].shield_value(),
		"taken_events":damage_events,"player_hits":player_hits,
		"notices":notices,"active_state":state["c"].c_engine.state_snapshot()}

func run() -> bool:
	var catalog: Array[Dictionary] = Catalog.entries()
	var effects = Effects.new()
	for uid in SHIELDS:
		var entry: Dictionary = Catalog.find_by_id(uid,catalog)
		var specs: Dictionary = effects.effect_definition(uid)
		for level in range(1,int(entry.get("max_level",0))+1):
			var title: String = uid+" Lv."+str(level)
			var instance: Dictionary = encounter({uid:level},4200+shield_cases)
			check(not instance.is_empty(),title+" B+C environment fixture initialized")
			if instance.is_empty():
				continue
			arm_shield(instance,uid)
			var expected: float = float(specs.get("shield_values",[])[level-1])
			check(is_equal_approx(instance["c"].shield_value(),expected),
				title+" armed B->C shield at expected level")
			var first_amount: float = expected * 0.5
			var first: Dictionary = impact(instance,first_amount,title+":first")
			check(is_equal_approx(first["after_hp"],100.0) and
				is_equal_approx(first["after_shield"],expected-first_amount) and
				int(first["taken_events"])==0 and int(first["player_hits"])==0,
				title+" environment contact absorbed with no HP-damage proc")
			var second: Dictionary = impact(instance,expected-first_amount+5.0,title+":spill")
			check(is_equal_approx(second["after_hp"],95.0) and
				is_zero_approx(second["after_shield"]) and
				int(second["taken_events"])==1 and int(second["player_hits"])==0,
				title+" environment spill produces one genuine B damage_taken")
			var third: Dictionary = impact(instance,5.0,title+":unshielded")
			check(is_equal_approx(third["after_hp"],90.0) and
				is_zero_approx(third["after_shield"]) and int(third["taken_events"])==1,
				title+" spent shield cannot absorb future environment damage")
			shield_cases += 1
	# HP already low is not sufficient to fire emergency if shield takes all HP damage.
	var blocked: Dictionary = encounter({
		"nv_c_periodic_shield":1,
		"nv_c_low_hp_emergency":1,
		"nv_c_breakable_sprint":1},4900,28.0)
	check(not blocked.is_empty(),"full shield + low-HP fixture")
	if not blocked.is_empty():
		arm_shield(blocked,"nv_c_periodic_shield")
		var shield_only: Dictionary = impact(blocked,10.0,"low-hp:shield-only")
		check(is_equal_approx(shield_only["after_hp"],28.0) and
			is_equal_approx(shield_only["after_shield"],2.0) and
			bool(shield_only["active_state"].get("sprint_active",false)) and
			int(shield_only["taken_events"])==0,
			"fully shielded contact does not break sprint or grant emergency barrier")
		trigger_cases += 1
	# When *actual* HP is hit, C reacts exactly through B damage_taken.
	var spill: Dictionary = encounter({
		"nv_c_periodic_shield":1,
		"nv_c_low_hp_emergency":1,
		"nv_r_low_hp_knockback":1,
		"nv_c_breakable_sprint":1,
		"nv_c_chain_lightning":1},5100,40.0)
	check(not spill.is_empty(),"environment + low HP proc fixture")
	if not spill.is_empty():
		arm_shield(spill,"nv_c_periodic_shield")
		var real: Dictionary = impact(spill,27.0,"low-hp:real-spill")
		var saw_knockback_request: bool = false
		for notice in real["notices"]:
			if str(notice.get("kind","")) == "knockback_requested":
				saw_knockback_request = true
		check(is_equal_approx(real["after_hp"],25.0) and
			is_equal_approx(real["after_shield"],50.0) and
			not bool(real["active_state"].get("sprint_active",true)) and
			saw_knockback_request and int(real["taken_events"])==1,
			"environment HP damage activates emergency barrier, sprint break and knockback request")
		check(is_equal_approx(float(spill["b"].snapshot("VIRUS")["stats"]["health"]),100.0)
			and int(real["player_hits"])==0,
			"environment damage never procs player's chain-lightning on_hit")
		var repeat: Dictionary = impact(spill,5.0,"low-hp:shield-followup")
		check(is_equal_approx(repeat["after_hp"],25.0) and
			is_equal_approx(repeat["after_shield"],45.0) and
			int(repeat["taken_events"])==0,
			"emergency shield absorbs next environment hit without repeated HP proc")
		trigger_cases += 1
	print("C ENVIRONMENT HIT MATRIX: shields=%d/8; trigger fixtures=%d/2; errors=%s" %
		[shield_cases,trigger_cases,str(failures)])
	return failures.is_empty() and shield_cases==8 and trigger_cases==2
