extends RefCounted
## Port of neonvoid/audio.py's original procedural tone/noise recipes.
## No external sound files or copyrighted assets. 16 effect waveforms are
## sampled once, normalized to the frozen Python 0.85 peak and stored as PCM.
const SR: int = 22050
const MUSIC_SR: int = 8000
const EFFECT_GAINS: Dictionary = {
    "shoot": 0.34, "hit": 0.40, "kill": 0.52, "boom": 0.70,
    "dash": 0.40, "hurt": 0.60, "up": 0.45, "pick": 0.50,
    "boss": 0.60, "dead": 0.65, "eshoot": 0.26, "hshot": 0.34,
    "wave": 0.40, "phase": 0.45, "lowhp": 0.50, "combo": 0.26
}

static func _envelope(i: int, count: int, attack: float, power: float) -> float:
    var ratio: float = float(i) / float(maxi(1, count))
    var fade_in: float = 1.0 if attack <= 0.0 else minf(1.0, ratio / attack)
    return fade_in * pow(1.0 - ratio, power)

static func _tone(f0: float, f1: float, dur: float, kind: String = "square",
        amp: float = 0.22, power: float = 2.0, attack: float = 0.01) -> PackedFloat32Array:
    var n: int = maxi(1, int(SR * dur))
    var out := PackedFloat32Array()
    out.resize(n)
    var phase: float = 0.0
    for i in range(n):
        var ratio: float = float(i) / float(n)
        phase += TAU * lerpf(f0, f1, ratio) / float(SR)
        var signal_value: float = 0.0
        match kind:
            "square":
                signal_value = 1.0 if sin(phase) >= 0.0 else -1.0
            "saw":
                signal_value = 2.0 * fposmod(phase / TAU, 1.0) - 1.0
            "tri":
                signal_value = 2.0 * absf(2.0 * fposmod(phase / TAU, 1.0) - 1.0) - 1.0
            _:
                signal_value = sin(phase)
        out[i] = signal_value * _envelope(i, n, attack, power) * amp
    return out

static func _noise(dur: float, amp: float = 0.2, power: float = 2.0,
        low_pass: float = 0.45, seed_value: int = 1234) -> PackedFloat32Array:
    var n: int = maxi(1, int(SR * dur))
    var out := PackedFloat32Array()
    out.resize(n)
    var rng := RandomNumberGenerator.new()
    rng.seed = seed_value
    var prev: float = 0.0
    for i in range(n):
        prev = prev * (1.0 - low_pass) + rng.randf_range(-1.0, 1.0) * low_pass
        out[i] = prev * _envelope(i, n, 0.004, power) * amp * 2.4
    return out

static func _mix(parts: Array) -> PackedFloat32Array:
    var total: int = 0
    for part in parts:
        total = maxi(total, part.size())
    var result := PackedFloat32Array()
    result.resize(total)
    for part in parts:
        for i in range(part.size()):
            result[i] = clampf(result[i] + float(part[i]), -1.0, 1.0)
    return result

static func _wav(samples: PackedFloat32Array, mix_rate: int = SR,
        looped: bool = false) -> AudioStreamWAV:
    var maximum: float = 0.0
    for sample in samples:
        maximum = maxf(maximum, absf(sample))
    var gain: float = 0.85 / maximum if maximum > 0.0 else 0.0
    var data := PackedByteArray()
    data.resize(samples.size() * 2)
    for i in range(samples.size()):
        var amp: int = int(roundf(clampf(samples[i] * gain, -1.0, 1.0) * 32767.0))
        data[i * 2] = amp & 0xff
        data[i * 2 + 1] = (amp >> 8) & 0xff
    var stream := AudioStreamWAV.new()
    stream.format = AudioStreamWAV.FORMAT_16_BITS
    stream.mix_rate = mix_rate
    stream.stereo = false
    stream.data = data
    if looped:
        stream.loop_mode = AudioStreamWAV.LOOP_FORWARD
        stream.loop_begin = 0
        stream.loop_end = samples.size()
    return stream

static func build_effects() -> Dictionary:
    var samples: Dictionary = {}
    samples["shoot"] = _mix([
        _tone(1250, 640, 0.06, "square", 0.30, 3.0, 0.002),
        _noise(0.05, 0.10, 3.0, 0.75, 7)])
    samples["hit"] = _noise(0.045, 0.30, 3.0, 0.55, 11)
    samples["kill"] = _mix([
        _tone(470, 85, 0.20, "saw", 0.30, 1.6, 0.002),
        _noise(0.16, 0.22, 2.0, 0.35, 21)])
    samples["boom"] = _mix([
        _tone(230, 38, 0.60, "saw", 0.34, 1.4, 0.002),
        _noise(0.55, 0.30, 1.4, 0.20, 31)])
    samples["dash"] = _noise(0.20, 0.24, 2.2, 0.22, 41)
    samples["hurt"] = _tone(200, 65, 0.28, "square", 0.34, 1.5, 0.004)
    samples["up"] = _mix([
        _tone(660, 660, 0.08, "tri", 0.30, 2.0, 0.004),
        _tone(990, 990, 0.14, "tri", 0.26, 2.0, 0.004)])
    samples["pick"] = _mix([
        _tone(784, 784, 0.06, "tri", 0.28, 2.0, 0.003),
        _tone(1046, 1046, 0.06, "tri", 0.28, 2.0, 0.003),
        _tone(1318, 1318, 0.16, "tri", 0.26, 2.2, 0.003)])
    samples["boss"] = _mix([
        _tone(150, 40, 0.95, "saw", 0.32, 1.2, 0.01),
        _noise(0.85, 0.18, 1.2, 0.12, 51)])
    samples["dead"] = _mix([
        _tone(330, 42, 1.10, "saw", 0.30, 1.1, 0.01),
        _tone(165, 28, 1.10, "square", 0.20, 1.1, 0.01)])
    samples["eshoot"] = _tone(760, 520, 0.07, "tri", 0.22, 3.0, 0.002)
    samples["hshot"] = _mix([
        _tone(240, 105, 0.13, "square", 0.26, 2.4, 0.004),
        _noise(0.11, 0.16, 2.4, 0.30, 61)])
    samples["wave"] = _mix([
        _tone(300, 900, 0.18, "tri", 0.22, 2.0, 0.02),
        _tone(880, 880, 0.22, "tri", 0.20, 2.0, 0.02)])
    samples["phase"] = _mix([
        _tone(520, 520, 0.10, "square", 0.26, 2.0, 0.004),
        _tone(390, 390, 0.16, "square", 0.24, 2.0, 0.004)])
    samples["lowhp"] = _tone(90, 55, 0.16, "sine", 0.45, 1.4, 0.006)
    samples["combo"] = _tone(1400, 1800, 0.07, "tri", 0.24, 2.4, 0.002)
    var result: Dictionary = {}
    for name in EFFECT_GAINS:
        result[name] = {
            "stream": _wav(samples[name]),
            "gain": float(EFFECT_GAINS[name])
        }
    return result

static func _music_note(buffer: PackedFloat32Array, at: float, duration: float,
        frequency: float, gain: float, kind: String, decay: float, attack: float = 0.01) -> void:
    var start: int = int(at * MUSIC_SR)
    var length: int = int(duration * MUSIC_SR)
    for i in range(length):
        var j: int = start + i
        if j < 0 or j >= buffer.size():
            break
        var t: float = float(i) / float(MUSIC_SR)
        var phase: float = frequency * t
        var sample: float = 0.0
        match kind:
            "sine":
                sample = sin(TAU * phase)
            "square":
                sample = 1.0 if sin(TAU * phase) >= 0.0 else -1.0
            "tri":
                sample = 4.0 * absf(fposmod(phase, 1.0) - 0.5) - 1.0
        var envelope: float = minf(1.0, t / maxf(0.00001, attack))
        if decay > 0.0:
            envelope *= exp(-decay * t)
        buffer[j] = clampf(buffer[j] + sample * envelope * gain, -1.0, 1.0)

static func _music_hat(buffer: PackedFloat32Array, at: float,
        rng: RandomNumberGenerator) -> void:
    # Original audio.py applies a 30ms exp(-90t) white-noise hat on every
    # odd arpeggio step. This is deterministic but not bit-identical to
    # Python's random.Random(77) sample stream.
    var start: int = int(at * MUSIC_SR)
    for i in range(int(0.03 * MUSIC_SR)):
        var index: int = start + i
        if index < 0 or index >= buffer.size():
            break
        var time: float = float(i) / float(MUSIC_SR)
        var v: float = (1.0 if rng.randf() > 0.5 else -1.0) * exp(-time * 90.0) * 0.05
        buffer[index] = clampf(buffer[index] + v, -1.0, 1.0)

static func build_music() -> AudioStreamWAV:
    # The original four-bar 100 BPM progression (Am, F, C, G) and arpeggios
    # from audio.py. Godot is not guaranteed PCM-bit-identical to Python waveOut.
    var beat: float = 60.0 / 100.0
    var samples := PackedFloat32Array()
    samples.resize(int(beat * 4.0 * 4.0 * float(MUSIC_SR)))
    var hats := RandomNumberGenerator.new()
    hats.seed = 77
    var chords: Array = [
        [110.00, 220.00, 261.63, 329.63],
        [87.31, 174.61, 220.00, 261.63],
        [130.81, 261.63, 329.63, 392.00],
        [98.00, 196.00, 246.94, 293.66]
    ]
    for bar in range(4):
        var entry: Array = chords[bar]
        var root_freq: float = float(entry[0])
        var base: float = float(bar) * beat * 4.0
        for k in range(1, 4):
            _music_note(samples, base, beat * 4.0, float(entry[k]), 0.055, "sine", 0.0, 0.25)
        for measure in range(4):
            var t: float = base + float(measure) * beat
            _music_note(samples, t, beat * 0.85, root_freq * 0.5, 0.10, "square", 4.0)
            _music_note(samples, t, 0.14, 70.0, 0.16, "sine", 22.0, 0.002)
        var arpeggio: Array = [entry[1], entry[2], entry[3], entry[2],
            entry[1], entry[3], entry[2], entry[3]]
        for step in range(8):
            _music_note(samples, base + float(step) * beat / 2.0, beat * 0.45,
                float(arpeggio[step]) * 2.0, 0.085, "tri", 7.0)
            if step % 2 == 1:
                _music_hat(samples, base + float(step) * beat / 2.0, hats)
    return _wav(samples, MUSIC_SR, true)
