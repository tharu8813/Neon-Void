extends PanelContainer
## A-owned hub management UX. All displayed state comes from E snapshots.
## UI never changes E saves by itself: it emits requested commands + E revision.
## A alone sends approved E transactions; no optimistic inventory/point awards.
signal close_requested()
signal allocate_requested(allocations: Dictionary, revision: int)
signal plugin_move_requested(action: String, plugin_id: String, revision: int)
signal equip_requested(equipped: Array, revision: int)

const Balance = preload("res://scripts/rework/progression/balance_config.gd")
const Catalog = preload("res://scripts/rework/plugins/plugin_catalog.gd")
const STAT_NAMES := {
    "max_hp": "최대 체력", "attack_damage": "공격력 (AD)",
    "ability_power": "주문력 (AP)", "armor": "물리 방어력",
    "magic_resist": "마법 저항력", "move_speed": "이동속도",
    "fire_rate": "발사속도", "projectile_speed": "탄환속도",
    "crit_chance": "치명타 확률", "crit_damage": "치명타 피해",
    "ability_haste": "스킬 가속", "luck": "행운",
    "max_mana": "최대 마나", "mana_regen": "초당 마나 재생",
    "projectile_size": "탄환 크기", "flat_armor_pen": "고정 물리 관통",
    "flat_magic_pen": "고정 마법 관통",
    "percent_armor_pen": "비율 물리 관통",
    "percent_magic_pen": "비율 마법 관통",
    "on_hit_vamp": "적중 흡혈", "omnivamp": "모든 피해 흡혈",
    "tenacity": "강인함"
}
const TEXT := Color(0.79, 0.95, 1.0)
const MUTED := Color(0.56, 0.69, 0.78)
const NEON := Color(0.18, 0.84, 0.94)

var source: Dictionary = {}
var vaccine: Dictionary = {}
var revision: int = -1
var can_edit: bool = false
var allocation_draft: Dictionary = {}
var selected_skills: Array[String] = ["", "", ""]
var catalog: Dictionary = {}
var tabs: TabContainer
var header: Label
var hint: Label
var stat_body: VBoxContainer
var database_body: VBoxContainer
var skills_body: VBoxContainer
var combat_body: VBoxContainer
var overview_body: VBoxContainer
var stat_summary: Label
var stat_submit: Button
var skill_submit: Button
var _built: bool = false
var _last_combat_projection: String = ""
var combat_render_count: int = 0

func _ready() -> void:
    name = "HubManagement"
    process_mode = Node.PROCESS_MODE_ALWAYS
    mouse_filter = Control.MOUSE_FILTER_STOP
    visible = false
    for entry in Catalog.entries():
        catalog[str(entry.get("plugin_id", ""))] = entry
    var background := StyleBoxFlat.new()
    background.bg_color = Color(0.018, 0.035, 0.063, 0.97)
    background.border_color = NEON
    background.set_border_width_all(2)
    background.set_corner_radius_all(12)
    background.set_content_margin_all(13.0)
    add_theme_stylebox_override("panel", background)

    var layout := VBoxContainer.new()
    layout.add_theme_constant_override("separation", 9)
    add_child(layout)
    var top := HBoxContainer.new()
    layout.add_child(top)
    header = _label("백신 관리 / DEFENDER DATABASE", 20, TEXT)
    # Root chrome must NOT request thousands of pixels of min-height while
    # the Container has zero width during its first minimum-size pass.
    header.autowrap_mode = TextServer.AUTOWRAP_OFF
    header.clip_text = true
    header.size_flags_horizontal = Control.SIZE_EXPAND_FILL
    top.add_child(header)
    var exit_btn := _button("닫기  [Tab / Esc]")
    exit_btn.pressed.connect(func() -> void: close_requested.emit())
    top.add_child(exit_btn)
    hint = _label("E 저장 정보를 확인하는 중", 13, MUTED)
    hint.autowrap_mode = TextServer.AUTOWRAP_OFF
    hint.clip_text = true
    layout.add_child(hint)

    tabs = TabContainer.new()
    tabs.size_flags_horizontal = Control.SIZE_EXPAND_FILL
    tabs.size_flags_vertical = Control.SIZE_EXPAND_FILL
    layout.add_child(tabs)
    overview_body = _page("백신 상태")
    stat_body = _page("능력치")
    database_body = _page("플러그인 DB")
    skills_body = _page("스킬 장착")
    combat_body = _page("전투 상세")
    var footer := _label(
        "허브에서만 편집 · E 저장 확인 후 반영 · 의뢰 중 읽기 전용", 12, MUTED)
    footer.autowrap_mode = TextServer.AUTOWRAP_OFF
    footer.clip_text = true
    layout.add_child(footer)
    _built = true
    _layout_to_viewport()

func _notification(what: int) -> void:
    if what == NOTIFICATION_TRANSFORM_CHANGED and _built:
        _layout_to_viewport()

func _layout_to_viewport() -> void:
    if get_viewport() == null:
        return
    var viewport_size: Vector2 = get_viewport().get_visible_rect().size
    size = Vector2(minf(960.0, maxf(280.0, viewport_size.x - 36.0)),
        minf(610.0, maxf(180.0, viewport_size.y - 36.0)))
    position = ((viewport_size - size) * 0.5).max(Vector2.ZERO)

func _page(title: String) -> VBoxContainer:
    var scroll := ScrollContainer.new()
    scroll.name = title
    scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
    scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
    scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
    tabs.add_child(scroll)
    var inner := VBoxContainer.new()
    inner.size_flags_horizontal = Control.SIZE_EXPAND_FILL
    inner.add_theme_constant_override("separation", 7)
    scroll.add_child(inner)
    return inner

func _label(value: String, size_pt: int = 14, tint: Color = TEXT) -> Label:
    var result := Label.new()
    result.text = value
    result.add_theme_font_size_override("font_size", size_pt)
    result.add_theme_color_override("font_color", tint)
    result.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
    return result

func _button(value: String) -> Button:
    var result := Button.new()
    result.text = value
    result.add_theme_color_override("font_color", TEXT)
    result.add_theme_color_override("font_hover_color", Color.WHITE)
    result.add_theme_color_override("font_disabled_color", MUTED)
    result.custom_minimum_size.y = 34
    return result

func _clear(target: Node) -> void:
    for node in target.get_children():
        target.remove_child(node)
        node.queue_free()

func show_saved(loaded: Dictionary, allow_actions: bool) -> void:
    if not _built:
        return
    if not bool(loaded.get("ok", false)):
        can_edit = false
        header.text = "백신 상태 확인 실패"
        hint.text = "E가 저장 데이터를 검증하지 못했어. 편집을 차단했어."
        for panel in [overview_body, stat_body, database_body, skills_body]:
            _clear(panel)
        return
    revision = int(loaded.get("revision", -1))
    source = loaded.get("snapshot", {}).duplicate(true)
    vaccine = source.get("vaccine", {})
    can_edit = allow_actions and not bool(source.get("selection_required", false)) \
        and not vaccine.is_empty() and vaccine.get("mission") == null
    if vaccine.is_empty():
        header.text = "백신 선택 필요"
        hint.text = "최종 파기 상태 · 데이터베이스와 능력치를 편집할 수 없어."
        for panel in [overview_body, stat_body, database_body, skills_body]:
            _clear(panel)
        return
    header.text = "백신 관리   Lv.%d  /  저장 r%d" % [
        int(vaccine.get("level", 1)), revision]
    hint.text = "허브: 편집·무료 재분배·보관 가능" if can_edit else \
        "의뢰 진행 중 · 현재 상태는 읽기 전용"
    allocation_draft = vaccine.get("invested_stats", {}).duplicate(true)
    selected_skills = ["", "", ""]
    var equipped: Array = vaccine.get("equipped_skills", [])
    for index in range(mini(3, equipped.size())):
        selected_skills[index] = str(equipped[index])
    _render_overview()
    _render_stats()
    _render_database()
    _render_skills()
    show_live_combat({})

func _render_overview() -> void:
    _clear(overview_body)
    var copies: int = int(vaccine.get("copies_remaining", 0))
    var level: int = int(vaccine.get("level", 1))
    var config: Dictionary = source.get("balance", {})
    var xp: int = int(vaccine.get("xp", 0))
    var needed: int = Balance.needed_xp(level, true, config)
    var point_unit: int = Balance.get_number("fragments_per_point", config)
    var available: int = int(vaccine.get("unspent_fragments", 0))
    overview_body.add_child(_label(
        "백신 Lv.%d  ·  XP %d / %d" % [level, xp, needed], 18))
    overview_body.add_child(_label(
        "복제 %d/3   ·   남은 성장 포인트 %dP + %d조각" % [
            copies, available / point_unit, available % point_unit], 16))
    if copies <= 0:
        overview_body.add_child(_label(
            "위험: 다음 사망 시 이 백신의 모든 성장과 소지 플러그인이 최종 파기돼.", 14,
            Color(1.0, 0.48, 0.48)))
    overview_body.add_child(_label(
        "데이터베이스 %d개   ·   출전 소지 %d개   ·   습득 스킬 %d개" % [
            vaccine.get("archive", {}).size(), vaccine.get("carried", {}).size(),
            vaccine.get("known_skills", []).size()], 15))
    if vaccine.get("mission") is Dictionary:
        var mission: Dictionary = vaccine["mission"]
        overview_body.add_child(_label(
            "의뢰 Lv.%d  ·  정화 %d/5  ·  보상 대기 %d  ·  재추첨 %d" % [
                int(mission.get("mission_level", 1)),
                mission.get("hardware_cleaned_ids", []).size(),
                int(mission.get("pending_plugin_offers", 0)),
                int(mission.get("rerolls_left", 0))], 15))
    overview_body.add_child(_label(
        "보관된 플러그인은 사망 시 보호돼. 출전 소지 플러그인은 사망 시 잃을 수 있어.",
        14, MUTED))
    overview_body.add_child(_label(
        "진행 중인 의뢰의 자동 재개나 무단 포기는 지원하지 않아.", 13, MUTED))

func _render_stats() -> void:
    _clear(stat_body)
    var earned: int = int(vaccine.get("earned_fragments", 0))
    var config: Dictionary = source.get("balance", {})
    var used: int = Balance.stat_cost_fragments(allocation_draft, config)
    var unit: int = Balance.get_number("fragments_per_point", config)
    var remaining: int = earned - used
    stat_summary = _label("총 %dP + %d조각  ·  배분 후 잔여 %dP + %d조각" % [
        earned / unit, earned % unit, maxi(0, remaining) / unit,
        maxi(0, remaining) % unit], 16)
    stat_body.add_child(stat_summary)
    stat_body.add_child(_label(
        "능력치 투자 내역은 E에 영구 저장돼. B 전투 적용은 추가 통합 검증이 필요해.", 12, MUTED))
    for stat_id in Balance.STAT_COSTS.keys():
        var line := HBoxContainer.new()
        line.add_theme_constant_override("separation", 10)
        stat_body.add_child(line)
        var name_label := _label(STAT_NAMES.get(str(stat_id), str(stat_id)), 14)
        name_label.custom_minimum_size.x = 220
        name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
        line.add_child(name_label)
        var cost: int = int(Balance.STAT_COSTS[stat_id])
        line.add_child(_label("%dP" % cost, 12, MUTED))
        var minus := _button("−")
        minus.name = "StatMinus_" + str(stat_id)
        minus.disabled = not can_edit or int(allocation_draft.get(stat_id, 0)) == 0
        minus.pressed.connect(_change_stat.bind(str(stat_id), -1))
        line.add_child(minus)
        var count := _label(str(int(allocation_draft.get(stat_id, 0))), 14)
        count.custom_minimum_size.x = 46
        count.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
        line.add_child(count)
        var plus := _button("+")
        plus.name = "StatPlus_" + str(stat_id)
        plus.disabled = not can_edit or remaining < cost * unit \
            or int(allocation_draft.get(stat_id, 0)) >= 1000000
        plus.pressed.connect(_change_stat.bind(str(stat_id), 1))
        line.add_child(plus)
    var controls := HBoxContainer.new()
    stat_body.add_child(controls)
    var clear := _button("무료 재분배 초기화 (미저장)")
    clear.disabled = not can_edit
    clear.pressed.connect(func() -> void:
        allocation_draft.clear()
        _render_stats())
    controls.add_child(clear)
    stat_submit = _button("배분 저장")
    stat_submit.name = "ApplyStats"
    stat_submit.disabled = not can_edit or used < 0 or used > earned \
        or allocation_draft == vaccine.get("invested_stats", {})
    stat_submit.pressed.connect(_request_stats)
    controls.add_child(stat_submit)

func _change_stat(stat_id: String, delta: int) -> void:
    if not can_edit or not Balance.STAT_COSTS.has(stat_id):
        return
    var previous: int = int(allocation_draft.get(stat_id, 0))
    var updated: int = previous + delta
    if updated < 0 or updated > 1000000:
        return
    var next: Dictionary = allocation_draft.duplicate(true)
    if updated == 0:
        next.erase(stat_id)
    else:
        next[stat_id] = updated
    var cost: int = Balance.stat_cost_fragments(next, source.get("balance", {}))
    if cost < 0 or cost > int(vaccine.get("earned_fragments", 0)):
        return
    allocation_draft = next
    _render_stats()

func _request_stats() -> void:
    if not can_edit:
        return
    var cost: int = Balance.stat_cost_fragments(allocation_draft, source.get("balance", {}))
    if cost < 0 or cost > int(vaccine.get("earned_fragments", 0)):
        return
    if allocation_draft != vaccine.get("invested_stats", {}):
        allocate_requested.emit(allocation_draft.duplicate(true), revision)

func _plugin_label(uid: String, info: Dictionary) -> String:
    var entry: Dictionary = catalog.get(uid, {})
    var rarity: String = str(info.get("rarity", ""))
    return "%s   Lv.%d   [%s]" % [
        str(entry.get("name_ko", uid)), int(info.get("level", 1)), rarity]

func _render_database() -> void:
    _clear(database_body)
    database_body.add_child(_label(
        "백신 데이터베이스 · 보관한 플러그인은 사망해도 보호돼", 17))
    database_body.add_child(_label(
        "꺼낸 플러그인은 소지 상태가 되며 다음 사망 때 잃을 수 있어.", 13,
        Color(1.0, 0.72, 0.46)))
    _inventory_group("보관된 플러그인", vaccine.get("archive", {}),
        "withdraw_plugin", "출전으로 꺼내기")
    _inventory_group("출전 소지 플러그인", vaccine.get("carried", {}),
        "archive_plugin", "데이터베이스에 보관")

func _inventory_group(title: String, inventory: Dictionary,
        action: String, caption: String) -> void:
    database_body.add_child(_label("%s (%d)" % [title, inventory.size()], 16))
    if inventory.is_empty():
        database_body.add_child(_label("해당 플러그인이 없어.", 13, MUTED))
        return
    var ids: Array = inventory.keys()
    ids.sort()
    for uid in ids:
        var row := HBoxContainer.new()
        database_body.add_child(row)
        var item_label := _label(_plugin_label(str(uid), inventory[uid]), 14)
        item_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
        row.add_child(item_label)
        var move := _button(caption)
        move.name = action + "_" + str(uid)
        move.disabled = not can_edit
        move.pressed.connect(_request_move.bind(action, str(uid)))
        row.add_child(move)

func _request_move(action: String, uid: String) -> void:
    if not can_edit or not ["archive_plugin", "withdraw_plugin"].has(action):
        return
    var inventory: Dictionary = vaccine.get(
        "carried" if action == "archive_plugin" else "archive", {})
    if not inventory.has(uid):
        return
    plugin_move_requested.emit(action, uid, revision)

func _render_skills() -> void:
    _clear(skills_body)
    skills_body.add_child(_label("액티브 스킬 · 3슬롯", 17))
    skills_body.add_child(_label(
        "E에 장착 순서를 저장해. 실제 전투 스킬 정의·시전 연결은 별도 검증이 필요해.", 13, MUTED))
    var known: Array = vaccine.get("known_skills", [])
    for slot in range(3):
        var row := HBoxContainer.new()
        skills_body.add_child(row)
        var lab := _label("%d번 슬롯" % (slot + 1), 15)
        lab.custom_minimum_size.x = 110
        row.add_child(lab)
        var options := OptionButton.new()
        options.name = "SkillSlot_" + str(slot)
        options.size_flags_horizontal = Control.SIZE_EXPAND_FILL
        options.add_item("빈 슬롯")
        for uid in known:
            options.add_item(str(uid))
        var selected: int = 0
        if known.has(selected_skills[slot]):
            selected = known.find(selected_skills[slot]) + 1
        options.select(selected)
        options.disabled = not can_edit
        options.item_selected.connect(_select_skill.bind(slot))
        row.add_child(options)
    skill_submit = _button("스킬 장착 저장")
    skill_submit.name = "ApplySkills"
    skill_submit.disabled = not can_edit or known.is_empty()
    skill_submit.pressed.connect(_request_skills)
    skills_body.add_child(skill_submit)
    skills_body.add_child(_label(
        "저장 시 비어 있는 슬롯은 뒤로 정렬돼. 동일 스킬을 두 슬롯에 장착할 수 없어.",
        13, MUTED))

func _select_skill(index: int, slot: int) -> void:
    if not can_edit or slot < 0 or slot >= 3:
        return
    var known: Array = vaccine.get("known_skills", [])
    selected_skills[slot] = "" if index == 0 else \
        str(known[index - 1]) if index <= known.size() else ""
    _render_skills()

func _request_skills() -> void:
    if not can_edit:
        return
    var known: Array = vaccine.get("known_skills", [])
    var equipped: Array = []
    for uid in selected_skills:
        if uid.is_empty():
            continue
        if not known.has(uid) or equipped.has(uid):
            hint.text = "장착할 수 없는 스킬이야. 중복 선택을 확인해."
            return
        equipped.append(uid)
    if equipped != vaccine.get("equipped_skills", []):
        equip_requested.emit(equipped, revision)
    else:
        hint.text = "이미 저장된 스킬 구성과 동일해."

## B is the only authority for current combat values. Never infer them from
## E investment counts or fabricate shield values the public B API omitted.
func show_live_combat(actor: Dictionary) -> void:
    if combat_body == null:
        return
    # Live B snapshots may be polled at 60 Hz. Rebuilding hundreds of Control
    # nodes on every poll destroys scroll/focus and wastes CPU/GPU frames.
    var projection: Dictionary = {
        "stats": actor.get("stats", {}),
        "mana": actor.get("mana", null)
    }
    var signature: String = JSON.stringify(projection)
    if signature == _last_combat_projection and combat_body.get_child_count() > 0:
        return
    _last_combat_projection = signature
    combat_render_count += 1
    var scroll: ScrollContainer = combat_body.get_parent() as ScrollContainer
    var scroll_before: int = scroll.scroll_vertical if scroll != null else 0
    _clear(combat_body)
    var stats: Dictionary = actor.get("stats", {})
    combat_body.add_child(_label("전투 능력치 · 실제 B 런타임", 17))
    if stats.is_empty():
        combat_body.add_child(_label(
            "현재 실행 중인 B 전투가 없어 실제 전투 수치를 확인할 수 없어.", 14, MUTED))
        return
    var rows: Array = [
        ["체력", "health", ""], ["최대 체력", "max_health", ""],
        ["공격력 (AD)", "attack_damage", ""], ["주문력 (AP)", "ability_power", ""],
        ["물리 방어력", "armor", ""], ["마법 저항력", "magic_resist", ""],
        ["고정 물리 관통", "physical_pen_flat", ""],
        ["비율 물리 관통", "physical_pen_percent", "rate"],
        ["고정 마법 관통", "magic_pen_flat", ""],
        ["비율 마법 관통", "magic_pen_percent", "rate"],
        ["발사속도", "attack_speed", "per_sec"],
        ["이동속도", "move_speed", ""],
        ["탄환속도 배율", "projectile_speed_mult", ""],
        ["치명타 확률", "crit_chance", "rate"],
        ["치명타 배율", "crit_multiplier", ""],
        ["스킬 가속", "skill_haste", ""], ["행운", "luck", "rate"],
        ["적중 흡혈", "on_hit_vamp", "rate"],
        ["모든 피해 흡혈", "omni_vamp", "rate"],
        ["강인함", "tenacity", "rate"],
        ["최대 마나", "max_mana", ""],
        ["초당 마나 재생", "mana_regen", "per_sec"]
    ]
    for spec in rows:
        var key: String = str(spec[1])
        if not stats.has(key):
            continue
        var amount: float = float(stats[key])
        var formatted: String = "%.1f" % amount
        if str(spec[2]) == "rate":
            formatted = "%.1f%%" % (amount * 100.0)
        elif str(spec[2]) == "per_sec":
            formatted += " /초"
        var line := HBoxContainer.new()
        combat_body.add_child(line)
        var name_label := _label(str(spec[0]), 14, MUTED)
        name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
        line.add_child(name_label)
        line.add_child(_label(formatted, 14))
    combat_body.add_child(_label(
        "현재 마나: %.1f" % float(actor.get("mana", 0.0)) if actor.has("mana")
            else "현재 마나: 확인 불가", 14))
    combat_body.add_child(_label(
        "보호막: %.1f" % float(stats["shield"]) if stats.has("shield")
            else "보호막: 공개된 B 데이터 없음", 14, MUTED))
    if scroll != null and scroll_before > 0:
        scroll.set_deferred("scroll_vertical", scroll_before)
