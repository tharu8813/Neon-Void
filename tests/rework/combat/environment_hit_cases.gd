extends RefCounted
## S01 B regression: D contacts must enter B's real 60Hz damage/status/shield queue.
const Runtime = preload("res://scripts/rework/combat/combat_runtime.gd")
const Statuses = preload("res://scripts/rework/combat/statuses.gd")

static func _near(a: float, b: float) -> bool:
    return absf(a - b) < 0.0001

static func _contact(id: String, typ: String, damage: float, repeat: int = 0) -> Dictionary:
    return {"source_id": "hazard-1", "region_id": "CPU", "event_id": id,
        "target_id": "P", "damage_type": typ, "base_damage": damage,
        "repeat_ticks": repeat}

static func _new_runtime() -> Variant:
    var sim = Runtime.new(20261118, {"encounter_id": "S01-environment"})
    sim.register_actor("P", "vaccine", Vector3.ZERO,
        {"max_health": 60.0, "health": 60.0, "armor": 100.0,
         "magic_resist": 100.0, "omni_vamp": 1.0,
         "attack_speed": 0.0, "mana_regen": 0.0},
        {"player_controlled": true})
    return sim

static func run(check: Callable) -> void:
    var sim = _new_runtime()
    var shield: Dictionary = {"remaining": 8.0}
    sim.damage_absorber = func(target_id: String, damage: Dictionary) -> Dictionary:
        var adjusted: Dictionary = damage.duplicate(true)
        if target_id == "P":
            var absorbed: float = minf(float(shield["remaining"]),
                float(adjusted["calculated_damage"]))
            shield["remaining"] = float(shield["remaining"]) - absorbed
            adjusted["calculated_damage"] = float(adjusted["calculated_damage"]) - absorbed
        return adjusted
    var hit: Dictionary = _contact("pulse", "physical", 20.0, 3)
    check.call(bool(sim.queue_environment_hit(hit).get("ok", false)),
        "S01 D hazard queues a B event")
    check.call(str(sim.queue_environment_hit(hit).get("reason", "")) ==
        "environment_contact_cooldown", "duplicate hazard contact denied before tick")
    var first: Array[Dictionary] = sim.step_fixed({"P": {"fire": false}})
    check.call(_near(float(sim.snapshot("P")["stats"]["health"]), 58.0) and
        _near(float(shield["remaining"]), 0.0),
        "environment physical damage applies resistance then provisional shield-first")
    check.call(first.any(func(note: Dictionary) -> bool:
        return str(note.get("kind", "")) == "hit" and
            bool(note.get("environment_source", false)) and
            str(note.get("region_id", "")) == "CPU" and
            str(note.get("external_event_id", "")) == "pulse"),
        "UI hit notice retains D source and region without mutable HP")
    check.call(str(sim.queue_environment_hit(hit).get("reason", "")) ==
        "environment_contact_cooldown", "hazard 60Hz repeat interval respected")
    sim.step_fixed({"P": {"fire": false}})
    sim.step_fixed({"P": {"fire": false}})
    check.call(bool(sim.queue_environment_hit(hit).get("ok", false)),
        "hazard interval opens after exactly 3 fixed ticks")
    sim.step_fixed({"P": {"fire": false}})
    check.call(_near(float(sim.snapshot("P")["stats"]["health"]), 48.0),
        "hazard repeated physical hit uses B's same damage ledger")
    var one_shot: Dictionary = _contact("one-shot", "true", 4.0)
    check.call(bool(sim.queue_environment_hit(one_shot).get("ok", false)),
        "one-shot environmental attack accepted")
    sim.step_fixed({"P": {"fire": false}})
    check.call(_near(float(sim.snapshot("P")["stats"]["health"]), 44.0) and
        str(sim.queue_environment_hit(one_shot).get("reason", "")) ==
        "environment_contact_cooldown",
        "true hazard damage ignores armor and is idempotent")
    check.call(not bool(sim.queue_environment_hit(
        _contact("bad", "invalid", 1.0)).get("ok", false)),
        "invalid hazard damage type rejected")
    check.call(not bool(sim.queue_environment_hit(
        _contact("bad", "true", -1.0)).get("ok", false)),
        "negative hazard damage rejected")
    check.call(not bool(sim.queue_environment_hit(
        {"target_id": "P", "damage_type": "true", "base_damage": 1.0,
         "repeat_ticks": 0}).get("ok", false)),
        "hazard without stable source/event/region fails closed")
    var bad_target: Dictionary = _contact("target", "true", 10.0)
    bad_target["target_id"] = "UNKNOWN"
    check.call(not bool(sim.queue_environment_hit(bad_target).get("ok", false)),
        "unregistered target cannot receive untracked damage")
    var magic = _new_runtime()
    magic.queue_environment_hit(_contact("magic", "magic", 20.0))
    magic.step_fixed({"P": {"fire": false}})
    check.call(_near(float(magic.snapshot("P")["stats"]["health"]), 50.0),
        "hazard magic obeys magic resist")
    var dot_sim = _new_runtime()
    var dot: Dictionary = _contact("poison", "true", 1.0)
    dot["status_effects"] = [{"id": "env_poison", "duration_ticks": 3,
        "stack_mode": "refresh", "dot_dps": 60.0, "dot_damage_type": "true"}]
    dot_sim.queue_environment_hit(dot)
    dot_sim.step_fixed({"P": {"fire": false}})
    for i in range(3):
        dot_sim.step_fixed({"P": {"fire": false}})
    check.call(_near(float(dot_sim.snapshot("P")["stats"]["health"]), 56.0),
        "environment DoT persists 3 ticks and never self-vamps despite target omnivamp")
    var invuln = _new_runtime()
    Statuses.apply(invuln.actors["P"]["status_state"],
        {"id": "inv", "duration_ticks": 3, "flags": ["invulnerable"]})
    invuln.queue_environment_hit(_contact("inv", "true", 100.0))
    invuln.step_fixed({"P": {"fire": false}})
    check.call(_near(float(invuln.snapshot("P")["stats"]["health"]), 60.0),
        "invulnerability defeats true environmental hit")
    var fatal = _new_runtime()
    fatal.queue_environment_hit(_contact("fatal", "true", 1000.0))
    var death_notes: Array[Dictionary] = fatal.step_fixed({"P": {"fire": false}})
    check.call(not bool(fatal.snapshot("P")["alive"]) and
        _near(float(fatal.snapshot("P")["stats"]["health"]), 0.0) and
        int(fatal.counters["killed"]) == 1,
        "lethal environmental hit yields exactly one death")
    check.call(death_notes.any(func(note: Dictionary) -> bool:
        return str(note.get("kind", "")) == "death" and
            str(note.get("killer_id", "")).begins_with("environment:")),
        "death notice identifies environmental killer")
    check.call(not bool(fatal.queue_environment_hit(
        _contact("after-death", "true", 10.0)).get("ok", false)),
        "dead actor cannot be killed repeatedly by hazard")
    var deterministic = _new_runtime()
    deterministic.queue_environment_hit(_contact("magic", "magic", 20.0))
    deterministic.step_fixed({"P": {"fire": false}})
    check.call(deterministic.snapshot("P")["stats"]["health"] ==
        magic.snapshot("P")["stats"]["health"],
        "identical environmental 60Hz replay produces identical HP")
