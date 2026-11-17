extends Camera3D
## Standalone rig. A must wire player/input explicitly; no root-scene changes.
const CameraMath = preload("res://scripts/rework/camera/look_ahead_math.gd")
@export var view_height: float = 7.2
@export var max_look_distance: float = 2.0
@export var responsiveness: float = 9.0
@export var player_margin: float = 0.5
var world_bounds: Rect2 = Rect2(Vector2.ZERO, Vector2(24.0, 18.0))

func _ready() -> void:
    projection = Camera3D.PROJECTION_ORTHOGONAL
    keep_aspect = Camera3D.KEEP_HEIGHT
    size = view_height
    rotation_degrees = Vector3(-90.0, 0.0, 0.0)

func follow(player_world: Vector3, pointer_logical: Vector2, delta_seconds: float) -> void:
    var next_target: Vector2 = CameraMath.camera_target(
        Vector2(player_world.x, player_world.z), pointer_logical,
        world_bounds, view_height, max_look_distance, player_margin
    )
    var now: Vector2 = Vector2(global_position.x, global_position.z)
    var smoothed: Vector2 = CameraMath.smooth(now, next_target, delta_seconds, responsiveness)
    global_position = Vector3(smoothed.x, 14.0, smoothed.y)

func pointer_ground_from_window(pixel: Vector2, client_size: Vector2) -> Dictionary:
    var result: Dictionary = CameraMath.window_to_logical(pixel, client_size)
    if not bool(result.get("valid", false)):
        return result
    var camera_xz := Vector2(global_position.x, global_position.z)
    result["world"] = CameraMath.pointer_ground(camera_xz, result["logical"], view_height)
    return result

func direction_to_window_pointer(player_world: Vector3, pixel: Vector2, client_size: Vector2) -> Vector3:
    var result: Dictionary = pointer_ground_from_window(pixel, client_size)
    if not bool(result.get("valid", false)):
        return Vector3.ZERO
    return CameraMath.aim_direction(
        player_world, Vector2(global_position.x, global_position.z), result["logical"], view_height
    )
