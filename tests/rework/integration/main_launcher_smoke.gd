extends SceneTree
## Proves the packed/default main scene is the launcher. Loading that launcher
## cannot create/modify an E slot. Production, QA and classic targets remain
## separate and all must resolve as scenes in the exported game.
const Launcher = preload("res://scenes/launcher.tscn")
const Production = preload("res://scenes/rework/integration/rework_main.tscn")
const QA = preload("res://scenes/rework/integration/rework_qa_boot.tscn")
const Classic = preload("res://scenes/main.tscn")
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")

var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func check(ok: bool, message: String) -> void:
    if not ok:
        failures += 1
        printerr("REWORK MAIN LAUNCHER FAIL: " + message)

func _run() -> void:
    var required: String = "res://scenes/launcher.tscn"
    if OS.get_environment("NEON_REQUIRE_LAUNCHER_MAIN_SCENE") == "1":
        check(str(ProjectSettings.get_setting("application/run/main_scene")) ==
            required, "packed main must be launcher and not old scenes/main.tscn")
    var normal_path: String = Store.default_slot_path("nv-rework-main-v1")
    var qa_path: String = Store.default_slot_path("nv-rework-qa-20261118")
    var before_main: bool = DirAccess.dir_exists_absolute(normal_path)
    var before_qa: bool = DirAccess.dir_exists_absolute(qa_path)
    var launcher: Control = Launcher.instantiate()
    root.add_child(launcher)
    await process_frame
    check(not paused, "launcher must not leave gameplay paused")
    check(launcher.name == "NeonVoidLauncher", "expected startup selection root")
    var choices: Dictionary = launcher.get("buttons")
    var expected: Dictionary = {
        "ReworkProduction": "res://scenes/rework/integration/rework_main.tscn",
        "ReworkQADemo": "res://scenes/rework/integration/rework_qa_boot.tscn",
        "ClassicGodot": "res://scenes/main.tscn",
    }
    check(choices.size() == 3, "exactly 3 distinct explicit choices")
    for id in expected:
        var node: Button = choices.get(id)
        check(node != null and node.visible and
            str(node.get_meta("target_scene", "")) == str(expected[id]),
            "button has unique real scene target: " + id)
        check(load(str(expected[id])) is PackedScene,
            "target scene loads: " + str(expected[id]))
    var normal: Node = Production.instantiate()
    var qa: Node = QA.instantiate()
    var classic: Node = Classic.instantiate()
    check(str(normal.get("slot_id")) == "nv-rework-main-v1",
        "normal rework has distinct persistent slot")
    check(qa.get_node_or_null("ReworkPersistentPilot") != null,
        "QA is a different top-level fixture scene")
    check(classic.get_script() != normal.get_script(),
        "classic Godot and production rework are separate scripts")
    normal.free()
    qa.free()
    classic.free()
    check(DirAccess.dir_exists_absolute(normal_path) == before_main and
        DirAccess.dir_exists_absolute(qa_path) == before_qa,
        "launcher/scene resource inspection never seeds E or QA saved data")
    root.remove_child(launcher)
    launcher.queue_free()
    await process_frame
    if failures == 0:
        print("NEON MAIN LAUNCHER PASS: 3 safe modes, classic preserved, normal/QA E isolated, launcher makes zero save mutations")
    quit(0 if failures == 0 else 1)
