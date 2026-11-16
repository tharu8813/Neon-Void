extends SceneTree
## Fixed 60Hz scenarios compared with frozen Python controls/game/world.
const Bindings = preload("res://scripts/input/legacy_bindings.gd")
const Collision = preload("res://scripts/player/legacy_collision.gd")
const PlayerScene = preload("res://scenes/player/legacy_player.tscn")
const InputScript = preload("res://scripts/input/legacy_input.gd")
const Playground = preload("res://scenes/player/parity_playground.tscn")
const STEP: float = 1.0 / 60.0
var failures: int = 0

func _near(a: float, b: float, label: String, tolerance: float = 0.005) -> void:
    if absf(a - b) > tolerance:
        failures += 1
        printerr("P3 ", label, " expected=", b, " actual=", a)

func _vec(a: Vector2, b: Vector2, label: String, tolerance: float = 0.005) -> void:
    if a.distance_to(b) > tolerance:
        failures += 1
        printerr("P3 ", label, " expected=", b, " actual=", a)

func _require(value: bool, label: String) -> void:
    if not value:
        failures += 1
        printerr("P3 failed: ", label)

func _initialize() -> void:
    call_deferred("_check")

func _check() -> void:
    var defaults: Dictionary = Bindings.DEFAULT_BINDINGS.duplicate()
    _require(Bindings.validated(defaults) == defaults, "default bindings")
    _require(Bindings.rebound(defaults, "up", "m").is_empty(), "reserved M")
    _require(Bindings.rebound(defaults, "up", "s").is_empty(), "duplicate S")
    var rebound: Dictionary = Bindings.rebound(defaults, "up", "g")
    _require(rebound.get("up", "") == "g" and defaults["up"] == "w", "non-mutating rebind")
    _vec(Bindings.movement({"g": true, "right": true}, rebound), Vector2(1, -1).normalized(), "diagonal arrows")
    _vec(Bindings.movement({"up": true}, rebound), Vector2.UP, "permanent arrow alternative")
    _vec(Bindings.dash_direction(Vector2.UP, Vector2.RIGHT * 80, Vector2.RIGHT), Vector2.UP, "input dash first")
    _vec(Bindings.dash_direction(Vector2.ZERO, Vector2.RIGHT * 30, Vector2.UP), Vector2.RIGHT, "velocity dash second")
    _vec(Bindings.dash_direction(Vector2.ZERO, Vector2.ZERO, Vector2.UP), Vector2.UP, "last dash third")
    _vec(Bindings.dash_direction(Vector2.ZERO, Vector2.ZERO, Vector2.ZERO), Vector2.UP, "dash fallback")
    var controls: Node = InputScript.new()
    root.add_child(controls)
    controls.call("set_key_state", "w", true)
    _vec(controls.call("movement_vector"), Vector2.UP, "input W")
    controls.call("set_key_state", "w", true)
    _require(controls.get("pressed_keys").size() == 1, "key repeat ignored")
    controls.call("set_key_state", "shift", true)
    _require(controls.call("consume_dash_request"), "shift dash fallback")
    _require(not controls.call("consume_dash_request"), "one-shot dash")
    controls.call("release_all")
    _vec(controls.call("movement_vector"), Vector2.ZERO, "focus loss clears held keys")
    _require(not controls.call("consume_dash_request"), "focus loss clears dash")
    _require(not controls.call("rebind_action", "up", "d"), "rebind key conflict rejected")
    _require(controls.call("rebind_action", "up", "g"), "rebind accepted")
    controls.call("set_key_state", "w", true)
    _vec(controls.call("movement_vector"), Vector2.ZERO, "old bound key no longer works")
    controls.call("set_key_state", "w", false)
    controls.call("set_key_state", "g", true)
    _vec(controls.call("movement_vector"), Vector2.UP, "new bound key works")
    controls.call("set_pointer_native", Vector2(512, 20), Vector2(1024, 768))
    _require(not controls.get("pointer_valid"), "letterbox pointer excluded")
    controls.call("set_pointer_native", Vector2(512, 384), Vector2(1024, 768))
    _vec(controls.get("pointer_logical"), Vector2(640, 360), "native pointer")
    _vec(Collision.move_hub(Vector2(20, 20), Vector2(-900, -900), 13), Vector2(13, 13), "hub min bounds")
    _vec(Collision.move_hub(Vector2(2380, 1540), Vector2(400, 400), 13), Vector2(2387, 1547), "hub max bounds")
    _vec(Collision.move_hub(Vector2(1000, 550), Vector2(55, 0), 13), Vector2(1000, 550), "pillar X blocks")
    _vec(Collision.move_hub(Vector2(1000, 550), Vector2(0, 30), 13), Vector2(1000, 580), "pillar Y clear")
    var p: Node3D = PlayerScene.instantiate()
    p.set("auto_tick", false)
    root.add_child(p)
    for frame in range(60):
        p.call("simulate_tick", STEP, Vector2.RIGHT)
    var pos: Vector2 = p.get("logical_position")
    _near(pos.x, 931.590659682, "60Hz 60 right ticks against Python")
    _near(pos.y, 360.0, "move stays on Y")
    p.call("set_logical_position", Vector2(640, 360))
    p.set("velocity_px", Vector2.ZERO)
    for frame in range(60):
        p.call("simulate_tick", STEP, Vector2(1, -1).normalized())
    pos = p.get("logical_position")
    _near(pos.x, 846.18573279, "diagonal 60Hz X")
    _near(pos.y, 153.81426721, "diagonal 60Hz Y")
    p.call("set_logical_position", Vector2(640, 360))
    p.set("velocity_px", Vector2.ZERO)
    p.set("last_move_dir", Vector2.UP)
    _require(p.call("try_dash", Vector2.RIGHT), "dash activated")
    _require(not p.call("try_dash", Vector2.UP), "no dash stacking")
    _vec(p.get("dash_dir"), Vector2.RIGHT, "dash respects move not cursor")
    _near(p.get("invulnerability_t"), 0.32, "dash invulnerability")
    _require(p.get("charges") == 0, "dash consumes charge")
    for frame in range(9):
        p.call("simulate_tick", STEP, Vector2.ZERO)
    _near(p.get("logical_position").x, 781.0, "arena dash 9 ticks 940px/s")
    _near(p.get("invulnerability_t"), 0.17, "dash invulnerability remaining")
    for frame in range(64):
        p.call("simulate_tick", STEP, Vector2.ZERO)
    _require(p.get("charges") == 1, "dash recharge")
    p.call("set_logical_position", Vector2(1200, 780))
    p.call("set_aim", Vector2(740, 360), Vector2(560, 420))
    _near(p.get("aim_radians"), 0.0, "hub pointer aim offset")
    _vec(p.call("combat_snapshot")["aim_direction"], Vector2.RIGHT, "C combat API aim")
    _near(p.call("combat_snapshot")["hp"], 100.0, "C combat API HP")
    var preview: Node3D = Playground.instantiate()
    root.add_child(preview)
    _require(preview.get_node_or_null("LegacyPlayer") != null, "standalone playground player")
    _require(preview.get_node_or_null("LegacyCamera") != null, "standalone playground camera")
    preview.queue_free()
    p.queue_free()
    controls.queue_free()
    if failures == 0:
        print("P3 PLAYER/INPUT/COLLISION PARITY PASS (60Hz; physical QA pending)")
    quit(0 if failures == 0 else 1)
