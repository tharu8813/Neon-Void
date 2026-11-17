extends Node3D
## Stable D world-facing contract. Keeps A's persisted pilot signal connections
## intact while replacing the internal combat implementation by mode.
## Demo mode keeps old preview smoke only; persisted pilot uses B live runtime.
signal enemy_defeated(actor_id: String, receipt_id: String)
signal player_died()
signal health_updated(health: float, maximum: float)
signal combat_notice(notice: Dictionary)
signal integration_failed(reason: String)

const Preview = preload("res://scripts/rework/world/demo_combat_bridge.gd")
const LiveB = preload("res://scripts/rework/integration/production_b_world_facade.gd")
var preview: Variant
var live: Variant
var mode: String = "preview"
var last_config: Dictionary = {}
var enemies: Dictionary:
    get:
        return live.enemies if mode == "formal" else preview.enemies

func _ready() -> void:
    preview = Preview.new()
    preview.name = "LegacyDemoCombatOnly"
    add_child(preview)
    live = LiveB.new()
    live.name = "BFormalCombatRuntime"
    add_child(live)
    preview.enemy_defeated.connect(_preview_death)
    preview.player_died.connect(_preview_player_death)
    preview.health_updated.connect(_relay_hp)
    live.enemy_defeated.connect(_formal_death)
    live.player_died.connect(_formal_player_death)
    live.health_updated.connect(_relay_hp)
    live.combat_notice.connect(_relay_notice)
    live.integration_failed.connect(_relay_failure)

func begin_encounter(player_node: CharacterBody3D, camera_node: Camera3D,
        world_map: Dictionary, run_id: String, config: Dictionary = {}) -> bool:
    var formal: bool = bool(config.get("formal", false))
    reset()
    mode = "formal" if formal else "preview"
    last_config = config.duplicate(true)
    if formal:
        return live.begin_encounter(player_node, camera_node, world_map, run_id, config)
    preview.begin_encounter(player_node, camera_node, world_map, run_id)
    return true

func reset() -> void:
    if preview != null:
        preview.reset()
    if live != null:
        live.reset()
    mode = "preview"

func spawn_hostile(info: Dictionary) -> bool:
    return live.spawn_hostile(info) if mode == "formal" else preview.spawn_hostile(info)

func configure_authorized_build(levels: Dictionary, instances: Array,
        skills: Array = []) -> bool:
    ## A obtains these exclusively from E-authorized snapshots or reward ACKs.
    return live.configure_authorized_build(levels, instances, skills)

func upgrade_plugin_after_ack(plugin_id: String, level: int) -> bool:
    ## The E receipt is already durably committed by A. Never grant a
    ## transient plugin to the B runtime before this step.
    return mode == "formal" and live != null \
        and live.has_method("upgrade_plugin_after_ack") \
        and bool(live.upgrade_plugin_after_ack(plugin_id, level))

func actor_snapshot(actor_id: String) -> Dictionary:
    return live.actor_snapshot(actor_id) if mode == "formal" else {}

func _apply_hit(enemy_id: String, hit_key: String) -> void:
    ## Retain the original D smoke fixture; NEVER a live B damage shortcut.
    if mode == "preview":
        preview._apply_hit(enemy_id, hit_key)

func _preview_death(actor_id: String, receipt_id: String) -> void:
    if mode == "preview":
        enemy_defeated.emit(actor_id, receipt_id)

func _formal_death(actor_id: String, receipt_id: String) -> void:
    if mode == "formal":
        enemy_defeated.emit(actor_id, receipt_id)

func _preview_player_death() -> void:
    if mode == "preview":
        player_died.emit()

func _formal_player_death() -> void:
    if mode == "formal":
        player_died.emit()

func _relay_hp(current: float, maximum: float) -> void:
    health_updated.emit(current, maximum)

func _relay_notice(notice: Dictionary) -> void:
    if mode == "formal":
        combat_notice.emit(notice)

func _relay_failure(reason: String) -> void:
    integration_failed.emit(reason)
