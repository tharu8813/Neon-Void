extends RefCounted
## Strict, read-only validation of the *Python* save formats at the P0 baseline.
## Godot JSON stores numbers as floating-point: reject values outside 2^53-1.
const MAX_SAFE_INT: float = 9007199254740991.0
const SOFTWARE_CAPS := {
    "rapid": 6, "power": 8, "pierce": 3, "split": 3,
    "homing": 3, "boom": 3, "crit": 5, "vamp": 3,
    "thorn": 3, "dash2": 3, "dashb": 3, "slow": 3,
    "magnet": 3, "drone": 3, "chain": 2, "rear": 1,
    "hull": 4, "ricochet": 2, "armor": 5, "breach": 5,
    "mana": 4, "regen": 5, "burn": 3, "toxin": 3,
    "leech": 3, "focus": 4, "cool": 4, "eco": 4,
    "sustain": 4, "duration": 3
}
const SECTORS := ["cpu_core", "memory_sector", "storage_vault", "network_grid"]
const ZONES := ["CPU", "MEMORY", "STORAGE", "NETWORK"]
const BINDINGS := ["up", "down", "left", "right", "dash", "pause", "skill_pulse", "skill_guard", "interact"]
const DEFAULT_KEYS := {"up":"w", "down":"s", "left":"a", "right":"d", "dash":"space", "pause":"p", "skill_pulse":"q", "skill_guard":"e", "interact":"f"}
const CHECKPOINT_KEYS := ["schema_version", "revision", "campaign", "scene", "sector",
    "hub_position", "player_position", "deployed_level", "player", "software_levels",
    "pending_cards", "infection", "purify_uses", "salvage_claimed", "score",
    "kills", "combo", "run_kills", "run_boss_kills", "run_best_combo", "elapsed_frames"]

static func _integer(value: Variant, low: float = 0.0, high: float = MAX_SAFE_INT) -> bool:
    if typeof(value) != TYPE_INT and typeof(value) != TYPE_FLOAT:
        return false
    var number := float(value)
    return is_finite(number) and number == floor(number) and number >= low and number <= high

static func _number(value: Variant, low: float, high: float) -> bool:
    if typeof(value) != TYPE_INT and typeof(value) != TYPE_FLOAT:
        return false
    return is_finite(float(value)) and float(value) >= low and float(value) <= high

static func _has_exact_keys(obj: Dictionary, keys: Array) -> bool:
    if obj.size() != keys.size():
        return false
    for key in keys:
        if not obj.has(key):
            return false
    return true

static func _id(value: Variant, max_len: int, allow_colon: bool = false) -> bool:
    if typeof(value) != TYPE_STRING or value.length() == 0 or value.length() > max_len:
        return false
    for i in value.length():
        var c: String = value.substr(i, 1)
        if not ((c >= "a" and c <= "z") or (c >= "A" and c <= "Z")
                or (c >= "0" and c <= "9") or c in ["_", "-", "."]
                or (allow_colon and c == ":")):
            return false
    return true

static func _software_map(value: Variant, allow_zero: bool = false) -> bool:
    if typeof(value) != TYPE_DICTIONARY:
        return false
    for key in value:
        if not SOFTWARE_CAPS.has(key):
            return false
        if not _integer(value[key], 0 if allow_zero else 1, SOFTWARE_CAPS[key]):
            return false
    return true

static func _level(xp: float) -> int:
    var rem := xp
    var level := 1
    while level < 30 and rem >= 100 + 50 * (level - 1):
        rem -= 100 + 50 * (level - 1)
        level += 1
    return level

static func validate(filename: String, obj: Variant) -> Dictionary:
    if typeof(obj) != TYPE_DICTIONARY:
        return {"ok": false, "reason": "root_not_object"}
    var valid := false
    match filename:
        "save.json": valid = _save(obj)
        "settings.json": valid = _settings(obj)
        "profile.json": valid = _profile(obj)
        "antivirus_db.json": valid = _database(obj)
        "os_checkpoint.json": valid = _checkpoint(obj)
        _: return {"ok": false, "reason": "unknown_file"}
    return {"ok": valid, "reason": "valid" if valid else "invalid_or_unsupported_schema"}

static func _legacy_counter(value: Variant) -> bool:
    # Python save.json calls int(...), including for numeric strings and
    # finite fractional numbers. Keep the same nonnegative integer outcome.
    if typeof(value) == TYPE_STRING:
        var trimmed: String = value.strip_edges()
        if not trimmed.is_valid_int() or trimmed.length() > 16:
            return false
        var number: int = trimmed.to_int()
        return number >= 0 and float(number) <= MAX_SAFE_INT
    if typeof(value) != TYPE_INT and typeof(value) != TYPE_FLOAT:
        return false
    return is_finite(float(value)) and float(value) >= 0.0 and float(value) <= MAX_SAFE_INT

static func _save(obj: Dictionary) -> bool:
    if obj.has("schema_version") and not _integer(obj["schema_version"], 1, 1):
        return false
    for key in ["best_score", "best_wave", "runs"]:
        if not _legacy_counter(obj.get(key, 0)):
            return false
    return true

static func _keys_valid(bindings: Variant, count: int) -> bool:
    if typeof(bindings) != TYPE_DICTIONARY or bindings.size() != count:
        return false
    var used := {}
    for i in count:
        var action: String = BINDINGS[i]
        if not bindings.has(action) or typeof(bindings[action]) != TYPE_STRING:
            return false
        var key: String = bindings[action].strip_edges().to_lower()
        if key != "space" and (key.length() != 1 or not (key >= "a" and key <= "z")):
            return false
        if key in ["m", "r", "o"] or used.has(key):
            return false
        used[key] = true
    return true

static func _settings(obj: Dictionary) -> bool:
    var version: Variant = obj.get("schema_version", 1)
    if not _integer(version, 1, 5):
        return false
    if not _number(obj.get("volume", 0.9), -INF, INF):
        return false
    for key in ["muted", "show_fps", "reduced_effects"]:
        if typeof(obj.get(key, false)) != TYPE_BOOL:
            return false
    if not obj.get("display_mode", "windowed") in ["windowed", "fullscreen", "borderless"]:
        return false
    if not _integer(obj.get("window_width", 1280), 720, 7680) or not _integer(obj.get("window_height", 720), 405, 4320):
        return false
    if int(version) == 1:
        return true
    return _keys_valid(obj.get("bindings"), 6 if int(version) == 2 else (8 if int(version) == 3 else 9))

static func _profile(obj: Dictionary) -> bool:
    if not _integer(obj.get("schema_version"), 1, 2):
        return false
    for key in ["total_kills", "boss_kills", "total_score", "completed_runs", "abandoned_runs", "extracted_runs", "best_combo"]:
        if not _integer(obj.get(key, 0)):
            return false
    var recent: Variant = obj.get("recent_runs", [])
    if typeof(recent) != TYPE_ARRAY or recent.size() > 20:
        return false
    for run in recent:
        if typeof(run) != TYPE_DICTIONARY or not run.get("status", "dead") in ["dead", "abandoned", "extracted"]:
            return false
        for key in ["score", "wave", "kills", "boss_kills", "best_combo"]:
            if not _integer(run.get(key, 0)):
                return false
        if not _number(run.get("duration_seconds", 0.0), 0.0, MAX_SAFE_INT):
            return false
    return true

static func _database(obj: Dictionary) -> bool:
    if not _has_exact_keys(obj, ["schema_version", "antivirus_level", "xp", "software_archive",
            "awarded_xp_ids", "processed_extractions", "closed_runs"]):
        return false
    if not _integer(obj["schema_version"], 1, 1) or not _integer(obj["xp"]):
        return false
    if not _integer(obj["antivirus_level"], 1, 30) or int(obj["antivirus_level"]) != _level(float(obj["xp"])):
        return false
    if not _software_map(obj["software_archive"]):
        return false
    var receipts: Variant = obj["awarded_xp_ids"]
    var extracted: Variant = obj["processed_extractions"]
    var closed: Variant = obj["closed_runs"]
    if typeof(receipts) != TYPE_DICTIONARY or typeof(extracted) != TYPE_DICTIONARY or typeof(closed) != TYPE_DICTIONARY:
        return false
    var total := 0.0
    for key in receipts:
        if not _id(key, 192, true) or not key.contains(":") or not _id(key.get_slice(":", 0), 128):
            return false
        if not _integer(receipts[key], 1):
            return false
        total += float(receipts[key])
        if total > MAX_SAFE_INT:
            return false
    if total != float(obj["xp"]):
        return false
    for run_id in closed:
        if not _id(run_id, 128) or not closed[run_id] in ["dead", "abandoned", "extracted"]:
            return false
        if (closed[run_id] == "extracted") != extracted.has(run_id):
            return false
    for run_id in extracted:
        if not _id(run_id, 128) or not SOFTWARE_CAPS.has(extracted[run_id]):
            return false
        if closed.get(run_id) != "extracted":
            return false
    return true

static func _position(value: Variant, x: float, y: float) -> bool:
    return typeof(value) == TYPE_ARRAY and value.size() == 2 and _number(value[0], 0, x) and _number(value[1], 0, y)

static func _checkpoint(obj: Dictionary) -> bool:
    if not _has_exact_keys(obj, CHECKPOINT_KEYS):
        return false
    if not _integer(obj["schema_version"], 1, 1) or not _integer(obj["revision"], 1, 1000000000):
        return false
    var scene: Variant = obj["scene"]
    var sector: Variant = obj["sector"]
    if not scene in ["hub", "play", "upgrade", "dead"]:
        return false
    if scene == "hub":
        if sector != null:
            return false
    elif not sector in ["cpu", "memory", "storage", "network"]:
        return false
    if not _position(obj["hub_position"], 2400, 1560):
        return false
    if not _position(obj["player_position"], 2400 if scene == "hub" else 1280, 1560 if scene == "hub" else 720):
        return false
    if not _integer(obj["deployed_level"], 1, 30) or not _software_map(obj["software_levels"]):
        return false
    var campaign: Variant = obj["campaign"]
    if typeof(campaign) != TYPE_DICTIONARY or not _has_exact_keys(campaign, ["run_id", "seed", "completed_waves"]):
        return false
    if not _id(campaign["run_id"], 128) or not _integer(campaign["seed"], -MAX_SAFE_INT, MAX_SAFE_INT):
        return false
    var waves: Variant = campaign["completed_waves"]
    if typeof(waves) != TYPE_DICTIONARY or not _has_exact_keys(waves, ZONES):
        return false
    for zone in ZONES:
        if not _integer(waves[zone], 0, 5):
            return false
    var cards: Variant = obj["pending_cards"]
    if typeof(cards) != TYPE_ARRAY or cards.size() > 3:
        return false
    if scene == "upgrade" and (cards.size() != 3 or not _integer(waves[sector.to_upper()], 1, 4)):
        return false
    if scene != "upgrade" and not cards.is_empty():
        return false
    if scene == "upgrade":
        for card in cards:
            if not SOFTWARE_CAPS.has(card) and card != "bonus":
                return false
    if scene == "upgrade" and ZONES.all(func(z): return int(waves[z]) == 5):
        return false
    var player: Variant = obj["player"]
    if typeof(player) != TYPE_DICTIONARY or not _has_exact_keys(player, ["hp", "mana", "charges", "fire_cd", "pulse_cd", "guard_cd", "guard_t"]):
        return false
    var advances: float = float(obj["deployed_level"]) - 1.0
    var hp_cap: float = 100 + 2 * advances + 20 * int(obj["software_levels"].get("hull", 0))
    var mana_cap: float = 100 + advances + 25 * int(obj["software_levels"].get("mana", 0))
    if not _number(player["hp"], 0 if scene == "dead" else 0.001, hp_cap) or not _number(player["mana"], 0, mana_cap):
        return false
    if not _integer(player["charges"], 0, 1 + int(obj["software_levels"].get("dash2", 0))):
        return false
    for key in ["fire_cd", "pulse_cd", "guard_cd", "guard_t"]:
        if not _number(player[key], 0, 3600):
            return false
    var infection: Variant = obj["infection"]
    if typeof(infection) != TYPE_DICTIONARY or not _has_exact_keys(infection, ["player_infection", "os_integrity"]):
        return false
    if not _number(infection["player_infection"], 0, 100) or not _number(infection["os_integrity"], 0, 100):
        return false
    if not _integer(obj["purify_uses"], 0, 3) or typeof(obj["salvage_claimed"]) != TYPE_BOOL:
        return false
    for key in ["score", "kills", "combo", "run_kills", "run_boss_kills", "run_best_combo", "elapsed_frames"]:
        if not _integer(obj[key], 0, 1000000000000):
            return false
    return float(obj["run_kills"]) <= float(obj["kills"])

## Convert supported historical forms to the current Godot-facing Python
## schema without altering the original object or its source bytes.
static func convert(filename: String, obj: Dictionary) -> Dictionary:
    if not validate(filename, obj).ok:
        return {"ok": false, "reason": "invalid_source"}
    if filename == "save.json":
        return {"ok": true, "value": {"best_score": int(obj.get("best_score", 0)),
            "best_wave": int(obj.get("best_wave", 0)), "runs": int(obj.get("runs", 0))}}
    if filename == "profile.json":
        var normalized := {"schema_version": 2}
        for key in ["total_kills", "boss_kills", "total_score", "completed_runs", "abandoned_runs", "extracted_runs", "best_combo"]:
            normalized[key] = int(obj.get(key, 0))
        normalized["recent_runs"] = []
        for run in obj.get("recent_runs", []):
            var record := {"status": run.get("status", "dead"),
                "duration_seconds": float(run.get("duration_seconds", 0.0))}
            for key in ["score", "wave", "kills", "boss_kills", "best_combo"]:
                record[key] = int(run.get(key, 0))
            normalized["recent_runs"].append(record)
        return {"ok": true, "value": normalized}
    if filename == "settings.json":
        var version: int = int(obj.get("schema_version", 1))
        var controls: Dictionary = DEFAULT_KEYS.duplicate(true)
        if version >= 2:
            controls = {}
            for i in (6 if version == 2 else (8 if version == 3 else 9)):
                var action: String = BINDINGS[i]
                controls[action] = str(obj.bindings[action]).to_lower().strip_edges()
            if version == 2:
                for action in ["skill_pulse", "skill_guard"]:
                    var preferred := "q" if action == "skill_pulse" else "e"
                    for key in [preferred, "f", "g", "h", "j", "k", "l", "z", "x", "c"]:
                        if not controls.values().has(key):
                            controls[action] = key
                            break
                    if not controls.has(action):
                        return {"ok": false, "reason": "no_free_legacy_skill_binding"}
            if version < 4:
                for key in ["f", "g", "h", "j", "k", "l", "z", "x", "c", "v", "b"]:
                    if not controls.values().has(key):
                        controls["interact"] = key
                        break
                if not controls.has("interact"):
                    return {"ok": false, "reason": "no_free_interact_binding"}
        if not _keys_valid(controls, 9):
            return {"ok": false, "reason": "normalized_bindings_invalid"}
        return {"ok": true, "value": {
            "schema_version": 5, "volume": clampf(float(obj.get("volume", 0.9)), 0.0, 1.0),
            "muted": obj.get("muted", false), "show_fps": obj.get("show_fps", false),
            "reduced_effects": obj.get("reduced_effects", false),
            "display_mode": obj.get("display_mode", "windowed"),
            "window_width": int(obj.get("window_width", 1280)),
            "window_height": int(obj.get("window_height", 720)), "bindings": controls}}
    return {"ok": true, "value": obj.duplicate(true)}
