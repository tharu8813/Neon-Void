extends RefCounted
## Pure Python combat.py parity helpers. Time inputs are simulated seconds.
static func mitigate_damage(raw: float, armor: float, penetration: float = 0.0) -> float:
    if raw <= 0.0:
        return 0.0
    return raw * 100.0 / (100.0 + maxf(0.0, armor - maxf(0.0, penetration)))

static func skill_cost(base: float, efficiency: float) -> float:
    return roundf(maxf(1.0, base * maxf(0.45, efficiency)) * 10.0) / 10.0

static func skill_cooldown(base: float, factor: float) -> float:
    return maxf(0.5, base * maxf(0.5, factor))

static func effective_armor(base: float, guard_seconds: float) -> float:
    return maxf(0.0, base + (20.0 if guard_seconds > 0.0 else 0.0))

static func add_dot(statuses: Array, kind: String, dps: float, duration: float, max_stacks: int = 3) -> void:
    if kind.is_empty() or dps <= 0 or duration <= 0:
        return
    for status in statuses:
        if status["kind"] == kind and float(status["remaining"]) > 0.0:
            status["stacks"] = mini(max_stacks, int(status["stacks"]) + 1)
            status["remaining"] = maxf(float(status["remaining"]), duration)
            status["dps"] = maxf(float(status["dps"]), dps)
            return
    statuses.append({"kind": kind, "remaining": duration, "dps": dps, "stacks": 1})

static func advance_dots(statuses: Array, dt: float) -> Array:
    var hits: Array = []
    var alive: Array = []
    for status in statuses:
        var remaining: float = float(status["remaining"])
        if remaining <= 0.0:
            continue
        var active_time: float = minf(dt, remaining)
        if active_time > 0.0:
            hits.append(float(status["dps"]) * int(status["stacks"]) * active_time)
        status["remaining"] = maxf(0.0, remaining - dt)
        if float(status["remaining"]) > 0.0:
            alive.append(status)
    statuses.clear()
    statuses.append_array(alive)
    return hits

static func boss_phase(hp_fraction: float) -> int:
    if hp_fraction > 2.0 / 3.0:
        return 1
    return 2 if hp_fraction > 1.0 / 3.0 else 3
