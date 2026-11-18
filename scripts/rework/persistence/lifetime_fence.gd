extends RefCounted
## Write-ahead monotonic lifetime fence.
## Prevents explicit backup recovery from refunding consumed clones or resurrecting
## a destroyed vaccine after a damaged primary. Never inferred from legacy saves.
const Model = preload("res://scripts/rework/progression/state_model.gd")
const FILENAME: String = "lifetime_fence.json"
const FORMAT: int = 1

static func from_state(state: Dictionary) -> Dictionary:
    if not Model.validate(state):
        return {}
    var fence := {
        "format": FORMAT, "generation": int(state.generation),
        "vaccine_id": "", "min_copies_remaining": 0,
        "terminal": true
    }
    if state.vaccine != null:
        fence.vaccine_id = str(state.vaccine.vaccine_id)
        fence.min_copies_remaining = int(state.vaccine.copies_remaining)
        fence.terminal = false
    return fence

static func valid(fence: Variant) -> bool:
    if typeof(fence) != TYPE_DICTIONARY or fence.size() != 5:
        return false
    for key in ["format", "generation", "vaccine_id", "min_copies_remaining", "terminal"]:
        if not fence.has(key):
            return false
    return Model.natural(fence.format, 1) and int(fence.format) == FORMAT \
        and Model.natural(fence.generation, 1000000000) and fence.generation >= 1 \
        and typeof(fence.vaccine_id) == TYPE_STRING \
        and (fence.vaccine_id.is_empty() or Model.valid_id(fence.vaccine_id)) \
        and Model.natural(fence.min_copies_remaining, 3) \
        and typeof(fence.terminal) == TYPE_BOOL \
        and ((fence.terminal and int(fence.min_copies_remaining) == 0) \
            or (not fence.terminal and Model.valid_id(fence.vaccine_id)))

static func allows(state: Dictionary, fence: Dictionary) -> bool:
    if not Model.validate(state) or not valid(fence) \
            or int(state.generation) != int(fence.generation):
        return false
    if state.vaccine == null:
        return fence.terminal
    return not fence.terminal and str(state.vaccine.vaccine_id) == str(fence.vaccine_id) \
        and int(state.vaccine.copies_remaining) <= int(fence.min_copies_remaining)

static func next_fence(before: Dictionary, after: Dictionary,
        existing: Dictionary) -> Dictionary:
    if not allows(before, existing) or not Model.validate(after):
        return {}
    var candidate: Dictionary = existing.duplicate(true)
    if before.vaccine == null and after.vaccine != null:
        candidate = from_state(after)
    elif before.vaccine != null and after.vaccine == null:
        candidate.terminal = true
        candidate.min_copies_remaining = 0
    elif before.vaccine != null and after.vaccine != null:
        candidate.min_copies_remaining = mini(int(candidate.min_copies_remaining),
            int(after.vaccine.copies_remaining))
    if not valid(candidate) or not allows(after, candidate):
        return {}
    return candidate

static func parse(data: PackedByteArray) -> Dictionary:
    if data.is_empty() or data.size() > 4096:
        return {"ok": false, "reason": "fence_invalid_size"}
    var source: String = data.get_string_from_utf8()
    if source.to_utf8_buffer() != data:
        return {"ok": false, "reason": "fence_bad_utf8"}
    var parser := JSON.new()
    if parser.parse(source) != OK or not valid(parser.data):
        return {"ok": false, "reason": "fence_invalid"}
    return {"ok": true, "fence": parser.data}

static func encode(fence: Dictionary) -> PackedByteArray:
    if not valid(fence):
        return PackedByteArray()
    return (JSON.stringify(fence, "", true, true) + "\n").to_utf8_buffer()
