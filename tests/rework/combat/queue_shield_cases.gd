extends RefCounted
## B-only regression: the authoritative actor/queue/projectile objects must change.
const Runtime = preload("res://scripts/rework/combat/combat_runtime.gd")
const Events = preload("res://scripts/rework/combat/events.gd")
const CEffects = preload("res://scripts/rework/combat/c_effect_adapter.gd")
const Production = preload("res://scripts/rework/combat/production_cpu_adapter.gd")

static func _near(a: float, b: float) -> bool:
    return absf(a - b) < 0.0001

static func _hit(world: Variant, damage: float, kind: String = "true") -> void:
    world.bus.submit_root({"kind": "damage", "actor_id": "E", "target_id": "P",
        "source_tags": ["test_attack"], "attack": {
        "damage_type": kind, "base_damage": damage, "ad_ratio": 0.0,
        "ap_ratio": 0.0, "can_crit": false}})
    world.step_fixed({"P": {"fire": false}})

static func _shield(adapter: Variant, source: String, amount: float) -> bool:
    adapter._context = {"event_id": "shield-test", "kind": "skill_cast",
        "actor_id": "P", "target_id": "P", "tick": adapter.combat.fixed_tick}
    var effect: Dictionary = {"type": "shield", "actor_id": "P",
        "plugin_id": source, "amount": amount}
    return bool(adapter._authoritative_sink([effect], adapter._world()).get("ok", false))

static func run(check: Callable) -> void:
    var bus = Events.new()
    bus.max_pending_events = 3
    bus.set_tick(1)
    for i in range(3):
        check.call(not bus.submit_root({"kind": "probe", "actor_id": "P",
            "target_id": str(i)}).is_empty(), "B FIFO accepts within cap")
    check.call(bus.submit_root({"kind": "probe", "target_id": "overflow"}).is_empty()
        and int(bus.metrics["overflow_rejected"]) == 1
        and bus.pending_count() == 3, "B FIFO rejects overflow with explicit metric")
    var first_batch: Array[Dictionary] = bus.drain_batch(2)
    check.call(first_batch.size() == 2 and str(first_batch[0]["target_id"]) == "0"
        and str(first_batch[1]["target_id"]) == "1", "B FIFO retains enqueue order")
    check.call(not bus.submit_root({"kind": "probe", "target_id": "new"}).is_empty(),
        "B FIFO recovers admission after draining")
    var last_batch: Array[Dictionary] = bus.drain()
    check.call(last_batch.size() == 2 and str(last_batch[0]["target_id"]) == "2"
        and str(last_batch[1]["target_id"]) == "new", "deferred B events never re-order")

    var dedup_bus = Events.new()
    dedup_bus.max_pending_events = 1
    dedup_bus.submit_root({"kind":"damage", "hit_key":"a", "target_id":"E"})
    check.call(dedup_bus.submit_root({"kind":"damage", "hit_key":"b",
        "target_id":"E"}).is_empty(), "overflow blocks second collision")
    dedup_bus.drain()
    check.call(not dedup_bus.submit_root({"kind":"damage", "hit_key":"b",
        "target_id":"E"}).is_empty(), "rejected collision key can retry after relief")

    var fast = Runtime.new(42, {"encounter_id":"bounded",
        "max_active_projectiles":2, "attack_speed_cap":600.0, "profile_window_ticks":16})
    fast.register_actor("P", "vaccine", Vector3.ZERO,
        {"attack_speed":100000.0, "move_speed":0.0}, {"player_controlled":true,
        "projectile_lifetime_ticks":120, "projectile_speed":3.0})
    for tick in range(30):
        fast.step_fixed({"P":{"fire":true, "aim_world":Vector3(100,0,0)}})
    var stats: Dictionary = fast.profiling_snapshot()
    check.call(fast.projectiles.size() == 2
        and int(fast.counters["fire_backpressured"]) > 0,
        "extreme attack speed cannot allocate unlimited real B projectiles")
    check.call(int(stats["ticks"]) == 16 and float(stats["mean_us"]) >= 0.0
        and int(stats["p99_us"]) >= int(stats["p95_us"])
        and int(stats["max_pending"]) >= 0,
        "B profiler provides rolling mean/p95/p99 and queue high water")

    # Minimal immutable D tile contract, without touching D owner files.
    var tiles: PackedInt32Array = PackedInt32Array()
    tiles.resize(25)
    tiles.fill(0)
    tiles[2 * 5 + 0] = 1
    tiles[2 * 5 + 2] = 1
    var battle = Production.new()
    battle.map_plan = {"width":5, "height":5, "tiles":tiles}
    var wall: Dictionary = battle._clip_projectile_to_floor(
        Vector3(0.6,0,3.0), Vector3(3.2,0,3.0), {})
    check.call(bool(wall["blocked"]) and float(wall["position"].x) < 1.2,
        "high-speed B projectile stops at the first D wall boundary")
    check.call(not bool(battle._clip_projectile_to_floor(
        Vector3(0.2,0,3.0), Vector3(0.8,0,3.0), {})["blocked"]),
        "B projectile may fly within an open D cell")
    var maze = Runtime.new(8, {"encounter_id":"wall", "default_projectile_speed":240.0})
    maze.register_actor("P", "vaccine", Vector3(0.6,0,3.0),
        {"attack_damage":10.0, "attack_speed":0.0},
        {"player_controlled":true, "projectile_speed":240.0,
        "attack_definition":{"damage_type":"true", "base_damage":10.0}})
    maze.register_actor("E", "virus", Vector3(3.0,0,3.0),
        {"max_health":30.0,"health":30.0,"move_speed":0.0,"attack_speed":0.0},
        {"attack_range":0.0})
    maze.projectile_segment_filter = Callable(battle, "_clip_projectile_to_floor")
    maze._spawn("P", Vector2.RIGHT, {"damage_type":"true", "base_damage":10.0})
    maze.step_fixed({"P":{"fire":false}})
    check.call(_near(float(maze.snapshot("E")["stats"]["health"]),30.0)
        and maze.projectiles.is_empty(), "wall blocks real high-speed HP damage")
    battle.free()

    var combat = Runtime.new(11, {"encounter_id":"barrier"})
    combat.register_actor("P","vaccine",Vector3.ZERO,
        {"max_health":100.0,"health":100.0,"attack_speed":0.0,"move_speed":0.0},
        {"player_controlled":true})
    combat.register_actor("E","virus",Vector3(4,0,0),
        {"attack_speed":0.0,"move_speed":0.0}, {"attack_range":0.0})
    var effects = CEffects.new()
    check.call(effects.attach(combat, "P", "shield-proof", 99),
        "B adapter attaches actual C command sink")
    check.call(effects.configure_shield_policy({"stacking":"add",
        "absorption":"post_resistance", "duration_ticks":3, "maximum_total":20.0}),
        "shield expiry, stacking and caps are configurable, not GDD fixed")
    check.call(_shield(effects,"nv_c_periodic_shield",8.0)
        and _shield(effects,"nv_c_skill_shield",6.0)
        and _near(effects.shield_value(),14.0),
        "two real C shield commands create distinct B layers")
    _hit(combat,10.0)
    check.call(_near(float(combat.snapshot("P")["stats"]["health"]),100.0)
        and _near(effects.shield_value(),4.0),
        "shield consumes first and HP cannot be double damaged")
    _hit(combat,8.0)
    check.call(_near(float(combat.snapshot("P")["stats"]["health"]),96.0)
        and _near(effects.shield_value(),0.0),
        "shield depletion forwards only remainder to authoritative B HP")
    check.call(effects.configure_shield_policy({"absorption":"true_damage_bypasses",
        "duration_ticks":3}), "A may select true-damage bypass policy")
    check.call(_shield(effects,"nv_r_dash_shield",7.0),
        "dash-shield command reaches same B barrier ledger")
    _hit(combat,6.0)
    check.call(_near(float(combat.snapshot("P")["stats"]["health"]),90.0)
        and _near(effects.shield_value(),7.0),
        "provisional true-damage bypass explicitly skips shield")
    _hit(combat,3.0,"physical")
    check.call(_near(effects.shield_value(),4.0)
        and _near(float(combat.snapshot("P")["stats"]["health"]),90.0),
        "physical damage depletes shield after resistance")
    combat.step_fixed({"P":{"fire":false}})
    check.call(_near(effects.shield_value(),0.0),
        "shield duration expires by fixed tick without healing HP")
    combat.settings["max_active_projectiles"] = 1
    var hp_before_rejection: float = float(combat.snapshot("E")["stats"]["health"])
    var denied: Dictionary = effects._authoritative_sink([
        {"type":"projectiles", "actor_id":"P", "plugin_id":"nv_c_extra_bullet",
            "count":2},
        {"type":"damage", "actor_id":"P", "plugin_id":"nv_c_hit_explosion",
            "target_id":"E", "attack":{"damage_type":"true", "base_damage":9.0}}
    ], effects._world())
    check.call(not bool(denied.get("ok", false))
        and str(denied.get("reason", "")) == "B_projectile_capacity_rejected"
        and _near(float(combat.snapshot("E")["stats"]["health"]), hp_before_rejection)
        and combat.projectiles.is_empty(),
        "C projectile batch preflight rejects all commands without partial B damage")
    effects.maximum_pending_events = 2
    effects._enqueue("skill_cast", {"actor_id":"P", "target_id":"E"})
    effects._enqueue("skill_cast", {"actor_id":"P", "target_id":"E"})
    effects._enqueue("skill_cast", {"actor_id":"P", "target_id":"E"})
    check.call(effects.fault == "C_event_queue_overflow"
        and effects.outstanding_events() == 2
        and effects.overflow_rejected == 1,
        "C overload fails closed and reports pending events without silent drops")
