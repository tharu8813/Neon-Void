extends SceneTree
## P7 headless fixture. Does not claim mouse pixel accuracy or Windows DPI coverage.
func _initialize() -> void:
	call_deferred("_check")

func _fail(message: String) -> void:
	printerr("P7 UI FAIL: " + message)
	quit(1)

func _check() -> void:
	var fixture: PackedScene = load("res://scenes/ui/ui_preview.tscn") as PackedScene
	if fixture == null:
		_fail("preview scene not found")
		return
	var demo: Control = fixture.instantiate() as Control
	root.add_child(demo)
	var shell: CanvasLayer = demo.shell
	if shell == null:
		_fail("missing CanvasLayer")
		return
	var hud: Control = shell._hud
	var menu: Control = shell._menu
	var cursor: Control = shell._cursor
	if hud == null or menu == null or cursor == null:
		_fail("missing HUD/menu/cursor")
		return
	for page_name in [
		"title", "pause", "settings", "controls", "database",
		"profile", "diagnostics", "deployment", "sectors",
		"purification", "os_clean", "extraction", "checkpoint",
		"death", "loadout", "error"
	]:
		menu.open_screen(page_name)
		if not menu.visible or menu.current != page_name or menu._items.get_child_count() == 0:
			_fail("screen not rendered: " + page_name)
			return
	menu.open_screen("settings")
	var changed: Array[String] = []
	menu.setting_change_requested.connect(func(key: String, value: Variant) -> void:
		changed.append(key))
	menu.setting_change_requested.emit("volume", 0.5)
	if changed != ["volume"]:
		_fail("settings signal")
		return
	var actions: Array[String] = []
	shell.action_requested.connect(func(action: String, payload: Dictionary) -> void:
		actions.append(action))
	menu.open_screen("sectors")
	menu._choose("enter_sector", {"sector_id": "CPU"})
	if actions != ["enter_sector"]:
		_fail("sector terminal request")
		return
	shell.close_menu()
	shell.set_gameplay_active(true)
	if not cursor.aim_enabled:
		_fail("gameplay cursor must be active")
		return
	shell.open_menu("pause")
	if cursor.aim_enabled:
		_fail("cursor must stop over menus")
		return
	shell.close_menu()
	var chosen: Array[int] = []
	shell.software_card_requested.connect(func(index: int) -> void:
		chosen.append(index))
	shell.show_reward_cards(["power", "rapid", "bonus"], {})
	shell._offer._on_pick(1)
	if chosen != [1] or cursor.aim_enabled:
		_fail("reward event/cursor isolation")
		return
	shell.complete_reward_selection()
	if not cursor.aim_enabled:
		_fail("cursor restoration")
		return
	shell.update_game_state({
		"hp": 10.0, "maxhp": 100.0, "mana": 5.0,
		"maxmana": 100.0, "boss_hp": 20.0, "boss_maxhp": 100.0,
		"boss_name": "OVERSEER", "infection": 55.0, "integrity": 40.0,
		"software_levels": {"rapid": 2}, "settings": {"reduced_effects": true},
	})
	if not hud._boss_panel.visible or not cursor.reduced_motion:
		_fail("status event and reduced-motion binding")
		return
	shell.show_error("Read-only fixture error")
	if not menu.visible or menu.current != "error":
		_fail("error message routing")
		return
	print("P7 UI HEADLESS SMOKE PASS")
	quit(0)
