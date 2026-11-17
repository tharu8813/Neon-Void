extends SceneTree
## Godot headless layout regression: A's UI remains in the viewport for 4:3,
## widescreen, ultrawide and constrained windowed sizes. No E/B writes.
const Tactical = preload("res://scripts/rework/ui/pilot_tactical_hud.gd")
const Tracker = preload("res://scripts/rework/ui/pilot_mission_tracker.gd")
const Result = preload("res://scripts/rework/ui/pilot_mission_result.gd")
const Status = preload("res://scripts/rework/ui/pilot_status_effects.gd")
const Hub = preload("res://scripts/rework/ui/hub_management_panel.gd")
var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func check(ok: bool, message: String) -> void:
    if not ok:
        failures += 1
        printerr("REWORK A RESPONSIVE FAIL: ", message)

func _within(node: Control, viewport: Vector2, label: String) -> void:
    check(node.position.x >= 0.0 and node.position.y >= 0.0
        and node.position.x + node.custom_minimum_size.x <= viewport.x + 1.0,
        label + ": anchored within viewport width")

func _run() -> void:
    var vp := SubViewport.new()
    vp.size = Vector2i(1280, 720)
    root.add_child(vp)
    var tactical: PanelContainer = Tactical.new()
    vp.add_child(tactical)
    var tracker: PanelContainer = Tracker.new()
    vp.add_child(tracker)
    var result: PanelContainer = Result.new()
    vp.add_child(result)
    var status: PanelContainer = Status.new()
    vp.add_child(status)
    var hub: PanelContainer = Hub.new()
    vp.add_child(hub)
    await process_frame
    # Test the populated 22-stat, plugin and skill tabs, not an empty shell.
    hub.show_saved({"ok": true, "revision": 8, "snapshot": {
        "selection_required": false, "balance": {},
        "vaccine": {
            "level": 4, "xp": 20, "copies_remaining": 2,
            "earned_fragments": 400, "unspent_fragments": 400,
            "invested_stats": {}, "known_skills": ["alpha", "beta"],
            "equipped_skills": ["alpha"],
            "archive": {"nv_c_move_speed": {"rarity": "common", "level": 2}},
            "carried": {}, "mission": null
        }
    }}, true)
    await process_frame
    for dimensions in [
        Vector2i(1280, 720), Vector2i(1024, 768),
        Vector2i(1920, 1080), Vector2i(800, 600)
    ]:
        vp.size = dimensions
        await process_frame
        var viewport: Vector2 = Vector2(dimensions)
        tactical.call("update_layout")
        tracker.call("update_layout")
        result.call("update_layout")
        status.call("update_layout")
        hub.call("_layout_to_viewport")
        _within(tactical, viewport, "tactical %s" % dimensions)
        _within(tracker, viewport, "tracker %s" % dimensions)
        _within(result, viewport, "result %s" % dimensions)
        _within(status, viewport, "status %s" % dimensions)
        _within(hub, viewport, "hub %s" % dimensions)
        var children: Array[String] = []
        var layout: Control = hub.get_child(0) as Control
        if layout != null:
            for child in layout.get_children():
                var control: Control = child as Control
                if control != null:
                    children.append("%s:%s" % [control.name,
                        control.get_combined_minimum_size()])
        check(hub.position.y + hub.size.y <= viewport.y + 1.0,
            "hub vertically centered at %s, pos=%s size=%s min=%s custom=%s layout_min=%s tabs_min=%s children=%s" %
            [dimensions, hub.position, hub.size, hub.get_combined_minimum_size(),
                hub.custom_minimum_size, layout.get_combined_minimum_size(),
                hub.get("tabs").get_combined_minimum_size(), children])
        if dimensions.x < 1100:
            check(tracker.position.y >= 160.0,
                "narrow mission tracker moves below top tactical bars")
            check(status.position.x + status.custom_minimum_size.x
                < tracker.position.x,
                "bottom status and tracker remain in separate columns")
        var health: ProgressBar = tactical.get("health_meter") as ProgressBar
        check(health.custom_minimum_size.x <= tactical.custom_minimum_size.x / 3.0,
            "responsive HP meter avoids fixed 175px overflow")
    var B: Dictionary = {
        "stats": {"max_health": 100.0, "health": 51.0,
            "attack_damage": 7.25, "crit_chance": 0.2},
        "mana": 44.0
    }
    hub.call("show_live_combat", B)
    var before: int = int(hub.get("combat_render_count"))
    hub.call("show_live_combat", B.duplicate(true))
    check(int(hub.get("combat_render_count")) == before,
        "unchanged B stats do not destroy and recreate Control rows")
    var changed: Dictionary = B.duplicate(true)
    changed["mana"] = 40.0
    hub.call("show_live_combat", changed)
    check(int(hub.get("combat_render_count")) == before + 1,
        "changed B snapshot triggers exactly one updated view")
    root.remove_child(vp)
    vp.queue_free()
    await process_frame
    if failures == 0:
        print("REWORK A RESPONSIVE PASS: four viewport sizes, bounded layouts, memoized B detail rendering")
    quit(0 if failures == 0 else 1)
