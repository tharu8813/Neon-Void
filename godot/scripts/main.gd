extends Node3D
## P1 is an isolated 3D rendering scaffold. No input/combat/save migration.
## The orthographic camera is a visual placeholder; P2 validates exact projection.

@onready var arena_floor: MeshInstance3D = $ArenaFloor
@onready var player_visual: MeshInstance3D = $PlayerVisual
@onready var camera_3d: Camera3D = $Camera3D

func _ready() -> void:
    assert(camera_3d.projection == Camera3D.PROJECTION_ORTHOGONAL)
    assert(arena_floor.mesh != null)
    assert(player_visual.mesh != null)
