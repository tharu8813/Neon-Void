extends Control
## Independent visual fixture. Never opens real player saves.
const ShellScene = preload("res://scenes/ui/ui_shell.tscn")
var shell: CanvasLayer

func _ready() -> void:
	var bg := ColorRect.new()
	bg.color = Color("#081220")
	bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(bg)
	var grid_text := Label.new()
	grid_text.text = "NEON VOID / UI PREVIEW / GODOT 4"
	grid_text.position = Vector2(35, 280)
	grid_text.add_theme_font_size_override("font_size", 34)
	grid_text.add_theme_color_override("font_color", Color("#1a4053"))
	add_child(grid_text)
	shell = ShellScene.instantiate() as CanvasLayer
	add_child(shell)
	shell.update_game_state({
		"hp": 72.0, "maxhp": 120.0, "mana": 45.0, "maxmana": 100.0,
		"dash_charges": 2, "dash_max": 3, "pulse_cd": 1.2, "guard_cd": 0.0,
		"sector": "MEMORY SECTOR", "wave": 4, "infection": 37.0,
		"integrity": 82.0, "boss_hp": 440.0, "boss_maxhp": 800.0,
		"boss_name": "REPLICATOR", "status": "웨이브 전투 중",
		"stats": {"dmg": 22, "armor": 12, "armor_pen": 4, "rate": 7.2, "mana_regen": 8, "skill_power": 1.25},
		"software_levels": {"rapid": 2, "power": 3, "armor": 1},
		"show_fps": true, "fps": 60.0,
		"settings": {"volume": 0.9, "muted": false, "show_fps": true,
			"reduced_effects": false, "display_mode": "windowed",
			"window_width": 1280, "window_height": 720,
			"bindings": {"up":"w","down":"s","left":"a","right":"d",
				"dash":"space","pause":"p","skill_pulse":"q","skill_guard":"e","interact":"f"}},
		"sector_progress": {"CPU": 5, "MEMORY": 3, "STORAGE": 0, "NETWORK": 0},
		"purify_uses": 2, "os_clean": false,
		"checkpoint_valid": true, "checkpoint_summary": "MEMORY 3/5 · 체력 72",
		"database": {"antivirus_level": 4, "xp": 85, "xp_to_next": 250,
			"clean_runs": 2, "software_archive": {"power": 1}},
		"extraction_options": ["power", "rapid", "armor"],
		"profile": {"best_score": 12000, "best_wave": 5, "runs": 17, "extracted_runs": 2},
	})
	shell.set_gameplay_active(false)
	shell.open_menu("pause")
