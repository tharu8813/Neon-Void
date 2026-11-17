extends Control
## Input-transparent gameplay cursor. Does not replace menu mouse pointers.
const ThemeD = preload("res://scripts/ui/neon_theme.gd")
var aim_enabled: bool = false
var reduced_motion: bool = false
var enemy_targeted: bool = false
var _phase: float = 0.0
var _tick: float = 0.0
var _old_mouse_mode: int = Input.MOUSE_MODE_VISIBLE

func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	custom_minimum_size = Vector2(96, 96)
	size = Vector2(96, 96)
	visible = false

func set_aim_enabled(enabled: bool) -> void:
	if aim_enabled == enabled:
		return
	aim_enabled = enabled
	visible = enabled
	if DisplayServer.get_name() != "headless":
		if enabled:
			_old_mouse_mode = Input.mouse_mode
			Input.mouse_mode = Input.MOUSE_MODE_HIDDEN
		else:
			Input.mouse_mode = _old_mouse_mode

func set_reduced_motion(enabled: bool) -> void:
	reduced_motion = enabled
	queue_redraw()

func set_targeted(enabled: bool) -> void:
	enemy_targeted = enabled

func _exit_tree() -> void:
	if aim_enabled and DisplayServer.get_name() != "headless":
		Input.mouse_mode = _old_mouse_mode

func _process(delta: float) -> void:
	if not aim_enabled:
		return
	position = get_viewport().get_mouse_position() - Vector2(48, 48)
	if not reduced_motion:
		_phase += delta * 1.6
		_tick += delta
		if _tick < 0.025:
			return
		_tick = 0.0
	queue_redraw()

func _draw() -> void:
	if not aim_enabled:
		return
	var tint: Color = ThemeD.RED if enemy_targeted else ThemeD.CYAN
	var center := Vector2(48, 48)
	var rotation_phase: float = 0.0 if reduced_motion else _phase
	draw_arc(center, 24.0, rotation_phase, rotation_phase + TAU * 0.28, 14, tint, 2.0, true)
	draw_arc(center, 24.0, rotation_phase + TAU * 0.5, rotation_phase + TAU * 0.78, 14, tint, 2.0, true)
	draw_line(center + Vector2(-32, 0), center + Vector2(-8, 0), tint, 1.4, true)
	draw_line(center + Vector2(8, 0), center + Vector2(32, 0), tint, 1.4, true)
	draw_line(center + Vector2(0, -32), center + Vector2(0, -8), tint, 1.4, true)
	draw_line(center + Vector2(0, 8), center + Vector2(0, 32), tint, 1.4, true)
	draw_circle(center, 2.7, Color.WHITE)
