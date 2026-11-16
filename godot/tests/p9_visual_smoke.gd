extends SceneTree
## Only structural, mapping and bound checks. No physical GPU claim.
const Showcase = preload("res://visuals/p9_showcase.tscn")
var failures: int = 0

func check(ok: bool, message: String) -> void:
    if not ok:
        failures += 1
        printerr("P9: " + message)

func _initialize() -> void:
    call_deferred("_check")

func _check() -> void:
    var scene: Node3D = Showcase.instantiate() as Node3D
    if scene == null:
        printerr("P9: showcase load failed")
        quit(1)
        return
    root.add_child(scene)
    var batch: MultiMeshInstance3D = scene.get_node("ProjectileBatch") as MultiMeshInstance3D
    var camera: Camera3D = scene.get_node("Camera3D") as Camera3D
    check(camera != null and camera.projection == Camera3D.PROJECTION_ORTHOGONAL, "orthographic top-down")
    check(batch != null and batch.multimesh != null, "multimesh initialized")
    if batch != null and batch.multimesh != null:
        batch.call("set_projectiles", PackedVector2Array([Vector2(640, 360), Vector2(700, 280)]))
        check(batch.multimesh.visible_instance_count == 2, "two snapshot positions")
        # A headless dummy renderer may report zeroed GPU instance transforms.
        # Validate the exact mapping function feeding those transforms instead.
        var origin: Vector3 = batch.call("logical_to_mesh_position", Vector2(640, 360))
        var expected: Vector3 = Vector3(6.4, 0.13, 3.6)
        check(origin.distance_to(expected) < 0.0001, "legacy XZ input mapping actual=" + str(origin) + " expected=" + str(expected))
        var points: PackedVector2Array = PackedVector2Array()
        for i in range(2050):
            points.append(Vector2(640, 360))
        batch.call("set_projectiles", points)
        check(batch.multimesh.visible_instance_count == 2048, "capped visual capacity")
        batch.call("clear_projectiles")
        check(batch.multimesh.visible_instance_count == 0, "clear visual instances")
    scene.queue_free()
    if failures == 0:
        print("P9 VISUAL HEADLESS SMOKE PASS (NOT PHYSICAL GPU)")
    quit(0 if failures == 0 else 1)
