extends PanelContainer
## A-only terminal result UI. Opens only after E snapshot independently confirms
## that the exact D run was closed with the matching outcome.
## Never pays/retries awards, undoes death or auto-resumes an interrupted run.
signal dismissed()

const TEXT := Color(0.86, 0.95, 1.0)
var title_label: Label
var result_label: Label
var progression_label: Label
var receipt_label: Label
var dismissal: Button
var verified_run_id: String = ""

func _ready() -> void:
    name = "MissionResult"
    process_mode = Node.PROCESS_MODE_ALWAYS
    mouse_filter = Control.MOUSE_FILTER_STOP
    visible = false
    custom_minimum_size = Vector2(590, 280)
    var style := StyleBoxFlat.new()
    style.bg_color = Color(0.016, 0.033, 0.064, 0.98)
    style.border_color = Color(0.27, 0.83, 0.89)
    style.set_border_width_all(2)
    style.set_corner_radius_all(12)
    style.content_margin_left = 22
    style.content_margin_right = 22
    style.content_margin_top = 17
    style.content_margin_bottom = 17
    add_theme_stylebox_override("panel", style)
    var layout := VBoxContainer.new()
    layout.add_theme_constant_override("separation", 13)
    add_child(layout)
    title_label = _text("의뢰 결과 확인", 22, Color(0.48, 0.92, 0.98))
    layout.add_child(title_label)
    result_label = _text("저장 검증 중", 17, TEXT)
    layout.add_child(result_label)
    progression_label = _text("", 14, TEXT)
    layout.add_child(progression_label)
    receipt_label = _text("", 12, Color(0.57, 0.7, 0.77))
    layout.add_child(receipt_label)
    dismissal = Button.new()
    dismissal.name = "CloseMissionResult"
    dismissal.text = "확인 · 허브에서 계속"
    dismissal.custom_minimum_size.y = 42
    dismissal.pressed.connect(_dismiss)
    layout.add_child(dismissal)
    _recenter()

func _text(value: String, size_pt: int, color: Color) -> Label:
    var label := Label.new()
    label.text = value
    label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
    label.add_theme_font_size_override("font_size", size_pt)
    label.add_theme_color_override("font_color", color)
    return label

func _recenter() -> void:
    update_layout()

func update_layout() -> void:
    if get_viewport() == null:
        return
    var vp: Vector2 = get_viewport().get_visible_rect().size
    var width: float = minf(590.0, maxf(260.0, vp.x - 36.0))
    custom_minimum_size.x = width
    position = Vector2(maxf(0.0, (vp.x - width) * 0.5),
        maxf(0.0, (vp.y - 300.0) * 0.5))

func show_verified(fact: Dictionary, loaded: Dictionary) -> bool:
    visible = false
    verified_run_id = ""
    if not bool(loaded.get("ok", false)) or int(loaded.get("revision", 0)) < 1:
        return false
    if int(fact.get("schema", -1)) != 1:
        return false
    var run_id: String = str(fact.get("run_id", ""))
    var kind: String = str(fact.get("type", ""))
    var success: bool = kind == "mission_extracted"
    if run_id.is_empty() or not ["mission_extracted", "mission_failed"].has(kind):
        return false
    if str(fact.get("outcome", "")) != ("success" if success else "death"):
        return false
    var cleared: int = int(fact.get("cleared_count", -1))
    if cleared < (1 if success else 0) or cleared > 5:
        return false
    if success and bool(fact.get("full_clear", false)) != (cleared == 5):
        return false
    var state: Dictionary = loaded.get("snapshot", {})
    var retired: bool = bool(state.get("selection_required", false))
    var vaccine_value: Variant = state.get("vaccine")
    if retired:
        if success or vaccine_value != null:
            return false
    elif not (vaccine_value is Dictionary):
        return false
    var vaccine: Dictionary = vaccine_value if vaccine_value is Dictionary else {}
    if not retired:
        if vaccine.get("mission") != null or str(vaccine.get("closed_runs", {}).get(
                run_id, "")) != ("extracted" if success else "dead"):
            return false
    verified_run_id = run_id
    visible = true
    title_label.text = "의뢰 완료 · 성공 철수" if success else \
        "의뢰 종료 · 백신 손실"
    title_label.add_theme_color_override("font_color",
        Color(0.49, 0.95, 0.78) if success else Color(1.0, 0.48, 0.49))
    result_label.text = "정화한 하드웨어 %d / 5%s" % [
        cleared, " · 전체 정화" if cleared == 5 else " · 부분 종료"]
    if retired:
        progression_label.text = "네 번째 사망 · 백신 최종 파기\n새 백신을 선택해야 다시 출전할 수 있어."
        dismissal.text = "백신 선택으로 돌아가기"
    else:
        var copies: int = int(vaccine.get("copies_remaining", 0))
        progression_label.text = "백신 Lv.%d · 보유 XP %d · 복제 %d/3" % [
            int(vaccine.get("level", 1)), int(vaccine.get("xp", 0)), copies]
        if copies == 0:
            progression_label.text += "\n주의: 다음 사망 시 이 백신은 최종 파기돼."
        dismissal.text = "허브에서 계속"
    receipt_label.text = "E 영구 저장 확인 r%d · 해당 의뢰 종료 영수증 검증됨" % [
        int(loaded.get("revision", 0))]
    return true

func _dismiss() -> void:
    if not visible:
        return
    visible = false
    dismissed.emit()
