extends Node
## P9 audio facade: original Python procedural sounds in Godot native PCM.
## Rendering/playback is strictly observational, with no game state mutations.
const Synth = preload("res://scripts/audio/legacy_synth.gd")
const SFX_VOICES: int = 12
var effects: Dictionary = {}
var players: Array[AudioStreamPlayer] = []
var music_player: AudioStreamPlayer
var last_played: Dictionary = {}
var accepted_count: int = 0
var muted: bool = false
var playback_enabled: bool = false

func _ready() -> void:
    playback_enabled = DisplayServer.get_name() != "headless"
    effects = Synth.build_effects()
    for i in range(SFX_VOICES):
        var node := AudioStreamPlayer.new()
        node.name = "SFXVoice%d" % i
        node.bus = "Master"
        add_child(node)
        players.append(node)
    music_player = AudioStreamPlayer.new()
    music_player.name = "OriginalMusic"
    music_player.bus = "Master"
    music_player.stream = Synth.build_music()
    music_player.volume_db = linear_to_db(0.30)
    add_child(music_player)
    # The source Python game starts music after Audio initialization.
    if playback_enabled and not muted:
        music_player.play()

func apply_settings(volume: float, mute: bool) -> void:
    # A already owns the Godot Master bus level/mute persistence. This facade
    # only stops effects on mute; do not stack duplicate volume attenuation.
    muted = mute
    if mute:
        for voice in players:
            voice.stop()
    if playback_enabled and not mute and not music_player.playing:
        music_player.play()

func play_effect(name: String, minimum_gap: float = 0.05) -> bool:
    if muted or not effects.has(name):
        return false
    var now: float = float(Time.get_ticks_usec()) / 1000000.0
    if now - float(last_played.get(name, -1000.0)) < minimum_gap:
        return false
    var available: AudioStreamPlayer = null
    for voice in players:
        if not voice.playing:
            available = voice
            break
    if available == null:
        return false
    last_played[name] = now
    accepted_count += 1
    if playback_enabled:
        var definition: Dictionary = effects[name]
        available.stream = definition["stream"]
        available.volume_db = linear_to_db(float(definition["gain"]))
        available.play()
    return true

func stop_all() -> void:
    for voice in players:
        voice.stop()
    if music_player != null:
        music_player.stop()
