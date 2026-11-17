extends RefCounted
## C/E transaction orchestration helper. E remains sole persistence owner.
## Use commit_choice AFTER showing a read-only offer. Do not call choose_card
## alone then show "acquired". Requires E gateway to be installed by A.
const E_GATEWAY := "res://scripts/rework/persistence/progression_gateway.gd"
const Catalog = preload("res://scripts/rework/plugins/plugin_catalog.gd")

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
	var confirmed: Dictionary = offers.confirm_card(str(choice["receipt_id"]),{
		"applied":true,"receipt_id":choice["receipt_id"],"revision":ack.get("revision",-1)})
	if not bool(confirmed.get("ok",false)):
		return {"ok":false,"reason":"C_E_divergence","requires_reconciliation":true}
	return {"ok":true,"receipt_id":choice["receipt_id"],
		"revision":ack.get("revision",-1),"plugin":confirmed}


static func commit_random_rare(runtime: RefCounted, slot_path: String,
		vaccine_id: String, generation: int, run_id: String,
		trigger_event_id: String, stable_seed: int, grant_index: int = 0) -> Dictionary:
	## Auto-award from nv_c_random_rare. Uses E's existing event-source
	## plugin_picked gateway, not a new persistence type or unaudited mutation.
	if not ResourceLoader.exists(E_GATEWAY):
		return {"ok":false,"reason":"E_gateway_not_installed"}
	if slot_path.is_empty() or vaccine_id.is_empty() or run_id.is_empty() or trigger_event_id.is_empty() or generation < 1 or grant_index < 0:
		return {"ok":false,"reason":"invalid_rare_event_identity"}
	var all: Array[Dictionary] = Catalog.entries()
	var eligible: Array[String] = []
	for entry in all:
		var uid: String = str(entry["plugin_id"])
		if str(entry.get("rarity","")) == "rare" and int(runtime.levels.get(uid,0)) < int(entry["max_level"]):
			eligible.append(uid)
	if eligible.is_empty():
		return {"ok":false,"reason":"rare_pool_exhausted"}
	eligible.sort()
	var rng := RandomNumberGenerator.new()
	rng.seed = stable_seed + grant_index * 104729
	var plugin_id: String = eligible[rng.randi_range(0,eligible.size()-1)]
	var next_level: int = int(runtime.levels.get(plugin_id,0)) + 1
	var offer_id: String = run_id + ":rare:" + trigger_event_id + ":" + str(grant_index)
	var receipt_id: String = offer_id + ":0"
	var gateway: Script = load(E_GATEWAY)
	var ack: Dictionary = gateway.transact_command(slot_path,{
		"action":"plugin_picked","vaccine_id":vaccine_id,
		"generation":generation,"run_id":run_id,"receipt_id":receipt_id,
		"offer_id":offer_id,"plugin_id":plugin_id,"rarity":"rare",
		"level":next_level,"offer_source":"event"})
	if not bool(ack.get("applied",false)):
		return {"ok":false,"reason":"E_rare_receipt_rejected_or_uncertain",
			"E_reason":ack.get("reason","unknown"),"receipt_id":receipt_id,
			"requires_reconciliation":true}
	if not runtime.set_plugin_level(plugin_id,next_level):
		return {"ok":false,"reason":"C_E_level_divergence",
			"receipt_id":receipt_id,"requires_reconciliation":true}
	return {"ok":true,"receipt_id":receipt_id,"plugin_id":plugin_id,
		"level":next_level,"rarity":"rare","revision":ack.get("revision",-1)}
