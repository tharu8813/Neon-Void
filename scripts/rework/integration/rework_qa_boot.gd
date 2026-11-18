extends Node3D
## A-only opt-in REWORK QA executable root. No default-main changes.
## A durable synthetic QA slot is initialized ONCE; never rewrite or delete
## an existing slot, including a corrupted/locked one.
const Store = preload("res://scripts/rework/persistence/rework_save_store.gd")
const State = preload("res://scripts/rework/progression/state_model.gd")
const Gateway = preload("res://scripts/rework/persistence/progression_gateway.gd")
const Catalog = preload("res://scripts/rework/plugins/plugin_catalog.gd")
const SkillRegistry = preload("res://scripts/rework/skills/skill_registry.gd")

@export var qa_slot_id: String = "nv-rework-qa-20261118"
@export var qa_vaccine_id: String = "nv-rework-qa-vaccine"
var initialized: bool = false
var did_seed: bool = false
var pilot: Node3D
var banner: Label

func _enter_tree() -> void:
    # Parent enters before its PackedScene child, so this fixture exists
    # before the child pilot performs its own E Gateway.snapshot/readback.
    initialized = _ensure_fixture()

func _ensure_fixture() -> bool:
    var path: String = Store.default_slot_path(qa_slot_id)
    if path.is_empty():
        push_error("REWORK QA rejected invalid slot ID")
        return false
    if DirAccess.dir_exists_absolute(path):
        # Never reset user's QA progress. The child pilot will independently
        # freeze/offer recovery if this existing slot fails E verification.
        return bool(Gateway.snapshot(path).get("ok", false))
    var data: Dictionary = State.new_slot(qa_slot_id, qa_vaccine_id)
    if data.is_empty():
        return false
    var vaccine: Dictionary = data["vaccine"]
    # Explicit QA-ONLY starting state, not a gameplay reward or approved
    # normal progression. Every field is validated by the actual E save schema.
    vaccine["earned_fragments"] = 500
    vaccine["unspent_fragments"] = 500
    vaccine["known_skills"] = [
        "nv_skill_arc_pulse", "nv_skill_penetrator", "nv_skill_root_packet"]
    vaccine["equipped_skills"] = vaccine["known_skills"].duplicate()
    vaccine["archive"] = {
        "nv_c_move_speed": {"rarity": "common", "level": 1},
        "nv_c_periodic_shield": {"rarity": "common", "level": 1}}
    vaccine["carried"] = {
        "nv_c_bullet_size": {"rarity": "common", "level": 1}}
    if not State.validate(data) or SkillRegistry.validate_loadout(
            vaccine["known_skills"], vaccine["equipped_skills"]).get("ok") != true:
        push_error("REWORK QA fixture refused by E/B schema")
        return false
    var saved: Dictionary = Store.initialize(path, data)
    did_seed = bool(saved.get("ok", false))
    if not did_seed:
        push_error("REWORK QA fixture initialization refused: " +
            str(saved.get("reason", "unknown")))
    return did_seed

func _ready() -> void:
    pilot = get_node_or_null("ReworkPersistentPilot")
    if pilot == null:
        push_error("REWORK QA boot missing production A/E pilot scene")
        return
    get_window().title = "Neon Void | REWORK QA (not legacy)"
    var layer := CanvasLayer.new()
    layer.name = "QABuildIdentification"
    layer.layer = 21
    add_child(layer)
    var panel := PanelContainer.new()
    panel.name = "ReworkQAStamp"
    layer.add_child(panel)
    var box := StyleBoxFlat.new()
    box.bg_color = Color(0.03, 0.1, 0.17, 0.88)
    box.border_color = Color(0.18, 0.81, 0.76, 0.95)
    box.set_border_width_all(1)
    box.set_corner_radius_all(6)
    box.set_content_margin_all(7.0)
    panel.add_theme_stylebox_override("panel", box)
    banner = Label.new()
    banner.name = "ReworkQAVersion"
    banner.text = "REWORK QA · NOV 18\nTab: 백신/22능력치/복제/3스킬\n27개 플러그인 보상 풀 · 5개 하드웨어"
    banner.add_theme_font_size_override("font_size", 12)
    banner.add_theme_color_override("font_color", Color(0.78, 1.0, 0.94))
    panel.add_child(banner)
    get_viewport().size_changed.connect(_layout_banner)
    _layout_banner()
    if not initialized:
        banner.text = "REWORK QA · E 저장 검증 필요\n기존 슬롯은 절대 덮어쓰지 않음"
        banner.add_theme_color_override("font_color", Color(1.0, 0.63, 0.55))
        push_error("REWORK QA E slot unavailable or requires reconciliation")
        return
    var current: Dictionary = Gateway.snapshot(Store.default_slot_path(qa_slot_id))
    var current_vaccine: Dictionary = current.get("snapshot", {}).get("vaccine", {})
    var skills: Array = current_vaccine.get("equipped_skills", [])
    print("NEON_REWORK_QA_ACTIVE main=", ProjectSettings.get_setting(
        "application/run/main_scene"), " slot=", qa_slot_id,
        " skills=", skills.size(), " catalog=", Catalog.entries().size(),
        " save_revision=", current.get("revision", -1))

func _layout_banner() -> void:
    if banner == null:
        return
    var layer: CanvasLayer = get_node("QABuildIdentification")
    var panel: PanelContainer = layer.get_node("ReworkQAStamp")
    var vp: Vector2 = get_viewport().get_visible_rect().size
    var width: float = minf(305.0, maxf(195.0, vp.x - 24.0))
    panel.custom_minimum_size.x = width
    panel.position = Vector2(maxf(0.0, vp.x - width - 12.0), 8.0)
