extends RefCounted
## Python controls.py equivalence. Bindings belong to Godot data, not legacy JSON.
const ACTION_ORDER: Array[String] = ["up", "down", "left", "right", "dash", "pause", "skill_pulse", "skill_guard", "interact"]
const DEFAULT_BINDINGS: Dictionary = {
    "up": "w", "down": "s", "left": "a", "right": "d", "dash": "space",
    "pause": "p", "skill_pulse": "q", "skill_guard": "e", "interact": "f"
}
const RESERVED: Array[String] = ["m", "r", "o"]

static func is_allowed_key(key: String) -> bool:
    if key == "space":
        return true
    return key.length() == 1 and key >= "a" and key <= "z" and not RESERVED.has(key)

static func validated(proposed: Dictionary) -> Dictionary:
    if proposed.size() != ACTION_ORDER.size():
        return {}
    var result: Dictionary = {}
    var used: Dictionary = {}
    for action in ACTION_ORDER:
        if not proposed.has(action) or typeof(proposed[action]) != TYPE_STRING:
            return {}
        var key: String = (proposed[action] as String).strip_edges().to_lower()
        if not is_allowed_key(key) or used.has(key):
            return {}
        used[key] = true
        result[action] = key
    return result

static func rebound(bindings: Dictionary, action: String, key: String) -> Dictionary:
    if not ACTION_ORDER.has(action):
        return {}
    var updated: Dictionary = validated(bindings)
    if updated.is_empty():
        return {}
    updated[action] = key
    return validated(updated)

static func movement(pressed: Dictionary, bindings: Dictionary) -> Vector2:
    var x: float = float(int(pressed.has(bindings["right"]) or pressed.has("right")) - int(pressed.has(bindings["left"]) or pressed.has("left")))
    var y: float = float(int(pressed.has(bindings["down"]) or pressed.has("down")) - int(pressed.has(bindings["up"]) or pressed.has("up")))
    var raw: Vector2 = Vector2(x, y)
    return raw.normalized() if raw.length_squared() > 0.0 else Vector2.ZERO

static func dash_direction(movement_input: Vector2, velocity_px: Vector2, last_direction: Vector2) -> Vector2:
    if movement_input.length_squared() > 0.0:
        return movement_input.normalized()
    if velocity_px.length() > 20.0:
        return velocity_px.normalized()
    if last_direction.length_squared() > 0.0:
        return last_direction.normalized()
    return Vector2.UP
