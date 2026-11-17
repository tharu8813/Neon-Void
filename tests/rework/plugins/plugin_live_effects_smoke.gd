extends SceneTree
const Suite = preload("res://tests/rework/plugins/plugin_effect_assertions.gd")

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var ok: bool = Suite.new().execute_tests()
	quit(0 if ok else 1)
