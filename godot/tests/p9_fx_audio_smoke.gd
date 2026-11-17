extends SceneTree
## Headless structural/equivalence tests only. They cannot prove speaker audio,
## physical GPU acceleration, visible Windows screenshots or frame timing.
const Main = preload("res://scenes/main.tscn")
const Synth = preload("res://scripts/audio/legacy_synth.gd")
const Catalog = preload("res://scripts/enemies/enemy_catalog.gd")
var failures: int = 0

func _initialize() -> void:
    create_timer(55.0).timeout.connect(func(): printerr("P9 FX AUDIO TIMEOUT"); quit(1))
    call_deferred("_run")

func require(ok: bool, why: String) -> void:
    if not ok:
        failures += 1
        printerr("P9 FX AUDIO FAIL: " + why)

func _run() -> void:
    var game: Node3D = Main.instantiate()
    root.add_child(game)
    var fx: Node3D = game.get_node("CombatFX")
    var audio: Node = game.get_node("LegacyAudio")
    require(fx != null and audio != null, "visual/audio channels present in main")
    var clips: Dictionary = audio.get("effects")
    require(clips.size() == 16, "all 16 original Python tone recipes")
    for name in Synth.EFFECT_GAINS:
        require(clips.has(name), "known Python audio cue: " + name)
        if not clips.has(name):
            continue
        var clip: Dictionary = clips[name]
        var stream: AudioStreamWAV = clip["stream"]
        require(stream.format == AudioStreamWAV.FORMAT_16_BITS and stream.mix_rate == 22050
            and not stream.stereo and stream.data.size() > 100,
            "22.05kHz mono 16-bit generated wave: " + name)
        require(is_equal_approx(float(clip["gain"]), float(Synth.EFFECT_GAINS[name])),
            "original Python mixer voice gain: " + name)
    var soundtrack: AudioStreamWAV = audio.music_player.stream
    require(soundtrack.mix_rate == 8000 and soundtrack.loop_mode == AudioStreamWAV.LOOP_FORWARD,
        "original four-bar synthesizer music with a loop")
    require(soundtrack.data.size() >= 150000 and soundtrack.data.size() <= 155000,
        "music is the original 9.6-second 100 BPM 4-bar form")
    require(not audio.get("playback_enabled"), "headless never claims native audio output")
    audio.call("apply_settings", 0.9, true)
    require(not audio.call("play_effect", "shoot"), "mute prevents effect voice")
    audio.call("apply_settings", 0.9, false)
    var played: bool = audio.call("play_effect", "shoot", 0.05)
    require(played and not audio.call("play_effect", "shoot", 0.05),
        "shot SFX accepted once then rate-limited")
    require(not audio.call("play_effect", "made_up_effect"),
        "unknown effects cannot consume voice rate limit")

    require(game.call("start_new_run"), "synthetic clean OS deployment")
    require(game.call("enter_sector", "CPU"), "combat scene starts unchanged")
    require(fx.visible, "3D FX visible in combat only")
    var sim: RefCounted = game.get("sim")
    var id: int = sim.spawn_enemy("rootkit", Vector2(800, 300))
    var enemy: Dictionary = sim.get_enemy(id)
    var bullets_snapshot: Array = [
        {"pos": Vector2(500, 320), "vel": Vector2(900, 0.0)},
        {"pos": Vector2(580, 240), "vel": Vector2(0, -880)}
    ]
    var hostile_snapshot: Array = [
        {"pos": Vector2(900, 460), "vel": Vector2(-360, 170)}
    ]
    var warning_snapshot: Array = [
        {"remaining": 0.12, "source_id": id,
            "action": {"behavior": "encryption_pulse"}},
    ]
    var original_enemy: Dictionary = enemy.duplicate(true)
    var original_friendly: Array = bullets_snapshot.duplicate(true)
    var original_warning: Array = warning_snapshot.duplicate(true)
    fx.call("sync_battle", bullets_snapshot, hostile_snapshot, warning_snapshot, [enemy], false)
    require(fx.friendly_trails.multimesh.visible_instance_count == 2 and
        fx.hostile_trails.multimesh.visible_instance_count == 1,
        "GPU MultiMesh batch counts follow snapshots")
    require(int(fx.get("last_warning_count")) == 1 and fx.warning_pool[0].visible,
        "infection virus attack telegraph visible")
    require(is_equal_approx(fx.warning_pool[0].scale.x, Catalog.PULSE_RADIUS * 0.01),
        "encryption pulse warning uses C's true attack radius")
    require(fx.warning_pool[0].material_override == fx.warning_danger_material,
        "imminent warning appears danger red")
    require(enemy == original_enemy and bullets_snapshot == original_friendly
        and warning_snapshot == original_warning,
        "FX never writes back to simulation snapshots")

    fx.call("sync_battle", bullets_snapshot, hostile_snapshot, warning_snapshot, [enemy], true)
    require(fx.friendly_trails.multimesh.visible_instance_count == 0 and
        fx.warning_pool[0].visible,
        "reduced effects hides streaks but preserves mechanical warning")
    fx.call("add_burst", Vector2(500, 320), "dash")
    require(fx.bursts.size() == 1, "visual dash burst is queued")
    for i in range(55):
        fx.call("sync_battle", [], [], [], [], true)
    require(fx.bursts.is_empty() and fx.last_warning_count == 0,
        "effect rings expire and warnings clear without modifying C")
    game.call("retreat_to_hub")
    require(fx.visible and fx.friendly_trails.multimesh.visible_instance_count == 0
        and fx.hostile_trails.multimesh.visible_instance_count == 0,
        "OS hub retains only safe purification feedback, no combat bullet FX")
    require(game.get_node("LegacySectorExit").visible == false,
        "Python sector EXIT is hidden outside combat arena")
    game.queue_free()
    if failures == 0:
        print("P9 3D FX / PYTHON ORIGINAL AUDIO PARITY PASS (headless structural only)")
    quit(1 if failures > 0 else 0)
