extends SceneTree
## A QA-specific real E persistence + re-instantiation acceptance.
## Does not grant production rewards, alter B/E source, or touch existing saves.
const QA = preload("res://scenes/rework/integration/rework_windows_qa.tscn")
const Gateway = preload("res://scripts/rework/persistence/progression_gateway.gd")
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")
const Board = preload("res://scripts/rework/world/motherboard_layout.gd")
const SKILLS := [
    "nv_skill_arc_pulse", "nv_skill_penetrator", "nv_skill_root_packet"]
var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func _check(ok: bool, message: String) -> void:
    if not ok:
        failures += 1
        printerr("REWORK A QA RESTART FAIL: " + message)

func _clean(path: String) -> void:
    if not path.contains("qa-skill-restart-"):
        _check(false, "refused arbitrary save deletion")
        return
    var directory: DirAccess = DirAccess.open(path)
    if directory == null:
        return
    directory.list_dir_begin()
    var name: String = directory.get_next()
    while not name.is_empty():
        var child: String = path.path_join(name)
        if directory.current_is_dir():
            _clean(child)
        else:
            DirAccess.remove_absolute(child)
        name = directory.get_next()
    directory.list_dir_end()
    DirAccess.remove_absolute(path)

func _loaded(path: String) -> Dictionary:
    var value: Dictionary = Gateway.snapshot(path)
    _check(bool(value.get("ok", false)), "E authenticated readback")
    return value

func _create(slot: String) -> Node3D:
    var qa: Node3D = QA.instantiate()
    qa.set("slot_id", slot)
    qa.set("initial_vaccine_id", "qa-restart-vaccine")
    root.add_child(qa)
    return qa

func _run() -> void:
    var slot: String = "qa-skill-restart-" + str(OS.get_process_id()) + "-" + str(Time.get_ticks_usec())
    var path: String = Store.default_slot_path(slot)
    _check(not DirAccess.dir_exists_absolute(path), "fresh isolated rework QA slot")
    var first: Node3D = _create(slot)
    await process_frame
    var original: Dictionary = _loaded(path)
    _check(bool(first.get("qa_skill_tools_enabled")) and
        first.get("qa_skill_button") != null and
        first.get("qa_skill_button").visible and
        not bool(first.get("frozen")), "QA-only button visible and safe hub active")
    _check(original["snapshot"]["vaccine"]["known_skills"].is_empty(),
        "new E vaccine initially has NO synthesized skills")
    first.call("_qa_install_authorized_skills")
    var saved: Dictionary = _loaded(path)
    var vaccine: Dictionary = saved["snapshot"]["vaccine"]
    _check(vaccine["known_skills"] == SKILLS and
        vaccine["equipped_skills"] == SKILLS and
        int(saved.get("revision", 0)) == int(original.get("revision", 0)) + 4,
        "three authentic E learn transactions and E equip confirmed")
    _check(not bool(first.get("frozen")) and
        first.get("qa_skill_button").disabled and
        str(first.get("message_label").text).contains("E 저장 확인"),
        "QA UI says approved E save and prevents repeated grants")
    var saved_revision: int = int(saved.get("revision", -1))
    first.call("_qa_install_authorized_skills")
    _check(int(_loaded(path).get("revision", -1)) == saved_revision,
        "repeated QA button cannot duplicate E skill grants")
    # Recreate real QA scene in the same engine process, simulating an EXE
    # relaunch with the same user:// E slot while no mission is active.
    root.remove_child(first)
    first.queue_free()
    await process_frame
    paused = false
    var second: Node3D = _create(slot)
    await process_frame
    var after_restart: Dictionary = _loaded(path)
    _check(not bool(second.get("frozen")) and
        int(after_restart.get("revision", -1)) == saved_revision and
        after_restart["snapshot"]["vaccine"]["equipped_skills"] == SKILLS,
        "second actual QA scene reads exact durable E revision and skill IDs")
    _check(second.get("qa_skill_button").disabled,
        "relaunch QA UI confirms skills remain equipped")
    second.call("_on_mission_requested", {"seed": 42})
    var world: Node3D = second.get_node("World")
    var router: Variant = world.get("combat")
    _check(world.current_mode == "motherboard" and
        str(router.mode) == "formal" and
        not bool(second.get("frozen")), "real B formal mission starts after restart")
    var actor: Dictionary = router.actor_snapshot("VACCINE")
    var state: Dictionary = actor.get("skill_state", {})
    var slots: Array = state.get("slots", [])
    _check(slots.size() == 3 and
        str(slots[0].get("skill_id", "")) == SKILLS[0] and
        str(slots[2].get("skill_id", "")) == SKILLS[2],
        "B real 60Hz combat receives persisted E Q/E/C loadout after restart")
    var before_mana: float = float(actor.get("mana", -1.0))
    var enemies: Dictionary = router.enemies
    if enemies.has("intro-0"):
        var aim: Vector3 = enemies["intro-0"]["node"].global_position
        router.live.advance_test_tick({"VACCINE": {
            "move": Vector2.ZERO, "aim_world": aim,
            "fire": false, "skill_slot": 0}})
    var after_cast: Dictionary = router.actor_snapshot("VACCINE")
    var cast_slots: Array = after_cast.get("skill_state", {}).get("slots", [])
    _check(float(after_cast.get("mana", before_mana)) < before_mana and
        cast_slots.size() == 3 and
        int(cast_slots[0].get("remaining_cooldown_ticks", 0)) > 0,
        "actual Q skill consumes B mana and schedules cooldown")
    var bar: PanelContainer = second.get("skill_bar")
    bar.render_actor(after_cast)
    _check(bar.visible and str(bar.slots[0].text).contains("대기"),
        "QA HUD displays actual B cooldown after E-saved skill cast")
    # The simulated run is intentionally unclosed; delete ONLY our disposable
    # CI slot after freeing the node, never claim production resumability.
    paused = false
    root.remove_child(second)
    second.queue_free()
    await process_frame
    _clean(path)
    if failures == 0:
        print("REWORK A QA RESTART PASS: QA-scoped E learning x3, E equip, no duplicate grants, exact revision preserved on scene restart, real B 3-slot mana/cooldown and read-only UI")
    quit(0 if failures == 0 else 1)
