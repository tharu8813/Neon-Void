extends RefCounted
## S01 B 9-skill REAL fixed-step path, not synthetic QA starter skill grants.
## These are BALANCE_DRAFT skill definitions; E must authorize learn/equip in production.
const Runtime = preload("res://scripts/rework/combat/combat_runtime.gd")
const Registry = preload("res://scripts/rework/skills/skill_registry.gd")
const Statuses = preload("res://scripts/rework/combat/statuses.gd")
const Slots = preload("res://scripts/rework/skills/skill_slots.gd")

static func _near(a: float, b: float) -> bool:
    return absf(a - b) < 0.0001

static func run(check: Callable) -> void:
    var ids: Array[String] = Registry.ids()
    check.call(ids.size() == 9, "S01 exactly 9 declared BALANCE_DRAFT active skills")
    var expected: Dictionary = {
        "nv_skill_arc_pulse": 4, "nv_skill_penetrator": 1,
        "nv_skill_root_packet": 1, "nv_skill_overclock": 0,
        "nv_skill_patch_kit": 0, "nv_skill_blink_step": 0,
        "nv_skill_scatter": 3, "nv_skill_emp_ring": 8,
        "nv_skill_seeker_swarm": 2
    }
    for uid in ids:
        var skill: Dictionary = Registry.definition(uid)
        var sim = Runtime.new(20261118, {"encounter_id": "S01-skill:" + uid})
        sim.register_actor("P", "vaccine", Vector3.ZERO,
            {"max_health": 100.0, "health": 50.0, "attack_speed": 0.0,
             "max_mana": 200.0, "mana": 200.0, "mana_regen": 0.0},
            {"player_controlled": true})
        sim.register_actor("E", "virus", Vector3(2, 0, 0),
            {"max_health": 1000.0, "health": 1000.0,
             "attack_speed": 0.0, "move_speed": 0.0},
            {"attack_range": 0.0})
        check.call(sim.learn_skill("P", skill, 0, true),
            uid + " E-authorized test fixture equips skill")
        var notices: Array[Dictionary] = sim.step_fixed({"P": {
            "fire": false, "aim_world": Vector3(10, 0, 0), "skill_slot": 0}})
        check.call(notices.any(func(n: Dictionary) -> bool:
            return (str(n.get("kind", "")) == "skill_cast"
                and str(n.get("skill_id", "")) == uid),
            uid + " produces real cast notice")
        check.call(notices.any(func(n: Dictionary) -> bool:
            return (str(n.get("kind", "")) == "skill_resolved"
                and int(n.get("projectiles", -1)) == int(expected[uid])),
            uid + " executes declared shape/projectile count")
        var mana: float = float(sim.snapshot("P")["mana"])
        var cost: float = float(skill["mana_cost"])
        var refund: float = float(skill.get("cast", {}).get("mana_restore", 0.0))
        check.call(_near(mana, minf(200.0, 200.0 - cost + refund)),
            uid + " applies mana charge and any self-refund once")
        var ready: int = int(sim.snapshot("P")["skill_state"]["slots"][0]["ready_at_tick"])
        check.call(ready > sim.fixed_tick,
            uid + " starts non-zero cooldown on successful cast")
        var rejected: Array[Dictionary] = sim.step_fixed({"P": {
            "fire": false, "aim_world": Vector3(10, 0, 0), "skill_slot": 0}})
        check.call(rejected.any(func(n: Dictionary) -> bool:
            return (str(n.get("kind", "")) == "skill_rejected"
                and str(n.get("reason", "")) == "cooldown"),
            uid + " rejects early recast instead of draining mana")
        check.call(_near(float(sim.snapshot("P")["mana"]), mana),
            uid + " failed recast cannot double-spend")
        if uid == "nv_skill_blink_step":
            check.call(_near(float(sim.snapshot("P")["position"].x), 6.5),
                "blink uses B movement displacement instead of VFX only")
        if uid == "nv_skill_overclock":
            check.call(float(sim.snapshot("P")["effective_stats"]["attack_speed"]) >= 2.0,
                "overclock actually changes effective attack speed")
        if uid == "nv_skill_patch_kit":
            check.call(_near(float(sim.snapshot("P")["stats"]["health"]), 72.0),
                "patch kit actually restores HP")
        if uid == "nv_skill_root_packet" or uid == "nv_skill_emp_ring":
            for t in range(20):
                sim.step_fixed({"P": {"fire": false, "aim_world": Vector3(10,0,0)}})
            check.call(not sim.snapshot("E")["status_state"]["statuses"].is_empty(),
                uid + " projectile hits and applies a real target status")
    var guard = Slots.new()
    guard.configure({"max_mana": 50.0, "mana": 0.0, "mana_regen": 0.0})
    var def: Dictionary = Registry.definition("nv_skill_root_packet")
    check.call(guard.learn(def) and guard.equip(2, def["id"], true),
        "third slot can be authorized independently")
    check.call(str(guard.cast(2, 1, 0).get("reason", "")) ==
        "insufficient_mana" and guard.ready_at.is_empty(),
        "insufficient mana cannot reserve cooldown")
    guard.mana = 50.0
    check.call(str(guard.cast(2, 1, 0, {"silenced": true}).get("reason", "")) ==
        "control_blocked" and _near(guard.mana, 50.0),
        "silence blocks cast without consuming resources")
    check.call(str(guard.cast(1, 1, 0).get("reason", "")) == "empty_slot",
        "empty third-slot loadout cannot conjure an unlearned skill")
    var movement = Slots.new()
    movement.configure({"max_mana": 50.0, "mana": 50.0, "mana_regen": 0.0})
    var blink: Dictionary = Registry.definition("nv_skill_blink_step")
    movement.learn(blink)
    movement.equip(0, blink["id"], true)
    check.call(str(movement.cast(0, 1, 0, {"rooted": true}).get("reason", "")) ==
        "rooted" and _near(movement.mana, 50.0),
        "root prevents blink and consumes no mana")
    var no_qa = Slots.new()
    no_qa.learn(def)
    check.call(not no_qa.equip(0, def["id"], false) and no_qa.slots[0].is_empty(),
        "ordinary play cannot equip skill without E progression authorization")
