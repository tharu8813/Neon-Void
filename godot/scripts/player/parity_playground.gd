extends Node3D
## Standalone integration preview, NOT A-owned main.tscn.
@onready var player: Node3D = $LegacyPlayer
@onready var controls: Node = $InputController
@onready var camera: Camera3D = $LegacyCamera
@onready var cursor: Control = $CursorLayer/Crosshair

func _ready() -> void:
    player.call("set_input_source", controls)
    player.set("camera_source", camera)
    camera.set("follow_target", player)
    cursor.set("input_source", controls)
