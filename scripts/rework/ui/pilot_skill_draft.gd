extends PanelContainer
## A-only skill discovery overlay. The draft itself is derived from durable
## mission data (seed + cleansed region + already learned skills) and is never
## persisted; only the chosen skill ID goes through E learn_skill.
## GDD leaves the concrete skill pool [미확정], so labels say so plainly.
signal skill_chosen(skill_id: String)
signal draft_skipped

const Registry = preload("res://scripts/rework/skills/skill_registry.gd")
const CLOCK_HZ: float = 60.0
var heading: Label
var hint: Label
var card_buttons: Array[Button] = []
var skip_button: Button
var replacement_selector: OptionButton
var _equipped: Array = []

func _ready() -> void:
    name = "SkillDraft"
    process_mode = Node.PROCESS_MODE_ALWAYS
    visible = false
    custom_minimum_size = Vector2(660, 330)
    var chrome := StyleBoxFlat.new()
    chrome.bg_color = Color(0.026, 0.024, 0.062, 0.97)
    chrome.border_color = Color(0.62, 0.48, 1.0, 0.9)
    chrome.set_border_width_all(2)
    chrome.set_corner_radius_all(10)
    chrome.set_content_margin_all(14.0)
    add_theme_stylebox_override("panel", chrome)
    var column := VBoxContainer.new()
    column.add_theme_constant_override("separation", 8)
    add_child(column)
    heading = Label.new()
    heading.text = "스킬 초안 · 3개 중 하나 (BALANCE_DRAFT)"
    heading.add_theme_font_size_override("font_size", 18)
    heading.add_theme_color_override("font_color", Color(0.86, 0.8, 1.0))
    column.add_child(heading)
    hint = Label.new()
    hint.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
    hint.add_theme_color_override("font_color", Color(0.68, 0.7, 0.86))
    column.add_child(hint)
    var row := HBoxContainer.new()
    row.add_theme_constant_override("separation", 8)
    column.add_child(row)
    for index in range(3):
        var card := Button.new()
        card.name = "SkillCard" + str(index)
        card.custom_minimum_size = Vector2(190, 176)
        card.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
        card.size_flags_horizontal = Control.SIZE_EXPAND_FILL
        card.pressed.connect(_choose.bind(index))
        row.add_child(card)
        card_buttons.append(card)
    replacement_selector = OptionButton.new()
    replacement_selector.name = "ReplacementSlot"
    replacement_selector.visible = false
    column.add_child(replacement_selector)
    skip_button = Button.new()
    skip_button.text = "이번 구역 스킬 초안 건너뛰기"
    skip_button.pressed.connect(_skip)
    column.add_child(skip_button)
    update_layout()

func present(candidates: Array, equipped: Array) -> void:
    _equipped = equipped.duplicate()
    replacement_selector.clear()
    replacement_selector.add_item("교체할 장착 슬롯을 선택해")
    replacement_selector.set_item_disabled(0, true)
    for index in range(_equipped.size()):
        var known: Dictionary = Registry.definition(str(_equipped[index]))
        replacement_selector.add_item("%d번 · %s 교체" % [
            index + 1, str(known.get("name_ko", _equipped[index]))])
    replacement_selector.select(0)
    replacement_selector.visible = _equipped.size() >= 3
    for index in range(3):
        var button: Button = card_buttons[index]
        var candidate: Dictionary = candidates[index] if index < candidates.size() else {}
        if candidate.is_empty():
            button.text = "—"
            button.disabled = true
            continue
        button.set_meta("skill_id", str(candidate.get("id", "")))
        button.disabled = false
        button.text = _card_text(candidate)
    var slots_left: int = maxi(0, 3 - _equipped.size())
    if slots_left > 0:
        hint.text = "발견 즉시 습득하고 빈 슬롯 %d개 중 하나에 장착합니다. 실제 수치는 승인 전 초안입니다." % slots_left
    else:
        hint.text = "스킬이 3개 장착되어 있어. 교체할 슬롯을 직접 선택해야 습득할 수 있어. 수치는 초안이야."
    visible = true

func dismiss() -> void:
    visible = false

func update_layout() -> void:
    if get_viewport() == null:
        return
    var vp: Vector2 = get_viewport().get_visible_rect().size
    var width: float = minf(760.0, maxf(320.0, vp.x - 24.0))
    var height: float = minf(360.0, maxf(220.0, vp.y - 24.0))
    custom_minimum_size = Vector2(width, height)
    position = ((vp - custom_minimum_size) * 0.5).max(Vector2.ZERO)

func _card_text(definition: Dictionary) -> String:
    var name_ko: String = str(definition.get("name_ko", definition.get("name", "")))
    var description: String = str(definition.get("description_ko", ""))
    var cost: int = ceili(maxf(0.0, float(definition.get("mana_cost", 0.0))))
    var cooldown: float = maxf(0.0, float(definition.get("cooldown_ticks", 0.0))) / CLOCK_HZ
    return "%s\n\n%s\n\n마나 %d · 재사용 %.1f초" % [name_ko, description, cost, cooldown]

func selected_replacement_slot() -> int:
    if _equipped.size() < 3:
        return _equipped.size()
    return replacement_selector.selected - 1 if replacement_selector.selected > 0 else -1

func _choose(index: int) -> void:
    if index < 0 or index >= card_buttons.size():
        return
    var skill_id: String = str(card_buttons[index].get_meta("skill_id", ""))
    if skill_id.is_empty() or not Registry.definition(skill_id):
        return
    if _equipped.size() >= 3 and selected_replacement_slot() < 0:
        hint.text = "먼저 교체할 스킬 슬롯을 선택해. 아직 기존 스킬은 유지돼."
        return
    skill_chosen.emit(skill_id)

func _skip() -> void:
    draft_skipped.emit()
