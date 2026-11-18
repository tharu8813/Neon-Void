extends RefCounted
## C/E transaction boundary. Only E may grant plugin levels or rare bonuses.
## C reads verified E receipts; no C-owned RNG or rare payout transaction.
const E_GATEWAY := "res://scripts/rework/persistence/progression_gateway.gd"
const Catalog = preload("res://scripts/rework/plugins/plugin_catalog.gd")
const RARE_SOURCE := "nv_c_random_rare"
const RARE_LEDGER := "e_rare_bonus_v1"
const RARE_RNG_POLICY := "rare-event-v1"

static func commit_choice(offers: RefCounted, slot: int, slot_path: String,
		vaccine_id: String, generation: int, run_id: String,
		offer_source: String = "levelup", expected_revision: int = -1) -> Dictionary:
	if not ResourceLoader.exists(E_GATEWAY):
		return {"ok":false,"reason":"E_gateway_not_installed"}
	if slot_path.is_empty() or vaccine_id.is_empty() or run_id.is_empty() or generation < 1:
		return {"ok":false,"reason":"invalid_E_identity"}
	if not offer_source in ["levelup","hardware","event"]:
		return {"ok":false,"reason":"invalid_offer_source"}
	var choice: Dictionary = offers.prepare_card(slot)
	if choice.is_empty():
		return {"ok":false,"reason":"not_selectable_or_prepared"}
	var gateway: Script = load(E_GATEWAY)
	var command: Dictionary = {
		"action":"plugin_picked","vaccine_id":vaccine_id,"generation":generation,
		"run_id":run_id,"receipt_id":choice["receipt_id"],"offer_id":choice["offer_id"],
		"plugin_id":choice["plugin_id"],"rarity":choice["rarity"],
		"level":choice["level"],"offer_source":offer_source
	}
	var ack: Dictionary = gateway.transact_command(slot_path,command,expected_revision)
	if not bool(ack.get("applied",false)):
		offers.confirm_card(str(choice["receipt_id"]),{
			"applied":false,"receipt_id":choice["receipt_id"]})
		return {"ok":false,"reason":"E_commit_uncertain_or_rejected",
			"E_reason":ack.get("reason","unknown"),"requires_reconciliation":true,
			"receipt_id":choice["receipt_id"]}
	var rare_bonus: Dictionary = {}
	if str(choice["plugin_id"]) == RARE_SOURCE:
		# E #121 grants source and derived rare in the SAME transaction.
		# A lost ACK or invalid saved pair must NOT be displayed as success.
		var saved: Dictionary = gateway.snapshot(slot_path)
		rare_bonus = inspect_atomic_rare(saved,str(choice["receipt_id"]),
			vaccine_id,generation,run_id)
		if not bool(rare_bonus.get("ok",false)):
			offers.confirm_card(str(choice["receipt_id"]),{
				"applied":false,"receipt_id":choice["receipt_id"]})
			return {"ok":false,"reason":"E_rare_pair_needs_reconciliation",
				"detail":rare_bonus.get("reason","unknown"),
				"receipt_id":choice["receipt_id"],"requires_reconciliation":true}
		if int(rare_bonus.get("source_level",-1)) != int(choice["level"]):
			offers.confirm_card(str(choice["receipt_id"]),{
				"applied":false,"receipt_id":choice["receipt_id"]})
			return {"ok":false,"reason":"E_rare_source_level_mismatch",
				"receipt_id":choice["receipt_id"],"requires_reconciliation":true}
	var confirmed: Dictionary = offers.confirm_card(str(choice["receipt_id"]),{
		"applied":true,"receipt_id":choice["receipt_id"],"revision":ack.get("revision",-1)})
	if not bool(confirmed.get("ok",false)):
		return {"ok":false,"reason":"C_E_divergence","requires_reconciliation":true}
	var result: Dictionary = {"ok":true,"receipt_id":choice["receipt_id"],
		"revision":ack.get("revision",-1),"plugin":confirmed}
	if not rare_bonus.is_empty():
		result["rare_bonus"] = rare_bonus
	return result


static func inspect_atomic_rare(E_snapshot: Dictionary, source_receipt: String,
		vaccine_id: String, generation: int, run_id: String) -> Dictionary:
	## Read-only verification. This is a display/level authorization, NOT a grant.
	if source_receipt.is_empty() or vaccine_id.is_empty() or run_id.is_empty() or generation < 1:
		return {"ok":false,"reason":"invalid_identity"}
	if not bool(E_snapshot.get("ok",false)):
		return {"ok":false,"reason":"E_snapshot_unavailable"}
	var slot_state: Variant = E_snapshot.get("snapshot",null)
	if not slot_state is Dictionary or int(slot_state.get("generation",-1)) != generation:
		return {"ok":false,"reason":"E_generation_mismatch"}
	var vaccine: Variant = slot_state.get("vaccine",null)
	if not vaccine is Dictionary or str(vaccine.get("vaccine_id","")) != vaccine_id:
		return {"ok":false,"reason":"E_vaccine_mismatch"}
	var mission: Variant = vaccine.get("mission",null)
	if not mission is Dictionary or str(mission.get("run_id","")) != run_id:
		return {"ok":false,"reason":"E_active_run_mismatch"}
	var receipts: Variant = vaccine.get("receipts",null)
	var rolls: Variant = mission.get("environment_rolls",null)
	if not receipts is Dictionary or not rolls is Dictionary:
		return {"ok":false,"reason":"E_ledger_missing"}
	var history: Variant = rolls.get(RARE_LEDGER,null)
	if not history is Dictionary:
		return {"ok":false,"reason":"E_rare_ledger_missing"}
	var grant: Variant = history.get(source_receipt,null)
	if not grant is Dictionary:
		return {"ok":false,"reason":"E_rare_source_receipt_missing"}
	var bonus_receipt: String = str(grant.get("bonus_receipt",""))
	if bonus_receipt != source_receipt + ":rare:0" \
			or receipts.get(source_receipt,false) != true \
			or receipts.get(bonus_receipt,false) != true:
		return {"ok":false,"reason":"E_rare_receipt_pair_invalid"}
	if str(grant.get("rarity","")) != "rare" \
			or str(grant.get("rng_policy","")) != RARE_RNG_POLICY:
		return {"ok":false,"reason":"E_rare_policy_mismatch"}
	var bonus_id: String = str(grant.get("plugin_id",""))
	var entry: Dictionary = Catalog.find_by_id(bonus_id,Catalog.entries())
	var level_value: Variant = grant.get("level",null)
	if entry.is_empty() or str(entry.get("rarity","")) != "rare" \
			or typeof(level_value) not in [TYPE_INT,TYPE_FLOAT]:
		return {"ok":false,"reason":"E_rare_plugin_invalid"}
	var level: int = int(level_value)
	if level < 1 or float(level) != float(level_value) or level > int(entry.get("max_level",0)):
		return {"ok":false,"reason":"E_rare_level_out_of_range"}
	var obtained: Variant = mission.get("obtained_plugins",null)
	var carried: Variant = vaccine.get("carried",null)
	if not obtained is Dictionary or not carried is Dictionary:
		return {"ok":false,"reason":"E_loadout_missing"}
	var authoritative: Variant = obtained.get(bonus_id,carried.get(bonus_id,{}))
	if not authoritative is Dictionary or str(authoritative.get("rarity","")) != "rare" \
			or int(authoritative.get("level",0)) < level:
		return {"ok":false,"reason":"E_rare_bonus_not_in_inventory"}
	var source: Variant = obtained.get(RARE_SOURCE,carried.get(RARE_SOURCE,{}))
	var source_entry: Dictionary = Catalog.find_by_id(RARE_SOURCE,Catalog.entries())
	if not source is Dictionary or str(source.get("rarity","")) != "common" \
			or int(source.get("level",0)) < 1 \
			or int(source.get("level",0)) > int(source_entry.get("max_level",0)):
		return {"ok":false,"reason":"E_rare_source_not_in_inventory"}
	return {"ok":true,"source_receipt":source_receipt,
		"bonus_receipt":bonus_receipt,"plugin_id":bonus_id,"rarity":"rare",
		"level":level,"source_level":int(source["level"]),
		"authoritative_level":int(authoritative["level"]),
		"revision":int(E_snapshot.get("revision",-1))}


static func apply_verified_rare(runtime: RefCounted, offers: RefCounted,
		E_snapshot: Dictionary, source_receipt: String,
		vaccine_id: String, generation: int, run_id: String) -> Dictionary:
	## One-way synchronization after E confirmation. Replayed ACK is a no-op.
	var verified: Dictionary = inspect_atomic_rare(E_snapshot,source_receipt,
		vaccine_id,generation,run_id)
	if not bool(verified.get("ok",false)):
		return verified
	if runtime == null or offers == null:
		return {"ok":false,"reason":"C_runtime_or_offer_missing"}
	var uid: String = str(verified["plugin_id"])
	var level: int = int(verified["level"])
	if int(verified["authoritative_level"]) != level:
		return {"ok":false,"reason":"E_rare_receipt_stale_reload_levels"}
	var runtime_level: int = int(runtime.levels.get(uid,0))
	var offer_level: int = int(offers.levels.get(uid,0))
	if not runtime_level in [level-1,level] or not offer_level in [level-1,level]:
		return {"ok":false,"reason":"C_E_level_divergence_requires_reload"}
	if runtime_level == level and offer_level == level:
		var replay: Dictionary = verified.duplicate(true)
		replay["already_synced"] = true
		return replay
	if runtime_level == level-1 and not runtime.set_plugin_level(uid,level):
		return {"ok":false,"reason":"C_runtime_update_rejected"}
	offers.levels[uid] = level
	var applied: Dictionary = verified.duplicate(true)
	applied["already_synced"] = false
	return applied


static func commit_random_rare(_runtime: RefCounted, _slot_path: String,
		_vaccine_id: String, _generation: int, _run_id: String,
		_trigger_event_id: String, _stable_seed: int, _grant_index: int = 0) -> Dictionary:
	## Backward-compatible API guard. Previously this independently paid a
	## SECOND reward. Never generate rare RNG, receipts or E transactions here.
	return {"ok":false,"reason":"rare_grant_owned_by_E_source_transaction"}
