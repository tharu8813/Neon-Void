extends Node
## Frozen 2D logical contract for P2 mapping. No gameplay state lives here.
const LOGICAL_WIDTH: int = 1280
const LOGICAL_HEIGHT: int = 720
const TICK_HZ: int = 60
const WORLD_UNITS_PER_PIXEL: float = 0.01

func logical_to_world(point: Vector2, elevation: float = 0.0) -> Vector3:
    return Vector3(point.x * WORLD_UNITS_PER_PIXEL, elevation, point.y * WORLD_UNITS_PER_PIXEL)

func world_to_logical(point: Vector3) -> Vector2:
    return Vector2(point.x / WORLD_UNITS_PER_PIXEL, point.z / WORLD_UNITS_PER_PIXEL)
