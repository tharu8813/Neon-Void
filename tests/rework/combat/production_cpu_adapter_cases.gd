extends RefCounted
## B integration suite. Tests the production drop-in bridge against the real
## D scene and C effect runtime without editing D or touching persistent saves.
const Production = preload("res://scripts/rework/combat/production_cpu_adapter.gd")
const Motor = preload("res://scripts/rework/world/preview_player_motor.gd")
const CameraRig = preload("res://scripts/rework/camera/look_ahead_camera.gd")
const Statuses = preload("res://scripts/rework/combat/statuses.gd")
const Runtime = preload("res://scripts/rework/combat/combat_runtime.gd")
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
    var hazard_notices: Array = []
    battle.enemy_defeated.connect(func(id: String, receipt: String) -> void:
        kills.append(id)
        receipts.append(receipt))
    battle.player_died.connect(func() -> void: deaths.append(true))
    battle.health_updated.connect(func(current: float, _maximum: float) -> void:
        hp.append(current))
    battle.combat_notice.connect(func(e: Dictionary) -> void:
        if str(e.get("kind", "")) == "projectile_spawn":
            spawn_notices.append(e)
        if str(e.get("kind", "")) == "hit" and bool(e.get("environment_source", false)):
            hazard_notices.append(e))
    battle.begin_encounter(player, camera, {}, "B-proof-1")
    check.call(battle.running and battle.simulation.actors.has("VACCINE"),
        "D drop-in begins real B live simulation and registers player")
    var environment: Dictionary = {"run_id": "B-proof-1", "source_id": "floor-heat",
        "region_id": "CPU", "event_id": "touch-1", "target_id": "VACCINE",
        "damage_type": "magic", "base_damage": 7.0, "repeat_ticks": 0}
    check.call(bool(battle.queue_environment_hit(environment).get("ok", false)),
        "production B accepts D environment descriptor without direct HP edit")
    battle.set_combat_input({"fire": false, "move": Vector2.ZERO})
    battle.advance_production_tick()
    check.call(close_to(float(battle.actor_snapshot()["stats"]["health"]), 93.0) and
        hazard_notices.size() == 1, "real B/C facade resolves hazard and publishes hit")
    check.call(str(battle.queue_environment_hit(environment).get("reason", "")) ==
        "environment_contact_cooldown", "D contact replay cannot double damage")
    environment["run_id"] = "stale-run"
    check.call(str(battle.queue_environment_hit(environment).get("reason", "")) ==
        "stale_environment_run", "stale world hazard fails closed")
    check.call(battle.combat_hud_snapshot().get("player", {}).has("skill_state"),
        "A HUD receives a defensive read-only combat snapshot")
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

    battle.spawn_hostile({"actor_id": "BOSS", "position": Vector3(4,0,0),
        "boss": true, "role": "kernel_overseer"})
    var control: Dictionary = Statuses.apply(
        battle.simulation.actors["BOSS"]["status_state"],
        {"id":"root", "control":true, "duration_ticks":120,
         "stack_mode":"refresh", "flags":["rooted"]})
    check.call(int(control.get("duration_ticks", -1)) == 60,
        "live boss control resistance applied in B status engine")
    Statuses.apply(battle.simulation.actors["BOSS"]["status_state"],
        {"id": "boss_stop", "duration_ticks": 90, "stack_mode": "refresh",
         "control": true, "flags": ["stopped"]})
    battle.enemies["BOSS"]["pattern_timer"] = 1
    var boss_shots_before: int = battle.simulation.projectiles.size()
    battle._run_boss_patterns()
    check.call(battle.simulation.projectiles.size() == boss_shots_before and
        int(battle.enemies["BOSS"]["pattern_timer"]) ==
        battle.PATTERN_TELEGRAPH_TICKS + 1,
        "controlled boss cannot bypass B attack gate with scripted volleys")
    var movement_buff: Dictionary = Statuses.apply(
        battle.simulation.actors["VACCINE"]["status_state"],
        {"id":"temporary_slow", "duration_ticks":120,
         "stack_mode":"refresh", "stat_modifiers":{"move_speed":-3.0,
         "max_mana":50.0, "mana_regen":60.0}})
    check.call(bool(movement_buff.get("ok", false)),
        "live player movement and mana stats can be modified by status")
    battle.simulation.actors["VACCINE"]["skills"].mana = 50.0
    var origin_x: float = player.global_position.x
    battle.set_combat_input({"move": Vector2.LEFT, "dash": true, "fire": false,
        "aim_world": player.global_position + Vector3.RIGHT * 5.0})
    var dash_notice: Array[Dictionary] = battle.advance_production_tick()
    check.call(player.global_position.x < origin_x - 0.5,
        "dash moves left despite right-aiming mouse")
    check.call(dash_notice.any(func(e: Dictionary) -> bool:
        return str(e.get("kind", "")) == "dash"),
        "real dash event emitted")
    check.call(close_to(float(player.get("walk_speed")), 2.0),
        "B slow debuff updates D's actual 3D motor speed")
    check.call(close_to(float(battle.simulation.actors["VACCINE"]["skills"].max_mana), 150.0) and
        close_to(float(battle.simulation.actors["VACCINE"]["skills"].mana_regen), 65.0),
        "temporary max-mana/regen stat changes refresh live B skill controller")
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
    check.call(battle.learn_active_skill({"id":"shield_dash", "mana_cost":5.0,
        "cooldown_ticks":60}, 1, true), "second active slot learned")
    check.call(battle.learn_active_skill({"id":"signal_burst", "mana_cost":12.0,
        "cooldown_ticks":120, "attack":{"damage_type":"true",
        "base_damage":3.0}}, 2, true), "third active slot learned")
    var before_slot_three: float = float(battle.simulation.snapshot("VACCINE")["mana"])
    battle.set_combat_input({"fire":false, "skill_slot":2,
        "aim_world": Vector3(4,0,0)})
    var third_notices: Array[Dictionary] = battle.advance_production_tick()
    check.call(float(battle.simulation.snapshot("VACCINE")["mana"]) < before_slot_three and
        third_notices.any(func(e: Dictionary) -> bool:
            return str(e.get("kind", "")) == "skill_cast"),
        "third skill slot casts through live bridge with mana and event")
    check.call(battle.diagnostics()["tick"] >= 3, "fixed-tick clock advanced")
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

    # Attack-speed hard ceiling exists by GDD, but the numerical 5 is only
    # a test override, never an approved fixed balance rule.
    var rate_test = Runtime.new(555, {"encounter_id":"rate-cap", "attack_speed_cap":5.0})
    rate_test.register_actor("P", "vaccine", Vector3.ZERO,
        {"attack_speed":100000.0},
        {"player_controlled":true})
    for rate_tick in range(60):
        rate_test.step_fixed({"P":{"fire":true, "aim_world":Vector3(20,0,0)}})
    check.call(int(rate_test.counters["shots"]) <= 6,
        "configurable attack-speed cap bounds 60Hz shot submissions")

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
    # Production (non-synthetic) B C-event -> real 3D hostile body movement.
    battle.reset()
    player.global_position = Vector3(0, 0.5, 0)
    battle.begin_encounter(player, camera, {}, "knockback-live-proof")
    check.call(battle.spawn_hostile({"actor_id": "KNOCK",
        "position": Vector3(1, 0, 0)}), "3D knockback target registered")
    check.call(battle.configure_plugins({"nv_r_low_hp_knockback": 1}, 4242),
        "real C low HP knockback installed in B production bridge")
    Statuses.apply(battle.simulation.actors["KNOCK"]["status_state"],
        {"id": "root_forced_move", "duration_ticks": 100,
        "control": true, "flags": ["rooted"]})
    battle.simulation.actors["VACCINE"]["stats"]["health"] = 25.0
    battle.simulation.bus.submit_root({
        "kind": "damage", "actor_id": "KNOCK", "target_id": "VACCINE",
        "source_tags": ["enemy_attack"],
        "attack": {"damage_type": "true", "base_damage": 1.0, "can_crit": false}})
    battle.set_combat_input({"fire": false, "move": Vector2.ZERO})
    var knock_notes: Array[Dictionary] = battle.advance_production_tick()
    var physical: CharacterBody3D = battle.enemies["KNOCK"]["body"]
    check.call(physical.global_position.x > 2.5 and
        close_to(physical.global_position.x,
            float(battle.simulation.snapshot("KNOCK")["position"].x)),
        "C low HP proc moves actual 3D body and reconciles B authoritative XZ state")
    var knock_reported: bool = false
    for notice in knock_notes:
        if str(notice.get("kind", "")) == "knockback_applied":
            knock_reported = true
    check.call(knock_reported, "A HUD receives real knockback applied notice")
    battle.reset()
    player.global_position = Vector3(0, 0.5, 0)
    battle.begin_encounter(player, camera, {}, "death-proof")
    var player_actor: Dictionary = battle.simulation.actors["VACCINE"]
    player_actor["stats"]["health"] = 1.0
    check.call(battle.spawn_hostile({"actor_id":"CLOSE_ENEMY",
        "position":Vector3(0.4,0,0)}), "melee-range attacker registered")
    battle.set_combat_input({"fire":false, "move":Vector2.ZERO})
    battle.advance_production_tick()
    check.call(battle.game_over and deaths.size() == 1,
        "real enemy shot causes one player death callback")
    check.call(not hp.is_empty() and close_to(hp.back(), 0.0),
        "player death publishes final zero HP for D/A HUD")
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
