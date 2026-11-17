extends SceneTree
## Headless Godot smoke: imports/scripts/scene tree/contract only.
## Rendering, framerate, DPI, mouse precision and gameplay require a real display.

func _initialize() -> void:
    call_deferred("_run_checks")

func _run_checks() -> void:
    var count: int = 0
    var resource: PackedScene = load("res://scenes/main.tscn") as PackedScene
    if resource == null:
        printerr("P1: cannot load main scene")
        quit(1)
        return
    var stage: Node3D = resource.instantiate() as Node3D
    if stage == null:
        printerr("P1: root must be Node3D")
        quit(1)
        return
    root.add_child(stage)
    var camera: Camera3D = stage.get_node_or_null("Camera3D") as Camera3D
    var player: MeshInstance3D = stage.get_node_or_null("PlayerVisual") as MeshInstance3D
    var arena: MeshInstance3D = stage.get_node_or_null("ArenaFloor") as MeshInstance3D
    if camera == null or camera.projection != Camera3D.PROJECTION_ORTHOGONAL:
        printerr("P1: missing orthographic Camera3D")
        count += 1
    elif absf(camera.size - 7.2) > 0.00001:
        printerr("P1: camera world-height changed")
        count += 1
    if player == null or player.mesh == null:
        printerr("P1: missing 3D placeholder")
        count += 1
    if arena == null or arena.mesh == null:
        printerr("P1: missing 3D arena")
        count += 1
    var config: Node = root.get_node_or_null("RuntimeConfig")
    if config == null:
        printerr("P1: RuntimeConfig autoload missing")
        count += 1
    else:
        var p: Vector3 = config.call("logical_to_world", Vector2(640, 360))
        if p.distance_to(Vector3(6.4, 0.0, 3.6)) > 0.00001:
            printerr("P1: logical coordinates differ from frozen reference")
            count += 1
        var restored: Vector2 = config.call("world_to_logical", p)
        if restored.distance_to(Vector2(640, 360)) > 0.0001:
            printerr("P1: world_to_logical inverse failed")
            count += 1
    if not is_equal_approx(float(Engine.physics_ticks_per_second), 60.0):
        printerr("P1: simulation tick must be 60Hz")
        count += 1
    stage.queue_free()
    if count == 0:
        print("P1 GODOT HEADLESS SMOKE PASS")
    quit(1 if count > 0 else 0)
