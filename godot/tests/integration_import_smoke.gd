extends SceneTree
## Explicit synthetic legacy import test only. No real user files are opened.
const Store = preload("res://scripts/persistence/save_store.gd")
var problems: int = 0

func _initialize() -> void:
    create_timer(50.0).timeout.connect(func(): printerr("P11 IMPORT TIMEOUT"); quit(1))
    call_deferred("_run")

func expect(ok: bool, why: String) -> void:
    if not ok:
        problems += 1
        printerr("P11 IMPORT FAIL: " + why)

func write_source(path: String, data: Variant) -> bool:
    var handle: FileAccess = FileAccess.open(path, FileAccess.WRITE)
    if handle == null:
        return false
    handle.store_string(JSON.stringify(data, "  ", true) + "\n")
    handle.flush()
    var passed: bool = handle.get_error() == OK
    handle.close()
    return passed

func _run() -> void:
    var rootpath: String = ProjectSettings.globalize_path(
        "user://p11-import-test-" + str(OS.get_process_id()) + "-" + str(Time.get_ticks_usec()))
    var source: String = rootpath.path_join("legacy-python")
    var target: String = rootpath.path_join("new-godot-slot")
    expect(DirAccess.make_dir_recursive_absolute(source) == OK, "synthetic source created")
    var originals: Dictionary = Store.fresh_state()
    originals["save.json"]["best_score"] = 2413
    originals["settings.json"]["volume"] = 0.4
    var bytes: Dictionary = {}
    for name in ["save.json", "settings.json", "profile.json", "antivirus_db.json"]:
        var filepath: String = source.path_join(name)
        expect(write_source(filepath, originals[name]), "create synthetic " + name)
        var handle: FileAccess = FileAccess.open(filepath, FileAccess.READ)
        if handle != null:
            bytes[name] = handle.get_buffer(handle.get_length())
            handle.close()
    var scene: PackedScene = load("res://scenes/main.tscn")
    var game: Node = scene.instantiate()
    root.add_child(game)
    game.set("persistence_enabled", true)
    game.set("save_slot", target)
    game.set("saved_state", Store.fresh_state())
    # This callback is invoked only after selecting "import" and a directory
    # in the live UI. No automatic discovery or source mutation occurs.
    game.call("_legacy_folder_selected", source)
    var imported: Dictionary = Store.read(target)
    expect(imported.get("ok", false), "Godot slot created and verifiable")
    if imported.get("ok", false):
        expect(int(imported.state["save.json"].best_score) == 2413, "legacy high score migrated")
        expect(is_equal_approx(float(imported.state["settings.json"].volume), 0.4), "legacy settings migrated")
    for name in bytes:
        var original: FileAccess = FileAccess.open(source.path_join(str(name)), FileAccess.READ)
        var backup: FileAccess = FileAccess.open(target.path_join("legacy_originals").path_join(str(name)), FileAccess.READ)
        expect(original != null and backup != null, "source and backup readable: " + str(name))
        if original != null and backup != null:
            expect(original.get_buffer(original.get_length()) == bytes[name], "source unchanged: " + str(name))
            expect(backup.get_buffer(backup.get_length()) == bytes[name], "byte-identical SHA-backed backup: " + str(name))
        if original != null:
            original.close()
        if backup != null:
            backup.close()
    expect(FileAccess.file_exists(target.path_join("migration_manifest.json")), "import audit manifest")
    # Never allow importing again into an occupied slot, even explicitly.
    game.call("_legacy_folder_selected", source)
    var verified: Dictionary = Store.read(target)
    expect(verified.get("ok", false) and int(verified.state["save.json"].best_score) == 2413,
        "repeated import cannot replace existing Godot data")
    game.get_parent().remove_child(game)
    game.free()
    if problems == 0:
        print("P11 LOSSLESS LEGACY IMPORT PASS (originals byte-identical, separate slot, no overwrite)")
    quit(1 if problems > 0 else 0)
