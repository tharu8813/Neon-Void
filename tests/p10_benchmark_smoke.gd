extends SceneTree
## Arithmetic and scene smoke; headless does not measure display FPS/GPU.
const Stats = preload("res://benchmarks/frame_stats.gd")
const Benchmark = preload("res://benchmarks/p10_benchmark.tscn")
var errors: int = 0

func check(ok: bool, message: String) -> void:
    if not ok:
        errors += 1
        printerr("P10 FAIL: " + message)

func _initialize() -> void:
    call_deferred("_test")

func _test() -> void:
    var empty = Stats.new()
    check(not empty.summary().get("ok", true), "empty rejected")
    var v = Stats.new(5)
    check(not v.add_ms(-1.0) and not v.add_ms(0.0) and not v.add_ms(INF), "invalid intervals")
    for x in [10.0, 20.0, 30.0, 40.0, 50.0]:
        check(v.add_ms(x), "accept sample")
    check(not v.add_ms(60.0), "bounded samples")
    var s: Dictionary = v.summary()
    check(s.get("count") == 5, "count")
    check(is_equal_approx(float(s.get("mean_ms", -1.0)), 30.0), "mean")
    check(is_equal_approx(float(s.get("p50_ms", -1.0)), 30.0), "p50")
    check(is_equal_approx(float(s.get("p95_ms", -1.0)), 50.0), "p95 nearest rank")
    check(is_equal_approx(float(s.get("p99_ms", -1.0)), 50.0), "p99 nearest rank")
    var root_scene: Node = Benchmark.instantiate()
    check(root_scene != null, "benchmark scene loads")
    if root_scene != null:
        root.add_child(root_scene)
        check(root_scene.get_node_or_null("ExistingGodotScene") != null, "original P1 scene intact")
        var observer: Node = root_scene.get_node_or_null("FrameProbe")
        check(observer != null, "observer is separate")
        if observer != null:
            check(not observer.is_processing(), "no simulated headless FPS")
        root_scene.queue_free()
    if errors == 0:
        print("P10 HEADLESS ARITHMETIC/SCENE PASS (NOT PERFORMANCE EVIDENCE)")
    quit(0 if errors == 0 else 1)
