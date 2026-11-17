extends RefCounted
## B integration suite. Tests the production drop-in bridge against the real
## D scene and C effect runtime without editing D or touching persistent saves.
const Production = preload("res://scripts/rework/combat/production_cpu_adapter.gd")
const Motor = preload("res://scripts/rework/world/preview_player_motor.gd")
const CameraRig = preload("res://scripts/rework/camera/look_ahead_camera.gd")
const Statuses = preload("res://scripts/rework/combat/statuses.gd")
const DScene = preload("res://scenes/rework/world/cpu_vertical_slice.tscn")
const Board = preload("res://scripts/rework/world/motherboard_layout.gd")

static func close_to(a: float, b: float) -> bool:
    return absf(a - b) <= 0.0001

static func hit_until_dead(bridge: Variant, id: String, check: Callable) -> void:
    for i in range(40):
        if not bridge.enemies.has(id):
            break
        bridge._apply_hit(id, "b:death:%s:%d" % [id, i])
    check.call(not bridge.enemies.has(id), "B production test hit finishes " + id)

static func run(check: Callable, scene_root: Node) -> void:
    var host := Node3D.new()
    host.name = "BProductionIntegrationFixture"
    scene_root.add_child(host)
    var player = Motor.new()
    player.name = "SyntheticPlayer"
    host.add_child(player)
    player.global_position = Vector3(0, 0.5, 0)
    var camera = CameraRig.new()
    host.add_child(camera)
    var battle = Production.new()
    battle.name = "BCombatProduction"
    host.add_child(battle)
    var kills: Array[String] = []
    var receipts: Array[String] = []
    var deaths: Array = []
    var hp: Array[float] = []
    var spawn_notices: Array = []
    battle.enemy_defeated.connect(func(id: String, receipt: String) -> void:
        kills.append(id)
        receipts.append(receipt))
    battle.player_died.connect(func() -> void: deaths.append(true))
    battle.health_updated.connect(func(current: float, _maximum: float) -> void:
        hp.append(current))
    battle.combat_notice.connect(func(e: Dictionary) -> void:
        if str(e.get("kind", "")) == "projectile_spawn":
            spawn_notices.append(e))
    battle.begin_encounter(player, camera, {}, "B-proof-1")
    check.call(battle.running and battle.simulation.actors.has("VACCINE"),
        "D drop-in begins real B live simulation and registers player")
    check.call(battle.spawn_hostile({"actor_id": "E1", "position": Vector3(2,0,0)}),
        "spawn register live hostile")
    check.call(not battle.spawn_hostile({"actor_id": "E1", "position": Vector3(2,0,0)}),
        "duplicate actor ID rejected")
    battle._apply_hit("E1", "B-proof-hit-1")
    check.call(close_to(float(battle.simulation.snapshot("E1")["stats"]["health"]), 11.0),
        "D compatibility hit calls B 4+5 AD damage, not copied damage")
    battle._apply_hit("E1", "B-proof-hit-1")
    check.call(close_to(float(battle.simulation.snapshot("E1")["stats"]["health"]), 11.0),
        "same impact key cannot hit one target twice")
    battle._apply_hit("E1", "B-proof-hit-2")
    battle._apply_hit("E1", "B-proof-hit-3")
    check.call(kills.size() == 1 and receipts.size() == 1 and
        receipts[0] == "B-proof-1:enemy:E1:death",
        "one B death produces one D/E-stable kill receipt")
    check.call(not battle.enemies.has("E1"),
        "defeated actor removed from D-facing enemy table")

    battle.spawn_hostile({"actor_id": "BOSS", "position": Vector3(4,0,0), "boss": true})
    var control: Dictionary = Statuses.apply(
        battle.simulation.actors["BOSS"]["status_state"],
        {"id":"root", "control":true, "duration_ticks":120,
         "stack_mode":"refresh", "flags":["rooted"]})
    check.call(int(control.get("duration_ticks", -1)) == 60,
        "live boss control resistance applied in B status engine")
    var origin_x: float = player.global_position.x
    battle.set_combat_input({"move": Vector2.LEFT, "dash": true, "fire": false,
        "aim_world": player.global_position + Vector3.RIGHT * 5.0})
    var dash_notice: Array[Dictionary] = battle.advance_production_tick()
    check.call(player.global_position.x < origin_x - 0.5,
        "dash moves left despite right-aiming mouse")
    check.call(dash_notice.any(func(e: Dictionary) -> bool:
        return str(e.get("kind", "")) == "dash"),
        "real dash event emitted")
    check.call(battle.learn_active_skill({"id":"pulse", "mana_cost":10.0,
        "cooldown_ticks":60, "attack":{"damage_type":"magic",
        "base_damage":7.0}}, 0, true), "learn and equip first of three skill slots")
    var before_mana: float = float(battle.simulation.snapshot("VACCINE")["mana"])
    battle.set_combat_input({"fire": false, "skill_slot": 0,
        "aim_world": Vector3(4,0,0)})
    battle.advance_production_tick()
    check.call(float(battle.simulation.snapshot("VACCINE")["mana"]) < before_mana,
        "actual C-compatible skill spends mana after 60Hz regeneration")
    check.call(spawn_notices.size() > 0, "projectile spawn reaches presentation signal")
    check.call(battle.diagnostics()["tick"] >= 2, "fixed-tick clock advanced")
    battle.reset()
    check.call(battle.enemies.is_empty() and not battle.running and
        battle.simulation.actors.is_empty(), "reset clears actors and combat physics")

    # A/C combined tree: no C recipes duplicated in B.
    battle.begin_encounter(player, camera, {}, "B-C-bridge-proof")
    check.call(battle.spawn_hostile({"actor_id":"TARGET", "position":Vector3(2,0,0)}),
        "re-register after region change")
    check.call(battle.spawn_hostile({"actor_id":"NEXT", "position":Vector3(4,0,0)}),
        "chain effect has a second nearby real actor")
    var configured: bool = battle.configure_plugins({
        "nv_c_chain_lightning": 1, "nv_c_armor_shred": 1}, 707)
    check.call(configured, "configure existing C effect engine using E-owned levels")
    if configured:
        var before_next: float = float(battle.simulation.snapshot("NEXT")["stats"]["health"])
        battle._apply_hit("TARGET", "C-proof-primary-hit")
        var after_next: float = float(battle.simulation.snapshot("NEXT")["stats"]["health"])
        check.call(after_next < before_next,
            "C real lightning command changes B authoritative second-target HP")
        check.call(not battle.simulation.actors["TARGET"]["status_state"]["statuses"].is_empty(),
            "C armor shred applied with B Statuses.apply to real target")
        check.call(battle.effects.fault.is_empty(),
            "C sink accepted actual B damage/status without duplicate application")
        check.call(battle.effects.outstanding_events() == 0,
            "C multi-step proc chain drained without runaway feedback")
    battle.reset()
    battle.begin_encounter(player, camera, {}, "extra-bullet-efficiency")
    battle.spawn_hostile({"actor_id":"E_PRIMARY", "position":Vector3(2,0,0)})
    battle.spawn_hostile({"actor_id":"E_CHAIN", "position":Vector3(4,0,0)})
    check.call(battle.configure_plugins({"nv_c_chain_lightning":1}, 246),
        "C derived projectile can trigger real B chain lightning")
    var original_target: float = float(battle.simulation.snapshot("E_CHAIN")["stats"]["health"])
    battle.simulation.bus.submit_root({
        "kind":"damage", "actor_id":"VACCINE", "target_id":"E_PRIMARY",
        "source_tags":["plugin_attack", "derived_attack"],
        "allowed_proc_triggers":["on_hit"],
        "proc_path":["test_extra|VACCINE|E_PRIMARY"],
        "on_hit_damage_efficiency":0.7,
        "attack":{"damage_type":"physical", "base_damage":1.0,
            "is_on_hit":true, "can_crit":false}
    })
    battle.simulation.presentation.clear()
    var derived: Array[Dictionary] = battle.simulation.bus.drain_batch(512,
        Callable(battle.simulation, "_resolve_event"))
    battle.effects.notices.clear()
    battle.effects.after_fixed_step(derived)
    var resulting_target: float = float(battle.simulation.snapshot("E_CHAIN")["stats"]["health"])
    check.call(close_to(original_target - resulting_target, 8.4),
        "GDD 70 percent on-hit efficiency scales proc magic damage, not bullet damage")
    check.call(battle.effects.fault.is_empty(),
        "derived projectile C/B proc path accepted without circular trigger")
    battle.reset()
    battle.begin_encounter(player, camera, {}, "solo-lightning")
    battle.spawn_hostile({"actor_id":"SOLO", "position":Vector3(2,0,0)})
    check.call(battle.configure_plugins({"nv_c_chain_lightning":1}, 234),
        "C lightning enabled when alone")
    battle._apply_hit("SOLO", "solo:hit")
    check.call(battle.effects.fault.is_empty() and
        float(battle.simulation.snapshot("SOLO")["stats"]["health"]) < 20.0,
        "single enemy: C lightning no-op must not break B normal attack")
    battle.reset()

    # P2 synthetic crowd: real actor bodies + B pooled projectiles, without
    # making a machine-specific FPS assertion from a GitHub runner.
    battle.begin_encounter(player, camera, {}, "crowd-100")
    for i in range(100):
        var row: int = floori(float(i) / 10.0)
        check.call(battle.spawn_hostile({"actor_id": "mob-" + str(i),
            "position": Vector3(float(8 + (i % 10) * 3), 0.0, float(8 + row * 3))}),
            "crowd actor registered: " + str(i))
    var attack: Dictionary = battle.simulation.actors["VACCINE"]["attack_definition"]
    for j in range(250):
        battle.simulation._spawn("VACCINE", Vector2.RIGHT, attack)
    var clock: int = Time.get_ticks_usec()
    for tick in range(5):
        battle.set_combat_input({"fire": false, "move": Vector2.ZERO,
            "aim_world": Vector3(12,0,0)})
        battle.advance_production_tick()
    var elapsed: int = Time.get_ticks_usec() - clock
    check.call(battle.simulation.counters["shots"] >= 250 and
        battle.simulation.counters["broadphase_candidates"] < 2500,
        "100 enemies / 250 bullets processed with spatial candidate reduction")
    check.call(battle.enemies.size() == 100 and battle.effects.fault.is_empty(),
        "crowd canary preserves stable enemy registry and C runtime")
    print("B PRODUCTION LOAD CANARY: 100 live enemies, 250 projectiles, 5 ticks; elapsed_us=", elapsed,
        " (not a measured Windows FPS guarantee)")
    battle.reset()
    host.queue_free()

    # Actual D CPU scene is reused unchanged: hot-swap only in test memory.
    var d_world: Variant = DScene.instantiate()
    scene_root.add_child(d_world)
    var old: Variant = d_world.combat
    d_world.remove_child(old)
    old.queue_free()
    var live = Production.new()
    live.name = "BReplacementForDDemo"
    d_world.add_child(live)
    d_world.combat = live
    live.enemy_defeated.connect(Callable(d_world, "_on_enemy_defeated"))
    live.player_died.connect(Callable(d_world, "_on_player_died"))
    d_world.enter_hub()
    check.call(d_world.begin_preview_mission(42),
        "D unchanged demo begins mission with B replacement bridge")
    check.call(d_world.current_mode == "motherboard" and live.enemies.size() == 3,
        "three D motherboard enemies registered in B production runtime")
    for entry in Board.INTRO:
        hit_until_dead(live, str(entry["id"]), check)
    check.call(d_world.board.board.gates_unlocked(),
        "B production kill receipts unlock D physical motherboard gates")
    var gate_position: Vector3 = Board.GATES["CPU"]["position"]
    d_world.player.global_position = gate_position + Vector3.UP * 0.5
    check.call(str(d_world.interact().get("region_id", "")) == "CPU",
        "D unchanged world transitions from board to CPU using B adapter")
    check.call(d_world.current_mode == "cpu" and live.running and
        live.simulation.actors.has("VACCINE"),
        "CPU re-registration starts fresh B encounter, no board actors leak")
    check.call(live.diagnostics()["enemies"] == 0,
        "CPU begins with no stale introductory hostiles")
    d_world.queue_free()
