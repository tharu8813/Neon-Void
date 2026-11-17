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

func run() -> bool:
	var entries: Array[Dictionary] = Catalog.entries()
	_check(entries.size() == 27 and Catalog.validate(entries).is_empty(),
		"canonical 27 entries")
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
	if failures.is_empty():
		print("C ATOMIC RARE PASS: 27 plugin level caps, read-only E receipts, no double award")
	return failures.is_empty()
