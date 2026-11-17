extends SceneTree
const Slots = preload("res://scripts/rework/skills/skill_slots.gd")
var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func check(condition: bool, message: String) -> void:
    if not condition:
        failures += 1
        printerr("REWORK B SKILLS FAIL: " + message)

func _run() -> void:
    var skills = Slots.new()
    skills.configure({"max_mana": 100.0, "mana": 50.0, "mana_regen": 6.0})
    check(skills.slots.size() == 3, "exactly three active slots")
    check(skills.learn({"id": "pulse", "mana_cost": 40.0, "cooldown_ticks": 120, "can_crit": true,
        "attack": {"damage_type": "magic", "ad_ratio": 0.25, "ap_ratio": 1.0}}), "learn skill")
    check(not skills.equip(0, "pulse", false), "no unauthorized equip")
    check(skills.equip(0, "pulse", true), "equip with E authorization")
    check(not skills.equip(3, "pulse", true), "slot 4 excluded")
    check(not skills.cast(0, 1, 0, {"silenced": true})["ok"], "silence prevents cast")
    var first: Dictionary = skills.cast(0, 1, 100)
    check(bool(first["ok"]) and bool(first["can_crit"]) and absf(skills.mana - 10.0) < 0.001, "cast costs mana and keeps crit flag")
    check(int(first["ready_at_tick"]) == 61, "100 haste halves cooldown")
    check(not skills.cast(0, 60, 100)["ok"], "cooldown prevents casting")
    for i in range(60):
        skills.tick()
    check(absf(skills.mana - 16.0) < 0.001, "mana regen per second at 60 Hz")
    check(not skills.cast(0, 61, 100)["ok"], "not enough mana after cooldown")
    skills.mana = 100.0
    check(skills.cast(0, 61, 100)["ok"], "ready skill can recast")
    var dash_config: Dictionary = {"distance": 9.0, "cooldown_ticks": 15}
    check(not skills.dash(Vector2.ZERO, 1, dash_config)["ok"], "dash requires movement")
    check(not skills.dash(Vector2.RIGHT, 1, dash_config, {"rooted": true})["ok"], "root blocks movement dash")
    var dash: Dictionary = skills.dash(Vector2(3, 4), 1, dash_config)
    check(bool(dash["ok"]) and Vector2(dash["direction"]).is_equal_approx(Vector2(0.6, 0.8)), "dash follows movement vector")
    check(not skills.dash(Vector2.LEFT, 2, dash_config)["ok"], "dash cooldown")
    check(skills.dash(Vector2.LEFT, 16, dash_config)["ok"], "dash ready tick")
    if failures == 0:
        print("REWORK B SKILLS PASS (three slots/mana/haste/dash)")
    quit(1 if failures > 0 else 0)
