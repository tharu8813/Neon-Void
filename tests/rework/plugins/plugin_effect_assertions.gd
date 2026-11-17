extends RefCounted
## C standalone + (when present) real B damage/status integration smoke.
const Runtime = preload("res://scripts/rework/plugins/plugin_effect_runtime.gd")
const Sink = preload("res://scripts/rework/plugins/plugin_b_combat_sink.gd")
const Catalog = preload("res://scripts/rework/plugins/plugin_catalog.gd")
const Offers = preload("res://scripts/rework/plugins/plugin_offer_engine.gd")

var failures: Array[String] = []
var sink_reject: bool = false
var sink_calls: int = 0

func execute_tests() -> bool:
	_run()
	return failures.is_empty()

func check(yes: bool, why: String) -> void:
	if not yes:
		failures.append(why)
		printerr("C LIVE EFFECTS FAIL: " + why)

func fake_sink(commands: Array, _world: Dictionary) -> Dictionary:
	sink_calls += 1
	if sink_reject:
		return {"ok":false,"reason":"test_rejection"}
	if commands.is_empty():
		return {"ok":false,"reason":"empty"}
	return {"ok":true}

func event(kind: String, number: int = 1, target: String = "virus-1") -> Dictionary:
	return {"event_id":"C-test-" + str(number),"kind":kind,"tick":number,
		"actor_id":"vaccine-1","target_id":target,"source_tags":["basic_attack"],
		"allowed_proc_triggers":["on_hit"],"proc_path":[],
		"actor_health_ratio":0.2,"target_health_ratio":0.2,"actual_damage":10.0,
		"plugin_id":"nv_c_random_rare"}

func _run() -> void:
	var full: Array[Dictionary] = Catalog.entries()
	var control = Runtime.new()
	check(control.validation_errors.is_empty(),"all 27 defined")
	var all_ids: Dictionary = {}
	for item in full:
		all_ids[str(item["plugin_id"])] = true
		check(not control.effect_definition(str(item["plugin_id"])).is_empty(),"effect spec: "+str(item["plugin_id"]))
	check(all_ids.size()==27,"27 immutable IDs")
	var sample: Dictionary = {
		"actor_stats":{"attack_damage":5.0,"ability_power":5.0,"max_health":100.0,
			"move_speed":5.0,"attack_speed":2.0,"projectile_speed_mult":1.0,"projectile_size":10},
		"targets":{"virus-1":{"health":100.0,"armor":0.0,"magic_resist":0.0},
			"virus-2":{"health":100.0,"armor":0.0,"magic_resist":0.0}},
		"target_statuses":{},"target_positions":{"virus-1":Vector2.ZERO,"virus-2":Vector2(2,0)},
		"projectiles":[],"buffs":[],"generated_events":[]
	}
	var tested: Dictionary = {}
	var mapping: Dictionary = {
		"periodic_shield":"tick", "skill_shield":"skill_cast", "mobility_buff":"dash",
		"low_hp_emergency":"damage_taken", "low_hp_knockback":"damage_taken",
		"chain_lightning":"on_hit", "hit_explosion":"on_hit",
		"breakable_sprint":"damage_taken", "armor_shred":"damage_dealt",
		"execute":"before_damage", "proc_volley":"on_hit_proc",
		"disassembly":"on_hit_proc", "skill_followup":"skill_damage",
		"dash_explosion":"dash", "grant_rare":"plugin_acquired",
		"kill_power":"kill", "dash_shield":"dash",
		"extra_bullet":"basic_attack","homing":"basic_attack",
		"laser":"basic_attack","boomerang":"basic_attack","fever":"kill"
	}
	var passive: Array = ["random_stats","passive_stat","dual_penetration","dual_speed"]
	for entry in full:
		var uid: String = str(entry["plugin_id"])
		var instance = Runtime.new()
		check(instance.configure({uid:1}, 42),"configure "+uid)
		var spec: Dictionary = instance.effect_definition(uid)
		var kind: String = str(spec.get("kind",""))
		if passive.has(kind):
			var original: Dictionary = sample["actor_stats"].duplicate(true)
			var upgraded: Dictionary = instance.derived_stats(original)
			check(upgraded != original,"passive actually alters stats: "+uid)
		elif mapping.has(kind):
			var e: Dictionary = event(str(mapping[kind]), 1800 if kind=="periodic_shield" else 5)
			e["plugin_id"] = uid
			var first: Dictionary = instance.process(e, sample.duplicate(true), Callable(self, "fake_sink"))
			check(first.get("ok",false),"event accepted: "+uid)
			if kind not in ["proc_volley","kill_power","breakable_sprint","fever"]:
				check(first.get("commands",[]).size()>0,"real command emitted: "+uid)
			elif kind=="kill_power":
				check(float(instance.derived_stats(sample["actor_stats"])["attack_damage"]) > 5.0,"kill power gained AD")
			elif kind=="breakable_sprint":
				check(not bool(instance.state_snapshot()["sprint_active"]),"sprint breaks on damage")
			elif kind=="fever":
				check(int(instance.state_snapshot()["fever_gauge"])>0,"fever gauge charged")
			if first.get("commands",[]).size()>0:
				var dup: Dictionary = instance.process(e,sample.duplicate(true),Callable(self,"fake_sink"))
				check(dup.get("duplicate",false) and dup.get("commands",[]).is_empty(),"idempotence: "+uid)
		else:
			check(false,"unhandled effect kind: "+uid+" / "+kind)
		tested[uid] = true
	check(tested.size()==27,"all original effects exercised")

	# Original GDD 4 proc triggers -> 4 projectiles at precisely 70% efficiency.
	var volley = Runtime.new()
	check(volley.configure({"nv_c_proc_volley":1},4),"volley config")
	for i in range(1,5):
		var step: Dictionary = volley.process(event("on_hit_proc",i),sample.duplicate(true),Callable(self,"fake_sink"))
		check(step.get("ok",false),"volley proc #"+str(i))
		if i==4:
			check(step["commands"].size()==1 and int(step["commands"][0]["count"])==4
				and is_equal_approx(float(step["commands"][0]["on_hit_efficiency"]),0.70),"exact 4-shot volley and 70% proc effectiveness")
		else:
			check(step["commands"].is_empty(),"volley does not trigger early")

	# B's per-target 180 tick cooldown must never apply to another target.
	var lightning = Runtime.new()
	check(lightning.configure({"nv_c_chain_lightning":1},31),"lightning config")
	var hit1: Dictionary = lightning.process(event("on_hit",100),sample.duplicate(true),Callable(self,"fake_sink"))
	var hit2: Dictionary = lightning.process(event("on_hit",101),sample.duplicate(true),Callable(self,"fake_sink"))
	var hit3: Dictionary = lightning.process(event("on_hit",102,"virus-2"),sample.duplicate(true),Callable(self,"fake_sink"))
	check(hit1["commands"].size()==1 and hit2["commands"].is_empty()
		and hit3["commands"].size()==1,"lightning per-target ICD")
	var looped: Dictionary = event("on_hit",300)
	looped["proc_path"] = ["nv_c_chain_lightning|vaccine-1|virus-1"]
	check(lightning.process(looped,sample.duplicate(true),Callable(self,"fake_sink"))["commands"].is_empty(),"cycle suppressed, independent root allowed")

	# Failures cannot consume counters, ICDs or event IDs.
	var failing = Runtime.new()
	failing.configure({"nv_c_chain_lightning":1},11)
	sink_reject = true
	var before: Dictionary = failing.state_snapshot()
	var failed: Dictionary = failing.process(event("on_hit",5),sample.duplicate(true),Callable(self,"fake_sink"))
	check(not failed.get("ok",false) and failing.state_snapshot()==before,"failed combat sink rolls back C state")
	sink_reject = false
	check(failing.process(event("on_hit",5),sample.duplicate(true),Callable(self,"fake_sink")).get("ok",false),"failed event retry with same ID")

	# Transactional choice: E must commit BEFORE C mutates inventory or closes offer.
	var offers = Offers.new()
	check(offers.begin_mission(777,{},"durable-run-777"),"offers begin")
	var opened: Dictionary = offers.roll_offer()
	var prepared: Dictionary = offers.choose_card(0)
	check(not prepared.is_empty() and offers.levels.is_empty()
		and offers.offer_snapshot()["active"],"prepare is not a grant")
	check(offers.reroll_card(0).is_empty(),"prepared choice locks rerolls")
	var denied: Dictionary = offers.confirm_card(str(prepared["receipt_id"]),{"applied":false,"receipt_id":prepared["receipt_id"]})
	check(not denied.get("ok",false) and offers.levels.is_empty()
		and bool(offers.offer_snapshot()["reconciliation_required"]),"failed save freezes without reward")
	check(not offers.reconcile_card(str(prepared["receipt_id"]),{}).get("ok",false),"no guessing after timeout")
	check(offers.reconcile_card(str(prepared["receipt_id"]),{"receipt_definitely_absent":true}).get("retry_same_receipt",false),"verified absent allows exact receipt retry")
	var accepted: Dictionary = offers.confirm_card(str(prepared["receipt_id"]),{"applied":true,"receipt_id":prepared["receipt_id"],"revision":4})
	check(accepted.get("ok",false) and offers.levels[prepared["plugin_id"]]==1
		and not offers.offer_snapshot()["active"],"persisted E ack finalizes exactly once")
	check(not offers.confirm_card(str(prepared["receipt_id"]),{"applied":true,"receipt_id":prepared["receipt_id"]}).get("ok",false),"double E ack never levels twice")
	check(opened["offer_id"]==prepared["offer_id"],"stable offer receipt")

	# A's aggregate #107 contains B. Exercise its real math/status functions
	# when this C smoke is mounted there; own stacked branch has no B files.
	if Sink.available():
		var b_world: Dictionary = sample.duplicate(true)
		var b_command: Dictionary = hit1["commands"][0]
		var real: Dictionary = Sink.apply([b_command],b_world)
		check(real.get("ok",false) and float(b_world["targets"]["virus-2"]["health"])<100.0,
			"real B Damage.calculate+apply_damage changes target HP")
		var status: Dictionary = {"type":"status","plugin_id":"nv_r_disassembly",
			"target_id":"virus-2","definition":{"id":"nv_r_disassembly","duration_ticks":240,
				"stack_mode":"stack","max_stacks":2147483647,"dot_dps":2.0,"dot_damage_type":"magic"}}
		check(Sink.apply([status,status],b_world).get("ok",false),"real B Statuses.apply")
		check(b_world["target_statuses"]["virus-2"]["statuses"][0]["stacks"]==2,"B stack count")
		var hp_before: float = float(b_world["targets"]["virus-2"]["health"])
		check(Sink.tick_dots(b_world).get("ok",false)
			and float(b_world["targets"]["virus-2"]["health"])<hp_before,"B DoT tick actually reduces HP")
		var snapshot: Dictionary = b_world.duplicate(true)
		var bad: Dictionary = Sink.apply([b_command,{"type":"bogus"}],b_world)
		check(not bad.get("ok",false) and b_world==snapshot,"all-or-none world publication on multi-command failure")
	else:
		print("B not installed on C-only stacked branch; real B damage/status path awaits A combined runner.")

	if failures.is_empty():
		print("C LIVE EFFECTS PASS: 27 executable kinds, real B if available, proc/ICD, E prepare-ack/rollback")

