extends SceneTree
## Math, camera and letterbox parity; physical Windows screenshots still required.
const Mapper = preload("res://scripts/camera/legacy_viewport.gd")
const CameraScene = preload("res://scenes/camera/legacy_camera.tscn")
var failures: int = 0

func _assert_close(actual: Vector2, expected: Vector2, name: String, tolerance: float = 0.001) -> void:
    if actual.distance_to(expected) > tolerance:
        failures += 1
        printerr("P2 ", name, " expected=", expected, " actual=", actual)

func _initialize() -> void:
    call_deferred("_check")

func _check() -> void:
    for size in [Vector2(960, 540), Vector2(1280, 720), Vector2(1920, 1080), Vector2(1024, 768), Vector2(2560, 1080)]:
        var logical: Vector2 = Vector2(731.25, 313.5)
        var native: Vector2 = Mapper.logical_to_window(logical, size)
        _assert_close(Mapper.window_to_logical(native, size), logical, "round trip " + str(size))
        if not Mapper.contains_window_point(native, size):
            failures += 1
            printerr("P2 content unexpectedly outside viewport ", size)
    var four_three: Rect2 = Mapper.fitted_rect(Vector2(1024, 768))
    _assert_close(four_three.position, Vector2(0, 96), "4:3 letterbox origin")
    _assert_close(four_three.size, Vector2(1024, 576), "4:3 letterbox content")
    if Mapper.contains_window_point(Vector2(512, 20), Vector2(1024, 768)):
        failures += 1
        printerr("P2 letterbox incorrectly accepts aim")
    _assert_close(Mapper.hub_camera_top_left(Vector2(1, 1)), Vector2.ZERO, "hub min")
    _assert_close(Mapper.hub_camera_top_left(Vector2(2400, 1560)), Vector2(1120, 840), "hub max")
    _assert_close(Mapper.hub_camera_top_left(Vector2(1200, 780)), Vector2(560, 420), "hub follow")
    _assert_close(Mapper.world_to_logical(Mapper.logical_to_world(Vector2(1200, 780))), Vector2(1200, 780), "XZ inverse")
    for dpi_scale in [1.0, 1.25, 1.5, 2.0]:
        var pixel_size: Vector2 = Vector2(1280, 720) * dpi_scale
        _assert_close(Mapper.window_to_logical(Vector2(640, 360) * dpi_scale, pixel_size), Vector2(640, 360), "DPI math " + str(dpi_scale))
    var camera: Camera3D = CameraScene.instantiate() as Camera3D
    root.add_child(camera)
    if camera.projection != Camera3D.PROJECTION_ORTHOGONAL or absf(camera.size - 7.2) > 0.00001:
        failures += 1
        printerr("P2 camera projection changed")
    camera.set("hub_mode", true)
    camera.call("follow_logical", Vector2(1200, 780))
    _assert_close(Mapper.world_to_logical(camera.global_position), Vector2(1200, 780), "hub center")
    _assert_close(camera.call("camera_top_left"), Vector2(560, 420), "hub top-left")
    _assert_close(Mapper.world_to_logical(camera.call("world_pointer_math", Vector2(700, 300))), Vector2(1260, 720), "hub pointer")
    camera.set("hub_mode", false)
    camera.call("follow_logical", Vector2(1200, 780))
    _assert_close(Mapper.world_to_logical(camera.global_position), Vector2(640, 360), "arena fixed center")
    _assert_close(Mapper.world_to_logical(camera.call("world_pointer_math", Vector2(700, 300))), Vector2(700, 300), "arena pointer")
    var hit: Dictionary = camera.call("window_pointer_to_world", Vector2(512, 20), Vector2(1024, 768))
    if hit.get("valid", true):
        failures += 1
        printerr("P2 letterbox ray accepted")
    camera.queue_free()
    if failures == 0:
        print("P2 CAMERA PARITY PASS (headless math; physical DPI pending)")
    quit(0 if failures == 0 else 1)
