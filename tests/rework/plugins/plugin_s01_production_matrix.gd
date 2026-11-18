extends RefCounted
## S01: all shield levels through the production B CAdapter and real B HP ledger.
## Fixture levels are synthetic; separate E tests prove durable authorization.
const Combat = preload("res://scripts/rework/combat/combat_runtime.gd")
const Adapter = preload("res://scripts/rework/combat/c_effect_adapter.gd")
const Catalog = preload("res://scripts/rework/plugins/plugin_catalog.gd")
const Effects = preload("res://scripts/rework/plugins/plugin_effect_runtime.gd")
const SHIELDS := ["nv_c_periodic_shield", "nv_c_skill_shield", "nv_r_dash_shield"]
var failures: Array[String] = []
var verified_cases: int = 0

func _check(condition: bool, label: String) -> void:
	if not condition:
		failures.append(label)
		printerr("C S01 PROD SHIELD FAIL: " + label)

func run() -> bool:
	var definitions = Effects.new()
	for uid in SHIELDS:
		var entry: Dictionary = Catalog.find_by_id(uid,Catalog.entries())
		var spec: Dictionary = definitions.effect_definition(uid)
		_check(not entry.is_empty() and str(spec.get("kind","")) in [
			"periodic_shield", "skill_shield", "dash_shield"],uid+" catalog exists")
		for level in range(1,int(entry.get("max_level",0))+1):
			var title: String = uid + " Lv." + str(level)
			var b = Combat.new(77, {"encounter_id":"c-s01-shield-" + title})
			_check(b.register_actor("VACCINE","vaccine",Vector3.ZERO,{
				"max_health":100.0,"health":100.0,"attack_speed":0.0,
				"move_speed":5.0}, {
				"player_controlled":true,"dash_definition":{
					"distance":2.0,"cooldown_ticks":60}}),title+" B actor")
			_check(b.register_actor("FOE","enemy",Vector3(3,0,0),{
				"max_health":100.0,"health":100.0,"attack_speed":0.0},{
				"player_controlled":true}),title+" enemy actor")
			var adapter = Adapter.new()
			if not adapter.attach(b,"VACCINE","c-s01-shield-"+title,77):
				_check(false,title+" production adapter")
				continue
			if not adapter.configure_levels({uid:level},77):
				_check(false,title+" configure level")
				continue
			var input: Dictionary = {"fire":false,"move":Vector2.ZERO}
			if uid == "nv_c_periodic_shield":
				b.fixed_tick=1799
				b.bus.set_tick(1799)
			elif uid == "nv_c_skill_shield":
				_check(b.learn_skill("VACCINE",{
					"id":"c-s01-test","mana_cost":5.0,"cooldown_ticks":60,
					"attack":{}},0,true),title+" B skill equipped")
				input["skill_slot"]=0
			else:
				input["move"]=Vector2.RIGHT
				input["dash"]=true
			b.step_fixed({"VACCINE":input,"FOE":{"fire":false}})
			adapter.after_fixed_step(b.last_processed_events)
			var expected: float = float(spec.get("shield_values",[])[level-1])
			var shield: float = adapter.shield_value()
			_check(is_equal_approx(shield,expected),title+" real B shield amount expected="+str(expected)+" got="+str(shield))
			if not is_equal_approx(shield,expected):
				continue
			b.bus.submit_root({
				"kind":"damage","trigger_kind":"on_hit",
				"actor_id":"FOE","target_id":"VACCINE",
				"source_tags":["hostile_attack"],"allowed_proc_triggers":[],
				"attack":{"damage_type":"true","base_damage":shield+5.0,
					"ad_ratio":0.0,"ap_ratio":0.0,"can_crit":false,"is_on_hit":true}
			})
			b.step_fixed({"VACCINE":{"fire":false,"move":Vector2.ZERO},
				"FOE":{"fire":false}})
			adapter.after_fixed_step(b.last_processed_events)
			_check(is_equal_approx(float(b.snapshot("VACCINE")["stats"]["health"]),95.0)
				and is_zero_approx(adapter.shield_value()),
				title+" B true damage only spends shield once and spills 5 HP")
			verified_cases += 1
	print("C S01 PROD SHIELD: %d/%d level fixtures executed; failures=%s" %
		[verified_cases,8,str(failures)])
	return failures.is_empty() and verified_cases == 8
