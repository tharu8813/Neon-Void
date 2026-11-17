extends RefCounted
## P5 four independent OS sectors. No disk writes, reward XP, or UI ownership.
## Each confirmed wave is idempotent. Incomplete waves are retried after retreat.
signal sector_entered(sector_id: String, completed: int)
signal wave_started(sector_id: String, wave_index: int)
signal wave_cleared(sector_id: String, wave_index: int, token: String)
signal sector_cleared(sector_id: String)
signal os_cleaned()

const Catalog = preload("res://scripts/sectors/sector_catalog.gd")
const SPREADING: float = 50.0
const CRITICAL: float = 75.0
var run_id: String
var seed_value: int
var progress: Dictionary = {}
var active_sector: String = ""
var active_wave: int = 0
var encounter: Dictionary = {}
var infection: float = 0.0
var integrity: float = 100.0
var os_clean_emitted: bool = false
var receipts: Dictionary = {}
var failed: bool = false

func _init(p_run_id: String = "test-os", p_seed: int = 0) -> void:
    assert(not p_run_id.is_empty(), "run_id is mandatory")
    run_id = p_run_id
    seed_value = p_seed
    for id in Catalog.IDS:
        progress[id] = 0

func is_sector_clean(id: String) -> bool:
    var sector: String = Catalog.normalize(id)
    return not sector.is_empty() and int(progress[sector]) == 5

func is_os_clean() -> bool:
    for id in Catalog.IDS:
        if not is_sector_clean(id):
            return false
    return true

func enter_sector(sector_id: String) -> bool:
    var id: String = Catalog.normalize(sector_id)
    if id.is_empty() or failed:
        return false
    if not active_sector.is_empty() and active_sector != id:
        retreat()
    active_sector = id
    sector_entered.emit(id, int(progress[id]))
    return true

func start_wave() -> Dictionary:
    if active_sector.is_empty() or active_wave > 0 or is_sector_clean(active_sector) or failed:
        return {}
    active_wave = int(progress[active_sector]) + 1
    encounter = Catalog.plan_wave(active_sector, active_wave, seed_value)
    wave_started.emit(active_sector, active_wave)
    return encounter.duplicate(true)

func confirm_wave(hostiles_remaining: int, pending_spawns: int, boss_defeated: bool) -> bool:
    ## Called after CombatSim.wave_ready; never trust a visual "boss defeated"
    ## without checking both surviving hostiles and pending spawn entries.
    if active_sector.is_empty() or active_wave == 0:
        return false
    if hostiles_remaining != 0 or pending_spawns != 0:
        return false
    if active_wave == 5 and not boss_defeated:
        return false
    var token: String = "%s:%s:%d" % [run_id, active_sector, active_wave]
    if receipts.has(token):
        return false
    receipts[token] = true
    progress[active_sector] = active_wave
    var cleared_id: String = active_sector
    var cleared_wave: int = active_wave
    var purification: float = float(encounter.get("purify_on_clear", 0.0))
    infection = maxf(0.0, infection - purification)
    active_wave = 0
    encounter = {}
    wave_cleared.emit(cleared_id, cleared_wave, token)
    if is_sector_clean(cleared_id):
        sector_cleared.emit(cleared_id)
    if is_os_clean() and not os_clean_emitted:
        os_clean_emitted = true
        os_cleaned.emit()
    return true

func retreat() -> void:
    ## An active wave has NO confirmed progress or reward.
    active_sector = ""
    active_wave = 0
    encounter = {}

func infection_stage() -> int:
    if infection >= 75.0:
        return 3
    if infection >= 50.0:
        return 2
    if infection >= 25.0:
        return 1
    return 0

func needs_purification() -> bool:
    return infection_stage() >= 2

func apply_infection(amount: float, paused: bool = false) -> float:
    if paused or amount < 0.0:
        return 0.0
    var previous: float = infection
    infection = clampf(infection + amount, 0.0, 100.0)
    return infection - previous

func purify(amount: float, paused: bool = false) -> float:
    if paused or amount < 0.0:
        return 0.0
    var previous: float = infection
    infection = clampf(infection - amount, 0.0, 100.0)
    return previous - infection

func repair_integrity(amount: float, paused: bool = false) -> float:
    if paused or amount < 0.0:
        return 0.0
    var previous: float = integrity
    integrity = clampf(integrity + amount, 0.0, 100.0)
    return integrity - previous

static func seconds_above(start: float, end: float, threshold: float, dt: float) -> float:
    if dt == 0.0:
        return 0.0
    if start >= threshold and end >= threshold:
        return dt
    if start <= threshold and end <= threshold:
        return 0.0
    if is_equal_approx(start, end):
        return dt if start >= threshold else 0.0
    var crossing: float = clampf(dt * (threshold - start) / (end - start), 0.0, dt)
    return dt - crossing if end > start else crossing

func tick_infection(dt: float = 1.0 / 60.0, paused: bool = false, purifier_rate: float = 0.0) -> Dictionary:
    if paused or dt <= 0.0:
        return snapshot_infection()
    var exposure: float = float(encounter.get("exposure_per_second", 0.0)) if active_wave > 0 else 0.0
    var start: float = infection
    var end: float = start + (exposure - maxf(0.0, purifier_rate)) * dt
    var high_time: float = seconds_above(start, end, SPREADING, dt)
    var critical_time: float = seconds_above(start, end, CRITICAL, dt)
    var loss: float = 0.30 * high_time + 0.70 * critical_time
    infection = clampf(end, 0.0, 100.0)
    integrity = maxf(0.0, integrity - loss)
    # Integrity loss is a world exit trigger, never immediate death.
    failed = integrity <= 0.0
    return snapshot_infection()

func snapshot_infection() -> Dictionary:
    return {
        "player_infection": infection, "os_integrity": integrity,
        "stage": infection_stage(), "needs_purification": needs_purification(),
        "emergency_exit": failed
    }

func checkpoint() -> Dictionary:
    ## Mirrors Python campaign progress, adds no live wave state.
    ## The P8 serializer owns durable schema and receipt persistence.
    return {
        "run_id": run_id, "seed": seed_value,
        "completed_waves": progress.duplicate(true),
        "infection": infection, "integrity": integrity,
        "receipts": receipts.duplicate(true)
    }

static func from_checkpoint(data: Dictionary) -> RefCounted:
    if not data.has("run_id") or not data.has("seed") or not data.has("completed_waves"):
        return null
    if not (data["run_id"] is String) or str(data["run_id"]).is_empty() or not (data["seed"] is int):
        return null
    var counts: Variant = data["completed_waves"]
    if not counts is Dictionary or counts.size() != 4:
        return null
    var restored = load("res://scripts/sectors/sector_campaign.gd").new(str(data["run_id"]), int(data["seed"]))
    for id in Catalog.IDS:
        if not counts.has(id) or not counts[id] is int or int(counts[id]) < 0 or int(counts[id]) > 5:
            return null
        restored.progress[id] = int(counts[id])
        for n in range(1, int(counts[id]) + 1):
            restored.receipts["%s:%s:%d" % [restored.run_id, id, n]] = true
    var infection_value: float = float(data.get("infection", 0.0))
    var integrity_value: float = float(data.get("integrity", 100.0))
    if not is_finite(infection_value) or not is_finite(integrity_value) or infection_value < 0.0 or infection_value > 100.0 or integrity_value < 0.0 or integrity_value > 100.0:
        return null
    restored.infection = infection_value
    restored.integrity = integrity_value
    restored.failed = integrity_value <= 0.0
    restored.os_clean_emitted = restored.is_os_clean()
    return restored
