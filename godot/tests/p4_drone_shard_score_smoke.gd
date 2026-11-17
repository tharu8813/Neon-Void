extends SceneTree
## Explicit new gameplay parity gates. Does not open any live user save.
const Combat = preload("res://scripts/combat/combat_sim.gd")
const Main = preload("res://scenes/main.tscn")
var failures: int = 0

func _initialize() -> void:
    create_timer(55.0).timeout.connect(func(): printerr("P4 DRONE/SHARD TIMEOUT"); quit(1))
    call_deferred("_run")

func expect(ok: bool, reason: String) -> void:
    if not ok:
        failures += 1
        printerr("P4 DRONE/SHARD FAIL: " + reason)

func _run() -> void:
    var sim: Variant = Combat.new(42)
    sim.begin_wave(1, [], "")
    sim.set_player_snapshot({
        "pos": Vector2(420.0, 360.0),
        "stats": {"drone": 2, "magnet": 100.0, "dmg": 10.0},
    })
    var enemy_id: int = sim.spawn_enemy("drifter", Vector2(680.0, 360.0))
    expect(enemy_id > 0, "drone-target fixture exists")
    var state_before: int = sim.rng.state
    sim.call("_step_drones")
    expect(sim.bullets.size() == 2 and sim.drone_cd > 0.0,
        "two 52px orbital drones fire two independently aimed shots per 0.55s")
    if sim.bullets.size() >= 2:
        for bullet in sim.bullets:
            expect(is_equal_approx(float(bullet["damage"]), 6.2),
                "original drone damage = basic damage *0.62")
            expect(is_equal_approx(float(bullet["radius"]), 2.88),
                "original drone bullet radius = normal *0.8")
            expect(absf(Vector2(bullet["pos"]).distance_to(Vector2(420, 360)) - 52.0) < 0.01,
                "drone projectile originated on 52px orbit")
    sim.call("_step_drones")
    expect(sim.bullets.size() == 2, "drone 0.55s cooldown prevents duplicate shots")
    expect(sim.rng.state == state_before, "drone shots do not disturb existing combat RNG")

    var collected: Array[int] = []
    sim.shard_collected.connect(func(value: int, location: Vector2) -> void:
        collected.append(value))
    sim.shards.clear()
    sim.shards.append({"pos": Vector2(432.0, 360.0),
        "vel": Vector2.ZERO, "life": 14.0, "value": 13})
    sim.call("_step_shards")
    expect(collected == [13] and sim.shards.is_empty(),
        "close-enough shard pickup emits exactly one score receipt")
    sim.shards.append({"pos": Vector2(470.0, 360.0),
        "vel": Vector2.ZERO, "life": 14.0, "value": 6})
    sim.call("_step_shards")
    expect(sim.shards.size() == 1 and float(sim.shards[0]["pos"].x) < 470.0,
        "magnet causes actual movement toward player without premature pickup")
    var shard_before: Dictionary = sim.shards[0].duplicate(true)
    sim.set_player_snapshot({"stats": {"magnet": 0.0}})
    sim.call("_step_shards")
    expect(float(sim.shards[0]["vel"].x) < 0.0,
        "zero magnet does not add acceleration, preserves prior momentum")
    expect(float(sim.shards[0]["life"]) < float(shard_before["life"]),
        "score shards age at simulation time")

    var rng_unchanged: int = sim.rng.state
    var victim_id: int = sim.spawn_enemy("drifter", Vector2(850, 360))
    sim.damage_enemy(victim_id, 2000.0)
    expect(sim.rng.state == rng_unchanged,
        "score shard random draws are isolated from established C combat RNG")

    var game: Node3D = Main.instantiate()
    game.set("persistence_enabled", false)
    root.add_child(game)
    expect(game.call("start_new_run") and game.call("enter_sector", "CPU"),
        "integrated scoring starts in a temporary OS combat fixture")
    game.call("_on_enemy_died", -1, "drifter")
    expect(int(game.get("score")) == 13 and int(game.get("combo_count")) == 1,
        "Python drifter 12pts x1.1 multiplier =13 points")
    game.call("_on_enemy_died", -1, "shooter")
    expect(int(game.get("score")) == 39 and int(game.get("combo_count")) == 2,
        "Python shooter 22pts x1.2 multiplier =26 additional points")
    game.call("_on_shard_collected", 6, Vector2(420, 360))
    expect(int(game.get("score")) == 45,
        "original score shards add their exact 60% enemy-score value")
    var fx: Variant = game.get("visual_fx")
    expect(fx.damage_labels.size() >= 1 and fx.bursts.size() >= 1,
        "shard credit also draws cyan pickup text and ring")
    var before_render: int = int(game.get("score"))
    fx.call("sync_battle", [], [], [], [], false, [
        {"pos": Vector2(610.0, 360.0), "life": 5.0, "value": 6}
    ])
    expect(fx.shard_batch.multimesh.visible_instance_count == 1,
        "score shard is GPU-instanced instead of a per-shard scene node")
    expect(int(game.get("score")) == before_render,
        "VFX snapshot synchronization never modifies score or simulation")
    fx.call("sync_drones", Vector2(420, 360), 2, 0.0)
    expect(fx.drone_nodes[0].visible and fx.drone_nodes[1].visible
        and not fx.drone_nodes[2].visible,
        "only the acquired number of drone meshes is visible")

    var game_sim: Variant = game.get("sim")
    var blast_id: int = game_sim.spawn_enemy("guardian", Vector2(430, 360))
    var actor: Node3D = game.get_node("LegacyPlayer")
    actor.call("set_logical_position", Vector2(420, 360))
    game.get("software").stats["dash_blast"] = 40.0
    var original_hp: float = float(game_sim.get_enemy(blast_id)["hp"])
    game.call("_on_dash_visual", Vector2.RIGHT)
    var target: Dictionary = game_sim.get_enemy(blast_id)
    expect(not target.is_empty() and float(target["hp"]) < original_hp,
        "installed dash-blast software damages nearby C enemy through true mitigation")
    expect(not target.is_empty() and Vector2(target["vel"]).x > 0.0,
        "dash-blast applies the source outward 140px/s knockback")
    actor.set("dash_dir", Vector2.RIGHT)
    var after_initial: float = float(target["hp"])
    game.call("_apply_dash_blast_pulse")
    expect(float(game_sim.get_enemy(blast_id)["hp"]) < after_initial,
        "ongoing dash applies extra 34-percent blast every third frame")
    game.queue_free()

    if failures == 0:
        print("P4 ORIGINAL DRONES, SHARDS AND COMBO SCORING PASS (headless)")
    quit(0 if failures == 0 else 1)
