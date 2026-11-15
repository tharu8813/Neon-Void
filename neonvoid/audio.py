"""Generated audio and native Windows playback."""

from __future__ import annotations

import ctypes
import math
import queue
import random
import struct
import sys
import threading
import time
from array import array
from .constants import (TAU, clamp)

# ----------------------------------------------------------------------------
# 사운드: 코드 합성 + waveOut 다중 채널 믹서 (실패 시 winsound → 무음)
# ----------------------------------------------------------------------------
SR = 22050       # 효과음 샘플레이트
MSR = 8000       # 배경음악 샘플레이트 (생성 비용 절감)


def _env(i, n, attack, power):
    t = i / n
    a = 1.0 if attack <= 0 else min(1.0, t / attack)
    return a * (1.0 - t) ** power


def _tone(f0, f1, dur, kind="square", amp=0.22, power=2.0, attack=0.01):
    n = max(1, int(SR * dur))
    buf = array("h")
    ph = 0.0
    for i in range(n):
        t = i / n
        f = f0 + (f1 - f0) * t
        ph += TAU * f / SR
        if kind == "square":
            s = 1.0 if math.sin(ph) >= 0.0 else -1.0
        elif kind == "saw":
            s = 2.0 * ((ph / TAU) % 1.0) - 1.0
        elif kind == "tri":
            s = 2.0 * abs(2.0 * ((ph / TAU) % 1.0) - 1.0) - 1.0
        else:
            s = math.sin(ph)
        v = s * _env(i, n, attack, power) * amp
        buf.append(int(clamp(v, -1.0, 1.0) * 32767))
    return buf


def _noise(dur, amp=0.2, power=2.0, lp=0.45, seed=1234):
    n = max(1, int(SR * dur))
    buf = array("h")
    rs = random.Random(seed)
    prev = 0.0
    for i in range(n):
        s = rs.uniform(-1.0, 1.0)
        prev = prev * (1.0 - lp) + s * lp
        v = prev * _env(i, n, 0.004, power) * amp * 2.4
        buf.append(int(clamp(v, -1.0, 1.0) * 32767))
    return buf


def _mix(*bufs):
    n = max(len(b) for b in bufs)
    out = array("h", bytes(2 * n))
    for k in range(n):
        s = 0
        for b in bufs:
            if k < len(b):
                s += b[k]
        out[k] = -32767 if s < -32767 else (32767 if s > 32767 else s)
    return out


def _norm(buf, peak=0.82):
    m = 0
    for v in buf:
        a = v if v >= 0 else -v
        if a > m:
            m = a
    if m:
        f = (peak * 32767.0) / m
        for i in range(len(buf)):
            buf[i] = int(buf[i] * f)
    return buf


def _wav(pcm):
    raw = pcm.tobytes()
    hdr = (
        b"RIFF"
        + struct.pack("<I", 36 + len(raw))
        + b"WAVEfmt "
        + struct.pack("<IHHIIHH", 16, 1, 1, SR, SR * 2, 2, 16)
        + b"data"
        + struct.pack("<I", len(raw))
    )
    return hdr + raw


def _build_effects():
    """(PCM, 기본 게인) 사전."""
    e = {}
    e["shoot"] = (_mix(_tone(1250, 640, 0.06, "square", 0.30, 3.0, 0.002),
                       _noise(0.05, 0.10, 3.0, 0.75, 7)), 0.34)
    e["hit"] = (_noise(0.045, 0.30, 3.0, 0.55, 11), 0.40)
    e["kill"] = (_mix(_tone(470, 85, 0.20, "saw", 0.30, 1.6, 0.002),
                      _noise(0.16, 0.22, 2.0, 0.35, 21)), 0.52)
    e["boom"] = (_mix(_tone(230, 38, 0.60, "saw", 0.34, 1.4, 0.002),
                      _noise(0.55, 0.30, 1.4, 0.20, 31)), 0.70)
    e["dash"] = (_noise(0.20, 0.24, 2.2, 0.22, 41), 0.40)
    e["hurt"] = (_tone(200, 65, 0.28, "square", 0.34, 1.5, 0.004), 0.60)
    e["up"] = (_mix(_tone(660, 660, 0.08, "tri", 0.30, 2.0, 0.004),
                    _tone(990, 990, 0.14, "tri", 0.26, 2.0, 0.004)), 0.45)
    e["pick"] = (_mix(_tone(784, 784, 0.06, "tri", 0.28, 2.0, 0.003),
                      _tone(1046, 1046, 0.06, "tri", 0.28, 2.0, 0.003),
                      _tone(1318, 1318, 0.16, "tri", 0.26, 2.2, 0.003)), 0.50)
    e["boss"] = (_mix(_tone(150, 40, 0.95, "saw", 0.32, 1.2, 0.01),
                      _noise(0.85, 0.18, 1.2, 0.12, 51)), 0.60)
    e["dead"] = (_mix(_tone(330, 42, 1.10, "saw", 0.30, 1.1, 0.01),
                      _tone(165, 28, 1.10, "square", 0.20, 1.1, 0.01)), 0.65)
    e["eshoot"] = (_tone(760, 520, 0.07, "tri", 0.22, 3.0, 0.002), 0.26)
    e["hshot"] = (_mix(_tone(240, 105, 0.13, "square", 0.26, 2.4, 0.004),
                       _noise(0.11, 0.16, 2.4, 0.30, 61)), 0.34)
    e["wave"] = (_mix(_tone(300, 900, 0.18, "tri", 0.22, 2.0, 0.02),
                      _tone(880, 880, 0.22, "tri", 0.20, 2.0, 0.02)), 0.40)
    e["phase"] = (_mix(_tone(520, 520, 0.10, "square", 0.26, 2.0, 0.004),
                       _tone(390, 390, 0.16, "square", 0.24, 2.0, 0.004)), 0.45)
    e["lowhp"] = (_tone(90, 55, 0.16, "sine", 0.45, 1.4, 0.006), 0.50)
    e["combo"] = (_tone(1400, 1800, 0.07, "tri", 0.24, 2.4, 0.002), 0.26)
    for k in e:
        _norm(e[k][0], 0.85)
    return e


def _build_music():
    """네온 분위기의 4마디 루프 (MSR)."""
    rate = MSR
    beat = 60.0 / 100.0
    bars = 4
    total = int(beat * 4 * bars * rate)
    buf = [0.0] * total

    def put(t0, dur, fn, gain):
        i0 = int(t0 * rate)
        n = int(dur * rate)
        for i in range(n):
            j = i0 + i
            if j < 0 or j >= total:
                break
            buf[j] += fn(i / rate) * gain

    def sine(f, decay=0.0, attack=0.01):
        def f2(t):
            a = 1.0 if t > attack else t / attack
            return math.sin(TAU * f * t) * a * (math.exp(-decay * t) if decay else 1.0)
        return f2

    def square(f, decay=0.0):
        def f2(t):
            return (1.0 if math.sin(TAU * f * t) >= 0 else -1.0) * math.exp(-decay * t)
        return f2

    def tri(f, decay=0.0):
        def f2(t):
            ph = (f * t) % 1.0
            return (4.0 * abs(ph - 0.5) - 1.0) * math.exp(-decay * t)
        return f2

    rs = random.Random(77)

    def hat(t):
        return (1.0 if rs.random() > 0.5 else -1.0) * math.exp(-t * 90.0)

    prog = [
        (110.00, [220.00, 261.63, 329.63]),   # Am
        (87.31, [174.61, 220.00, 261.63]),    # F
        (130.81, [261.63, 329.63, 392.00]),   # C
        (98.00, [196.00, 246.94, 293.66]),    # G
    ]
    for bar in range(bars):
        root, tones = prog[bar % 4]
        t0 = bar * beat * 4
        for f in tones:
            put(t0, beat * 4, sine(f, 0.0, 0.25), 0.055)
        for b in range(4):
            put(t0 + b * beat, beat * 0.85, square(root / 2, 4.0), 0.10)
            put(t0 + b * beat, 0.14, sine(70, 22.0, 0.002), 0.16)
        seq = [tones[0] * 2, tones[1] * 2, tones[2] * 2, tones[1] * 2,
               tones[0] * 2, tones[2] * 2, tones[1] * 2, tones[2] * 2]
        for k in range(8):
            put(t0 + k * beat / 2, beat * 0.45, tri(seq[k], 7.0), 0.085)
            if k % 2 == 1:
                put(t0 + k * beat / 2, 0.03, hat, 0.05)
    return _norm(array("h", (int(clamp(v, -1.0, 1.0) * 32767) for v in buf)),
                 0.90)


class _Mixer:
    """winmm waveOut + 소프트웨어 믹싱. 실패하면 ok=False."""

    RATE = SR
    NFRAMES = 512
    NBUF = 3
    WHDR_DONE = 0x01
    WHDR_INQUEUE = 0x10

    def __init__(self):
        self.ok = False
        self.error = None
        self.voices = []
        self.lock = threading.Lock()
        self.muted = False
        self.music_voice = None
        self.music_target = 0.0
        self.blocks = 0
        try:
            self._open()
        except Exception as exc:
            self.error = repr(exc)
            self.ok = False

    def _open(self):
        from ctypes import wintypes

        class WAVEFORMATEX(ctypes.Structure):
            _fields_ = [
                ("wFormatTag", wintypes.WORD),
                ("nChannels", wintypes.WORD),
                ("nSamplesPerSec", wintypes.DWORD),
                ("nAvgBytesPerSec", wintypes.DWORD),
                ("nBlockAlign", wintypes.WORD),
                ("wBitsPerSample", wintypes.WORD),
                ("cbSize", wintypes.WORD),
            ]

        class WAVEHDR(ctypes.Structure):
            _fields_ = [
                ("lpData", ctypes.c_void_p),
                ("dwBufferLength", wintypes.DWORD),
                ("dwBytesRecorded", wintypes.DWORD),
                ("dwUser", ctypes.c_size_t),
                ("dwFlags", wintypes.DWORD),
                ("dwLoops", wintypes.DWORD),
                ("lpNext", ctypes.c_void_p),
                ("reserved", ctypes.c_size_t),
            ]

        winmm = ctypes.WinDLL("winmm")
        winmm.waveOutOpen.argtypes = [
            ctypes.POINTER(ctypes.c_void_p), wintypes.UINT,
            ctypes.POINTER(WAVEFORMATEX), ctypes.c_size_t, ctypes.c_size_t,
            wintypes.UINT,
        ]
        winmm.waveOutOpen.restype = wintypes.UINT
        winmm.waveOutPrepareHeader.argtypes = [
            ctypes.c_void_p, ctypes.POINTER(WAVEHDR), wintypes.UINT]
        winmm.waveOutPrepareHeader.restype = wintypes.UINT
        winmm.waveOutWrite.argtypes = [
            ctypes.c_void_p, ctypes.POINTER(WAVEHDR), wintypes.UINT]
        winmm.waveOutWrite.restype = wintypes.UINT

        wf = WAVEFORMATEX(1, 1, self.RATE, self.RATE * 2, 2, 16, 0)
        handle = ctypes.c_void_p()
        rc = winmm.waveOutOpen(ctypes.byref(handle),
                               wintypes.UINT(0xFFFFFFFF),
                               ctypes.byref(wf), 0, 0, 0)
        if rc != 0:
            raise RuntimeError(f"waveOutOpen rc={rc}")

        self.winmm = winmm
        self.handle = handle
        self.hdrs = []
        self.bufs = []
        self.ptrs = []
        for _ in range(self.NBUF):
            buf = ctypes.create_string_buffer(self.NFRAMES * 2)
            hdr = WAVEHDR()
            hdr.lpData = ctypes.cast(buf, ctypes.c_void_p)
            hdr.dwBufferLength = self.NFRAMES * 2
            hdr.dwFlags = 0
            if winmm.waveOutPrepareHeader(handle, ctypes.byref(hdr),
                                          ctypes.sizeof(hdr)) != 0:
                raise RuntimeError("waveOutPrepareHeader 실패")
            self.hdrs.append(hdr)
            self.bufs.append(buf)
            self.ptrs.append(ctypes.cast(buf, ctypes.POINTER(ctypes.c_int16)))
        self.ok = True
        threading.Thread(target=self._run, daemon=True).start()

    def _run(self):
        try:
            while self.ok:
                wrote = False
                for i in range(self.NBUF):
                    hdr = self.hdrs[i]
                    if hdr.dwFlags & self.WHDR_INQUEUE:
                        continue
                    hdr.dwFlags &= ~self.WHDR_DONE
                    self._fill(i)
                    if self.winmm.waveOutWrite(self.handle, ctypes.byref(hdr),
                                               ctypes.sizeof(hdr)) != 0:
                        self.ok = False
                        return
                    wrote = True
                    self.blocks += 1
                if not wrote:
                    time.sleep(0.004)
        except Exception:
            self.ok = False

    def _fill(self, i):
        n = self.NFRAMES
        mix = self._mix_block(n)
        peak = 0.0
        for v in mix:
            a = v if v >= 0 else -v
            if a > peak:
                peak = a
        if peak > 26000.0:
            k = 26000.0 / peak
            for j in range(n):
                mix[j] *= k
        ptr = self.ptrs[i]
        for k in range(n):
            v = mix[k]
            ptr[k] = -32768 if v < -32768 else (32767 if v > 32767 else int(v))

    def _mix_block(self, n):
        out = [0] * n
        with self.lock:
            voices = list(self.voices)
            music = self.music_voice
            muted = self.muted
            mtarget = self.music_target

        if music is not None:
            target = 0.0 if muted else mtarget
            music["gain"] += (target - music["gain"]) * min(1.0, n / (self.RATE * 1.2))
            s = music["s"]
            ln = len(s)
            p = music["pos"]
            step = music["step"]
            g = music["gain"]
            for i in range(n):
                idx = int(p)
                if idx >= ln:
                    p -= ln
                    idx = int(p)
                out[i] += s[idx] * g
                p += step
            music["pos"] = p % ln

        if not muted:
            for v in voices:
                s = v["s"]
                ln = len(s)
                p = v["pos"]
                g = v["gain"]
                if v["step"] == 1.0:
                    i0 = int(p)
                    cnt = min(n, ln - i0)
                    for i in range(cnt):
                        out[i] += s[i0 + i] * g
                    v["pos"] = p + cnt
                else:
                    for i in range(n):
                        idx = int(p)
                        if idx >= ln:
                            break
                        out[i] += s[idx] * g
                        p += v["step"]
                    v["pos"] = p

        with self.lock:
            self.voices = [v for v in self.voices
                           if v["loop"] or v["pos"] < len(v["s"])]
        return out

    def play(self, samples, gain):
        if not self.ok:
            return
        with self.lock:
            if len(self.voices) > 26:
                return
            self.voices.append({"s": samples, "pos": 0.0, "step": 1.0,
                                "gain": gain, "loop": False})

    def set_music(self, samples, gain=0.34):
        if not self.ok:
            return
        with self.lock:
            self.music_voice = {"s": samples, "pos": 0.0,
                                "step": MSR / float(self.RATE),
                                "gain": 0.0, "loop": True}
            self.music_target = gain


class Audio:
    """믹서 → winsound → 무음 순서로 폴백하는 사운드 파사드."""

    def __init__(self, enabled=True, volume=0.9):
        self.enabled = False
        self.volume = clamp(volume, 0.0, 1.0)
        self.effects = {}
        self.last = {}
        self.mixer = None
        self.winsound = None
        self.wavdata = {}
        self.q = queue.Queue(maxsize=24)
        self.muted = False
        if not enabled:
            return
        try:
            self.effects = _build_effects()
        except Exception:
            self.effects = {}

        m = _Mixer()
        if m.ok:
            self.mixer = m
            self.enabled = True

        if not self.enabled and self.effects:
            try:
                import winsound
                self.winsound = winsound
                self.wavdata = {k: _wav(v[0]) for k, v in self.effects.items()}
                threading.Thread(target=self._simple_worker, daemon=True).start()
                self.enabled = True
            except Exception:
                self.enabled = False

    def _simple_worker(self):
        while True:
            name = self.q.get()
            try:
                self.winsound.PlaySound(
                    self.wavdata[name],
                    self.winsound.SND_MEMORY | self.winsound.SND_ASYNC)
            except Exception:
                pass
            time.sleep(0.02)

    def play(self, name, gap=0.05):
        if not self.enabled or self.muted:
            return
        now = time.perf_counter()
        if now - self.last.get(name, 0.0) < gap:
            return
        self.last[name] = now
        fx = self.effects.get(name)
        if fx is None:
            return
        if self.mixer is not None:
            self.mixer.play(fx[0], fx[1] * self.volume)
        else:
            try:
                self.q.put_nowait(name)
            except queue.Full:
                pass

    def start_music(self):
        if self.mixer is None:
            return

        def work():
            try:
                samples = _build_music()
            except Exception:
                return
            self.mixer.set_music(samples, 0.30 * self.volume)

        threading.Thread(target=work, daemon=True).start()

    def set_volume(self, volume):
        """Change the active software mixer level (including ongoing music)."""
        self.volume = clamp(float(volume), 0.0, 1.0)
        if self.mixer is not None:
            with self.mixer.lock:
                self.mixer.music_target = 0.30 * self.volume
        return self.volume

    def set_muted(self, muted):
        """Synchronize the mixer and logical mute states."""
        self.muted = bool(muted)
        if self.mixer is not None:
            with self.mixer.lock:
                self.mixer.muted = self.muted
                if self.muted:
                    self.mixer.voices.clear()
        return self.muted

    def toggle_mute(self):
        return self.set_muted(not self.muted)
