extends SceneTree
## P7 headless fixture. Does not claim mouse pixel accuracy or Windows DPI coverage.
func _initialize() -> void:
	call_deferred("_check")

func _fail(message: String) -> void:
	printerr("P7 UI FAIL: " + message)
	quit(1)

func _rect_matches(node: Control, rect: Rect2, ax: float = 0.0, ay: float = 0.0) -> bool:
	return is_equal_approx(node.anchor_left, ax) and is_equal_approx(node.anchor_right, ax) and \
		is_equal_approx(node.anchor_top, ay) and is_equal_approx(node.anchor_bottom, ay) and \
		is_equal_approx(node.offset_left, rect.position.x) and \
		is_equal_approx(node.offset_top, rect.position.y) and \
		is_equal_approx(node.offset_right, rect.end.x) and \
		is_equal_approx(node.offset_bottom, rect.end.y)

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
	# Source: neonvoid/view.py::_build_hud with frozen 1280x720 layout.
	var layout: Array = [
		[hud._combat_hud.get_node("HPFrame"), Rect2(24, 22, 240, 12), 0.0, 0.0],
		[hud._combat_hud.get_node("ManaFrame"), Rect2(24, 60, 240, 14), 0.0, 0.0],
		[hud._combat_hud.get_node("LiveStatsPanel"), Rect2(19, 90, 354, 172), 0.0, 0.0],
		[hud._combat_hud.get_node("SkillPanel"), Rect2(-275, -86, 550, 62), 0.5, 1.0],
		[hud._combat_hud.get_node("InfectionPanel"), Rect2(-420, 107, 396, 83), 1.0, 0.0],
		[hud._combat_hud.get_node("WaveText"), Rect2(-260, 8, 236, 40), 1.0, 0.0],
		[hud._boss_panel, Rect2(-300, 52, 600, 80), 0.5, 0.0],
		[hud._world_hud.get_node("WorldInfoPanel"), Rect2(12, 12, 481, 63), 0.0, 0.0],
	]
	for spec in layout:
		if not _rect_matches(spec[0], spec[1], spec[2], spec[3]):
			_fail("Python A/B HUD coordinate mismatch: " + str(spec[0].name))
			return
	if not hud._stat_text.text.contains("공격력") or not hud._stat_right.text.contains("마나 회복"):
		_fail("dual-column live stats are missing")
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
	var sample_offers: Array[String] = ["power", "rapid", "bonus"]
	shell.show_reward_cards(sample_offers, {})
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
		"attack_warning_count": 2, "attack_warning_urgent": true,
		"target_locked": true, "guard_t": 1.0,
		"software_levels": {"rapid": 2}, "status": "combat",
		"wave": 5, "score": 340, "pulse_cd": 0.0, "guard_cd": 2.5,
		"stats": {"dmg": 18, "crit": 0.25, "armor": 10, "armor_pen": 3,
			"burn_chance": 0.2, "toxin_chance": 0.1, "mana_regen": 8},
		"settings": {"reduced_effects": true,
			"bindings": {"skill_pulse": "q", "skill_guard": "e"}},
	})
	if not hud._boss_panel.visible or not cursor.reduced_motion:
		_fail("status event and reduced-motion binding")
		return
	if not hud._combat_hud.visible or hud._world_hud.visible:
		_fail("combat-only HUD visibility")
		return
	# P1: dense stats are opt-in and telegraphs remain readable with reduced effects.
	if not hud._stats_compact.visible or hud._combat_hud.get_node("LiveStatsPanel").visible:
		_fail("P1 compact HUD is default and old dense panel stays hidden")
		return
	if not hud._stat_glance.text.contains("F1") or not hud._stat_text.text.contains("30.0"):
		_fail("P1 compact hint and real guard +20 defense")
		return
	hud.toggle_stat_details()
	if not hud._combat_hud.get_node("LiveStatsPanel").visible or hud._stats_compact.visible:
		_fail("P1 F1 expands full live stats")
		return
	hud.toggle_stat_details()
	if not hud._stats_compact.visible or hud._combat_hud.get_node("StatsLeft").visible:
		_fail("P1 F1 returns to compact stats")
		return
	if not hud._threat_alert.visible or not hud._threat_alert.text.contains("즉시 회피"):
		_fail("P1 non-animated urgent telegraph text visible in reduced effects")
		return
	if not cursor.enemy_targeted:
		_fail("P1 cursor receives actual hovered-target flag")
		return
	if not is_equal_approx(hud._pulse_bar.value, 1.0) or not (hud._guard_bar.value > 0.0 and hud._guard_bar.value < 1.0):
		_fail("P1 cooldown bars fill toward ready, without jumping at completion")
		return
	if not hud._wave_text.text.contains("WAVE 5") or not hud._score_text.text.contains("340"):
		_fail("score/wave locations must be backed by data")
		return
	if not hud._stat_text.text.contains("25%") or not hud._stat_right.text.contains("20%"):
		_fail("source-equivalent crit and burn statistics")
		return
	if not hud._pulse_text.text.contains("마나 부족") or not hud._guard_text.text.contains("2.5s"):
		_fail("actual mana cost and cooldown visibility")
		return
	shell.update_game_state({"status": "base", "nearby": "OS DEPLOYMENT", "hp": 100.0})
	if not hud._world_hud.visible or hud._combat_hud.visible:
		_fail("base hub must hide combat HUD")
		return
	if not hud._world_prompt.text.contains("OS DEPLOYMENT") or not hud._world_hud.get_node("NearbyPanel").visible:
		_fail("original in-world F terminal prompt")
		return
	shell.update_game_state({"status": "hub", "nearby": "", "integrity": 85.0, "infection": 15.0})
	if hud._world_hud.get_node("NearbyPanel").visible:
		_fail("far-away hub terminal must not show F prompt")
		return
	shell.show_error("Read-only fixture error")
	if not menu.visible or menu.current != "error":
		_fail("error message routing")
		return
	print("P7 UI HEADLESS SMOKE PASS")
	quit(0)
