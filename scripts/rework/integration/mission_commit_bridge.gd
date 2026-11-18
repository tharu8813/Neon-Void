extends RefCounted
## A-owned integration boundary: world and plugin events must be persisted by E
## before UI/VFX acknowledges success. This does not yet wire a playable scene.
##
## C plugin offers now prepare before durable commit, then confirm only on E ACK.
## D world facts are durable before presentation. On rejection, freeze this bridge
## and require explicit reconciliation. Never display or pay a rejected reward.
signal transaction_committed(result: Dictionary)
signal reconciliation_required(reason: String, event: Dictionary)

const Gateway = preload("res://scripts/rework/persistence/progression_gateway.gd")

var slot_path: String = ""
var run_id: String = ""
var vaccine_id: String = ""
var generation: int = 1
var frozen: bool = false
var failure_reason: String = ""

func _init(new_slot_path: String = "", new_run_id: String = "",
        new_vaccine_id: String = "", new_generation: int = 1) -> void:
    slot_path = new_slot_path
    run_id = new_run_id
    vaccine_id = new_vaccine_id
    generation = new_generation

func _fail(reason: String, event: Dictionary = {}) -> Dictionary:
    frozen = true
    failure_reason = reason
    reconciliation_required.emit(reason, event.duplicate(true))
    return {"ok": false, "reason": reason, "reconciliation_required": true}

func _identity_valid() -> bool:
    return not slot_path.is_empty() and not run_id.is_empty() \
        and not vaccine_id.is_empty() and generation >= 1

func _commit(action: String, receipt_id: String, extra: Dictionary = {}) -> Dictionary:
    if frozen:
        return {"ok": false, "reason": "reconciliation_required"}
    if not _identity_valid() or action.is_empty():
        return _fail("invalid_bridge_identity")
    var command: Dictionary = {
        "action": action, "vaccine_id": vaccine_id, "generation": generation,
        "run_id": run_id
    }
    if not receipt_id.is_empty():
        command["receipt_id"] = receipt_id
    for key in extra:
        if command.has(key):
            return _fail("reserved_command_field:" + str(key))
        command[key] = extra[key]
    var result: Dictionary = Gateway.transact_command(slot_path, command)
    if not result.get("applied", false):
        return _fail("persistence_" + str(result.get("reason", "unknown")), command)
    var ack: Dictionary = {
        "ok": true, "action": action, "receipt_id": receipt_id,
        "revision": result.get("revision", -1)
    }
    transaction_committed.emit(ack.duplicate(true))
    return ack

## E #118 optional checkpoint. This is a separate durable write BEFORE
## A presents or re-rolls C cards. It is never a substitute for plugin_picked.
## Persist the D region's exact procedural generator + map seed before
## entering its geometry. Never record a map only in a transient Godot scene.
func checkpoint_hardware_entry(fact: Dictionary) -> Dictionary:
    if frozen:
        return {"ok": false, "reason": "reconciliation_required"}
    if int(fact.get("schema", -1)) != 1 \
            or str(fact.get("type", "")) != "hardware_checkpoint_requested" \
            or str(fact.get("run_id", "")) != run_id:
        return _fail("invalid_map_checkpoint_run_or_schema", fact)
    var region: String = str(fact.get("region_id", ""))
    var receipt: String = str(fact.get("receipt_id", ""))
    var generator: String = str(fact.get("generator_id", ""))
    var seed: int = int(fact.get("map_seed", -1))
    var plan: Variant = fact.get("region_plan")
    # E #118 supports all five GDD hardware IDs; D only enters a region
    # when its owner-created scene is available. This bridge cannot invent one.
    if not ["CPU", "GPU", "RAM", "SSD", "HDD"].has(region) \
            or receipt != run_id + ":" + region + ":map-checkpoint" \
            or generator.is_empty() or seed <= 0 \
            or seed > 9007199254740991 or typeof(plan) != TYPE_DICTIONARY \
            or str(plan.get("id", "")) != region \
            or int(plan.get("map_seed", -1)) != seed:
        return _fail("invalid_map_checkpoint_identity", fact)
    return _commit("checkpoint_region", receipt, {
        "region_id": region, "map_seed": seed, "generator_id": generator
    })

func checkpoint_offer(offer: Dictionary, rng_state_text: String) -> Dictionary:
    if frozen:
        return {"ok": false, "reason": "reconciliation_required"}
    var offer_id: String = str(offer.get("offer_id", ""))
    if offer_id.is_empty() or not bool(offer.get("active", false)):
        return {"ok": false, "reason": "invalid_offer_checkpoint"}
    if rng_state_text.is_empty() or not rng_state_text.is_valid_int():
        return _fail("missing_lossless_offer_rng_checkpoint", offer)
    var used_rerolls: int = 3 - int(offer.get("rerolls_remaining", -1))
    if used_rerolls < 0:
        return _fail("invalid_offer_reroll_count", offer)
    # Stable unique receipt for each in-offer C state transition.
    var receipt_id: String = offer_id + ":checkpoint:" + str(used_rerolls)
    return _commit("checkpoint_offer", receipt_id, {
        "offer_snapshot": offer.duplicate(true),
        "rng_state_text": rng_state_text
    })

func begin_mission(seed: int) -> Dictionary:
    if seed < 0:
        return {"ok": false, "reason": "invalid_seed"}
    return _commit("start_mission", "", {"seed": seed})

func record_mission_xp(receipt_id: String, amount: int) -> Dictionary:
    if receipt_id.is_empty() or amount <= 0:
        return {"ok": false, "reason": "invalid_mission_xp"}
    return _commit("mission_xp", receipt_id, {"amount": amount})

func handle_world_fact(fact: Dictionary) -> Dictionary:
    if frozen:
        return {"ok": false, "reason": "reconciliation_required"}
    if str(fact.get("run_id", "")) != run_id or int(fact.get("schema", -1)) != 1:
        return _fail("wrong_world_fact_run_or_schema", fact)
    var kind: String = str(fact.get("type", ""))
    var receipt_id: String = str(fact.get("receipt_id", ""))
    var region: String = str(fact.get("region_id", ""))
    if kind == "boss_defeated":
        # D emits hardware_cleansed as the one clean-reward fact after a boss.
        return {"ok": true, "ignored": true}
    if receipt_id.is_empty() or region.is_empty():
        return _fail("missing_world_fact_identity", fact)
    if kind == "objective_completed":
        var objective_id: String = str(fact.get("objective_id", ""))
        if objective_id.is_empty():
            return _fail("missing_objective_id", fact)
        return _commit("objective_completed", receipt_id,
            {"objective_id": region + "." + objective_id})
    if kind == "hardware_cleansed":
        return _commit("hardware_cleaned", receipt_id, {"hardware_id": region})
    return _fail("unknown_world_fact:" + kind, fact)

func commit_plugin_choice(choice: Dictionary, offer_source: String = "levelup") -> Dictionary:
    if frozen:
        return {"ok": false, "reason": "reconciliation_required"}
    for key in ["receipt_id", "offer_id", "plugin_id", "rarity", "level"]:
        if not choice.has(key):
            return _fail("invalid_plugin_choice:" + key, choice)
    if not ["levelup", "hardware", "event"].has(offer_source):
        return _fail("invalid_offer_source", choice)
    return _commit("plugin_picked", str(choice["receipt_id"]), {
        "offer_id": str(choice["offer_id"]),
        "plugin_id": str(choice["plugin_id"]),
        "rarity": str(choice["rarity"]),
        "level": int(choice["level"]),
        "offer_source": offer_source
    })

## The safe A entry point for C's new prepare -> E durable -> confirm protocol.
## Raw commit_plugin_choice() remains a low-level persistence primitive only.
func choose_and_commit_plugin(offers: RefCounted, slot: int,
        offer_source: String = "levelup") -> Dictionary:
    if frozen:
        return {"ok": false, "reason": "reconciliation_required"}
    if offers == null or not offers.has_method("prepare_card") or not offers.has_method("confirm_card"):
        return {"ok": false, "reason": "invalid_offer_engine"}
    var prepared: Dictionary = offers.prepare_card(slot)
    if prepared.is_empty():
        return {"ok": false, "reason": "no_selectable_card"}
    var receipt: String = str(prepared.get("receipt_id", ""))
    var persisted: Dictionary = commit_plugin_choice(prepared, offer_source)
    if not bool(persisted.get("ok", false)):
        # Even an error can mean E committed and the ACK was lost; never cancel
        # or generate a different receipt until a verified E snapshot reconciles.
        offers.confirm_card(receipt, {"applied": false, "receipt_id": receipt})
        return persisted
    var confirmed: Dictionary = offers.confirm_card(receipt, {
        "applied": true, "receipt_id": receipt,
        "revision": persisted.get("revision", -1)
    })
    if not bool(confirmed.get("ok", false)):
        return _fail("plugin_E_C_confirmation_diverged", prepared)
    return {"ok": true, "receipt_id": receipt,
        "revision": persisted.get("revision", -1), "plugin": confirmed}

func commit_terminal_fact(fact: Dictionary, learn_plugin_ids: Array = []) -> Dictionary:
    if frozen:
        return {"ok": false, "reason": "reconciliation_required"}
    if str(fact.get("run_id", "")) != run_id or int(fact.get("schema", -1)) != 1:
        return _fail("wrong_terminal_fact_run_or_schema", fact)
    var receipt_id: String = str(fact.get("receipt_id", ""))
    if receipt_id.is_empty():
        return _fail("missing_terminal_receipt", fact)
    match str(fact.get("type", "")):
        "mission_extracted":
            if str(fact.get("outcome", "")) != "success" or int(fact.get("cleared_count", 0)) < 1:
                return _fail("invalid_extraction_fact", fact)
            return _commit("extract", receipt_id,
                {"learn_plugin_ids": learn_plugin_ids.duplicate(true)})
        "mission_failed":
            if str(fact.get("outcome", "")) != "death":
                return _fail("invalid_death_fact", fact)
            return _commit("die", receipt_id)
        _:
            return _fail("unknown_terminal_fact", fact)

func snapshot() -> Dictionary:
    return Gateway.snapshot(slot_path)
