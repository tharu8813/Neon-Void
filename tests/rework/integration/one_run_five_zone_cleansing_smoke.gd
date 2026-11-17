extends SceneTree
## A full five-hardware run in ONE E slot and ONE run_id.
## Uses D's real interactive goals/timer/visual checksum, B's actual projectile
## kills, C's E-committed choice UI, and E's final extraction transaction.
## Combat lethality is test-only B stats; no direct HP/death/goal/reward mutation.
## Headless deterministic acceptance, NOT physical human play/FPS/audio QA.
const Pilot = preload("res://scenes/rework/integration/rework_pilot.tscn")
const Gateway = preload("res://scripts/rework/persistence/progression_gateway.gd")
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")
const Board = preload("res://scripts/rework/world/motherboard_layout.gd")
const REGIONS := ["CPU", "GPU", "RAM", "SSD", "HDD"]
var errors: int = 0
var pilot: Node3D
var world: Node3D
var run_id: String
var slot_path: String
var boss_deaths: int = 0
var region_count: int = 0

func _initialize() -> void:
    call_deferred("_run")

func check(ok: bool, title: String) -> void:
    if not ok:
        errors += 1
        printerr("REWORK A FULL RUN FAIL: ", title)

func _clean(path: String) -> void:
    if not path.contains("a-full-run-"):
        check(false, "refused non-synthetic slot cleanup")
        return
    var d: DirAccess = DirAccess.open(path)
    if d == null:
        return
    d.list_dir_begin()
    var n: String = d.get_next()
    while not n.is_empty():
        var child: String = path.path_join(n)
        if d.current_is_dir():
            _clean(child)
        else:
            DirAccess.remove_absolute(child)
        n = d.get_next()
    d.list_dir_end()
    DirAccess.remove_absolute(path)

func _saved() -> Dictionary:
    var loaded: Dictionary = Gateway.snapshot(slot_path)
    check(bool(loaded.get("ok", false)), "authoritative E snapshot readable")
    return loaded

func _drain_offers() -> bool:
    for attempt in range(32):
        if bool(pilot.get("frozen")):
            return false
        var offer_panel: PanelContainer = pilot.get("offer_panel")
        if not offer_panel.visible:
            return true
        var offers: Variant = pilot.get("offers")
        if offers == null:
            return false
        var cards: Array = offers.offer_snapshot().get("cards", [])
        var choose_index: int = -1
        for index in range(cards.size()):
            if bool(cards[index].get("empty", false)):
                continue
            # Keep rare transaction optional; use a legal E-verified C card.
            if str(cards[index].get("rarity", "")) == "common":
                choose_index = index
                break
            if choose_index < 0:
                choose_index = index
        if choose_index < 0:
            return false
        pilot.call("_choose_card", choose_index)
    return false

func _boost_b_combat(router: Variant) -> void:
    # Aggressive fixture speeds up repeated B projectile combat only.
    # It does not create receipts, clear objectives, or touch E save data.
    var b_actor: Dictionary = router.live.bridge.simulation.actors["VACCINE"]
    b_actor["stats"]["attack_damage"] = 180.0
    b_actor["stats"]["attack_speed"] = 25.0
    b_actor["stats"]["projectile_speed_mult"] = 2.5

func _was_certified_dead(actor_id: String) -> bool:
    if world.current_mode == "motherboard":
        return world.intro_receipts.has(actor_id)
    var session: Variant = world.cpu_session if world.current_mode == "cpu" else world.region_session
    return session != null and session.defeated_hostiles.has(actor_id)

func _kill_real_B(router: Variant, actor_id: String,
        max_ticks: int = 280) -> bool:
    # A previously targeted B projectile can kill a second nearby enemy
    # before its own turn. Its B/D receipt is valid; absence alone is NOT.
    if not router.enemies.has(actor_id):
        return _was_certified_dead(actor_id) and _drain_offers()
    for tick in range(max_ticks):
        if not router.enemies.has(actor_id):
            return _was_certified_dead(actor_id) and _drain_offers()
        if bool(pilot.get("frozen")) or not _drain_offers():
            return false
        var target: Vector3 = router.enemies[actor_id]["node"].global_position
        if world.current_mode != "motherboard":
            # Deterministic combat fixture positions the *actual player body*
            # on a valid open floor tile close to the B enemy, rather than
            # firing indefinitely through D procedural walls from a console.
            # Still only B projectile collision can issue an accepted kill.
            world.player.global_position = target + Vector3(0.5, 0.5, 0.0)
        var commands: Dictionary = {"VACCINE": {
            "move": Vector2.ZERO, "aim_world": target, "fire": true}}
        for enemy in router.enemies.keys():
            commands[str(enemy)] = {"move": Vector2.ZERO,
                "aim_world": world.player.global_position, "fire": false}
        router.live.advance_test_tick(commands)
    return not router.enemies.has(actor_id) and _was_certified_dead(actor_id) \
        and _drain_offers()

func _kill_all_real_B(router: Variant, boss: bool = false) -> void:
    # Snapshot actor IDs to avoid false skipping caused by B removals.
    for id in router.enemies.keys().duplicate():
        if str(id).contains("BOSS") != boss:
            continue
        check(_kill_real_B(router, str(id)),
            "real B projectile kills " + str(id))
        if str(id).contains("BOSS"):
            boss_deaths += 1
        if errors > 0:
            return

func _interact_at(position: Vector3) -> Dictionary:
    world.player.global_position = position + Vector3.UP * 0.5
    return world.interact()

func _activate(name: String) -> Dictionary:
    var location: Vector3 = world.hardware.objective_world(name)
    return _interact_at(location)

func _drive_objectives(region: String) -> void:
    var session: Variant = world.cpu_session if region == "CPU" else world.region_session
    var router: Variant = world.combat
    if region == "CPU":
        check(str(_activate("secure_core").get("objective_id", "")) == "secure_core",
            "CPU defense terminal activated by real world interaction")
        var core: Vector3 = world.hardware.objective_world("secure_core")
        for tick in range(2400):
            session.tick_player(core)
        check(bool(session.objectives.progress["secure_core"]["completed"]),
            "CPU real 2400 defense ticks complete")
        check(str(_activate("stop_intruders").get("objective_id", ""))
            == "stop_intruders", "CPU intrusion objective enabled by player")
    elif region == "GPU":
        for click in range(3):
            check(str(_activate("disable_emitter").get("objective_id", ""))
                == "disable_emitter", "GPU emitter switch %d" % click)
        check(str(_activate("stabilize_coolant").get("objective_id", ""))
            == "stabilize_coolant", "GPU coolant minigame activation")
        var coolant: Vector3 = world.hardware.objective_world("stabilize_coolant")
        var dodging: Vector3 = coolant + Vector3(1.0, 0.0, 1.0)
        for tick in range(8 * 90):
            session.tick_player(dodging)
        check(bool(session.objectives.progress["stabilize_coolant"]["completed"]),
            "GPU real eight consecutive 90-tick hazard dodges")
    elif region == "RAM":
        for click in range(4):
            check(str(_activate("purge_corruption").get("objective_id", ""))
                == "purge_corruption", "RAM purge action %d" % click)
        check(bool(session.objectives.progress["purge_corruption"]["completed"]),
            "RAM real four purge actions")
        for attempt in range(2):
            var authentic: bool = false
            for pad in session.pads:
                if not str(pad["label"].text).contains("VALID CRC"):
                    continue
                var response: Dictionary = _interact_at(pad["position"])
                authentic = bool(response.get("authentic", false))
                break
            check(authentic, "RAM authentic player-visible green checksum pad")
        check(bool(session.objectives.progress["verify_memory"]["completed"]),
            "RAM two real checksum verification events")
    elif region == "SSD" or region == "HDD":
        var expected: int = 3 if region == "SSD" else 7
        var activated: int = 0
        for step in range(expected):
            var found: bool = false
            for node in session.targets:
                if not str(node["label"].text).contains("[F]"):
                    continue
                var clicked: Dictionary = _interact_at(node["position"])
                if str(clicked.get("type", "")) == "storage_node_activated":
                    found = true
                    activated += 1
                    break
            check(found, region + " physical ordered data node " + str(step))
        check(activated == expected, region + " all data nodes actually interacted")
        if region == "SSD":
            check(str(_activate("seal_transfer").get("objective_id", ""))
                == "seal_transfer", "SSD transfer terminal started")
            var center: Vector3 = world.hardware.objective_world("seal_transfer")
            for tick in range(1800):
                session.tick_player(center)
            check(bool(session.objectives.progress["seal_transfer"]["completed"]),
                "SSD real 1800 transfer ticks complete")
    # CPU stop_intruders is a B-confirmed KILL objective; the 10th actual
    # B projectile kill is its final progress event. Do not assert completion
    # before processing those kills.
    check(not bool(world.world_state.progress[region]["cleared"]),
        region + ": objectives alone do not grant a fake boss clean")
    _kill_all_real_B(router)
    check(session.objectives.all_objectives_completed(),
        region + ": all D gameplay objectives completed after B kill events. progress=" +
        str(session.objectives.progress) + " D_known=" +
        str(session.known_hostiles.size()) + " D_defeated=" +
        str(session.defeated_hostiles.size()) + " B_remaining=" +
        str(router.enemies.keys()) + " frozen=" + str(pilot.get("frozen")))
    check(router.enemies.has(region + "-BOSS"),
        region + ": real B boss spawned only after goals and hostile deaths")
    if errors == 0:
        _kill_all_real_B(router, true)
    check(bool(world.world_state.progress[region]["cleared"]) and
        bool(world.hardware.extraction_open),
        region + ": B boss death and E receipt unlock physical return/extract")
    check(world.pending_world_receipts.is_empty(),
        region + ": every world fact has E ACK")

func _run() -> void:
    var slot_id: String = "a-full-run-" + str(OS.get_process_id()) + "-" + str(Time.get_ticks_usec())
    slot_path = Store.default_slot_path(slot_id)
    check(not DirAccess.dir_exists_absolute(slot_path), "synthetic E slot unique")
    pilot = Pilot.instantiate()
    pilot.set("slot_id", slot_id)
    pilot.set("initial_vaccine_id", "a-full-run-vaccine")
    root.add_child(pilot)
    await process_frame
    world = pilot.get_node("World")
    check(world.current_mode == "hub" and not bool(pilot.get("frozen")),
        "starts in walkable saved Defender hub")
    pilot.call("_on_mission_requested", {"seed": 42})
    run_id = str(world.current_run_id)
    check(not run_id.is_empty() and world.current_mode == "motherboard",
        "one E mission ID created before D physical motherboard")
    var router: Variant = world.combat
    _boost_b_combat(router)
    for intro in Board.INTRO:
        check(_kill_real_B(router, str(intro["id"])),
            "first B motherboard real kill " + str(intro["id"]))
    check(world.board != null and world.board.board.gates_unlocked(),
        "actual B kill receipts unlock all five physical hardware gates")
    var previous_clean: int = 0
    var seed_revision: int = int(_saved().get("revision", -1))
    for region in REGIONS:
        if errors > 0 or bool(pilot.get("frozen")):
            break
        check(world.current_mode == "motherboard" and world.current_run_id == run_id
            and world.board.board.gates_unlocked(),
            region + ": returned motherboard keeps original run and intro kills")
        var gate: Dictionary = _interact_at(Board.GATES[region]["position"])
        check(str(gate.get("region_id", "")) == region
            and str(world.current_mode) == region.to_lower()
            and not bool(pilot.get("frozen")),
            region + ": genuine D hardware interaction after E checkpoint")
        check(int(_saved().get("revision", -1)) > seed_revision,
            region + ": E map checkpoint advanced durable revision")
        var auth: Dictionary = router.live.authorized_growth_snapshot()
        check(str(auth.get("vaccine_id", "")) == "a-full-run-vaccine"
            and int(auth.get("revision", -1)) == int(_saved().get("revision", -2)),
            region + ": B growth authorization rehydrated from current E revision")
        _boost_b_combat(router)
        _drive_objectives(region)
        if errors > 0:
            break
        var progress: Dictionary = _saved()
        var mission: Dictionary = progress["snapshot"]["vaccine"]["mission"]
        var cleaned: Array = mission.get("hardware_cleaned_ids", [])
        check(cleaned.size() == previous_clean + 1 and cleaned.has(region),
            region + ": real B boss -> D fact -> E durable single clean")
        previous_clean = cleaned.size()
        region_count += 1
        if region != REGIONS.back():
            var before_return: int = int(progress["revision"])
            var returned: Dictionary = _interact_at(world.hardware.spawn_world())
            check(str(returned.get("type", "")) == "hardware_board_return_requested"
                and world.current_mode == "motherboard" and world.current_run_id == run_id,
                region + ": physical board return re-enters same mission")
            check(int(_saved().get("revision", -1)) == before_return,
                region + ": return alone never invents E reward")
        else:
            var exit_choice: Dictionary = _interact_at(world.hardware.extraction_world())
            check(str(exit_choice.get("type", "")) == "hardware_extraction_requested"
                and bool(pilot.learn_panel.visible) and
                not pilot.pending_extraction.is_empty(),
                "5/5 physical extraction prepares explicit E plugin learning choice")
            # Choosing no newly learned plugins is legal; avoids phantom awards.
            pilot.call("_finish_extraction")
            var final: Dictionary = _saved()
            var vaccine: Dictionary = final["snapshot"]["vaccine"]
            check(vaccine.get("mission") == null
                and str(vaccine.get("closed_runs", {}).get(run_id, "")) == "extracted",
                "single E final extraction one-shot receipt is permanently closed")
            check(world.current_mode == "hub" and
                bool(pilot.result_panel.visible) and
                not bool(pilot.get("frozen")),
                "real five-cleaned E receipt opens verified 5/5 result in Defender hub")
            check(str(pilot.result_panel.result_label.text).contains("5 / 5"),
                "player sees verified full-clean result, not D preview count")
            check(int(final["revision"]) > int(progress["revision"]),
                "final extraction advanced E revision exactly through actual action")
    check(region_count == 5 and boss_deaths == 5,
        "ONE mission, five completed regions and five real B boss kills")
    check(not bool(pilot.get("frozen")), "no uncertain E receipts or fatal world errors")
    paused = false
    root.remove_child(pilot)
    pilot.queue_free()
    await process_frame
    _clean(slot_path)
    if errors == 0:
        print("REWORK A FULL FIVE-ZONE RUN PASS: same E run, CPU/GPU/RAM/SSD/HDD real objectives + 5 B bosses, E clean receipts, physical board returns, 5/5 final extraction")
    quit(0 if errors == 0 else 1)
