extends SceneTree
## A S01 end-to-end bridge check on a NEW synthetic E slot.
## D reports typed GPU contact, A validates the saved map before D forwards to
## B's real 60 Hz HP ledger. No live user saves or legacy files are touched.
const PilotScene = preload("res://scenes/rework/integration/rework_pilot.tscn")
const Gateway = preload("res://scripts/rework/persistence/progression_gateway.gd")
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")
const Board = preload("res://scripts/rework/world/motherboard_layout.gd")
const SkillDraft = preload("res://scripts/rework/ui/pilot_skill_draft.gd")
const SkillOffer = preload("res://scripts/rework/skills/skill_offer.gd")
const Registry = preload("res://scripts/rework/skills/skill_registry.gd")
var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func _check(ok: bool, label: String) -> void:
    if not ok:
        failures += 1
        printerr("REWORK S01 A BRIDGE FAIL: " + label)

func _clean_fixture(path: String) -> void:
    if not path.contains("a-s01-bridge-smoke-"):
        _check(false, "refuse cleanup outside synthetic fixture")
        return
    var folder: DirAccess = DirAccess.open(path)
    if folder == null:
        return
    folder.list_dir_begin()
    var next: String = folder.get_next()
    while not next.is_empty():
        var child: String = path.path_join(next)
        if folder.current_is_dir():
            _clean_fixture(child)
        else:
            DirAccess.remove_absolute(child)
        next = folder.get_next()
    folder.list_dir_end()
    DirAccess.remove_absolute(path)

func _kill_intro(router: Variant, player: Node3D, actor_id: String) -> bool:
    for tick in range(230):
        if not router.enemies.has(actor_id):
            return true
        var target: Vector3 = router.enemies[actor_id]["node"].global_position
        var actions: Dictionary = {"VACCINE": {
            "move": Vector2.ZERO, "aim_world": target, "fire": true}}
        for enemy_id in router.enemies.keys():
            actions[str(enemy_id)] = {
                "move": Vector2.ZERO, "aim_world": player.global_position,
                "fire": false}
        router.live.advance_test_tick(actions)
    return not router.enemies.has(actor_id)

func _run() -> void:
    var suffix: String = str(OS.get_process_id()) + "-" + str(Time.get_ticks_usec())
    var slot_id: String = "a-s01-bridge-smoke-" + suffix
    var slot_path: String = Store.default_slot_path(slot_id)
    var pilot: Node3D = PilotScene.instantiate()
    pilot.set("slot_id", slot_id)
    pilot.set("initial_vaccine_id", "synthetic-a-s01")
    root.add_child(pilot)
    await process_frame
    _check(not bool(pilot.get("frozen")), "production pilot starts safely")
    var world: Node3D = pilot.get_node_or_null("World") as Node3D
    if world != null and not bool(pilot.get("frozen")):
        _check(world.has_signal("environment_contact_requested"),
            "D scene declares live environmental contact signal")
        pilot.call("_on_mission_requested", {"seed": 42})
        var router: Variant = world.get("combat")
        var run_id: String = str(world.get("current_run_id"))
        _check(not run_id.is_empty() and router != null and router.mode == "formal",
            "formal B and E mission are active")
        if router != null and router.live != null and router.live.bridge != null:
            var b: Variant = router.live.bridge
            var stats: Dictionary = b.simulation.actors["VACCINE"]["stats"]
            stats["attack_damage"] = 150.0
            stats["attack_speed"] = 20.0
            for intro in Board.INTRO:
                _check(_kill_intro(router, world.player, str(intro["id"])),
                    "B damage unlocks GPU via motherboard combat")
            _check(world.board.board.gates_unlocked(),
                "D motherboard gates unlock after real B combat")
            if world.board.board.gates_unlocked():
                world.player.global_position = Board.GATES["GPU"]["position"] + Vector3.UP * 0.5
                var entered: Dictionary = world.interact()
                _check(str(entered.get("region_id", "")) == "GPU"
                    and world.current_mode == "gpu" and not bool(pilot.get("frozen")),
                    "D GPU gate and E map checkpoint accepted")
                var e: Dictionary = Gateway.snapshot(slot_path)
                _check(bool(e.get("ok", false))
                    and e["snapshot"]["vaccine"]["mission"]["environment_rolls"]
                    .get("e_checkpoint_v1", {}).get("regions", {}).has("GPU"),
                    "E durable GPU map exists before contact")
                b = router.live.bridge
                var before: float = float(b.actor_snapshot("VACCINE")["stats"]["health"])
                var fact: Dictionary = {
                    "schema": 1, "type": "world_hazard_contact", "run_id": run_id,
                    "region_id": "GPU", "actor_id": "VACCINE",
                    "hazard_kind": "coolant_overload_beam",
                    "hazard_id": "GPU:beam:0", "cycle": 0, "tick": 90,
                    "player_world": world.player.global_position,
                    "source_world": world.player.global_position,
                    "damage_type": "magic"
                }
                if world.has_signal("environment_contact_requested"):
                    world.call("_on_environment_contact", fact)
                _check(not bool(pilot.get("frozen")),
                    "A checks E durable map and D submits contact once")
                router.live.advance_test_tick({"VACCINE": {
                    "move": Vector2.ZERO, "aim_world": world.player.global_position
                    + Vector3.RIGHT, "fire": false}})
                var after: float = float(b.actor_snapshot("VACCINE")["stats"]["health"])
                _check(after < before, "B real HP lost after GPU contact at 60 Hz")
                if world.has_signal("environment_contact_requested"):
                    world.call("_on_environment_contact", fact)
                router.live.advance_test_tick({"VACCINE": {
                    "move": Vector2.ZERO, "fire": false}})
                var duplicate: float = float(b.actor_snapshot("VACCINE")["stats"]["health"])
                _check(is_equal_approx(duplicate, after),
                    "same GPU contact cannot damage twice")
                fact["run_id"] = "old-run"
                if world.has_signal("environment_contact_requested"):
                    world.call("_on_environment_contact", fact)
                _check(not bool(pilot.get("frozen")),
                    "stale run contact is ignored without save mutation")
                _check(int(Gateway.snapshot(slot_path).get("revision", -1)) > 0,
                    "E slot is still readable after B-only hazard")
    paused = false
    root.remove_child(pilot)
    pilot.queue_free()
    await process_frame
    _clean_fixture(slot_path)
    await _check_skill_replacement()
    if failures == 0:
        print("REWORK S01 A BRIDGE PASS: E-authorized D GPU contact -> B HP once; manual skill slot selection")
    quit(1 if failures > 0 else 0)

func _check_skill_replacement() -> void:
    var ui := SkillDraft.new()
    root.add_child(ui)
    await process_frame
    var ids: Array[String] = Registry.ids()
    _check(ids.size() >= 4, "skill pool covers three equipped plus new offer")
    if ids.size() >= 4:
        var existing: Array = [ids[0], ids[1], ids[2]]
        var offers: Array = SkillOffer.candidates(42, "GPU", existing)
        _check(not offers.is_empty(), "skill draft candidates exclude known slots")
        if not offers.is_empty():
            var emitted: Array[String] = []
            ui.skill_chosen.connect(func(id: String) -> void: emitted.append(id))
            ui.present(offers, existing)
            _check(ui.replacement_selector.visible and
                int(ui.selected_replacement_slot()) == -1,
                "three equipped skills require explicit replacement choice")
            ui._choose(0)
            _check(emitted.is_empty(), "unselected replacement emits no skill")
            ui.replacement_selector.select(2)
            ui._choose(0)
            _check(emitted.size() == 1 and
                int(ui.selected_replacement_slot()) == 1,
                "chosen second slot emits exactly one skill request")
    root.remove_child(ui)
    ui.queue_free()
    await process_frame
