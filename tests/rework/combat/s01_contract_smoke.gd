extends SceneTree
## Standalone S01 smoke. Dynamically loads B regression resources so headless CI
## reports individual parse/load errors instead of masking them in a parent preload.
var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func _check(ok: bool, what: String) -> void:
    if not ok:
        failures += 1
        printerr("S01 B FAIL: " + what)

func _run() -> void:
    var scripts: Array[String] = [
        "res://tests/rework/combat/environment_hit_cases.gd",
        "res://tests/rework/skills/skill_s01_cases.gd"
    ]
    for path in scripts:
        var resource: Variant = load(path)
        _check(resource is Script and resource.can_instantiate(),
            "S01 parse/load failure: " + path)
        if resource is Script and resource.can_instantiate():
            resource.run(Callable(self, "_check"))
    if failures == 0:
        print("S01 B CONTRACT PASS (environment, 9 skills)")
    quit(1 if failures != 0 else 0)
