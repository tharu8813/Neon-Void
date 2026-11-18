extends PanelContainer
## A-only real B skill readout. Slot order/MP/cooldown comes exclusively
## from B public actor_snapshot().skill_state, never from E drafts.
const Registry = preload("res://scripts/rework/skills/skill_registry.gd")
const INPUT_LABELS := ["Q", "E", "C"]
const CLOCK_HZ: float = 60.0
var slots: Array[Label] = []
var _last_projection: String = ""

func _ready() -> void:
    name = "ActiveSkillBar"
    process_mode = Node.PROCESS_MODE_ALWAYS
    mouse_filter = Control.MOUSE_FILTER_IGNORE
    visible = false
    var chrome := StyleBoxFlat.new()
    chrome.bg_color = Color(0.018, 0.035, 0.058, 0.91)
    chrome.border_color = Color(0.32, 0.68, 0.85, 0.75)
    chrome.set_border_width_all(1)
    chrome.set_corner_radius_all(8)
    chrome.set_content_margin_all(7.0)
    add_theme_stylebox_override("panel", chrome)
    var row := HBoxContainer.new()
    row.add_theme_constant_override("separation", 9)
    row.mouse_filter = Control.MOUSE_FILTER_IGNORE
    add_child(row)
    for index in range(3):
        var label := Label.new()
        label.name = "Slot" + str(index + 1)
        label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
        label.clip_text = true
        label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
        label.add_theme_font_size_override("font_size", 12)
        label.add_theme_color_override("font_color", Color(0.81, 0.93, 0.99))
        label.mouse_filter = Control.MOUSE_FILTER_IGNORE
        row.add_child(label)
        slots.append(label)
    update_layout()

func update_layout() -> void:
    if get_viewport() == null:
        return
    var vp: Vector2 = get_viewport().get_visible_rect().size
    var desired: float = minf(530.0, maxf(240.0, vp.x - 24.0))
    custom_minimum_size = Vector2(desired, 52.0)
    position = Vector2(maxf(0.0, (vp.x - desired) * 0.5),
        maxf(0.0, vp.y - 64.0))

func render_actor(actor: Dictionary) -> void:
    var state: Variant = actor.get("skill_state", null)
    if not (state is Dictionary):
        clear_view()
        return
    var entries: Variant = state.get("slots", [])
    if not (entries is Array) or entries.size() != 3:
        clear_view()
        return
    var projection: String = JSON.stringify({
        "slots": entries, "mana": state.get("mana", null)})
    if projection == _last_projection:
        return
    _last_projection = projection
    visible = true
    for index in range(3):
        var entry: Variant = entries[index]
        if not (entry is Dictionary):
            clear_view()
            return
        var uid: String = str(entry.get("skill_id", ""))
        var caption: String = "%s · 빈 슬롯" % INPUT_LABELS[index]
        var remaining: int = maxi(0, int(entry.get("remaining_cooldown_ticks", 0)))
        if not uid.is_empty():
            var definition: Dictionary = Registry.definition(uid)
            var skill_name: String = str(definition.get("name", uid))
            var cost: int = ceili(maxf(0.0, float(entry.get("mana_cost", 0.0))))
            caption = "%s · %s\n" % [INPUT_LABELS[index], skill_name]
            if remaining > 0:
                caption += "%.1f초 대기" % (float(remaining) / CLOCK_HZ)
            elif float(state.get("mana", 0.0)) < float(entry.get("mana_cost", 0.0)):
                caption += "마나 부족 (%d)" % cost
            else:
                caption += "사용 가능 · %d MP" % cost
        slots[index].text = caption
        slots[index].add_theme_color_override("font_color",
            Color(0.94, 0.74, 0.5) if remaining > 0 else
            Color(0.58, 0.95, 0.8) if not uid.is_empty() else
            Color(0.58, 0.71, 0.8))

func clear_view() -> void:
    _last_projection = ""
    visible = false
