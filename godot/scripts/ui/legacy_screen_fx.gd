extends Control
## Python view.py::_render_screen_fx and render() wave banner remade as
## native Godot 2D overlay above HUD, below menus and cursor.
## Does not consume input and never calls gameplay/save actions.
const CYAN := Color("#9fe8ff")
const GOLD := Color("#ffd166")
const RED := Color("#ff4d6d")
var reduced_effects: bool = false
var wave_title: Label
var wave_detail: Label
var banner_time: float = 0.0
var banner_total: float = 2.25
var sweep_time: float = 0.0
var sweep_total: float = 0.55
var flash_time: float = 0.0
var flash_total: float = 0.18
var sweep_color: Color = CYAN
var flash_color: Color = CYAN
var wave_color: Color = CYAN

func _ready() -> void:
    name = "LegacyScreenFX"
    mouse_filter = Control.MOUSE_FILTER_IGNORE
    set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
    wave_title = _make_label("WaveTitle", 32, CYAN)
    wave_detail = _make_label("WaveSubtitle", 14, Color("#92b9cc"))
    wave_title.visible = false
    wave_detail.visible = false
    set_process(true)

func _make_label(id: String, size_px: int, tint: Color) -> Label:
    var label := Label.new()
    label.name = id
    label.mouse_filter = Control.MOUSE_FILTER_IGNORE
    label.add_theme_font_size_override("font_size", size_px)
    label.add_theme_color_override("font_color", tint)
    label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
    label.clip_text = false
    label.set_anchors_and_offsets_preset(Control.PRESET_TOP_WIDE)
    add_child(label)
    return label

func set_reduced_effects(enabled: bool) -> void:
    reduced_effects = enabled
    if reduced_effects:
        sweep_time = 0.0
        flash_time = 0.0
    queue_redraw()

func announce_wave(sector_name: String, index: int, boss_wave: bool = false) -> void:
    banner_total = 2.25
    banner_time = banner_total
    wave_color = GOLD if boss_wave else CYAN
    wave_title.text = "BOSS WAVE %d" % index if boss_wave else "WAVE %d" % index
    wave_detail.text = "%s   ·   적을 모두 제거하세요" % sector_name
    wave_title.add_theme_color_override("font_color", wave_color)
    wave_title.visible = true
    wave_detail.visible = true
    trigger_sweep(wave_color, 0.55)

func announce_clear(sector_name: String) -> void:
    banner_total = 2.75
    banner_time = banner_total
    wave_color = Color("#68f2b5")
    wave_title.text = "SECTOR CLEAN"
    wave_detail.text = "%s   ·   왼쪽 EXIT에서 [F]로 복귀" % sector_name
    wave_title.add_theme_color_override("font_color", wave_color)
    wave_title.visible = true
    wave_detail.visible = true
    trigger_sweep(wave_color, 0.84)

func trigger_flash(tint: Color = CYAN, duration: float = 0.18) -> void:
    if reduced_effects:
        return
    flash_color = tint
    flash_total = maxf(0.02, duration)
    flash_time = flash_total

func trigger_sweep(tint: Color = CYAN, duration: float = 0.55) -> void:
    if reduced_effects:
        return
    sweep_color = tint
    sweep_total = maxf(0.02, duration)
    sweep_time = sweep_total

func clear_scene() -> void:
    banner_time = 0.0
    flash_time = 0.0
    sweep_time = 0.0
    if wave_title != null:
        wave_title.visible = false
        wave_detail.visible = false
    queue_redraw()

func _process(delta: float) -> void:
    var dt: float = maxf(0.0, delta)
    if flash_time > 0.0:
        flash_time = maxf(0.0, flash_time - dt)
    if sweep_time > 0.0:
        sweep_time = maxf(0.0, sweep_time - dt)
    if banner_time > 0.0:
        banner_time = maxf(0.0, banner_time - dt)
    var active: bool = banner_time > 0.0
    wave_title.visible = active
    wave_detail.visible = active
    if active:
        var appeared: float = 1.0 if reduced_effects else clampf(
            (1.0 - banner_time / maxf(0.01, banner_total)) / 0.22, 0.0, 1.0)
        var eased: float = 1.0 - pow(1.0 - appeared, 3.0)
        var height: float = size.y
        var title_y: float = height * 0.5 - 150.0 - (1.0 - eased) * 35.0
        wave_title.offset_top = title_y - 12.0
        wave_title.offset_bottom = title_y + 37.0
        wave_detail.offset_top = title_y + 38.0
        wave_detail.offset_bottom = title_y + 66.0
        var fade: float = minf(1.0, banner_time / 0.36)
        wave_title.modulate.a = fade
        wave_detail.modulate.a = fade
    queue_redraw()

func _draw() -> void:
    var width_px: float = size.x
    var height_px: float = size.y
    if width_px <= 0.0 or height_px <= 0.0:
        return
    if not reduced_effects and flash_time > 0.0:
        var flash := flash_color
        flash.a = 0.13 * minf(1.0, flash_time / maxf(0.01, flash_total))
        draw_rect(Rect2(Vector2.ZERO, size), flash)
    if not reduced_effects and sweep_time > 0.0:
        var completion: float = 1.0 - sweep_time / maxf(0.01, sweep_total)
        var y: float = -40.0 + completion * (height_px + 80.0)
        var band: Color = sweep_color
        band.a = 0.075
        draw_rect(Rect2(0.0, y - 20.0, width_px, 40.0), band)
        band.a = 0.53
        draw_line(Vector2(0.0, y), Vector2(width_px, y), band, 2.0)
    if banner_time > 0.0:
        var appeared: float = 1.0 if reduced_effects else clampf(
            (1.0 - banner_time / maxf(0.01, banner_total)) / 0.22, 0.0, 1.0)
        var eased: float = 1.0 - pow(1.0 - appeared, 3.0)
        var y: float = height_px * 0.5 - 150.0 - (1.0 - eased) * 35.0
        var color: Color = wave_color
        color.a = minf(1.0, banner_time / 0.36) * 0.84
        draw_line(Vector2(width_px / 2.0 - 140.0 * eased, y + 23.0),
            Vector2(width_px / 2.0 + 140.0 * eased, y + 23.0), color, 1.4)
