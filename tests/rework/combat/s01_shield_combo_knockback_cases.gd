extends RefCounted
## B S01 regression of real C/B barrier, transform and knockback integrations.
## Numerical combo ordering and knockback distance are BALANCE_DRAFT, not GDD approval.
const Runtime = preload("res://scripts/rework/combat/combat_runtime.gd")
const CEffects = preload("res://scripts/rework/combat/c_effect_adapter.gd")
const Statuses = preload("res://scripts/rework/combat/statuses.gd")

static func _near(a: float, b: float) -> bool:
    return absf(a - b) < 0.0001

static func _add_shield(adapter: Variant, id: String, amount: float, lifetime: int) -> bool:
    adapter._context = {"kind": "skill_cast", "event_id": "B:test:" + id,
        "actor_id": "P", "target_id": "P", "tick": adapter.combat.fixed_tick}
    return bool(adapter._authoritative_sink([{"type": "shield", "actor_id": "P",
        "plugin_id": id, "amount": amount, "duration_ticks": lifetime}],
        adapter._world()).get("ok", false))

static func _deal(combat: Variant, amount: float, damage_type: String = "true") -> void:
    combat.bus.submit_root({"kind": "damage", "actor_id": "E",
        "target_id": "P", "source_tags": ["enemy_attack"],
        "attack": {"damage_type": damage_type, "base_damage": amount,
            "can_crit": false, "ad_ratio": 0.0, "ap_ratio": 0.0}})
    combat.step_fixed({"P": {"fire": false}})

static func run(check: Callable) -> void:
    var sim = Runtime.new(201, {"encounter_id": "shield-layer-order"})
    sim.register_actor("P", "vaccine", Vector3.ZERO,
        {"max_health": 100.0, "health": 100.0,
         "attack_speed": 0.0, "armor": 100.0},
        {"player_controlled": true})
    sim.register_actor("E", "virus", Vector3(4,0,0),
        {"attack_speed": 0.0, "move_speed": 0.0}, {"attack_range": 0.0})
    var shields = CEffects.new()
    check.call(shields.attach(sim, "P", "shield-order", 201),
        "attach production C/B shield absorber")
    check.call(shields.configure_shield_policy({"stacking": "add",
        "absorption": "post_resistance", "maximum_total": 20.0}),
        "configurable provisional shield policy accepted")
    check.call(_add_shield(shields, "periodic", 8.0, 5) and
        _add_shield(shields, "skill", 6.0, 2) and
        _add_shield(shields, "dash", 30.0, 7),
        "three distinct shield sources enter B ledger")
    check.call(_near(shields.shield_value(), 20.0) and
        shields.shield_diagnostics()["layers"].size() == 3,
        "shield maximum cap never silently removes earlier layers")
    _deal(sim, 10.0, "physical")
    check.call(_near(shields.shield_value(), 15.0) and
        _near(float(sim.snapshot("P")["stats"]["health"]), 100.0),
        "post-resistance shield absorbs 5 before B HP changes")
    var history: Dictionary = shields.shield_diagnostics()
    check.call(_near(float(history["layers"][0]["remaining"]), 3.0) and
        _near(float(history["layers"][1]["remaining"]), 6.0) and
        _near(float(history["layers"][2]["remaining"]), 6.0),
        "oldest layer absorbs first, no double-consumption")
    sim.step_fixed({"P": {"fire": false}})
    check.call(_near(shields.shield_value(), 9.0) and
        shields.shield_diagnostics()["layers"].size() == 2,
        "skill shield expires at exact independent tick without extending others")
    for i in range(3):
        sim.step_fixed({"P": {"fire": false}})
    check.call(_near(shields.shield_value(), 6.0) and
        shields.shield_diagnostics()["layers"].size() == 1,
        "periodic shield expires without deleting longer dash shield")
    _deal(sim, 10.0, "true")
    check.call(_near(shields.shield_value(), 0.0) and
        _near(float(sim.snapshot("P")["stats"]["health"]), 96.0),
        "true damage (default draft) burns remaining shield then 4 HP")
    var bad: Dictionary = shields._authoritative_sink([
        {"type": "shield", "actor_id": "P", "amount": -5.0,
         "duration_ticks": 1}], shields._world())
    check.call(not bool(bad.get("ok", false)) and
        _near(shields.shield_value(), 0.0),
        "invalid shield amount rejected atomically")
    check.call(shields.configure_shield_policy({"stacking": "strongest",
        "maximum_total": 0.0, "absorption": "true_damage_bypasses"}),
        "shield stacking and true-bypass policies stay configurable drafts")
    _add_shield(shields, "weak", 8.0, 0)
    _add_shield(shields, "weaker", 5.0, 0)
    check.call(_near(shields.shield_value(), 8.0),
        "strongest stacking does not accumulate a weaker layer")
    _add_shield(shields, "stronger", 12.0, 0)
    check.call(_near(shields.shield_value(), 12.0) and
        shields.shield_diagnostics()["layers"].size() == 1,
        "strongest policy replaces weaker shield deterministically")
    _deal(sim, 6.0, "true")
    check.call(_near(shields.shield_value(), 12.0) and
        _near(float(sim.snapshot("P")["stats"]["health"]), 90.0),
        "explicit true bypass consumes HP without consuming barrier")
    # Composite laser+boomerang must not cause a C abort or a permanent firing lock.
    var combo = Runtime.new(203, {"encounter_id": "laser-return",
        "max_active_projectiles": 16})
    combo.register_actor("P", "vaccine", Vector3.ZERO,
        {"attack_speed": 1.0, "attack_damage": 5.0},
        {"player_controlled": true, "projectile_speed": 60.0,
         "projectile_radius": 0.1, "projectile_lifetime_ticks": 24})
    combo.register_actor("E1", "virus", Vector3(3,0,0),
        {"max_health": 100.0, "health": 100.0,
         "attack_speed": 0.0, "move_speed": 0.0}, {"attack_range": 0.0})
    combo.register_actor("E2", "virus", Vector3(3.8,0,0),
        {"max_health": 100.0, "health": 100.0,
         "attack_speed": 0.0, "move_speed": 0.0}, {"attack_range": 0.0})
    var effects = CEffects.new()
    check.call(effects.attach(combo, "P", "laser-return", 203) and
        effects.configure_levels({"nv_l_laser": 2, "nv_l_boomerang": 2}, 203),
        "C real legendary laser and boomerang load together")
    combo.step_fixed({"P": {"fire": true, "aim_world": Vector3(10,0,0)}})
    effects.after_fixed_step(combo.last_processed_events)
    var composite: Array[Dictionary] = combo.projectile_snapshots()
    check.call(effects.fault.is_empty() and composite.size() == 2,
        "laser+boomerang simultaneous C command batch creates two live projectiles")
    var both: bool = true
    for p in composite:
        if not (str(p.get("shape", "")) == "boomerang" and
                bool(p.get("laser_active", false)) and
                bool(p.get("infinite_pierce", false)) and
                _near(float(p["radius"]), 0.12) and
                _near(float(p["on_hit_damage_efficiency"]), 0.7)):
            both = false
    check.call(both, "all boomerang copies inherit laser width, pierce and proc efficiency")
    check.call(int(effects._auxiliary["boomerang_outstanding"]) == 2 and
        bool(effects._auxiliary["basic_attack_blocked"]),
        "both physical boomerangs block ordinary firing until retired")
    for tick in range(35):
        var prev: Dictionary = {}
        for p in combo.projectile_snapshots():
            prev[str(p["id"])] = p.duplicate(true)
        var notes: Array[Dictionary] = combo.step_fixed({"P": {"fire": false}})
        for note in notes:
            if str(note.get("kind", "")) == "projectile_retire" and
                    prev.has(str(note.get("id", ""))):
                effects.handle_boomerang_return(prev[str(note["id"])])
        effects.after_fixed_step(combo.last_processed_events)
    check.call(float(combo.snapshot("E1")["stats"]["health"]) < 100.0 and
        float(combo.snapshot("E2")["stats"]["health"]) < 100.0,
        "boomerang area hit includes adjacent enemy through B damage queue")
    check.call(effects.fault.is_empty() and combo.projectiles.is_empty() and
        int(effects._auxiliary["boomerang_outstanding"]) == 0 and
        not bool(effects._auxiliary["basic_attack_blocked"]),
        "all return/timeout receipts release attack lock exactly once")
    # CC interactions: boss resistance and tenacity affect displacement;
    # voluntary root stays active, but does not cancel *external* knockback.
    var force_sim = Runtime.new(205, {"encounter_id": "knockback-cc"})
    force_sim.register_actor("P", "vaccine", Vector3.ZERO,
        {"attack_speed": 0.0}, {"player_controlled": true})
    force_sim.register_actor("E", "virus", Vector3(1,0,0),
        {"attack_speed": 0.0, "move_speed": 0.0}, {"attack_range": 0.0})
    force_sim.register_actor("BOSS", "virus", Vector3(2,0,0),
        {"attack_speed": 0.0, "move_speed": 0.0, "tenacity": 0.25,
         "boss_control_resistance": 0.5}, {"attack_range": 0.0})
    Statuses.apply(force_sim.actors["E"]["status_state"],
        {"id": "root", "duration_ticks": 30, "control": true,
         "flags": ["rooted"]})
    Statuses.apply(force_sim.actors["E"]["status_state"],
        {"id": "poison", "duration_ticks": 30,
         "dot_dps": 60.0, "dot_damage_type": "true"})
    var forced: Dictionary = force_sim.apply_radial_knockback("P", 4.0, 5.0)
    check.call(bool(forced.get("ok", false)) and
        forced.get("targets", []).size() == 2,
        "radial knockback displaces actual eligible B targets")
    check.call(_near(float(force_sim.snapshot("E")["position"].x), 5.0) and
        _near(float(force_sim.snapshot("BOSS")["position"].x), 3.5),
        "rooted enemy is pushed but boss CC+tenacity reduce displacement")
    check.call(bool(Statuses.active_flags(
        force_sim.actors["E"]["status_state"]).get("rooted", false)),
        "forced displacement does not erase existing root status")
    force_sim.step_fixed({"P": {"fire": false}})
    check.call(_near(float(force_sim.snapshot("E")["stats"]["health"]), 99.0),
        "DoT continues after knockback without duplicated ticks")
    var live = Runtime.new(207, {"encounter_id": "C-low-hp-knockback"})
    live.register_actor("P", "vaccine", Vector3.ZERO,
        {"max_health": 100.0, "health": 25.0, "attack_speed": 0.0},
        {"player_controlled": true})
    live.register_actor("E", "virus", Vector3(1,0,0),
        {"attack_speed": 0.0, "move_speed": 0.0}, {"attack_range": 0.0})
    var live_c = CEffects.new()
    check.call(live_c.attach(live, "P", "C-low-hp", 207) and
        live_c.configure_levels({"nv_r_low_hp_knockback": 1}, 207),
        "real C low-HP knockback plugin installed")
    _deal(live, 1.0)
    var move_notes: Array[Dictionary] = live_c.after_fixed_step(
        live.last_processed_events)
    check.call(_near(float(live.snapshot("E")["position"].x), 3.0) and
        move_notes.any(func(n: Dictionary) -> bool:
            return str(n.get("kind", "")) == "knockback_applied"),
        "low-HP on-damage C proc now moves B enemy, not notice-only")
    _deal(live, 1.0)
    live_c.after_fixed_step(live.last_processed_events)
    check.call(_near(float(live.snapshot("E")["position"].x), 3.0),
        "low-HP knockback respects C internal cooldown")
