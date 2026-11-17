extends RefCounted
## Presentation-only adapter for C's verified catalog and already-drawn cards.
## No random rolls, reward confirmation, skill changes or E persistence.

static func rarity_name(rarity: String) -> String:
    match rarity:
        "common":
            return "일반"
        "rare":
            return "레어"
        "legendary":
            return "전설"
        "mythic":
            return "신화"
        _:
            return "등급 확인 필요"

static func rarity_color(rarity: String) -> Color:
    match rarity:
        "common":
            return Color(0.45, 0.86, 0.92)
        "rare":
            return Color(0.49, 0.61, 1.0)
        "legendary":
            return Color(1.0, 0.72, 0.31)
        "mythic":
            return Color(0.95, 0.43, 0.89)
        _:
            return Color(0.52, 0.57, 0.65)

static func card_label(card: Dictionary, entry: Dictionary,
        owned_level: int) -> String:
    if bool(card.get("empty", false)):
        return "선택 가능한 플러그인 없음"
    var rarity: String = str(card.get("rarity", ""))
    var uid: String = str(card.get("plugin_id", ""))
    var name: String = str(entry.get("name_ko", uid))
    var next_level: int = owned_level + 1
    var description: String = str(entry.get("description_ko", "효과 정보 확인 필요"))
    var badge: String = ""
    var effects: Variant = entry.get("level_effects", [])
    if effects is Array:
        for variant in effects:
            if variant is Dictionary and int(variant.get("level", 0)) == next_level:
                description = str(variant.get("summary_ko", description))
                if str(variant.get("status", "")).begins_with("proposal"):
                    badge = " [초안]"
                break
    return "[%s] %s    Lv.%d / %d\n%s%s" % [
        rarity_name(rarity), name, next_level, int(entry.get("max_level", next_level)),
        description, badge
    ]

static func card_style(rarity: String, variant: String = "normal") -> StyleBoxFlat:
    var accent: Color = rarity_color(rarity)
    var style := StyleBoxFlat.new()
    style.bg_color = Color(0.035, 0.065, 0.11).lerp(accent, 0.2)
    style.border_color = accent
    style.set_border_width_all(2)
    style.set_corner_radius_all(8)
    style.content_margin_left = 12
    style.content_margin_right = 12
    style.content_margin_top = 9
    style.content_margin_bottom = 9
    if variant == "hover":
        style.bg_color = style.bg_color.lightened(0.15)
    elif variant == "pressed":
        style.bg_color = style.bg_color.lightened(0.24)
    elif variant == "disabled":
        style.bg_color = Color(0.045, 0.052, 0.07)
        style.border_color = Color(0.27, 0.29, 0.33)
    return style
