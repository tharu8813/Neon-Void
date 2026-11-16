extends RefCounted
## Original enemy HP/radius/speed/damage/armor/score and sector-virus action data.
const STATS: Dictionary = {
    "drifter": [22, 13, 108, 6, 2, 12],
    "mini": [10, 8, 158, 4, 0, 6],
    "shooter": [34, 15, 78, 8, 8, 22],
    "splitter": [50, 18, 94, 9, 13, 26],
    "rusher": [28, 14, 124, 12, 5, 20],
    "boss": [620, 46, 70, 18, 26, 400],
    "overheater": [29, 13, 172, 11, 3, 24],
    "worm": [35, 14, 126, 7, 3, 22],
    "trojan": [41, 15, 109, 10, 9, 28],
    "ransomware": [76, 19, 70, 13, 24, 46],
    "rootkit": [54, 16, 91, 12, 17, 38],
    "packet_swarm": [17, 10, 193, 6, 1, 12],
    "guardian": [86, 20, 68, 10, 31, 44],
    "overseer": [660, 43, 103, 20, 22, 450],
    "replicator": [620, 45, 92, 17, 19, 450],
    "encryptor": [780, 48, 65, 22, 38, 480],
    "zero_day": [850, 48, 128, 23, 27, 520]
}
const BOSSES: Array[String] = ["boss", "overseer", "replicator", "encryptor", "zero_day"]
const MAX_LIVE_ENEMIES: int = 64
const MAX_REPLICATIONS: int = 24
const MAX_ENEMY_BULLETS: int = 260
const PULSE_RADIUS: float = 185.0
## [behavior, cooldown, power, shots, pen, infection, clone_kind, clones/cast, clones/source, telegraph]
const VIRUS: Dictionary = {
    "overheater": ["telegraphed_charge", 2.4, 190, 0, 0, 2, "", 0, 0, 0.45],
    "worm": ["replicate", 3.2, 1, 0, 0, 3, "worm", 1, 2, 0.65],
    "trojan": ["feint_volley", 2.8, 300, 3, 0, 4, "", 0, 0, 0.55],
    "ransomware": ["encryption_pulse", 3.8, 7, 0, 0, 7, "", 0, 0, 0.8],
    "rootkit": ["armor_piercing_volley", 3.0, 320, 4, 12, 5, "", 0, 0, 0.35],
    "packet_swarm": ["quick_volley", 2.2, 340, 2, 0, 2, "", 0, 0, 0],
    "guardian": ["armor_pulse", 4.2, 12, 0, 0, 2, "", 0, 0, 0.6],
    "overseer": ["overload_barrage", 2.5, 350, 10, 0, 5, "", 0, 0, 0.5],
    "replicator": ["replicate", 3.1, 1, 0, 0, 6, "worm", 2, 10, 0.75],
    "encryptor": ["encryption_pulse", 3.0, 13, 0, 0, 9, "", 0, 0, 0.7],
    "zero_day": ["breach_barrage", 2.0, 405, 12, 16, 9, "", 0, 0, 0.35]
}

static func is_boss(kind: String) -> bool:
    return kind in BOSSES

static func make_enemy(kind: String, enemy_id: int, position: Vector2, wave: int, rng: RandomNumberGenerator) -> Dictionary:
    assert(STATS.has(kind), "Unknown enemy kind: " + kind)
    var s: Array = STATS[kind]
    var boss: bool = is_boss(kind)
    var hp: float = float(s[0])
    if kind == "boss":
        hp += 380.0 * (wave / 5 - 1)
    elif boss:
        hp *= 1.0 + 0.06 * float(wave - 1)
    else:
        hp *= 1.0 + 0.13 * float(wave - 1)
    return {
        "id": enemy_id, "kind": kind, "pos": position, "vel": Vector2.ZERO,
        "hp": maxf(4.0, hp), "max_hp": maxf(4.0, hp), "radius": float(s[1]),
        "speed": float(s[2]), "damage": float(s[3]),
        "armor": float(s[4]) * (1.0 + 0.025 * maxf(0.0, float(wave - 1))),
        "score": int(s[5]), "boss": boss, "state": 0, "timer": 0.0,
        "elapsed": 0.0, "cooldown": (rng.randf_range(1.0, 2.4) if kind == "shooter" else (1.6 if boss else 0.0)),
        "virus_elapsed": 0.0, "clone_count": 0, "guard": 0.0, "vulnerable": 0.0,
        "statuses": [], "angle": 0.0
    }
