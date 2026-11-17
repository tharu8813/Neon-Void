extends SceneTree
## Run: godot --headless --path . --script res://tests/rework/camera/look_ahead_smoke.gd
const Math = preload("res://scripts/rework/camera/look_ahead_math.gd")
const Rig = preload("res://scripts/rework/camera/look_ahead_camera.gd")
var failures: int = 0

func check(ok: bool, message: String) -> void:
    if not ok:
        failures += 1
        printerr("REWORK CAMERA FAIL: ", message)

func close(a: Vector2, b: Vector2, name: String, tolerance: float = 0.001) -> void:
    check(a.distance_to(b) < tolerance, "%s expected=%s actual=%s" % [name, b, a])

func _initialize() -> void:
    call_deferred("_run")

func _run() -> void:
    var bounds := Rect2(0.0, 0.0, 30.0, 25.0)
    close(Math.camera_target(Vector2(12, 9), Vector2(640, 360), bounds), Vector2(12, 9), "zero cursor displacement")
    close(Math.camera_target(Vector2(12, 9), Vector2(1280, 360), bounds), Vector2(14, 9), "50 percent offset with max clamp")
    close(Math.camera_target(Vector2(1, 1), Vector2(0, 0), bounds), Vector2(6.4, 3.6), "world edge bounded")
    close(Math.camera_target(Vector2(12, 9), Vector2(900, 360), bounds, 7.2, 10.0), Vector2(13.3, 9), "exact half cursor offset")
    var narrow := Rect2(0.0, 0.0, 4.0, 3.0)
    close(Math.camera_target(Vector2(2, 1), Vector2(1100, 650), narrow), narrow.get_center(), "small map centering")
    var full := Math.window_to_logical(Vector2(640, 360), Vector2(1280, 720))
    check(full.get("valid", false), "logical center accepted")
    close(full["logical"], Vector2(640, 360), "native center")
    var four_three := Math.content_rect(Vector2(1024, 768))
    close(four_three.position, Vector2(0, 96), "letterbox origin")
    close(four_three.size, Vector2(1024, 576), "letterbox size")
    check(not Math.window_to_logical(Vector2(512, 20), Vector2(1024, 768)).get("valid", true), "bar rejected")
    for scale in [1.0, 1.25, 1.5, 2.0]:
        var window: Vector2 = Vector2(1280, 720) * float(scale)
        for pointer in [Vector2(0, 0), Vector2(640, 360), Vector2(1280, 720)]:
            var mapped: Dictionary = Math.window_to_logical(pointer * scale, window)
            check(mapped.get("valid", false), "DPI pointer valid")
            if mapped.get("valid", false):
                close(mapped["logical"], pointer, "DPI round trip " + str(scale))
    for camera_center in [Vector2(6.4, 3.6), Vector2(12.1, 9.4), Vector2(18.0, 14.0)]:
        for pointer in [Vector2(0, 0), Vector2(640, 360), Vector2(950, 500), Vector2(1280, 720)]:
            var target: Vector3 = Math.pointer_ground(camera_center, pointer)
            var player := Vector3(camera_center.x + 1.0, 0, camera_center.y - 1.0)
            var expected: Vector3 = (target - player).normalized()
            var aim: Vector3 = Math.aim_direction(player, camera_center, pointer)
            check(aim.distance_to(expected) <= 0.00001, "aim uses currently rendered camera ray")
            close(Vector2(target.x, target.z), camera_center + (pointer - Vector2(640, 360)) * 0.01, "world XZ projection")
    var once: Vector2 = Math.smooth(Vector2.ZERO, Vector2(10, 5), 1.0 / 60.0)
    var twice: Vector2 = Math.smooth(Math.smooth(Vector2.ZERO, Vector2(10, 5), 1.0 / 120.0), Vector2(10, 5), 1.0 / 120.0)
    close(once, twice, "frame-rate independent smoothing")
    var lagging: Vector2 = Math.clamp_visible_center(Vector2(-100, -100), Vector2(12, 9), bounds)
    var half_view := Vector2(6.4, 3.6)
    var safety := half_view - Vector2(0.5, 0.5)
    check(absf(lagging.x - 12.0) <= safety.x and absf(lagging.y - 9.0) <= safety.y, "fast movement cannot hide player")
    check(lagging.x >= half_view.x and lagging.y >= half_view.y, "lagging camera stays inside map")
    var rig: Camera3D = Rig.new()
    root.add_child(rig)
    check(rig.projection == Camera3D.PROJECTION_ORTHOGONAL, "orthographic rig")
    rig.world_bounds = bounds
    rig.global_position = Vector3(12.0, 14.0, 9.0)
    rig.call("follow", Vector3(12, 0, 9), Vector2(1280, 360), 1.0)
    var current: Vector2 = Vector2(rig.global_position.x, rig.global_position.z)
    check(current.distance_to(Vector2(14, 9)) < 0.001, "rig moves toward clamped target")
    var aim_result: Dictionary = rig.call("pointer_ground_from_window", Vector2(640, 360), Vector2(1280, 720))
    check(aim_result.get("valid", false), "rig aim result")
    close(Vector2(aim_result["world"].x, aim_result["world"].z), current, "pointer center tracks moved camera")
    rig.queue_free()
    if failures == 0:
        print("REWORK CAMERA PASS (50% lookahead, bounds, DPI, letterbox, aim, smoothing)")
    quit(0 if failures == 0 else 1)
