extends Node3D
## A opt-in scene adapter. Add this node only in the NEW rework scene.
## No project.godot/autoload or legacy scene edits.
signal combat_notice(notice: Dictionary)
signal combat_actor_died(actor_id: String, killer_id: String)

const Runtime = preload("res://scripts/rework/combat/combat_runtime.gd")
var simulation = Runtime.new()
var bound_nodes: Dictionary = {}
var intents: Dictionary = {}
var visual_nodes: Dictionary = {}
var projectile_factory: Callable = Callable()
var drive_actor_nodes: bool = false
var running: bool = false
var accumulator: float = 0.0
const STEP: float = 1.0 / 60.0

func setup(encounter_id: String, seed_value: int, config: Dictionary = {}) -> void:
    var settings: Dictionary = config.duplicate(true)
    settings["encounter_id"] = encounter_id
    simulation.begin(seed_value, settings)
    bound_nodes.clear()
    intents.clear()
    for node in visual_nodes.values():
        if is_instance_valid(node):
            node.queue_free()
    visual_nodes.clear()
    accumulator = 0.0
    running = true

func register_node(actor_id: String, team: String, node: Node3D,
        stats: Dictionary = {}, options: Dictionary = {}) -> bool:
    if node == null or not is_instance_valid(node):
        return false
    var ok: bool = simulation.register_actor(actor_id, team, node.global_position,
        stats, options)
    if ok:
        bound_nodes[actor_id] = node
    return ok

func unregister_node(actor_id: String) -> void:
    bound_nodes.erase(actor_id)
    simulation.remove_actor(actor_id)
    intents.erase(actor_id)

func set_input(actor_id: String, command: Dictionary) -> void:
    ## A translates keyboard/mouse view to XZ move and aim_world before call.
    intents[actor_id] = command.duplicate(true)

func set_projectile_factory(factory: Callable) -> void:
    ## Optional A VFX: factory(projectile_snapshot)->Node3D
    projectile_factory = factory

func _physics_process(delta: float) -> void:
    if not running:
        return
    accumulator += maxf(0.0, delta)
    # Fixed 60Hz catch-up has no dropped simulation ticks. A can pause if needed.
    while accumulator >= STEP:
        advance_one_tick()
        accumulator -= STEP

func advance_one_tick() -> Array[Dictionary]:
    ## World/controller remains authoritative for actor nodes before simulation.
    for actor_id in bound_nodes.keys():
        var node: Node3D = bound_nodes[actor_id]
        if is_instance_valid(node):
            simulation.set_actor_position(str(actor_id), node.global_position)
    var notices: Array[Dictionary] = simulation.step_fixed(intents)
    if drive_actor_nodes:
        for actor_id in bound_nodes.keys():
            var node: Node3D = bound_nodes[actor_id]
            var state: Dictionary = simulation.snapshot(str(actor_id))
            if state.is_empty() or not is_instance_valid(node):
                continue
            var destination: Vector3 = state["position"]
            if node is CharacterBody3D:
                var body: CharacterBody3D = node
                body.velocity = (destination - body.global_position) / STEP
                body.move_and_slide()
                simulation.set_actor_position(str(actor_id), body.global_position)
            else:
                node.global_position = destination
    _sync_projectile_visuals()
    for note in notices:
        combat_notice.emit(note)
        if str(note.get("kind", "")) == "death":
            combat_actor_died.emit(str(note["actor_id"]), str(note["killer_id"]))
    # dash and skill requests are one-shot; movement/fire may remain held.
    for actor_id in intents.keys():
        var intent: Dictionary = intents[actor_id]
        intent.erase("dash")
        intent.erase("skill_slot")
    return notices

func _sync_projectile_visuals() -> void:
    var active: Dictionary = {}
    for projectile in simulation.projectile_snapshots():
        var id: String = str(projectile["id"])
        active[id] = true
        if not visual_nodes.has(id) and projectile_factory.is_valid():
            var visual: Variant = projectile_factory.call(projectile.duplicate(true))
            if visual is Node3D:
                visual_nodes[id] = visual
                if visual.get_parent() == null:
                    add_child(visual)
        if visual_nodes.has(id) and is_instance_valid(visual_nodes[id]):
            visual_nodes[id].global_position = projectile["position"]
    for id in visual_nodes.keys():
        if active.has(id):
            continue
        if is_instance_valid(visual_nodes[id]):
            visual_nodes[id].queue_free()
        visual_nodes.erase(id)
