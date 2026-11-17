extends PanelContainer
## A presentation for the B *public* actor_snapshot().status_state contract.
## Runtime effects, duration and stack counts are owned only by B.
## This view never applies/clears status, derives rewards, or writes to E.
const TICKS_PER_SECOND := 60.0
const MAX_VISIBLE := 5
const FLAGS_KO := {
    "stopped": "행동 불가", "silenced": "침묵", "rooted": "이동 불가",
    "infinite_pierce": "무한 관통"
}
var summary: Label
var lines: Array[Label] = []
var _last_signature: String = ""
var _effects: Array[Dictionary] = []
var _has_actor: bool = false

func _ready() -> void:
    name = "BStatusEffects"
    process_mode = Node.PROCESS_MODE_ALWAYS
    mouse_filter = Control.MOUSE_FILTER_IGNORE
    visible = false
    custom_minimum_size = Vector2(365, 110)
    var style := StyleBoxFlat.new()
    style.bg_color = Color(0.018, 0.039, 0.065, 0.93)
    style.border_color = Color(0.35, 0.73, 0.91, 0.75)
    style.set_border_width_all(1)
    style.set_corner_radius_all(6)
    style.set_content_margin_all(9.0)
    add_theme_stylebox_override("panel", style)

    var body := VBoxContainer.new()
    body.add_theme_constant_override("separation", 3)
    body.mouse_filter = Control.MOUSE_FILTER_IGNORE
    add_child(body)
    summary = _label("상태이상", Color(0.78, 0.94, 1.0), 13)
    body.add_child(summary)
    for index in range(MAX_VISIBLE):
        var chip := _label("", Color(0.92, 0.87, 0.71), 12)
        chip.visible = false
        chip.name = "Effect_" + str(index)
        body.add_child(chip)
        lines.append(chip)
    update_layout()

func _label(value: String, color: Color, font_size: int) -> Label:
    var node := Label.new()
    node.text = value
    node.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
    node.mouse_filter = Control.MOUSE_FILTER_IGNORE
    node.add_theme_color_override("font_color", color)
    node.add_theme_font_size_override("font_size", font_size)
    return node

func update_layout() -> void:
    if get_viewport() == null:
        return
    var vp: Vector2 = get_viewport().get_visible_rect().size
    # At narrow widths, reserve room for the tracker on the right.
    var available: float = vp.x - (420.0 if vp.x < 1100.0 else 36.0)
    custom_minimum_size.x = minf(450.0, maxf(180.0, available))
    position = Vector2(18.0, maxf(0.0, vp.y - 172.0))

static func _clean_entries(actor: Dictionary) -> Array[Dictionary]:
    var found: Array[Dictionary] = []
    var status: Variant = actor.get("status_state", {})
    if not (status is Dictionary):
        return found
    var entries: Variant = status.get("statuses", [])
    if not (entries is Array):
        return found
    for item in entries:
        if not (item is Dictionary):
            continue
        var uid: String = str(item.get("id", ""))
        var ticks: int = int(item.get("remaining_ticks", 0))
        if uid.is_empty() or ticks <= 0:
            continue
        found.append({
            "id": uid, "ticks": ticks, "stacks": maxi(1, int(item.get("stacks", 1))),
            "flags": item.get("flags", []), "control": bool(item.get("control", false)),
            "dot_dps": maxf(0.0, float(item.get("dot_dps", 0.0))),
            "regen_hps": maxf(0.0, float(item.get("regen_hps", 0.0)))
        })
    # Stable order prevents rapid UI jumping when B reorders effect packets.
    found.sort_custom(func(a: Dictionary, b: Dictionary) -> bool:
        return str(a["id"]) < str(b["id"]))
    return found

static func effect_label(data: Dictionary) -> String:
    var flags: Array[String] = []
    var raw_flags: Variant = data.get("flags", [])
    if raw_flags is Array:
        for item in raw_flags:
            var flag: String = str(item)
            flags.append(str(FLAGS_KO.get(flag, flag)))
    var info: String = ""
    if not flags.is_empty():
        info = " · " + ", ".join(flags)
    elif bool(data.get("control", false)):
        info = " · 제어 효과"
    elif float(data.get("dot_dps", 0.0)) > 0.0:
        info = " · 지속 피해"
    elif float(data.get("regen_hps", 0.0)) > 0.0:
        info = " · 지속 회복"
    var stacks: int = maxi(1, int(data.get("stacks", 1)))
    var stack_label: String = " ×%d" % stacks if stacks > 1 else ""
    var seconds: float = maxf(0.0, float(data.get("ticks", 0))) / TICKS_PER_SECOND
    return "%s%s%s  ·  %.1f초" % [
        str(data.get("id", "미확인 효과")), stack_label, info, seconds]

func render_actor(actor: Dictionary) -> void:
    var incoming: Array[Dictionary] = _clean_entries(actor)
    _effects = incoming.duplicate(true)
    _has_actor = not actor.is_empty()
    var signature: String = JSON.stringify(incoming)
    if signature == _last_signature and (not visible or not incoming.is_empty()):
        return
    _last_signature = signature
    visible = not incoming.is_empty()
    if not visible:
        return
    summary.text = "전투 상태 · %d개 적용 중" % incoming.size()
    for i in range(MAX_VISIBLE):
        lines[i].visible = i < incoming.size()
        if not lines[i].visible:
            continue
        var item: Dictionary = incoming[i]
        lines[i].text = effect_label(item)
        var danger: bool = bool(item.get("control", false)) \
            or float(item.get("dot_dps", 0.0)) > 0.0
        lines[i].add_theme_color_override("font_color",
            Color(1.0, 0.58, 0.54) if danger else Color(0.74, 0.95, 0.85))
    if incoming.size() > MAX_VISIBLE:
        summary.text += " · 처음 %d개만 표시" % MAX_VISIBLE

func clear_view() -> void:
    _effects.clear()
    _last_signature = ""
    _has_actor = false
    visible = false
