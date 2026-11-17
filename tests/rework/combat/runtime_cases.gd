extends RefCounted
## Exercised by combat_foundation_smoke.gd on Linux + Windows.
const Runtime = preload("res://scripts/rework/combat/combat_runtime.gd")
const Grid = preload("res://scripts/rework/combat/collision_grid.gd")
const Events = preload("res://scripts/rework/combat/events.gd")
const Statuses = preload("res://scripts/rework/combat/statuses.gd")
const Plugins = preload("res://scripts/rework/combat/plugin_effects.gd")
const Bridge = preload("res://scripts/rework/combat/combat_node_bridge.gd")

static func _close(value: float, goal: float) -> bool:
    return absf(value - goal) < 0.0001

static func _chain(event: Dictionary) -> Array:
    var kind: String = str(event.get("kind", ""))
    if kind == "root":
        return [{"effect_id": "lightning", "event": {"kind": "lightning",
            "actor_id": "P", "target_id": "E1", "trigger_kind": "on_hit"}}]
    if kind == "lightning":
        return [{"effect_id": "explosion", "event": {"kind": "explosion",
            "actor_id": "P", "target_id": "E2", "trigger_kind": "on_hit"}}]
    if kind == "explosion":
        return [{"effect_id": "lightning", "event": {"kind": "lightning",
            "actor_id": "P", "target_id": "E1", "trigger_kind": "on_hit"}}]
    return []

static func run(check: Callable) -> void:
    var hit: float = Grid.swept_fraction(Vector3.ZERO, Vector3(10, 0, 0),
        Vector3(5, 0, 0), 0.5)
    check.call(_close(hit, 0.45), "swept XZ collision avoids projectile tunneling")
    check.call(Grid.swept_fraction(Vector3.ZERO, Vector3(1, 0, 0),
        Vector3(5, 0, 0), 0.5) < 0.0, "non-intersecting swept segment")
    var bus = Events.new()
    bus.set_tick(1)
    bus.submit_root({"kind": "root", "actor_id": "P", "target_id": "E0",
        "allowed_proc_triggers": ["on_hit"]})
    var chain_handler: Callable = func(event: Dictionary) -> Array:
        var kind: String = str(event.get("kind", ""))
        if kind == "root":
            return [{"effect_id": "lightning", "event": {
                "kind": "lightning", "actor_id": "P", "target_id": "E1",
                "trigger_kind": "on_hit"}}]
        if kind == "lightning":
            return [{"effect_id": "explosion", "event": {
                "kind": "explosion", "actor_id": "P", "target_id": "E2",
                "trigger_kind": "on_hit"}}]
        if kind == "explosion":
            return [{"effect_id": "lightning", "event": {
                "kind": "lightning", "actor_id": "P", "target_id": "E1",
                "trigger_kind": "on_hit"}}]
        return []
    var part: Array[Dictionary] = bus.drain_batch(2, chain_handler)
    check.call(part.size() == 2 and bus.pending.size() > 0,
        "event work budget defers legitimate chain without deletion")
    var rest: Array[Dictionary] = bus.drain_batch(2, chain_handler)
    check.call(rest.size() == 1 and bus.pending.is_empty() and
        int(bus.metrics["cycle_blocked"]) == 1,
        "deferred multi-step chain resolves with cycle excluded")
    var global_bus = Events.new()
    global_bus.set_tick(10)
    var global_one: Dictionary = global_bus.submit_root({
        "actor_id": "A1", "target_id": "E", "kind": "damage"})
    check.call(not global_bus.submit_proc(global_one, "global_effect", {
        "actor_id": "A1", "target_id": "E",
        "cooldown_scope": "global"}, 30).is_empty(),
        "first globally scoped ICD allowed")
    var global_two: Dictionary = global_bus.submit_root({
        "actor_id": "A2", "target_id": "E", "kind": "damage"})
    check.call(global_bus.submit_proc(global_two, "global_effect", {
        "actor_id": "A2", "target_id": "E",
        "cooldown_scope": "global"}, 30).is_empty(),
        "global ICD blocks different actor until cooldown expires")
    var combat = Runtime.new(99, {"encounter_id": "unit"})
    check.call(combat.register_actor("P", "vaccine", Vector3.ZERO,
        {"attack_damage": 5.0, "attack_speed": 2.0},
        {"player_controlled": true, "projectile_speed": 120.0}),
        "register player shared stat shape")
    check.call(combat.register_actor("E", "virus", Vector3(2, 0, 0),
        {"max_health": 12.0, "health": 12.0, "attack_speed": 0.0},
        {"radius": 0.5, "preferred_min_range": 0.0,
         "preferred_max_range": 8.0, "attack_range": 0.0}),
        "register enemy shared stat shape")
    var input: Dictionary = {"P": {"move": Vector2.ZERO,
        "aim_world": Vector3(5, 0, 0), "fire": true}}
    var first: Array[Dictionary] = combat.step_fixed(input)
    var one: Dictionary = combat.snapshot("E")
    check.call(_close(float(one["stats"]["health"]), 7.0),
        "first projectile applies 100% AD damage on actual enemy")
    var hit_notices: int = 0
    for note in first:
        if str(note.get("kind", "")) == "hit":
            hit_notices += 1
    check.call(hit_notices == 1, "real impact produces read-only VFX event")
    for tick in range(70):
        combat.step_fixed(input)
    check.call(not bool(combat.snapshot("E")["alive"]) and
        int(combat.counters["killed"]) == 1, "fire cadence and exactly one enemy death")
    check.call(combat.projectile_pool.size() <= 128,
        "projectile object reuse bounded by memory-only cap")
    var second = Runtime.new(99, {"encounter_id": "unit"})
    second.register_actor("P", "vaccine", Vector3.ZERO,
        {"attack_damage": 5.0, "attack_speed": 2.0},
        {"player_controlled": true, "projectile_speed": 120.0})
    second.register_actor("E", "virus", Vector3(2, 0, 0),
        {"max_health": 12.0, "health": 12.0, "attack_speed": 0.0},
        {"radius": 0.5, "preferred_max_range": 8.0, "attack_range": 0.0})
    for tick in range(71):
        second.step_fixed(input)
    check.call(_close(float(combat.snapshot("E")["stats"]["health"]),
        float(second.snapshot("E")["stats"]["health"])) and
        combat.counters["damage_events"] == second.counters["damage_events"],
        "same seed inputs => deterministic 60Hz combat")
    var combo = Runtime.new(401, {"encounter_id": "combo"})
    combo.register_actor("P", "vaccine", Vector3.ZERO,
        {"attack_damage": 5.0, "attack_speed": 0.0},
        {"player_controlled": true, "projectile_speed": 120.0})
    combo.register_actor("E1", "virus", Vector3(2, 0, 0),
        {"max_health": 30.0, "health": 30.0, "attack_speed": 0.0},
        {"attack_range": 0.0})
    combo.register_actor("E2", "virus", Vector3(4, 0, 0),
        {"max_health": 30.0, "health": 30.0, "attack_speed": 0.0},
        {"attack_range": 0.0})
    var lightning: Dictionary = {
        "plugin_id": "test_lightning", "trigger": {
            "event": "projectile_hit", "source_tags": ["basic_attack"]},
        "internal_cooldown_seconds": 0.0, "cooldown_scope": "per_target",
        "proc_permissions": {"allowed_proc_triggers": ["on_hit"],
            "can_trigger_on_hit_effects": true},
        "effect_commands": [{"kind": "damage", "target_policy": "nearest_other_enemy",
            "attack": {"damage_type": "magic", "base_damage": 4.0}}]
    }
    var explosion: Dictionary = {
        "plugin_id": "test_explosion", "trigger": {
            "event": "damage_dealt", "source_tags": ["proc_damage"]},
        "internal_cooldown_seconds": 0.0, "cooldown_scope": "per_target",
        "proc_permissions": {"allowed_proc_triggers": ["on_hit"],
            "can_trigger_on_hit_effects": true},
        "effect_commands": [{"kind": "damage", "target_policy": "current",
            "attack": {"damage_type": "true", "base_damage": 2.0}}]
    }
    combo.set_actor_plugins("P", [lightning, explosion])
    combo.actors["P"]["fire_credit"] = 1.0
    combo._fire_if_ready("P", Vector2.RIGHT)
    combo.step_fixed({"P": {"fire": false, "aim_world": Vector3(5, 0, 0)}})
    check.call(_close(float(combo.snapshot("E1")["stats"]["health"]), 25.0) and
        _close(float(combo.snapshot("E2")["stats"]["health"]), 24.0),
        "basic hit -> lightning -> explosion multi-stage live damage")
    var no_chain: Dictionary = lightning.duplicate(true)
    no_chain["proc_permissions"]["can_trigger_on_hit_effects"] = false
    var gated: Array[Dictionary] = Plugins.requests_for([no_chain], {
        "kind": "damage", "trigger_kind": "on_hit", "actor_id": "P",
        "target_id": "E1", "source_tags": ["basic_attack", "projectile_hit"],
        "actual_damage": 5.0})
    check.call(gated.size() == 1 and
        gated[0]["event"]["allowed_proc_triggers"].is_empty(),
        "C can_trigger_on_hit_effects=false denies downstream proc rights")
    var area = Runtime.new(723, {"encounter_id": "area"})
    area.register_actor("P", "vaccine", Vector3.ZERO,
        {"attack_damage": 5.0, "attack_speed": 0.0},
        {"player_controlled": true, "projectile_speed": 120.0})
    area.register_actor("E1", "virus", Vector3(2, 0, 0),
        {"max_health": 20.0, "health": 20.0, "attack_speed": 0.0},
        {"attack_range": 0.0})
    area.register_actor("E2", "virus", Vector3(4, 0, 0),
        {"max_health": 20.0, "health": 20.0, "attack_speed": 0.0},
        {"attack_range": 0.0})
    var splash: Dictionary = {
        "plugin_id": "splash", "trigger": {"event": "projectile_hit",
            "source_tags": ["basic_attack"]},
        "proc_permissions": {"can_trigger_on_hit_effects": false,
            "allowed_proc_triggers": []},
        "effect_commands": [{"kind": "damage", "target_policy": "area_around_target",
            "radius": 2.1, "attack": {"damage_type": "true",
            "base_damage": 2.0}}]
    }
    area.set_actor_plugins("P", [splash])
    area.actors["P"]["fire_credit"] = 1.0
    area._fire_if_ready("P", Vector2.RIGHT)
    area.step_fixed({"P": {"fire": false, "aim_world": Vector3(5, 0, 0)}})
    check.call(_close(float(area.snapshot("E1")["stats"]["health"]), 13.0) and
        _close(float(area.snapshot("E2")["stats"]["health"]), 18.0),
        "explicit area radius hits all valid targets with actual AoE damage")
    var feedback = Runtime.new(800, {"encounter_id": "feedback"})
    feedback.register_actor("P", "vaccine", Vector3.ZERO,
        {"attack_damage": 5.0, "attack_speed": 0.0},
        {"player_controlled": true, "projectile_speed": 120.0})
    feedback.register_actor("E", "virus", Vector3(2, 0, 0),
        {"max_health": 30.0, "health": 30.0, "attack_speed": 0.0},
        {"attack_range": 0.0})
    var extra_bullet: Dictionary = {
        "plugin_id": "feedback_bullet",
        "trigger": {"event": "projectile_hit", "source_tags": []},
        "proc_permissions": {"can_trigger_on_hit_effects": true,
            "allowed_proc_triggers": ["on_hit"]},
        "effect_commands": [{"kind": "spawn_projectile", "speed": 120.0,
            "count": 1, "attack": {"damage_type": "physical", "base_damage": 1.0}}]
    }
    feedback.set_actor_plugins("P", [extra_bullet])
    feedback.actors["P"]["fire_credit"] = 1.0
    feedback._fire_if_ready("P", Vector2.RIGHT)
    for tick in range(5):
        feedback.step_fixed({"P": {"fire": false,
            "aim_world": Vector3(5, 0, 0)}})
    check.call(int(feedback.counters["shots"]) == 2 and
        int(feedback.bus.metrics["cycle_blocked"]) >= 1,
        "derived projectile inherits ancestry and cannot create infinite feedback")
    var dot_self = Runtime.new(700, {"encounter_id": "dot"})
    dot_self.register_actor("P", "vaccine", Vector3.ZERO,
        {"max_health": 30.0, "health": 30.0, "attack_speed": 0.0},
        {"player_controlled": true})
    var dot_status: Dictionary = {
        "id": "integrity", "duration_ticks": 2,
        "dot_dps": 60.0, "dot_damage_type": "true"}
    Statuses.apply(dot_self.actors["P"]["status_state"], dot_status)
    dot_self.step_fixed({"P": {"fire": false}})
    check.call(_close(float(dot_self.snapshot("P")["stats"]["health"]), 29.0),
        "self-sourced DoT status applies HP damage even without external owner")
    var count_metadata: Dictionary = {
        "plugin_id": "threshold", "trigger": {"event": "on_hit_proc_count",
            "source_tags": []}, "trigger_after_hits": 4,
        "proc_permissions": {"allowed_proc_triggers": []},
        "effect_commands": [{"kind": "heal", "amount": 3.0}]
    }
    var state: Dictionary = {}
    var probe: Dictionary = {"kind": "damage", "trigger_kind": "on_hit",
        "actor_id": "P", "target_id": "E", "actual_damage": 1.0,
        "source_tags": ["basic_attack"]}
    for tick in range(3):
        check.call(Plugins.requests_for([count_metadata], probe, state).is_empty(),
            "threshold proc does not activate prematurely")
    check.call(Plugins.requests_for([count_metadata], probe, state).size() == 1,
        "count-based proc activates exactly on fourth qualifying hit")
    var skill_run = Runtime.new(405, {"encounter_id": "skill"})
    skill_run.register_actor("P", "vaccine", Vector3.ZERO,
        {"attack_speed": 0.0, "max_mana": 100.0, "mana": 50.0, "mana_regen": 6.0},
        {"player_controlled": true, "projectile_speed": 120.0,
         "dash_definition": {"distance": 2.0, "cooldown_ticks": 30}})
    skill_run.register_actor("E", "virus", Vector3(4, 0, 0),
        {"max_health": 30.0, "health": 30.0, "attack_speed": 0.0},
        {"attack_range": 0.0})
    check.call(skill_run.learn_skill("P", {"id": "pulse", "mana_cost": 10.0,
        "cooldown_ticks": 60, "attack": {"damage_type": "magic",
        "base_damage": 7.0}}, 0, true), "skill learned and equipped")
    var cast_notices: Array[Dictionary] = skill_run.step_fixed({"P": {
        "move": Vector2.RIGHT, "aim_world": Vector3(6, 0, 0),
        "dash": true, "skill_slot": 0, "fire": false}})
    check.call(float(skill_run.snapshot("P")["position"].x) > 2.0,
        "dash moves along movement input and not cursor")
    check.call(_close(float(skill_run.snapshot("P")["mana"]), 40.1),
        "real skill cast drains mana after fixed regen")
    check.call(_close(float(skill_run.snapshot("E")["stats"]["health"]), 23.0),
        "skill projectile deals magic damage to live enemy")
    var casts: int = 0
    for note in cast_notices:
        if str(note.get("kind", "")) == "skill_cast":
            casts += 1
    check.call(casts == 1, "one skill cast presentation event")
    var bridge = Bridge.new()
    check.call(bridge is Node3D and bridge.simulation != null,
        "scene attachable Node3D bridge instantiates without modifying root")
    bridge.free()

