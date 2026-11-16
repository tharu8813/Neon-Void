"""Armor mitigation and bounded damage-over-time effects.

All calculations operate on simulated seconds. DoT bypasses armor and is never
treated as a projectile hit, preventing recursive on-hit procs.
"""
from __future__ import annotations
from dataclasses import dataclass
import math


@dataclass(slots=True)
class DamageOverTime:
    kind: str
    remaining: float
    dps: float
    stacks: int = 1


def mitigate_damage(raw: float, armor: float, penetration: float = 0.0) -> float:
    """Every 100 effective armor doubles effective HP; penetration offsets armor."""
    if not all(math.isfinite(v) for v in (raw, armor, penetration)):
        raise ValueError("Damage values must be finite")
    if raw <= 0:
        return 0.0
    effective = max(0.0, armor - max(0.0, penetration))
    return raw * 100.0 / (100.0 + effective)


def add_dot(statuses: list[DamageOverTime], kind: str, dps: float,
            duration: float, max_stacks: int = 3) -> None:
    """Refresh a status kind, stacking only up to max_stacks."""
    if (not kind or not all(math.isfinite(v) for v in (dps, duration))
            or dps <= 0 or duration <= 0 or max_stacks < 1):
        raise ValueError("Invalid DoT parameters")
    for status in statuses:
        if status.kind == kind and status.remaining > 0:
            status.stacks = min(max_stacks, status.stacks + 1)
            status.remaining = max(status.remaining, duration)
            status.dps = max(status.dps, dps)
            return
    statuses.append(DamageOverTime(kind, duration, dps))


def advance_dots(statuses: list[DamageOverTime], dt: float) -> list[tuple[str, float]]:
    """Return elemental damage for dt, respecting fractional expiry exactly."""
    if not math.isfinite(dt) or dt < 0:
        raise ValueError("dt must be finite and nonnegative")
    damage = []
    remaining = []
    for effect in statuses:
        if effect.remaining <= 0:
            continue
        active_dt = min(dt, effect.remaining)
        if active_dt:
            damage.append((effect.kind, effect.dps * effect.stacks * active_dt))
        effect.remaining = max(0.0, effect.remaining - dt)
        if effect.remaining > 0:
            remaining.append(effect)
    statuses[:] = remaining
    return damage


def skill_cost(base: float, efficiency: float) -> float:
    return round(max(1.0, base * max(0.45, efficiency)), 1)


def skill_cooldown(base: float, factor: float) -> float:
    return max(0.5, base * max(0.5, factor))


def effective_armor(base: float, shield_seconds: float,
                    shield_bonus: float = 20.0) -> float:
    return max(0.0, base + (shield_bonus if shield_seconds > 0 else 0.0))
