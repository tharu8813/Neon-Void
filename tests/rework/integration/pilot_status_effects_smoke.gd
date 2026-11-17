extends SceneTree
## A UI reads B *public* 60 Hz status_state snapshots, never mutates them.
const B = preload("res://scripts/rework/combat/combat_runtime.gd")
const Statuses = preload("res://scripts/rework/combat/statuses.gd")
const View = preload("res://scripts/rework/ui/pilot_status_effects.gd")
var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func check(ok: bool, description: String) -> void:
    if not ok:
        failures += 1
        printerr("REWORK A STATUS HUD FAIL: ", description)

func _run() -> void:
    var view: PanelContainer = View.new()
    root.add_child(view)
    await process_frame
    view.render_actor({})
    check(not view.visible, "unavailable B actor never shows fake debuff")
    var combat = B.new(7452)
    check(combat.register_actor("VACCINE", "vaccine", Vector3.ZERO, {},
        {"player_controlled": true}), "real B actor registered")
    var state: Dictionary = combat.actors["VACCINE"]["status_state"]
    check(Statuses.apply(state, {"id": "burn", "duration_ticks": 180,
        "dot_dps": 6.0, "stack_mode": "stack", "max_stacks": 5}).get("ok", false),
        "real B burn status applied")
    check(Statuses.apply(state, {"id": "burn", "duration_ticks": 180,
        "dot_dps": 6.0, "stack_mode": "stack", "max_stacks": 5}).get("ok", false),
        "real B burn stacks")
    check(Statuses.apply(state, {"id": "root", "duration_ticks": 120,
        "control": true, "flags": ["rooted"]}).get("ok", false),
        "real B rooted control applied")
    var snapshot: Dictionary = combat.snapshot("VACCINE")
    var frozen: Dictionary = snapshot.duplicate(true)
    view.render_actor(snapshot)
    var entries: Array = view.get("_effects")
    var rows: Array = view.get("lines")
    check(view.visible and entries.size() == 2, "exact B active statuses displayed")
    check(rows[0].text.contains("burn") and rows[0].text.contains("×2")
        and rows[0].text.contains("3.0초")
        and rows[0].text.contains("지속 피해"),
        "B stack count, 60 Hz time and DoT kind are exact")
    check(rows[1].text.contains("root") and rows[1].text.contains("이동 불가")
        and rows[1].text.contains("2.0초"),
        "B root flag and remaining duration localized")
    check(snapshot == frozen, "rendering leaves B public snapshot untouched")
    var rendered: String = rows[0].text
    view.render_actor(snapshot)
    check(rows[0].text == rendered, "repeated polling is idempotent")
    var after_tick: Dictionary = combat.snapshot("VACCINE")
    after_tick["status_state"]["statuses"][0]["remaining_ticks"] = 60
    view.render_actor(after_tick)
    check(rows[0].text.contains("1.0초")
        and snapshot["status_state"]["statuses"][0]["remaining_ticks"] == 180,
        "new read-only B snapshot changes only timer text")
    var full: Dictionary = {"status_state": {"statuses": []}}
    for index in range(8):
        full["status_state"]["statuses"].append({
            "id": "effect_%d" % index, "remaining_ticks": 120, "stacks": 1})
    view.render_actor(full)
    check(view.visible and view.summary.text.contains("8개")
        and view.summary.text.contains("처음 5개")
        and rows[4].visible, "long effect lists stay bounded")
    view.render_actor({"status_state": {"statuses": [
        {"id": "expired", "remaining_ticks": 0, "stacks": 3}]}})
    check(not view.visible, "expired effects disappear without inferred rewards")
    view.clear_view()
    check(not view.visible and view.get("_effects").is_empty(),
        "fatal E stop suppresses stale combat effects")
    root.remove_child(view)
    view.queue_free()
    await process_frame
    if failures == 0:
        print("REWORK A STATUS HUD PASS: real B snapshot, stacks, time, control, no mutations, bounded rows")
    quit(0 if failures == 0 else 1)
