extends RefCounted
## S01 full-catalog E durable receipt -> production B CAdapter activation test.
## Fresh synthetic save slots only. E has no C proxy grant, C cannot forge ACK.
const Catalog = preload("res://scripts/rework/plugins/plugin_catalog.gd")
const Offers = preload("res://scripts/rework/plugins/plugin_offer_engine.gd")
const Coordinator = preload("res://scripts/rework/plugins/plugin_reward_coordinator.gd")
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")
const Model = preload("res://scripts/rework/progression/state_model.gd")
const Gateway = preload("res://scripts/rework/persistence/progression_gateway.gd")
const Combat = preload("res://scripts/rework/combat/combat_runtime.gd")
const Adapter = preload("res://scripts/rework/combat/c_effect_adapter.gd")
const Effect = preload("res://scripts/rework/plugins/plugin_effect_runtime.gd")
const PASSIVE_KINDS := ["random_stats","passive_stat","dual_penetration",
	"dual_speed","breakable_sprint"]
var failures: Array[String] = []
var confirmed_ids: Array[String] = []
var live_stats_ids: Array[String] = []

func _check(condition: bool, label: String) -> void:
	if not condition:
		failures.append(label)
		printerr("C S01 E-B FAIL: "+label)

func _clean_test_directory(folder: String) -> void:
	var dir: DirAccess = DirAccess.open(folder)
	if dir == null:
		return
	dir.list_dir_begin()
	var filename: String = dir.get_next()
	while not filename.is_empty():
		var current: String = folder.path_join(filename)
		if dir.current_is_dir():
			_clean_test_directory(current)
		else:
			DirAccess.remove_absolute(current)
		filename = dir.get_next()
	dir.list_dir_end()
	DirAccess.remove_absolute(folder)

func _production_adapter(run_id: String, seed: int) -> Variant:
	var b = Combat.new(seed,{"encounter_id":"c-s01-"+run_id})
	if not b.register_actor("VACCINE","vaccine",Vector3.ZERO,{
		"health":100.0,"max_health":100.0,"attack_damage":5.0,
		"ability_power":10.0,"move_speed":5.0,"attack_speed":0.0,
		"armor":0.0,"magic_resist":0.0},{
		"player_controlled":true,"dash_definition":{
			"distance":2.0,"cooldown_ticks":60}}):
		return null
	var adapter = Adapter.new()
	if not adapter.attach(b,"VACCINE",run_id,seed):
		return null
	if not adapter.configure_levels({},seed):
		return null
	return adapter

func run() -> bool:
	var test_root: String = ProjectSettings.globalize_path("user://rework-c-s01-eb-" +
		str(Time.get_ticks_usec()) + "-" + str(OS.get_process_id()))
	var entries: Array[Dictionary] = Catalog.entries()
	var specs = Effect.new()
	_check(entries.size()==27,"canonical 27 IDs are available")
	for index in range(entries.size()):
		var entry: Dictionary = entries[index]
		var uid: String = str(entry["plugin_id"])
		var label: String = uid + " E->B"
		var seed: int = 1800+index
		var slot_path: String = test_root.path_join("rework-slot-s01-"+str(index))
		var run_id: String = "run-c-s01-"+str(index)
		var vaccine_id: String = "vaccine-c-s01"
		var created: Dictionary = Store.initialize(slot_path,
			Model.new_slot("s01-"+str(index),vaccine_id))
		if not bool(created.get("ok",false)):
			_check(false,label+" isolated E slot created")
			continue
		var started: Dictionary = Gateway.transact_command(slot_path,{
			"action":"start_mission","vaccine_id":vaccine_id,"generation":1,
			"run_id":run_id,"seed":seed})
		if not bool(started.get("applied",false)):
			_check(false,label+" mission started")
			continue
		var xp: Dictionary = Gateway.transact_command(slot_path,{
			"action":"mission_xp","vaccine_id":vaccine_id,"generation":1,
			"run_id":run_id,"receipt_id":"s01-xp-"+str(index),"amount":100})
		if not bool(xp.get("applied",false)):
			_check(false,label+" E authorized a card")
			continue
		var single_entry: Array[Dictionary] = [entry]
		var offers = Offers.new(single_entry)
		if not offers.begin_mission(seed,{},run_id):
			_check(false,label+" C initial RNG")
			continue
		var screen: Dictionary = offers.roll_offer()
		if str(screen.get("cards",[])[0].get("plugin_id",""))!=uid:
			_check(false,label+" deterministic single-eligible card")
			continue
		var adapter: Variant = _production_adapter(run_id,seed)
		if adapter == null:
			_check(false,label+" actual B combat adapter attached")
			continue
		var baseline: Dictionary = adapter.stats_for_player()
		_check(adapter.active_levels().is_empty(),label+" cannot pre-equip before E commit")
		var pending: Dictionary = offers.prepare_card(0)
		_check(not pending.is_empty() and offers.levels.is_empty()
			and adapter.active_levels().is_empty(),label+" preparing cannot mutate C/B levels")
		# Repeated same receipt is idempotent; Coordinator must use E transaction.
		var ack: Dictionary = Coordinator.commit_choice(offers,0,slot_path,
			vaccine_id,1,run_id)
		if not bool(ack.get("ok",false)):
			_check(false,label+" E durable commit success: "+str(ack.get("reason","")))
			continue
		var saved: Dictionary = Gateway.snapshot(slot_path)
		var obtained: Dictionary = saved.get("snapshot",{}).get("vaccine",{}).get(
			"mission",{}).get("obtained_plugins",{})
		_check(bool(saved.get("ok",false)) and int(obtained.get(uid,{}).get(
			"level",0))==1,label+" E disk read verifies Lv1")
		_check(adapter.active_levels().is_empty(),label+" B still unchanged even after C confirmation")
		if not bool(adapter.upgrade_plugin(uid,1)):
			_check(false,label+" B apply after E verified receipt")
			continue
		if uid == "nv_c_random_rare":
			var source_receipt: String = str(ack.get("receipt_id",""))
			var rare: Dictionary = Coordinator.apply_verified_rare(adapter.c_engine,
				offers,saved,source_receipt,vaccine_id,1,run_id)
			_check(bool(rare.get("ok",false)) and
				int(adapter.active_levels().get(str(rare.get("plugin_id","")),0))==
				int(rare.get("level",-1)),label+" E only rare bonus synced to B")
			var replay: Dictionary = Coordinator.apply_verified_rare(adapter.c_engine,
				offers,saved,source_receipt,vaccine_id,1,run_id)
			_check(bool(replay.get("already_synced",false)),label+" rare replay never double grants")
		var active: Dictionary = adapter.active_levels()
		_check(int(active.get(uid,0))==1,label+" authoritative B runtime has Lv1")
		if int(active.get(uid,0))==1:
			confirmed_ids.append(uid)
		var kind: String = str(specs.effect_definition(uid).get("kind",""))
		if kind in PASSIVE_KINDS:
			var next_stats: Dictionary = adapter.stats_for_player()
			_check(next_stats!=baseline,label+" actual B effective stats change")
			if next_stats!=baseline:
				live_stats_ids.append(uid)
		# E rejects a duplicate source receipt, with no additional B change.
		var replay_ack: Dictionary = Gateway.transact_command(slot_path,{
			"action":"plugin_picked","vaccine_id":vaccine_id,"generation":1,
			"run_id":run_id,"receipt_id":str(ack.get("receipt_id","")),
			"offer_id":str(ack.get("plugin",{}).get("offer_id","")),
			"plugin_id":uid,"rarity":str(entry["rarity"]),"level":1,
			"offer_source":"levelup"})
		_check(not bool(replay_ack.get("applied",false)) and
			int(adapter.active_levels().get(uid,0))==1,
			label+" duplicate E receipt never upgrades B twice")
	_clean_test_directory(test_root)
	print("C S01 E-B MATRIX: %d/27 E receipts synced to real B; %d passive stat alterations; failures=%s" %
		[confirmed_ids.size(),live_stats_ids.size(),str(failures)])
	return failures.is_empty() and confirmed_ids.size()==27 and live_stats_ids.size()>=5
