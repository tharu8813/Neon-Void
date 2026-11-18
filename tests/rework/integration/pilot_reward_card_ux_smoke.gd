extends SceneTree
## A UI-only smoke: never commits E, rerolls C or grants plugin levels.
const Styles = preload("res://scripts/rework/ui/pilot_reward_card_style.gd")
var failures: int = 0

func _initialize() -> void:
    call_deferred("_run")

func check(ok: bool, label: String) -> void:
    if not ok:
        failures += 1
        printerr("REWORK A CARD UX FAIL: " + label)

func _run() -> void:
    var entry: Dictionary = {
        "plugin_id": "nv_c_move_speed", "name_ko": "고속 이동",
        "description_ko": "이동속도 증가", "max_level": 2,
        "level_effects": [
            {"level": 1, "summary_ko": "이동속도 증가",
                "status": "concept_confirmed_numbers_as_labeled"},
            {"level": 2, "summary_ko": "이동속도 보너스 강화",
                "status": "proposal_v0.1"}
        ]
    }
    var card: Dictionary = {"rarity": "common",
        "plugin_id": "nv_c_move_speed", "slot": 0}
    var original: Dictionary = entry.duplicate(true)
    var first: String = Styles.card_label(card, entry, 0)
    check(first.contains("일반") and first.contains("고속 이동")
        and first.contains("Lv.1 / 2") and first.contains("이동속도 증가"),
        "level 1 renders only catalog truth")
    var second: String = Styles.card_label(card, entry, 1)
    check(second.contains("Lv.2 / 2") and second.contains("보너스 강화")
        and second.contains("[초안]"),
        "level-up summary and unapproved tuning clearly labelled")
    check(entry == original, "presenter does not mutate C metadata")
    var empty: String = Styles.card_label({"empty": true}, {}, 0)
    check(empty.contains("선택 가능한 플러그인 없음"),
        "exhausted pool has no phantom reward")
    check(Styles.rarity_name("legendary") == "전설"
        and Styles.rarity_name("mythic") == "신화",
        "all four rarity names available")
    check(Styles.rarity_color("rare") != Styles.rarity_color("common")
        and Styles.rarity_color("mythic") != Styles.rarity_color("legendary"),
        "rarity differentiation is visual")
    var normal: StyleBoxFlat = Styles.card_style("rare")
    var hover: StyleBoxFlat = Styles.card_style("rare", "hover")
    var disabled: StyleBoxFlat = Styles.card_style("rare", "disabled")
    check(normal.border_color != disabled.border_color
        and normal.bg_color != hover.bg_color,
        "hover and disabled never mimic an enabled reward")
    if failures == 0:
        print("REWORK A CARD UX PASS: localized C catalog, draft levels, visual rarity")
    quit(1 if failures > 0 else 0)
