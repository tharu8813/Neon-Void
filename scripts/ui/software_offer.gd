extends Control
## Isolated P6 three-card reward UI; the combat owner grants the selected reward.
signal card_selected(index: int)
const Catalog = preload("res://scripts/software/software_catalog.gd")
const Preview = preload("res://scripts/ui/software_preview.gd")
const CYAN := Color("#37e8e0")
const GOLD := Color("#f0cf82")
var _cards: Array[Button] = []
var _offers: Array[String] = []
var _levels: Dictionary = {}
var _stats: Dictionary = {}
var _maxhp: float = 100.0
var _maxmana: float = 100.0
var _selected: int = 0

func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_STOP
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	visible = false

func set_offers(ids: Array[String], levels: Dictionary,
		stats: Dictionary = {}, maxhp: float = 100.0, maxmana: float = 100.0) -> void:
	_offers = ids.duplicate()
	_levels = levels.duplicate(true)
	_stats = stats.duplicate(true)
	_maxhp = maxhp
	_maxmana = maxmana
	_selected = 0
	_render_cards()
	visible = true
	if not _cards.is_empty():
		_cards[0].grab_focus()

func dismiss() -> void:
	visible = false
	_offers.clear()

func _render_cards() -> void:
	for child in get_children():
		child.queue_free()
	_cards.clear()
	var dim := ColorRect.new()
	dim.color = Color("#060d1bef")
	dim.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	dim.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(dim)
	var column := VBoxContainer.new()
	column.custom_minimum_size = Vector2(940, 332)
	column.set_anchors_and_offsets_preset(Control.PRESET_CENTER)
	column.position = Vector2(-470, -166)
	column.add_theme_constant_override("separation", 20)
	add_child(column)
	var headline := Label.new()
	headline.text = "SOFTWARE UPGRADE  /  소프트웨어 선택"
	headline.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	headline.add_theme_color_override("font_color", CYAN)
	headline.add_theme_font_size_override("font_size", 25)
	column.add_child(headline)
	var grid := HBoxContainer.new()
	grid.add_theme_constant_override("separation", 14)
	column.add_child(grid)
	for i in range(_offers.size()):
		var uid: String = _offers[i]
		var data: Dictionary = Catalog.find(uid)
		if uid == "bonus":
			data = {"name": "잔여 신호", "description": "점수 +250 즉시 획득", "tier": 0, "max_stack": 99}
		if data.is_empty():
			continue
		var button := Button.new()
		button.custom_minimum_size = Vector2(300, 235)
		button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		var current_level: int = int(_levels.get(uid, 0))
		var next_level: int = current_level + 1 if uid != "bonus" else 0
		var tier_label: String = "BONUS" if uid == "bonus" else "TIER %d" % (int(data["tier"]) + 1)
		var desc: String = str(data["description"]) if uid == "bonus" else Catalog.description_for_display(uid)
		var changes: Array[String] = Preview.effect_lines(uid, _stats, _maxhp, _maxmana)
		var forecast: String = "\n".join(changes)
		var partner: String = Preview.owned_complement(uid, _levels)
		var level_label: String = "" if uid == "bonus" else "Lv.%d → %d / %d" % [
			current_level, next_level, int(data["max_stack"])]
		button.text = "[%d]  %s\n%s  %s\n\n%s\n\n%s" % [
			i + 1, data["name"], tier_label, level_label, desc, forecast]
		if not partner.is_empty():
			button.text += "\n" + partner
		button.tooltip_text = "%s\n%s" % [desc, forecast]
		button.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		button.add_theme_font_size_override("font_size", 15)
		button.add_theme_color_override("font_color", GOLD if int(data["tier"]) == 2 else CYAN)
		button.pressed.connect(_on_pick.bind(i))
		grid.add_child(button)
		_cards.append(button)
	var hint := Label.new()
	hint.text = "1 / 2 / 3  ·  방향키 + Enter"
	hint.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	hint.add_theme_color_override("font_color", Color("#91adc0"))
	column.add_child(hint)

func _on_pick(index: int) -> void:
	if visible and index >= 0 and index < _offers.size():
		card_selected.emit(index)

func _unhandled_key_input(event: InputEvent) -> void:
	if not visible or not event is InputEventKey or not event.pressed or event.echo:
		return
	match event.keycode:
		KEY_1, KEY_KP_1:
			_choose_keyboard(0)
		KEY_2, KEY_KP_2:
			_choose_keyboard(1)
		KEY_3, KEY_KP_3:
			_choose_keyboard(2)
		KEY_LEFT, KEY_UP:
			_focus_card(-1)
		KEY_RIGHT, KEY_DOWN:
			_focus_card(1)
		KEY_ENTER, KEY_KP_ENTER:
			_choose_keyboard(_selected)

func _choose_keyboard(index: int) -> void:
	if index >= 0 and index < _cards.size():
		_on_pick(index)
		get_viewport().set_input_as_handled()

func _focus_card(delta: int) -> void:
	if _cards.is_empty():
		return
	_selected = posmod(_selected + delta, _cards.size())
	_cards[_selected].grab_focus()
	get_viewport().set_input_as_handled()
