extends RefCounted
## Pure deterministic planar AI intent, no scene tree/physics. D owns spawns.
static func decide(enemy: Dictionary, target: Dictionary) -> Dictionary:
    if not bool(enemy.get("alive", true)) or not bool(target.get("alive", true)):
        return {"move": Vector2.ZERO, "shoot": false, "aim": Vector2.RIGHT}
    var from: Vector3 = enemy.get("position", Vector3.ZERO)
    var to: Vector3 = target.get("position", Vector3.ZERO)
    var diff: Vector2 = Vector2(to.x - from.x, to.z - from.z)
    var distance: float = diff.length()
    var direction: Vector2 = diff.normalized() if distance > 0.00001 else Vector2.RIGHT
    var minimum: float = maxf(0.0, float(enemy.get("preferred_min_range", 0.0)))
    var maximum: float = maxf(minimum, float(enemy.get("preferred_max_range", 8.0)))
    var attack_range: float = maxf(0.0, float(enemy.get("attack_range", maximum)))
    var movement: Vector2 = Vector2.ZERO
    if distance > maximum:
        movement = direction
    elif distance < minimum:
        movement = -direction
    return {"move": movement, "shoot": distance <= attack_range, "aim": direction,
        "distance": distance}
