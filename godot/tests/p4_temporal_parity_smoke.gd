extends SceneTree
## Python game.py::step hitstop and slow preserve 60Hz cooldowns/spawns.
## This test runs deterministically without a live Windows graphics device.
const Combat = preload("res://scripts/combat/combat_sim.gd")
var failed: int = 0

func _initialize() -> void:
    call_deferred("_test")

func verify(ok: bool, why: String) -> void:
    if not ok:
        failed += 1
        printerr("P4 TEMPORAL PARITY FAIL: " + why)

func _test() -> void:
    var sim: Variant = Combat.new(42)
    sim.begin_wave(1, [], "")
    verify(is_equal_approx(sim.current_time_scale(), 1.0),
        "normal fixed 60Hz scale")
    sim.set_player_snapshot({
        "pos": Vector2(640.0, 650.0), "mana": 40.0,
        "fire_cd": 10.0,
        "stats": {"slow": 0.15, "mana_regen": 6.0},
    })
    var enemy_id: int = sim.spawn_enemy("drifter", Vector2(1110.0, 610.0))
    sim._fire_friendly(Vector2(100.0, 130.0), 0.0)
    sim.damage_enemy(enemy_id, 10000.0)
    verify(is_equal_approx(float(sim.hitstop_t), 0.028),
        "ordinary kill triggers original 0.028s hitstop")
    verify(is_equal_approx(float(sim.slow_t), 0.15),
        "slow software accumulates 0.15 seconds on kill")
    verify(is_equal_approx(float(sim.current_time_scale()), 0.072),
        "0.16 hitstop and 0.45 slow stack")
    var old_x: float = float(sim.bullets[0]["pos"].x)
    var old_mana: float = float(sim.player["mana"])
    var old_fire_cd: float = float(sim.player["fire_cd"])
    sim.step_fixed()
    verify(is_equal_approx(float(sim.last_time_scale), 0.072),
        "world tick stores source time multiplier")
    verify(absf(float(sim.bullets[0]["pos"].x) - old_x - 880.0 / 60.0 * 0.072) < 0.001,
        "bullets move at reduced original time scale")
    verify(absf(float(sim.player["mana"]) - old_mana - 6.0 / 60.0) < 0.001,
        "mana regeneration stays real 60Hz")
    verify(absf(float(sim.player["fire_cd"]) - old_fire_cd + 1.0 / 60.0) < 0.001,
        "auto-fire cadence stays real 60Hz")
    verify(sim.slow_t < 0.15 and sim.hitstop_t < 0.028,
        "temporal modifiers decay on real 60Hz clock")
    sim.step_fixed()
    sim.step_fixed()
    verify(is_equal_approx(float(sim.hitstop_t), 0.0)
        and is_equal_approx(float(sim.current_time_scale()), 0.45),
        "normal hitstop expires before slow, leaving 45 percent scale")

    sim.slow_t = 0.55
    var second: int = sim.spawn_enemy("drifter", Vector2(1100.0, 610.0))
    sim.damage_enemy(second, 10000.0)
    verify(is_equal_approx(float(sim.slow_t), 0.6),
        "stacked kill-slow duration caps at source 0.6 seconds")

    var boss: Variant = Combat.new(55)
    boss.begin_wave(5, [], "overseer")
    var boss_id: int = boss.spawn_enemy("overseer", Vector2(640, 200))
    boss.damage_enemy(boss_id, 100000.0)
    verify(is_equal_approx(float(boss.hitstop_t), 0.16),
        "real boss kill triggers original 0.16s hitstop")
    verify(is_equal_approx(float(boss.current_time_scale()), 0.16),
        "boss hitstop without slow multiplies time by 0.16")
    boss.abort_wave()
    boss.begin_wave(1, [], "")
    verify(is_equal_approx(float(boss.current_time_scale()), 1.0),
        "new wave cannot inherit stale hitstop from previous boss")

    # Source Python on_bullet_hit emits hit, critical, explosion and chain
    # feedback when the actual corresponding C mechanics have occurred.
    var feedback: Variant = Combat.new(42)
    feedback.begin_wave(1, [], "")
    feedback.set_player_snapshot({"stats": {
        "crit": 1.0, "chain": 1, "explode": 42.0, "dmg": 10.0,
    }})
    var eid: int = feedback.spawn_enemy("guardian", Vector2(420, 360))
    feedback.spawn_enemy("guardian", Vector2(520, 360))
    var effects: Array[String] = []
    feedback.visual_event.connect(func(kind: String, _start: Vector2,
            _end: Vector2, _value: float) -> void:
        effects.append(kind))
    feedback._fire_friendly(Vector2(418, 360), 0.0)
    var bullet: Dictionary = feedback.bullets[0]
    feedback._on_bullet_hit(bullet, feedback.get_enemy(eid))
    verify(effects == ["critical", "explosion", "chain"],
        "C emits only the actual critical, explosion and chained events")
    var fx_script: GDScript = load("res://scripts/visuals/legacy_combat_fx.gd")
    var fx: Node3D = fx_script.new()
    root.add_child(fx)
    fx.call("add_chain_arc", Vector2(418, 360), Vector2(520, 360))
    fx.call("add_explosion", Vector2(418, 360), 42.0)
    fx.call("show_critical", Vector2(420, 360))
    fx.call("sync_battle", [], [], [], [], false, [])
    verify(fx.chain_pool[0].visible and fx.chain_arcs.size() == 1,
        "real C chain event renders one pooled electric beam")
    verify(fx.bursts.size() == 1 and is_equal_approx(float(fx.bursts[0]["radius"]), 42.0),
        "explosion ring uses exact actual gameplay radius")
    verify(fx.damage_labels.size() == 1 and fx.damage_pool[0].text == "CRIT!",
        "original critical-hit label displayed with pooled text")
    for tick in range(20):
        fx.call("sync_battle", [], [], [], [], false, [])
    verify(fx.chain_arcs.is_empty() and not fx.chain_pool[0].visible,
        "0.12-second chain lightning expires without stale meshes")
    fx.queue_free()

    if failed == 0:
        print("P4 PYTHON HITSTOP/SLOW AND PROJECTILE FEEDBACK PARITY PASS (headless)")
    quit(1 if failed > 0 else 0)
