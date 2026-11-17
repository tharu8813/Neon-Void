extends SceneTree
## Five independent physical Godot zones in non-canonical order, one preview run.
## Real E-protected production five-region play requires A/E approval; no save here.
const Scene = preload("res://scenes/rework/world/cpu_vertical_slice.tscn")
const Board = preload("res://scripts/rework/world/motherboard_layout.gd")
const Maps = preload("res://scripts/rework/world/procedural_map.gd")
var failures: int = 0

func check(ok: bool, what: String) -> void:
    if not ok:
        failures += 1
        printerr("FIVE WORLD ROUTE FAIL: ", what)

func _initialize() -> void:
    call_deferred("_run")

func _kill(world: Variant, actor_id: String) -> void:
    for n in range(70):
        if not world.combat.enemies.has(actor_id):
            break
        world.combat._apply_hit(actor_id, "five-hardware:%s:%d" % [actor_id, n])
    check(not world.combat.enemies.has(actor_id), "B-preview actor defeated: " + actor_id)

func _dodge_tile(plan: Dictionary, anchor: Vector3, horizontal: bool) -> Vector3:
    for y in range(1, int(plan["height"]) - 1):
        for x in range(1, int(plan["width"]) - 1):
            var cell := Vector2i(x, y)
            if not Maps.is_floor(plan, cell):
                continue
            var at: Vector3 = Maps.cell_to_world(cell)
            var offset: Vector2 = Vector2(at.x - anchor.x, at.z - anchor.z)
            if offset.length() <= 3.8 and absf(offset.y if horizontal else offset.x) > 0.65:
                return at + Vector3.UP * 0.5
    return Vector3.INF

func _activate(world: Variant, where: Vector3) -> Dictionary:
    world.player.global_position = where + Vector3.UP * 0.5
    return world.interact()

func _complete_gpu(world: Variant) -> void:
    for i in range(3):
        check(_activate(world, world.hardware.objective_world("disable_emitter")).get(
            "objective_id", "") == "disable_emitter", "GPU switch")
    check(_activate(world, world.hardware.objective_world("stabilize_coolant")).get(
        "objective_id", "") == "stabilize_coolant", "GPU coolant")
    var anchor: Vector3 = world.hardware.objective_world("stabilize_coolant")
    var x: Vector3 = _dodge_tile(world.hardware.map_plan, anchor, true)
    var z: Vector3 = _dodge_tile(world.hardware.map_plan, anchor, false)
    check(x.is_finite() and z.is_finite(), "GPU has dodge tiles on seeded 3D map")
    if x.is_finite() and z.is_finite():
        for trial in range(8):
            for frame in range(90):
                world.region_session.tick_player(x if trial % 2 == 0 else z)
    for i in range(8):
        _kill(world, "GPU-OVERLOAD-%02d" % i)
    _kill(world, "GPU-BOSS")

func _complete_ram(world: Variant) -> void:
    for i in range(4):
        check(_activate(world, world.hardware.objective_world("purge_corruption")).get(
            "objective_id", "") == "purge_corruption", "RAM purge")
    for i in range(2):
        var correct: int = world.region_session.real_slot
        var pad: Vector3 = world.region_session.pads[correct]["position"]
        check(bool(_activate(world, pad).get("authentic", false)), "RAM genuine checksum")
    for i in range(8):
        _kill(world, "RAM-MIRROR-%02d" % i)
    _kill(world, "RAM-BOSS")

func _complete_storage(world: Variant, id: String) -> void:
    for target in world.region_session.targets:
        check(_activate(world, target["position"]).get("type", "") == "storage_node_activated",
            id + " physical storage objective")
    if id == "SSD":
        var seal: Vector3 = world.hardware.objective_world("seal_transfer")
        check(_activate(world, seal).get("objective_id", "") == "seal_transfer", "SSD seal")
        for frame in range(1800):
            world.region_session.tick_player(seal + Vector3.UP * 0.5)
    var count: int = 10 if id == "SSD" else 12
    for i in range(count):
        _kill(world, "%s-HOSTILE-%02d" % [id, i])
    _kill(world, id + "-BOSS")

func _complete_cpu(world: Variant) -> void:
    var core: Vector3 = world.hardware.objective_world("secure_core")
    check(_activate(world, core).get("objective_id", "") == "secure_core", "CPU core")
    for frame in range(2400):
        world.cpu_session.tick_player(core + Vector3.UP * 0.5)
    check(_activate(world, world.hardware.objective_world("stop_intruders")).get(
        "objective_id", "") == "stop_intruders", "CPU intrusion")
    for i in range(10):
        _kill(world, "CPU-VIRUS-%02d" % i)
    _kill(world, "CPU-BOSS")

func _run() -> void:
    var game: Variant = Scene.instantiate()
    root.add_child(game)
    check(game.begin_preview_mission(42), "seeded mission start")
    for enemy in Board.INTRO:
        _kill(game, str(enemy["id"]))
    check(game.board.board.gates_unlocked(), "three introductory B actors dead")
    var sequence: Array[String] = ["GPU", "RAM", "HDD", "SSD", "CPU"]
    var actual: Array[String] = []
    var facts: Array = []
    game.mission_returned.connect(func(fact: Dictionary) -> void: facts.append(fact))
    for id in sequence:
        if game.current_mode != "motherboard":
            check(false, id + " not on motherboard")
            break
        check(game.board.board.gates_unlocked(), "gates stay unlocked")
        var request: Dictionary = _activate(game, Board.GATES[id]["position"])
        check(str(request.get("region_id", "")) == id, "noncanonical gate choice " + id)
        if game.current_mode != id.to_lower():
            check(false, "region scene entered: " + id)
            break
        actual.append(id)
        check(Maps.all_floor_reachable(game.hardware.map_plan), "reachable " + id + " map")
        check(not game.world_state.is_extraction_open(id), "exit locked before boss " + id)
        match id:
            "GPU":
                _complete_gpu(game)
            "RAM":
                _complete_ram(game)
            "SSD", "HDD":
                _complete_storage(game, id)
            "CPU":
                _complete_cpu(game)
        check(game.world_state.is_extraction_open(id), "boss-cleared region " + id)
        check(game.hardware.extraction_open, "physical extraction marker for " + id)
        if id != "CPU":
            var ret: Dictionary = _activate(game, game.hardware.spawn_world())
            check(ret.get("type", "") == "hardware_board_return_requested",
                "board return keeps mission open " + id)
            check(game.current_mode == "motherboard", "returned to live motherboard " + id)
        else:
            check(_activate(game, game.hardware.extraction_world()).get(
                "type", "") == "hardware_extraction_requested", "full mission exit")
    check(actual == sequence, "freely chosen order is preserved")
    check(game.current_mode == "hub", "full clear ends in hub")
    check(facts.size() == 1, "single extraction fact")
    if facts.size() == 1:
        check(bool(facts[0].get("full_clear", false)), "all five generates full-clear flag")
        check(int(facts[0].get("cleared_count", 0)) == 5,
            "full hardware count is exactly five")
        check(facts[0].get("cleared_regions", []).size() == 5,
            "cleaned regions are all carried in E-bound receipt")
    game.queue_free()
    if failures == 0:
        print("D FIVE HARDWARE ROUTE PASS: GPU -> RAM -> HDD -> SSD -> CPU -> full extraction")
    quit(0 if failures == 0 else 1)
