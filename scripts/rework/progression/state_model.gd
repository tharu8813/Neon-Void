extends RefCounted
## Versioned, self-contained in-memory state. No imports from legacy save scripts.
const Balance = preload("res://scripts/rework/progression/balance_config.gd")
const VERSION: int = 1
const MAX_EXACT_INT: int = 9007199254740991
const HARDWARE: Array[String] = ["CPU", "GPU", "RAM", "SSD", "HDD"]
const RARITIES: Array[String] = ["common", "rare", "legendary", "mythic"]

static func valid_id(value: Variant) -> bool:
    if typeof(value) != TYPE_STRING or value.is_empty() or value.length() > 100:
        return false
    var allowed := "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-."
    for i in value.length():
        if not allowed.contains(value.substr(i, 1)):
            return false
    return true

static func natural(value: Variant, limit: int = MAX_EXACT_INT) -> bool:
    if typeof(value) != TYPE_INT and typeof(value) != TYPE_FLOAT:
        return false
    var numeric := float(value)
    if is_nan(numeric) or is_inf(numeric):
        return false
    return numeric >= 0.0 and numeric <= float(limit) and floor(numeric) == numeric

static func _exact_keys(value: Dictionary, keys: Array) -> bool:
    if value.size() != keys.size():
        return false
    for key in keys:
        if not value.has(key):
            return false
    return true

static func _valid_ids(values: Variant, max_size: int = 100000) -> bool:
    if typeof(values) != TYPE_ARRAY or values.size() > max_size:
        return false
    var seen := {}
    for item in values:
        if not valid_id(item) or seen.has(item):
            return false
        seen[item] = true
    return true

static func _valid_plugins(plugins: Variant, allow_temporary: bool) -> bool:
    if typeof(plugins) != TYPE_DICTIONARY:
        return false
    for plugin_id in plugins:
        if not valid_id(plugin_id):
            return false
        var info: Variant = plugins[plugin_id]
        if typeof(info) != TYPE_DICTIONARY or info.size() != 2                 or not info.has("rarity") or not info.has("level"):
            return false
        if not RARITIES.has(info.rarity) or not natural(info.level, 1000000)                 or int(info.level) < 1:
            return false
        if not allow_temporary and not ["common", "rare"].has(info.rarity):
            return false
    return true

static func new_vaccine(vaccine_id: String) -> Dictionary:
    return {
        "vaccine_id": vaccine_id, "alive": true, "copies_remaining": 3,
        "level": 1, "xp": 0, "total_xp": 0,
        "earned_fragments": 0, "unspent_fragments": 0,
        "invested_stats": {}, "known_skills": [], "equipped_skills": [],
        "archive": {}, "carried": {}, "mission": null,
        "receipts": {}, "closed_runs": {}
    }

static func new_slot(slot_id: String, vaccine_id: String, config: Dictionary = {}) -> Dictionary:
    if not valid_id(slot_id) or not valid_id(vaccine_id) or not Balance.valid_config(config):
        return {}
    return {
        "schema_version": VERSION, "slot_id": slot_id, "generation": 1,
        "selection_required": false, "balance": config.duplicate(true),
        "vaccine": new_vaccine(vaccine_id),
    }

static func new_mission(run_id: String, seed: int, config: Dictionary = {}) -> Dictionary:
    return {
        "run_id": run_id, "seed": seed, "mission_level": 1, "mission_xp": 0,
        "pending_plugin_offers": 0,
        "uncommitted_vaccine_xp": 0, "uncommitted_point_fragments": 0,
        "hardware_cleaned_ids": [], "objective_ids": [],
        "obtained_plugins": {}, "generated_map_seeds": {},
        "mission_difficulty": {}, "environment_rolls": {},
        "rerolls_left": Balance.get_number("mission_start_rerolls", config),
        "pity_counter": 0
    }

static func _valid_mission(mission: Variant) -> bool:
    if typeof(mission) != TYPE_DICTIONARY or mission.size() != 15             or not valid_id(mission.get("run_id")) or not natural(mission.get("seed")):
        return false
    for key in ["mission_level", "mission_xp", "pending_plugin_offers",
                "uncommitted_vaccine_xp", "uncommitted_point_fragments",
                "rerolls_left", "pity_counter"]:
        if not natural(mission.get(key), 1000000000000):
            return false
    if int(mission.mission_level) < 1             or not _valid_ids(mission.get("hardware_cleaned_ids"), 5)             or not _valid_ids(mission.get("objective_ids"))             or not _valid_plugins(mission.get("obtained_plugins"), true):
        return false
    for region in mission.hardware_cleaned_ids:
        if not HARDWARE.has(region):
            return false
    for key in ["generated_map_seeds", "mission_difficulty", "environment_rolls"]:
        if typeof(mission.get(key)) != TYPE_DICTIONARY:
            return false
    return true

static func _valid_vaccine(vaccine: Variant, config: Dictionary) -> bool:
    if typeof(vaccine) != TYPE_DICTIONARY:
        return false
    if not _exact_keys(vaccine, ["vaccine_id", "alive", "copies_remaining", "level",
            "xp", "total_xp", "earned_fragments", "unspent_fragments",
            "invested_stats", "known_skills", "equipped_skills",
            "archive", "carried", "mission", "receipts", "closed_runs"]):
        return false
    if not valid_id(vaccine.get("vaccine_id")) or vaccine.get("alive") != true             or not natural(vaccine.get("copies_remaining"), 3)             or not natural(vaccine.get("level"), 1000000) or vaccine.level < 1             or not natural(vaccine.get("xp")) or not natural(vaccine.get("total_xp"))             or not natural(vaccine.get("earned_fragments"))             or not natural(vaccine.get("unspent_fragments")):
        return false
    if typeof(vaccine.get("invested_stats")) != TYPE_DICTIONARY             or Balance.stat_cost_fragments(vaccine.invested_stats, config) < 0:
        return false
    if int(vaccine.unspent_fragments) +             Balance.stat_cost_fragments(vaccine.invested_stats, config) != int(vaccine.earned_fragments):
        return false
    if not _valid_ids(vaccine.get("known_skills"))             or not _valid_ids(vaccine.get("equipped_skills"), 3):
        return false
    for skill in vaccine.equipped_skills:
        if not vaccine.known_skills.has(skill):
            return false
    if not _valid_plugins(vaccine.get("archive"), false)             or not _valid_plugins(vaccine.get("carried"), false):
        return false
    for plugin_id in vaccine.archive:
        if vaccine.carried.has(plugin_id):
            return false
    if typeof(vaccine.get("receipts")) != TYPE_DICTIONARY             or typeof(vaccine.get("closed_runs")) != TYPE_DICTIONARY:
        return false
    for receipt in vaccine.receipts:
        if not valid_id(receipt) or vaccine.receipts[receipt] != true:
            return false
    for run_id in vaccine.closed_runs:
        if not valid_id(run_id) or not ["dead", "extracted"].has(vaccine.closed_runs[run_id]):
            return false
    if vaccine.get("mission") != null:
        if not _valid_mission(vaccine.mission) or vaccine.closed_runs.has(vaccine.mission.run_id):
            return false
    return true

static func validate(state: Variant) -> bool:
    if typeof(state) != TYPE_DICTIONARY or state.size() != 6             or state.get("schema_version") != VERSION             or not valid_id(state.get("slot_id"))             or not natural(state.get("generation"), 1000000000)             or state.generation < 1 or typeof(state.get("selection_required")) != TYPE_BOOL             or typeof(state.get("balance")) != TYPE_DICTIONARY             or not Balance.valid_config(state.balance):
        return false
    if state.vaccine == null:
        return state.selection_required
    return not state.selection_required and _valid_vaccine(state.vaccine, state.balance)
