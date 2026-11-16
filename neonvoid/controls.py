"""Configurable keyboard bindings and movement vectors.

Movement uses user-selectable keys plus fixed arrow-key alternatives.
Shift always remains a dash fallback; Escape, O, M and R are protected
system keys so the player cannot accidentally lock themselves out.
"""

from __future__ import annotations

import math
import string

ACTION_ORDER = ("up", "down", "left", "right", "dash", "pause",
                "skill_pulse", "skill_guard")
ACTION_LABELS = {
    "up": "위로 이동",
    "down": "아래로 이동",
    "left": "왼쪽 이동",
    "right": "오른쪽 이동",
    "dash": "대시",
    "pause": "일시정지",
    "skill_pulse": "충격파 스킬",
    "skill_guard": "보호막 스킬",
}
DEFAULT_BINDINGS = {
    "up": "w",
    "down": "s",
    "left": "a",
    "right": "d",
    "dash": "space",
    "pause": "p",
    "skill_pulse": "q",
    "skill_guard": "e",
}
RESERVED_KEYS = frozenset(("m", "r", "o"))
ALLOWED_KEYS = (frozenset(string.ascii_lowercase) - RESERVED_KEYS) | frozenset(("space",))


def validate_bindings(bindings):
    """Validate the complete action mapping and normalize key names."""
    if not isinstance(bindings, dict) or set(bindings) != set(ACTION_ORDER):
        raise ValueError("Expected one key binding per action")
    output = {}
    for action in ACTION_ORDER:
        key = bindings[action]
        if type(key) is not str:
            raise ValueError(f"Invalid key for {action}")
        key = key.lower().strip()
        if key not in ALLOWED_KEYS:
            raise ValueError(f"Unsupported or reserved key: {key}")
        output[action] = key
    if len(set(output.values())) != len(ACTION_ORDER):
        raise ValueError("Each action must use a different key")
    return output


def migrate_v2_bindings(bindings):
    """Preserve six existing bindings and assign nonconflicting new skill keys."""
    legacy = ACTION_ORDER[:6]
    if not isinstance(bindings, dict) or set(bindings) != set(legacy):
        raise ValueError("Invalid old bindings")
    old = {}
    for action in legacy:
        key = bindings[action]
        if type(key) is not str or key.lower().strip() not in ALLOWED_KEYS:
            raise ValueError("Invalid legacy key")
        old[action] = key.lower().strip()
    if len(set(old.values())) != len(old):
        raise ValueError("Duplicate legacy key")
    used = set(old.values())
    for action, preferred in (("skill_pulse", "q"), ("skill_guard", "e")):
        for candidate in (preferred, "f", "g", "h", "j", "k", "l", "z", "x", "c"):
            if candidate not in used:
                old[action] = candidate
                used.add(candidate)
                break
        else:
            raise ValueError("No free skill key")
    return validate_bindings(old)


def rebind_action(bindings, action, key):
    """Return a new mapping; never silently steal another action's key."""
    if action not in ACTION_ORDER:
        raise ValueError("Unknown action")
    if type(key) is not str:
        raise ValueError("Key must be text")
    updated = validate_bindings(bindings)
    updated[action] = key.lower().strip()
    return validate_bindings(updated)


def movement_vector(keys, bindings=None):
    """Normalized directional input; arrow keys always remain available."""
    bindings = DEFAULT_BINDINGS if bindings is None else bindings
    x = int(bindings["right"] in keys or "right" in keys) - int(
        bindings["left"] in keys or "left" in keys)
    y = int(bindings["down"] in keys or "down" in keys) - int(
        bindings["up"] in keys or "up" in keys)
    magnitude = math.hypot(x, y)
    return (x / magnitude, y / magnitude) if magnitude else (0.0, 0.0)


def dash_vector(keys, velocity, last_direction, bindings=None):
    """Pressed direction, momentum, or last deliberate move; no mouse aim."""
    x, y = movement_vector(keys, bindings)
    if x or y:
        return x, y
    vx, vy = velocity
    speed = math.hypot(vx, vy)
    if speed > 20.0:
        return vx / speed, vy / speed
    lx, ly = last_direction
    magnitude = math.hypot(lx, ly)
    return (lx / magnitude, ly / magnitude) if magnitude else (0.0, -1.0)
