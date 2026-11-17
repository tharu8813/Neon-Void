extends SceneTree
## Pure visual regression: 17 Python silhouettes/colors + 3D boss indicators.
## No real user files, physical GPU claims or combat-rule mutations.
const Shapes = preload("res://scripts/visuals/legacy_actor_shapes.gd")
const ActorView = preload("res://scripts/visuals/legacy_enemy_visual.gd")
const Catalog = preload("res://scripts/enemies/enemy_catalog.gd")
const MainScene = preload("res://scenes/main.tscn")
var failures: int = 0

const ORIGINAL_COLORS: Dictionary = {
    "drifter": "#ff3f7f", "mini": "#b06bff", "shooter": "#ffcf5c",
    "splitter": "#b06bff", "rusher": "#ff8a3d", "boss": "#ff5cf0",
    "overheater": "#ff874c", "worm": "#aa80ff", "trojan": "#f79bda",
    "ransomware": "#ffca69", "rootkit": "#dc70e9", "packet_swarm": "#50dbe9",
    "guardian": "#ffe298", "overseer": "#ff6538", "replicator": "#b878ff",
    "encryptor": "#ffc05d", "zero_day": "#f05ef1"
}
const POLYGON_SIDES: Dictionary = {
    "drifter": 3, "mini": 3, "shooter": 4, "splitter": 5,
    "rusher": 4, "boss": 6, "overheater": 3, "worm": 5,
    "trojan": 4, "ransomware": 6, "rootkit": 8,
    "packet_swarm": 3, "guardian": 7, "overseer": 6,
    "replicator": 6, "encryptor": 6, "zero_day": 6
}

func _initialize() -> void:
    create_timer(40.0).timeout.connect(func(): printerr("P9 ACTOR TIMEOUT"); quit(1))
    call_deferred("_run")

func verify(ok: bool, what: String) -> void:
    if not ok:
        failures += 1
        printerr("P9 ACTOR FAIL: " + what)

func _run() -> void:
    verify(Catalog.STATS.size() == 17, "frozen enemy kind count")
    verify(ORIGINAL_COLORS.size() == Catalog.STATS.size(), "every Python enemy has a color")
    for kind in Catalog.STATS:
        verify(ORIGINAL_COLORS.has(kind), "Python color registered for " + kind)
        if not ORIGINAL_COLORS.has(kind):
            continue
        verify(Shapes.color_for(kind) == Color(ORIGINAL_COLORS[kind]), "exact original tint: " + kind)
        verify(Shapes.polygon_for(kind, float(Catalog.STATS[kind][1])).size() == int(POLYGON_SIDES[kind]), "unique silhouette: " + kind)
        var view: Node3D = ActorView.new()
        view.call("configure", str(kind), float(Catalog.STATS[kind][1]))
        root.add_child(view)
        verify(view.get_node_or_null("Silhouette") is MeshInstance3D, "mesh hull node: " + kind)
        var mesh: Mesh = view.get_node("Silhouette").mesh
        verify(mesh is ArrayMesh and mesh.get_surface_count() == 1, "actual extruded ArrayMesh: " + kind)
        var snapshot: Dictionary = {
            "kind": kind, "hp": 60.0, "max_hp": 100.0, "pos": Vector2(100, 100),
            "elapsed": 2.0, "angle": 0.35, "state": 1, "guard": 0.0,
            "vulnerable": 0.0, "statuses": []
        }
        var original: Dictionary = snapshot.duplicate(true)
        view.call("sync_state", snapshot, false)
        verify(snapshot == original, "visual sync does not mutate C enemy: " + kind)
        if Catalog.is_boss(str(kind)):
            verify(view.get_node_or_null("CounterRotatingCore") != null and
                view.get_node_or_null("Orbit/OrbitBeacon") != null,
                "boss nested rings: " + kind)
            verify(absf(view.get_node("Silhouette").rotation.y) > 0.5,
                "boss animation driven by C elapsed time: " + kind)
            view.call("sync_state", snapshot, true)
            verify(is_zero_approx(view.get_node("Silhouette").rotation.y),
                "reduced effects stops boss spin: " + kind)
        if str(kind) == "rusher":
            verify(view.get_node("StatusRing").visible, "windup warning ring")
            snapshot["state"] = 0
            view.call("sync_state", snapshot, false)
            verify(not view.get_node("StatusRing").visible, "no false rush warning")
        snapshot["hp"] = 48.0
        view.call("sync_state", snapshot, false)
        verify(view.get_node("Silhouette").material_override == view.get("hurt_material"),
            "damage flash material: " + kind)
        view.queue_free()

    var game: Node3D = MainScene.instantiate() as Node3D
    root.add_child(game)
    verify(game.get_node("PlayerVisual").mesh is ArrayMesh, "player ship is not a placeholder sphere")
    verify(Shapes.polygon_for("player", 13.0).size() == 4, "original four-point ship hull")
    verify(game.get_node("PlayerVisual/IdentityRing") is MeshInstance3D, "original player neon ring")
    verify(game.get_node("PlayerVisual/DashRing") is MeshInstance3D, "player dash cue is visual-only")
    verify(game.call("start_new_run"), "existing C/E game still starts")
    verify(game.call("enter_sector", "CPU"), "C sector gameplay still starts")
    var sim: RefCounted = game.get("sim")
    var spawned: int = sim.spawn_enemy("rusher", Vector2(410.0, 330.0))
    game.call("_update_visuals")
    var views: Dictionary = game.get("enemy_visuals")
    verify(views.has(spawned), "main integrates original rusher visual")
    if views.has(spawned):
        verify(views[spawned].get_node_or_null("Silhouette") != null, "no generic sphere in main")
    game.queue_free()
    if failures == 0:
        print("P9 ORIGINAL 17 ENEMY + PLAYER 3D SILHOUETTE PASS (headless structure, not visible GPU)")
    quit(1 if failures > 0 else 0)
