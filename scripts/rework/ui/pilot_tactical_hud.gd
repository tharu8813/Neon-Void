extends PanelContainer
## A-only, read-only tactical HUD for the opt-in rework pilot.
## E is authoritative for progression; B is authoritative for live resources.
## This view NEVER writes a slot, infers a reward or resumes an interrupted run.

var vaccine_label: Label
var mission_label: Label
var save_label: Label
var plugin_label: Label
var health_label: Label
var shield_label: Label
var mana_label: Label
var health_meter: ProgressBar
var shield_meter: ProgressBar
var mana_meter: ProgressBar
var blocked: bool = false

func _ready() -> void:
    name = "TacticalHUD"
    process_mode = Node.PROCESS_MODE_ALWAYS
    mouse_filter = Control.MOUSE_FILTER_IGNORE
    position = Vector2(18, 12)
    custom_minimum_size = Vector2(632, 132)
    var panel_style := StyleBoxFlat.new()
    panel_style.bg_color = Color(0.025, 0.055, 0.09, 0.91)
    panel_style.border_color = Color(0.15, 0.86, 0.95, 0.8)
    panel_style.set_border_width_all(1)
    panel_style.set_corner_radius_all(7)
    panel_style.content_margin_left = 12
    panel_style.content_margin_right = 12
    panel_style.content_margin_top = 7
    panel_style.content_margin_bottom = 7
    add_theme_stylebox_override("panel", panel_style)

    var body := VBoxContainer.new()
    body.add_theme_constant_override("separation", 4)
    body.mouse_filter = Control.MOUSE_FILTER_IGNORE
    add_child(body)
    var top := HBoxContainer.new()
    top.mouse_filter = Control.MOUSE_FILTER_IGNORE
    body.add_child(top)
    vaccine_label = _make_text("백신 정보 확인 중", Color(0.72, 0.95, 1.0))
    vaccine_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
    top.add_child(vaccine_label)
    mission_label = _make_text("허브", Color(0.9, 0.83, 1.0))
    top.add_child(mission_label)

    var meters := HBoxContainer.new()
    meters.add_theme_constant_override("separation", 10)
    meters.mouse_filter = Control.MOUSE_FILTER_IGNORE
    body.add_child(meters)
    var hp: Dictionary = _make_resource(meters, "체력", Color(0.99, 0.3, 0.46))
    health_label = hp["label"]
    health_meter = hp["meter"]
    var barrier: Dictionary = _make_resource(meters, "보호막", Color(0.3, 0.85, 1.0))
    shield_label = barrier["label"]
    shield_meter = barrier["meter"]
    var mp: Dictionary = _make_resource(meters, "마나", Color(0.65, 0.43, 1.0))
    mana_label = mp["label"]
    mana_meter = mp["meter"]
    save_label = _make_text("저장 확인 전 · 전투 자원 대기", Color(0.68, 0.76, 0.85))
    body.add_child(save_label)
    plugin_label = _make_text("전투 플러그인 · E/B 대조 대기", Color(0.65, 0.85, 0.90))
    body.add_child(plugin_label)
    render_combat_snapshot({})
    update_layout()

func update_layout() -> void:
    if get_viewport() == null:
        return
    var width: float = get_viewport().get_visible_rect().size.x
    var available: float = minf(632.0, maxf(250.0, width - 36.0))
    custom_minimum_size.x = available
    var bar_width: float = maxf(70.0, (available - 70.0) / 3.0 - 9.0)
    for bar in [health_meter, shield_meter, mana_meter]:
        if bar != null:
            bar.custom_minimum_size.x = bar_width
    position = Vector2(18.0, 12.0)

func _make_text(value: String, color: Color) -> Label:
    var label := Label.new()
    label.text = value
    label.add_theme_color_override("font_color", color)
    label.mouse_filter = Control.MOUSE_FILTER_IGNORE
    return label

func _make_resource(parent: HBoxContainer, text: String, tint: Color) -> Dictionary:
    var column := VBoxContainer.new()
    column.size_flags_horizontal = Control.SIZE_EXPAND_FILL
    column.mouse_filter = Control.MOUSE_FILTER_IGNORE
    parent.add_child(column)
    var label := _make_text(text + " --", tint)
    column.add_child(label)
    var meter := ProgressBar.new()
    meter.custom_minimum_size = Vector2(175, 10)
    meter.size_flags_horizontal = Control.SIZE_EXPAND_FILL
    meter.show_percentage = false
    meter.mouse_filter = Control.MOUSE_FILTER_IGNORE
    meter.add_theme_color_override("font_color", tint)
    var fill := StyleBoxFlat.new()
    fill.bg_color = tint
    fill.set_corner_radius_all(3)
    meter.add_theme_stylebox_override("fill", fill)
    column.add_child(meter)
    return {"label": label, "meter": meter}

func render_saved_snapshot(loaded: Dictionary) -> void:
    if not bool(loaded.get("ok", false)):
        vaccine_label.text = "백신 확인 불가"
        mission_label.text = "저장 확인 필요"
        save_label.text = "저장 상태 미검증"
        return
    var state: Dictionary = loaded.get("snapshot", {})
    var revision: int = int(loaded.get("revision", -1))
    if bool(state.get("selection_required", false)):
        vaccine_label.text = "백신 최종 파기 · 새 백신 선택 필요"
        mission_label.text = "출전 불가"
        save_label.text = "검증된 저장 r%d · 새로운 백신 선택은 수동으로 진행" % revision
        return
    var vaccine: Dictionary = state.get("vaccine", {})
    if vaccine.is_empty():
        vaccine_label.text = "백신 정보 없음"
        mission_label.text = "진입 차단"
        save_label.text = "저장 구조 확인 필요"
        return
    vaccine_label.text = "백신 Lv.%d   복제 %d/3   성장 포인트 %d" % [
        int(vaccine.get("level", 1)), int(vaccine.get("copies_remaining", 0)),
        int(vaccine.get("unspent_fragments", 0))]
    var mission: Variant = vaccine.get("mission")
    if mission is Dictionary:
        mission_label.text = "의뢰 Lv.%d · XP %d" % [
            int(mission.get("mission_level", 1)), int(mission.get("mission_xp", 0))]
        save_label.text = "저장 r%d · 정화 %d/5 · 보상 대기 %d · 재추첨 %d" % [
            revision, mission.get("hardware_cleaned_ids", []).size(),
            int(mission.get("pending_plugin_offers", 0)),
            int(mission.get("rerolls_left", 0))]
    else:
        mission_label.text = "디펜더 허브"
        save_label.text = "저장 r%d · 백신 XP %d · 보관 %d · 소지 %d" % [
            revision, int(vaccine.get("xp", 0)),
            vaccine.get("archive", {}).size(), vaccine.get("carried", {}).size()]

func render_combat_snapshot(actor: Dictionary) -> void:
    var stats: Dictionary = actor.get("stats", {})
    if actor.is_empty() or stats.is_empty():
        _set_resource(health_label, health_meter, "체력", false, 0.0, 0.0)
        _set_resource(shield_label, shield_meter, "보호막", false, 0.0, 0.0)
        _set_resource(mana_label, mana_meter, "마나", false, 0.0, 0.0)
        return
    var hp_max: float = float(stats.get("max_health", 0.0))
    _set_resource(health_label, health_meter, "체력", hp_max > 0.0,
        float(stats.get("health", 0.0)), hp_max)
    # No independent shield ledger: until B exposes a public snapshot field,
    # show unknown instead of inventing zero or reading C's private buffers.
    var shield_known: bool = stats.has("shield") and hp_max > 0.0
    _set_resource(shield_label, shield_meter, "보호막", shield_known,
        float(stats.get("shield", 0.0)), hp_max)
    var mana_max: float = float(stats.get("max_mana", 0.0))
    _set_resource(mana_label, mana_meter, "마나",
        actor.has("mana") and mana_max > 0.0, float(actor.get("mana", 0.0)), mana_max)

func _set_resource(label: Label, meter: ProgressBar, caption: String,
        known: bool, amount: float, capacity: float) -> void:
    meter.visible = known
    if not known:
        label.text = caption + " --"
        return
    meter.min_value = 0.0
    meter.max_value = maxf(1.0, capacity)
    meter.value = clampf(amount, 0.0, meter.max_value)
    label.text = "%s %d/%d" % [caption, ceili(maxf(0.0, amount)), ceili(capacity)]

## Read-only projection: "deployed" means B installed it, NOT that it proc'd.
func render_plugin_view(view: Dictionary) -> void:
    if plugin_label == null:
        return
    if view.is_empty():
        plugin_label.text = "전투 플러그인 · B 전투 대기 (보관/소지는 허브 DB 확인)"
    elif not bool(view.get("ok", false)):
        plugin_label.text = "플러그인 확인 필요 · E/B 불일치"
    else:
        plugin_label.text = "전투 등록 %d종 · 동기화 대기 %d종 · 카탈로그 %d종" % [
            int(view.get("equipped", 0)), int(view.get("awaiting_sync", 0)),
            int(view.get("total_catalog", 0))]

func show_blocked() -> void:
    blocked = true
    mission_label.text = "안전 정지"
    save_label.text = "저장 상태 확인 필요 · 자동 재개 및 재지급 없음"
