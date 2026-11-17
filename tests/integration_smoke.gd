extends SceneTree
## P11 integration contract. Drives the verified wave-completion callback without
## simulating kills; C's own combat tests cover damage, spawns and 60Hz combat.
var failures: int = 0

func _initialize() -> void:
    create_timer(50.0).timeout.connect(func(): printerr("P11 TIMEOUT"); quit(1))
    call_deferred("_run")

func verify(condition: bool, description: String) -> void:
    if not condition:
        failures += 1
        printerr("P11 FAIL: " + description)

func _run() -> void:
    var scene: PackedScene = load("res://scenes/main.tscn") as PackedScene
    verify(scene != null, "A main scene loads")
    if scene == null:
        quit(1)
        return
    var game: Node3D = scene.instantiate() as Node3D
    verify(game != null, "A main is 3D")
    if game == null:
        quit(1)
        return
    root.add_child(game)
    verify(game.get_node_or_null("LegacyPlayer") != null, "B player in main")
    verify(game.get_node_or_null("InputController") != null, "B input in main")
    verify(game.get_node_or_null("ProjectileBatch") != null, "P9 GPU instancing in main")
    verify(game.get_node_or_null("UIShell") != null, "D HUD and menus in main")
    verify(str(game.get("mode")) == "base", "Python default launch enters walkable antivirus base")
    verify(not bool(game.get("paused")), "base begins unpaused")
    verify(not game.get_node("UIShell")._menu.visible, "no mandatory title menu at boot")
    verify(game.call("nearest_base_terminal", Vector2(1200, 780)) == "", "F ignores distant terminals")
    var actor: Node3D = game.get_node("LegacyPlayer")
    var base_nodes: Dictionary = {
        "deploy": [Vector2(1580, 780), "deployment"],
        "database": [Vector2(820, 610), "database"],
        "diagnostics": [Vector2(820, 1000), "profile"],
        "settings": [Vector2(1540, 1030), "settings"],
    }
    for id in base_nodes:
        var data: Array = base_nodes[id]
        actor.call("set_logical_position", data[0])
        verify(game.call("nearest_base_terminal", data[0]) == id, "same Python terminal coordinates: " + id)
        game.call("_on_interact")
        verify(str(game.get_node("UIShell")._menu.current) == str(data[1]), "F opens " + id)
        verify(bool(game.get("paused")), "terminal interaction pauses only the menu")
        game.get_node("UIShell")._menu.close_screen()
        verify(not bool(game.get("paused")), "closing terminal resumes walking: " + id)
    verify(game.get_node("GateVisuals/AntivirusBaseTerminals").visible, "base markers visible before OS deployment")
    verify(not game.get_node("GateVisuals/OSStations").visible, "OS gates hidden in antivirus base")
    var base_decor: Node = game.get_node("GateVisuals/AntivirusBaseTerminals/HubDecoration")
    verify(base_decor.get_node("Grid").get_child_count() >= 30, "120px hub grid built as 3D mesh")
    verify(base_decor.get_node("CentralCore").get_child_count() > 10, "concentric neon core geometry")
    verify(base_decor.get_node("Pillars").get_child_count() == 4, "exact four frozen collision pillars rendered")
    verify(base_decor.get_node("Conduits").get_child_count() == 4, "all four base terminals linked to core")
    var os_decor: Node = game.get_node("GateVisuals/OSStations/HubDecoration")
    verify(os_decor.get_node("Conduits").get_child_count() >= 4, "OS gate conduits retained")
    actor.call("set_logical_position", Vector2(1580, 780))
    game.call("_on_interact")
    verify(str(game.get_node("UIShell")._menu.current) == "deployment", "deploy only through in-world terminal")
    verify(bool(game.call("start_new_run")), "new OS launch")
    verify(str(game.get("mode")) == "hub", "starts inside OS world hub")
    verify(game.get_node("GateVisuals/OSStations").visible, "OS gates appear after deployment")
    verify(not game.get_node("GateVisuals/AntivirusBaseTerminals").visible, "base terminals hidden inside OS")

    var sectors: Array[String] = ["NETWORK", "CPU", "STORAGE", "MEMORY"]
    for sector_id in sectors:
        verify(bool(game.call("enter_sector", sector_id)), "enter " + sector_id)
        if sector_id == "NETWORK":
            # P1 presentation-only hover/telegraph flags must be grounded in
            # real combat state, without changing C's auto-targeting rules.
            var combat: RefCounted = game.get("sim")
            combat.enemies.clear()
            var hostile: int = combat.spawn_enemy("rootkit", Vector2(640, 360))
            var inputs: Node = game.get_node("InputController")
            inputs.call("set_pointer_logical", Vector2(640, 360))
            var snap: Dictionary = game.call("_hud_snapshot")
            verify(hostile > 0 and bool(snap.target_locked), "enemy under cursor lights aim reticle")
            combat.telegraphs.append({"remaining": 0.18, "source_id": hostile, "action": {}})
            snap = game.call("_hud_snapshot")
            verify(int(snap.attack_warning_count) == 1 and bool(snap.attack_warning_urgent),
                "nonboss imminent attack is announced in static HUD text")
            inputs.call("set_pointer_logical", Vector2(120, 120))
            snap = game.call("_hud_snapshot")
            verify(not bool(snap.target_locked), "reticle returns neutral off-target")
            combat.enemies.clear()
            combat.telegraphs.clear()
        for wave_index in range(1, 6):
            var campaign = game.get("campaign")
            verify(int(campaign.active_wave) == wave_index, "correct wave " + sector_id + str(wave_index))
            var sim = game.get("sim")
            sim.enemies.clear()
            sim.spawn_queue.clear()
            game.call("_on_wave_ready", wave_index == 5)
            verify(int(campaign.progress[sector_id]) == wave_index, "wave receipt " + sector_id + str(wave_index))
            verify(str(game.get("mode")) == "reward", "reward state after wave")
            verify(game.get_node("UIShell")._offer.visible, "D card selector visible")
            game.call("_on_card_selected", 0)
            verify(str(game.get("mode")) == ("clean" if wave_index == 5 else "combat"),
                "Python pick_upgrade automatically enters the next C wave")
            if wave_index < 5:
                verify(int(campaign.active_wave) == wave_index + 1,
                    "next wave begins automatically only after reward choice")
        game.call("retreat_to_hub")
        verify(str(game.get("mode")) == "hub", "return hub")
    verify(game.get("campaign").is_os_clean(), "all sectors clean in any order")
    verify(not game.call("enter_sector", "CPU"), "cannot rerun clean sector")
    verify(game.get("software").levels.size() > 0, "D software rewards acquired")
    verify(game.get_node("PlayerVisual").mesh != null, "3D player visual preserved")
    verify(game.get_node("Camera3D").projection == Camera3D.PROJECTION_ORTHOGONAL, "top-down camera")
    game.queue_free()
    if failures == 0:
        print("P11 MAIN GAMEPLAY INTEGRATION PASS (synthetic wave-ready; not human visual QA)")
    quit(1 if failures > 0 else 0)
