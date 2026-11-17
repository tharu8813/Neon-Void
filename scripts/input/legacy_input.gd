extends Node
## P3 input dispatcher. Only key press transitions trigger one-shot actions.
const Bindings = preload("res://scripts/input/legacy_bindings.gd")
const Mapper = preload("res://scripts/camera/legacy_viewport.gd")
signal action_pressed(action: String)
signal action_released(action: String)
signal system_pressed(command: String)
signal bindings_changed(bindings: Dictionary)

var bindings: Dictionary = Bindings.DEFAULT_BINDINGS.duplicate()
var pressed_keys: Dictionary = {}
var input_enabled: bool = true
var pointer_logical: Vector2 = Vector2(640.0, 240.0)
var pointer_valid: bool = false
var _dash_requested: bool = false

func _ready() -> void:
    get_window().focus_exited.connect(release_all)

func _input(event: InputEvent) -> void:
    if event is InputEventKey:
        var key_event: InputEventKey = event as InputEventKey
        if key_event.echo:
            return
        var key: String = OS.get_keycode_string(key_event.keycode).to_lower()
        if key_event.keycode == KEY_SHIFT:
            key = "shift"
        if key_event.keycode == KEY_SPACE:
            key = "space"
        if key_event.keycode == KEY_ESCAPE:
            key = "escape"
        if key_event.keycode == KEY_TAB:
            key = "tab"
        if key_event.keycode == KEY_F11:
            key = "f11"
        if key_event.keycode == KEY_ENTER or key_event.keycode == KEY_KP_ENTER:
            key = "return"
        if key == "return" and key_event.alt_pressed and key_event.pressed:
            system_pressed.emit("borderless")
            return
        set_key_state(key, key_event.pressed)
    elif event is InputEventMouseMotion:
        ## Godot InputEventMouse is already in logical viewport coordinates
        ## when A's fixed-viewport integration is active; do not scale twice.
        set_pointer_logical((event as InputEventMouseMotion).position)

func set_key_state(key: String, down: bool) -> void:
    if not input_enabled:
        return
    key = key.strip_edges().to_lower()
    if key.is_empty():
        return
    var was_down: bool = pressed_keys.has(key)
    if down == was_down:
        return
    if down:
        pressed_keys[key] = true
    else:
        pressed_keys.erase(key)
    var action: String = ""
    for candidate in Bindings.ACTION_ORDER:
        if bindings[candidate] == key:
            action = candidate
            break
    if key == "shift":
        action = "dash"
    if key == "escape":
        action = "pause"
    if down:
        if action == "dash":
            _dash_requested = true
        if not action.is_empty():
            action_pressed.emit(action)
        if ["tab", "o", "m", "r", "f11"].has(key):
            system_pressed.emit(key)
    elif not action.is_empty():
        action_released.emit(action)

func consume_dash_request() -> bool:
    var result: bool = _dash_requested
    _dash_requested = false
    return result

func movement_vector() -> Vector2:
    return Bindings.movement(pressed_keys, bindings) if input_enabled else Vector2.ZERO

func apply_bindings(candidate: Dictionary) -> bool:
    var validated: Dictionary = Bindings.validated(candidate)
    if validated.is_empty():
        return false
    bindings = validated
    release_all()
    bindings_changed.emit(bindings.duplicate())
    return true

func rebind_action(action: String, key: String) -> bool:
    return apply_bindings(Bindings.rebound(bindings, action, key))

func set_enabled(value: bool) -> void:
    if input_enabled != value:
        input_enabled = value
        release_all()

func release_all() -> void:
    pressed_keys.clear()
    _dash_requested = false
    pointer_valid = false

func set_pointer_logical(point: Vector2) -> void:
    pointer_valid = input_enabled and point.x >= 0.0 and point.x <= 1280.0 and point.y >= 0.0 and point.y <= 720.0
    if pointer_valid:
        pointer_logical = point

func set_pointer_native(pixel: Vector2, native_client_size: Vector2) -> void:
    pointer_valid = input_enabled and Mapper.contains_window_point(pixel, native_client_size)
    if pointer_valid:
        pointer_logical = Mapper.window_to_logical(pixel, native_client_size)
