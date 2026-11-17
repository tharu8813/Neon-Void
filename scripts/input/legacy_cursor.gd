extends Control
## Lightweight temporary 2D drawn cursor on top of 3D. A owns final art.
## Mouse OS cursor hides only while the playable viewport accepts pointer aim.
var input_source: Node
var active: bool = true
var hot: bool = false
var recoil: float = 0.0
var dash_ratio: float = 0.0
var reduced_effects: bool = false
var _spin: float = 0.0
var _time: float = 0.0
var _last_pointer: Vector2 = Vector2.ZERO
var _trail: Array[Dictionary] = []

func _ready() -> void:
    mouse_filter = Control.MOUSE_FILTER_IGNORE

func set_active(enabled: bool) -> void:
    active = enabled
    if not active:
        _show_native_cursor()

func _show_native_cursor() -> void:
    if Input.mouse_mode == Input.MOUSE_MODE_HIDDEN:
        Input.mouse_mode = Input.MOUSE_MODE_VISIBLE

func _exit_tree() -> void:
    _show_native_cursor()

func _process(delta: float) -> void:
    var should_show: bool = active and is_instance_valid(input_source) and bool(input_source.get("pointer_valid"))
    if not should_show:
        _show_native_cursor()
        queue_redraw()
        return
    if Input.mouse_mode == Input.MOUSE_MODE_VISIBLE:
        Input.mouse_mode = Input.MOUSE_MODE_HIDDEN
    var point: Vector2 = input_source.get("pointer_logical")
    if point.distance_squared_to(_last_pointer) > 49.0 and not reduced_effects:
        _trail.append({"point": _last_pointer, "life": 0.26})
        if _trail.size() > 8:
            _trail.pop_front()
    _last_pointer = point
    for i in range(_trail.size() - 1, -1, -1):
        var item: Dictionary = _trail[i]
        item["life"] = float(item["life"]) - delta
        if item["life"] <= 0.0:
            _trail.remove_at(i)
        else:
            _trail[i] = item
    if not reduced_effects:
        _spin += delta * (0.9 + recoil * 7.5 + dash_ratio * 13.0)
        _time += delta
    queue_redraw()

func _draw() -> void:
    if not active or not is_instance_valid(input_source) or not bool(input_source.get("pointer_valid")):
        return
    var center: Vector2 = input_source.get("pointer_logical")
    var color: Color = Color("#ff4d6d") if hot else Color("#6ff2ff")
    var radius: float = 26.0 + 2.2 * sin(_time * 3.8) + recoil * 3.5 + dash_ratio * 12.0
    draw_arc(center, radius + 13.0, 0.0, TAU, 64, Color("#245665"), 2.0, true)
    for i in range(3):
        var start: float = _spin + float(i) * TAU / 3.0
        draw_arc(center, radius, start, start + deg_to_rad(62.0), 18, color, 2.0, true)
    for i in range(4):
        var angle: float = _spin * 0.55 + float(i) * TAU / 4.0
        var axis: Vector2 = Vector2(cos(angle), sin(angle))
        draw_line(center + axis * (radius + 3.0), center + axis * (radius + 12.0), color, 2.0, true)
    var gap: float = 5.8 + recoil * 3.0 + dash_ratio * 5.0
    var reach: float = 18.0 + dash_ratio * 8.0
    draw_line(center + Vector2(-reach, 0), center + Vector2(-gap, 0), color, 1.5, true)
    draw_line(center + Vector2(gap, 0), center + Vector2(reach, 0), color, 1.5, true)
    for entry in _trail:
        var strength: float = float(entry["life"]) / 0.26
        draw_circle(entry["point"], 2.0 + strength * 6.0, Color(0.435, 0.949, 1.0, strength * 0.6))
    draw_circle(center, 2.5, Color.WHITE)
