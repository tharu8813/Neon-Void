extends Camera3D
## Fixed top-down orthographic P2 camera; native viewport stays 1280x720.
const Mapper = preload("res://scripts/camera/legacy_viewport.gd")
const CAMERA_ELEVATION: float = 14.0
const ORTHOGRAPHIC_HEIGHT: float = 7.2

@export var hub_mode: bool = false
@export var follow_target: Node3D

func _ready() -> void:
    projection = Camera3D.PROJECTION_ORTHOGONAL
    keep_aspect = Camera3D.KEEP_HEIGHT
    size = ORTHOGRAPHIC_HEIGHT
    rotation_degrees = Vector3(-90.0, 0.0, 0.0)
    if is_instance_valid(follow_target):
        follow_logical(Mapper.world_to_logical(follow_target.global_position))
    else:
        follow_logical(Vector2(640.0, 360.0))

func _physics_process(_delta: float) -> void:
    if is_instance_valid(follow_target):
        follow_logical(Mapper.world_to_logical(follow_target.global_position))

func follow_logical(player: Vector2) -> void:
    var center: Vector2 = Mapper.camera_center(player, hub_mode)
    global_position = Mapper.logical_to_world(center, CAMERA_ELEVATION)

func camera_top_left() -> Vector2:
    return Mapper.world_to_logical(global_position) - Vector2(640.0, 360.0)

func pointer_to_world(logical_pointer: Vector2) -> Vector3:
    ## project_ray_* expects 1280x720 logical viewport coordinates.
    var origin: Vector3 = project_ray_origin(logical_pointer)
    var ray: Vector3 = project_ray_normal(logical_pointer)
    if absf(ray.y) < 0.000001:
        return Vector3(INF, INF, INF)
    var t: float = -origin.y / ray.y
    if t < 0.0:
        return Vector3(INF, INF, INF)
    return origin + ray * t

func window_pointer_to_world(pixel: Vector2, client_size: Vector2) -> Dictionary:
    if not Mapper.contains_window_point(pixel, client_size):
        return {"valid": false}
    var logical: Vector2 = Mapper.window_to_logical(pixel, client_size)
    return {"valid": true, "logical": logical, "world": pointer_to_world(logical)}

func world_pointer_math(logical_pointer: Vector2) -> Vector3:
    ## Renderer-independent reference transform for fixed-camera QA.
    return Mapper.view_pointer_to_world(logical_pointer, Mapper.world_to_logical(global_position))
