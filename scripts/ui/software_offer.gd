extends Control
## Isolated P6 three-card reward UI; the combat owner grants the selected reward.
signal card_selected(index: int)
const Catalog = preload("res://scripts/software/software_catalog.gd")
const CYAN := Color("#37e8e0")
const GOLD := Color("#f0cf82")
var _cards: Array[Button] = []
var _offers: Array[String] = []
var _levels: Dictionary = {}
var _selected: int = 0

func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_STOP
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	visible = false

func set_offers(ids: Array[String], levels: Dictionary) -> void:
	_offers = ids.duplicate()
	_levels = levels.duplicate()
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
	column.custom_minimum_size = Vector2(940, 290)
	column.set_anchors_and_offsets_preset(Control.PRESET_CENTER)
	column.position = Vector2(-470, -145)
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
		button.custom_minimum_size = Vector2(300, 190)
		button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		button.text = "%d   %s\n\n%s\n\nLv. %d / %d" % [i + 1, data["name"], (str(data["description"]) if uid == "bonus" else Catalog.description_for_display(uid)), int(_levels.get(uid, 0)), int(data["max_stack"])]
		button.tooltip_text = str((str(data["description"]) if uid == "bonus" else Catalog.description_for_display(uid)))
		button.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		button.add_theme_font_size_override("font_size", 17)
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
