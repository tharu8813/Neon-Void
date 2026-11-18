extends SceneTree
## A full public-boundary regression: E Store receipts -> A pilot/D real
## motherboard -> B production 60Hz -> E-gated real five hardware entries.
## Isolated synthetic E slots only, no legacy/production saves or D preview hits.
const Pilot = preload("res://scenes/rework/integration/rework_pilot.tscn")
const Gateway = preload("res://scripts/rework/persistence/progression_gateway.gd")
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")
const State = preload("res://scripts/rework/progression/state_model.gd")
const Board = preload("res://scripts/rework/world/motherboard_layout.gd")
const REGIONS := ["CPU", "GPU", "RAM", "SSD", "HDD"]
const VACCINE_ID := "a-skill-verified-vaccine"
var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func check(ok: bool, label: String) -> void:
    if not ok:
        failures += 1
        printerr("REWORK A E-B GROWTH FAIL: " + label)

func _clean(path: String) -> void:
    if not path.contains("a-e-b-growth-"):
        check(false, "unsafe test-slot deletion blocked")
        return
    var directory: DirAccess = DirAccess.open(path)
    if directory == null:
        return
    directory.list_dir_begin()
    var name: String = directory.get_next()
    while not name.is_empty():
        var entry: String = path.path_join(name)
        if directory.current_is_dir():
            _clean(entry)
        else:
            DirAccess.remove_absolute(entry)
        name = directory.get_next()
    directory.list_dir_end()
    DirAccess.remove_absolute(path)

func _apply(path: String, payload: Dictionary) -> void:
    var loaded: Dictionary = Gateway.snapshot(path)
    check(bool(loaded.get("ok", false)), "E readback before real command")
    if not bool(loaded.get("ok", false)):
        return
    var action: String = str(payload["action"])
    var command: Dictionary = {
        "action": action, "vaccine_id": VACCINE_ID,
        "generation": int(loaded["snapshot"]["generation"]),
        "receipt_id": "a-growth:" + action + ":" + str(loaded["revision"])
    }
    command.merge(payload, true)
    var result: Dictionary = Gateway.transact_command(
        path, command, int(loaded["revision"]))
    check(bool(result.get("applied", false)),
        "E permanently accepted " + action + ": " + str(result))
    var readback: Dictionary = Gateway.snapshot(path)
    check(bool(readback.get("ok", false)) and
        int(readback.get("revision", 0)) > int(loaded["revision"]),
        "E revision advanced and readable after " + action)

func _kill_intro(router: Variant, actor_id: String, player: Node3D) -> bool:
    if not router.enemies.has(actor_id):
        return false
    for tick in range(220):
        if not router.enemies.has(actor_id):
            return true
        var target: Vector3 = router.enemies[actor_id]["node"].global_position
        var commands: Dictionary = {
            "VACCINE": {"move": Vector2.ZERO, "aim_world": target, "fire": true}
        }
        for enemy in router.enemies.keys():
            commands[str(enemy)] = {
                "move": Vector2.ZERO, "aim_world": player.global_position, "fire": false}
        router.live.advance_test_tick(commands)
    return not router.enemies.has(actor_id)

func _assert_live(router: Variant, revision: int, tag: String) -> void:
    check(router != null and router.mode == "formal"
        and router.live != null and router.live.bridge != null,
        tag + ": formal B runtime exists")
    if router == null or router.live == null or router.live.bridge == null:
        return
    var facade: Variant = router.live
    var actor: Dictionary = router.actor_snapshot("VACCINE")
    var stats: Dictionary = actor.get("stats", {})
    var effective: Dictionary = actor.get("effective_stats", {})
    check(is_equal_approx(float(stats.get("max_health", -1)), 110.0)
        and is_equal_approx(float(stats.get("attack_damage", -1)), 6.0)
        and is_equal_approx(float(stats.get("max_mana", -1)), 105.0)
        and int(stats.get("skill_haste", -1)) == 6,
        tag + ": 22-ID E investment correctly maps to real B actor stats")
    check(not effective.is_empty() and actor.has("shield")
        and actor.has("skill_state"), tag + ": A read-only HUD sees real B public fields")
    var growth: Dictionary = facade.authorized_growth_snapshot()
    check(int(growth.get("revision", -1)) == revision
        and growth.get("invested_stats", {}).get("attack_damage", 0) == 4
        and growth.get("equipped_skills", []).size() == 3,
        tag + ": B received matching E revision, 4 AD levels and 3 E slots")
    var skills: Dictionary = facade.skill_snapshot()
    var slots: Array = skills.get("slots", [])
    check(slots.size() == 3, tag + ": real B has exactly 3 learned skill slots")
    if slots.size() == 3:
        check(str(slots[0].get("skill_id", "")) == "nv_skill_arc_pulse"
            and str(slots[1].get("skill_id", "")) == "nv_skill_penetrator"
            and str(slots[2].get("skill_id", "")) == "nv_skill_root_packet",
            tag + ": E saved slot order survived actual world entry")
        check(int(slots[0].get("remaining_cooldown_ticks", -1)) == 0,
            tag + ": B new region resets temporary skill cooldown")

func _run() -> void:
    var suffix: String = str(OS.get_process_id()) + "-" + str(Time.get_ticks_usec())
    for region in REGIONS:
        paused = false
        var slot_id: String = "a-e-b-growth-" + suffix + "-" + region.to_lower()
        var path: String = Store.default_slot_path(slot_id)
        check(not DirAccess.dir_exists_absolute(path),
            region + ": fresh nonproduction E slot")
        var state: Dictionary = State.new_slot(slot_id, VACCINE_ID)
        state["vaccine"]["earned_fragments"] = 1200
        state["vaccine"]["unspent_fragments"] = 1200
        check(State.validate(state), region + ": valid synthetic E starting slot")
        var initialized: Dictionary = Store.initialize(path, state)
        check(bool(initialized.get("ok", false)), region + ": synthetic slot initialized")
        if not bool(initialized.get("ok", false)):
            _clean(path)
            continue
        _apply(path, {"action": "reallocate_stats", "allocations": {
            "max_hp": 2, "attack_damage": 4, "max_mana": 1,
            "ability_haste": 3}})
        _apply(path, {"action": "learn_skill", "skill_id": "nv_skill_arc_pulse",
            "equip_on_discovery": ["nv_skill_arc_pulse"]})
        _apply(path, {"action": "learn_skill", "skill_id": "nv_skill_penetrator",
            "equip_on_discovery": ["nv_skill_arc_pulse", "nv_skill_penetrator"]})
        _apply(path, {"action": "learn_skill", "skill_id": "nv_skill_root_packet",
            "equip_on_discovery": ["nv_skill_arc_pulse", "nv_skill_penetrator",
                "nv_skill_root_packet"]})
        var pilot: Node3D = Pilot.instantiate()
        pilot.set("slot_id", slot_id)
        pilot.set("initial_vaccine_id", VACCINE_ID)
        root.add_child(pilot)
        await process_frame
        check(not bool(pilot.get("frozen")), region + ": A pilot loads E verified profile")
        pilot.call("_on_mission_requested", {"seed": 42})
        var world: Node3D = pilot.get_node("World")
        var router: Variant = world.get("combat")
        var started: Dictionary = Gateway.snapshot(path)
        check(bool(started.get("ok", false))
            and str(started["snapshot"]["vaccine"]["mission"]["run_id"])
                == str(world.get("current_run_id"))
            and not bool(pilot.get("frozen")), region + ": E mission identity verified")
        _assert_live(router, int(started.get("revision", -1)), region + ": motherboard")
        var skill_bar: PanelContainer = pilot.get("skill_bar") as PanelContainer
        var actor: Dictionary = router.actor_snapshot("VACCINE")
        skill_bar.render_actor(actor)
        check(skill_bar.visible
            and skill_bar.slots[0].text.contains("Arc Pulse")
            and skill_bar.slots[2].text.contains("Root Packet"),
            region + ": actual E-saved Q/E/C skill labels are visible")
        var growth_before: Dictionary = router.live.authorized_growth_snapshot()
        check(bool(pilot.call("_sync_authorized_build_from_E"))
            and router.live.authorized_growth_snapshot() == growth_before,
            region + ": identical pre-tick E snapshot is idempotent")
        var next_enemy: String = str(Board.INTRO[0]["id"])
        var target: Vector3 = router.enemies[next_enemy]["node"].global_position
        var commands := {"VACCINE": {
            "move": Vector2.ZERO, "aim_world": target, "fire": false,
            "skill_slot": 0}}
        router.live.advance_test_tick(commands)
        var live_actor: Dictionary = router.actor_snapshot("VACCINE")
        var live_skills: Array = router.live.skill_snapshot().get("slots", [])
        check(live_actor.get("mana", 1000.0) < 105.0
            and live_skills.size() == 3
            and int(live_skills[0].get("remaining_cooldown_ticks", 0)) > 0,
            region + ": real B skill cast consumed mana and started cooldown")
        skill_bar.render_actor(live_actor)
        check(skill_bar.slots[0].text.contains("대기"),
            region + ": UI cooldown comes from actual B state")
        # Fast-track the intro enemy HP using a test-fixture B combat balance;
        # kills still require real B 60 Hz bullets and confirmed death events.
        var b_actor: Dictionary = router.live.bridge.simulation.actors["VACCINE"]
        b_actor["stats"]["attack_damage"] = 150.0
        b_actor["stats"]["attack_speed"] = 20.0
        for intro in Board.INTRO:
            check(_kill_intro(router, str(intro["id"]), world.player),
                region + ": real B projectiles clear motherboard intro")
        check(world.board != null and world.board.board.gates_unlocked(),
            region + ": E-verified profile allows a physical hardware gate")
        if world.board != null and world.board.board.gates_unlocked():
            world.player.global_position = Board.GATES[region]["position"] + Vector3.UP * 0.5
            world.interact()
            var entered: Dictionary = Gateway.snapshot(path)
            check(str(world.current_mode) == region.to_lower()
                and bool(entered.get("ok", false))
                and not bool(pilot.get("frozen")),
                region + ": map ACK and actual B production region entered")
            _assert_live(router, int(entered.get("revision", -1)),
                region + ": hardware")
            var after_switch: Dictionary = router.actor_snapshot("VACCINE")
            check(is_equal_approx(float(after_switch.get("mana", -1)), 105.0)
                and is_equal_approx(float(after_switch.get("stats", {}).get(
                    "attack_damage", -1)), 6.0),
                region + ": E growth once, B resources reset, no fixture damage leak")
        paused = false
        root.remove_child(pilot)
        pilot.queue_free()
        await process_frame
        _clean(path)
    if failures == 0:
        print("REWORK A E-B GROWTH PASS: 5 physical hardware entries E->A->B stat/3 skill readback, real mana/60Hz cooldown, no double grants")
    quit(1 if failures > 0 else 0)
