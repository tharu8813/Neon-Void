extends Control
## P7 pixel-aligned restoration of Python neonvoid/view.py::_build_hud.
## Read-only presentation; B/C own combat, D owns software, E owns persistence.
## Logical layout is 1280x720. Anchors keep left/right/centre/bottom positions
## while Godot's viewport stretch maps this coordinate system to window pixels.
const Software = preload("res://scripts/software/software_system.gd")
const PLAYER_COLOR := Color("#6ff2ff")
const HP_COLOR := Color("#3ff5a0")
const HP_LOW := Color("#ff4d6d")
const GOLD := Color("#ffd166")
const MANA_COLOR := Color("#528dff")
const BOSS_COLOR := Color("#ff5cf0")
const TEXT_COLOR := Color("#d5ebff")
const DIM_COLOR := Color("#5b7898")
const PANEL_COLOR := Color("#070f1e")
const BASE_PANEL := Color("#061322")
const INFECTION_PANEL := Color("#0b1526")

var _data: Dictionary = {}
var _combat_hud: Control
var _world_hud: Control
var _hp: ProgressBar
var _hp_chip: ProgressBar
var _mana: ProgressBar
var _hp_text: Label
var _mana_text: Label
var _stat_text: Label
var _stat_right: Label
var _stat_effect: Label
var _location_text: Label
var _world_detail: Label
var _world_hint: Label
var _world_prompt: Label
var _security_text: Label
var _infection_hint: Label
var _infection_detail: Label
var _infection_bar: ProgressBar
var _wave_text: Label
var _score_text: Label
var _combo_text: Label
var _audio_status: Label
var _danger_sides: Array[ColorRect] = []
var _hit_flash: ColorRect
var _hit_flash_t: float = 0.0
var _last_hp: float = -1.0
var _software_text: Label
var _fps_text: Label
var _pulse_text: Label
var _guard_text: Label
var _pulse_bar: ProgressBar
var _guard_bar: ProgressBar
var _dash_pips: Array[ColorRect] = []
var _boss_panel: Control
var _boss_bar: ProgressBar
var _boss_text: Label
var _boss_warning: Label
var _hp_trailing: float = 1.0
var _last_mode: String = ""

func _ready() -> void:
    mouse_filter = Control.MOUSE_FILTER_IGNORE
    set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
    _combat_hud = _layer("CombatHUD")
    _world_hud = _layer("WorldHUD")
    _build_combat()
    _build_world()
    _audio_status = _label(self, "AudioStatus", Rect2(-152, -37, 128, 24),
        "MUTED · M", 11, DIM_COLOR, 1.0, 1.0)
    _audio_status.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
    _draw_state()

func _layer(id: String) -> Control:
    var layer := Control.new()
    layer.name = id
    layer.mouse_filter = Control.MOUSE_FILTER_IGNORE
    add_child(layer)
    layer.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
    return layer

func _place(item: Control, rect: Rect2, x_anchor: float = 0.0, y_anchor: float = 0.0) -> void:
    item.anchor_left = x_anchor
    item.anchor_right = x_anchor
    item.anchor_top = y_anchor
    item.anchor_bottom = y_anchor
    item.offset_left = rect.position.x
    item.offset_top = rect.position.y
    item.offset_right = rect.position.x + rect.size.x
    item.offset_bottom = rect.position.y + rect.size.y

func _style(bg: Color, edge: Color = Color.TRANSPARENT) -> StyleBoxFlat:
    var panel := StyleBoxFlat.new()
    panel.bg_color = bg
    panel.border_color = edge
    panel.set_border_width_all(1 if edge.a > 0.0 else 0)
    return panel

func _panel(parent: Control, id: String, rect: Rect2,
        color: Color, border: Color, ax: float = 0.0, ay: float = 0.0) -> Panel:
    var panel := Panel.new()
    panel.name = id
    panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
    panel.add_theme_stylebox_override("panel", _style(color, border))
    parent.add_child(panel)
    _place(panel, rect, ax, ay)
    return panel

func _label(parent: Control, id: String, rect: Rect2, value: String,
        size_px: int, color: Color, ax: float = 0.0, ay: float = 0.0) -> Label:
    var label := Label.new()
    label.name = id
    label.text = value
    label.mouse_filter = Control.MOUSE_FILTER_IGNORE
    label.add_theme_font_size_override("font_size", size_px)
    label.add_theme_color_override("font_color", color)
    label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
    label.autowrap_mode = TextServer.AUTOWRAP_OFF
    label.clip_text = true
    parent.add_child(label)
    _place(label, rect, ax, ay)
    return label

func _bar(parent: Control, id: String, rect: Rect2,
        color: Color, ax: float = 0.0, ay: float = 0.0) -> ProgressBar:
    var bar := ProgressBar.new()
    bar.name = id
    bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
    bar.show_percentage = false
    bar.custom_minimum_size = Vector2.ZERO
    bar.add_theme_stylebox_override("background", _style(Color("#0d1626")))
    bar.add_theme_stylebox_override("fill", _style(color))
    parent.add_child(bar)
    _place(bar, rect, ax, ay)
    return bar

func _build_danger_overlay() -> void:
    # Original Tk low-HP vignette/flash as strictly decorative UI surfaces.
    _hit_flash = ColorRect.new()
    _hit_flash.name = "ScreenHitFlash"
    _hit_flash.color = Color(1.0, 0.22, 0.32, 0.0)
    _hit_flash.mouse_filter = Control.MOUSE_FILTER_IGNORE
    _combat_hud.add_child(_hit_flash)
    _hit_flash.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
    _hit_flash.visible = false
    for edge in ["top", "bottom", "left", "right"]:
        var band := ColorRect.new()
        band.name = "Danger_" + edge
        band.mouse_filter = Control.MOUSE_FILTER_IGNORE
        band.color = Color(0.7, 0.06, 0.16, 0.0)
        _combat_hud.add_child(band)
        if edge == "top":
            band.anchor_right = 1.0
            band.offset_bottom = 34.0
        elif edge == "bottom":
            band.anchor_left = 0.0
            band.anchor_right = 1.0
            band.anchor_top = 1.0
            band.anchor_bottom = 1.0
            band.offset_top = -34.0
        elif edge == "left":
            band.anchor_bottom = 1.0
            band.offset_right = 34.0
        else:
            band.anchor_left = 1.0
            band.anchor_right = 1.0
            band.anchor_bottom = 1.0
            band.offset_left = -34.0
        _danger_sides.append(band)

func _process(delta: float) -> void:
    if _hit_flash_t <= 0.0:
        return
    _hit_flash_t = maxf(0.0, _hit_flash_t - delta)
    _hit_flash.visible = _hit_flash_t > 0.0 and _combat_hud.visible
    _hit_flash.color.a = 0.16 * (_hit_flash_t / 0.22)

func _build_combat() -> void:
    _build_danger_overlay()
    # Frozen Tk Canvas coordinates, including the exact HP/MANA gutters.
    _panel(_combat_hud, "HPFrame", Rect2(24, 22, 240, 12),
        Color("#0d1626"), Color("#1d2c46"))
    _hp_chip = _bar(_combat_hud, "HPChip", Rect2(26, 24, 236, 8), Color("#7a2038"))
    _hp = _bar(_combat_hud, "HP", Rect2(26, 24, 236, 8), HP_COLOR)
    _hp_text = _label(_combat_hud, "HPText", Rect2(24, 17, 240, 22),
        "100 / 100", 11, Color("#f6ffff"))
    _hp_text.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    _panel(_combat_hud, "ManaFrame", Rect2(24, 60, 240, 14),
        Color("#0b1528"), Color("#274e79"))
    _mana = _bar(_combat_hud, "Mana", Rect2(26, 62, 236, 10), MANA_COLOR)
    _mana_text = _label(_combat_hud, "ManaText", Rect2(24, 57, 240, 20),
        "MANA 100 / 100", 11, Color("#eff5ff"))
    _mana_text.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER

    for index in range(4):
        _panel(_combat_hud, "DashSlot%d" % index, Rect2(24 + index * 30, 44, 24, 6),
            Color("#0b1a26"), Color("#16303e"))
        var pip := ColorRect.new()
        pip.name = "DashFill%d" % index
        pip.mouse_filter = Control.MOUSE_FILTER_IGNORE
        pip.color = PLAYER_COLOR
        _combat_hud.add_child(pip)
        _place(pip, Rect2(24 + index * 30, 44, 24, 6))
        _dash_pips.append(pip)

    _panel(_combat_hud, "LiveStatsPanel", Rect2(19, 90, 354, 172),
        PANEL_COLOR, Color("#21394e"))
    _label(_combat_hud, "LiveStatsHeading", Rect2(34, 94, 330, 22),
        "LIVE STATS  /  실시간 능력치", 13, PLAYER_COLOR)
    _stat_text = _label(_combat_hud, "StatsLeft", Rect2(34, 120, 155, 111),
        "", 12, TEXT_COLOR)
    _stat_text.vertical_alignment = VERTICAL_ALIGNMENT_TOP
    _stat_text.autowrap_mode = TextServer.AUTOWRAP_OFF
    _stat_right = _label(_combat_hud, "StatsRight", Rect2(194, 120, 168, 111),
        "", 12, Color("#a4bfd7"))
    _stat_right.vertical_alignment = VERTICAL_ALIGNMENT_TOP
    _stat_right.autowrap_mode = TextServer.AUTOWRAP_OFF
    _panel(_combat_hud, "StatsRule", Rect2(30, 228, 332, 1),
        Color("#16233a"), Color.TRANSPARENT)
    _stat_effect = _label(_combat_hud, "StatsEffects", Rect2(34, 233, 329, 23),
        "", 11, GOLD)

    _panel(_combat_hud, "SkillPanel", Rect2(-275, -86, 550, 62),
        Color("#07121e"), Color("#28455b"), 0.5, 1.0)
    _pulse_text = _label(_combat_hud, "PulseText", Rect2(-257, -77, 240, 31),
        "[Q] 충격파", 13, PLAYER_COLOR, 0.5, 1.0)
    _guard_text = _label(_combat_hud, "GuardText", Rect2(17, -77, 240, 31),
        "[E] 보호막", 13, HP_COLOR, 0.5, 1.0)
    _guard_text.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
    _pulse_bar = _bar(_combat_hud, "PulseCooldown", Rect2(-224, -42, 184, 4),
        PLAYER_COLOR, 0.5, 1.0)
    _guard_bar = _bar(_combat_hud, "GuardCooldown", Rect2(40, -42, 184, 4),
        HP_COLOR, 0.5, 1.0)

    _panel(_combat_hud, "InfectionPanel", Rect2(-420, 107, 396, 83),
        INFECTION_PANEL, Color("#384457"), 1.0, 0.0)
    _security_text = _label(_combat_hud, "InfectionText", Rect2(-411, 112, 380, 20),
        "INFECTION 0% · OS 100%", 13, Color("#e8c997"), 1.0, 0.0)
    _infection_bar = _bar(_combat_hud, "InfectionLevel", Rect2(-416, 134, 387, 10),
        Color("#e8ae50"), 1.0, 0.0)
    _infection_hint = _label(_combat_hud, "InfectionHint", Rect2(-411, 149, 380, 19),
        "", 12, TEXT_COLOR, 1.0, 0.0)
    _infection_detail = _label(_combat_hud, "InfectionDetail", Rect2(-411, 169, 380, 18),
        "", 11, Color("#92b9c4"), 1.0, 0.0)

    _wave_text = _label(_combat_hud, "WaveText", Rect2(-260, 8, 236, 40),
        "WAVE 1", 24, TEXT_COLOR, 1.0, 0.0)
    _wave_text.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
    _score_text = _label(_combat_hud, "ScoreText", Rect2(-250, 44, 226, 26),
        "0", 14, GOLD, 1.0, 0.0)
    _score_text.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
    _combo_text = _label(_combat_hud, "ComboText", Rect2(-220, 8, 440, 36),
        "", 20, GOLD, 0.5, 0.0)
    _combo_text.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    _combo_text.visible = false

    _boss_panel = Control.new()
    _boss_panel.name = "BossHUD"
    _boss_panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
    _combat_hud.add_child(_boss_panel)
    _place(_boss_panel, Rect2(-300, 52, 600, 80), 0.5, 0.0)
    _boss_text = _label(_boss_panel, "BossName", Rect2(0, 0, 600, 28),
        "OVERSEER", 16, BOSS_COLOR)
    _boss_text.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    _panel(_boss_panel, "BossBarBackground", Rect2(0, 32, 600, 12),
        Color("#1a0a1e"), Color("#3a1a42"))
    _boss_bar = _bar(_boss_panel, "BossHealth", Rect2(2, 34, 596, 8), BOSS_COLOR)
    _boss_warning = _label(_boss_panel, "BossWarning", Rect2(0, 50, 600, 23),
        "", 12, GOLD)
    _boss_warning.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    _boss_panel.visible = false

    _software_text = _label(_combat_hud, "InstalledSoftware", Rect2(24, -45, 500, 25),
        "", 11, DIM_COLOR, 0.0, 1.0)
    _fps_text = _label(_combat_hud, "FPS", Rect2(-143, -44, 119, 24),
        "", 11, DIM_COLOR, 1.0, 1.0)
    _fps_text.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT

func _build_world() -> void:
    _panel(_world_hud, "WorldInfoPanel", Rect2(12, 12, 481, 63),
        BASE_PANEL, Color("#284c5c"))
    _location_text = _label(_world_hud, "WorldName", Rect2(28, 17, 450, 27),
        "ANTIVIRUS CORE", 20, PLAYER_COLOR)
    _world_detail = _label(_world_hud, "WorldDescription", Rect2(28, 44, 450, 23),
        "백신 시스템 대기", 13, Color("#98b8ca"))
    _world_hint = _label(_world_hud, "WorldHelp", Rect2(-450, 13, 433, 30),
        "이동 후 [F] 상호작용", 13, Color("#6c9cb0"), 1.0, 0.0)
    _world_hint.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
    _panel(_world_hud, "NearbyPanel", Rect2(-270, -95, 540, 46),
        Color("#0a2736"), Color("#397b93"), 0.5, 1.0)
    _world_prompt = _label(_world_hud, "NearbyPrompt", Rect2(-255, -91, 510, 38),
        "", 14, Color("#c6f8ff"), 0.5, 1.0)
    _world_prompt.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER

func update_state(payload: Dictionary) -> void:
    if payload == _data:
        return
    _data = payload.duplicate(true)
    if is_node_ready():
        _draw_state()

func _draw_state() -> void:
    var mode: String = str(_data.get("status", "base"))
    var world: bool = mode in ["base", "hub"]
    var combat: bool = mode in ["combat", "between", "clean"]
    _world_hud.visible = world
    _combat_hud.visible = combat
    _last_mode = mode
    var audio_opts: Dictionary = _data.get("settings", {})
    _audio_status.visible = bool(audio_opts.get("muted", false))
    if world:
        var is_base: bool = mode == "base"
        _location_text.text = "ANTIVIRUS CORE" if is_base else "OPERATING SYSTEM"
        _world_detail.text = ("백신 시스템 · 운영체제 배포 대기" if is_base else
            "OS 무결성 %.0f%% · 감염 %.0f%% · 정화 %d회" % [
                float(_data.get("integrity", 100.0)),
                float(_data.get("infection", 0.0)), int(_data.get("purify_uses", 3))])
        var prompt: String = str(_data.get("nearby", ""))
        _world_prompt.text = "[F] " + prompt if not prompt.is_empty() else ""
        _world_prompt.get_parent().get_node("NearbyPanel").visible = not prompt.is_empty()
        return
    if not combat:
        return

    var hp: float = float(_data.get("hp", 100.0))
    var maxhp: float = maxf(1.0, float(_data.get("maxhp", 100.0)))
    var hp_ratio: float = clampf(hp / maxhp, 0.0, 1.0)
    var reduced: bool = bool(_data.get("settings", {}).get("reduced_effects", false))
    if _last_hp >= 0.0 and hp < _last_hp - 0.5 and not reduced:
        _hit_flash_t = 0.22
        _hit_flash.visible = true
    _last_hp = hp
    var danger: float = clampf((0.35 - hp_ratio) / 0.35, 0.0, 1.0)
    for band in _danger_sides:
        band.visible = danger > 0.0 and not reduced
        band.color.a = minf(0.36, 0.32 * danger)
    if reduced:
        _hit_flash_t = 0.0
        _hit_flash.visible = false
    if hp_ratio < _hp_trailing:
        _hp_trailing = lerpf(_hp_trailing, hp_ratio, 0.11)
    else:
        _hp_trailing = hp_ratio
    _hp_chip.max_value = 1.0
    _hp_chip.value = _hp_trailing
    _hp.max_value = 1.0
    _hp.value = hp_ratio
    var hp_color: Color = HP_COLOR if hp_ratio > 0.5 else (GOLD if hp_ratio > 0.25 else HP_LOW)
    _hp.add_theme_stylebox_override("fill", _style(hp_color))
    _hp_text.text = "%d / %d" % [int(hp), int(maxhp)]
    var mana: float = float(_data.get("mana", 100.0))
    var maxmana: float = maxf(1.0, float(_data.get("maxmana", 100.0)))
    _mana.max_value = maxmana
    _mana.value = clampf(mana, 0.0, maxmana)
    _mana_text.text = "MANA  %.0f / %.0f" % [mana, maxmana]

    var dash_max: int = mini(4, maxi(1, int(_data.get("dash_max", 1))))
    var charges: int = int(_data.get("dash_charges", 1))
    for index in range(_dash_pips.size()):
        var pip: ColorRect = _dash_pips[index]
        var slot: Panel = _combat_hud.get_node("DashSlot%d" % index)
        slot.visible = index < dash_max
        pip.visible = index < dash_max and (index < charges or
            index == charges and float(_data.get("charge_t", 0.0)) > 0.0)
        if not pip.visible:
            continue
        var fill: float = 1.0 if index < charges else clampf(
            float(_data.get("charge_t", 0.0)) / maxf(0.01, float(_data.get("dash_cd", 1.05))), 0.0, 1.0)
        pip.offset_right = pip.offset_left + 24.0 * fill
        pip.color = PLAYER_COLOR if index < charges else Color("#2b7f8f")

    var stats: Dictionary = _data.get("stats", {})
    _stat_text.text = ("공격력   %6.1f\n연사 속도 %5.1f/s\n치명타    %4.0f%%\n" +
        "방어력   %6.1f\n방어 관통 %6.1f\n이동 속도 %6.0f") % [
        float(stats.get("dmg", 10.0)), float(stats.get("rate", 5.4)),
        float(stats.get("crit", 0.05)) * 100.0,
        float(stats.get("armor", 4.0)) + (12.0 if float(_data.get("guard_t", 0.0)) > 0.0 else 0.0),
        float(stats.get("armor_pen", 0.0)), float(stats.get("speed", 322.0))]
    _stat_right.text = ("마나 회복 %4.1f/s\n체력 재생 %4.1f/s\n적중 회복 %4.1f\n" +
        "화상 확률 %4.0f%%\n중독 확률 %4.0f%%\n스킬 위력 %4.0f%%") % [
        float(stats.get("mana_regen", 6.0)),
        float(stats.get("hp_regen", 0.0)) + (1.8 if float(_data.get("guard_t", 0.0)) > 0.0 else 0.0),
        float(stats.get("life_on_hit", 0.0)),
        float(stats.get("burn_chance", 0.0)) * 100.0,
        float(stats.get("toxin_chance", 0.0)) * 100.0,
        float(stats.get("skill_power", 1.0)) * 100.0]
    var guard_t: float = float(_data.get("guard_t", 0.0))
    _stat_effect.text = ("%s  ·  지속 %.0f%%" % [
        "방어막 %.1fs" % guard_t if guard_t > 0.0 else "상시 효과 활성",
        float(stats.get("effect_duration", 1.0)) * 100.0])

    var settings: Dictionary = _data.get("settings", {})
    var binds: Dictionary = settings.get("bindings", {})
    var q_key: String = str(binds.get("skill_pulse", "q")).to_upper()
    var e_key: String = str(binds.get("skill_guard", "e")).to_upper()
    var cost_multiplier: float = float(stats.get("skill_efficiency", 1.0))
    var pulse_cost: float = Software.skill_mana_cost(35.0, cost_multiplier)
    var guard_cost: float = Software.skill_mana_cost(25.0, cost_multiplier)
    var pulse_remaining: float = float(_data.get("pulse_cd", 0.0))
    var guard_remaining: float = float(_data.get("guard_cd", 0.0))
    var pulse_ready: bool = pulse_remaining <= 0.0 and mana >= pulse_cost
    var guard_ready: bool = guard_remaining <= 0.0 and mana >= guard_cost
    _pulse_text.text = "[%s] 충격파 %.0f MP · %s" % [
        q_key, pulse_cost, ("%.1fs" % pulse_remaining if pulse_remaining > 0.0 else
            ("준비" if pulse_ready else "마나 부족"))]
    _guard_text.text = "[%s] 보호막 %.0f MP · %s" % [
        e_key, guard_cost, ("%.1fs" % guard_remaining if guard_remaining > 0.0 else
            ("준비" if guard_ready else "마나 부족"))]
    _pulse_text.add_theme_color_override("font_color", PLAYER_COLOR if pulse_ready else DIM_COLOR)
    _guard_text.add_theme_color_override("font_color", HP_COLOR if guard_ready else DIM_COLOR)
    _pulse_bar.max_value = Software.skill_cooldown(7.0, float(stats.get("skill_cooldown", 1.0)))
    _pulse_bar.value = pulse_remaining if pulse_remaining > 0.0 else _pulse_bar.max_value
    _guard_bar.max_value = Software.skill_cooldown(12.0, float(stats.get("skill_cooldown", 1.0)))
    _guard_bar.value = guard_remaining if guard_remaining > 0.0 else _guard_bar.max_value
    var module_levels: Dictionary = _data.get("software_levels", {})
    _software_text.text = "설치된 소프트웨어 %d종  [TAB]  ·  대시 %d/%d" % [
        module_levels.size(), charges, dash_max]

    var wave: int = int(_data.get("wave", 0))
    _wave_text.text = "WAVE %d" % wave if wave > 0 else "WAVE CLEAR"
    _score_text.text = "SCORE %d" % int(_data.get("score", 0))
    var combo: int = int(_data.get("combo", 0))
    _combo_text.visible = combo > 1 and float(_data.get("combo_window", 0.0)) > 0.0
    _combo_text.text = "%d COMBO" % combo if _combo_text.visible else ""
    _fps_text.text = "FPS %.0f" % float(_data.get("fps", 0.0)) if bool(_data.get("show_fps", false)) else ""

    var infected: float = clampf(float(_data.get("infection", 0.0)), 0.0, 100.0)
    var integrity: float = clampf(float(_data.get("integrity", 100.0)), 0.0, 100.0)
    _infection_bar.max_value = 100.0
    _infection_bar.value = infected
    var alarm: bool = infected >= 75.0 or integrity <= 25.0
    var pressure: bool = infected >= 50.0
    var alarm_color: Color = HP_LOW if alarm else (GOLD if pressure else Color("#e8c997"))
    _security_text.text = "%s · 감염 %.0f%% · OS %.0f%%" % [
        "위험" if alarm else ("주의" if pressure else "안정"), infected, integrity]
    _security_text.add_theme_color_override("font_color", alarm_color)
    _infection_bar.add_theme_stylebox_override("fill", _style(alarm_color))
    _infection_hint.text = ("위험 · 구역 출구에서 [F]로 복귀" if alarm else
        ("무결성 감소 중 · 허브 정화 권장" if pressure else
        "감염 50%부터 무결성 감소"))
    _infection_detail.text = "OS 무결성 %.0f%%  ·  정화 충전 %d회" % [
        integrity, int(_data.get("purify_uses", 0))]

    var boss_hp: float = float(_data.get("boss_hp", 0.0))
    var boss_max: float = float(_data.get("boss_maxhp", 0.0))
    _boss_panel.visible = boss_max > 0.0 and boss_hp > 0.0
    if _boss_panel.visible:
        _boss_bar.max_value = maxf(1.0, boss_max)
        _boss_bar.value = clampf(boss_hp, 0.0, boss_max)
        _boss_text.text = "%s  %.0f / %.0f" % [
            str(_data.get("boss_name", "BOSS")).to_upper(), boss_hp, boss_max]
        _boss_warning.text = str(_data.get("boss_warning", ""))
        _boss_warning.visible = not _boss_warning.text.is_empty()
