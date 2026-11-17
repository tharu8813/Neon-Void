extends SceneTree
## Final pre-Windows P0 parity guards: actual functional F/EXIT, safe purification,
## global shortcuts, procedural audiovisual UI and pooled read-only damage FX.
## No real save data, no native speaker/GPU/display claim.
const Main = preload("res://scenes/main.tscn")
const LegacyExit = preload("res://scripts/visuals/legacy_sector_exit.gd")
var failures: int = 0

func _initialize() -> void:
    create_timer(55.0).timeout.connect(func(): printerr("P0 CODE PARITY TIMEOUT"); quit(1))
    call_deferred("_run")

func expect(ok: bool, reason: String) -> void:
    if not ok:
        failures += 1
        printerr("P0 CODE PARITY FAIL: " + reason)

func _run() -> void:
    var game: Node3D = Main.instantiate()
    game.set("persistence_enabled", false)
    root.add_child(game)
    var actor: Node3D = game.get_node("LegacyPlayer")
    var fx: Variant = game.get_node("CombatFX")
    var ui: Variant = game.get_node("UIShell")
    var gate: Variant = game.get_node("LegacySectorExit")
    expect(not gate.visible, "source Python EXIT hidden inside antivirus base")
    game.call("_on_system_pressed", "m")
    expect(bool(game.get("saved_state")["settings.json"]["muted"]) and bool(game.get("legacy_audio").muted),
        "M toggles settings and actual audio facade mute")
    game.call("_on_system_pressed", "m")
    expect(not bool(game.get("saved_state")["settings.json"]["muted"]), "M unmutes")
    game.call("_on_system_pressed", "f11")
    expect(str(game.get("saved_state")["settings.json"]["display_mode"]) == "fullscreen",
        "F11 enters original fullscreen")
    game.call("_on_system_pressed", "f11")
    expect(str(game.get("saved_state")["settings.json"]["display_mode"]) == "windowed",
        "F11 toggles back")
    game.call("_on_system_pressed", "borderless")
    expect(str(game.get("saved_state")["settings.json"]["display_mode"]) == "borderless",
        "Alt+Enter uses original borderless toggle")

    expect(LegacyExit.EXIT_POSITION == Vector2(100.0, 360.0),
        "Python world/session.py CPU_EXIT point")
    expect(LegacyExit.can_interact(Vector2(100.0, 360.0)), "EXIT center in range")
    expect(LegacyExit.can_interact(Vector2(211.9, 360.0)), "EXIT strict radius inside")
    expect(not LegacyExit.can_interact(Vector2(212.0, 360.0)),
        "EXIT strict <112 threshold")
    expect(not LegacyExit.can_interact(Vector2(640.0, 360.0)),
        "EXIT unreachable from combat center")

    expect(bool(game.call("start_new_run")), "start OS in synthetic save-only fixture")
    expect(bool(game.call("enter_sector", "CPU")), "enter first sector")
    expect(gate.visible and str(game.get("mode")) == "combat",
        "3D EXIT is present during actual C combat")
    gate.call("sync_gate", Vector2(100, 360), "CPU CORE", false, "f", false, 1.0 / 60.0)
    expect(bool(gate.near_exit) and gate.prompt.visible and gate.scan.visible,
        "nearby F prompt and moving scan in normal visual mode")
    gate.call("sync_gate", Vector2(600, 360), "CPU CORE", false, "f", true, 1.0 / 60.0)
    expect(not gate.near_exit and not gate.prompt.visible and not gate.scan.visible,
        "distant player hides F prompt; reduced visuals disable cosmetic scan")

    actor.call("set_logical_position", Vector2(700, 360))
    game.call("_on_interact")
    expect(str(game.get("mode")) == "combat", "distant F cannot abandon active wave")
    actor.call("set_logical_position", Vector2(100, 360))
    game.call("_on_interact")
    expect(str(game.get("mode")) == "hub" and not gate.visible,
        "near F retreats safely to OS hub, hides 3D EXIT")
    expect(fx.visible and fx.friendly_trails.multimesh.visible_instance_count == 0,
        "OS hub can show purification pulse but not combat bullets")

    var campaign: Variant = game.get("campaign")
    game.set("purify_uses", 3)
    campaign.infection = 0.0
    campaign.integrity = 100.0
    expect(not game.call("purify") and int(game.get("purify_uses")) == 3,
        "zero-benefit purification preserves all charges")
    campaign.infection = 50.0
    campaign.integrity = 90.0
    expect(bool(game.call("purify")), "beneficial purification executes")
    expect(int(game.get("purify_uses")) == 2 and is_equal_approx(float(campaign.infection), 18.0)
        and is_equal_approx(float(campaign.integrity), 99.0),
        "original 32-point infection heal, 9-point integrity repair")
    expect(fx.bursts.size() == 1, "successful purification emits hub feedback ring")

    expect(bool(game.call("enter_sector", "MEMORY")), "second sector accepts C combat")
    var sim: Variant = game.get("sim")
    var id: int = sim.spawn_enemy("rootkit", Vector2(700.0, 340.0))
    game.call("_update_visuals")
    var before: float = float(sim.get_enemy(id)["hp"])
    var actual: float = sim.damage_enemy(id, 5.0)
    game.call("_update_visuals")
    expect(float(sim.get_enemy(id)["hp"]) < before and actual > 0.0,
        "C damage retains its authoritative mitigated value")
    expect(fx.damage_labels.size() == 1 and fx.damage_pool[0].visible,
        "FText-like read-only damage popup emitted for observed hit")

    fx.call("clear_battle")
    fx.call("add_burst", Vector2(120.0, 200.0), "dash")
    fx.call("add_burst", Vector2(940.0, 510.0), "kill")
    expect(fx.bursts.size() == 2, "two pooled rings allocated")
    var oldest: Dictionary = fx.bursts[0]
    oldest["elapsed"] = float(oldest["duration"]) - 0.01
    fx.call("sync_battle", [], [], [], [], false)
    expect(fx.bursts.size() == 1 and fx.burst_pool[0].visible,
        "expired ring removed while surviving ring remains visible")
    expect(fx.burst_pool[0].position.distance_to(Vector3(9.4, 0.055, 5.1)) < 0.001,
        "recycled pooled ring retains second original position (no teleport)")

    var snapshot: Dictionary = game.call("_hud_snapshot")
    snapshot["status"] = "combat"
    snapshot["hp"] = 20.0
    snapshot["maxhp"] = 100.0
    snapshot["combo"] = 3
    snapshot["combo_window"] = 2.0
    snapshot["settings"] = {"reduced_effects": false, "muted": true}
    snapshot["boss_hp"] = 42.0
    snapshot["boss_maxhp"] = 100.0
    snapshot["boss_warning"] = "공격 발동 임박"
    ui.call("update_game_state", snapshot)
    var hud: Variant = ui._hud
    expect(hud._combo_text.visible and hud._combo_text.text.contains("3 COMBO"),
        "source combo overlay is cosmetic and never multiplies C score")
    expect(hud._danger_sides[0].visible, "HP <35% red edge warning")
    expect(hud._audio_status.visible, "muted status visible across UI")
    expect(hud._boss_warning.visible and hud._boss_warning.text == "공격 발동 임박",
        "C telegraphed boss warning is never silently hidden")
    snapshot["settings"] = {"reduced_effects": true, "muted": false}
    ui.call("update_game_state", snapshot)
    expect(not hud._danger_sides[0].visible and not hud._audio_status.visible,
        "reduced motion hides purely cosmetic danger motion, M status updates")

    game.queue_free()
    if failures == 0:
        print("P0 FINAL CODE PARITY PASS (Headless, physical Windows QA pending)")
    quit(0 if failures == 0 else 1)
