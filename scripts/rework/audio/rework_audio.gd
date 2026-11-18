extends Node
## A-owned audio feedback for the opt-in rework pilot.
## Reuses the legacy project's original procedural synthesizer (no external
## assets, no copyrighted files) but never touches the classic game's mixer,
## autoloads or save data. Missing/failed audio must never block gameplay.
const Synth = preload("res://scripts/audio/legacy_synth.gd")
const VOICES: int = 10

var streams: Dictionary = {}
var voices: Array[AudioStreamPlayer] = []
var _next_voice: int = 0
var _built: bool = false
var _muted: bool = false
var _silent: bool = false

func _ready() -> void:
    name = "ReworkAudio"
    process_mode = Node.PROCESS_MODE_ALWAYS
    # Headless CI has no audio device; starting a playback there only leaves
    # leaked AudioStreamPlayback objects at exit. Tests never assert sound.
    _silent = DisplayServer.get_name() == "headless"
    for index in range(VOICES):
        var player := AudioStreamPlayer.new()
        player.name = "Voice" + str(index)
        player.process_mode = Node.PROCESS_MODE_ALWAYS
        add_child(player)
        voices.append(player)

func _exit_tree() -> void:
    ## Release the synthesizer streams explicitly. Leaving a playing voice in
    ## the AudioServer makes headless runs report leaked playback resources.
    for player in voices:
        if is_instance_valid(player):
            player.stop()
            player.stream = null
    streams.clear()
    voices.clear()

func play(effect: String) -> void:
    ## Unknown effects are ignored instead of falling back to a random sound.
    if _silent or _muted or voices.is_empty():
        return
    if not _built:
        _build()
    if not streams.has(effect):
        return
    var entry: Dictionary = streams[effect]
    var player: AudioStreamPlayer = voices[_next_voice]
    _next_voice = (_next_voice + 1) % voices.size()
    player.stream = entry["stream"]
    player.volume_db = linear_to_db(maxf(0.0001, float(entry["gain"])))
    player.play()

func set_muted(value: bool) -> void:
    _muted = value
    if value:
        for player in voices:
            player.stop()

func _build() -> void:
    _built = true
    streams = Synth.build_effects()

## Single mapping point so gameplay code only emits facts, never audio names.
func play_notice(notice: Dictionary) -> void:
    var kind: String = str(notice.get("kind", ""))
    match kind:
        "projectile_spawn":
            play("shoot" if str(notice.get("team", "")) == "vaccine" else "eshoot")
        "hit":
            play("hurt" if str(notice.get("target_id", "")) == "VACCINE" else "hit")
        "death":
            play("dead" if str(notice.get("actor_id", "")) == "VACCINE" else "kill")
        "dash", "blink":
            play("dash")
        "skill_cast":
            play("wave")
        "skill_resolved", "skill_impact":
            play("combo")
        "skill_heal", "field_recovery":
            play("up")
        "boss_pattern_telegraph":
            play("phase")
        "boss_pattern":
            play("boss")
        "shield_absorbed":
            play("hit")
        _:
            pass
