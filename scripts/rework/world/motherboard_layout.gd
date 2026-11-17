extends RefCounted
## Dispatch motherboard: B confirms the three weak intro enemies,
## A loads hardware scenes; E never receives fake reward events.
signal board_event(event: Dictionary)
const GATES := {
    "CPU": {"position": Vector3(14.0, 0.0, 3.0), "radius": 2.0},
    "GPU": {"position": Vector3(24.0, 0.0, 8.0), "radius": 2.0},
    "RAM": {"position": Vector3(14.0, 0.0, 15.0), "radius": 2.0},
    "SSD": {"position": Vector3(4.0, 0.0, 8.0), "radius": 2.0},
    "HDD": {"position": Vector3(24.0, 0.0, 15.0), "radius": 2.0}
}
const BOUNDS := Rect2(Vector2.ZERO, Vector2(28.0, 19.0))
const SPAWN := Vector3(14.0, 0.0, 9.0)
const INTRO := [
    {"id": "intro-0", "role": "weak_virus", "position": Vector3(12, 0, 7)},
    {"id": "intro-1", "role": "weak_virus", "position": Vector3(16, 0, 7)},
    {"id": "intro-2", "role": "weak_virus", "position": Vector3(14, 0, 11)}
]
var run_id: String = ""
var mission: Dictionary = {}
var intro_defeated: Dictionary = {}
var seen_receipts: Dictionary = {}
var active_region: String = ""

func _init(new_run_id: String = "", new_mission: Dictionary = {}) -> void:
    run_id = new_run_id
    mission = new_mission.duplicate(true)

func intro_remaining() -> int:
    return INTRO.size() - intro_defeated.size()

func gates_unlocked() -> bool:
    return intro_remaining() == 0

func confirm_intro_defeat(enemy_id: String, receipt_id: String) -> bool:
    if receipt_id.is_empty() or seen_receipts.has(receipt_id) or intro_defeated.has(enemy_id):
        return false
    var recognized: bool = false
    for data in INTRO:
        if data["id"] == enemy_id:
            recognized = true
            break
    if not recognized:
        return false
    intro_defeated[enemy_id] = true
    seen_receipts[receipt_id] = true
    board_event.emit({
        "schema": 1, "type": "intro_enemy_defeated", "run_id": run_id,
        "enemy_id": enemy_id, "receipt_id": receipt_id, "remaining": intro_remaining()
    })
    if gates_unlocked():
        board_event.emit({
            "schema": 1, "type": "hardware_gates_unlocked", "run_id": run_id,
            "receipt_id": "world:%s:intro_cleared" % run_id
        })
    return true

func gate_available(region_id: String) -> bool:
    return gates_unlocked() and active_region.is_empty() and GATES.has(region_id) and mission.get("regions", {}).has(region_id)

func request_gate(region_id: String, player_world: Vector3) -> Dictionary:
    if not gate_available(region_id):
        return {}
    var gate: Dictionary = GATES[region_id]
    var anchor: Vector3 = gate["position"]
    if Vector2(player_world.x - anchor.x, player_world.z - anchor.z).length() > float(gate["radius"]):
        return {}
    active_region = region_id
    var request: Dictionary = {
        "schema": 1, "type": "hardware_gate_requested", "run_id": run_id,
        "region_id": region_id, "region_plan": mission["regions"][region_id].duplicate(true)
    }
    board_event.emit(request.duplicate(true))
    return request

func return_to_board(region_id: String) -> bool:
    if region_id.is_empty() or active_region != region_id:
        return false
    active_region = ""
    board_event.emit({"schema": 1, "type": "returned_to_motherboard", "run_id": run_id, "region_id": region_id})
    return true
