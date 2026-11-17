extends RefCounted
## B stat mapping from E's 22 STAT_COSTS IDs to the actual combat stat keys.
## Increments reproduce GDD §13.1 BALANCE_DRAFT v0.1, NOT final balance.
## Values are additive per purchased allocation; E owns their point costs.
const Stats = preload("res://scripts/rework/combat/stats.gd")
const MAX_ALLOCATIONS: int = 1000000
const INCREMENTS: Dictionary = {
    "max_hp": {"stat": "max_health", "amount": 5.0},
    "attack_damage": {"stat": "attack_damage", "amount": 0.25},
    "ability_power": {"stat": "ability_power", "amount": 1.0},
    "armor": {"stat": "armor", "amount": 2.0},
    "magic_resist": {"stat": "magic_resist", "amount": 2.0},
    "move_speed": {"stat": "move_speed", "amount": 0.05},
    "fire_rate": {"stat": "attack_speed", "amount": 0.05},
    "projectile_speed": {"stat": "projectile_speed_mult", "amount": 0.02},
    "crit_chance": {"stat": "crit_chance", "amount": 0.005},
    "crit_damage": {"stat": "crit_multiplier", "amount": 0.01},
    "ability_haste": {"stat": "skill_haste", "amount": 2.0},
    "luck": {"stat": "luck", "amount": 0.005},
    "max_mana": {"stat": "max_mana", "amount": 5.0},
    "mana_regen": {"stat": "mana_regen", "amount": 0.25},
    "projectile_size": {"stat": "projectile_size", "amount": 1.0},
    "flat_armor_pen": {"stat": "physical_pen_flat", "amount": 0.5},
    "flat_magic_pen": {"stat": "magic_pen_flat", "amount": 0.5},
    "percent_armor_pen": {"stat": "physical_pen_percent", "amount": 0.0025},
    "percent_magic_pen": {"stat": "magic_pen_percent", "amount": 0.0025},
    "on_hit_vamp": {"stat": "on_hit_vamp", "amount": 0.0025},
    "omnivamp": {"stat": "omni_vamp", "amount": 0.0025},
    "tenacity": {"stat": "tenacity", "amount": 0.01},
}
const RATES: Array[String] = [
    "physical_pen_percent", "magic_pen_percent", "crit_chance", "luck",
    "on_hit_vamp", "omni_vamp", "tenacity"
]

static func validate_allocations(allocations: Variant) -> Dictionary:
    if not (allocations is Dictionary):
        return {"ok": false, "reason": "invalid_invested_stats_type"}
    for id in allocations:
        if not (id is String) or not INCREMENTS.has(id):
            return {"ok": false, "reason": "unknown_stat_id:" + str(id)}
        var number: Variant = allocations[id]
        if not (typeof(number) == TYPE_INT or typeof(number) == TYPE_FLOAT):
            return {"ok": false, "reason": "non_numeric_allocation:" + str(id)}
        var value: float = float(number)
        if is_nan(value) or is_inf(value) or value < 0.0 or (
                value != floorf(value)) or value > MAX_ALLOCATIONS:
            return {"ok": false, "reason": "invalid_allocation:" + str(id)}
    return {"ok": true}

static func compile(base: Dictionary, allocations: Variant,
        increments: Dictionary = INCREMENTS) -> Dictionary:
    var validation: Dictionary = validate_allocations(allocations)
    if not bool(validation.get("ok", false)):
        return validation
    if increments.size() != INCREMENTS.size():
        return {"ok": false, "reason": "invalid_increment_policy"}
    for id in INCREMENTS:
        if not increments.has(id) or not increments[id] is Dictionary:
            return {"ok": false, "reason": "missing_increment:" + str(id)}
        if str(increments[id].get("stat", "")) != str(INCREMENTS[id]["stat"]):
            return {"ok": false, "reason": "increment_target_mismatch:" + str(id)}
        var raw: Variant = increments[id].get("amount", null)
        if not (typeof(raw) == TYPE_INT or typeof(raw) == TYPE_FLOAT):
            return {"ok": false, "reason": "invalid_increment:" + str(id)}
        if not is_finite(float(raw)) or float(raw) < 0.0:
            return {"ok": false, "reason": "invalid_increment:" + str(id)}
    var combined: Dictionary = Stats.make(base)
    for id in allocations:
        var spec: Dictionary = increments[id]
        var key: String = str(spec["stat"])
        combined[key] = float(combined.get(key, 0.0)) + (
            float(allocations[id]) * float(spec["amount"]))
    for stat in combined:
        var raw: Variant = combined[stat]
        if typeof(raw) == TYPE_FLOAT or typeof(raw) == TYPE_INT:
            if not is_finite(float(raw)):
                return {"ok": false, "reason": "nonfinite_stat:" + str(stat)}
    for rate in RATES:
        if float(combined.get(rate, 0.0)) < 0.0 or float(combined[rate]) > 1.0:
            return {"ok": false, "reason": "rate_out_of_range:" + rate}
    for positive in ["max_health", "max_mana", "attack_speed", "move_speed",
            "projectile_speed_mult", "projectile_size", "mana_regen",
            "skill_haste", "crit_multiplier", "health"]:
        if float(combined.get(positive, 0.0)) < 0.0:
            return {"ok": false, "reason": "negative_stat:" + positive}
    if float(combined["max_health"]) <= 0.0:
        return {"ok": false, "reason": "invalid_max_health"}
    # This is an encounter *spawn*, so current resources start full.
    # In-mission effects still preserve actual current HP/mana.
    combined["health"] = float(combined["max_health"])
    combined["mana"] = float(combined["max_mana"])
    return {"ok": true, "stats": combined,
        "investments": (allocations as Dictionary).duplicate(true)}
