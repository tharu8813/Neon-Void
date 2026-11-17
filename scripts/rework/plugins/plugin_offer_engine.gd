extends RefCounted
## Isolated reward engine. A owns UI, B combat, E persistence.
const Catalog = preload("res://scripts/rework/plugins/plugin_catalog.gd")
const RARITIES := ["common", "rare", "legendary", "mythic"]

var _rng := RandomNumberGenerator.new()
var _catalog: Array[Dictionary] = []
var _settings: Dictionary = {}
var _started: bool = false
var _seed: int = 0
var _run_id: String = ""
var _offer_sequence: int = 0
var _pending: Dictionary = {}
var _prepared: Dictionary = {}
var _commit_uncertain: bool = false
var levels: Dictionary = {}
var missed_screens: int = 0
var rerolls_remaining: int = 0
var validation_errors: Array[String] = []

func _init(custom_entries: Array[Dictionary] = [], custom_settings: Dictionary = {}) -> void:
	_catalog = custom_entries.duplicate(true) if not custom_entries.is_empty() else Catalog.entries()
	_settings = custom_settings.duplicate(true) if not custom_settings.is_empty() else Catalog.odds_settings()
	validation_errors = Catalog.validate(_catalog)
	if not _settings.has("rarity_weights") or not _settings.has("modifiers") or not _settings.has("pity"):
		validation_errors.append("Missing odds settings")

func begin_mission(seed_value: int, initial_levels: Dictionary = {}, run_id: String = "") -> bool:
	if not validation_errors.is_empty():
		return false
	for uid in initial_levels:
		var entry: Dictionary = Catalog.find_by_id(str(uid), _catalog)
		var value: Variant = initial_levels[uid]
		if entry.is_empty() or typeof(value) != TYPE_INT or value < 0 or value > int(entry["max_level"]):
			return false
	_seed = seed_value
	_run_id = run_id if not run_id.is_empty() else str(seed_value)
	_rng.seed = seed_value
	_started = true
	_offer_sequence = 0
	_pending.clear()
	_prepared.clear()
	_commit_uncertain = false
	levels = initial_levels.duplicate(true)
	missed_screens = 0
	rerolls_remaining = maxi(0, int(_settings.get("base_rerolls", 3)))
	return true

func export_mission_state() -> Dictionary:
	## A/E must atomically persist this packet with the authoritative mission.
	## This method does NOT write user data.
	if not _started:
		return {}
	return {"schema":1,"run_id":_run_id,"seed":_seed,
		"rng_state":str(_rng.state),"offer_sequence":_offer_sequence,
		"levels":levels.duplicate(true),"missed_screens":missed_screens,
		"rerolls_remaining":rerolls_remaining,"pending":_pending.duplicate(true),
		"prepared":_prepared.duplicate(true),"commit_uncertain":_commit_uncertain}

func restore_mission_state(snapshot: Dictionary, E_levels: Dictionary) -> bool:
	## Fail closed: caller must supply levels loaded from E's verified snapshot.
	## A must reconcile any pending receipt with E before enabling the UI.
	if int(snapshot.get("schema",0)) != 1 or not snapshot.get("run_id",null) is String:
		return false
	var saved_run: String = str(snapshot["run_id"])
	if saved_run.is_empty() or saved_run.length()>100:
		return false
	var saved_levels: Variant = snapshot.get("levels",null)
	if not saved_levels is Dictionary or saved_levels != E_levels:
		return false
	for uid in E_levels:
		var entry: Dictionary = get_entry(str(uid))
		if entry.is_empty() or typeof(E_levels[uid]) != TYPE_INT or int(E_levels[uid])<0 or int(E_levels[uid])>int(entry["max_level"]):
			return false
	for name in ["seed","offer_sequence","missed_screens","rerolls_remaining"]:
		if typeof(snapshot.get(name,null)) != TYPE_INT:
			return false
	if not snapshot.get("rng_state",null) is String or not str(snapshot["rng_state"]).is_valid_int():
		return false
	if int(snapshot["offer_sequence"])<0 or int(snapshot["missed_screens"])<0 or int(snapshot["rerolls_remaining"])<0:
		return false
	var pending: Variant = snapshot.get("pending",null)
	var prepared: Variant = snapshot.get("prepared",null)
	if not pending is Dictionary or not prepared is Dictionary or typeof(snapshot.get("commit_uncertain",null)) != TYPE_BOOL:
		return false
	if not pending.is_empty():
		if int(snapshot["offer_sequence"])<1 or str(pending.get("offer_id",""))!=saved_run+":"+str(snapshot["offer_sequence"]):
			return false
		var cards: Variant = pending.get("cards",null)
		if not cards is Array or cards.size()!=3 or not pending.get("context",null) is Dictionary or typeof(pending.get("high_seen",null))!=TYPE_BOOL:
			return false
		for i in range(3):
			if not cards[i] is Dictionary or int(cards[i].get("slot",-1))!=i:
				return false
			var uid: String = str(cards[i].get("plugin_id",""))
			if uid.is_empty():
				if not bool(cards[i].get("empty",false)):
					return false
				continue
			var entry: Dictionary = get_entry(uid)
			if entry.is_empty() or str(cards[i].get("rarity",""))!=str(entry["rarity"]):
				return false
			if int(E_levels.get(uid,0))>=int(entry["max_level"]) and prepared.is_empty():
				return false
	elif not prepared.is_empty():
		return false
	if not prepared.is_empty():
		if int(prepared.get("slot",-1))<0 or int(prepared.get("slot",-1))>=3 or not prepared.get("choice",null) is Dictionary:
			return false
		var choice: Dictionary = prepared["choice"]
		if str(choice.get("offer_id",""))!=str(pending.get("offer_id","")) or str(choice.get("receipt_id",""))!=str(choice.get("offer_id",""))+":"+str(prepared["slot"]):
			return false
		if str(choice.get("plugin_id",""))!=str(pending["cards"][int(prepared["slot"])].get("plugin_id","")):
			return false
	if not begin_mission(int(snapshot["seed"]),E_levels,saved_run):
		return false
	_rng.state = str(snapshot["rng_state"]).to_int()
	_offer_sequence = int(snapshot["offer_sequence"])
	missed_screens = int(snapshot["missed_screens"])
	rerolls_remaining = int(snapshot["rerolls_remaining"])
	_pending = pending.duplicate(true)
	_prepared = prepared.duplicate(true)
	# Never treat an interrupted prepared selection as confirmed. E must check
	# the durable receipt and call reconcile_card explicitly.
	_commit_uncertain = bool(snapshot["commit_uncertain"]) or not _prepared.is_empty()
	return true

func get_catalog() -> Array[Dictionary]:
	return _catalog.duplicate(true)

func get_entry(plugin_id: String) -> Dictionary:
	return Catalog.find_by_id(plugin_id, _catalog)

func is_eligible(plugin_id: String) -> bool:
	var entry: Dictionary = get_entry(plugin_id)
	return not entry.is_empty() and int(levels.get(plugin_id, 0)) < int(entry["max_level"])

func add_rerolls(amount: int) -> bool:
	if not _started or amount <= 0:
		return false
	rerolls_remaining += amount
	return true

func offer_snapshot() -> Dictionary:
	var snapshot: Dictionary = _pending.duplicate(true)
	snapshot["rerolls_remaining"] = rerolls_remaining
	snapshot["missed_screens"] = missed_screens
	snapshot["active"] = not _pending.is_empty()
	snapshot["prepared"] = _prepared.duplicate(true)
	snapshot["reconciliation_required"] = _commit_uncertain
	return snapshot

func roll_offer(context: Dictionary = {}) -> Dictionary:
	if not _started:
		return {}
	if not _pending.is_empty():
		return offer_snapshot()
	_offer_sequence += 1
	var safe_context: Dictionary = _clean_context(context)
	var cards: Array[Dictionary] = []
	var high_seen: bool = false
	for slot in range(3):
		var card: Dictionary = _draw_card(safe_context)
		card["slot"] = slot
		cards.append(card)
		high_seen = high_seen or _is_high(card)
	_pending = {
		"offer_id": _run_id + ":" + str(_offer_sequence),
		"context": safe_context,
		"cards": cards,
		"high_seen": high_seen
	}
	return offer_snapshot()

func reroll_card(slot: int) -> Dictionary:
	if _pending.is_empty() or not _prepared.is_empty() or _commit_uncertain or rerolls_remaining <= 0 or slot < 0 or slot >= 3:
		return {}
	var cards: Array = _pending["cards"]
	# All-eligible-candidates-exhausted cards cannot be rerolled at a cost.
	if bool(cards[slot].get("empty", false)):
		return {}
	var replacement: Dictionary = _draw_card(_pending["context"])
	replacement["slot"] = slot
	cards[slot] = replacement
	_pending["cards"] = cards
	_pending["high_seen"] = bool(_pending["high_seen"]) or _is_high(replacement)
	rerolls_remaining -= 1
	return offer_snapshot()

## Kept as a compatibility name for A's integration test: now PREPARES only.
## Never grant an inventory level before E confirms durable plugin_picked.
func choose_card(slot: int) -> Dictionary:
	return prepare_card(slot)

func prepare_card(slot: int) -> Dictionary:
	if _pending.is_empty() or _commit_uncertain or slot < 0 or slot >= 3:
		return {}
	if not _prepared.is_empty():
		if int(_prepared.get("slot",-1)) == slot:
			return _prepared["choice"].duplicate(true)
		return {}
	var cards: Array = _pending["cards"]
	var card: Dictionary = cards[slot]
	var plugin_id: String = str(card.get("plugin_id", ""))
	if plugin_id.is_empty() or not is_eligible(plugin_id):
		return {}
	var entry: Dictionary = get_entry(plugin_id)
	var choice: Dictionary = {
		"offer_id": _pending["offer_id"],
		"receipt_id": str(_pending["offer_id"]) + ":" + str(slot),
		"plugin_id": plugin_id,
		"rarity": entry["rarity"],
		"level": int(levels.get(plugin_id, 0)) + 1,
		"learnable": Catalog.is_learnable(entry)
	}
	_prepared = {"slot":slot,"choice":choice.duplicate(true)}
	return choice.duplicate(true)

func confirm_card(receipt_id: String, durable_ack: Dictionary) -> Dictionary:
	if _prepared.is_empty():
		return {"ok":false,"reason":"no_pending_choice"}
	var chosen: Dictionary = _prepared["choice"]
	if receipt_id != str(chosen["receipt_id"]):
		return {"ok":false,"reason":"receipt_mismatch"}
	# A must normalize E's applied result + the exact sent receipt ID.
	if not bool(durable_ack.get("applied",false)) or str(durable_ack.get("receipt_id","")) != receipt_id:
		_commit_uncertain = true
		return {"ok":false,"reason":"requires_E_reconciliation"}
	if not is_eligible(str(chosen["plugin_id"])):
		_commit_uncertain = true
		return {"ok":false,"reason":"level_divergence_requires_reconciliation"}
	levels[str(chosen["plugin_id"])] = int(chosen["level"])
	_prepared.clear()
	_commit_uncertain = false
	_settle_offer()
	var confirmed: Dictionary = chosen.duplicate(true)
	confirmed["ok"] = true
	return confirmed

func reconcile_card(receipt_id: String, E_verified: Dictionary) -> Dictionary:
	## This function is only for an E-verified persisted snapshot result.
	## Never infer receipt absence from timeout or a failed network response.
	if _prepared.is_empty() or receipt_id != str(_prepared["choice"]["receipt_id"]):
		return {"ok":false,"reason":"unknown_prepared_receipt"}
	if bool(E_verified.get("receipt_committed",false)):
		return confirm_card(receipt_id, {"applied":true,"receipt_id":receipt_id})
	if bool(E_verified.get("receipt_definitely_absent",false)):
		_commit_uncertain = false
		return {"ok":true,"retry_same_receipt":true}
	return {"ok":false,"reason":"unverified_reconciliation"}

func dismiss_offer() -> bool:
	if _pending.is_empty() or not _prepared.is_empty() or _commit_uncertain:
		return false
	_settle_offer()
	return true

func rarity_probabilities(context: Dictionary = {}) -> Dictionary:
	return _eligible_weights(_clean_context(context), _eligible_pools())

func _clean_context(context: Dictionary) -> Dictionary:
	return {
		"cleared_hardware": clampi(int(context.get("cleared_hardware", 0)), 0, 5),
		"difficulty_tier": maxi(0, int(context.get("difficulty_tier", 0))),
		"luck_points": maxf(0.0, float(context.get("luck_points", 0.0)))
	}

func _eligible_pools() -> Dictionary:
	var pools: Dictionary = {"common": [], "rare": [], "legendary": [], "mythic": []}
	for entry in _catalog:
		var plugin_id: String = str(entry["plugin_id"])
		if is_eligible(plugin_id):
			pools[str(entry["rarity"])].append(plugin_id)
	return pools

func _eligible_weights(context: Dictionary, pools: Dictionary) -> Dictionary:
	var weights: Dictionary = {"common": 0.0, "rare": 0.0, "legendary": 0.0, "mythic": 0.0}
	var base: Dictionary = _settings.get("rarity_weights", {})
	var mod: Dictionary = _settings.get("modifiers", {})
	var pity: Dictionary = _settings.get("pity", {})
	var cleared: float = float(context["cleared_hardware"])
	var difficulty: float = float(context["difficulty_tier"])
	var luck: float = float(context["luck_points"])
	var threshold: int = maxi(0, int(pity.get("missed_screens_before_boost", 5)))
	var extra: int = mini(maxi(0, missed_screens - threshold + 1), maxi(0, int(pity.get("max_extra_screens_for_boost", 20))))
	var total: float = 0.0
	for rarity in RARITIES:
		if pools[rarity].is_empty():
			continue
		var chance: float = maxf(0.0, float(base.get(rarity, 0.0)))
		if rarity != "common":
			chance *= 1.0 + cleared * float(mod.get("per_cleared_hardware_" + rarity, 0.0))
			chance *= 1.0 + difficulty * float(mod.get("per_difficulty_tier_" + rarity, 0.0))
			chance *= 1.0 + luck * float(mod.get("per_luck_point_" + rarity, 0.0))
		if rarity == "legendary" or rarity == "mythic":
			chance *= 1.0 + float(extra) * float(pity.get(rarity + "_multiplier_per_extra_screen", 0.0))
		weights[rarity] = maxf(0.0, chance)
		total += float(weights[rarity])
	if total > 0.0:
		for rarity in RARITIES:
			weights[rarity] = float(weights[rarity]) * 100.0 / total
	return weights

func _draw_card(context: Dictionary) -> Dictionary:
	var pools: Dictionary = _eligible_pools()
	var weights: Dictionary = _eligible_weights(context, pools)
	var total: float = 0.0
	for rarity in RARITIES:
		total += float(weights[rarity])
	if total <= 0.0:
		return {"plugin_id": "", "rarity": "", "empty": true}
	var rolled: float = _rng.randf() * total
	var cumulative: float = 0.0
	var chosen: String = ""
	for rarity in RARITIES:
		var weight: float = float(weights[rarity])
		if weight <= 0.0:
			continue
		cumulative += weight
		chosen = rarity
		if rolled < cumulative:
			break
	var candidates: Array = pools[chosen]
	var selected_index: int = _rng.randi_range(0, candidates.size() - 1)
	return {"plugin_id": str(candidates[selected_index]), "rarity": chosen, "empty": false}

func _is_high(card: Dictionary) -> bool:
	return str(card.get("rarity", "")) in ["legendary", "mythic"]

func _settle_offer() -> void:
	var has_candidates: bool = false
	for card in _pending["cards"]:
		if not bool(card.get("empty", true)):
			has_candidates = true
			break
	if has_candidates:
		if bool(_pending["high_seen"]):
			missed_screens = 0
		else:
			missed_screens += 1
	_pending.clear()
	_prepared.clear()
	_commit_uncertain = false
