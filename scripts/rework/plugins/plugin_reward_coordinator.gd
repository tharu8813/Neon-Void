extends RefCounted
## C/E transaction orchestration helper. E remains sole persistence owner.
## Use commit_choice AFTER showing a read-only offer. Do not call choose_card
## alone then show "acquired". Requires E gateway to be installed by A.
const E_GATEWAY := "res://scripts/rework/persistence/progression_gateway.gd"

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
