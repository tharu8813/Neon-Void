extends Node3D
## Standalone visual showcase: does NOT run combat or influence gameplay.
@export_range(16, 2048, 16) var demo_count: int = 480
@export var animate_demo: bool = true
@onready var batch: MultiMeshInstance3D = $ProjectileBatch
var demo_time: float = 0.0

func _ready() -> void:
    _draw_demo()

func _process(delta: float) -> void:
    if animate_demo:
        demo_time += delta
        _draw_demo()

func _draw_demo() -> void:
    var positions: PackedVector2Array = PackedVector2Array()
    var colors: PackedColorArray = PackedColorArray()
    var n: int = mini(demo_count, 2048)
    for i in range(n):
        var angle: float = TAU * float(i) / float(maxi(1, n))
        var r: float = 90.0 + float(i % 11) * 18.0
        var phase: float = angle + demo_time * (0.08 + float(i % 4) * 0.04)
        positions.append(Vector2(640.0 + cos(phase) * r, 360.0 + sin(phase) * r))
        colors.append(Color(0.2 + float(i % 4) * 0.12, 0.75, 1.0))
    batch.call("set_projectiles", positions, colors)
