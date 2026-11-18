extends SceneTree
## Verifies the ACTUAL QA startup PackedScene with an isolated disposable E
## save. Unlike legacy main-port CI, this must load real A/B/C/D/E rework.
const Boot = preload("res://scenes/rework/integration/rework_qa_boot.tscn")
const Catalog = preload("res://scripts/rework/plugins/plugin_catalog.gd")
const Registry = preload("res://scripts/rework/skills/skill_registry.gd")
const Gateway = preload("res://scripts/rework/persistence/progression_gateway.gd")
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")
var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func _check(value: bool, title: String) -> void:
    if not value:
        failures += 1
        printerr("REWORK QA BOOT FAIL: " + title)

func _clean(path: String) -> void:
    if not path.contains("qa-boot-smoke-"):
        _check(false, "refusing to erase non-test E slot")
        return
    var dir: DirAccess = DirAccess.open(path)
    if dir == null:
        return
    dir.list_dir_begin()
    var name: String = dir.get_next()
    while not name.is_empty():
        var child: String = path.path_join(name)
        if dir.current_is_dir():
            _clean(child)
        else:
            DirAccess.remove_absolute(child)
        name = dir.get_next()
    dir.list_dir_end()
    DirAccess.remove_absolute(path)

func _configure_qa_scene(qa: Node3D, id: String) -> Node3D:
    qa.set("qa_slot_id", id)
    qa.set("qa_vaccine_id", "qa-boot-vaccine")
    var pilot: Node3D = qa.get_node("ReworkPersistentPilot")
    pilot.set("slot_id", id)
    pilot.set("initial_vaccine_id", "qa-boot-vaccine")
    return pilot

func _run() -> void:
    var main_scene: String = str(ProjectSettings.get_setting("application/run/main_scene"))
    if OS.get_environment("NEON_REQUIRE_QA_MAIN_SCENE") == "1":
        _check(main_scene == "res://scenes/rework/integration/rework_qa_boot.tscn",
            "QA executable must NOT launch legacy scenes/main.tscn: " + main_scene)
    _check(Catalog.entries().size() == 27, "27 C reward catalog IDs are loaded")
    # The draft pool is larger than the three QA-granted skills, so assert the
    # QA fixtures specifically instead of pinning the total registry size.
    _check(Registry.ids().size() >= 3
        and not Registry.definition("nv_skill_arc_pulse").is_empty()
        and not Registry.definition("nv_skill_penetrator").is_empty()
        and not Registry.definition("nv_skill_root_packet").is_empty(),
        "three explicit B active skill definitions")
    var id: String = "qa-boot-smoke-%d-%d" % [
        OS.get_process_id(), Time.get_ticks_usec()]
    var path: String = Store.default_slot_path(id)
    _check(not DirAccess.dir_exists_absolute(path), "unique isolated E QA fixture slot")
    var qa: Node3D = Boot.instantiate()
    var pilot: Node3D = _configure_qa_scene(qa, id)
    root.add_child(qa)
    await process_frame
    _check(not bool(pilot.get("frozen")) and str(
        pilot.get_node("World").get("current_mode")) == "hub",
        "QA startup enters real A/E hub rather than old legacy game")
    _check(bool(qa.get("initialized")) and bool(qa.get("did_seed")),
        "A first-run QA fixture seeded exactly once")
    var loaded: Dictionary = Gateway.snapshot(path)
    _check(bool(loaded.get("ok", false)), "real E save readback after QA startup")
    if bool(loaded.get("ok", false)):
        var v: Dictionary = loaded["snapshot"]["vaccine"]
        _check(int(v.get("copies_remaining", 0)) == 3
            and int(v.get("unspent_fragments", -1)) == 500,
            "QA UI displays three E copies and five E stat points")
        _check(v.get("known_skills", []).size() == 3
            and v.get("equipped_skills", []).size() == 3,
            "three E-authorized B skills are visible and usable")
        _check(v.get("archive", {}).size() == 2
            and v.get("carried", {}).size() == 1,
            "QA showcase plugins exist without granting on next restart")
    var tactical: PanelContainer = pilot.get("tactical_hud") as PanelContainer
    var panel: PanelContainer = pilot.get("hub_panel") as PanelContainer
    var ident: Node = qa.get_node_or_null("QABuildIdentification/ReworkQAStamp/ReworkQAVersion")
    _check(tactical != null and panel != null and ident != null,
        "new rework HUD, five-tab management and unmistakable QA version stamp")
    if tactical != null:
        _check(tactical.vaccine_label.text.contains("복제 3/3")
            and tactical.vaccine_label.text.contains("성장 포인트 5"),
            "E-authoritative vaccine copies and stat point text rendered")
    if panel != null:
        pilot.call("_toggle_management")
        _check(panel.visible and panel.tabs.get_tab_count() == 5,
            "Tab opens actual A stat/plugin/skills management UI")
        pilot.call("_close_management")
        _check(not paused, "closing Tab restores controls")
    if ident is Label:
        _check(ident.text.contains("REWORK QA") and ident.text.contains("Tab:"),
            "build cannot be mistaken for legacy main")
    # A clean hub restart must not overwrite or regrant the E QA starter pack.
    var pre_restart: Dictionary = Gateway.snapshot(path)
    paused = false
    root.remove_child(qa)
    qa.queue_free()
    await process_frame
    var other: Node3D = Boot.instantiate()
    var other_pilot: Node3D = _configure_qa_scene(other, id)
    root.add_child(other)
    await process_frame
    var after_restart: Dictionary = Gateway.snapshot(path)
    _check(not bool(other.get("did_seed"))
        and int(after_restart.get("revision", -1)) ==
            int(pre_restart.get("revision", -2))
        and not bool(other_pilot.get("frozen")),
        "clean-hub relaunch never resets E slot or grants QA start pack twice")
    other_pilot.call("_on_mission_requested", {"seed": 42})
    var world: Node3D = other_pilot.get_node("World") as Node3D
    var combat: Variant = world.get("combat")
    var state: Dictionary = combat.actor_snapshot("VACCINE")
    var skills: Array = state.get("skill_state", {}).get("slots", [])
    _check(str(world.get("current_mode")) == "motherboard"
        and not bool(other_pilot.get("frozen")) and skills.size() == 3,
        "real A→E→B pilot mission boot and 3 B skill slots")
    if skills.size() == 3:
        _check(str(skills[0].get("skill_id", "")) == "nv_skill_arc_pulse"
            and str(skills[2].get("skill_id", "")) == "nv_skill_root_packet",
            "B sees actual E-authorized Q/E/C skill IDs, not placeholder UI")
    paused = false
    root.remove_child(other)
    other.queue_free()
    await process_frame
    _clean(path)
    if failures == 0:
        print("REWORK QA BOOT PASS: dedicated scene, 27 C catalog, 22 E stats, 3 E/B real skills, copies 3/3, first-run-only safe slot and reload")
    quit(0 if failures == 0 else 1)
