extends SceneTree
## Read-only result UI: closed-run outcome must match verified E snapshot.
const Result = preload("res://scripts/rework/ui/pilot_mission_result.gd")
var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func check(ok: bool, message: String) -> void:
    if not ok:
        failures += 1
        printerr("REWORK A RESULT FAIL: ", message)

func _fact(kind: String, run_id: String, cleaned: int) -> Dictionary:
    return {
        "schema": 1, "type": kind, "run_id": run_id,
        "outcome": "success" if kind == "mission_extracted" else "death",
        "receipt_id": "world:" + run_id + ":terminal",
        "cleared_count": cleaned, "full_clear": cleaned == 5
    }

func _snapshot(run_id: String, state: String) -> Dictionary:
    return {"ok": true, "revision": 19, "snapshot": {
        "selection_required": false,
        "vaccine": {
            "level": 5, "xp": 80, "copies_remaining": 2,
            "mission": null, "closed_runs": {run_id: state}
        }
    }}

func _run() -> void:
    var panel: PanelContainer = Result.new()
    root.add_child(panel)
    await process_frame
    var success: Dictionary = _fact("mission_extracted", "run-ui", 3)
    var loaded: Dictionary = _snapshot("run-ui", "extracted")
    var source: Dictionary = loaded.duplicate(true)
    check(panel.show_verified(success, loaded) and panel.visible,
        "saved extraction opens results only after E closed-run confirmation")
    check(panel.get("title_label").text.contains("성공")
        and panel.get("result_label").text.contains("3 / 5")
        and panel.get("receipt_label").text.contains("r19"),
        "partial extraction displays only verified completed count and revision")
    check(loaded == source, "result UI cannot edit a saved-state snapshot")
    check(not panel.show_verified(success, _snapshot("other-run", "extracted"))
        and not panel.visible, "mismatched run never displays E success")
    check(not panel.show_verified(success, _snapshot("run-ui", "dead"))
        and not panel.visible, "wrong terminal outcome fails closed")
    var five: Dictionary = _fact("mission_extracted", "run-ui", 5)
    check(panel.show_verified(five, loaded)
        and panel.get("result_label").text.contains("전체 정화"),
        "five confirmed cleanses show full clear")
    five["full_clear"] = false
    check(not panel.show_verified(five, loaded),
        "full clear claim inconsistent with count rejected")
    var death: Dictionary = _fact("mission_failed", "run-ui", 1)
    check(panel.show_verified(death, _snapshot("run-ui", "dead"))
        and panel.get("title_label").text.contains("손실"),
        "clone loss uses E dead receipt")
    var terminal: Dictionary = {"ok": true, "revision": 22,
        "snapshot": {"selection_required": true, "vaccine": null}}
    check(panel.show_verified(death, terminal)
        and panel.get("progression_label").text.contains("최종 파기"),
        "fourth death displays vaccine final destruction, not extra clones")
    check(not panel.show_verified(success, terminal),
        "destroyed vaccine cannot be shown as successful extraction")
    check(not panel.show_verified(death, {"ok": false, "reason": "missing"}),
        "unreadable E terminal cannot show success or death")
    panel.show_verified(_fact("mission_failed", "run-ui", 0), terminal)
    var dismisses: Array = []
    panel.dismissed.connect(func() -> void: dismisses.append(true))
    panel.call("_dismiss")
    check(not panel.visible and dismisses.size() == 1,
        "dismiss only closes result overlay; never changes save state")
    root.remove_child(panel)
    panel.queue_free()
    await process_frame
    if failures == 0:
        print("REWORK A RESULT PASS: E-verified extraction/death/final destruction, replay/run mismatch rejection")
    quit(0 if failures == 0 else 1)
