extends RefCounted
## Pure, data-defined status effects. Units: ticks at 60 Hz.
## State shape: {"statuses": Array, "tenacity": fraction, "boss_control_resistance": fraction}
## Definition shape: id, duration_ticks, control, stack_mode, max_stacks,
## flags: Array[String], stat_modifiers: Dictionary, dot_dps, regen_hps.
## No individual stacking formula is represented as user-approved.
const Stats = preload("res://scripts/rework/combat/stats.gd")
const TICKS_PER_SECOND: float = 60.0

static func apply(state: Dictionary, definition: Dictionary) -> Dictionary:
    var effect_id: String = str(definition.get("id", ""))
    var mode: String = str(definition.get("stack_mode", "refresh"))
    if effect_id.is_empty() or not mode in ["refresh", "replace", "stack", "independent"]:
        return {"ok": false, "reason": "invalid_status_definition"}
    var ticks: int = maxi(0, int(definition.get("duration_ticks", 0)))
    if bool(definition.get("control", false)):
        var tenacity: float = Stats.valid_rate(float(state.get("tenacity", 0.0)))
        var boss_resist: float = Stats.valid_rate(float(state.get("boss_control_resistance", 0.0)))
        # Multiplicative control-resistance handling is BALANCE_DRAFT.
        ticks = ceili(float(ticks) * (1.0 - tenacity) * (1.0 - boss_resist))
    if ticks <= 0:
        return {"ok": false, "reason": "immune_or_zero_duration"}
    if not state.has("statuses"):
        state["statuses"] = []
    var active: Array = state["statuses"]
    var entry: Dictionary = definition.duplicate(true)
    entry["remaining_ticks"] = ticks
    entry["stacks"] = 1
    var cap: int = maxi(1, int(definition.get("max_stacks", 1)))
    if mode != "independent":
        for i in range(active.size()):
            var existing: Dictionary = active[i]
            if str(existing.get("id", "")) != effect_id:
                continue
            if mode == "replace":
                active[i] = entry
            elif mode == "stack":
                existing["stacks"] = mini(cap, int(existing.get("stacks", 1)) + 1)
                existing["remaining_ticks"] = maxi(int(existing["remaining_ticks"]), ticks)
            else:
                existing["remaining_ticks"] = maxi(int(existing["remaining_ticks"]), ticks)
            return {"ok": true, "stack_count": int(active[i].get("stacks", 1)), "duration_ticks": int(active[i]["remaining_ticks"])}
    active.append(entry)
    return {"ok": true, "stack_count": 1, "duration_ticks": ticks}

static func active_flags(state: Dictionary) -> Dictionary:
    var flags: Dictionary = {}
    for entry in state.get("statuses", []):
        for name in entry.get("flags", []):
            flags[str(name)] = true
    return flags

static func stat_offsets(state: Dictionary) -> Dictionary:
    var offsets: Dictionary = {}
    for entry in state.get("statuses", []):
        var stacks: int = maxi(1, int(entry.get("stacks", 1)))
        var values: Dictionary = entry.get("stat_modifiers", {})
        for key in values:
            offsets[key] = float(offsets.get(key, 0.0)) + float(values[key]) * stacks
    return offsets

static func tick(state: Dictionary) -> Dictionary:
    ## Emits damage REQUESTS, not HP mutations or VFX.
    ## Caller applies requests through damage.gd at a fixed 60 Hz tick.
    var next_statuses: Array = []
    var damage_requests: Array = []
    var healing: float = 0.0
    for entry in state.get("statuses", []):
        var ticks: int = int(entry.get("remaining_ticks", 0))
        if ticks <= 0:
            continue
        var stacks: int = maxi(1, int(entry.get("stacks", 1)))
        var dps: float = float(entry.get("dot_dps", 0.0))
        if dps > 0.0:
            damage_requests.append({
                "status_id": str(entry.get("id", "")),
                "damage_type": str(entry.get("dot_damage_type", "magic")),
                "base_damage": dps * float(stacks) / TICKS_PER_SECOND,
                "ad_ratio": 0.0, "ap_ratio": 0.0,
                "can_crit": false, "is_dot": true, "is_area": bool(entry.get("is_area", false)),
                "source_id": str(entry.get("source_id", ""))
            })
        healing += maxf(0.0, float(entry.get("regen_hps", 0.0))) * float(stacks) / TICKS_PER_SECOND
        entry["remaining_ticks"] = ticks - 1
        if int(entry["remaining_ticks"]) > 0:
            next_statuses.append(entry)
    state["statuses"] = next_statuses
    return {"damage_requests": damage_requests, "healing_request": healing}

static func modified_stats(base: Dictionary, state: Dictionary) -> Dictionary:
    ## Derive ephemeral stats; do not mutate the source/loadout baseline.
    var merged: Dictionary = base.duplicate(true)
    var offsets: Dictionary = stat_offsets(state)
    for key in offsets:
        merged[key] = float(merged.get(key, 0.0)) + float(offsets[key])
    return merged

static func action_allowed(state: Dictionary, action: String, gameplay_rng: RandomNumberGenerator) -> bool:
    ## Logical flags are authored by status definitions, not by presentation.
    ## Action kinds: attack, skill, movement_skill, move, dash, interact.
    var flags: Dictionary = active_flags(state)
    if bool(flags.get("stopped", false)):
        return false
    if (action == "skill" or action == "movement_skill") and bool(flags.get("silenced", false)):
        return false
    if (action == "move" or action == "dash" or action == "movement_skill") and bool(flags.get("rooted", false)):
        return false
    if action == "attack":
        # Multiple malfunction sources: max chance is a BALANCE_DRAFT policy.
        var fail_chance: float = 0.0
        for entry in state.get("statuses", []):
            fail_chance = maxf(fail_chance, Stats.valid_rate(float(entry.get("attack_failure_chance", 0.0))))
        if fail_chance > 0.0 and gameplay_rng.randf() < fail_chance:
            return false
    return true
