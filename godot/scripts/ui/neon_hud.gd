extends Control
## Lightweight input-transparent HUD. update_state accepts C's data snapshot.
const ThemeD = preload("res://scripts/ui/neon_theme.gd")
var _data: Dictionary = {}
var _hp: ProgressBar
var _mana: ProgressBar
var _hp_text: Label
var _mana_text: Label
var _dash_text: Label
var _skills_text: Label
var _location_text: Label
var _security_text: Label
var _combat_text: Label
var _stat_text: Label
var _software_text: Label
var _fps_text: Label
var _boss_panel: Control
var _boss_bar: ProgressBar
var _boss_text: Label

func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	var margins := MarginContainer.new()
	margins.anchor_right = 1.0
	margins.offset_bottom = 132.0
	for edge in ["left", "right", "top", "bottom"]:
		margins.add_theme_constant_override("margin_" + edge, 16)
	margins.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(margins)
	var top := HBoxContainer.new()
	top.mouse_filter = Control.MOUSE_FILTER_IGNORE
	top.add_theme_constant_override("separation", 22)
	margins.add_child(top)
	var resources := VBoxContainer.new()
	resources.custom_minimum_size.x = 340.0
	resources.mouse_filter = Control.MOUSE_FILTER_IGNORE
	top.add_child(resources)
	_hp_text = ThemeD.label("HP 100 / 100", 15)
	resources.add_child(_hp_text)
	_hp = ThemeD.progress(ThemeD.RED)
	resources.add_child(_hp)
	_mana_text = ThemeD.label("MANA 100 / 100", 15)
	resources.add_child(_mana_text)
	_mana = ThemeD.progress(ThemeD.CYAN)
	resources.add_child(_mana)
	var center := VBoxContainer.new()
	center.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	center.mouse_filter = Control.MOUSE_FILTER_IGNORE
	top.add_child(center)
	_location_text = ThemeD.label("OS HUB", 19, ThemeD.CYAN)
	center.add_child(_location_text)
	_security_text = ThemeD.label("감염 0%  ·  무결성 100%", 16)
	center.add_child(_security_text)
	_combat_text = ThemeD.label("전투 대기", 14, ThemeD.MUTED)
	center.add_child(_combat_text)
	var cooldowns := VBoxContainer.new()
	cooldowns.custom_minimum_size.x = 245.0
	cooldowns.mouse_filter = Control.MOUSE_FILTER_IGNORE
	top.add_child(cooldowns)
	_dash_text = ThemeD.label("대시 1/1", 16, ThemeD.GOLD)
	cooldowns.add_child(_dash_text)
	_skills_text = ThemeD.label("Q 0.0s / E 0.0s", 16)
	cooldowns.add_child(_skills_text)
	_fps_text = ThemeD.label("", 13, ThemeD.MUTED)
	cooldowns.add_child(_fps_text)
	_boss_panel = PanelContainer.new()
	_boss_panel.anchor_left = 0.5
	_boss_panel.anchor_right = 0.5
	_boss_panel.offset_left = -240.0
	_boss_panel.offset_right = 240.0
	_boss_panel.offset_top = 140.0
	_boss_panel.offset_bottom = 205.0
	_boss_panel.add_theme_stylebox_override("panel", ThemeD.panel_style())
	_boss_panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_boss_panel)
	var boss_col := VBoxContainer.new()
	boss_col.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_boss_panel.add_child(boss_col)
	_boss_text = ThemeD.label("", 15, ThemeD.RED)
	boss_col.add_child(_boss_text)
	_boss_bar = ThemeD.progress(ThemeD.RED)
	boss_col.add_child(_boss_bar)
	var bottom := PanelContainer.new()
	bottom.anchor_top = 1.0
	bottom.anchor_bottom = 1.0
	bottom.anchor_right = 0.56
	bottom.offset_left = 16.0
	bottom.offset_right = -12.0
	bottom.offset_top = -155.0
	bottom.offset_bottom = -15.0
	bottom.add_theme_stylebox_override("panel", ThemeD.panel_style())
	bottom.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(bottom)
	var stats := VBoxContainer.new()
	stats.mouse_filter = Control.MOUSE_FILTER_IGNORE
	bottom.add_child(stats)
	_stat_text = ThemeD.label("공격 10  ·  방어 4  ·  관통 0", 14)
	stats.add_child(_stat_text)
	_software_text = ThemeD.label("소프트웨어 0종  [TAB]", 13, ThemeD.MUTED)
	stats.add_child(_software_text)
	_boss_panel.visible = false
	if not _data.is_empty():
		_draw_state()

func update_state(payload: Dictionary) -> void:
	if payload == _data:
		return
	_data = payload.duplicate(true)
	if is_node_ready():
		_draw_state()

func _draw_state() -> void:
	var hp: float = float(_data.get("hp", 100.0))
	var maxhp: float = maxf(1.0, float(_data.get("maxhp", 100.0)))
	var mana: float = float(_data.get("mana", 100.0))
	var maxmana: float = maxf(1.0, float(_data.get("maxmana", 100.0)))
	_hp.max_value = maxhp
	_hp.value = clampf(hp, 0.0, maxhp)
	_hp_text.text = "HP  %.0f / %.0f" % [hp, maxhp]
	_mana.max_value = maxmana
	_mana.value = clampf(mana, 0.0, maxmana)
	_mana_text.text = "MANA  %.0f / %.0f" % [mana, maxmana]
	_dash_text.text = "DASH  %d / %d" % [int(_data.get("dash_charges", 1)), int(_data.get("dash_max", 1))]
	_skills_text.text = "Q %.1fs  ·  E %.1fs" % [float(_data.get("pulse_cd", 0.0)), float(_data.get("guard_cd", 0.0))]
	var sector: String = str(_data.get("sector", "OS HUB"))
	var wave: int = int(_data.get("wave", 0))
	_location_text.text = "%s  /  WAVE %d" % [sector, wave] if wave > 0 else sector
	_security_text.text = "감염 %.0f%%  ·  OS 무결성 %.0f%%" % [float(_data.get("infection", 0.0)), float(_data.get("integrity", 100.0))]
	_combat_text.text = str(_data.get("status", "전투 대기"))
	var stats: Dictionary = _data.get("stats", {})
	_stat_text.text = "피해 %.1f  ·  방어 %.1f  ·  관통 %.1f  ·  연사 %.1f/s" % [
		float(stats.get("dmg", 10.0)), float(stats.get("armor", 4.0)),
		float(stats.get("armor_pen", 0.0)), float(stats.get("rate", 5.4))]
	var modules: Dictionary = _data.get("software_levels", {})
	_software_text.text = "소프트웨어 %d종 [TAB]  ·  마나 회복 %.1f/s  ·  스킬 위력 %.2fx" % [
		modules.size(), float(stats.get("mana_regen", 6.0)), float(stats.get("skill_power", 1.0))]
	_fps_text.text = "FPS %.0f" % float(_data.get("fps", 0.0)) if bool(_data.get("show_fps", false)) else ""
	var boss_max: float = float(_data.get("boss_maxhp", 0.0))
	var boss_hp: float = float(_data.get("boss_hp", 0.0))
	_boss_panel.visible = boss_max > 0.0 and boss_hp > 0.0
	if _boss_panel.visible:
		_boss_text.text = "%s  %.0f / %.0f" % [str(_data.get("boss_name", "BOSS")), boss_hp, boss_max]
		_boss_bar.max_value = maxf(1.0, boss_max)
		_boss_bar.value = clampf(boss_hp, 0.0, boss_max)
