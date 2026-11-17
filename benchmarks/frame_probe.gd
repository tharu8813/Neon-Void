extends Node
## Explicitly added to a benchmark-only scene. Headless does not write reports.
## No player data, legacy filesystem access, simulation state or RNG.
const Stats = preload("res://benchmarks/frame_stats.gd")
@export var warmup_seconds: float = 5.0
@export var record_seconds: float = 30.0
@export var test_label: String = "P1 primitive scene: NOT gameplay"
var elapsed: float = 0.0
var measured: float = 0.0
var finished: bool = false
var stats: RefCounted

func _ready() -> void:
    stats = Stats.new(12000)
    if DisplayServer.get_name().to_lower() == "headless":
        set_process(false)
    else:
        print("P10 probe: real display frame intervals only; GPU usage is not measured.")

func _process(delta: float) -> void:
    if finished:
        return
    elapsed += delta
    if elapsed < warmup_seconds:
        return
    measured += delta
    stats.add_ms(delta * 1000.0)
    if measured >= record_seconds or stats.count() >= 12000:
        finished = true
        set_process(false)
        _export()

func _export() -> void:
    var data: Dictionary = stats.summary()
    if not data.get("ok", false):
        printerr("P10: no valid real-display frames")
        return
    var size: Vector2i = DisplayServer.window_get_size()
    var result: Dictionary = {
        "schema": 1, "kind": "non_headless_display_observation",
        "scene": test_label, "epoch_utc": int(Time.get_unix_time_from_system()),
        "godot": str(Engine.get_version_info().get("string", "unknown")),
        "os": OS.get_name(), "display_server": DisplayServer.get_name(),
        "configured_renderer_not_verified_backend": str(ProjectSettings.get_setting("rendering/renderer/rendering_method", "unknown")),
        "client_width": size.x, "client_height": size.y,
        "warmup_seconds": warmup_seconds, "sampled_seconds": measured,
        "frame_intervals": data,
        "real_gpu_and_driver": "MANUAL_EVIDENCE_REQUIRED",
        "windows_dpi": "MANUAL_EVIDENCE_REQUIRED",
        "gpu_utilization": "NOT_MEASURED",
        "gameplay_seed_and_stage": "MANUAL_EVIDENCE_REQUIRED"
    }
    var directory: String = "user://neonvoid_p10_reports"
    if DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(directory)) != OK:
        printerr("P10: cannot create report directory")
        return
    var target: String = directory.path_join("frame_" + str(Time.get_ticks_usec()) + ".json")
    var handle: FileAccess = FileAccess.open(target, FileAccess.WRITE)
    if handle == null:
        printerr("P10: cannot open report file")
        return
    handle.store_string(JSON.stringify(result, "  "))
    handle.flush()
    handle.close()
    print("P10 benchmark saved: ", ProjectSettings.globalize_path(target))
