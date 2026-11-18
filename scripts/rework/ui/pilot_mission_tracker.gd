extends PanelContainer
## Read-only A mission tracker. The word "saved" is never inferred from D preview.
## E snapshot is the source for cleaned hardware/objective completion/XP.
## D plan metadata supplies ONLY future difficulty/environment labels.
const HARDWARE := ["CPU", "GPU", "RAM", "SSD", "HDD"]
const OBJECTIVE_NAMES := {
    "secure_core": "연산 코어 방어", "stop_intruders": "침입 프로세스 제거",
    "disable_emitter": "과부하 장치 차단", "stabilize_coolant": "냉각 장치 안정화",
    "purge_corruption": "메모리 오염 제거", "verify_memory": "체크섬 진위 확인",
    "capture_node": "데이터 노드 확보", "seal_transfer": "전송 구역 봉인",
    "repair_platter": "플래터 수리", "restore_index": "인덱스 복원"
}
const DIFFICULTY_LABELS := {"easy": "쉬움", "normal": "보통", "hard": "어려움"}
const ENVIRONMENT_LABELS := {
    "normal": "이상없음", "mutation": "변이발생",
    "stack_overflow": "스택오버플로우", "tunnel_infection": "터널감염",
    "spread": "확산", "initial": "초기상태"
}
var mission: Dictionary = {}
var revision: int = -1
var is_verified: bool = false
var title_label: Label
var summary_label: Label
var objective_label: Label
var region_labels: Dictionary = {}

func _ready() -> void:
    name = "MissionTracker"
    process_mode = Node.PROCESS_MODE_ALWAYS
    mouse_filter = Control.MOUSE_FILTER_IGNORE
    visible = false
    custom_minimum_size = Vector2(320, 140)
    var style := StyleBoxFlat.new()
    style.bg_color = Color(0.025, 0.043, 0.071, 0.91)
    style.border_color = Color(0.35, 0.62, 0.82, 0.7)
    style.set_border_width_all(1)
    style.set_corner_radius_all(7)
    style.content_margin_left = 12
    style.content_margin_right = 12
    style.content_margin_top = 8
    style.content_margin_bottom = 8
    add_theme_stylebox_override("panel", style)
    var rows := VBoxContainer.new()
    rows.mouse_filter = Control.MOUSE_FILTER_IGNORE
    rows.add_theme_constant_override("separation", 4)
    add_child(rows)
    title_label = _label("하드웨어 진행도", Color(0.70, 0.94, 1.0), 16)
    rows.add_child(title_label)
    summary_label = _label("저장 확인 전", Color(0.75, 0.79, 0.9), 13)
    rows.add_child(summary_label)
    for id in HARDWARE:
        var line := _label(id, Color(0.64, 0.75, 0.82), 13)
        line.name = "Region_" + id
        rows.add_child(line)
        region_labels[id] = line
    objective_label = _label("", Color(0.91, 0.86, 0.65), 13)
    rows.add_child(objective_label)
    _position_at_right()

func _label(caption: String, tint: Color, size_pt: int) -> Label:
    var label := Label.new()
    label.text = caption
    label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
    label.mouse_filter = Control.MOUSE_FILTER_IGNORE
    label.add_theme_font_size_override("font_size", size_pt)
    label.add_theme_color_override("font_color", tint)
    return label

func _position_at_right() -> void:
    update_layout()

func update_layout() -> void:
    if get_viewport() == null:
        return
    var vp: Vector2 = get_viewport().get_visible_rect().size
    var width: float = minf(350.0, maxf(210.0, vp.x - 36.0))
    custom_minimum_size.x = width
    # Keep 1280+ HUD side by side. At narrower widths, move the mission
    # tracker beneath the status/controls so it never covers the HP bars.
    var y: float = 18.0 if vp.x >= 1100.0 else maxf(168.0, vp.y - 238.0)
    position = Vector2(maxf(0.0, vp.x - width - 18.0), y)

func show_saved_snapshot(loaded: Dictionary) -> void:
    if not bool(loaded.get("ok", false)):
        is_verified = false
        mission.clear()
        visible = false
        return
    var saved: Dictionary = loaded.get("snapshot", {})
    var vaccine: Variant = saved.get("vaccine")
    if not (vaccine is Dictionary) or not (vaccine.get("mission") is Dictionary):
        is_verified = false
        mission.clear()
        visible = false
        return
    mission = vaccine["mission"].duplicate(true)
    revision = int(loaded.get("revision", -1))
    is_verified = true
    visible = true
    summary_label.text = "의뢰 Lv.%d · 정화 %d/5 · 저장 r%d" % [
        int(mission.get("mission_level", 1)),
        mission.get("hardware_cleaned_ids", []).size(), revision]

func render_world(world: Node3D) -> void:
    if not is_verified or not visible or world == null:
        return
    var mode: String = str(world.get("current_mode"))
    if str(mission.get("run_id", "")) != str(world.get("current_run_id")):
        title_label.text = "의뢰 데이터 확인 필요"
        summary_label.text = "저장된 의뢰 ID와 현재 월드 ID가 일치하지 않아"
        objective_label.text = "정화/철수 상태를 표시하지 않아"
        for region in HARDWARE:
            region_labels[region].text = str(region) + " · 확인 필요"
        return
    var id: String = mode.to_upper()
    var world_mission: Variant = world.get("mission")
    var plan: Dictionary = world_mission if world_mission is Dictionary else {}
    var regions: Dictionary = plan.get("regions", {})
    var cleaned: Array = mission.get("hardware_cleaned_ids", [])
    var persisted_maps: Dictionary = mission.get("environment_rolls", {}).get(
        "e_checkpoint_v1", {}).get("regions", {})
    for key in HARDWARE:
        var region: Dictionary = regions.get(key, {})
        var difficulty: String = str(region.get("difficulty", ""))
        var environment: Dictionary = region.get("environment", {})
        var environment_id: String = str(environment.get("id", ""))
        var difficulty_text: String = DIFFICULTY_LABELS.get(difficulty, "미확인")
        var environment_text: String = ENVIRONMENT_LABELS.get(environment_id, "미확인")
        if environment_id == "mutation":
            environment_text += " %d단계" % int(environment.get("mutation_stage", 0))
        var status: String = "미방문"
        if cleaned.has(key):
            status = "정화 완료 · 저장됨"
        elif persisted_maps.has(key):
            status = "맵 승인 · 진행 중"
        var line: Label = region_labels[key]
        line.text = "%s  ·  %s  ·  %s / %s" % [
            key, status, difficulty_text, environment_text]
        line.add_theme_color_override("font_color",
            Color(0.42, 0.94, 0.74) if cleaned.has(key) else
            Color(0.83, 0.88, 1.0) if key == id else Color(0.57, 0.69, 0.79))
    if not HARDWARE.has(id):
        title_label.text = "하드웨어 현황 · 메인보드"
        objective_label.text = "순서 자유 · 정화한 구역은 재진입하지 않아"
        return
    title_label.text = id + " · 목표 진행"
    var objective_ids: Array = regions.get(id, {}).get("objective_ids", [])
    var persisted: Array = mission.get("objective_ids", [])
    var complete: int = 0
    var waiting: Array[String] = []
    for objective in objective_ids:
        var uid: String = str(objective)
        if persisted.has(id + "." + uid):
            complete += 1
        else:
            waiting.append(str(OBJECTIVE_NAMES.get(uid, uid)))
    var footer: String = "목표 %d/%d 저장 완료" % [complete, objective_ids.size()]
    if cleaned.has(id):
        footer += " · 철수 지점 사용 가능"
    elif not waiting.is_empty():
        footer += " · 남은 목표: " + ", ".join(waiting)
    else:
        footer += " · 보스 정화 확인 대기"
    objective_label.text = footer

func show_blocked() -> void:
    is_verified = false
    visible = false
