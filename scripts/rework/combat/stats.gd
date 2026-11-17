extends RefCounted
## Rework v1 stat contract. Rates use fractions (0.25 == 25%) except
## attack_speed (shots/second), haste (rating), and mana_regen (per second).
## Numbers tagged BALANCE_DRAFT in GDD are replaceable by actor data.
const DRAFT_DEFAULTS: Dictionary = {
    "max_health": 100.0, "health": 100.0,
    "attack_damage": 5.0, "ability_power": 0.0,
    "armor": 0.0, "magic_resist": 0.0,
    "physical_pen_percent": 0.0, "physical_pen_flat": 0.0,
    "magic_pen_percent": 0.0, "magic_pen_flat": 0.0,
    "move_speed": 5.0, "attack_speed": 2.0,
    "projectile_speed_mult": 1.0, "projectile_size": 10,
    "crit_chance": 0.0, "crit_multiplier": 1.2,
    "skill_haste": 0, "luck": 0.0,
    "on_hit_vamp": 0.0, "omni_vamp": 0.0,
    "tenacity": 0.0, "boss_control_resistance": 0.0,
    "max_mana": 100.0, "mana": 100.0, "mana_regen": 5.0
}

static func make(overrides: Dictionary = {}) -> Dictionary:
    var result: Dictionary = DRAFT_DEFAULTS.duplicate(true)
    result.merge(overrides, true)
    return result

static func valid_rate(value: float) -> float:
    return clampf(value, 0.0, 1.0)

static func effective_cooldown_ticks(base_ticks: int, haste: int) -> int:
    ## Formula is BALANCE_DRAFT; the nonzero cooldown is CONFIRMED.
    ## Nonnegative haste is a temporary input policy, not a finalized cap.
    return maxi(1, ceili(float(maxi(1, base_ticks)) / (1.0 + float(maxi(0, haste)) / 100.0)))
