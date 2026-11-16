extends Node3D
## Legacy deterministic player motion. All positions/velocities below are PX.
## P3 uses explicit axis collision so engine physics cannot change game feel.
const Mapper = preload("res://scripts/camera/legacy_viewport.gd")
const Collision = preload("res://scripts/player/legacy_collision.gd")
const Bindings = preload("res://scripts/input/legacy_bindings.gd")
signal position_changed(logical_position: Vector2)
signal aim_changed(aim_radians: float, aim_direction: Vector2)
signal dash_started(direction: Vector2)
signal skill_requested(kind: String)
signal interact_requested()
signal health_changed(hp: float, max_hp: float)

@export var hub_mode: bool = false
@export var auto_tick: bool = true
var input_source: Node
var camera_source: Camera3D
var simulation_enabled: bool = true

var logical_position: Vector2 = Vector2(640.0, 360.0)
var velocity_px: Vector2 = Vector2.ZERO
var last_move_dir: Vector2 = Vector2.UP
var dash_dir: Vector2 = Vector2.ZERO
var aim_radians: float = -PI / 2.0
var aim_direction: Vector2 = Vector2.UP
var hp: float = 100.0
var max_hp: float = 100.0
var mana: float = 100.0
var max_mana: float = 100.0
var speed_px: float = 322.0
var radius_px: float = 13.0
var dash_speed_px: float = 940.0
var dash_time: float = 0.145
var dash_cd: float = 1.05
var dash_max: int = 1
var charges: int = 1
var dash_t: float = 0.0
var charge_t: float = 0.0
var invulnerability_t: float = 0.0

func _ready() -> void:
    _apply_world_position()
    if is_instance_valid(input_source):
        input_source.connect("action_pressed", Callable(self, "_input_action"))

func set_input_source(source: Node) -> void:
    if is_instance_valid(input_source) and input_source.is_connected("action_pressed", Callable(self, "_input_action")):
        input_source.disconnect("action_pressed", Callable(self, "_input_action"))
    input_source = source
    if is_inside_tree() and is_instance_valid(input_source):
        input_source.connect("action_pressed", Callable(self, "_input_action"))

func _input_action(action: String) -> void:
    match action:
        "skill_pulse":
            skill_requested.emit("pulse")
        "skill_guard":
            skill_requested.emit("guard")
        "interact":
            interact_requested.emit()

func _physics_process(dt: float) -> void:
    if not auto_tick or not simulation_enabled or not is_instance_valid(input_source):
        return
    var direction: Vector2 = input_source.call("movement_vector")
    var dash_pressed: bool = input_source.call("consume_dash_request")
    simulate_tick(dt, direction, dash_pressed)
    if input_source.get("pointer_valid"):
        var offset: Vector2 = Vector2.ZERO
        if is_instance_valid(camera_source):
            offset = camera_source.call("camera_top_left")
        set_aim(input_source.get("pointer_logical"), offset)

func _apply_world_position() -> void:
    global_position = Mapper.logical_to_world(logical_position)

func set_logical_position(point: Vector2) -> void:
    logical_position = point
    _apply_world_position()
    position_changed.emit(logical_position)

func set_aim(pointer: Vector2, camera_origin: Vector2 = Vector2.ZERO) -> void:
    var from_player: Vector2 = pointer - (logical_position - camera_origin)
    if from_player.length_squared() <= 0.0:
        return
    aim_radians = atan2(from_player.y, from_player.x)
    aim_direction = from_player.normalized()
    aim_changed.emit(aim_radians, aim_direction)
    ## Visual child can be oriented toward the cursor by the art owner.
    rotation.y = atan2(aim_direction.x, -aim_direction.y)

func try_dash(direction: Vector2) -> bool:
    if dash_t > 0.0 or charges <= 0:
        return false
    dash_dir = Bindings.dash_direction(direction, velocity_px, last_move_dir)
    dash_t = dash_time
    invulnerability_t = maxf(invulnerability_t, 0.32)
    charges -= 1
    dash_started.emit(dash_dir)
    return true

func simulate_tick(dt: float, direction: Vector2, dash_pressed: bool = false, scaled_dt: float = -1.0) -> void:
    if not simulation_enabled:
        return
    var dts: float = dt if scaled_dt < 0.0 else scaled_dt
    var raw: Vector2 = direction.normalized() if direction.length_squared() > 1.0 else direction
    if raw.length_squared() > 0.0:
        last_move_dir = raw
    if dash_pressed:
        try_dash(raw)
    if hub_mode:
        ## Python step_hub subtracts timer BEFORE testing dash.
        dash_t = maxf(0.0, dash_t - dt)
        if dash_t > 0.0:
            velocity_px = dash_dir * dash_speed_px
        else:
            velocity_px += (raw * speed_px - velocity_px) * minf(1.0, 9.0 * dt)
        logical_position = Collision.move_hub(logical_position, velocity_px * dt, radius_px)
    else:
        ## Python step_player tests dash BEFORE subtracting dts.
        if dash_t > 0.0:
            dash_t -= dts
            velocity_px = dash_dir * dash_speed_px
        else:
            velocity_px += (raw * speed_px - velocity_px) * clampf(9.0 * dt, 0.0, 1.0)
        var new_position: Vector2 = Collision.move_arena(logical_position, velocity_px * dts, radius_px)
        if absf(new_position.x - (logical_position.x + velocity_px.x * dts)) > 0.0001:
            velocity_px.x = 0.0
        if absf(new_position.y - (logical_position.y + velocity_px.y * dts)) > 0.0001:
            velocity_px.y = 0.0
        logical_position = new_position
    invulnerability_t = maxf(0.0, invulnerability_t - dt)
    if charges < dash_max:
        charge_t += dt
        if charge_t >= dash_cd:
            charge_t = 0.0
            charges += 1
    elif not hub_mode:
        ## Python hub preserves accumulated charge_t when full.
        charge_t = 0.0
    _apply_world_position()
    position_changed.emit(logical_position)

func change_hp(amount: float) -> void:
    hp = clampf(hp + amount, 0.0, max_hp)
    health_changed.emit(hp, max_hp)

func combat_snapshot() -> Dictionary:
    ## API for C: combat uses legacy pixels; Godot world is a presentation layer.
    return {
        "logical_position": logical_position,
        "world_position": global_position,
        "velocity_px": velocity_px,
        "aim_radians": aim_radians,
        "aim_direction": aim_direction,
        "hp": hp,
        "max_hp": max_hp,
        "mana": mana,
        "max_mana": max_mana,
        "radius_px": radius_px,
        "dash_t": dash_t,
        "dash_dir": dash_dir,
        "dash_charges": charges,
        "invulnerability_t": invulnerability_t
    }
