extends SceneTree
## P4 headless fixed-seed regression; uses no B input or common main scene.
const Combat = preload("res://scripts/combat/combat_sim.gd")
const Rules = preload("res://scripts/combat/combat_rules.gd")
const Catalog = preload("res://scripts/enemies/enemy_catalog.gd")
var failures: int = 0

func _initialize() -> void:
    call_deferred("_test")

func check(condition: bool, description: String) -> void:
    if not condition:
        failures += 1
        printerr("P4 FAIL: " + description)

func _test() -> void:
    check(is_equal_approx(Rules.mitigate_damage(100.0, 100.0), 50.0), "armor")
    check(is_equal_approx(Rules.mitigate_damage(100.0, 100.0, 50.0), 100.0 * 100.0 / 150.0), "penetration")
    check(is_equal_approx(Rules.skill_cost(35.0, 1.0), 35.0), "Q cost")
    check(is_equal_approx(Rules.skill_cooldown(12.0, 1.0), 12.0), "E cooldown")
    check(Catalog.STATS.size() == 17 and Catalog.VIRUS.size() == 11, "catalog parity")
    var dots: Array = []
    for i in range(5):
        Rules.add_dot(dots, "burn", 4.0, 3.0)
    check(dots.size() == 1 and int(dots[0]["stacks"]) == 3, "DoT max 3 stacks")
    var doses: Array = Rules.advance_dots(dots, 1.0 / 60.0)
    check(doses.size() == 1 and is_equal_approx(float(doses[0]), 0.2), "DoT 60Hz")

    var first = Combat.new(42)
    var repeat = Combat.new(42)
    var schedule: Array = [{"at": 0.4, "kind": "drifter"}, {"at": 0.9, "kind": "shooter"}]
    first.begin_wave(1, schedule)
    repeat.begin_wave(1, schedule)
    for tick in range(90):
        first.step_fixed()
        repeat.step_fixed()
    check(first.enemies.size() == repeat.enemies.size(), "seeded enemy count")
    check(first.enemy_bullets.size() == repeat.enemy_bullets.size(), "seeded shot count")
    check(first.enemies.size() == 2, "scheduled spawns")
    if first.enemies.size() == repeat.enemies.size() and not first.enemies.is_empty():
        check(Vector2(first.enemies[0]["pos"]).is_equal_approx(repeat.enemies[0]["pos"]), "seeded AI position")
    var id: int = int(first.enemies[0]["id"])
    check(first.damage_enemy(id, 100.0) > 0.0, "damage applied")
    check(first.get_enemy(id).is_empty(), "kill removes entity")

    var boss = Combat.new(42)
    boss.begin_wave(5, [])
    for tick in range(60):
        boss.step_fixed()
    check(not boss.wave_ready_sent, "no boss means wave 5 not awarded")
    boss.abort_wave()
    boss.begin_wave(5, [{"at": 0.0, "kind": "overseer"}], "overseer")
    boss.step_fixed()
    check(boss.enemies.size() == 1, "boss spawn")
    check(not boss.try_skill("guard") == false, "guard accepts initial mana")
    check(not boss.try_skill("guard"), "guard on cooldown")
    check(boss.try_skill("pulse"), "pulse uses mana")
    check(is_equal_approx(float(boss.player["mana"]), 40.0), "Q/E mana cost")
    var boss_id: int = int(boss.enemies[0]["id"])
    boss.damage_enemy(boss_id, 100000.0)
    check(boss.boss_defeated, "boss kill confirms boss condition")
    for tick in range(50):
        boss.step_fixed()
    check(boss.wave_ready_sent, "wave ready after 0.75s empty")
    var completed: int = 0
    for event in boss.events:
        if event["type"] == "wave_ready":
            completed += 1
    check(completed == 1, "no duplicate completion event")
    boss.abort_wave()
    check(not boss.active and boss.enemies.is_empty(), "retreat resets incomplete combat")

    if failures > 0:
        printerr("P4 HEADLESS COMBAT FAILURES: %d" % failures)
    else:
        print("P4 HEADLESS COMBAT PASS (seed 42, 60 Hz)")
    quit(1 if failures > 0 else 0)
