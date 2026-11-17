extends RefCounted
## Rework v1 balance draft. Every numeric balance value is configurable; no GDD draft is final.
const DEFAULTS := {
    "mission_xp_base": 100, "mission_xp_step": 30,
    "vaccine_xp_base": 180, "vaccine_xp_step": 90,
    "fragments_per_point": 100, "fragments_per_vaccine_level": 100,
    "objective_vaccine_xp": 15, "hardware_vaccine_xp": 120,
    "all_clean_extraction_bonus_xp": 220,
    "death_base_percent": 20, "death_per_hardware_percent": 10,
    "death_max_percent": 70,
    "mission_start_rerolls": 3,
}
## Costs are in whole points. Concrete stat increments are owned by B, not E.
const STAT_COSTS := {
    "max_hp": 1, "attack_damage": 1, "ability_power": 1,
    "armor": 1, "magic_resist": 1, "move_speed": 1,
    "fire_rate": 1, "projectile_speed": 1, "crit_chance": 1,
    "crit_damage": 1, "ability_haste": 1, "luck": 1,
    "max_mana": 1, "mana_regen": 1,
    "projectile_size": 2, "flat_armor_pen": 2, "flat_magic_pen": 2,
    "percent_armor_pen": 2, "percent_magic_pen": 2,
    "on_hit_vamp": 2, "omnivamp": 3, "tenacity": 2,
}
static func valid_config(config: Dictionary) -> bool:
    for key in config:
        if not DEFAULTS.has(key) or typeof(config[key]) != TYPE_INT:
            return false
        if config[key] < 0 or config[key] > 100000:
            return false
    var effective: Dictionary = DEFAULTS.duplicate()
    effective.merge(config, true)
    if effective.mission_xp_base < 1 or effective.vaccine_xp_base < 1             or effective.fragments_per_point < 1:
        return false
    if effective.death_base_percent > effective.death_max_percent             or effective.death_max_percent > 100:
        return false
    return true

static func get_number(key: String, config: Dictionary = {}) -> int:
    return int(config.get(key, DEFAULTS.get(key, 0)))

static func needed_xp(level: int, vaccine: bool, config: Dictionary = {}) -> int:
    if level < 1 or level > 1000000 or not valid_config(config):
        return -1
    var prefix := "vaccine" if vaccine else "mission"
    return get_number(prefix + "_xp_base", config) + (level - 1) * get_number(prefix + "_xp_step", config)

static func death_retention_percent(cleaned_count: int, config: Dictionary = {}) -> int:
    if cleaned_count < 0 or cleaned_count > 5 or not valid_config(config):
        return -1
    return mini(get_number("death_max_percent", config),
        get_number("death_base_percent", config) +
        cleaned_count * get_number("death_per_hardware_percent", config))

static func stat_cost_fragments(allocations: Dictionary, config: Dictionary = {}) -> int:
    if not valid_config(config):
        return -1
    var spent: int = 0
    for stat_id in allocations:
        if not STAT_COSTS.has(stat_id) or typeof(allocations[stat_id]) != TYPE_INT                 or allocations[stat_id] < 0 or allocations[stat_id] > 1000000:
            return -1
        spent += int(allocations[stat_id]) * int(STAT_COSTS[stat_id]) * get_number("fragments_per_point", config)
        if spent > 9007199254740991:
            return -1
    return spent
