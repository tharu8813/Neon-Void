extends RefCounted
## Python world/session.py circle/rect, per-axis translation; not move_and_slide.
const Mapper = preload("res://scripts/camera/legacy_viewport.gd")

static func hub_pillars() -> Array[Rect2]:
    return [
        Rect2(1030.0, 515.0, 40.0, 95.0),
        Rect2(1330.0, 515.0, 40.0, 95.0),
        Rect2(1030.0, 965.0, 40.0, 95.0),
        Rect2(1330.0, 965.0, 40.0, 95.0)
    ]

static func overlaps_circle_rect(center: Vector2, radius: float, rect: Rect2) -> bool:
    var close: Vector2 = Vector2(
        clampf(center.x, rect.position.x, rect.end.x),
        clampf(center.y, rect.position.y, rect.end.y)
    )
    return center.distance_squared_to(close) < radius * radius

static func move_hub(origin: Vector2, offset: Vector2, radius: float, obstacles: Array[Rect2] = []) -> Vector2:
    var pillars: Array[Rect2] = hub_pillars() if obstacles.is_empty() else obstacles
    var next_x: float = clampf(origin.x + offset.x, radius, Mapper.HUB_WIDTH - radius)
    for pillar in pillars:
        if overlaps_circle_rect(Vector2(next_x, origin.y), radius, pillar):
            next_x = origin.x
            break
    var next_y: float = clampf(origin.y + offset.y, radius, Mapper.HUB_HEIGHT - radius)
    for pillar in pillars:
        if overlaps_circle_rect(Vector2(next_x, next_y), radius, pillar):
            next_y = origin.y
            break
    return Vector2(next_x, next_y)

static func move_arena(origin: Vector2, offset: Vector2, radius: float) -> Vector2:
    return Vector2(
        clampf(origin.x + offset.x, radius, Mapper.WIDTH - radius),
        clampf(origin.y + offset.y, radius, Mapper.HEIGHT - radius)
    )
