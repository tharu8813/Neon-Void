extends SceneTree
## A-only production entry regression: real B kills on the motherboard, D physical
## gate, E durable immutable map ACK, then actual formal B scene for each hardware.
## Five *independent* synthetic missions. NOT a saved five-hardware full playthrough.
const PilotScene = preload("res://scenes/rework/integration/rework_pilot.tscn")
const Gateway = preload("res://scripts/rework/persistence/progression_gateway.gd")
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")
const Board = preload("res://scripts/rework/world/motherboard_layout.gd")
const Maps = preload("res://scripts/rework/world/procedural_map.gd")
const REGIONS := ["CPU", "GPU", "RAM", "SSD", "HDD"]
var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func _check(ok: bool, message: String) -> void:
    if not ok:
        failures += 1
        printerr("REWORK A LIVE HARDWARE ENTRY FAIL: " + message)

func _clean_fixture(path: String) -> void:
    # Never recurse into an arbitrary user folder or erase a real player slot.
    if not path.contains("a-live-entry-smoke-"):
        _check(false, "refused non-fixture cleanup path")
        return
    var directory: DirAccess = DirAccess.open(path)
    if directory == null:
        return
    directory.list_dir_begin()
    var name: String = directory.get_next()
    while not name.is_empty():
        var child: String = path.path_join(name)
        if directory.current_is_dir():
            _clean_fixture(child)
        else:
            DirAccess.remove_absolute(child)
        name = directory.get_next()
    directory.list_dir_end()
    DirAccess.remove_absolute(path)

func _kill_with_real_B(router: Variant, actor_id: String,
        player: Node3D, max_ticks: int = 220) -> bool:
    if router == null or router.live == null or not router.enemies.has(actor_id):
        return false
    for tick in range(max_ticks):
        if not router.enemies.has(actor_id):
            return true
        var target: Vector3 = router.enemies[actor_id]["node"].global_position
        var commands: Dictionary = {
            "VACCINE": {"move": Vector2.ZERO, "aim_world": target, "fire": true}
        }
        for hostile_id in router.enemies.keys():
            commands[str(hostile_id)] = {
                "move": Vector2.ZERO,
                "aim_world": player.global_position, "fire": false
            }
        router.live.advance_test_tick(commands)
    return not router.enemies.has(actor_id)

func _run() -> void:
    var suffix: String = str(OS.get_process_id()) + "-" + str(Time.get_ticks_usec())
    for region in REGIONS:
        paused = false
        var slot_id: String = "a-live-entry-smoke-" + suffix + "-" + region.to_lower()
        var slot_path: String = Store.default_slot_path(slot_id)
        _check(not DirAccess.dir_exists_absolute(slot_path),
            region + ": unique synthetic E slot does not exist")
        var pilot: Node3D = PilotScene.instantiate()
        pilot.set("slot_id", slot_id)
        pilot.set("initial_vaccine_id", "a-smoke-vaccine")
        root.add_child(pilot)
        await process_frame
        var help: Label = pilot.get("message_label") as Label
        _check(help != null and help.text.contains("CPU/GPU/RAM/SSD/HDD"),
            region + ": pilot UI lists all five available hardware choices")
        var world: Node3D = pilot.get_node_or_null("World") as Node3D
        if world == null or bool(pilot.get("frozen")):
            _check(false, region + ": pilot initialized with real E slot")
        else:
            pilot.call("_on_mission_requested", {"seed": 42})
            var run_id: String = str(world.get("current_run_id"))
            var router: Variant = world.get("combat")
            var bridge: Variant = router.live.bridge if router != null and router.live != null else null
            _check(not run_id.is_empty() and world.current_mode == "motherboard"
                and not bool(pilot.get("frozen")),
                region + ": E mission committed before B motherboard")
            if bridge != null and bridge.simulation.actors.has("VACCINE"):
                var stats: Dictionary = bridge.simulation.actors["VACCINE"]["stats"]
                # Test-fixture damage only. B projectiles, hits and death receipts stay real.
                stats["attack_damage"] = 150.0
                stats["attack_speed"] = 20.0
                for intro in Board.INTRO:
                    var actor_id: String = str(intro["id"])
                    _check(_kill_with_real_B(router, actor_id, world.player),
                        region + ": live B projectile defeated " + actor_id)
                _check(world.board != null and world.board.board.gates_unlocked(),
                    region + ": real B intro deaths unlocked hardware gates")
                if world.board != null and world.board.board.gates_unlocked():
                    world.player.global_position = Board.GATES[region]["position"] + Vector3.UP * 0.5
                    var gate: Dictionary = world.interact()
                    _check(str(gate.get("region_id", "")) == region,
                        region + ": physical F gate chooses correct hardware")
                    _check(world.current_mode == region.to_lower()
                        and not bool(pilot.get("frozen")),
                        region + ": production B/D scene enters after E ACK")
                    var persisted: Dictionary = Gateway.snapshot(slot_path)
                    _check(bool(persisted.get("ok", false)),
                        region + ": authoritative E snapshot after scene entry")
                    if bool(persisted.get("ok", false)):
                        var mission: Dictionary = persisted["snapshot"]["vaccine"]["mission"]
                        var checkpoints: Dictionary = mission.get(
                            "environment_rolls", {}).get("e_checkpoint_v1", {}).get("regions", {})
                        var entry: Dictionary = checkpoints.get(region, {})
                        var plan: Dictionary = world.mission["regions"][region]
                        _check(checkpoints.size() == 1
                            and int(entry.get("seed", -1)) == int(plan.get("map_seed", -2))
                            and str(entry.get("generator_id", "")) == Maps.GENERATOR_ID
                            and int(mission.get("generated_map_seeds", {}).get(region, -1))
                                == int(plan.get("map_seed", -2)),
                            region + ": E saved immutable D map before geometry")
                        _check(world.hardware != null and
                            world.hardware.map_plan == Maps.generate(
                                region, int(plan["map_seed"]), plan.get("environment", {})),
                            region + ": live 3D geometry matches E-authorized plan")
                        _check(world.get("pending_hardware_entry").is_empty()
                            and not bool(world.get("demo_mode")),
                            region + ": no preview shortcut or dangling ACK")
                        _check(router.mode == "formal" and router.live.bridge.run_id == run_id
                            and router.live.bridge.simulation.actors.has("VACCINE"),
                            region + ": new hardware uses authoritative B 60Hz actor")
                        _check(mission.get("hardware_cleaned_ids", []).is_empty(),
                            region + ": map entry never grants a clean reward")
            else:
                _check(false, region + ": B production vaccine actor missing")
        paused = false
        root.remove_child(pilot)
        pilot.queue_free()
        await process_frame
        _clean_fixture(slot_path)
    if failures == 0:
        print("REWORK A LIVE HARDWARE ENTRY PASS: 5 E-approved plans, physical gates, real B combat; no preview receipts")
    quit(0 if failures == 0 else 1)
