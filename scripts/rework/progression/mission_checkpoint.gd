extends RefCounted
## E-owned, additive mission checkpoint contract. It lives in the existing
## mission.environment_rolls dictionary, so old v1 slots are never migrated.
## C/D supply authoritative snapshots; E validates/commits before displaying.
const KEY: String = "e_checkpoint_v1"
const VERSION: int = 1
const HARDWARE := ["CPU", "GPU", "RAM", "SSD", "HDD"]
const RARITIES := ["common", "rare", "legendary", "mythic"]
const MAX_SAFE: int = 9007199254740991

static func _whole(value: Variant, maximum: int = MAX_SAFE) -> bool:
    if typeof(value) != TYPE_INT and typeof(value) != TYPE_FLOAT:
        return false
    var num: float = float(value)
    return not is_nan(num) and not is_inf(num) and num >= 0.0 \
        and num <= float(maximum) and floor(num) == num

static func _id(value: Variant) -> bool:
    if typeof(value) != TYPE_STRING or value.is_empty() or value.length() > 100:
        return false
    for letter in value:
        if not "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_.:-".contains(letter):
            return false
    return true

static func _keys(value: Variant, keys: Array) -> bool:
    if typeof(value) != TYPE_DICTIONARY or value.size() != keys.size():
        return false
    for key in keys:
        if not value.has(key):
            return false
    return true

static func _same_context(a: Dictionary, b: Dictionary) -> bool:
    return int(a.cleared_hardware) == int(b.cleared_hardware) \
        and int(a.difficulty_tier) == int(b.difficulty_tier) \
        and float(a.luck_points) == float(b.luck_points)

static func _same_card(a: Dictionary, b: Dictionary) -> bool:
    return int(a.slot) == int(b.slot) and a.plugin_id == b.plugin_id \
        and a.rarity == b.rarity and bool(a.empty) == bool(b.empty)

static func _cards(value: Variant) -> bool:
    if typeof(value) != TYPE_ARRAY or value.size() != 3:
        return false
    for i in range(3):
        var card: Variant = value[i]
        if not _keys(card, ["slot", "plugin_id", "rarity", "empty"]) \
                or not _whole(card.slot, 2) or int(card.slot) != i \
                or typeof(card.empty) != TYPE_BOOL:
            return false
        if card.empty:
            if card.plugin_id != "" or card.rarity != "":
                return false
        elif not _id(card.plugin_id) or not RARITIES.has(card.rarity):
            return false
    return true

static func _offer(value: Variant) -> bool:
    if value == null:
        return true
    if not _keys(value, ["offer_id", "context", "cards", "high_seen"]):
        return false
    if not _id(value.offer_id) or typeof(value.high_seen) != TYPE_BOOL \
            or not _cards(value.cards):
        return false
    var ctx: Variant = value.context
    if not _keys(ctx, ["cleared_hardware", "difficulty_tier", "luck_points"]) \
            or not _whole(ctx.cleared_hardware, 5) \
            or not _whole(ctx.difficulty_tier, 100000):
        return false
    if typeof(ctx.luck_points) != TYPE_FLOAT and typeof(ctx.luck_points) != TYPE_INT:
        return false
    var luck: float = float(ctx.luck_points)
    if is_nan(luck) or is_inf(luck) or luck < 0.0 or luck > 100000.0:
        return false
    var high: bool = false
    for card in value.cards:
        high = high or str(card.rarity) in ["legendary", "mythic"]
    # C retains high_seen=true after a high-card reroll, even if replaced later.
    return not high or bool(value.high_seen)

static func _terminal(value: Variant) -> bool:
    if value == null:
        return true
    if not _keys(value, ["kind", "receipt_id", "learn_plugin_ids"]) \
            or not ["extract", "die"].has(value.kind) or not _id(value.receipt_id) \
            or typeof(value.learn_plugin_ids) != TYPE_ARRAY \
            or value.learn_plugin_ids.size() > 2:
        return false
    if value.kind == "die" and not value.learn_plugin_ids.is_empty():
        return false
    var seen: Dictionary = {}
    for plugin_id in value.learn_plugin_ids:
        if not _id(plugin_id) or seen.has(plugin_id):
            return false
        seen[plugin_id] = true
    return true

static func fresh(mission: Dictionary) -> Dictionary:
    return {
        "version": VERSION,
        "regions": {},
        "offer_sequence": 0,
        "active_offer": null,
        "rerolls_left": int(mission.rerolls_left),
        "pity_counter": int(mission.pity_counter),
        "rng_state_text": "",
        "terminal_pending": null,
    }

static func valid(mission: Dictionary) -> bool:
    if typeof(mission.get("environment_rolls")) != TYPE_DICTIONARY:
        return false
    if not mission.environment_rolls.has(KEY):
        return true # v1 slots stay valid; no write-time automatic migration.
    var cp: Variant = mission.environment_rolls[KEY]
    if not _keys(cp, ["version", "regions", "offer_sequence", "active_offer",
            "rerolls_left", "pity_counter", "rng_state_text", "terminal_pending"]):
        return false
    if cp.version != VERSION or not _whole(cp.offer_sequence, 1000000) \
            or not _whole(cp.rerolls_left, 1000000) \
            or not _whole(cp.pity_counter, 1000000) \
            or int(cp.rerolls_left) != int(mission.rerolls_left) \
            or int(cp.pity_counter) != int(mission.pity_counter) \
            or typeof(cp.rng_state_text) != TYPE_STRING \
            or cp.rng_state_text.length() > 21 \
            or not _offer(cp.active_offer) or not _terminal(cp.terminal_pending):
        return false
    if not cp.rng_state_text.is_empty():
        var raw: String = cp.rng_state_text
        if raw.begins_with("-"):
            raw = raw.substr(1)
        if raw.is_empty() or not raw.is_valid_int():
            return false
    if typeof(cp.regions) != TYPE_DICTIONARY or cp.regions.size() > 5:
        return false
    for region in cp.regions:
        var info: Variant = cp.regions[region]
        if not HARDWARE.has(region) \
                or not _keys(info, ["seed", "generator_id"]) \
                or not _whole(info.seed) or not _id(info.generator_id):
            return false
        if not mission.generated_map_seeds.has(region) \
                or not _whole(mission.generated_map_seeds[region]) \
                or int(mission.generated_map_seeds[region]) != int(info.seed):
            return false
    if cp.active_offer != null:
        if int(mission.pending_plugin_offers) < 1 \
                or int(cp.offer_sequence) < 1 \
                or cp.active_offer.offer_id != str(mission.run_id) + ":" + str(int(cp.offer_sequence)) \
                or mission.claimed_offer_ids.has(cp.active_offer.offer_id) \
                or cp.terminal_pending != null:
            return false
    return true

static func monotonic(before: Dictionary, after: Dictionary) -> bool:
    ## Store boundary: even direct callers cannot erase the checkpoint,
    ## reroll multiple cards or silently roll back a staged terminal intent.
    var old_has: bool = before.environment_rolls.has(KEY)
    var new_has: bool = after.environment_rolls.has(KEY)
    if not old_has:
        return true
    if not new_has or not valid(after):
        return false
    var old: Dictionary = before.environment_rolls[KEY]
    var now: Dictionary = after.environment_rolls[KEY]
    if int(now.offer_sequence) < int(old.offer_sequence) \
            or int(now.offer_sequence) > int(old.offer_sequence) + 1:
        return false
    for region in old.regions:
        if not now.regions.has(region) or now.regions[region] != old.regions[region]:
            return false
    if old.terminal_pending != null and now.terminal_pending != old.terminal_pending:
        return false
    if old.active_offer != null:
        if now.active_offer == null:
            if not after.claimed_offer_ids.has(old.active_offer.offer_id):
                return false
        else:
            if old.active_offer.offer_id != now.active_offer.offer_id \
                    or not _same_context(old.active_offer.context, now.active_offer.context) \
                    or int(now.rerolls_left) > int(old.rerolls_left) \
                    or int(now.rerolls_left) < int(old.rerolls_left) - 1 \
                    or (old.active_offer.high_seen and not now.active_offer.high_seen):
                return false
            var changed: int = 0
            for i in range(3):
                if not _same_card(old.active_offer.cards[i], now.active_offer.cards[i]):
                    changed += 1
            if changed > 1:
                return false
            if changed > 0 and int(now.rerolls_left) != int(old.rerolls_left) - 1:
                return false
    elif now.active_offer != null:
        if int(now.offer_sequence) != int(old.offer_sequence) + 1:
            return false
    return true

static func view(mission: Dictionary) -> Dictionary:
    if not valid(mission):
        return {"ok": false, "reason": "invalid_mission_checkpoint"}
    var value: Dictionary = fresh(mission)
    if mission.environment_rolls.has(KEY):
        value = mission.environment_rolls[KEY].duplicate(true)
    return {"ok": true, "checkpoint_present": mission.environment_rolls.has(KEY),
        "checkpoint": value, "has_active_offer": value.active_offer != null,
        "has_rng_cursor": not value.rng_state_text.is_empty(),
        "regions_saved": value.regions.size()}

static func record_region(mission: Dictionary, region: String, seed: int,
        generator_id: String) -> Dictionary:
    if not HARDWARE.has(region) or seed < 0 or seed > MAX_SAFE \
            or not _id(generator_id) or not valid(mission):
        return {"ok": false, "reason": "invalid_region_checkpoint"}
    var cp: Dictionary = view(mission).checkpoint
    if cp.terminal_pending != null:
        return {"ok": false, "reason": "terminal_pending"}
    if cp.regions.has(region):
        return {"ok": false, "reason": "region_seed_already_committed"}
    if mission.generated_map_seeds.has(region) \
            and (not _whole(mission.generated_map_seeds[region]) \
                or int(mission.generated_map_seeds[region]) != seed):
        return {"ok": false, "reason": "map_seed_conflict"}
    mission.generated_map_seeds[region] = seed
    cp.regions[region] = {"seed": seed, "generator_id": generator_id}
    mission.environment_rolls[KEY] = cp
    return {"ok": valid(mission), "event": "region_checkpointed"}

static func _normalized_offer(snapshot: Dictionary) -> Dictionary:
    var cards: Array = []
    for i in range(3):
        var card: Dictionary = snapshot.cards[i]
        cards.append({"slot": i, "plugin_id": str(card.plugin_id),
            "rarity": str(card.rarity), "empty": bool(card.empty)})
    return {
        "offer_id": str(snapshot.offer_id),
        "context": snapshot.context.duplicate(true),
        "cards": cards, "high_seen": bool(snapshot.high_seen)
    }

static func record_offer(mission: Dictionary, snapshot: Dictionary,
        rng_state_text: String) -> Dictionary:
    if not valid(mission) or not snapshot.get("active", false) \
            or snapshot.get("reconciliation_required", true) \
            or not _keys(snapshot.get("prepared"), []) \
            or not _whole(snapshot.get("rerolls_remaining"), 1000000) \
            or not _whole(snapshot.get("missed_screens"), 1000000) \
            or not _keys(snapshot.get("context"), [
                "cleared_hardware", "difficulty_tier", "luck_points"]) \
            or typeof(snapshot.get("offer_id")) != TYPE_STRING \
            or typeof(snapshot.get("high_seen")) != TYPE_BOOL \
            or not _cards(snapshot.get("cards")):
        return {"ok": false, "reason": "invalid_offer_snapshot"}
    var candidate: Dictionary = _normalized_offer(snapshot)
    if not _offer(candidate):
        return {"ok": false, "reason": "invalid_offer_cards"}
    var cp: Dictionary = view(mission).checkpoint
    if cp.terminal_pending != null:
        return {"ok": false, "reason": "terminal_pending"}
    var current: Variant = cp.active_offer
    var next_rerolls: int = int(snapshot.rerolls_remaining)
    if current == null:
        if int(mission.pending_plugin_offers) < 1 \
                or mission.claimed_offer_ids.has(candidate.offer_id) \
                or next_rerolls != int(mission.rerolls_left) \
                or int(snapshot.missed_screens) != int(mission.pity_counter) \
                or candidate.offer_id != str(mission.run_id) + ":" + str(int(cp.offer_sequence) + 1):
            return {"ok": false, "reason": "invalid_new_offer_transition"}
        var initial_high: bool = false
        for card in candidate.cards:
            initial_high = initial_high or str(card.rarity) in ["legendary", "mythic"]
        if initial_high != bool(candidate.high_seen):
            return {"ok": false, "reason": "initial_pity_screen_mismatch"}
        cp.offer_sequence += 1
    else:
        # One-card reroll: same offer/context, exactly one slot may change,
        # reroll counter must go down by exactly one, no pity manipulation.
        if current.offer_id != candidate.offer_id \
                or not _same_context(current.context, candidate.context) \
                or int(mission.rerolls_left) < 1 \
                or next_rerolls != int(mission.rerolls_left) - 1 \
                or int(snapshot.missed_screens) != int(mission.pity_counter):
            return {"ok": false, "reason": "invalid_reroll_transition"}
        var changed: int = 0
        for i in range(3):
            if not _same_card(current.cards[i], candidate.cards[i]):
                changed += 1
        if changed > 1 or (current.high_seen and not candidate.high_seen):
            return {"ok": false, "reason": "reroll_mutated_other_cards"}
        if not current.high_seen and candidate.high_seen:
            var new_high: bool = false
            for card in candidate.cards:
                new_high = new_high or str(card.rarity) in ["legendary", "mythic"]
            if not new_high:
                return {"ok": false, "reason": "forged_high_rarity_pity"}
    cp.active_offer = candidate
    cp.rerolls_left = next_rerolls
    cp.rng_state_text = rng_state_text
    mission.rerolls_left = next_rerolls
    mission.environment_rolls[KEY] = cp
    if not valid(mission):
        return {"ok": false, "reason": "checkpoint_failed_validation"}
    return {"ok": true, "event": "offer_checkpointed"}

static func settle_choice(mission: Dictionary, offer_id: String,
        receipt_id: String, plugin_id: String, rarity: String) -> Dictionary:
    if not valid(mission) or not mission.environment_rolls.has(KEY):
        return {"ok": true}
    var cp: Dictionary = mission.environment_rolls[KEY]
    if cp.active_offer == null:
        return {"ok": false, "reason": "offer_not_checkpointed"}
    var offer: Dictionary = cp.active_offer
    if offer.offer_id != offer_id:
        return {"ok": false, "reason": "checkpoint_offer_mismatch"}
    var matched: bool = false
    for card in offer.cards:
        if receipt_id == str(offer_id) + ":" + str(int(card.slot)) \
                and not bool(card.empty) and card.plugin_id == plugin_id \
                and card.rarity == rarity:
            matched = true
    if not matched:
        return {"ok": false, "reason": "card_differs_from_saved_offer"}
    cp.active_offer = null
    cp.pity_counter = 0 if bool(offer.high_seen) else int(cp.pity_counter) + 1
    mission.pity_counter = cp.pity_counter
    mission.environment_rolls[KEY] = cp
    return {"ok": valid(mission), "event": "offer_settled"}

static func prepare_terminal(mission: Dictionary, kind: String,
        terminal_receipt_id: String, learn_ids: Array) -> Dictionary:
    if not valid(mission):
        return {"ok": false, "reason": "invalid_mission_checkpoint"}
    var cp: Dictionary = view(mission).checkpoint
    if cp.terminal_pending != null or cp.active_offer != null:
        return {"ok": false, "reason": "terminal_or_offer_pending"}
    if not _terminal({"kind": kind, "receipt_id": terminal_receipt_id,
            "learn_plugin_ids": learn_ids}):
        return {"ok": false, "reason": "invalid_terminal_intent"}
    cp.terminal_pending = {"kind": kind, "receipt_id": terminal_receipt_id,
        "learn_plugin_ids": learn_ids.duplicate(true)}
    mission.environment_rolls[KEY] = cp
    return {"ok": valid(mission), "event": "terminal_prepared"}

static func terminal_matches(mission: Dictionary, kind: String,
        receipt_id: String, learn_ids: Array) -> bool:
    if not mission.environment_rolls.has(KEY):
        return true # Pre-checkpoint v1 integration remains compatible.
    var cp: Dictionary = mission.environment_rolls[KEY]
    if cp.terminal_pending == null:
        return true # A's existing bridge does not prepare terminal intents yet.
    var terminal: Dictionary = cp.terminal_pending
    return terminal.kind == kind and terminal.receipt_id == receipt_id \
        and terminal.learn_plugin_ids == learn_ids
