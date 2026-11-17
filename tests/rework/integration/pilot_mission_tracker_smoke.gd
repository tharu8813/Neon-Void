extends SceneTree
## A view regression: display E-saved facts, NEVER claim volatile D preview clean.
const Tracker = preload("res://scripts/rework/ui/pilot_mission_tracker.gd")
const Scene = preload("res://scenes/rework/world/cpu_vertical_slice.tscn")
var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func check(ok: bool, message: String) -> void:
    if not ok:
        failures += 1
        printerr("REWORK A TRACKER FAIL: ", message)

func _run() -> void:
    var game: Node3D = Scene.instantiate()
    root.add_child(game)
    check(game.begin_preview_mission(42), "D seeded preview supplies five metadata plans")
    var run_id: String = str(game.get("current_run_id"))
    var tracker: PanelContainer = Tracker.new()
    root.add_child(tracker)
    await process_frame
    check(not tracker.visible, "not shown before E snapshot")
    var committed: Dictionary = {
        "ok": true, "revision": 12, "snapshot": {
            "vaccine": {"mission": {
                "run_id": run_id, "mission_level": 3,
                "hardware_cleaned_ids": ["GPU"],
                "objective_ids": ["GPU.disable_emitter"],
                "environment_rolls": {"e_checkpoint_v1": {
                    "regions": {"GPU": {"seed": 42}}}}
            }}
        }
    }
    tracker.call("show_saved_snapshot", committed)
    tracker.call("render_world", game)
    check(tracker.visible and tracker.get("is_verified"),
        "mission UI appears only on E-verified mission")
    var gpu: Label = tracker.get("region_labels")["GPU"]
    var ram: Label = tracker.get("region_labels")["RAM"]
    check(gpu.text.contains("정화 완료") and gpu.text.contains("저장됨"),
        "GPU clean label exclusively from E snapshot")
    check(ram.text.contains("미방문"), "other D regions remain unconfirmed")
    game.world_state.progress["RAM"]["cleared"] = true
    tracker.call("render_world", game)
    check(ram.text.contains("미방문"),
        "volatile D-only cleanse cannot falsely mark E confirmation")
    game.set("current_mode", "gpu")
    tracker.call("render_world", game)
    var current: Label = tracker.get("objective_label")
    check(tracker.get("title_label").text.contains("GPU")
        and current.text.contains("목표 1/2")
        and current.text.contains("철수"),
        "active zone shows objective receipts and E-confirmed extraction")
    var bad: Dictionary = committed.duplicate(true)
    bad["snapshot"]["vaccine"]["mission"]["run_id"] = "another-run"
    tracker.call("show_saved_snapshot", bad)
    tracker.call("render_world", game)
    check(tracker.get("title_label").text.contains("확인 필요")
        and gpu.text.contains("확인 필요"),
        "cross-run saved state must never show stale clear facts")
    tracker.call("show_saved_snapshot", {"ok": false, "reason": "corrupt"})
    check(not tracker.visible and not tracker.get("is_verified"),
        "unreadable saved mission hides all confident map progress")
    tracker.call("show_blocked")
    check(not tracker.visible, "safe halt disables tracker")
    root.remove_child(tracker)
    tracker.queue_free()
    root.remove_child(game)
    game.queue_free()
    await process_frame
    if failures == 0:
        print("REWORK A TRACKER PASS: E-confirmed five-zone UI, objectives, D-only preview rejection, wrong-run fail closed")
    quit(0 if failures == 0 else 1)
