extends SceneTree
## Headless visual structure: exactly matches Python procedural allocations;
## cannot substitute for a real Windows 3D screenshot or GPU benchmark.
const Main = preload("res://scenes/main.tscn")
var failures: int = 0

func _initialize() -> void:
    create_timer(55.0).timeout.connect(func(): printerr("P9 ATMOSPHERE TIMEOUT"); quit(1))
    call_deferred("_test")

func verify(ok: bool, reason: String) -> void:
    if not ok:
        failures += 1
        printerr("P9 ATMOSPHERE FAIL: " + reason)

func _test() -> void:
    var game: Node3D = Main.instantiate()
    game.set("persistence_enabled", false)
    root.add_child(game)
    var atmosphere: Node3D = game.get_node("LegacyAtmosphere")
    var echoes: Node3D = game.get_node("LegacyPlayerEcho")
    var sparks: Node3D = game.get_node("LegacySparks")
    var shell: CanvasLayer = game.get_node("UIShell")
    var screen: Control = shell._screen_fx
    verify(atmosphere != null and echoes != null and sparks != null,
        "original source procedural scene and player FX are present")
    verify(atmosphere.arena_stars.multimesh.instance_count == 52,
        "exact original arena star count")
    verify(atmosphere.hub_dust.multimesh.instance_count == 34,
        "exact original hub dust count")
    verify(atmosphere.arena_grid.multimesh.instance_count == 28,
        "source 80px 1280x720 arena grid")
    verify(not atmosphere.arena_grid.visible and atmosphere.hub_dust.visible,
        "default antivirus base has animated dust, not combat grid")
    verify(atmosphere.core_halo.visible and atmosphere.link_pulses[0].visible,
        "original breathing hub core and conduit packets")
    verify(echoes.pool.size() == 14 and sparks.cloud.multimesh.instance_count == 380,
        "visual pools stay bounded instead of creating per-frame mesh nodes")
    verify(screen != null and screen.mouse_filter == Control.MOUSE_FILTER_IGNORE,
        "waves/flash overlay never absorbs click or aim input")
    verify(shell._menu.get_index() > screen.get_index() and
        shell._cursor.get_index() > screen.get_index(),
        "3D feedback appears below menus and original custom cursor")

    verify(game.call("start_new_run") and game.call("enter_sector", "CPU"),
        "start real C wave 1 from in-world OS deployment")
    verify(atmosphere.arena_stars.visible and atmosphere.arena_grid.visible
        and atmosphere.scanline.visible and not atmosphere.hub_dust.visible,
        "source starfield/grid/scan is re-enabled only in combat")
    verify(screen.banner_time > 0.0 and screen.wave_title.text == "WAVE 1",
        "original neon wave banner is queued by true C stage start")
    verify(screen.sweep_time > 0.0, "wave title carries original screen sweep")

    var score_before: int = int(game.get("score"))
    var rng_before: int = int(game.get("sim").rng.state)
    game.call("_on_projectile_visual", "critical",
        Vector2(360, 350), Vector2(380, 350), 20.0)
    verify(sparks.particles.size() == 10, "real critical event spawns ten source-color sparks")
    verify(screen.flash_time > 0.0 and screen.flash_color == Color("#fff0a6"),
        "critical hit triggers short gold flash")
    verify(int(game.get("score")) == score_before and
        int(game.get("sim").rng.state) == rng_before,
        "extra feedback never changes combat RNG or persisted score")
    sparks.call("_process", 0.7)
    verify(sparks.particles.is_empty() and
        sparks.cloud.multimesh.visible_instance_count == 0,
        "all sparks expire instead of leaking 3D nodes")

    var actor_mesh: Mesh = game.get_node("PlayerVisual").mesh
    verify(echoes.pool[0].mesh == actor_mesh,
        "dash ghosts reuse original player silhouette mesh")
    for tick in range(7):
        echoes.call("sync_echo", Vector3(3.5 + tick * 0.08, 0.13, 3.6),
            0.0, true, false, 1.0 / 60.0)
    var echo_shown: int = 0
    for ghost in echoes.pool:
        if ghost.visible:
            echo_shown += 1
    verify(echo_shown > 0, "dashing visibly trails reusable 3D silhouettes")
    echoes.call("sync_echo", Vector3(5.0, 0.13, 3.6), 0.0, true, true, 1.0 / 60.0)
    verify(not echoes.pool[0].visible,
        "reduced effects immediately hides afterimages")
    atmosphere.call("set_zone", "combat", true)
    verify(not atmosphere.scanline.visible and
        atmosphere.arena_stars.visible and atmosphere.arena_grid.visible,
        "reduced effects removes motion, not the readable arena geometry")
    screen.call("set_reduced_effects", true)
    screen.call("trigger_flash", Color.RED, 0.4)
    verify(is_equal_approx(float(screen.flash_time), 0.0),
        "reduced effects suppresses screen flashes")
    sparks.call("set_reduced_effects", true)
    sparks.call("emit_sparks", Vector2(100, 100), "boss")
    verify(sparks.particles.is_empty(), "reduced effects suppresses cosmetic sparks")

    game.call("retreat_to_hub")
    verify(atmosphere.hub_dust.visible and not atmosphere.arena_grid.visible,
        "hub re-entry restores different world atmosphere")
    verify(not echoes.pool[0].visible and sparks.particles.is_empty(),
        "retreat clears dash afterimages and combat burst state")
    game.queue_free()
    if failures == 0:
        print("P9 PYTHON ORIGINAL ATMOSPHERE AND ANIMATION PARITY PASS (headless)")
    quit(0 if failures == 0 else 1)
