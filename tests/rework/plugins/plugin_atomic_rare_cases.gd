extends RefCounted
## C-only regression: every catalog level and E-authoritative rare receipts.
## No on-disk save is created by the synthetic inspector tests.
const Catalog = preload("res://scripts/rework/plugins/plugin_catalog.gd")
const Runtime = preload("res://scripts/rework/plugins/plugin_effect_runtime.gd")
const Offers = preload("res://scripts/rework/plugins/plugin_offer_engine.gd")
const Coordinator = preload("res://scripts/rework/plugins/plugin_reward_coordinator.gd")
const LiveB = preload("res://scripts/rework/plugins/plugin_b_live_runtime.gd")
var failures: Array[String] = []

func _check(yes: bool, label: String) -> void:
	if not yes:
		failures.append(label)
		printerr("C ATOMIC RARE FAIL: " + label)

func _accept_commands(_commands: Array, _world: Dictionary) -> Dictionary:
	return {"ok":true}


func _clean_generated(folder: String) -> void:
	# Only called for unique user://rework-c-atomic-* test directories.
	var dir: DirAccess = DirAccess.open(folder)
	if dir == null:
		return
	dir.list_dir_begin()
	var next: String = dir.get_next()
	while not next.is_empty():
		var child: String = folder.path_join(next)
		if dir.current_is_dir():
			_clean_generated(child)
		else:
			DirAccess.remove_absolute(child)
		next = dir.get_next()
	dir.list_dir_end()
	DirAccess.remove_absolute(folder)

func _test_actual_E_transaction() -> void:
	## Runs on A #121+ only, using a fresh synthetic slot, never real saves.
	if not ResourceLoader.exists("res://scripts/rework/progression/rare_bonus.gd"):
		print("C ATOMIC RARE: E atomic rare implementation unavailable on this stack")
		return
	var store: Script = load("res://scripts/rework/persistence/rework_save_store.gd")
	var model: Script = load("res://scripts/rework/progression/state_model.gd")
	var gateway: Script = load("res://scripts/rework/persistence/progression_gateway.gd")
	var suffix: String = "c-atomic-" + str(Time.get_ticks_usec()) + "-" + str(OS.get_process_id())
	var folder: String = ProjectSettings.globalize_path("user://rework-c-atomic-" + suffix)
	var slot_path: String = folder.path_join("rework-slot-" + suffix)
	var vaccine_id: String = "vaccine-c-atomic"
	var run_id: String = "run-c-atomic"
	var created: Dictionary = store.initialize(slot_path,model.new_slot(suffix,vaccine_id))
	_check(bool(created.get("ok",false)),"E source+bonus test slot initialized")
	if not bool(created.get("ok",false)):
		return
	var started: Dictionary = gateway.transact_command(slot_path,{
		"action":"start_mission","vaccine_id":vaccine_id,"generation":1,
		"run_id":run_id,"seed":1789})
	_check(bool(started.get("applied",false)),"E atomic test mission started")
	if not bool(started.get("applied",false)):
		_clean_generated(folder)
		return
	var xp: Dictionary = gateway.transact_command(slot_path,{
		"action":"mission_xp","vaccine_id":vaccine_id,"generation":1,
		"run_id":run_id,"receipt_id":"c-rare-xp1","amount":100})
	_check(bool(xp.get("applied",false)),"E issued one level-up offer")
	var single_source: Array[Dictionary] = [
		Catalog.find_by_id("nv_c_random_rare",Catalog.entries())
	]
	var offers = Offers.new(single_source)
	_check(offers.begin_mission(1789,{},run_id),"C prepares source-only card")
	var opening: Dictionary = offers.roll_offer()
	_check(not opening.get("cards",[]).is_empty() and
		str(opening["cards"][0].get("plugin_id",""))=="nv_c_random_rare",
		"deterministic fixture offers random rare source")
	var picked: Dictionary = Coordinator.commit_choice(offers,0,slot_path,
		vaccine_id,1,run_id)
	_check(bool(picked.get("ok",false)) and
		bool(picked.get("rare_bonus",{}).get("ok",false)),
		"C displays source only after E confirms atomic rare pair")
	var saved: Dictionary = gateway.snapshot(slot_path)
	if bool(picked.get("ok",false)) and bool(saved.get("ok",false)):
		var receipt: String = str(picked.get("receipt_id",""))
		var bonus: Dictionary = picked.get("rare_bonus",{})
		var source_rare: String = str(bonus.get("plugin_id",""))
		var mission: Dictionary = saved["snapshot"]["vaccine"]["mission"]
		_check(mission["obtained_plugins"].has("nv_c_random_rare")
			and mission["obtained_plugins"].has(source_rare)
			and saved["snapshot"]["vaccine"]["receipts"].has(receipt)
			and saved["snapshot"]["vaccine"]["receipts"].has(str(bonus.get("bonus_receipt",""))),
			"E persisted both source and bonus receipts in one transaction")
		var combat = Runtime.new()
		_check(combat.configure({"nv_c_random_rare":1},23),
			"C combat ready for E confirmed rare bonus")
		var once: Dictionary = Coordinator.apply_verified_rare(combat,offers,
			saved,receipt,vaccine_id,1,run_id)
		var twice: Dictionary = Coordinator.apply_verified_rare(combat,offers,
			saved,receipt,vaccine_id,1,run_id)
		_check(bool(once.get("ok",false)) and bool(twice.get("already_synced",false))
			and int(combat.levels.get(source_rare,0))==int(bonus.get("level",-1)),
			"E source+bonus ACK reconciles exactly once")
		var before_replay: Dictionary = gateway.snapshot(slot_path)
		var original: Dictionary = picked.get("plugin",{})
		var duplicate_tx: Dictionary = gateway.transact_command(slot_path,{
			"action":"plugin_picked","vaccine_id":vaccine_id,"generation":1,
			"run_id":run_id,"receipt_id":receipt,
			"offer_id":original.get("offer_id",""),
			"plugin_id":"nv_c_random_rare","rarity":"common",
			"level":int(original.get("level",1)),
			"offer_source":"levelup"})
		var after_replay: Dictionary = gateway.snapshot(slot_path)
		_check(not bool(duplicate_tx.get("applied",false)) and
			int(after_replay.get("revision",-1))==int(before_replay.get("revision",-2)),
			"E duplicate transaction cannot issue another rare")
	_clean_generated(folder)

func run() -> bool:
	var entries: Array[Dictionary] = Catalog.entries()
	_check(entries.size() == 27 and Catalog.validate(entries).is_empty(),
		"canonical 27 entries")
	var probe_event: Dictionary = {
		"periodic_shield":"tick","skill_shield":"skill_cast",
		"mobility_buff":"dash","low_hp_emergency":"damage_taken",
		"low_hp_knockback":"damage_taken","chain_lightning":"on_hit",
		"hit_explosion":"on_hit","breakable_sprint":"damage_taken",
		"armor_shred":"damage_dealt","execute":"before_damage",
		"proc_volley":"on_hit_proc","disassembly":"on_hit_proc",
		"skill_followup":"skill_damage","dash_explosion":"dash",
		"grant_rare":"plugin_acquired","kill_power":"kill",
		"dash_shield":"dash","extra_bullet":"basic_attack",
		"homing":"basic_attack","laser":"basic_attack",
		"boomerang":"basic_attack","fever":"kill"
	}
	var level_checks: int = 0
	var rare_id: String = ""
	for entry in entries:
		var uid: String = str(entry["plugin_id"])
		var max_level: int = int(entry["max_level"])
		var effect = Runtime.new()
		var initial: Dictionary = {}
		initial[uid] = 1
		_check(effect.configure(initial,123),uid+" start level")
		_check(not effect.effect_definition(uid).is_empty(),uid+" effect definition")
		var spec: Dictionary = effect.effect_definition(uid)
		for key in spec:
			var values: Variant = spec[key]
			if not values is Array or values.is_empty():
				continue
			var numeric: bool = true
			for value in values:
				if typeof(value) not in [TYPE_INT,TYPE_FLOAT]:
					numeric = false
					break
			if numeric:
				_check(values.size()==max_level,
					uid+" numeric level vector "+str(key)+" covers all levels")
		var kind: String = str(spec.get("kind",""))
		for level_index in range(1,max_level+1):
			var instance = Runtime.new()
			var levels_for_test: Dictionary = {}
			levels_for_test[uid] = level_index
			_check(instance.configure(levels_for_test,400+level_index),
				uid+" can instantiate Lv."+str(level_index))
			var stats: Dictionary = instance.derived_stats({
				"max_health":100.0,"attack_damage":5.0,"ability_power":10.0,
				"move_speed":5.0,"attack_speed":2.0,"projectile_speed_mult":1.0
			},1800)
			_check(not stats.is_empty(),uid+" Lv."+str(level_index)+" derives stats")
			if probe_event.has(kind):
				var tick: int = 1800 if kind=="periodic_shield" else 60+level_index
				var probe: Dictionary = {
					"event_id":uid+":level:"+str(level_index),
					"kind":str(probe_event[kind]),"tick":tick,
					"actor_id":"vaccine","target_id":"enemy",
					"plugin_id":uid,"source_tags":["basic_attack"],
					"allowed_proc_triggers":["on_hit"],
					"proc_path":[],"actor_health_ratio":0.2,
					"target_health_ratio":0.2,"actual_damage":10.0
				}
				var outcome: Dictionary = instance.process(probe,{
					"actor_stats":stats
				},Callable(self,"_accept_commands"))
				_check(bool(outcome.get("ok",false)),
					uid+" Lv."+str(level_index)+" effect dispatch accepted")
			else:
				_check(kind in ["random_stats","passive_stat",
					"dual_penetration","dual_speed"],
					uid+" Lv."+str(level_index)+" has a supported trigger")

		if str(entry["rarity"]) == "rare" and rare_id.is_empty():
			rare_id = uid
		for lvl in range(2,max_level+1):
			_check(effect.set_plugin_level(uid,lvl) and int(effect.levels[uid])==lvl,
				uid+" level "+str(lvl))
			level_checks += 1
		_check(not effect.set_plugin_level(uid,max_level+1) and
			int(effect.levels[uid])==max_level,uid+" max level cannot over-award")
		_check(not effect.set_plugin_level(uid,max_level),uid+" cannot replay same level")
		var rolled = Offers.new()
		var capped_levels: Dictionary = {}
		capped_levels[uid] = max_level
		_check(rolled.begin_mission(333,capped_levels) and
			not rolled.is_eligible(uid),uid+" max-level candidate rejected")
	_check(not rare_id.is_empty(),"rare candidate available")

	# Source-acquisition event is NOT a second grant. No Lv2 count assumed.
	var trigger = Runtime.new()
	_check(trigger.configure({"nv_c_random_rare":2},55),"random rare source level 2")
	var notice: Dictionary = trigger.process({
		"event_id":"rare-source:once","kind":"plugin_acquired",
		"tick":3,"actor_id":"vaccine","plugin_id":"nv_c_random_rare"
	}, {},Callable(self,"_accept_commands"))
	_check(bool(notice.get("ok",false)) and notice.get("commands",[]).size()==1,
		"source event is accepted as notice")
	if notice.get("commands",[]).size()==1:
		var command: Dictionary = notice["commands"][0]
		_check(command.get("type")=="rare_reward_notice" and
			not command.has("count") and bool(command.get("requires_E_receipt",false)),
			"Lv2 notice never fabricates a reward quantity")
	var b = LiveB.new(73)
	_check(b.register_actor("vaccine","player",Vector3.ZERO,{},{"player_controlled":true}),
		"B source actor registered")
	_check(b.attach_plugins("vaccine",{"nv_c_random_rare":1},77),
		"B source equipped")
	b._active_parent = {"actor_id":"vaccine","target_id":""}
	var routed: Dictionary = b._submit_effect_commands(
		[{"type":"rare_reward_notice","plugin_id":"nv_c_random_rare"}],{})
	b._active_parent = {}
	_check(bool(routed.get("ok",false)) and b.pending_e_rewards.is_empty(),
		"B cannot queue a bonus grant from an unverified notice")

	# Simulated response is E's verified read-only snapshot shape.
	var receipt: String = "run-c-rare:1:0"
	var fake: Dictionary = {"ok":true,"revision":8,"snapshot":{
		"generation":1,"vaccine":{
			"vaccine_id":"vaccine-c","receipts":{},"carried":{},
			"mission":{
				"run_id":"run-c-rare",
				"environment_rolls":{"e_rare_bonus_v1":{}},
				"obtained_plugins":{"nv_c_random_rare":{"rarity":"common","level":1}}
			}
		}
	}}
	fake["snapshot"]["vaccine"]["receipts"][receipt] = true
	fake["snapshot"]["vaccine"]["receipts"][receipt+":rare:0"] = true
	fake["snapshot"]["vaccine"]["mission"]["environment_rolls"]["e_rare_bonus_v1"][receipt] = {
		"bonus_receipt":receipt+":rare:0","plugin_id":rare_id,
		"rarity":"rare","level":1,"rng_policy":"rare-event-v1"
	}
	fake["snapshot"]["vaccine"]["mission"]["obtained_plugins"][rare_id] = {
		"rarity":"rare","level":1
	}
	var verified: Dictionary = Coordinator.inspect_atomic_rare(
		fake,receipt,"vaccine-c",1,"run-c-rare")
	_check(bool(verified.get("ok",false)) and verified.get("plugin_id")==rare_id
		and int(verified.get("level",-1))==1,"read E source + rare receipts together")
	var active = Runtime.new()
	var offers = Offers.new()
	_check(active.configure({"nv_c_random_rare":1},12)
		and offers.begin_mission(12,{"nv_c_random_rare":1},"run-c-rare"),
		"fresh C runtimes waiting for E")
	var applied: Dictionary = Coordinator.apply_verified_rare(active,offers,
		fake,receipt,"vaccine-c",1,"run-c-rare")
	_check(bool(applied.get("ok",false)) and not bool(applied.get("already_synced",true))
		and int(active.levels.get(rare_id,0))==1
		and int(offers.levels.get(rare_id,0))==1,"E alone authorizes first rare sync")
	var duplicate: Dictionary = Coordinator.apply_verified_rare(active,offers,
		fake,receipt,"vaccine-c",1,"run-c-rare")
	_check(bool(duplicate.get("ok",false)) and bool(duplicate.get("already_synced",false))
		and int(active.levels.get(rare_id,0))==1,"ACK replay changes no levels")

	var no_bonus: Dictionary = fake.duplicate(true)
	no_bonus["snapshot"]["vaccine"]["receipts"].erase(receipt+":rare:0")
	_check(not Coordinator.inspect_atomic_rare(no_bonus,receipt,
		"vaccine-c",1,"run-c-rare").get("ok",false),"missing derived receipt fails closed")
	var no_source: Dictionary = fake.duplicate(true)
	no_source["snapshot"]["vaccine"]["receipts"].erase(receipt)
	_check(not Coordinator.inspect_atomic_rare(no_source,receipt,
		"vaccine-c",1,"run-c-rare").get("ok",false),"missing source receipt fails closed")
	var bad_rng: Dictionary = fake.duplicate(true)
	bad_rng["snapshot"]["vaccine"]["mission"]["environment_rolls"][
		"e_rare_bonus_v1"][receipt]["rng_policy"] = "C-reselected"
	_check(not Coordinator.inspect_atomic_rare(bad_rng,receipt,
		"vaccine-c",1,"run-c-rare").get("ok",false),"non-E RNG policy rejected")
	_check(not Coordinator.inspect_atomic_rare(fake,receipt,
		"vaccine-c",2,"run-c-rare").get("ok",false),"generation fence")
	_check(not Coordinator.inspect_atomic_rare(fake,receipt,
		"vaccine-c",1,"another-run").get("ok",false),"run fence")
	var exhausted: Dictionary = fake.duplicate(true)
	exhausted["snapshot"]["vaccine"]["mission"]["environment_rolls"][
		"e_rare_bonus_v1"][receipt]["level"] = 99999
	_check(not Coordinator.inspect_atomic_rare(exhausted,receipt,
		"vaccine-c",1,"run-c-rare").get("ok",false),"invalid over-max level")

	var empty_runtime = Runtime.new()
	var empty_offers = Offers.new()
	_check(empty_runtime.configure({"nv_c_random_rare":1},14)
		and empty_offers.begin_mission(14,{"nv_c_random_rare":1},"run-c-rare"),
		"blocked pair setup")
	_check(not Coordinator.apply_verified_rare(empty_runtime,empty_offers,
		no_bonus,receipt,"vaccine-c",1,"run-c-rare").get("ok",false)
		and int(empty_runtime.levels.get(rare_id,0))==0
		and int(empty_offers.levels.get(rare_id,0))==0,
		"no local awards after corrupted or absent receipt pair")
	_check(level_checks > 0,"every defined level boundary tested")
	_test_actual_E_transaction()
	if failures.is_empty():
		print("C ATOMIC RARE PASS: 27 plugin level caps, read-only E receipts, no double award")
	return failures.is_empty()
