"""Keyboard-driven movement and dash direction, independent of mouse aim."""

import math


def movement_vector(keys):
    """Normalized WASD / arrow-key input, or (0, 0) when idle."""
    x = int("d" in keys or "right" in keys) - int("a" in keys or "left" in keys)
    y = int("s" in keys or "down" in keys) - int("w" in keys or "up" in keys)
    d = math.hypot(x, y)
    return (x / d, y / d) if d else (0.0, 0.0)


def dash_vector(keys, velocity, last_direction):
    """Prefer pressed keys, then momentum, then the last deliberate direction."""
    x, y = movement_vector(keys)
    if x or y:
        return x, y
    vx, vy = velocity
    speed = math.hypot(vx, vy)
    if speed > 20.0:
        return vx / speed, vy / speed
    lx, ly = last_direction
    d = math.hypot(lx, ly)
    return (lx / d, ly / d) if d else (0.0, -1.0)
