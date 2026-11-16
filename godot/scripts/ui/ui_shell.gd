extends CanvasLayer
## UI integration boundary for C combat and E storage. No gameplay changes.
signal action_requested(action: String, payload: Dictionary)
signal setting_change_requested(key: String, value: Variant)
signal binding_change_requested(action: String, key: String)
signal software_card_requested(index: int)
signal pause_requested
signal resume_requested

const Hud = preload("res://scripts/ui/neon_hud.gd")
const Menu = preload("res://scripts/ui/neon_menu.gd")
const Cursor = preload("res://scripts/ui/neon_cursor.gd")
const OfferScene = preload("res://scenes/ui/software_offer.tscn")
var _hud: Control
var _menu: Control
var _cursor: Control
var _offer: Control
var _gameplay_active: bool = false
var _state: Dictionary = {}

func _ready() -> void:
	layer = 20
	_hud = Hud.new()
	add_child(_hud)
	_menu = Menu.new()
	add_child(_menu)
	_offer = OfferScene.instantiate() as Control
	add_child(_offer)
	_cursor = Cursor.new()
	add_child(_cursor)
	_menu.action_requested.connect(_on_menu_action)
	_menu.setting_change_requested.connect(_on_setting_change)
	_menu.binding_change_requested.connect(_on_binding_change)
	_menu.closed.connect(_on_menu_closed)
	_offer.card_selected.connect(_on_card_selected)
	if not _state.is_empty():
		update_game_state(_state)

func update_game_state(snapshot: Dictionary) -> void:
	_state = snapshot.duplicate(true)
	if not is_node_ready():
		return
	_hud.update_state(_state)
	_menu.update_state(_state)
	_cursor.set_reduced_motion(bool(_state.get("settings", {}).get("reduced_effects", false)))
	_cursor.set_targeted(bool(_state.get("target_locked", false)))
	_update_cursor()

func set_gameplay_active(enabled: bool) -> void:
	_gameplay_active = enabled
	_update_cursor()

func open_menu(screen: String, payload: Dictionary = {}) -> void:
	_menu.open_screen(screen, payload)
	_update_cursor()

func close_menu() -> void:
	_menu.close_screen()

func show_reward_cards(ids: Array[String], levels: Dictionary) -> void:
	_offer.set_offers(ids, levels)
	_update_cursor()

func complete_reward_selection() -> void:
	_offer.dismiss()
	_update_cursor()

func show_error(message: String) -> void:
	open_menu("error", {"error_message": message})

func _on_card_selected(index: int) -> void:
	software_card_requested.emit(index) # Combat calls SoftwareSystem.claim_offer(index).

func _on_setting_change(key: String, value: Variant) -> void:
	setting_change_requested.emit(key, value) # E validates and persists.

func _on_binding_change(action: String, key: String) -> void:
	binding_change_requested.emit(action, key) # E validates reserved/duplicate keys.

func _on_menu_action(action: String, payload: Dictionary) -> void:
	if action == "resume":
		close_menu()
		resume_requested.emit()
	else:
		action_requested.emit(action, payload)

func _on_menu_closed() -> void:
	_update_cursor()

func _update_cursor() -> void:
	if _cursor == null:
		return
	_cursor.set_aim_enabled(_gameplay_active and not _menu.visible and not _offer.visible)

func _unhandled_key_input(event: InputEvent) -> void:
	if not _gameplay_active or _menu.visible or _offer.visible:
		return
	if not event is InputEventKey or not event.pressed or event.echo:
		return
	if event.keycode == KEY_ESCAPE:
		pause_requested.emit()
		open_menu("pause")
		get_viewport().set_input_as_handled()
	elif event.keycode == KEY_TAB:
		pause_requested.emit()
		open_menu("loadout")
		get_viewport().set_input_as_handled()
