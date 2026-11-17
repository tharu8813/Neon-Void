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

    if failed == 0:
        print("P4 PYTHON HITSTOP/SLOW 60HZ TIMING PARITY PASS (headless)")
    quit(1 if failed > 0 else 0)
