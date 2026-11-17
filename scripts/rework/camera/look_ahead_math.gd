extends RefCounted
## Orthographic top-down, XZ ground plane. All input coordinates must use the
## same pixel units as client_size; logical viewport is always 1280x720.
const LOGICAL_SIZE := Vector2(1280.0, 720.0)
const DEFAULT_HEIGHT: float = 7.2

static func content_rect(client_size: Vector2) -> Rect2:
    if client_size.x <= 0.0 or client_size.y <= 0.0:
        return Rect2()
    var scale: float = minf(client_size.x / LOGICAL_SIZE.x, client_size.y / LOGICAL_SIZE.y)
    var size: Vector2 = LOGICAL_SIZE * scale
    return Rect2((client_size - size) * 0.5, size)

static func window_to_logical(pixel: Vector2, client_size: Vector2) -> Dictionary:
    var content: Rect2 = content_rect(client_size)
    if content.size.x <= 0.0 or not content.has_point(pixel):
        return {"valid": false}
    return {"valid": true, "logical": (pixel - content.position) / content.size * LOGICAL_SIZE}

static func pointer_ground(camera_center_xz: Vector2, logical_pointer: Vector2, view_height: float = DEFAULT_HEIGHT) -> Vector3:
    var offset: Vector2 = (logical_pointer / LOGICAL_SIZE - Vector2(0.5, 0.5)) * Vector2(view_height * LOGICAL_SIZE.x / LOGICAL_SIZE.y, view_height)
    return Vector3(camera_center_xz.x + offset.x, 0.0, camera_center_xz.y + offset.y)

static func aim_direction(player_world: Vector3, current_camera_xz: Vector2, logical_pointer: Vector2, view_height: float = DEFAULT_HEIGHT) -> Vector3:
    # NEVER use the future camera target for firing. Use the rendered camera.
    var target: Vector3 = pointer_ground(current_camera_xz, logical_pointer, view_height)
    var delta: Vector3 = target - Vector3(player_world.x, 0.0, player_world.z)
    return delta.normalized() if delta.length_squared() > 0.00000001 else Vector3.ZERO

static func camera_target(player_xz: Vector2, logical_pointer: Vector2, bounds: Rect2, view_height: float = DEFAULT_HEIGHT, max_look_distance: float = 2.0, player_margin: float = 0.5) -> Vector2:
    # Pointer offset is relative to screen center, not the current world aim ray.
    # This breaks the camera->ray->camera feedback loop.
    var cursor_offset: Vector2 = (logical_pointer / LOGICAL_SIZE - Vector2(0.5, 0.5)) * Vector2(view_height * LOGICAL_SIZE.x / LOGICAL_SIZE.y, view_height)
    var half_view: Vector2 = Vector2(view_height * LOGICAL_SIZE.x / LOGICAL_SIZE.y, view_height) * 0.5
    var safe: Vector2 = Vector2(maxf(0.0, half_view.x - player_margin), maxf(0.0, half_view.y - player_margin))
    var look: Vector2 = (cursor_offset * 0.5).limit_length(maxf(0.0, max_look_distance))
    look.x = clampf(look.x, -safe.x, safe.x)
    look.y = clampf(look.y, -safe.y, safe.y)
    var requested: Vector2 = player_xz + look
    # On narrow maps center the camera rather than producing inverted clamps.
    var min_center: Vector2 = bounds.position + half_view
    var max_center: Vector2 = bounds.end - half_view
    var center: Vector2 = requested
    center.x = (bounds.position.x + bounds.end.x) * 0.5 if min_center.x > max_center.x else clampf(requested.x, min_center.x, max_center.x)
    center.y = (bounds.position.y + bounds.end.y) * 0.5 if min_center.y > max_center.y else clampf(requested.y, min_center.y, max_center.y)
    return center

static func smooth(current: Vector2, target: Vector2, delta_seconds: float, responsiveness: float = 9.0) -> Vector2:
    if delta_seconds <= 0.0:
        return current
    var alpha: float = 1.0 - exp(-maxf(0.0, responsiveness) * delta_seconds)
    return current.lerp(target, alpha)
