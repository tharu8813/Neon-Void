extends Control
## Safe single main entrypoint. Distinct modes have distinct E/legacy save
## namespaces; the launcher never writes a save or auto-grants any QA items.
const REWORK_MAIN: String = "res://scenes/rework/integration/rework_main.tscn"
const REWORK_QA: String = "res://scenes/rework/integration/rework_qa_boot.tscn"
const CLASSIC_GODOT: String = "res://scenes/main.tscn"
const MODES: Dictionary = {
    "ReworkProduction": REWORK_MAIN,
    "ReworkQADemo": REWORK_QA,
    "ClassicGodot": CLASSIC_GODOT,
}

var buttons: Dictionary = {}
var status: Label

func _ready() -> void:
    name = "NeonVoidLauncher"
    process_mode = Node.PROCESS_MODE_ALWAYS
    get_tree().paused = false
    _build()
    get_window().title = "Neon Void | 게임 선택"
    print("NEON_MAIN_LAUNCHER_ACTIVE main=",
        ProjectSettings.get_setting("application/run/main_scene"),
        " modes=", MODES.size())

func _build() -> void:
    var background := ColorRect.new()
    background.name = "Backdrop"
    background.color = Color(0.018, 0.028, 0.054)
    background.mouse_filter = Control.MOUSE_FILTER_IGNORE
    background.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
    add_child(background)

    var center := CenterContainer.new()
    center.name = "ModeSelection"
    center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
    add_child(center)

    var column := VBoxContainer.new()
    column.name = "ModeOptions"
    column.custom_minimum_size = Vector2(420, 0)
    column.add_theme_constant_override("separation", 12)
    center.add_child(column)

    var heading := Label.new()
    heading.name = "GameTitle"
    heading.text = "NEON VOID"
    heading.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    heading.add_theme_font_size_override("font_size", 32)
    heading.add_theme_color_override("font_color", Color(0.43, 0.94, 1.0))
    column.add_child(heading)

    var subtitle := Label.new()
    subtitle.name = "EditionLabel"
    subtitle.text = "Rework v1.0 · 실행 모드 선택"
    subtitle.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    subtitle.add_theme_font_size_override("font_size", 15)
    subtitle.add_theme_color_override("font_color", Color(0.74, 0.85, 0.95))
    column.add_child(subtitle)

    _add_button(column, "ReworkProduction",
        "새 리워크 플레이",
        "백신 성장과 5개 하드웨어 · 일반 저장 데이터\n처음부터 진행 (QA 보너스 없음)")
    _add_button(column, "ReworkQADemo",
        "새 리워크 QA · 기능 체험",
        "별도 테스트 슬롯 · 스킬 3개, 능력치 포인트 5개,\n복제 3회 및 샘플 플러그인")
    _add_button(column, "ClassicGodot",
        "기존 Godot 게임",
        "원래 4개 OS 구역 · 기존 진행 데이터 유지")

    status = Label.new()
    status.name = "LauncherStatus"
    status.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
    status.add_theme_color_override("font_color", Color(0.66, 0.79, 0.87))
    status.text = "기존 Python 게임과 저장 파일은 변경하지 않습니다."
    column.add_child(status)

    buttons["ReworkProduction"].grab_focus()

func _add_button(parent: VBoxContainer, mode_id: String,
        label: String, explanation: String) -> void:
    var button := Button.new()
    button.name = mode_id
    button.custom_minimum_size = Vector2(460, 83)
    button.text = "%s\n%s" % [label, explanation]
    button.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
    button.add_theme_font_size_override("font_size", 16)
    button.set_meta("target_scene", MODES[mode_id])
    button.pressed.connect(_on_mode_pressed.bind(mode_id))
    parent.add_child(button)
    buttons[mode_id] = button

func _on_mode_pressed(mode_id: String) -> void:
    if not MODES.has(mode_id):
        status.text = "알 수 없는 실행 모드: " + mode_id
        return
    var target: String = str(MODES[mode_id])
    var scene: PackedScene = load(target) as PackedScene
    if scene == null:
        status.text = "실행 파일에 해당 씬이 없습니다: " + target
        push_error("NEON_LAUNCHER_MODE_NOT_FOUND " + target)
        return
    # Scene switch is deferred so the button signal finishes first.
    get_tree().call_deferred("change_scene_to_packed", scene)
