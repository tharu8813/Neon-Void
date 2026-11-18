extends RefCounted
## E-owned provisional event bonus for nv_c_random_rare.
## The source choice AND one rare bonus are granted by ONE Store transaction.
## It does not resolve the unapproved extra Lv2 reward quantity or UI effects.
## No legacy save formats or top-level mission keys are changed.

const Catalog = preload("res://scripts/rework/plugins/plugin_catalog.gd")
const KEY: String = "e_rare_bonus_v1"
const SOURCE_ID: String = "nv_c_random_rare"
const RNG_POLICY: String = "rare-event-v1"
const MAX_SAFE: int = 9007199254740991
# GDD-proposed settings. These values do not authorize an unapproved Lv2 multiplier.
const DRAFT_POLICY: Dictionary = {
    "baseline_grants": 1,
    "lv2_extra_grants": 0,
    "exhausted_candidates": "reject_without_spending_receipt"
}

static func policy_draft() -> Dictionary:
    return DRAFT_POLICY.duplicate(true)

static func verify_committed_pair(mission: Dictionary,
        receipts: Dictionary, source_receipt: String) -> Dictionary:
    ## Verify source + derived receipts, authoritative level, and persisted policy.
    ## Read-only: an absent ACK must never generate another award.
    if not _id(source_receipt) or not valid(mission):
        return {"ok": false, "reason": "invalid_rare_grant_state"}
    var history: Dictionary = mission.environment_rolls.get(KEY, {})
    if not history.has(source_receipt):
        return {"ok": false, "reason": "rare_grant_record_missing"}
    var record: Dictionary = history[source_receipt]
    var source: Variant = mission.obtained_plugins.get(SOURCE_ID, {})
    if not source is Dictionary or source.get("rarity") != "common" \
            or int(source.get("level", 0)) < 1:
        return {"ok": false, "reason": "rare_source_not_owned"}
    if not receipts.has(source_receipt) or receipts[source_receipt] != true \
            or not receipts.has(str(record.bonus_receipt)) \
            or receipts[str(record.bonus_receipt)] != true:
        return {"ok": false, "reason": "rare_pair_receipt_incomplete"}
    return {"ok": true, "source_receipt": source_receipt,
        "bonus_receipt": str(record.bonus_receipt),
        "plugin_id": str(record.plugin_id), "level": int(record.level),
        "rng_policy": str(record.rng_policy)}

static func _id(value: Variant) -> bool:
    if typeof(value) != TYPE_STRING or value.is_empty() or value.length() > 100:
        return false
    for letter in value:
        if not "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_.:-".contains(letter):
            return false
    return true

static func valid(mission: Dictionary) -> bool:
    var rolls: Variant = mission.get("environment_rolls")
    if typeof(rolls) != TYPE_DICTIONARY:
        return false
    if not rolls.has(KEY):
        return true # Old v1 missions are not mutated or migrated by read().
    var history: Variant = rolls[KEY]
    if typeof(history) != TYPE_DICTIONARY or history.size() > 100000:
        return false
    for source_receipt in history:
        var entry: Variant = history[source_receipt]
        if not _id(source_receipt) or not entry is Dictionary or entry.size() != 5:
            return false
        for field in ["bonus_receipt", "plugin_id", "rarity", "level", "rng_policy"]:
            if not entry.has(field):
                return false
        if not _id(entry.bonus_receipt) or not _id(entry.plugin_id) \
                or entry.bonus_receipt != str(source_receipt) + ":rare:0" \
                or entry.rarity != "rare" or entry.rng_policy != RNG_POLICY \
                or typeof(entry.level) != TYPE_INT and typeof(entry.level) != TYPE_FLOAT:
            return false
        var lvl: float = float(entry.level)
        if is_nan(lvl) or is_inf(lvl) or lvl < 1.0 \
                or lvl > 1000000.0 or floor(lvl) != lvl:
            return false
        var obtained: Variant = mission.get("obtained_plugins", {}).get(
            str(entry.plugin_id), {})
        if not obtained is Dictionary or obtained.is_empty() \
                or obtained.get("rarity") != "rare" \
                or int(obtained.get("level", 0)) < int(entry.level):
            return false
    return true

static func monotonic(before: Dictionary, after: Dictionary) -> bool:
    var old: Variant = before.get("environment_rolls", {}).get(KEY, {})
    var now: Variant = after.get("environment_rolls", {}).get(KEY, {})
    if not old is Dictionary or not now is Dictionary:
        return false
    for source_receipt in old:
        if not now.has(source_receipt) or now[source_receipt] != old[source_receipt]:
            return false
    return true

static func plan(mission: Dictionary, carried: Dictionary,
        source_receipt: String, source_level: int) -> Dictionary:
    ## Draft policy is visible and explicit; do not enable unapproved Lv2 counts.
    if DRAFT_POLICY.baseline_grants != 1 or DRAFT_POLICY.lv2_extra_grants != 0 \
            or DRAFT_POLICY.exhausted_candidates != "reject_without_spending_receipt":
        return {"ok": false, "reason": "rare_reward_policy_not_approved"}
    var bonus_receipt: String = source_receipt + ":rare:0"
    if not _id(source_receipt) or not _id(bonus_receipt) \
            or source_level < 1 or source_level > 2 \
            or not valid(mission):
        return {"ok": false, "reason": "invalid_rare_source"}
    var previous: Dictionary = mission.get("environment_rolls", {}).get(KEY, {})
    if previous.has(source_receipt):
        return {"ok": false, "reason": "rare_bonus_already_awarded"}
    var available: Array[Dictionary] = []
    var entries: Array[Dictionary] = Catalog.entries()
    if not Catalog.validate(entries).is_empty():
        return {"ok": false, "reason": "invalid_authoritative_rare_catalog"}
    for entry in entries:
        if str(entry.get("rarity", "")) != "rare":
            continue
        var plugin_id: String = str(entry.get("plugin_id", ""))
        var held: Variant = mission.get("obtained_plugins", {}).get(
            plugin_id, carried.get(plugin_id, {}))
        var current_level: int = 0
        if held is Dictionary and not held.is_empty():
            if held.get("rarity") != "rare":
                return {"ok": false, "reason": "rare_catalog_rarity_conflict"}
            current_level = int(held.get("level", 0))
        if current_level < int(entry.get("max_level", 0)):
            available.append({
                "plugin_id": plugin_id, "level": current_level + 1
            })
    if available.is_empty():
        return {"ok": false, "reason": "no_eligible_rare_candidate"}
    available.sort_custom(func(a: Dictionary, b: Dictionary) -> bool:
        return str(a.plugin_id) < str(b.plugin_id))
    # Cross-run deterministic, bounded integer hash. The chosen result is
    # persisted so future Godot/RNG changes cannot silently reroll it.
    var cursor: int = posmod(int(mission.get("seed", 0)), 2147483646) + 1
    var entropy: String = str(mission.get("run_id", "")) + "|" + \
        source_receipt + "|" + str(source_level) + "|" + RNG_POLICY
    for octet in entropy.to_utf8_buffer():
        cursor = (cursor * 131 + int(octet)) % 2147483647
    var chosen: Dictionary = available[int(cursor % available.size())]
    return {"ok": true, "source_receipt": source_receipt,
        "record": {
            "bonus_receipt": bonus_receipt,
            "plugin_id": str(chosen.plugin_id),
            "rarity": "rare",
            "level": int(chosen.level),
            "rng_policy": RNG_POLICY
        }
    }

static func commit(mission: Dictionary, proposal: Dictionary) -> Dictionary:
    if not bool(proposal.get("ok", false)):
        return {"ok": false, "reason": "rare_bonus_not_prepared"}
    var source_receipt: String = str(proposal.get("source_receipt", ""))
    var record: Dictionary = proposal.get("record", {})
    if not _id(source_receipt) or record.is_empty() or not valid(mission):
        return {"ok": false, "reason": "invalid_rare_grant_commit"}
    var history: Dictionary = mission.environment_rolls.get(KEY, {})
    if history.has(source_receipt):
        return {"ok": false, "reason": "duplicate_rare_source"}
    var plugin_id: String = str(record.get("plugin_id", ""))
    if not _id(plugin_id) or str(record.get("rarity", "")) != "rare":
        return {"ok": false, "reason": "invalid_rare_grant_id"}
    # E has already validated/accepted the source choice in the same
    # transactional copy. This bonus must be visible only with that source.
    mission.obtained_plugins[plugin_id] = {
        "rarity": "rare", "level": int(record.get("level", 0))
    }
    history[source_receipt] = record.duplicate(true)
    mission.environment_rolls[KEY] = history
    if not valid(mission):
        return {"ok": false, "reason": "invalid_rare_grant_state"}
    return {"ok": true, "plugin_id": plugin_id,
        "level": int(record.level), "bonus_receipt": str(record.bonus_receipt)}
