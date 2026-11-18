extends SceneTree
## Standalone Godot 4 headless tests. Run via --headless --path . --script ...
const Catalog = preload("res://scripts/rework/plugins/plugin_catalog.gd")
const Offers = preload("res://scripts/rework/plugins/plugin_offer_engine.gd")
const Policy = preload("res://scripts/rework/plugins/plugin_proc_policy.gd")

func _initialize() -> void:
	call_deferred("_run")

func _check(ok: bool, label: String) -> bool:
	if not ok:
		printerr("REWORK PLUGINS FAIL: " + label)
		quit(1)
		return false
	return true

func _durable_claim(engine: RefCounted, slot: int) -> Dictionary:
	var prepared: Dictionary = engine.choose_card(slot)
	if prepared.is_empty():
		return {}
	if engine.levels.get(prepared["plugin_id"],0) == int(prepared["level"]):
		return {}
	return engine.confirm_card(str(prepared["receipt_id"]), {
		"applied":true,"receipt_id":prepared["receipt_id"],"revision":1})

func _run() -> void:
	var entries: Array[Dictionary] = Catalog.entries()
	if not _check(entries.size() == 27 and Catalog.validate(entries).is_empty(), "catalog validation"):
		return
	var counts: Dictionary = {"common": 0, "rare": 0, "legendary": 0, "mythic": 0}
	for item in entries:
		counts[item["rarity"]] += 1
		if not _check(bool(item["learnable"]) == (str(item["rarity"]) in ["common", "rare"]), "learning policy"):
			return
	if not _check(counts == {"common": 17, "rare": 7, "legendary": 3, "mythic": 0}, "GDD 17/7/3 and no invented mythics"):
		return
	if not _check(Catalog.find_by_id("nv_c_chain_lightning", entries)["internal_cooldown_seconds"] == 3, "chain target cooldown"):
		return
	if not _check(Catalog.find_by_id("nv_c_periodic_shield", entries)["internal_cooldown_seconds"] == 30, "shield interval"):
		return
	if not _check(Catalog.find_by_id("nv_c_low_hp_emergency", entries)["internal_cooldown_seconds"] == 60, "low HP cooldown"):
		return

	var first = Offers.new()
	var same = Offers.new()
	var ctx: Dictionary = {"cleared_hardware": 2, "difficulty_tier": 2, "luck_points": 7.0}
	if not _check(first.begin_mission(9891) and same.begin_mission(9891), "begin mission"):
		return
	var offer: Dictionary = first.roll_offer(ctx)
	var replay: Dictionary = same.roll_offer(ctx)
	if not _check(offer["cards"] == replay["cards"] and offer["cards"].size() == 3, "deterministic seed / 3 slots"):
		return
	if not _check(first.roll_offer(ctx)["offer_id"] == offer["offer_id"], "open offer idempotence"):
		return
	var original_cards: Array = offer["cards"]
	var rerolled: Dictionary = first.reroll_card(1)
	if not _check(rerolled["cards"][0] == original_cards[0] and rerolled["cards"][2] == original_cards[2], "reroll changes only selected slot"):
		return
	if not _check(rerolled["offer_id"] == offer["offer_id"] and first.rerolls_remaining == 2, "reroll keeps receipt and spends once"):
		return
	if not _check(first.missed_screens == 0 and first.reroll_card(-1).is_empty(), "reroll not counted as screen / invalid index"):
		return
	if not _check(first.add_rerolls(2) and first.rerolls_remaining == 4 and not first.add_rerolls(0), "extra reroll grants"):
		return
	if not _check(first.reroll_card(1).size() > 0 and first.reroll_card(1).size() > 0 and first.reroll_card(1).size() > 0, "independent repeated selected card rerolls"):
		return
	if not _check(first.rerolls_remaining == 1, "reroll budget"):
		return
	var chosen: Dictionary = _durable_claim(first, 0)
	if not _check(not chosen.is_empty() and chosen["level"] == 1 and chosen["receipt_id"] == str(offer["offer_id"]) + ":0", "claim receipt and level"):
		return
	if not _check(first.choose_card(0).is_empty(), "no double claim"):
		return
	if not _check(first.levels[chosen["plugin_id"]] == 1, "single inventory entry"):
		return

	var other_run = Offers.new()
	if not _check(other_run.begin_mission(9891, {}, "run-other") and offer["offer_id"] != other_run.roll_offer(ctx)["offer_id"], "mission-scoped receipt namespace"):
		return
	var scoped_replay = Offers.new()
	if not _check(scoped_replay.begin_mission(9891, {}, "run-other") and scoped_replay.roll_offer(ctx)["cards"] == replay["cards"], "run_id does not change gameplay RNG"):
		return
	var seen_different: bool = false
	var seen_same: bool = false
	for i in range(120):
		same.begin_mission(1000 + i)
		var cards: Array = same.roll_offer()["cards"]
		if cards[0]["rarity"] != cards[1]["rarity"] or cards[1]["rarity"] != cards[2]["rarity"]:
			seen_different = true
		if cards[0]["rarity"] == cards[1]["rarity"]:
			seen_same = true
	if not _check(seen_different and seen_same, "per-slot independent rarity results"):
		return

	var base: Dictionary = same.rarity_probabilities()
	var boosted: Dictionary = same.rarity_probabilities({"cleared_hardware": 5, "difficulty_tier": 4, "luck_points": 30.0})
	if not _check(float(boosted["legendary"]) > float(base["legendary"]), "clear / difficulty / luck affect high rarity"):
		return
	if not _check(is_equal_approx(float(base["common"]) + float(base["rare"]) + float(base["legendary"]) + float(base["mythic"]), 100.0), "eligible weights normalize to 100"):
		return
	if not _check(float(base["mythic"]) == 0.0, "undefined mythic is never rolled"):
		return

	# Force an only-common catalog to prove five misses are five SCREENS, not 15 cards.
	var common_only: Array[Dictionary] = [entries[0].duplicate(true)]
	var pity_engine = Offers.new(common_only)
	if not _check(pity_engine.begin_mission(12345) and pity_engine.rerolls_remaining == 3, "default reroll budget"):
		return
	for screen_index in range(5):
		var opened: Dictionary = pity_engine.roll_offer()
		if not _check(opened["cards"].size() == 3 and pity_engine.missed_screens == screen_index, "pity not added on open"):
			return
		if screen_index == 0:
			pity_engine.reroll_card(0)
			if not _check(pity_engine.missed_screens == 0, "pity not added per reroll"):
				return
		if not _check(pity_engine.dismiss_offer(), "settle miss"):
			return
		if not _check(pity_engine.missed_screens == screen_index + 1 and not pity_engine.dismiss_offer(), "exactly one increment / no second settle"):
			return
	var pity_weights_before: Dictionary = same.rarity_probabilities()
	same.missed_screens = 5
	var pity_weights_after: Dictionary = same.rarity_probabilities()
	if not _check(float(pity_weights_after["legendary"]) > float(pity_weights_before["legendary"]), "5 missed screens trigger odds increase on next offer"):
		return
	if not _check(pity_engine.begin_mission(12345) and pity_engine.missed_screens == 0, "pity resets on new mission"):
		return

	var settings: Dictionary = Catalog.odds_settings()
	settings["rarity_weights"] = {"common": 0, "rare": 0, "legendary": 100, "mythic": 0}
	var one_legend: Array[Dictionary] = [Catalog.find_by_id("nv_l_laser", entries)]
	var high_engine = Offers.new(one_legend, settings)
	if not _check(high_engine.begin_mission(73), "forced high rarity test begins"):
		return
	high_engine.missed_screens = 7
	var high_offer: Dictionary = high_engine.roll_offer()
	if not _check(high_offer["high_seen"] and high_offer["cards"][0]["rarity"] == "legendary", "legendary seen even if not selected"):
		return
	if not _check(high_engine.dismiss_offer() and high_engine.missed_screens == 0, "high appearance resets pity at settlement"):
		return

	# Duplicate card selection upgrades an existing ID, then exhausts only candidate.
	var single = Offers.new(common_only)
	if not _check(single.begin_mission(412), "single candidate begins"):
		return
	for next_level in range(1, int(common_only[0]["max_level"]) + 1):
		var cards: Array = single.roll_offer()["cards"]
		if not _check(cards[0]["plugin_id"] == common_only[0]["plugin_id"] and cards[0]["plugin_id"] == cards[1]["plugin_id"], "same screen duplicates provisional policy"):
			return
		var claim: Dictionary = _durable_claim(single, 0)
		if not _check(int(claim["level"]) == next_level, "duplicate level increment"):
			return
	var capped: Dictionary = single.roll_offer()
	if not _check(capped["cards"][0]["empty"] and capped["cards"][1]["empty"] and capped["cards"][2]["empty"], "max level excluded and zero candidate cards"):
		return
	var before_empty_miss: int = single.missed_screens
	var before_empty_rerolls: int = single.rerolls_remaining
	if not _check(single.reroll_card(0).is_empty() and single.rerolls_remaining == before_empty_rerolls, "empty card does not waste reroll"):
		return
	if not _check(single.choose_card(0).is_empty() and single.dismiss_offer() and single.missed_screens == before_empty_miss, "empty cannot claim or add pity"):
		return
	if not _check(not single.is_eligible(str(common_only[0]["plugin_id"])), "maxed ID not eligible"):
		return

	var all_maxed: Dictionary = {}
	for item in entries:
		all_maxed[str(item["plugin_id"])] = int(item["max_level"])
	var exhausted = Offers.new()
	if not _check(exhausted.begin_mission(9, all_maxed), "all maxed levels valid"):
		return
	var all_empty: Dictionary = exhausted.roll_offer()
	if not _check(all_empty["cards"][2]["empty"] and exhausted.rarity_probabilities()["legendary"] == 0.0, "exhausted pool safe"):
		return
	if not _check(not exhausted.begin_mission(9, {"invalid_id": 1}) and exhausted.levels == all_maxed, "invalid saved levels rejected atomically"):
		return

	# The same effect can chain onto a NEW target, but may not revisit its own
	# effect + actor + target tuple in this ancestry (B PR #100 semantics).
	var first_hop: Dictionary = Policy.try_extend_path([], "lightning", "vaccine", "enemy1")
	var second_hop: Dictionary = Policy.try_extend_path(first_hop["proc_path"], "explosion", "vaccine", "enemy1")
	var looped: Dictionary = Policy.try_extend_path(second_hop["proc_path"], "lightning", "vaccine", "enemy1")
	var different_target: Dictionary = Policy.try_extend_path(second_hop["proc_path"], "lightning", "vaccine", "enemy2")
	if not _check(first_hop["allowed"] and second_hop["allowed"] and not looped["allowed"] and different_target["allowed"], "target-aware multi-hop, reject only cyclic signature"):
		return
	if not _check(not Policy.try_extend_path([], "", "vaccine", "enemy1")["allowed"], "invalid proc identity rejected"):
		return
	var lightning: Dictionary = Catalog.find_by_id("nv_c_chain_lightning", entries)
	if not _check(Policy.may_emit_tag(lightning, "proc_damage") and Policy.may_trigger_from_source(lightning, ["basic_attack"]), "proc event permissions"):
		return
	if not _check(Policy.derived_attack_may_proc(lightning) and not Policy.may_trigger_from_source(lightning, ["dot"]), "chain approved as provisional opt-in / source denied"):
		return
	var safe_passive: Dictionary = Catalog.find_by_id("nv_c_periodic_shield", entries)
	if not _check(not Policy.derived_attack_may_proc(safe_passive), "unlisted derived attacks default to denied"):
		return
	var extra: Dictionary = Catalog.find_by_id("nv_r_extra_bullet", entries)
	if not _check(Policy.derived_attack_may_proc(extra) and is_equal_approx(float(extra["proc_permissions"]["on_hit_damage_efficiency"]), 0.80), "80% on-hit efficiency preserved as data"):
		return
	var laser: Dictionary = Catalog.find_by_id("nv_l_laser", entries)
	if not _check(Policy.derived_attack_may_proc(laser) and is_equal_approx(float(laser["proc_permissions"]["on_hit_damage_efficiency"]), 0.70), "laser 70% on-hit efficiency data"):
		return
	var live_suite = preload("res://tests/rework/plugins/plugin_effect_assertions.gd").new()
	if not _check(live_suite.execute_tests(), "live effect + save acknowledgement regressions"):
		return
	var save_source = Offers.new()
	if not _check(save_source.begin_mission(78123,{},"restore-c-1"),"state source starts"):
		return
	save_source.roll_offer({"difficulty_tier":2,"luck_points":3.0})
	save_source.reroll_card(1)
	var durable_packet: Dictionary = save_source.export_mission_state()
	var save_reload = Offers.new()
	if not _check(save_reload.restore_mission_state(durable_packet,save_source.levels),
		"reopen E-verified RNG, pity and per-card rerolls"):
		return
	if not _check(save_source.offer_snapshot()["cards"]==save_reload.offer_snapshot()["cards"]
		and save_source.rerolls_remaining==save_reload.rerolls_remaining
		and save_source.missed_screens==save_reload.missed_screens,
		"resume preserves existing three cards and reroll budget"):
		return
	save_source.dismiss_offer()
	save_reload.dismiss_offer()
	if not _check(save_source.roll_offer()["cards"]==save_reload.roll_offer()["cards"],
		"JSON-safe 64bit RNG state resumes next independent draw"):
		return
	var broken_packet: Dictionary = durable_packet.duplicate(true)
	broken_packet["levels"] = {"unknown":1}
	if not _check(not Offers.new().restore_mission_state(broken_packet,{}),
		"reject tampered or uncommitted E levels"):
		return
	var preparing = Offers.new()
	preparing.begin_mission(188,{},"resume-prepared")
	preparing.roll_offer()
	var preparing_choice: Dictionary = preparing.prepare_card(0)
	var pending_packet: Dictionary = preparing.export_mission_state()
	var recovery = Offers.new()
	if not _check(recovery.restore_mission_state(pending_packet,preparing.levels)
		and bool(recovery.offer_snapshot()["reconciliation_required"])
		and recovery.reroll_card(0).is_empty(),
		"interrupted prepared receipt stays frozen until E reconciliation"):
		return
	if not _check(recovery.reconcile_card(str(preparing_choice["receipt_id"]),
		{"receipt_definitely_absent":true}).get("retry_same_receipt",false),
		"E-authorized definitely-absent receipt may retry without duplicate"):
		return
	var actual_b = preload("res://tests/rework/plugins/plugin_b_live_cases.gd").new()
	if not _check(actual_b.run(), "production B runtime plugin tests"):
		quit(1)
		return
	var rare_contract = preload("res://tests/rework/plugins/plugin_atomic_rare_cases.gd").new()
	if not _check(rare_contract.run(), "E-only rare receipts and all 27 level caps"):
		return
	var production_shields = preload("res://tests/rework/plugins/plugin_s01_production_matrix.gd").new()
	if not _check(production_shields.run(), "S01 production B adapter shield levels + actual HP"):
		return
	print("REWORK PLUGINS PASS: 27 entries, seeded draws, reroll, pity, caps, E rare receipts, proc path")
	quit(0)
