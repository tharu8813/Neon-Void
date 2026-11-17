extends RefCounted
## Immutable 4-sector content, wave budgets and Python MT seed parity.
const PythonMT = preload("res://scripts/sectors/python_mt.gd")
const IDS: Array[String] = ["CPU", "MEMORY", "STORAGE", "NETWORK"]
const WAVE_LIMIT: int = 5
const COSTS: Dictionary = {
    "drifter": 1.0, "shooter": 2.0, "splitter": 2.0,
    "rusher": 1.7, "overheater": 1.4, "worm": 1.6,
    "trojan": 2.1, "ransomware": 3.3, "rootkit": 2.6,
    "packet_swarm": 1.0, "guardian": 3.5
}
const SECTORS: Dictionary = {
    "CPU": {
        "name": "CPU CORE", "entry_gate": "gate_cpu", "exit_gate": "exit_cpu",
        "danger": 2, "feature": "Overload and fast melee pressure",
        "base": 7.0, "per_wave": 3.25, "fallback": "overheater",
        "unlocks": [[1, "overheater"], [2, "rusher"], [3, "trojan"], [4, "packet_swarm"]],
        "boss": "overseer", "supports": ["overheater", "rusher"],
        "exposure": 0.30, "purify": 7.0,
        "features": ["Heat leak", "Clock surge", "Thermal throttle", "Overclock cascade", "OVERSEER control core"],
        "signature": ["dashb", "rapid", "power"],
        "focus": ["rapid", "power", "dash2", "dashb", "cool"]
    },
    "MEMORY": {
        "name": "MEMORY SECTOR", "entry_gate": "gate_memory", "exit_gate": "exit_memory",
        "danger": 3, "feature": "Limited worm replication",
        "base": 6.5, "per_wave": 3.0, "fallback": "worm",
        "unlocks": [[1, "worm"], [2, "splitter"], [3, "trojan"], [4, "rootkit"]],
        "boss": "replicator", "supports": ["worm", "trojan"],
        "exposure": 0.45, "purify": 9.0,
        "features": ["Memory leak", "Parasite threads", "Fork bomb", "Page corruption", "REPLICATOR hive"],
        "signature": ["boom", "chain", "split"],
        "focus": ["boom", "chain", "split", "focus", "burn"]
    },
    "STORAGE": {
        "name": "STORAGE VAULT", "entry_gate": "gate_storage", "exit_gate": "exit_storage",
        "danger": 3, "feature": "Encryption and armored defenders",
        "base": 7.0, "per_wave": 3.0, "fallback": "ransomware",
        "unlocks": [[1, "ransomware"], [2, "guardian"], [3, "rootkit"], [4, "trojan"]],
        "boss": "encryptor", "supports": ["ransomware", "guardian"],
        "exposure": 0.40, "purify": 10.0,
        "features": ["File lock", "Integrity scan", "Vault lockdown", "Encryption storm", "ENCRYPTOR keykeeper"],
        "signature": ["breach", "pierce", "armor"],
        "focus": ["breach", "pierce", "armor", "ricochet", "regen"]
    },
    "NETWORK": {
        "name": "NETWORK GRID", "entry_gate": "gate_network", "exit_gate": "exit_network",
        "danger": 5, "feature": "Mixed swarms, projectiles and intrusion",
        "base": 10.0, "per_wave": 4.2, "fallback": "packet_swarm",
        "unlocks": [[1, "packet_swarm"], [2, "trojan"], [3, "rootkit"], [4, "worm"], [4, "overheater"]],
        "boss": "zero_day", "supports": ["packet_swarm", "rootkit", "trojan"],
        "exposure": 0.65, "purify": 12.0,
        "features": ["Packet flood", "Man-in-the-middle", "Remote execution", "Botnet convergence", "ZERO-DAY outbreak"],
        "signature": ["homing", "drone", "rear"],
        "focus": ["homing", "drone", "rear", "sustain", "mana"]
    }
}

static func normalize(sector_id: String) -> String:
    var normalized: String = sector_id.to_upper()
    return normalized if SECTORS.has(normalized) else ""

static func definition(sector_id: String) -> Dictionary:
    var id: String = normalize(sector_id)
    return SECTORS[id].duplicate(true) if not id.is_empty() else {}

static func plan_wave(sector_id: String, wave: int, seed: int = 0) -> Dictionary:
    var id: String = normalize(sector_id)
    if id.is_empty() or wave < 1 or wave > WAVE_LIMIT:
        return {}
    var sector: Dictionary = SECTORS[id]
    var context: HashingContext = HashingContext.new()
    context.start(HashingContext.HASH_SHA256)
    context.update(("%d:%s:%d" % [seed, id, wave]).to_ascii_buffer())
    var random: RefCounted = PythonMT.new()
    random.seed_from_digest(context.finish())
    var schedule: Array[Dictionary] = []
    var boss: bool = wave == 5
    if boss:
        schedule.append({"at": 0.7, "kind": str(sector["boss"])})
        var supports: Array = sector["supports"]
        for i in range(2 + wave / 5):
            var selection: String = str(supports[random.choice_index(supports.size())])
            schedule.append({"at": 2.2 + i * 1.4, "kind": selection})
    else:
        var budget: float = float(sector["base"]) + float(wave) * float(sector["per_wave"])
        var pool: Array[String] = []
        for unlocked in sector["unlocks"]:
            if wave >= int(unlocked[0]):
                pool.append(str(unlocked[1]))
        var at: float = 0.4
        var guard: int = 0
        while budget > 0.0 and guard < 200:
            guard += 1
            var enemy_kind: String = pool[random.choice_index(pool.size())]
            var cost: float = float(COSTS[enemy_kind])
            if cost > budget and enemy_kind != str(sector["fallback"]):
                enemy_kind = str(sector["fallback"])
                cost = float(COSTS[enemy_kind])
            budget -= cost
            schedule.append({"at": at, "kind": enemy_kind})
            at += maxf(0.22, 0.62 - float(wave) * 0.015)
    return {
        "sector_id": id, "wave_index": wave,
        "boss_kind": str(sector["boss"]) if boss else "",
        "objective": "defeat_boss_and_hostiles" if boss else "eliminate_hostiles",
        "feature": sector["features"][wave - 1],
        "danger": mini(5, int(sector["danger"]) + (wave - 1) / 2),
        "exposure_per_second": snappedf(float(sector["exposure"]) * (1.0 + 0.22 * float(wave - 1)), 0.001),
        "purify_on_clear": float(sector["purify"]),
        "spawns": schedule,
        "banner": "WAVE %d  ·  BOSS" % wave if boss else "WAVE %d" % wave,
        "banner_seconds": 2.4 if boss else 1.6
    }
