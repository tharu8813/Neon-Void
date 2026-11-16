extends Control
## Data-only modal menu router. Combat and persistence own all side effects.
signal action_requested(action: String, payload: Dictionary)
signal setting_change_requested(key: String, value: Variant)
signal binding_change_requested(action: String, key: String)
signal closed
const ThemeD = preload("res://scripts/ui/neon_theme.gd")
const Catalog = preload("res://scripts/software/software_catalog.gd")
const LoadoutScene = preload("res://scenes/ui/software_loadout.tscn")
const ACTIONS := ["up", "down", "left", "right", "dash", "pause", "skill_pulse", "skill_guard", "interact"]
const ACTION_NAMES := ["위로 이동", "아래로 이동", "왼쪽 이동", "오른쪽 이동", "대시", "일시정지", "충격파", "보호막", "월드 상호작용"]
const DEFAULT_BINDINGS := {"up": "w", "down": "s", "left": "a", "right": "d", "dash": "space", "pause": "p", "skill_pulse": "q", "skill_guard": "e", "interact": "f"}
const SECTORS := ["CPU", "MEMORY", "STORAGE", "NETWORK"]
const DISPLAY_MODES := ["windowed", "fullscreen", "borderless"]
var current: String = ""
var state: Dictionary = {}
var _history: Array[String] = []
var _panel: PanelContainer
var _items: VBoxContainer
var _center: CenterContainer
var _capture: String = ""
var _status: Label

func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_STOP
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	var backdrop := ColorRect.new()
	backdrop.color = Color("#050a17ea")
	backdrop.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	backdrop.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(backdrop)
	_center = CenterContainer.new()
	_center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_center.mouse_filter = Control.MOUSE_FILTER_PASS
	add_child(_center)
	_panel = PanelContainer.new()
	_panel.add_theme_stylebox_override("panel", ThemeD.panel_style())
	_center.add_child(_panel)
	var layout := VBoxContainer.new()
	layout.add_theme_constant_override("separation", 12)
	_panel.add_child(layout)
	var top := HBoxContainer.new()
	layout.add_child(top)
	var brand := ThemeD.label("NEON VOID  /  SYSTEM", 22, ThemeD.CYAN)
	brand.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	top.add_child(brand)
	var back := ThemeD.button("뒤로")
	back.custom_minimum_size = Vector2(75, 40)
	back.size_flags_horizontal = Control.SIZE_SHRINK_END
	back.pressed.connect(go_back)
	top.add_child(back)
	var line := HSeparator.new()
	layout.add_child(line)
	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	layout.add_child(scroll)
	_items = VBoxContainer.new()
	_items.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_items.add_theme_constant_override("separation", 10)
	scroll.add_child(_items)
	_status = ThemeD.label("", 13, ThemeD.GOLD)
	layout.add_child(_status)
	_resize_panel()
	visible = false

func _notification(what: int) -> void:
	if what == NOTIFICATION_RESIZED and _panel != null:
		_resize_panel()

func _resize_panel() -> void:
	_panel.custom_minimum_size = Vector2(
		minf(720.0, maxf(300.0, size.x - 28.0)),
		minf(625.0, maxf(250.0, size.y - 28.0)))

func update_state(snapshot: Dictionary) -> void:
	state = snapshot.duplicate(true)
	# Do not rebuild a focused menu on every physics tick.
	if visible and current in ["database", "diagnostics", "profile", "loadout", "sectors"]:
		_render()

func open_screen(name: String, payload: Dictionary = {}) -> void:
	if name.is_empty():
		return
	if visible and not current.is_empty() and current != name:
		_history.append(current)
	current = name
	_capture = ""
	for key in payload:
		state[key] = payload[key]
	visible = true
	_render()

func close_screen() -> void:
	visible = false
	current = ""
	_capture = ""
	_history.clear()
	closed.emit()

func go_back() -> void:
	if not _capture.is_empty():
		_capture = ""
		_status.text = "키 변경 취소"
		return
	if not _history.is_empty():
		current = _history.pop_back()
		_render()
	else:
		close_screen()

func _clear_items() -> void:
	for child in _items.get_children():
		_items.remove_child(child)
		child.queue_free()
	_status.text = ""

func _info(text_value: String, color: Color = ThemeD.TEXT) -> void:
	_items.add_child(ThemeD.label(text_value, 16, color))

func _heading(title: String) -> void:
	_items.add_child(ThemeD.label(title, 23, ThemeD.CYAN))

func _button(text_value: String, action: String, payload: Dictionary = {}) -> void:
	var b := ThemeD.button(text_value)
	b.pressed.connect(_choose.bind(action, payload))
	_items.add_child(b)

func _choose(action: String, payload: Dictionary) -> void:
	if action.begins_with("page:"):
		open_screen(action.trim_prefix("page:"))
	elif action == "close":
		go_back()
	elif action == "capture_binding":
		_capture = str(payload.get("action", ""))
		_status.text = "새 키를 누르세요. ESC: 취소"
	else:
		action_requested.emit(action, payload)

func _render() -> void:
	if _items == null:
		return
	_clear_items()
	match current:
		"title":
			_heading("백신 시스템")
			_info("운영체제 보안 프로토콜")
			if bool(state.get("checkpoint_valid", false)):
				_button("체크포인트 이어하기", "page:checkpoint")
			_button("운영체제 배포", "page:deployment")
			_button("설정", "page:settings")
			_button("백신 데이터베이스", "page:database")
			if not bool(state.get("checkpoint_valid", false)):
				_button("기존 Python 저장 데이터 가져오기 (원본 보존)", "import_legacy")
			_button("프로필", "page:profile")
		"pause":
			_heading("일시정지")
			_button("계속 플레이", "resume")
			_button("설정", "page:settings")
			_button("설치된 소프트웨어", "page:loadout")
			_button("시스템 진단", "page:diagnostics")
			_button("허브로 돌아가기", "return_hub")
		"settings":
			_render_settings()
		"controls":
			_render_controls()
		"database":
			_render_database()
		"profile":
			_render_profile()
		"diagnostics":
			_render_diagnostics()
		"deployment":
			_heading("운영체제 배포 단말기")
			_info("작업할 OS를 선택한 뒤 배포를 시작합니다.")
			_button("새 운영체제 배포", "deploy")
			if bool(state.get("checkpoint_valid", false)):
				_button("이전 OS 이어하기", "page:checkpoint")
			_button("백신 데이터베이스", "page:database")
			_button("시스템 진단", "page:diagnostics")
		"sectors":
			_render_sectors()
		"purification":
			_heading("감염 정화")
			_info("남은 정화 횟수: %d" % int(state.get("purify_uses", 0)))
			_info("현재 감염 %.0f%%" % float(state.get("infection", 0.0)))
			_button("정화 실행", "purify")
		"os_clean":
			_heading("OS CLEAN")
			_info("모든 구역 정화가 완료되었습니다.", ThemeD.CYAN)
			_button("철수 단말기", "page:extraction")
			_button("진단", "page:diagnostics")
		"extraction":
			_render_extraction()
		"checkpoint":
			_heading("OS 체크포인트")
			_info(str(state.get("checkpoint_summary", "이전 운영체제 정보")))
			_button("이어하기", "resume_checkpoint")
			_button("새 OS 시작", "discard_checkpoint")
		"death":
			_heading("시스템 중단")
			_info("플레이어 시스템이 종료되었습니다.", ThemeD.RED)
			_button("새 OS 배포", "deploy")
			_button("백신 데이터베이스", "page:database")
		"loadout":
			_heading("설치된 소프트웨어")
			var view := LoadoutScene.instantiate()
			_items.add_child(view)
			view.set_levels(state.get("software_levels", {}))
		"error":
			_heading("오류")
			_info(str(state.get("error_message", "알 수 없는 오류")), ThemeD.RED)
			_button("확인", "close")
		_:
			_heading("화면 없음")
			_info("지원하지 않는 메뉴: " + current)
			_button("닫기", "close")

func _render_settings() -> void:
	_heading("설정")
	var opts: Dictionary = state.get("settings", {})
	_button("키 바인딩", "page:controls")
	_info("창 모드")
	var mode := OptionButton.new()
	for item in DISPLAY_MODES:
		mode.add_item(item)
	mode.select(maxi(0, DISPLAY_MODES.find(str(opts.get("display_mode", "windowed")))))
	mode.item_selected.connect(func(index: int) -> void:
		setting_change_requested.emit("display_mode", DISPLAY_MODES[index]))
	_items.add_child(mode)
	_info("해상도")
	var res := OptionButton.new()
	var resolutions := [Vector2i(960, 540), Vector2i(1280, 720), Vector2i(1600, 900), Vector2i(1920, 1080)]
	for size_pair in resolutions:
		res.add_item("%d × %d" % [size_pair.x, size_pair.y])
	var selected := Vector2i(int(opts.get("window_width", 1280)), int(opts.get("window_height", 720)))
	res.select(maxi(0, resolutions.find(selected)))
	res.item_selected.connect(func(index: int) -> void:
		setting_change_requested.emit("resolution", {"window_width": resolutions[index].x, "window_height": resolutions[index].y}))
	_items.add_child(res)
	_info("음량")
	var volume := HSlider.new()
	volume.min_value = 0.0
	volume.max_value = 1.0
	volume.step = 0.05
	volume.value = float(opts.get("volume", 0.9))
	volume.value_changed.connect(func(value: float) -> void:
		setting_change_requested.emit("volume", value))
	_items.add_child(volume)
	for key in ["muted", "show_fps", "reduced_effects"]:
		var text_value: String = {"muted": "음소거", "show_fps": "FPS 표시", "reduced_effects": "모션 줄이기"}[key]
		var check := CheckButton.new()
		check.text = text_value
		check.button_pressed = bool(opts.get(key, false))
		check.toggled.connect(func(value: bool) -> void:
			setting_change_requested.emit(key, value))
		_items.add_child(check)

func _render_controls() -> void:
	_heading("키 바인딩")
	_info("방향키는 이동 대체키, Shift는 대시 대체키로 유지됩니다.", ThemeD.MUTED)
	var bindings: Dictionary = state.get("settings", {}).get("bindings", DEFAULT_BINDINGS)
	for i in range(ACTIONS.size()):
		var uid: String = ACTIONS[i]
		_button("%s  :  %s" % [ACTION_NAMES[i], str(bindings.get(uid, DEFAULT_BINDINGS[uid])).to_upper()], "capture_binding", {"action": uid})

func _render_database() -> void:
	_heading("백신 데이터베이스")
	var db: Dictionary = state.get("database", {})
	_info("레벨 %d  ·  XP %d / %d" % [int(db.get("antivirus_level", 1)), int(db.get("xp", 0)), int(db.get("xp_to_next", 100))])
	_info("정화 완료 OS: %d" % int(db.get("clean_runs", 0)))
	var archive: Dictionary = db.get("software_archive", {})
	_info("영구 소프트웨어: %d종" % archive.size())
	if not archive.is_empty():
		_info("다음 OS에 가져갈 영구 소프트웨어를 선택하세요.", ThemeD.MUTED)
		_button("장착하지 않음", "select_archived", {"id": ""})
	for uid in archive:
		var record: Dictionary = Catalog.find(str(uid))
		if not record.is_empty():
			var choice: String = "  [선택됨]" if str(state.get("selected_archived_software", "")) == str(uid) else ""
			_button("%s Lv.%d%s" % [record["name"], int(archive[uid]), choice],
				"select_archived", {"id": str(uid)})

func _render_profile() -> void:
	_heading("프로필")
	var profile: Dictionary = state.get("profile", {})
	_info("최고 점수: %d" % int(profile.get("best_score", 0)))
	_info("최고 웨이브: %d" % int(profile.get("best_wave", 0)))
	_info("플레이 횟수: %d" % int(profile.get("runs", 0)))
	_info("성공적으로 철수한 OS: %d" % int(profile.get("extracted_runs", 0)))

func _render_diagnostics() -> void:
	_heading("시스템 진단")
	_info("운영체제 무결성: %.1f%%" % float(state.get("integrity", 100.0)))
	_info("감염도: %.1f%%" % float(state.get("infection", 0.0)))
	_info("현재 구역: %s" % str(state.get("sector", "OS HUB")))
	_info("남은 정화: %d" % int(state.get("purify_uses", 0)))
	_info("OS CLEAN: %s" % ("완료" if bool(state.get("os_clean", false)) else "진행 중"))
	_button("구역 이동", "page:sectors")
	_button("정화 단말기", "page:purification")

func _render_sectors() -> void:
	_heading("OS 구역")
	var progress: Dictionary = state.get("sector_progress", {})
	for sector_id in SECTORS:
		var count: int = int(progress.get(sector_id, 0))
		_button("%s  ·  %d/5 웨이브" % [sector_id, count], "enter_sector", {"sector_id": sector_id})

func _render_extraction() -> void:
	_heading("철수  /  소프트웨어 영구 보존")
	_info("선택한 소프트웨어 1개만 보존합니다.")
	var choices: Array = state.get("extraction_options", [])
	for uid in choices:
		var entry: Dictionary = Catalog.find(str(uid))
		if not entry.is_empty():
			_button("%s" % str(entry["name"]), "extract_software", {"id": str(uid)})
	if choices.is_empty():
		_info("보존 가능한 소프트웨어가 없습니다.")
		_button("돌아가기", "close")

func _unhandled_key_input(event: InputEvent) -> void:
	if not visible or not event is InputEventKey or not event.pressed or event.echo:
		return
	if not _capture.is_empty():
		var candidate: String = OS.get_keycode_string(event.keycode).to_lower()
		if event.keycode == KEY_ESCAPE:
			_capture = ""
			_status.text = "키 변경 취소"
		elif (candidate.length() == 1 and candidate >= "a" and candidate <= "z" and candidate not in ["m", "r", "o"]) or candidate == "space":
			var bindings: Dictionary = state.get("settings", {}).get("bindings", DEFAULT_BINDINGS)
			if candidate in bindings.values() and str(bindings.get(_capture, "")) != candidate:
				_status.text = "다른 동작에서 사용 중인 키입니다."
			else:
				binding_change_requested.emit(_capture, candidate)
				_status.text = "키 변경 요청: " + _capture + " → " + candidate
				_capture = ""
		else:
			_status.text = "지원하지 않거나 예약된 키입니다."
		get_viewport().set_input_as_handled()
	elif event.keycode == KEY_ESCAPE:
		go_back()
		get_viewport().set_input_as_handled()
