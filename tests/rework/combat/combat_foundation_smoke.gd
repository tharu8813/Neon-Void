extends SceneTree
## Standalone: godot --headless --path . --script res://tests/rework/combat/combat_foundation_smoke.gd
const Stats = preload("res://scripts/rework/combat/stats.gd")
const Damage = preload("res://scripts/rework/combat/damage.gd")
const Statuses = preload("res://scripts/rework/combat/statuses.gd")
const Events = preload("res://scripts/rework/combat/events.gd")
const RuntimeCases = preload("res://tests/rework/combat/runtime_cases.gd")
const ProductionCases = preload("res://tests/rework/combat/production_cpu_adapter_cases.gd")
var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func check(value: bool, message: String) -> void:
    if not value:
        failures += 1
        printerr("REWORK B COMBAT FAIL: " + message)

func near(a: float, b: float) -> bool:
    return absf(a - b) < 0.0001

func chain_handler(event: Dictionary) -> Array:
    var kind: String = str(event.get("kind", ""))
    if kind == "root":
        return [{"effect_id": "lightning", "event": {"kind": "lightning", "target_id": "B"}}]
    if kind == "lightning":
        return [{"effect_id": "explosion", "event": {"kind": "explosion", "target_id": "C"}}]
    if kind == "explosion":
        return [{"effect_id": "lightning", "event": {"kind": "lightning", "target_id": "B"}}]
    return []

func _run() -> void:
    var attacker: Dictionary = Stats.make({"attack_damage": 10.0, "ability_power": 20.0})
    var target: Dictionary = Stats.make({"health": 200.0, "armor": 100.0, "magic_resist": -100.0})
    var attack: Dictionary = {"damage_type": "physical", "base_damage": 30.0, "ad_ratio": 1.0, "ap_ratio": 0.5, "can_crit": true}
    var result: Dictionary = Damage.calculate(attacker, target, attack)
    check(near(float(result["calculated_damage"]), 25.0), "AD+AP coefficient and 100 armor")
    check(near(float(Damage.calculate(attacker, target, attack, true)["calculated_damage"]), 30.0), "120% base crit")
    attacker["physical_pen_percent"] = 0.2
    attacker["physical_pen_flat"] = 20.0
    check(near(float(Damage.calculate(attacker, target, attack)["calculated_damage"]), 31.25), "percent then flat penetration")
    var spell: Dictionary = {"damage_type": "magic", "base_damage": 100.0}
    check(near(float(Damage.calculate(attacker, target, spell)["calculated_damage"]), 150.0), "negative magic resistance")
    spell["damage_type"] = "true"
    check(near(float(Damage.calculate(attacker, target, spell)["calculated_damage"]), 100.0), "true damage ignores resistance")
    var hp_target: Dictionary = {"health": 50.0}
    var applied: Dictionary = Damage.apply_damage(hp_target, Damage.calculate(attacker, target, spell))
    check(near(float(applied["actual_damage"]), 50.0) and near(float(hp_target["health"]), 0.0), "overkill HP capped")
    check(near(float(Damage.apply_damage({"health": 50.0}, result, true)["actual_damage"]), 0.0), "invulnerability")
    var vamp_actor: Dictionary = {"omni_vamp": 0.1, "on_hit_vamp": 0.2}
    for flag_combo in [
        {"is_area": false, "is_dot": false},
        {"is_area": true, "is_dot": false},
        {"is_area": false, "is_dot": true},
        {"is_area": true, "is_dot": true}
    ]:
        var leech: Dictionary = Damage.vamp_components(vamp_actor, flag_combo, 100.0)
        var expected: float = 10.0 if not bool(flag_combo["is_area"]) and not bool(flag_combo["is_dot"]) else 3.3
        check(near(float(leech["omni_vamp_heal"]), expected), "omni vamp AoE/DoT 33% once")
        check(near(float(leech["on_hit_vamp_heal"]), 0.0), "no implicit on-hit vamp")
    check(near(float(Damage.vamp_components(vamp_actor, {"is_on_hit": true}, 100.0)["on_hit_vamp_heal"]), 20.0), "on-hit component separate")
    check(Stats.effective_cooldown_ticks(120, 100) == 60 and Stats.effective_cooldown_ticks(1, 10000000) == 1, "haste nonzero")
    var rng_a: RandomNumberGenerator = RandomNumberGenerator.new()
    var rng_b: RandomNumberGenerator = RandomNumberGenerator.new()
    rng_a.seed = 7452
    rng_b.seed = 7452
    var deterministic: bool = true
    for i in range(32):
        if Damage.roll_critical(rng_a, 0.36) != Damage.roll_critical(rng_b, 0.36):
            deterministic = false
    check(deterministic, "seeded crit reproducibility")
    var controlled: Dictionary = {"tenacity": 0.25, "boss_control_resistance": 0.5}
    var cc: Dictionary = {"id": "root", "control": true, "duration_ticks": 120, "flags": ["rooted"], "stack_mode": "refresh"}
    var cc_result: Dictionary = Statuses.apply(controlled, cc)
    check(int(cc_result["duration_ticks"]) == 45, "tenacity and boss CC reduction draft")
    check(bool(Statuses.active_flags(controlled).get("rooted", false)), "root flag")
    cc_result = Statuses.apply(controlled, cc)
    check(controlled["statuses"].size() == 1 and int(cc_result["duration_ticks"]) == 45, "refresh no duplicate status")
    var dot_state: Dictionary = {}
    var dot: Dictionary = {"id": "integrity", "duration_ticks": 120, "stack_mode": "stack",
        "max_stacks": 2, "dot_dps": 60.0, "dot_damage_type": "magic", "stat_modifiers": {"armor": -5.0}}
    Statuses.apply(dot_state, dot)
    Statuses.apply(dot_state, dot)
    Statuses.apply(dot_state, dot)
    check(dot_state["statuses"].size() == 1 and int(dot_state["statuses"][0]["stacks"]) == 2, "bounded stack policy")
    check(near(float(Statuses.stat_offsets(dot_state)["armor"]), -10.0), "stacked stat modifier")
    var sum_damage: float = 0.0
    for i in range(120):
        var change: Dictionary = Statuses.tick(dot_state)
        for request in change["damage_requests"]:
            sum_damage += float(request["base_damage"])
    check(near(sum_damage, 240.0) and dot_state["statuses"].is_empty(), "60Hz DoT and expiry")
    var one_tick: Dictionary = {"tenacity": 1.0}
    check(not bool(Statuses.apply(one_tick, cc)["ok"]), "full CC immunity")
    var gate_rng: RandomNumberGenerator = RandomNumberGenerator.new()
    gate_rng.seed = 2026
    var stopped_state: Dictionary = {}
    Statuses.apply(stopped_state, {"id": "stop", "flags": ["stopped"], "duration_ticks": 30})
    check(not Statuses.action_allowed(stopped_state, "attack", gate_rng), "stop disables attack")
    check(not Statuses.action_allowed(stopped_state, "interact", gate_rng), "stop disables interaction")
    var rooted_state: Dictionary = {}
    Statuses.apply(rooted_state, {"id": "root", "flags": ["rooted"], "duration_ticks": 30})
    check(not Statuses.action_allowed(rooted_state, "dash", gate_rng), "root disables dash")
    check(Statuses.action_allowed(rooted_state, "attack", gate_rng), "root does not disable attack")
    var silenced_state: Dictionary = {}
    Statuses.apply(silenced_state, {"id": "silence", "flags": ["silenced"], "duration_ticks": 30})
    check(not Statuses.action_allowed(silenced_state, "skill", gate_rng), "silence disables skills")
    var broken_state: Dictionary = {}
    Statuses.apply(broken_state, {"id": "malfunction", "duration_ticks": 30, "attack_failure_chance": 1.0})
    check(not Statuses.action_allowed(broken_state, "attack", gate_rng), "100% malfunction always blocks attack")
    check(near(float(Statuses.modified_stats({"armor": 20.0}, dot_state)["armor"]), 20.0), "expired modifier removed")
    var bus = Events.new()
    bus.set_tick(5)
    var root: Dictionary = bus.submit_root({"kind": "root", "actor_id": "P", "target_id": "A", "hit_key": "projectile-1"})
    check(not root.is_empty(), "root event accepted")
    check(bus.submit_root({"kind": "root", "target_id": "A", "hit_key": "projectile-1"}).is_empty(), "duplicate projectile target rejected")
    bus.retire_hit_key("projectile-1")
    check(not bus.submit_root({"kind": "other", "target_id": "A", "hit_key": "projectile-1"}).is_empty(), "retired projectile ledger can be released")
    var chain: Array[Dictionary] = bus.drain(Callable(self, "chain_handler"))
    check(chain.size() == 4 and int(bus.metrics["cycle_blocked"]) == 1, "multi-step chain allowed but cycle blocked")
    var second: Dictionary = bus.submit_root({"kind": "root", "target_id": "A", "hit_key": "projectile-2"})
    check(not second.is_empty() and bus.drain(Callable(self, "chain_handler")).size() == 3, "independent root can proc again")
    var a: Dictionary = bus.submit_root({"kind": "manual", "target_id": "A"})
    var b: Dictionary = bus.submit_proc(a, "lightning", {"target_id": "B"}, 30)
    check(not b.is_empty(), "first proc accepted")
    check(bus.submit_proc(a, "lightning", {"target_id": "B"}, 30).is_empty(), "per-target ICD")
    check(not bus.submit_proc(a, "lightning", {"target_id": "C"}, 30).is_empty(), "same proc at another target allowed")
    var origin: Dictionary = bus.submit_root({"kind": "manual", "actor_id": "actor-1", "target_id": "B"})
    var first_source: Dictionary = bus.submit_proc(origin, "reflect", {"target_id": "B", "actor_id": "actor-1"})
    check(not first_source.is_empty(), "first effect source accepted")
    check(bus.submit_proc(first_source, "reflect", {"target_id": "B", "actor_id": "actor-1"}).is_empty(), "same source-target effect cycle blocked")
    check(not bus.submit_proc(first_source, "reflect", {"target_id": "B", "actor_id": "actor-2"}).is_empty(), "different actor legitimate proc allowed")
    var forbidden: Dictionary = bus.submit_root({"kind": "manual", "allowed_proc_triggers": ["on_kill"]})
    check(bus.submit_proc(forbidden, "on_hit_plugin", {"trigger_kind": "on_hit", "target_id": "B"}).is_empty(), "proc trigger permission")
    RuntimeCases.run(Callable(self, "check"))
    ProductionCases.run(Callable(self, "check"), root)
    if failures == 0:
        print("REWORK B COMBAT PASS (damage/status/procs/60 Hz, seed=7452)")
    quit(1 if failures > 0 else 0)
