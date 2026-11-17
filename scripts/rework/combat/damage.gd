extends RefCounted
## Pure combat arithmetic: does not render, randomize, award XP or mutate saves.
const Stats = preload("res://scripts/rework/combat/stats.gd")
const AOE_DOT_OMNI_EFFICIENCY: float = 0.33  # CONFIRMED

static func resistance_multiplier(resistance: float) -> float:
    ## BALANCE_DRAFT v0.1 (LoL-inspired resistance curve).
    if resistance >= 0.0:
        return 100.0 / (100.0 + resistance)
    return 2.0 - 100.0 / (100.0 - resistance)

static func effective_resistance(resistance: float, percent_pen: float, flat_pen: float) -> float:
    ## BALANCE_DRAFT v0.1: percent penetration BEFORE flat penetration.
    return resistance * (1.0 - Stats.valid_rate(percent_pen)) - maxf(0.0, flat_pen)

static func roll_critical(rng: RandomNumberGenerator, chance: float) -> bool:
    ## Caller injects a dedicated, seeded gameplay RNG.
    return rng.randf() < Stats.valid_rate(chance)

static func calculate(attacker: Dictionary, defender: Dictionary, attack: Dictionary, critical: bool = false) -> Dictionary:
    ## attack: damage_type physical|magic|true, base_damage, ad_ratio,
    ## ap_ratio, can_crit, is_area, is_dot, is_on_hit.
    ## attack and defender are read only here.
    var damage_type: String = str(attack.get("damage_type", "physical"))
    if damage_type != "physical" and damage_type != "magic" and damage_type != "true":
        return {"ok": false, "reason": "invalid_damage_type"}
    var raw: float = maxf(0.0,
        float(attack.get("base_damage", 0.0))
        + float(attack.get("ad_ratio", 0.0)) * float(attacker.get("attack_damage", 0.0))
        + float(attack.get("ap_ratio", 0.0)) * float(attacker.get("ability_power", 0.0)))
    var did_crit: bool = critical and bool(attack.get("can_crit", false))
    if did_crit:
        raw *= maxf(0.0, float(attacker.get("crit_multiplier", 1.2)))
    var resist: float = 0.0
    var multiplier: float = 1.0
    if damage_type == "physical":
        resist = effective_resistance(float(defender.get("armor", 0.0)),
            float(attacker.get("physical_pen_percent", 0.0)),
            float(attacker.get("physical_pen_flat", 0.0)))
        multiplier = resistance_multiplier(resist)
    elif damage_type == "magic":
        resist = effective_resistance(float(defender.get("magic_resist", 0.0)),
            float(attacker.get("magic_pen_percent", 0.0)),
            float(attacker.get("magic_pen_flat", 0.0)))
        multiplier = resistance_multiplier(resist)
    return {
        "ok": true, "damage_type": damage_type, "critical": did_crit,
        "raw_damage": raw, "effective_resistance": resist,
        "damage_multiplier": multiplier, "calculated_damage": maxf(0.0, raw * multiplier)
    }

static func apply_damage(defender: Dictionary, calculation: Dictionary, invulnerable: bool = false) -> Dictionary:
    ## HP-only policy: shield/overkill leech handling is UNCONFIRMED.
    ## Actual HP reduction is returned; caller commits the mutable entity state.
    if not bool(calculation.get("ok", false)):
        return {"ok": false, "actual_damage": 0.0}
    var current: float = maxf(0.0, float(defender.get("health", 0.0)))
    var applied: float = 0.0 if invulnerable else minf(current, maxf(0.0, float(calculation["calculated_damage"])))
    defender["health"] = current - applied
    return {"ok": true, "actual_damage": applied, "killed": current > 0.0 and applied >= current}

static func vamp_components(attacker: Dictionary, attack: Dictionary, actual_damage: float) -> Dictionary:
    ## Returns distinct components; whether they stack and heal order
    ## are UNCONFIRMED. Do not silently resolve both together.
    var dealt: float = maxf(0.0, actual_damage)
    var efficiency: float = AOE_DOT_OMNI_EFFICIENCY if (
        bool(attack.get("is_area", false)) or bool(attack.get("is_dot", false))) else 1.0
    return {
        "omni_vamp_heal": dealt * maxf(0.0, float(attacker.get("omni_vamp", 0.0))) * efficiency,
        "on_hit_vamp_heal": dealt * maxf(0.0, float(attacker.get("on_hit_vamp", 0.0))) if bool(attack.get("is_on_hit", false)) else 0.0,
        "omni_efficiency": efficiency,
        "stack_policy_unconfirmed": true
    }
