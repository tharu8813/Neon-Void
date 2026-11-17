extends RefCounted
## Shared restrained cyberpunk palette; no external font or texture dependencies.
# The frozen Python neonvoid/constants.py + view.py modal palette.
const BACKDROP := Color("#04060e")
const SURFACE := Color("#071320")
const SURFACE_ALT := Color("#10313d")
const CYAN := Color("#6ff2ff")
const GOLD := Color("#ffd166")
const RED := Color("#ff4d6d")
const TEXT := Color("#d5ebff")
const MUTED := Color("#5b7898")

static func panel_style() -> StyleBoxFlat:
	var s := StyleBoxFlat.new()
	s.bg_color = SURFACE
	s.border_color = Color("#2a5166")
	s.set_border_width_all(2)
	s.set_corner_radius_all(0)
	s.content_margin_left = 20.0
	s.content_margin_right = 20.0
	s.content_margin_top = 16.0
	s.content_margin_bottom = 16.0
	return s

static func button(text_value: String, accent: Color = CYAN) -> Button:
	var b := Button.new()
	b.text = text_value
	b.custom_minimum_size = Vector2(130, 44)
	b.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	b.add_theme_font_size_override("font_size", 16)
	b.add_theme_color_override("font_color", TEXT)
	b.add_theme_color_override("font_hover_color", accent)
	b.add_theme_color_override("font_focus_color", accent)
	for state in ["normal", "hover", "pressed", "focus", "disabled"]:
		var box := panel_style()
		box.bg_color = SURFACE_ALT if state == "hover" or state == "pressed" else SURFACE
		box.border_color = accent if state == "focus" or state == "hover" else Color("#31536b")
		box.content_margin_top = 10
		box.content_margin_bottom = 10
		b.add_theme_stylebox_override(state, box)
	return b

static func label(text_value: String, size_px: int = 16, color: Color = TEXT) -> Label:
	var label_node := Label.new()
	label_node.text = text_value
	label_node.add_theme_font_size_override("font_size", size_px)
	label_node.add_theme_color_override("font_color", color)
	label_node.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	return label_node

static func progress(tint: Color = CYAN) -> ProgressBar:
	var bar := ProgressBar.new()
	bar.custom_minimum_size = Vector2(210, 16)
	bar.show_percentage = false
	var background := StyleBoxFlat.new()
	background.bg_color = Color("#203044")
	background.set_corner_radius_all(5)
	var fill := StyleBoxFlat.new()
	fill.bg_color = tint
	fill.set_corner_radius_all(5)
	bar.add_theme_stylebox_override("background", background)
	bar.add_theme_stylebox_override("fill", fill)
	return bar
