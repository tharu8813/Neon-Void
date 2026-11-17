extends RefCounted
## Frozen Python neonvoid/view.py::draw_world and draw_player top-down silhouettes.
## These meshes are VISUAL ONLY: B/C own logical radii, hit tests and physics.
const PX_TO_WORLD: float = 0.01
const COLORS: Dictionary = {
    "player": "#6ff2ff",
    "drifter": "#ff3f7f", "mini": "#b06bff",
    "shooter": "#ffcf5c", "splitter": "#b06bff",
    "rusher": "#ff8a3d", "boss": "#ff5cf0",
    "overheater": "#ff874c", "worm": "#aa80ff",
    "trojan": "#f79bda", "ransomware": "#ffca69",
    "rootkit": "#dc70e9", "packet_swarm": "#50dbe9",
    "guardian": "#ffe298", "overseer": "#ff6538",
    "replicator": "#b878ff", "encryptor": "#ffc05d",
    "zero_day": "#f05ef1",
}
const SIDES: Dictionary = {
    "shooter": 4, "splitter": 5,
    "overheater": 3, "worm": 5, "trojan": 4,
    "ransomware": 6, "rootkit": 8, "packet_swarm": 3,
    "guardian": 7, "boss": 6, "overseer": 6,
    "replicator": 6, "encryptor": 6, "zero_day": 6,
}

static func color_for(kind: String) -> Color:
    assert(COLORS.has(kind), "Unknown legacy visual kind: " + kind)
    return Color(str(COLORS.get(kind, "#ffffff")))

static func polygon_for(kind: String, radius_px: float) -> PackedVector2Array:
    # Local X/Z is identical to original screen X/Y. Rotation is visual only.
    var result := PackedVector2Array()
    if kind == "player":
        # draw_player: nose 21 px, 13 px swept wings, 7 px recessed tail.
        result.append(Vector2(21.0, 0.0))
        result.append(Vector2(cos(2.42) * 13.0, sin(2.42) * 13.0))
        result.append(Vector2(-7.0, 0.0))
        result.append(Vector2(cos(-2.42) * 13.0, sin(-2.42) * 13.0))
    elif kind == "drifter" or kind == "mini":
        result.append(Vector2(radius_px * 1.25, 0.0))
        result.append(Vector2(cos(2.35) * radius_px * 0.85, sin(2.35) * radius_px * 0.85))
        result.append(Vector2(cos(-2.35) * radius_px * 0.85, sin(-2.35) * radius_px * 0.85))
    elif kind == "rusher":
        # draw_world::_arrow uses a four-point forward-charging arrow.
        var length_px: float = radius_px * 1.5
        result.append(Vector2(length_px, 0.0))
        result.append(Vector2(cos(2.5) * length_px * 0.62, sin(2.5) * length_px * 0.62))
        result.append(Vector2(-length_px * 0.45, 0.0))
        result.append(Vector2(cos(-2.5) * length_px * 0.62, sin(-2.5) * length_px * 0.62))
    else:
        assert(SIDES.has(kind), "Unmapped enemy silhouette: " + kind)
        var count: int = int(SIDES.get(kind, 6))
        for index in range(count):
            var angle: float = TAU * float(index) / float(count)
            result.append(Vector2(cos(angle), sin(angle)) * radius_px)
    return result

static func mesh_for(kind: String, radius_px: float) -> ArrayMesh:
    var outline: PackedVector2Array = polygon_for(kind, radius_px)
    assert(outline.size() >= 3, "Silhouette requires three or more vertices")
    var vertices := PackedVector3Array()
    var normals := PackedVector3Array()
    var count: int = outline.size()
    var top: float = 0.12 if kind == "player" else 0.10
    var bottom: float = 0.005
    # Triangular fan top, keeping the original polygon's outer silhouette.
    for index in range(count):
        var a: Vector2 = outline[index] * PX_TO_WORLD
        var b: Vector2 = outline[(index + 1) % count] * PX_TO_WORLD
        vertices.append(Vector3(0.0, top, 0.0))
        vertices.append(Vector3(a.x, top, a.y))
        vertices.append(Vector3(b.x, top, b.y))
        for n in range(3):
            normals.append(Vector3.UP)
    # Real extruded vertical sides, not a flat 2D shader billboard.
    for index in range(count):
        var a: Vector2 = outline[index] * PX_TO_WORLD
        var b: Vector2 = outline[(index + 1) % count] * PX_TO_WORLD
        var va := Vector3(a.x, top, a.y)
        var vb := Vector3(b.x, top, b.y)
        var vc := Vector3(a.x, bottom, a.y)
        var vd := Vector3(b.x, bottom, b.y)
        var out: Vector3 = Vector3(b.y - a.y, 0.0, a.x - b.x).normalized()
        for v in [va, vb, vc, vb, vd, vc]:
            vertices.append(v)
            normals.append(out)
    var arrays: Array = []
    arrays.resize(Mesh.ARRAY_MAX)
    arrays[Mesh.ARRAY_VERTEX] = vertices
    arrays[Mesh.ARRAY_NORMAL] = normals
    var result := ArrayMesh.new()
    result.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
    return result

static func lit_material(tint: Color) -> StandardMaterial3D:
    var value := StandardMaterial3D.new()
    value.albedo_color = tint
    value.metallic = 0.12
    value.roughness = 0.38
    value.emission_enabled = true
    value.emission = tint
    value.emission_energy_multiplier = 0.8
    value.cull_mode = BaseMaterial3D.CULL_DISABLED
    return value

static func ring_mesh(outer_radius: float, band: float = 0.022) -> TorusMesh:
    var ring := TorusMesh.new()
    ring.inner_radius = maxf(0.01, outer_radius - band)
    ring.outer_radius = outer_radius
    ring.rings = 32
    ring.ring_segments = 6
    return ring
