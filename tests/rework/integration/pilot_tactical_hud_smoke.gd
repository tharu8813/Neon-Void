extends SceneTree
## A read-only HUD contract: synthetic E/B snapshots, no slot reads or writes.
const HUD = preload("res://scripts/rework/ui/pilot_tactical_hud.gd")
var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func check(ok: bool, description: String) -> void:
    if not ok:
        failures += 1
        printerr("REWORK A HUD FAIL: " + description)

func _run() -> void:
    var hud: PanelContainer = HUD.new()
    root.add_child(hud)
    await process_frame
    hud.render_saved_snapshot({
        "ok": true, "revision": 11,
        "snapshot": {"selection_required": false, "vaccine": {
            "level": 4, "copies_remaining": 2, "xp": 40,
            "unspent_fragments": 3, "archive": {"test": {}}, "carried": {},
            "mission": null
        }}
    })
    check(hud.vaccine_label.text.contains("Lv.4")
        and hud.vaccine_label.text.contains("2/3"),
        "lifetime level and remaining clones come from E")
    check(hud.mission_label.text.contains("허브")
        and hud.save_label.text.contains("r11"),
        "hub and E verified revision shown")
    hud.render_combat_snapshot({})
    check(hud.health_label.text.contains("--")
        and hud.shield_label.text.contains("--")
        and hud.mana_label.text.contains("--"),
        "absent B actor is never displayed as zero HP or zero shield")
    hud.render_combat_snapshot({
        "stats": {"health": 62.0, "max_health": 100.0,
            "max_mana": 80.0}, "mana": 23.0
    })
    check(hud.health_meter.visible and int(hud.health_meter.value) == 62
        and hud.mana_meter.visible and int(hud.mana_meter.value) == 23
        and not hud.shield_meter.visible,
        "B resource bars are grounded; shield stays unknown without public B field")
    hud.render_combat_snapshot({
        "stats": {"health": 38.0, "max_health": 100.0,
            "shield": 12.0, "max_mana": 80.0}, "mana": 90.0
    })
    check(hud.shield_meter.visible and int(hud.shield_meter.value) == 12
        and int(hud.mana_meter.value) == 80,
        "public shield field used when available; resource value is clamped")
    hud.render_saved_snapshot({
        "ok": true, "revision": 13,
        "snapshot": {"selection_required": false, "vaccine": {
            "level": 4, "copies_remaining": 2, "unspent_fragments": 3,
            "mission": {"mission_level": 3, "mission_xp": 47,
                "hardware_cleaned_ids": ["CPU", "GPU"],
                "pending_plugin_offers": 1, "rerolls_left": 2}
        }}
    })
    check(hud.mission_label.text.contains("Lv.3")
        and hud.save_label.text.contains("2/5")
        and hud.save_label.text.contains("보상 대기 1"),
        "mission XP, cleanses and pending offers taken only from E")
    hud.render_saved_snapshot({
        "ok": true, "revision": 14,
        "snapshot": {"selection_required": true, "vaccine": null}
    })
    check(hud.vaccine_label.text.contains("최종 파기")
        and hud.mission_label.text.contains("출전 불가"),
        "terminal generation requires explicit vaccine selection")
    hud.render_saved_snapshot({"ok": false, "reason": "corrupt"})
    check(hud.save_label.text.contains("미검증"),
        "unreadable E slot is not presented as a valid save")
    hud.show_blocked()
    check(hud.blocked and hud.mission_label.text.contains("안전 정지")
        and hud.save_label.text.contains("자동 재개"),
        "halt UX cannot advertise resume or regrant")
    root.remove_child(hud)
    hud.queue_free()
    await process_frame
    if failures == 0:
        print("REWORK A TACTICAL HUD PASS: read-only E/B data, unknown shield, safe halt")
    quit(1 if failures > 0 else 0)
