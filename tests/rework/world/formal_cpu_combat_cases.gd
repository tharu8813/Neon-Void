extends RefCounted
## D live-B world integration assertions. Executed by registered CPU smoke on
## BOTH Windows/Linux; never substitute for A's real E persisted pilot smoke.
const LiveB = preload("res://scripts/rework/world/live_b_combat_world.gd")
const Router = preload("res://scripts/rework/world/world_combat_router.gd")
const Player = preload("res://scripts/rework/world/preview_player_motor.gd")
const CameraRig = preload("res://scripts/rework/camera/look_ahead_camera.gd")
const BStatuses = preload("res://scripts/rework/combat/statuses.gd")
const BStats = preload("res://scripts/rework/combat/stats.gd")
var failures: Array[String] = []

func _check(value: bool, description: String) -> void:
    if not value:
        failures.append(description)

func _make_player(world: Node3D, point: Vector3) -> CharacterBody3D:
    var player: CharacterBody3D = Player.new()
    player.position = point + Vector3.UP * 0.5
    player.movement_enabled = false
    world.add_child(player)
    return player

func _make_camera(world: Node3D, point: Vector3) -> Camera3D:
    var camera: Camera3D = CameraRig.new()
    camera.world_bounds = Rect2(Vector2.ZERO, Vector2(28, 20))
    world.add_child(camera)
    camera.global_position = point + Vector3.UP * 14
    return camera

func execute(root: Window) -> Array[String]:
    failures.clear()
    var fixture := Node3D.new()
    root.add_child(fixture)
    var player := _make_player(fixture, Vector3(5, 0, 5))
    var camera := _make_camera(fixture, Vector3(5, 0, 5))
    var adapter: Variant = LiveB.new()
    fixture.add_child(adapter)
    var statuses: Array = []
    var notes: Array = []
    var deaths: Array = []
    adapter.combat_notice.connect(func(note: Dictionary) -> void: notes.append(note.duplicate(true)))
    adapter.enemy_defeated.connect(func(id: String, receipt: String) -> void:
        deaths.append({"actor_id": id, "receipt_id": receipt}))
    adapter.player_died.connect(func() -> void: statuses.append("player_died"))
    var skill: Dictionary = {
        "id": "cpu-smoke-skill", "mana_cost": 10.0, "cooldown_ticks": 60,
        "attack": {"damage_type": "physical", "base_damage": 5.0,
            "ad_ratio": 0.0, "ap_ratio": 0.0, "can_crit": false}
    }
    var plugin: Dictionary = {
        "plugin_id": "c-numeric-authorized-fixture",
        "trigger": {"event": "projectile_hit"},
        "effect_commands": [{"kind": "status", "target_policy": "current",
            "status": {"id": "c-fixture-mark", "duration_ticks": 180,
                "stack_mode": "refresh", "flags": ["marked"]}}]
    }
    _check(adapter.configure_authorized_build({"nv_c_breakable_sprint": 1},
        [plugin], [skill]), "C-owned passive levels/B numeric plugin contract accepted")
    _check(adapter.begin_encounter(player, camera, {}, "b-live-smoke", {
        "seed": 84, "encounter_id": "CPU:test", "player_stats": {}}),
        "B runtime begins registered world combat")
    _check(adapter.bridge.simulation.fixed_tick == 0, "B runtime tick begins zero")
    _check(float(adapter.actor_snapshot("VACCINE").get("stats", {}).get("move_speed", 0.0)) >
        float(BStats.DRAFT_DEFAULTS["move_speed"]), "C passive numerics reach B stats")
    _check(adapter.actor_snapshot("VACCINE").get("slots", []).has(skill["id"]),
        "authorized skill equipped in real B slots")
    _check(adapter.spawn_hostile({"actor_id": "cpu-b-test-1",
        "position": Vector3(8, 0, 5), "boss": false}), "D spawns B-registered actor")
    _check(not adapter.spawn_hostile({"actor_id": "cpu-b-test-1",
        "position": Vector3(8, 0, 5)}), "duplicate B actor IDs rejected")
    var fixture_enemy: Vector3 = Vector3(8, 0.5, 5)
    for tick in range(16):
        var command: Dictionary = {"move": Vector2.ZERO,
            "aim_world": fixture_enemy, "fire": true}
        if tick == 1:
            command["skill_slot"] = 0
        adapter.advance_test_tick({
            "VACCINE": command,
            "cpu-b-test-1": {"move": Vector2.ZERO,
                "aim_world": player.global_position, "fire": false}
        })
    var skill_events: int = 0
    var projectile_events: int = 0
    var hit_events: int = 0
    for entry in notes:
        if entry.get("kind", "") == "skill_cast":
            skill_events += 1
        elif entry.get("kind", "") == "projectile_spawn":
            projectile_events += 1
        elif entry.get("kind", "") == "hit":
            hit_events += 1
    _check(skill_events == 1, "real B cast emits one skill event")
    _check(projectile_events >= 1, "real B auto-fire spawns actual projectile")
    _check(hit_events >= 1, "actual B projectile resolves hit/damage")
    var enemy_state: Dictionary = adapter.actor_snapshot("cpu-b-test-1")
    _check(BStatuses.active_flags(enemy_state.get("status_state", {})).get(
        "marked", false), "B applies C-shaped authorized numeric status command")
    var player_state: Dictionary = adapter.bridge.simulation.actors["VACCINE"]
    var cast_mana: float = float(adapter.actor_snapshot("VACCINE")["mana"])
    _check(cast_mana < 100.0, "B skill cost deducted from B mana pool")
    var rooted: Dictionary = BStatuses.apply(player_state["status_state"], {
        "id": "cpu-test-root", "duration_ticks": 6,
        "stack_mode": "refresh", "control": true, "flags": ["rooted"]})
    _check(rooted.get("ok", false), "B status engine accepts root fixture")
    var before: Vector3 = adapter.actor_snapshot("VACCINE")["position"]
    adapter.advance_test_tick({
        "VACCINE": {"move": Vector2.RIGHT, "aim_world": fixture_enemy,
            "fire": false},
        "cpu-b-test-1": {"move": Vector2.ZERO,
            "aim_world": player.global_position, "fire": false}
    })
    var after: Vector3 = adapter.actor_snapshot("VACCINE")["position"]
    _check(before.distance_to(after) < 0.00001, "B rooted status blocks movement")
    for tick in range(170):
        if not adapter.enemies.has("cpu-b-test-1"):
            break
        adapter.advance_test_tick({
            "VACCINE": {"move": Vector2.ZERO, "aim_world": fixture_enemy,
                "fire": true},
            "cpu-b-test-1": {"move": Vector2.ZERO,
                "aim_world": player.global_position, "fire": false}
        })
    _check(not adapter.enemies.has("cpu-b-test-1"), "B authoritative damage kills enemy")
    _check(deaths.size() == 1, "B death emits exactly one D receipt")
    if deaths.size() == 1:
        _check(deaths[0]["receipt_id"] == "world:b-live-smoke:cpu-b-test-1:death",
            "stable D receipt does not depend on volatile B event_id")
    _check(not adapter.actor_snapshot("cpu-b-test-1").get("alive", true),
        "B snapshot marks dead actor")
    for tick in range(3):
        adapter.advance_test_tick({"VACCINE": {"fire": false}})
    _check(deaths.size() == 1, "dead actor cannot issue a second reward receipt")
    adapter.reset()
    _check(adapter.enemies.is_empty(), "B adapter reset clears entity binding")
    _check(adapter.begin_encounter(player, camera, {}, "b-death-smoke", {
        "seed": 87, "encounter_id": "CPU:death",
        "player_stats": {"health": 1.0, "max_health": 1.0}}),
        "B reset can start fresh independent encounter")
    _check(adapter.spawn_hostile({"actor_id": "cpu-b-test-boss",
        "position": Vector3(6.8, 0, 5), "boss": true}),
        "boss registered in B runtime")
    for tick in range(100):
        if not statuses.is_empty():
            break
        adapter.advance_test_tick({
            "VACCINE": {"move": Vector2.ZERO,
                "aim_world": Vector3(6.8, 0.5, 5), "fire": false},
            "cpu-b-test-boss": {"move": Vector2.ZERO,
                "aim_world": player.global_position, "fire": true}
        })
    _check(statuses.size() == 1, "actual B hostile damage kills player once")
    _check(not adapter.actor_snapshot("VACCINE").get("alive", true),
        "player death from B actual HP transition")
    var route: Variant = Router.new()
    fixture.add_child(route)
    _check(route.has_signal("enemy_defeated"), "A pilot stable enemy_defeated signal")
    _check(route.has_signal("player_died"), "A pilot stable player_died signal")
    _check(route.has_method("configure_authorized_build"),
        "A can pass E-authorized C/B loadout explicitly")
    route.queue_free()
    fixture.queue_free()
    return failures.duplicate()
