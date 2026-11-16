extends RefCounted
## Exact Python ScaledCanvas viewport transform and XZ coordinates.
const WIDTH: float = 1280.0
const HEIGHT: float = 720.0
const UNITS_PER_PIXEL: float = 0.01
const HUB_WIDTH: float = 2400.0
const HUB_HEIGHT: float = 1560.0

static func fitted_rect(window_size: Vector2) -> Rect2:
    if window_size.x <= 0.0 or window_size.y <= 0.0:
        return Rect2()
    var factor: float = minf(window_size.x / WIDTH, window_size.y / HEIGHT)
    var content_size: Vector2 = Vector2(WIDTH, HEIGHT) * factor
    return Rect2((window_size - content_size) * 0.5, content_size)

static func window_to_logical(pixel: Vector2, window_size: Vector2) -> Vector2:
    var r: Rect2 = fitted_rect(window_size)
    if r.size.x <= 0.0:
        return Vector2(INF, INF)
    return (pixel - r.position) * (WIDTH / r.size.x)

static func logical_to_window(point: Vector2, window_size: Vector2) -> Vector2:
    var r: Rect2 = fitted_rect(window_size)
    return r.position + point * (r.size.x / WIDTH)

static func contains_window_point(pixel: Vector2, window_size: Vector2) -> bool:
    var r: Rect2 = fitted_rect(window_size)
    if r.size.x <= 0.0:
        return false
    var logical: Vector2 = window_to_logical(pixel, window_size)
    return logical.x >= 0.0 and logical.x <= WIDTH and logical.y >= 0.0 and logical.y <= HEIGHT

static func logical_to_world(point: Vector2, elevation: float = 0.0) -> Vector3:
    return Vector3(point.x * UNITS_PER_PIXEL, elevation, point.y * UNITS_PER_PIXEL)

static func world_to_logical(point: Vector3) -> Vector2:
    return Vector2(point.x, point.z) / UNITS_PER_PIXEL

static func hub_camera_top_left(player: Vector2) -> Vector2:
    return Vector2(
        clampf(player.x - WIDTH * 0.5, 0.0, HUB_WIDTH - WIDTH),
        clampf(player.y - HEIGHT * 0.5, 0.0, HUB_HEIGHT - HEIGHT)
    )

static func camera_center(player: Vector2, in_hub: bool) -> Vector2:
    if in_hub:
        return hub_camera_top_left(player) + Vector2(WIDTH, HEIGHT) * 0.5
    return Vector2(WIDTH, HEIGHT) * 0.5

static func view_pointer_to_world(pointer: Vector2, center: Vector2) -> Vector3:
    return logical_to_world(center + pointer - Vector2(WIDTH, HEIGHT) * 0.5)
