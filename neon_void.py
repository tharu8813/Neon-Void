#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
NEON VOID
=========
표준 라이브러리(tkinter)만으로 동작하는 네온 로그라이크 탄막 슈터.

    python neon_void.py                  # 게임 시작
    python neon_void.py --no-sound       # 무음
    python neon_void.py --selftest 6000  # 자동 플레이 검증

조작
    WASD / 방향키 : 이동
    마우스        : 조준 (자동 사격)
    Space / Shift : 대시 (무적)
    P / Esc       : 일시정지
    1,2,3 / 클릭  : 업그레이드 선택
    R             : 재시작
"""

from __future__ import annotations

import argparse
import ctypes
import json
import math
import os
import queue
import random
import struct
import sys
import threading
import time
import traceback
from array import array

import tkinter as tk

# ----------------------------------------------------------------------------
# 상수
# ----------------------------------------------------------------------------
W, H = 1280, 720
DT = 1.0 / 60.0
TAU = math.pi * 2.0
SAVE_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "save.json")

C_BG = "#04060e"
C_GRID = "#0a1220"
C_STAR = "#16233a"

C_PLAYER = "#6ff2ff"
C_PLAYER_GLOW = "#12525f"
C_BULLET = "#ccff4d"
C_ENEMY = "#ff3f7f"
C_SHOOT = "#ffcf5c"
C_SPLIT = "#b06bff"
C_RUSH = "#ff8a3d"
C_BOSS = "#ff5cf0"
C_EBULLET = "#ff6f9c"
C_SHARD = "#7ef0ff"
C_TEXT = "#d5ebff"
C_DIM = "#5b7898"
C_HP = "#3ff5a0"
C_BAD = "#ff4d6d"
C_GOLD = "#ffd166"

# 글꼴 -----------------------------------------------------------------------
F_TITLE = ("Malgun Gothic", 46, "bold")
F_HUGE = ("Malgun Gothic", 34, "bold")
F_BIG = ("Malgun Gothic", 22, "bold")
F_MID = ("Malgun Gothic", 15, "bold")
F_UI = ("Malgun Gothic", 11)
F_HUD = ("Malgun Gothic", 11, "bold")
F_NUM = ("Consolas", 11, "bold")
F_MONO = ("Consolas", 9)
F_DMG = ("Consolas", 10, "bold")

RARITY_COL = {0: "#7ee0ff", 1: "#b98cff", 2: "#ffd166"}
RARITY_NAME = {0: "일반", 1: "희귀", 2: "전설"}


def clamp(v, a, b):
    return a if v < a else (b if v > b else v)


def rnd(a, b):
    return a + (b - a) * random.random()


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

    def toggle_mute(self):
        self.muted = not self.muted
        if self.mixer is not None:
            self.mixer.muted = self.muted
            if self.muted:
                with self.mixer.lock:
                    self.mixer.voices.clear()
        return self.muted


# ----------------------------------------------------------------------------
# 저장
# ----------------------------------------------------------------------------
def load_save():
    try:
        with open(SAVE_FILE, "r", encoding="utf-8") as f:
            d = json.load(f)
        if isinstance(d, dict):
            return {
                "best_score": int(d.get("best_score", 0)),
                "best_wave": int(d.get("best_wave", 0)),
                "runs": int(d.get("runs", 0)),
            }
    except Exception:
        pass
    return {"best_score": 0, "best_wave": 0, "runs": 0}


def write_save(d):
    try:
        with open(SAVE_FILE, "w", encoding="utf-8") as f:
            json.dump(d, f, ensure_ascii=False, indent=2)
    except Exception:
        pass


# ----------------------------------------------------------------------------
# 캔버스 아이템 풀 (매 프레임 생성/삭제 대신 재사용)
# ----------------------------------------------------------------------------
class OvalPool:
    def __init__(self, canvas, count, **kw):
        self.c = canvas
        self.items = [
            canvas.create_oval(-30, -30, -30, -30, state="hidden", **kw)
            for _ in range(count)
        ]
        self.n = 0
        self.prev = 0

    def begin(self):
        self.prev = self.n
        self.n = 0

    def draw(self, x, y, r, fill, outline=""):
        if self.n >= len(self.items):
            return None
        it = self.items[self.n]
        self.n += 1
        self.c.coords(it, x - r, y - r, x + r, y + r)
        self.c.itemconfigure(it, fill=fill, outline=outline, state="normal")
        return it

    def end(self):
        for k in range(self.n, self.prev):
            self.c.itemconfigure(self.items[k], state="hidden")


class PolyPool:
    """모든 아이템을 같은 정점 수로 만들고, 남는 정점은 마지막 점에 겹쳐 둔다."""

    def __init__(self, canvas, count, npoints=8, **kw):
        self.c = canvas
        self.np = npoints
        self.items = [
            canvas.create_polygon(*([-40, -40] * npoints), state="hidden", **kw)
            for _ in range(count)
        ]
        self.n = 0
        self.prev = 0

    def begin(self):
        self.prev = self.n
        self.n = 0

    def draw(self, pts, fill, outline="", width=1):
        if self.n >= len(self.items) or not pts:
            return None
        it = self.items[self.n]
        self.n += 1
        coords = []
        for i in range(self.np):
            p = pts[i] if i < len(pts) else pts[-1]
            coords.append(p[0])
            coords.append(p[1])
        self.c.coords(it, *coords)
        self.c.itemconfigure(it, fill=fill, outline=outline,
                             width=width, state="normal")
        return it

    def end(self):
        for k in range(self.n, self.prev):
            self.c.itemconfigure(self.items[k], state="hidden")


class TextPool:
    def __init__(self, canvas, count, font, **kw):
        self.c = canvas
        self.items = [
            canvas.create_text(-40, -40, text="", state="hidden", font=font, **kw)
            for _ in range(count)
        ]
        self.n = 0
        self.prev = 0

    def draw(self, x, y, text, fill):
        if self.n >= len(self.items):
            return None
        it = self.items[self.n]
        self.n += 1
        self.c.coords(it, x, y)
        self.c.itemconfigure(it, text=text, fill=fill, state="normal")
        return it

    def begin(self):
        self.prev = self.n
        self.n = 0

    def end(self):
        for k in range(self.n, self.prev):
            self.c.itemconfigure(self.items[k], state="hidden")


class LinePool:
    def __init__(self, canvas, count, **kw):
        self.c = canvas
        self.items = [
            canvas.create_line(0, 0, 0, 0, state="hidden", **kw) for _ in range(count)
        ]
        self.n = 0
        self.prev = 0

    def begin(self):
        self.prev = self.n
        self.n = 0

    def draw(self, x1, y1, x2, y2, fill, width=2):
        if self.n >= len(self.items):
            return None
        it = self.items[self.n]
        self.n += 1
        self.c.coords(it, x1, y1, x2, y2)
        self.c.itemconfigure(it, fill=fill, width=width, state="normal")
        return it

    def end(self):
        for k in range(self.n, self.prev):
            self.c.itemconfigure(self.items[k], state="hidden")


# ----------------------------------------------------------------------------
# 엔티티
# ----------------------------------------------------------------------------
class Player:
    __slots__ = ("x", "y", "vx", "vy", "hp", "maxhp", "r", "aim",
                 "fire_cd", "dash_t", "dash_cd", "charges", "charge_t",
                 "inv", "dash_dir", "drone_ang", "st", "recoil")

    def __init__(self):
        self.x, self.y = W / 2, H / 2
        self.vx = self.vy = 0.0
        self.maxhp = 100.0
        self.hp = 100.0
        self.r = 13.0
        self.aim = -math.pi / 2
        self.fire_cd = 0.0
        self.dash_t = 0.0
        self.dash_cd = 1.05
        self.charges = 1
        self.charge_t = 0.0
        self.inv = 0.0
        self.dash_dir = (0.0, 0.0)
        self.drone_ang = 0.0
        self.recoil = 0.0
        self.st = {
            "speed": 322.0, "rate": 5.4, "dmg": 10.0, "bspeed": 880.0,
            "pierce": 0, "split": 0, "homing": 0.0, "explode": 0.0,
            "ricochet": 0, "crit": 0.05, "lifesteal": 0.0, "thorns": 0.0,
            "dash_max": 1, "dash_blast": 0.0, "slow": 0.0, "magnet": 82.0,
            "drone": 0, "chain": 0, "rear": 0,
        }


class Enemy:
    __slots__ = ("kind", "x", "y", "vx", "vy", "hp", "maxhp", "r",
                 "t", "cd", "state", "ang", "flash", "score", "spd", "dmg")

    def __init__(self):
        self.kind = "drifter"
        self.x = self.y = 0.0
        self.vx = self.vy = 0.0
        self.hp = self.maxhp = 10.0
        self.r = 12.0
        self.t = 0.0
        self.cd = 0.0
        self.state = 0
        self.ang = 0.0
        self.flash = 0.0
        self.score = 10
        self.spd = 100.0
        self.dmg = 8.0


class Bullet:
    __slots__ = ("x", "y", "vx", "vy", "r", "dmg", "life", "pierce",
                 "homing", "explode", "ricochet", "crit", "chain",
                 "color", "dead", "hits", "friendly")

    def __init__(self):
        self.x = self.y = 0.0
        self.vx = self.vy = 0.0
        self.r = 3.5
        self.dmg = 10.0
        self.life = 2.0
        self.pierce = 0
        self.homing = 0.0
        self.explode = 0.0
        self.ricochet = 0
        self.crit = 0.0
        self.chain = 0
        self.color = C_BULLET
        self.dead = False
        self.hits = []
        self.friendly = True


class Particle:
    __slots__ = ("x", "y", "vx", "vy", "life", "maxlife", "r", "color", "drag")

    def __init__(self, x, y, vx, vy, life, r, color, drag=2.6):
        self.x = x
        self.y = y
        self.vx = vx
        self.vy = vy
        self.life = life
        self.maxlife = life
        self.r = r
        self.color = color
        self.drag = drag


class Shard:
    __slots__ = ("x", "y", "vx", "vy", "life", "value")

    def __init__(self, x, y, value):
        self.x = x
        self.y = y
        self.vx = rnd(-90, 90)
        self.vy = rnd(-90, 90)
        self.life = 14.0
        self.value = value


class FText:
    __slots__ = ("x", "y", "vy", "life", "text", "color")

    def __init__(self, x, y, text, color):
        self.x = x
        self.y = y
        self.vy = -46.0
        self.life = 0.7
        self.text = text
        self.color = color


# ----------------------------------------------------------------------------
# 업그레이드
# ----------------------------------------------------------------------------
# (id, 이름, 설명, 등급, 최대 중첩)
UPGRADES = [
    ("rapid", "과열 방출", "연사 속도 +18%", 0, 6),
    ("power", "과충전 탄두", "탄환 피해 +4", 0, 8),
    ("pierce", "관통 코어", "탄환이 적 1체를 더 관통", 0, 3),
    ("split", "분열 사격", "탄환이 갈라져 2발 추가", 0, 3),
    ("homing", "유도 코일", "탄환이 적을 추적", 1, 3),
    ("boom", "폭발 탄두", "명중 시 주변에 폭발 피해", 1, 3),
    ("crit", "치명타 회로", "치명타 확률 +8%", 0, 5),
    ("vamp", "흡혈 장치", "적 처치 시 체력 +0.7", 1, 3),
    ("thorn", "가시 갑옷", "접촉한 적에게 피해 반사", 0, 3),
    ("dash2", "대시 충전", "대시 횟수 +1", 1, 3),
    ("dashb", "대시 폭발", "대시 중 주변 적에게 피해", 1, 3),
    ("slow", "시간 왜곡", "처치 시 시간이 느려짐", 2, 3),
    ("magnet", "자력 수집", "파편 수집 범위 +90", 0, 3),
    ("drone", "위성 드론", "궤도 드론이 자동 사격", 2, 3),
    ("chain", "연쇄 방전", "명중 시 근처 적에게 방전", 2, 2),
    ("rear", "후방 포탑", "후방으로 추가 사격", 1, 1),
    ("hull", "외골격", "최대 체력 +20 (즉시 회복)", 0, 4),
    ("ricochet", "도탄 장치", "탄환이 벽에서 1회 튕김", 1, 2),
]
UP_BY_ID = {u[0]: u for u in UPGRADES}
UP_WEIGHT = {0: 1.0, 1: 0.42, 2: 0.13}


def apply_upgrade(g, uid):
    st = g.p.st
    g.levels[uid] = g.levels.get(uid, 0) + 1
    if uid == "rapid":
        st["rate"] *= 1.18
    elif uid == "power":
        st["dmg"] += 4.0
    elif uid == "pierce":
        st["pierce"] += 1
    elif uid == "split":
        st["split"] += 1
    elif uid == "homing":
        st["homing"] += 2.6
    elif uid == "boom":
        st["explode"] += 20.0
    elif uid == "crit":
        st["crit"] += 0.08
    elif uid == "vamp":
        st["lifesteal"] += 0.7
    elif uid == "thorn":
        st["thorns"] += 14.0
    elif uid == "dash2":
        st["dash_max"] += 1
        g.p.charges += 1
    elif uid == "dashb":
        st["dash_blast"] += 30.0
    elif uid == "slow":
        st["slow"] += 0.075
    elif uid == "magnet":
        st["magnet"] += 90.0
    elif uid == "drone":
        st["drone"] += 1
    elif uid == "chain":
        st["chain"] += 1
    elif uid == "rear":
        st["rear"] += 1
    elif uid == "hull":
        g.p.maxhp += 20.0
        g.p.hp = min(g.p.maxhp, g.p.hp + 20.0)
    elif uid == "ricochet":
        st["ricochet"] += 1


# 적 기본 능력치
E_PARAMS = {
    "drifter": dict(hp=22, r=13, spd=108, dmg=6, score=12, color=C_ENEMY),
    "mini": dict(hp=10, r=8, spd=158, dmg=4, score=6, color=C_SPLIT),
    "shooter": dict(hp=34, r=15, spd=78, dmg=8, score=22, color=C_SHOOT),
    "splitter": dict(hp=50, r=18, spd=94, dmg=9, score=26, color=C_SPLIT),
    "rusher": dict(hp=28, r=14, spd=124, dmg=12, score=20, color=C_RUSH),
    "boss": dict(hp=620, r=46, spd=70, dmg=18, score=400, color=C_BOSS),
}
SPAWN_COST = {"drifter": 1.0, "shooter": 2.0, "splitter": 2.0, "rusher": 1.7}


# ----------------------------------------------------------------------------
# 게임
# ----------------------------------------------------------------------------
class Game:
    def __init__(self, root, opts):
        self.root = root
        self.opts = opts
        self.auto = bool(opts.selftest)
        self.selftest_left = int(opts.selftest or 0)
        self.show_fps = bool(opts.fps)
        self.audio = Audio(enabled=not opts.no_sound and not self.auto,
                           volume=getattr(opts, "volume", 0.9))
        self.music_started = False
        self.muted = False
        self.mute_t = 0.0
        self.lowhp_t = 0.0
        self.combo_mark = 0

        root.title("NEON VOID")
        root.configure(bg=C_BG)
        root.resizable(False, False)

        self.c = tk.Canvas(root, width=W, height=H, bg=C_BG,
                           highlightthickness=0, bd=0)
        self.c.pack()
        if self.auto:
            # 자동 검증 중에는 창을 띄우지 않는다
            root.withdraw()

        self.save = load_save()
        self.rng = random.Random(opts.seed)

        self.keys = set()
        self.mx, self.my = W / 2, H / 2 - 120
        self.mouse_down = False

        root.bind("<KeyPress>", self.on_key)
        root.bind("<KeyRelease>", self.on_key_up)
        root.bind("<FocusOut>", self._on_focus_out)
        self.c.bind("<Motion>", self.on_motion)
        self.c.bind("<ButtonPress-1>", self.on_mouse_down)
        self.c.bind("<ButtonRelease-1>", self.on_mouse_up)
        # 기본 OS 커서를 숨기고 게임 전용 커서를 그린다
        self.c.configure(cursor="none")
        self.c.bind("<Enter>", self._on_pointer_enter)
        self.c.bind("<Leave>", self._on_pointer_leave)

        # 애니메이션/연출 상태
        self.ui_t = 0.0
        self.cursor_on = True
        self.cursor_hit = 0.0
        self.cursor_hot = False
        self.hover_card = -1
        self.flash_t = 0.0
        self.flash_dur = 0.3
        self.flash_col = C_PLAYER
        self.sweep_t = 0.0
        self.sweep_dur = 0.55
        self.sweep_col = C_PLAYER
        self.hp_ratio_chip = 1.0
        self.score_shown = 0.0
        self.wave_pop = 0.0
        self.combo_pop = 0.0
        self.dash_ready_t = 0.0
        self.prev_charges = 1
        self.banner_total = 1.0
        self.card_t = 0.0
        self.card_anim = []
        self.card_yoff = []
        self.title_age = 0.0
        self.pause_t = 0.0
        self.go_t = 0.0
        self.go_score = 0
        self.go_kills = 0
        self.go_record = False

        # 배경 (가장 아래 레이어)
        self.stars = []
        self.grid_items = []
        self._build_background()

        # 렌더 순서대로 풀 생성
        self.pool_enemy = PolyPool(self.c, 200, 8, fill=C_ENEMY, outline="")
        self.pool_ring = OvalPool(self.c, 90, fill="", outline=C_BOSS)
        self.pool_particle = OvalPool(self.c, 300, fill="#ffffff", outline="")
        self.pool_ebullet = OvalPool(self.c, 140, fill=C_EBULLET, outline="")
        self.pool_streak = LinePool(self.c, 240, capstyle="round", fill=C_BULLET)
        self.pool_bullet = OvalPool(self.c, 60, fill=C_BULLET, outline="")
        self.pool_shard = OvalPool(self.c, 70, fill=C_SHARD, outline="")
        self.pool_ftext = TextPool(self.c, 40, F_DMG, anchor="center")
        self.pool_line = LinePool(self.c, 16, capstyle="round", fill=C_BULLET)
        self.pool_drone = OvalPool(self.c, 8, fill=C_PLAYER, outline="")

        # 플레이어 표시
        self.ship = self.c.create_polygon(0, 0, 0, 0, 0, 0,
                                          fill=C_PLAYER, outline=C_PLAYER, width=1)
        self.thrust = self.c.create_oval(0, 0, 0, 0, fill="#1c6d7d", outline="")
        self.aim_dot = self.c.create_oval(0, 0, 0, 0, outline=C_PLAYER, width=1)

        # HUD
        self._build_hud()

        self.state = "title"
        self.t = 0.0
        self.acc = 0.0
        self.last = time.perf_counter()
        self.fps = 60.0
        self.frames = 0

        self.p = Player()
        self.enemies = []
        self.bullets = []
        self.ebullets = []
        self.particles = []
        self.shards = []
        self.ftexts = []
        self.grid = {}
        self.chain_fx = []

        self.wave = 0
        self.score = 0
        self.combo = 0
        self.combo_t = 0.0
        self.levels = {}
        self.spawn_queue = []
        self.wave_done_t = 0.0
        self.hitstop = 0.0
        self.slow = 0.0
        self.shake = 0.0
        self.paused = False
        self.banner = ""
        self.banner_t = 0.0
        self.kills = 0
        self.boss = None
        self.cards = []
        self.card_opts = []
        self.title_pulse = 0.0
        self.auto_pick_t = 0.0
        self.dead_t = 0.0
        self._drone_cd = 0.0

        self._build_title()
        self._build_cursor()
        if not self.auto:
            self.loop()

    # ------------------------------------------------------------------ 배경
    def _build_background(self):
        c = self.c
        for _ in range(52):
            x = rnd(0, W)
            y = rnd(0, H)
            r = rnd(0.7, 1.9)
            it = c.create_oval(x - r, y - r, x + r, y + r,
                               fill=C_STAR, outline="")
            self.stars.append((it, x, y, rnd(0.15, 0.5)))
        for x in range(0, W + 1, 80):
            it = c.create_line(x, 0, x, H, fill=C_GRID)
            self.grid_items.append((it, 0, x))
        for y in range(0, H + 1, 80):
            it = c.create_line(0, y, W, y, fill=C_GRID)
            self.grid_items.append((it, 1, y))

    # ------------------------------------------------------------------ 배경
    def _build_background(self):
        c = self.c
        for _ in range(52):
            x = rnd(0, W)
            y = rnd(0, H)
            r = rnd(0.7, 1.9)
            it = c.create_oval(x - r, y - r, x + r, y + r,
                               fill=C_STAR, outline="")
            self.stars.append((it, x, y, rnd(0.15, 0.5),
                               rnd(0, TAU), rnd(0.7, 2.1), rnd(3.0, 11.0)))
        for x in range(0, W + 1, 80):
            it = c.create_line(x, 0, x, H, fill=C_GRID)
            self.grid_items.append((it, 0, x))
        for y in range(0, H + 1, 80):
            it = c.create_line(0, y, W, y, fill=C_GRID)
            self.grid_items.append((it, 1, y))
        # 스캔라인 + 저체력 비네트 + 화면 플래시
        self.scanline = c.create_line(0, 0, W, 0, fill="#0c2131", width=2)
        self.vig_items = [
            c.create_rectangle(0, 0, W, 22, fill=C_BAD, outline="", stipple="gray12",
                               state="hidden"),
            c.create_rectangle(0, H - 22, W, H, fill=C_BAD, outline="", stipple="gray12",
                               state="hidden"),
            c.create_rectangle(0, 0, 22, H, fill=C_BAD, outline="", stipple="gray12",
                               state="hidden"),
            c.create_rectangle(W - 22, 0, W, H, fill=C_BAD, outline="", stipple="gray12",
                               state="hidden"),
        ]
        self.flash_rect = c.create_rectangle(0, 0, W, H, fill=C_PLAYER,
                                             outline="", stipple="gray25",
                                             state="hidden", tags=("fx",))
        self.sweep_band = c.create_rectangle(0, 0, W, 0, fill=C_PLAYER,
                                              outline="", stipple="gray12",
                                              state="hidden", tags=("fx",))
        self.sweep_line = c.create_line(0, 0, W, 0, fill=C_PLAYER, width=2,
                                         state="hidden", tags=("fx",))

    def _move_background(self, ox, oy):
        c = self.c
        u = self.ui_t
        for it, x, y, pr, ph, tw, dr in self.stars:
            r = (0.55 + pr * 2.1) * (0.62 + 0.38 * math.sin(u * tw + ph))
            sx = x + ox * 0.35
            sy = ((y + u * dr) % (H + 16)) - 8 + oy * 0.35
            c.coords(it, sx - r, sy - r, sx + r, sy + r)
        for it, kind, pos in self.grid_items:
            if kind == 0:
                c.coords(it, pos + ox, oy, pos + ox, H + oy)
            else:
                c.coords(it, ox, pos + oy, W + ox, pos + oy)
        sy = (u * 34.0) % H
        c.coords(self.scanline, ox, sy + oy, W + ox, sy + oy)

    # --------------------------------------------------------------- 커서
    def _build_cursor(self):
        c = self.c
        self.cursor_arcs = [
            c.create_arc(0, 0, 0, 0, start=0, extent=62, style="arc",
                         outline=C_PLAYER, width=2, state="hidden", tags=("cursor",))
            for _ in range(3)
        ]
        self.cursor_ticks = [
            c.create_line(0, 0, 0, 0, fill=C_PLAYER, width=2,
                          state="hidden", tags=("cursor",))
            for _ in range(4)
        ]
        self.cursor_cross = [
            c.create_line(0, 0, 0, 0, fill=C_PLAYER, width=1,
                          state="hidden", tags=("cursor",))
            for _ in range(2)
        ]
        self.cursor_dot = c.create_oval(0, 0, 0, 0, fill="#ffffff", outline="",
                                        state="hidden", tags=("cursor",))
        self.cursor_hit_marks = [
            c.create_line(0, 0, 0, 0, fill="#ffffff", width=2,
                          state="hidden", tags=("cursor",))
            for _ in range(4)
        ]

    def _on_pointer_enter(self, ev=None):
        self.cursor_on = True
        self.c.configure(cursor="none")

    def _on_pointer_leave(self, ev=None):
        self.cursor_on = False
        try:
            self.c.configure(cursor="")
        except tk.TclError:
            pass

    def _on_focus_out(self, ev=None):
        self.keys.clear()
        self.cursor_on = False
        try:
            self.c.configure(cursor="")
        except tk.TclError:
            pass

    def draw_cursor(self):
        c = self.c
        items = (self.cursor_arcs + self.cursor_ticks + self.cursor_cross
                 + [self.cursor_dot] + self.cursor_hit_marks)
        if not self.cursor_on:
            for it in items:
                c.itemconfigure(it, state="hidden")
            return
        x, y = self.mx, self.my
        u = self.ui_t
        p = self.p
        dash = clamp(p.dash_t / 0.145, 0, 1) if p.dash_t > 0 else 0.0
        hot = False
        for e in self.enemies:
            if e.hp > 0 and (e.x - x) ** 2 + (e.y - y) ** 2 < (e.r + 8) ** 2:
                hot = True
                break
        if self.state == "upgrade" and self.hover_card >= 0:
            hot = True
        self.cursor_hot = hot
        if hot:
            col = C_GOLD if self.state == "upgrade" else C_BAD
        elif self.state == "title":
            col = "#7ef0ff"
        else:
            col = C_PLAYER
        heat = clamp(p.recoil, 0, 1) if self.state == "play" else 0.0
        self.cursor_spin = getattr(self, "cursor_spin", 0.0) + \
            (0.9 + heat * 7.5 + dash * 13.0) * (1.0 / 60.0)
        rad = 17.0 + 1.7 * math.sin(u * 3.8) + heat * 2.4 + dash * 11.0
        if hot:
            rad += 1.0 + 0.8 * math.sin(u * 12.0)
        # 회전하는 3개의 호
        for i, it in enumerate(self.cursor_arcs):
            a0 = math.degrees(self.cursor_spin) + i * 120.0
            c.coords(it, x - rad, y - rad, x + rad, y + rad)
            c.itemconfigure(it, start=a0, extent=62, outline=col,
                            width=2 if not hot else 3, state="normal")
        # 바깥 4방향 틱
        for i, it in enumerate(self.cursor_ticks):
            a = self.cursor_spin * 0.55 + TAU * i / 4.0
            r0, r1 = rad + 3.0, rad + 9.0
            c.coords(it, x + math.cos(a) * r0, y + math.sin(a) * r0,
                     x + math.cos(a) * r1, y + math.sin(a) * r1)
            c.itemconfigure(it, fill=col, state="normal")
        # 중앙 십자선 (작은 간격)
        gap = 4.5 + heat * 2.5 + dash * 5.0
        r2 = 12.0 + dash * 7.0
        c.coords(self.cursor_cross[0], x - r2, y, x - gap, y)
        c.coords(self.cursor_cross[1], x + gap, y, x + r2, y)
        for it in self.cursor_cross:
            c.itemconfigure(it, fill=col, state="normal")
        dr = 2.0 + 0.5 * math.sin(u * 9.0)
        c.coords(self.cursor_dot, x - dr, y - dr, x + dr, y + dr)
        c.itemconfigure(self.cursor_dot, fill="#ffffff" if not hot else col,
                        state="normal")
        # 명중 표시(히트마커)
        if self.cursor_hit > 0:
            k = clamp(self.cursor_hit / 0.15, 0, 1)
            for i, it in enumerate(self.cursor_hit_marks):
                a = math.pi / 4.0 + math.pi / 2.0 * i
                r0 = 9.0 + (1.0 - k) * 7.0
                r1 = 16.0 + (1.0 - k) * 9.0
                c.coords(it, x + math.cos(a) * r0, y + math.sin(a) * r0,
                         x + math.cos(a) * r1, y + math.sin(a) * r1)
                c.itemconfigure(it, fill="#ffffff" if hot else C_GOLD,
                                state="normal")
        else:
            for it in self.cursor_hit_marks:
                c.itemconfigure(it, state="hidden")

    # ------------------------------------------------------------------- HUD
    def _build_hud(self):
        c = self.c
        self.hp_bg = c.create_rectangle(24, 22, 264, 34, fill="#0d1626",
                                        outline="#1d2c46")
        self.hp_chip = c.create_rectangle(24, 22, 264, 34, fill="#7a2038",
                                          outline="")
        self.hp_fg = c.create_rectangle(24, 22, 264, 34, fill=C_HP, outline="")
        self.hp_txt = c.create_text(144, 28, text="100", fill="#04121a",
                                    font=("Consolas", 9, "bold"))
        self.wave_txt = c.create_text(W - 24, 26, text="WAVE 1",
                                      fill=C_TEXT, font=F_BIG, anchor="e")
        self.score_txt = c.create_text(W - 24, 58, text="0",
                                       fill=C_DIM, font=F_NUM, anchor="e")
        self.combo_txt = c.create_text(W / 2, 26, text="", fill=C_GOLD,
                                       font=F_BIG, anchor="center")
        self.dash_pip_bgs = [
            c.create_rectangle(0, 0, 0, 0, fill="#0b1a26",
                               outline="#16303e") for _ in range(4)
        ]
        self.dash_pips = [
            c.create_rectangle(0, 0, 0, 0, fill=C_PLAYER, outline="")
            for _ in range(4)
        ]
        self.upg_txt = c.create_text(24, H - 24, text="", fill=C_DIM,
                                     font=F_MONO, anchor="sw", justify="left")
        self.boss_bg = c.create_rectangle(W / 2 - 300, 84, W / 2 + 300, 96,
                                          fill="#1a0a1e", outline="#3a1a42")
        self.boss_fg = c.create_rectangle(W / 2 - 300, 84, W / 2 + 300, 96,
                                          fill=C_BOSS, outline="")
        self.boss_txt = c.create_text(W / 2, 62, text="OVERSEER", fill=C_BOSS,
                                      font=F_MID, anchor="center")
        self.hud = [self.hp_bg, self.hp_chip, self.hp_fg, self.hp_txt,
                    self.wave_txt, self.score_txt, self.combo_txt, self.upg_txt]
        self.hud += self.dash_pip_bgs
        self.hud += self.dash_pips
        self.hud += [self.boss_bg, self.boss_fg, self.boss_txt]
        self._show_hud(False)

        self.banner_item = c.create_text(W / 2, H / 2 - 150, text="",
                                         fill=C_TEXT, font=F_HUGE)
        self.banner_item2 = c.create_text(W / 2, H / 2 - 112, text="",
                                          fill=C_DIM, font=F_UI)
        self.banner_rule = c.create_line(W / 2, H / 2 - 128, W / 2, H / 2 - 128,
                                         fill=C_PLAYER, width=2)
        # 사운드 상태 표시 (HUD와 무관하게 항상 보임)
        self.audio_txt = c.create_text(W - 24, H - 24, text="", fill=C_DIM,
                                       font=F_MONO, anchor="se")

    def _show_hud(self, on):
        st = "normal" if on else "hidden"
        for it in self.hud:
            self.c.itemconfigure(it, state=st)

    def _update_hud(self):
        c = self.c
        p = self.p
        ratio = clamp(p.hp / p.maxhp, 0, 1)
        # 잔상 체력바: 피해를 입으면 붉은 바가 천천히 따라 줄어든다
        if ratio < self.hp_ratio_chip:
            self.hp_ratio_chip += (ratio - self.hp_ratio_chip) * min(1.0, 3.2 * DT)
            if self.hp_ratio_chip - ratio < 0.004:
                self.hp_ratio_chip = ratio
        else:
            self.hp_ratio_chip = ratio
        c.coords(self.hp_chip, 26, 24, 26 + 236 * self.hp_ratio_chip, 32)
        c.coords(self.hp_fg, 26, 24, 26 + 236 * ratio, 32)
        if ratio > 0.5:
            col = C_HP
        elif ratio > 0.25:
            col = C_GOLD
        else:
            col = C_BAD if math.sin(self.ui_t * 9.0) > -0.2 else "#ff9bb0"
        c.itemconfigure(self.hp_fg, fill=col)
        c.itemconfigure(self.hp_txt, text=f"{int(p.hp)} / {int(p.maxhp)}")

        # 점수 카운트업
        d = self.score - self.score_shown
        if abs(d) < 1.0:
            self.score_shown = float(self.score)
        else:
            self.score_shown += d * min(1.0, 9.0 * DT)
        c.itemconfigure(self.score_txt, text=f"{int(self.score_shown):,}",
                        fill=C_GOLD if abs(d) > 1.0 else C_DIM)

        # 웨이브 팝
        c.itemconfigure(self.wave_txt, text=f"WAVE {self.wave}")
        c.coords(self.wave_txt, W - 24, 26 - 9.0 * self.wave_pop)
        c.itemconfigure(self.wave_txt,
                        fill="#ffffff" if self.wave_pop > 0.45 else C_TEXT)

        if self.combo > 1:
            c.coords(self.combo_txt, W / 2, 26 - 8.0 * self.combo_pop)
            c.itemconfigure(self.combo_txt,
                            text=f"x{self.combo_mult():.1f}  {self.combo} COMBO",
                            fill="#ffffff" if self.combo_pop > 0.35 else C_GOLD,
                            state="normal")
        else:
            c.itemconfigure(self.combo_txt, state="hidden")

        n = int(p.st["dash_max"])
        for i in range(4):
            bg = self.dash_pip_bgs[i]
            fg = self.dash_pips[i]
            if i >= n:
                c.itemconfigure(bg, state="hidden")
                c.itemconfigure(fg, state="hidden")
                continue
            x0 = 24 + i * 30
            c.coords(bg, x0, 44, x0 + 24, 50)
            c.itemconfigure(bg, state="normal")
            if i < p.charges:
                fill = C_PLAYER
                if p.charges >= n and math.sin(self.ui_t * 6.4) > 0.7:
                    fill = "#b8ffff"
                if self.dash_ready_t > 0:
                    fill = "#ffffff" if self.dash_ready_t > 0.35 else fill
                c.coords(fg, x0, 44, x0 + 24, 50)
                c.itemconfigure(fg, fill=fill, state="normal")
            elif i == p.charges and p.charge_t > 0:
                w = 24.0 * clamp(p.charge_t / max(p.dash_cd, 0.01), 0, 1)
                c.coords(fg, x0, 44, x0 + max(1.0, w), 50)
                c.itemconfigure(fg, fill="#2b7f8f", state="normal")
            else:
                c.itemconfigure(fg, state="hidden")

        if self.levels:
            parts = []
            for uid, lv in sorted(self.levels.items(), key=lambda kv: -kv[1]):
                parts.append(f"{UP_BY_ID[uid][1]} x{lv}")
            c.itemconfigure(self.upg_txt, text=" · ".join(parts[:7]))
        else:
            c.itemconfigure(self.upg_txt, text="")

        if self.boss is not None and self.boss.hp > 0:
            r = clamp(self.boss.hp / self.boss.maxhp, 0, 1)
            c.coords(self.boss_bg, W / 2 - 300, 84, W / 2 + 300, 96)
            c.coords(self.boss_fg, W / 2 - 298, 86,
                     W / 2 - 298 + 596 * r, 94)
            pulse = 0.75 + 0.25 * math.sin(self.ui_t * 6.0)
            c.itemconfigure(self.boss_fg,
                            fill=C_BOSS if pulse > 0.85 else "#ff8cf6")
            for it in (self.boss_bg, self.boss_fg, self.boss_txt):
                c.itemconfigure(it, state="normal")
        else:
            for it in (self.boss_bg, self.boss_fg, self.boss_txt):
                c.itemconfigure(it, state="hidden")

    # ----------------------------------------------------------------- 타이틀
    def _build_title(self):
        c = self.c
        c.delete("scr")
        self.title_age = 0.0
        self.hover_card = -1
        self._show_hud(False)
        # 색수차 글리치용 복제 레이어
        self.title_glitch_a = c.create_text(W / 2 + 4, 190, text="NEON VOID",
                                            fill="#ff2c66", font=F_TITLE,
                                            state="hidden", tags="scr")
        self.title_glitch_b = c.create_text(W / 2 - 4, 190, text="NEON VOID",
                                            fill="#24d7ff", font=F_TITLE,
                                            state="hidden", tags="scr")
        self.title_logo = c.create_text(W / 2, 190, text="NEON VOID", fill=C_PLAYER,
                                        font=F_TITLE, tags="scr")
        self.title_sub = c.create_text(W / 2, 250, text="", fill=C_DIM,
                                       font=F_MID, tags="scr")
        self.title_line = c.create_line(W / 2 - 240, 278, W / 2 + 240, 278,
                                        fill="#1d4a55", width=2, tags="scr")
        lines = [
            "WASD / 방향키  ·  이동",
            "마우스  ·  조준 (자동 사격)",
            "Space / Shift  ·  이동 방향 대시 (무적)",
            "P  ·  일시정지      R  ·  재시작      M  ·  음소거",
        ]
        for i, t in enumerate(lines):
            c.create_text(W / 2, 330 + i * 26, text=t, fill=C_TEXT,
                          font=F_UI, tags="scr")
        c.create_text(W / 2, 480,
                      text="웨이브를 넘길 때마다 업그레이드 1장을 고르세요",
                      fill=C_DIM, font=F_UI, tags="scr")
        best = self.save["best_score"]
        c.create_text(W / 2, 512,
                      text=f"최고 기록  {best:,}  ·  최고 웨이브  {self.save['best_wave']}",
                      fill=C_GOLD, font=F_HUD, tags="scr")
        self.title_press = c.create_text(W / 2, 600, text="PRESS  ENTER",
                                         fill=C_PLAYER, font=F_BIG, tags="scr")
        c.create_text(W / 2, 660, text="단일 파일 · 표준 라이브러리만 사용",
                      fill="#22374d", font=F_MONO, tags="scr")
        # 커서가 항상 위에 오도록
        c.tag_raise("cursor")

    def _render_title(self):
        c = self.c
        u = self.ui_t
        age = self.title_age
        bob = math.sin(u * 1.7) * 5.0
        sway = math.sin(u * 0.9) * 3.0
        glitch = (u % 3.9) < 0.14
        gx = rnd(-5, 5) if glitch else 0.0
        gy = rnd(-2, 2) if glitch else 0.0
        c.coords(self.title_logo, W / 2 + sway + gx, 190 + bob + gy)
        c.itemconfigure(self.title_logo,
                        fill="#ffffff" if glitch else C_PLAYER)
        if glitch:
            c.coords(self.title_glitch_a, W / 2 + sway + gx + 5, 190 + bob - 2)
            c.coords(self.title_glitch_b, W / 2 + sway + gx - 5, 190 + bob + 2)
            c.itemconfigure(self.title_glitch_a, state="normal")
            c.itemconfigure(self.title_glitch_b, state="normal")
        else:
            c.itemconfigure(self.title_glitch_a, state="hidden")
            c.itemconfigure(self.title_glitch_b, state="hidden")

        full = "네온 로그라이크 탄막 슈터"
        n = int(clamp(age * 13.0, 0, len(full)))
        caret = "▌" if (age > len(full) / 13.0 and int(u * 3.0) % 2 == 0) else ""
        c.itemconfigure(self.title_sub, text=full[:n] + caret)

        v = 0.5 + 0.5 * math.sin(u * 3.4)
        col = "#6ff2ff" if v > 0.5 else "#2c6b78"
        c.itemconfigure(self.title_press, fill=col)
        c.coords(self.title_press, W / 2, 600 + math.sin(u * 3.0) * 4.0)
        x = (u * 220) % (W + 200) - 100
        c.coords(self.title_line, max(W / 2 - 240, x), 278,
                 min(W / 2 + 240, x + 120), 278)

    # ------------------------------------------------------------------ 입력
    def on_key(self, ev):
        k = ev.keysym.lower()
        self.keys.add(k)

        if k == "m":
            self.muted = self.audio.toggle_mute()
            self.mute_t = 1.8
            return

        if self.state == "title":
            if k in ("return", "kp_enter", "space"):
                self.new_game()
            elif k == "escape":
                self.root.destroy()
            return

        if k in ("p",) or (k == "escape" and self.state == "play"):
            if self.state == "play":
                self.paused = not self.paused
                self._draw_pause()
            return

        if self.state == "dead":
            if k in ("r", "return", "space"):
                self.new_game()
            elif k == "escape":
                self._build_title()
                self.state = "title"
            return

        if self.state == "upgrade":
            if k in ("1", "2", "3"):
                idx = int(k) - 1
                if idx < len(self.card_opts):
                    self.pick_upgrade(idx)
            return

        if self.state == "play":
            if k in ("space", "shift_l", "shift_r"):
                self.try_dash()
            elif k == "r":
                self.new_game()

    def on_key_up(self, ev):
        self.keys.discard(ev.keysym.lower())

    def on_motion(self, ev):
        self.mx, self.my = ev.x, ev.y
        self.cursor_on = True
        if self.state == "upgrade":
            self.hover_card = -1
            for idx, (x0, y0, x1, y1) in enumerate(self.cards):
                off = self.card_yoff[idx] if idx < len(self.card_yoff) else 0.0
                if x0 <= ev.x <= x1 and y0 + off <= ev.y <= y1 + off:
                    self.hover_card = idx
                    break
        else:
            self.hover_card = -1

    def on_mouse_down(self, ev):
        self.mouse_down = True
        if self.state == "title":
            self.new_game()
        elif self.state == "dead":
            self.new_game()
        elif self.state == "upgrade":
            for idx, (x0, y0, x1, y1) in enumerate(self.cards):
                off = self.card_yoff[idx] if idx < len(self.card_yoff) else 0.0
                if x0 <= ev.x <= x1 and y0 + off <= ev.y <= y1 + off:
                    self.pick_upgrade(idx)
                    break

    def on_mouse_up(self, ev):
        self.mouse_down = False

    # ------------------------------------------------------------------ 상태
    def new_game(self):
        c = self.c
        c.delete("scr")
        c.delete("ovl")
        self._show_hud(True)
        self.state = "play"
        self.paused = False
        self.p = Player()
        self.enemies = []
        self.bullets = []
        self.ebullets = []
        self.particles = []
        self.shards = []
        self.ftexts = []
        self.chain_fx = []
        self.levels = {}
        self.wave = 0
        self.score = 0
        self.combo = 0
        self.combo_t = 0.0
        self.hitstop = 0.0
        self.slow = 0.0
        self.shake = 0.0
        self.kills = 0
        self.boss = None
        self.spawn_queue = []
        self.wave_done_t = 0.0
        self.banner = ""
        self.banner_t = 0.0
        self.dead_t = 0.0
        self.lowhp_t = 0.0
        self.combo_mark = 0
        self.hp_ratio_chip = 1.0
        self.score_shown = 0.0
        self.wave_pop = 0.0
        self.combo_pop = 0.0
        self.dash_ready_t = 0.0
        self.prev_charges = 1
        self.hover_card = -1
        self.card_anim = []
        self.card_yoff = []
        self.pause_t = 0.0
        self.go_t = 0.0
        self.go_score = 0
        self.go_kills = 0
        self.go_record = False
        self.cursor_hit = 0.0
        self.screen_flash(0.32, C_PLAYER)
        self.screen_sweep(0.5, C_PLAYER)
        self.save["runs"] += 1
        write_save(self.save)
        if not self.music_started:
            self.music_started = True
            self.audio.start_music()
        self.start_wave(1)

    def start_wave(self, n):
        self.wave = n
        self.spawn_queue = []
        self.wave_done_t = 0.0
        boss_wave = (n % 5 == 0)
        if boss_wave:
            self.banner = f"WAVE {n}  ·  BOSS"
            self.banner_t = 2.4
            self.spawn_queue.append((0.7, "boss"))
            extra = 2 + n // 5
            for i in range(extra):
                self.spawn_queue.append((2.2 + i * 1.4,
                                         self.rng.choice(["drifter", "shooter"])))
            self.audio.play("boss", 0.5)
        else:
            self.banner = f"WAVE {n}"
            self.banner_t = 1.6
            self.audio.play("wave", 0.9)
            budget = 8.0 + n * 4.0
            pool = ["drifter"]
            if n >= 2:
                pool += ["shooter"]
            if n >= 3:
                pool += ["splitter"]
            if n >= 4:
                pool += ["rusher"]
            t = 0.4
            guard = 0
            while budget > 0 and guard < 200:
                guard += 1
                kind = self.rng.choice(pool)
                cost = SPAWN_COST[kind]
                if cost > budget and kind != "drifter":
                    kind = "drifter"
                    cost = 1.0
                budget -= cost
                self.spawn_queue.append((t, kind))
                t += max(0.22, 0.62 - n * 0.015)
        self.state = "play"

    def combo_mult(self):
        return 1.0 + min(self.combo, 40) * 0.1

    # ------------------------------------------------------------------ 스폰
    def spawn_point(self):
        side = self.rng.randrange(4)
        if side == 0:
            return rnd(60, W - 60), -50.0
        if side == 1:
            return rnd(60, W - 60), H + 50.0
        if side == 2:
            return -50.0, rnd(60, H - 60)
        return W + 50.0, rnd(60, H - 60)

    def make_enemy(self, kind, x=None, y=None, scale=1.0):
        prm = E_PARAMS[kind]
        e = Enemy()
        e.kind = kind
        if x is None or y is None:
            e.x, e.y = self.spawn_point()
        else:
            e.x, e.y = x, y
        e.r = prm["r"]
        e.spd = prm["spd"]
        e.dmg = prm["dmg"]
        e.score = prm["score"]
        if kind == "boss":
            e.maxhp = prm["hp"] + 380 * (self.wave // 5 - 1)
            e.cd = 1.6
        else:
            e.maxhp = prm["hp"] * (1.0 + 0.13 * (self.wave - 1)) * scale
        e.maxhp = max(4.0, e.maxhp)
        e.hp = e.maxhp
        e.t = 0.0
        e.state = 0
        e.cd = rnd(1.0, 2.4) if kind == "shooter" else 0.0
        e.flash = 0.0
        if kind == "boss":
            e.x, e.y = W / 2, -80.0
            self.boss = e
        self.enemies.append(e)
        if kind != "boss":
            self.burst(e.x, e.y, 6, prm["color"], 90, 0.35, 2.2)
        return e

    # ------------------------------------------------------------- 이펙트
    def burst(self, x, y, n, color, spd=160, life=0.45, size=2.4, drag=2.6):
        if len(self.particles) > 380:
            return
        for _ in range(n):
            a = rnd(0, TAU)
            s = rnd(spd * 0.35, spd)
            self.particles.append(Particle(
                x, y, math.cos(a) * s, math.sin(a) * s,
                rnd(life * 0.55, life), rnd(0.9, size), color, drag))

    def ring(self, x, y, n, color, spd=240, life=0.4):
        for i in range(n):
            a = TAU * i / n
            self.particles.append(Particle(
                x, y, math.cos(a) * spd, math.sin(a) * spd,
                life, 2.2, color, 3.4))

    def pop_text(self, x, y, text, color):
        if len(self.ftexts) < 40:
            self.ftexts.append(FText(x, y, text, color))

    def screen_flash(self, dur=0.3, color=C_PLAYER):
        self.flash_t = dur
        self.flash_dur = max(dur, 0.01)
        self.flash_col = color

    def screen_sweep(self, dur=0.55, color=C_PLAYER):
        self.sweep_t = dur
        self.sweep_dur = max(dur, 0.01)
        self.sweep_col = color

    # --------------------------------------------------------------- 대시
    def try_dash(self):
        p = self.p
        if p.dash_t > 0 or p.charges <= 0:
            return
        dx = self.mx - p.x
        dy = self.my - p.y
        d = math.hypot(dx, dy) or 1.0
        p.dash_dir = (dx / d, dy / d)
        p.dash_t = 0.145
        p.inv = max(p.inv, 0.32)
        p.charges -= 1
        self.audio.play("dash", 0.1)
        self.burst(p.x, p.y, 10, C_PLAYER, 130, 0.3, 2.0)
        if p.st["dash_blast"] > 0:
            self.dash_blast(p)

    def dash_blast(self, p):
        dmg = p.st["dash_blast"]
        for e in list(self.enemies):
            if math.hypot(e.x - p.x, e.y - p.y) < 78 + e.r:
                self.damage_enemy(e, dmg, C_PLAYER,
                                  knock=(p.dash_dir[0] * 140,
                                         p.dash_dir[1] * 140))

    # --------------------------------------------------------------- 사격
    def shoot(self, ax, ay, angle, dmg_mult=1.0, radius_mul=1.0):
        p = self.p
        st = p.st
        b = Bullet()
        b.x, b.y = ax, ay
        spd = st["bspeed"]
        b.vx = math.cos(angle) * spd
        b.vy = math.sin(angle) * spd
        b.dmg = st["dmg"] * dmg_mult
        b.r = 3.6 * radius_mul
        b.life = 1.4
        b.pierce = int(st["pierce"])
        b.homing = st["homing"]
        b.explode = st["explode"]
        b.ricochet = int(st["ricochet"])
        b.crit = st["crit"]
        b.chain = int(st["chain"])
        b.color = C_BULLET if dmg_mult >= 1.0 else "#8fe0ff"
        self.bullets.append(b)

    def player_shoot(self):
        p = self.p
        st = p.st
        p.fire_cd -= DT
        if p.fire_cd > 0:
            return
        p.fire_cd = 1.0 / max(0.8, st["rate"])
        p.recoil = 1.0
        ax = p.x + math.cos(p.aim) * 16
        ay = p.y + math.sin(p.aim) * 16
        self.shoot(ax, ay, p.aim)
        n = int(st["split"])
        for i in range(n):
            off = 0.14 * (i + 1)
            self.shoot(ax, ay, p.aim + off, 0.85)
            self.shoot(ax, ay, p.aim - off, 0.85)
        if st["rear"]:
            self.shoot(p.x, p.y, p.aim + math.pi, 0.7)
        self.audio.play("shoot", 0.075)

    def enemy_shoot(self, e, count=1, spread=0.16, spd=330.0, color=None):
        p = self.p
        base = math.atan2(p.y - e.y, p.x - e.x)
        col = color or C_EBULLET
        for i in range(count):
            a = base + (i - (count - 1) / 2.0) * spread
            b = Bullet()
            b.friendly = False
            b.x, b.y = e.x + math.cos(a) * (e.r + 2), e.y + math.sin(a) * (e.r + 2)
            b.vx = math.cos(a) * spd
            b.vy = math.sin(a) * spd
            b.dmg = 9.0
            b.r = 4.2
            b.life = 6.0
            b.color = col
            self.ebullets.append(b)
        self.audio.play("eshoot", 0.14)

    def enemy_radial(self, e, count, spd=250.0, col=None, offset=0.0):
        col = col or C_BOSS
        for i in range(count):
            a = offset + TAU * i / count
            b = Bullet()
            b.friendly = False
            b.x, b.y = e.x + math.cos(a) * (e.r + 4), e.y + math.sin(a) * (e.r + 4)
            b.vx = math.cos(a) * spd
            b.vy = math.sin(a) * spd
            b.dmg = 10.0
            b.r = 4.6
            b.life = 7.0
            b.color = col
            self.ebullets.append(b)
        self.burst(e.x, e.y, 8, col, 140, 0.35, 2.4)
        self.audio.play("hshot", 0.2)

    # --------------------------------------------------------------- 피해
    def damage_enemy(self, e, dmg, color=C_BULLET, knock=None, crit=False):
        if e.hp <= 0:
            return
        e.hp -= dmg
        e.flash = 1.0
        if knock:
            e.vx += knock[0]
            e.vy += knock[1]
        self.pop_text(e.x + rnd(-6, 6), e.y - e.r - 4,
                      f"{int(dmg)}" if not crit else f"{int(dmg)}!",
                      C_GOLD if crit else color)
        if e.hp <= 0:
            self.kill_enemy(e)

    def kill_enemy(self, e):
        if e.kind == "boss":
            self.boss = None
            self.shake = 26.0
            self.hitstop = 0.16
            self.audio.play("boom", 0.2)
            for i in range(6):
                self.ring(e.x, e.y, 18, C_BOSS, 260 + i * 60, 0.7)
            self.burst(e.x, e.y, 60, C_BOSS, 420, 0.9, 3.4)
            self.pop_text(e.x, e.y - 40, "OVERSEER DOWN", C_GOLD)
        else:
            self.shake = max(self.shake, 5.0)
            self.hitstop = max(self.hitstop, 0.028)
            self.burst(e.x, e.y, 14, self._enemy_color(e), 200, 0.5, 2.6)
            self.ring(e.x, e.y, 8, self._enemy_color(e), 190, 0.3)
            self.audio.play("kill", 0.06)

        self.combo += 1
        self.combo_t = 2.6
        self.score += int(e.score * self.combo_mult())
        self.kills += 1
        if self.combo % 10 == 0:
            self.audio.play("combo", 0.15)
            self.pop_text(self.p.x, self.p.y - 52, f"{self.combo} COMBO",
                          C_GOLD)

        st = self.p.st
        if st["lifesteal"] > 0:
            self.p.hp = min(self.p.maxhp, self.p.hp + st["lifesteal"] * 0.5)
        if st["slow"] > 0:
            self.slow = min(0.6, self.slow + st["slow"])

        if e.kind == "splitter":
            for _ in range(2):
                m = self.make_enemy("mini", e.x + rnd(-14, 14), e.y + rnd(-14, 14))
                m.vx = rnd(-120, 120)
                m.vy = rnd(-120, 120)

        if e.kind != "mini" or self.rng.random() < 0.3:
            if self.rng.random() < 0.55:
                self.shards.append(Shard(e.x, e.y, int(e.score * 0.6)))

        if e in self.enemies:
            self.enemies.remove(e)

    def _enemy_color(self, e):
        if e.flash > 0.4:
            return "#ffffff"
        return E_PARAMS[e.kind]["color"]

    # ------------------------------------------------------------------ 스텝
    def step(self, dt):
        self.t += dt
        self.frames += 1
        self.title_pulse += dt

        if self.banner_t > 0:
            self.banner_t -= dt
        if self.mute_t > 0:
            self.mute_t -= dt

        if self.hitstop > 0:
            self.hitstop -= dt
            dts = dt * 0.16
        else:
            dts = dt
        if self.slow > 0:
            self.slow -= dt
            dts *= 0.45

        self.shake *= (1.0 - 7.0 * dt)
        if self.shake < 0.15:
            self.shake = 0.0

        if self.state == "title":
            self._step_fx(dts)
            if self.auto:
                self.new_game()
            return

        if self.state == "upgrade":
            self._step_fx(dts * 0.4)
            self.auto_pick_t += dt
            if self.auto and self.auto_pick_t > 0.25 and self.card_opts:
                self.pick_upgrade(self.rng.randrange(len(self.card_opts)))
            return

        if self.state == "dead":
            self._step_fx(dts)
            return

        if self.paused:
            return

        self._step_fx(dts)
        self.step_player(dt, dts)
        self.step_spawns(dt)
        self.step_enemies(dts)
        self.build_grid()
        self.step_bullets(dts)
        self.step_shards(dts)
        self.step_collisions(dt)
        if self.combo_t > 0:
            self.combo_t -= dt
            if self.combo_t <= 0:
                self.combo = 0
        self.check_wave(dt)

        # 저체력 심박음
        if self.p.hp / self.p.maxhp < 0.3:
            self.lowhp_t -= dt
            if self.lowhp_t <= 0:
                self.lowhp_t = 1.15
                self.audio.play("lowhp", 0.7)
        else:
            self.lowhp_t = 0.0

        if self.auto:
            self.auto_play(dt)

    def _step_fx(self, dt):
        for q in self.particles:
            q.life -= dt
            q.x += q.vx * dt
            q.y += q.vy * dt
            f = 1.0 - q.drag * dt
            q.vx *= f
            q.vy *= f
        if self.particles:
            self.particles = [q for q in self.particles if q.life > 0]

        for ft in self.ftexts:
            ft.life -= dt
            ft.y += ft.vy * dt
            ft.vy *= (1.0 - 2.2 * dt)
        if self.ftexts:
            self.ftexts = [f for f in self.ftexts if f.life > 0]

        self.p.recoil = max(0.0, self.p.recoil - dt * 6.0)

        for i in range(len(self.chain_fx) - 1, -1, -1):
            x1, y1, x2, y2, life = self.chain_fx[i]
            life -= dt
            if life <= 0:
                self.chain_fx.pop(i)
            else:
                self.chain_fx[i] = (x1, y1, x2, y2, life)

    def step_player(self, dt, dts):
        p = self.p
        st = p.st

        p.aim = math.atan2(self.my - p.y, self.mx - p.x)

        ax = ay = 0.0
        if "a" in self.keys or "left" in self.keys:
            ax -= 1.0
        if "d" in self.keys or "right" in self.keys:
            ax += 1.0
        if "w" in self.keys or "up" in self.keys:
            ay -= 1.0
        if "s" in self.keys or "down" in self.keys:
            ay += 1.0
        if ax or ay:
            m = math.hypot(ax, ay)
            ax /= m
            ay /= m

        if p.dash_t > 0:
            p.dash_t -= dts
            dx, dy = p.dash_dir
            p.vx = dx * 940.0
            p.vy = dy * 940.0
            if st["dash_blast"] > 0 and self.frames % 3 == 0:
                for e in list(self.enemies):
                    if math.hypot(e.x - p.x, e.y - p.y) < 70 + e.r:
                        self.damage_enemy(e, st["dash_blast"] * 0.34, C_PLAYER,
                                          knock=(dx * 90, dy * 90))
            self.particles.append(Particle(
                p.x, p.y, rnd(-30, 30), rnd(-30, 30), 0.3, 6.0,
                "#1b93a8", 3.0))
        else:
            target_vx = ax * st["speed"]
            target_vy = ay * st["speed"]
            k = clamp(9.0 * dt, 0, 1)
            p.vx += (target_vx - p.vx) * k
            p.vy += (target_vy - p.vy) * k

        p.x += p.vx * dts
        p.y += p.vy * dts
        if p.x < p.r:
            p.x = p.r
            p.vx = 0
        elif p.x > W - p.r:
            p.x = W - p.r
            p.vx = 0
        if p.y < p.r:
            p.y = p.r
            p.vy = 0
        elif p.y > H - p.r:
            p.y = H - p.r
            p.vy = 0

        if p.inv > 0:
            p.inv -= dt

        if p.charges < st["dash_max"]:
            p.charge_t += dt
            if p.charge_t >= p.dash_cd:
                p.charge_t = 0.0
                p.charges += 1
        else:
            p.charge_t = 0.0

        self.player_shoot()

        if st["drone"] > 0:
            p.drone_ang += dt * 1.9
            self.update_drones(dt)

    def update_drones(self, dt):
        p = self.p
        n = int(p.st["drone"])
        self._drone_cd -= dt
        if self._drone_cd > 0:
            return
        self._drone_cd = 0.55
        for i in range(n):
            a = p.drone_ang + TAU * i / n
            dx = p.x + math.cos(a) * 52
            dy = p.y + math.sin(a) * 52
            tgt = self.nearest_enemy(dx, dy, 460)
            if tgt is None:
                continue
            ang = math.atan2(tgt.y - dy, tgt.x - dx)
            self.shoot(dx, dy, ang, 0.62, 0.8)

    def nearest_enemy(self, x, y, radius, exclude=None):
        best = None
        bd = radius * radius
        for e in self.enemies:
            if e is exclude or e.hp <= 0:
                continue
            d = (e.x - x) ** 2 + (e.y - y) ** 2
            if d < bd:
                bd = d
                best = e
        return best

    def step_spawns(self, dt):
        if not self.spawn_queue:
            return
        alive = len(self.enemies)
        rest = []
        for t, kind in self.spawn_queue:
            t -= dt
            if t <= 0 and alive < 88:
                self.make_enemy(kind)
                alive += 1
            else:
                rest.append((t, kind))
        self.spawn_queue = rest

    def step_enemies(self, dt):
        p = self.p
        for e in list(self.enemies):
            if e.hp <= 0:
                continue
            e.flash = max(0.0, e.flash - dt * 5.0)
            if e.kind in ("shooter", "splitter"):
                e.t += dt
            dx = p.x - e.x
            dy = p.y - e.y
            d = math.hypot(dx, dy) or 1.0
            ux, uy = dx / d, dy / d
            k = e.kind

            if k in ("drifter", "mini"):
                e.vx += (ux * e.spd - e.vx) * min(1.0, 4.5 * dt)
                e.vy += (uy * e.spd - e.vy) * min(1.0, 4.5 * dt)
                e.ang = math.atan2(uy, ux)

            elif k == "shooter":
                want = 330.0
                if d > want + 60:
                    mv = 1.0
                elif d < want - 60:
                    mv = -1.0
                else:
                    mv = 0.0
                e.vx += (ux * e.spd * mv - uy * e.spd * 0.55 - e.vx) * min(1.0, 3.0 * dt)
                e.vy += (uy * e.spd * mv + ux * e.spd * 0.55 - e.vy) * min(1.0, 3.0 * dt)
                e.cd -= dt
                if e.cd <= 0 and d < 700:
                    e.cd = rnd(1.9, 2.6)
                    self.enemy_shoot(e, 3, 0.18, 330.0)
                    self.burst(e.x, e.y, 3, C_SHOOT, 60, 0.2)

            elif k == "splitter":
                e.vx += (ux * e.spd - e.vx) * min(1.0, 3.4 * dt)
                e.vy += (uy * e.spd - e.vy) * min(1.0, 3.4 * dt)

            elif k == "rusher":
                if e.state == 0:
                    e.vx += (ux * e.spd - e.vx) * min(1.0, 4.0 * dt)
                    e.vy += (uy * e.spd - e.vy) * min(1.0, 4.0 * dt)
                    e.ang = math.atan2(uy, ux)
                    if d < 320:
                        e.state = 1
                        e.t = 0.55
                        e.vx *= 0.2
                        e.vy *= 0.2
                elif e.state == 1:
                    e.t -= dt
                    e.ang = math.atan2(dy, dx)
                    if e.t <= 0:
                        e.state = 2
                        e.t = 0.5
                        e.vx = math.cos(e.ang) * 780
                        e.vy = math.sin(e.ang) * 780
                        self.burst(e.x, e.y, 5, C_RUSH, 90, 0.2)
                elif e.state == 2:
                    e.t -= dt
                    if e.t <= 0:
                        e.state = 3
                        e.t = 0.9
                else:
                    e.t -= dt
                    e.vx *= (1.0 - 3.0 * dt)
                    e.vy *= (1.0 - 3.0 * dt)
                    if e.t <= 0:
                        e.state = 0

            elif k == "boss":
                self.update_boss(e, dt, d, ux, uy)

            e.x += e.vx * dt
            e.y += e.vy * dt

            if k != "boss":
                e.x = clamp(e.x, -30, W + 30)
                e.y = clamp(e.y, -30, H + 30)

    def update_boss(self, e, dt, d, ux, uy):
        e.t += dt
        hp_r = e.hp / e.maxhp
        phase = 1 if hp_r > 0.66 else (2 if hp_r > 0.33 else 3)
        if phase > e.state:
            if e.state:
                self.audio.play("phase", 0.8)
                self.pop_text(e.x, e.y - 64, f"PHASE {phase}", C_BOSS)
                self.shake = max(self.shake, 15.0)
                self.ring(e.x, e.y, 22, C_BOSS, 320, 0.5)
            e.state = phase

        tx = W / 2 + math.cos(e.t * 0.55) * 300
        ty = H / 2 + math.sin(e.t * 0.85) * 150
        e.vx += ((tx - e.x) * 0.9 - e.vx) * min(1.0, 1.6 * dt)
        e.vy += ((ty - e.y) * 0.9 - e.vy) * min(1.0, 1.6 * dt)

        e.cd -= dt
        if e.cd > 0:
            return
        if phase == 1:
            e.cd = 2.1
            self.enemy_radial(e, 16, 240.0, C_BOSS, e.t)
        elif phase == 2:
            e.cd = 1.35
            self.enemy_radial(e, 22, 270.0, C_BOSS, e.t * 1.7)
            if self.rng.random() < 0.6:
                self.enemy_shoot(e, 5, 0.13, 350.0, C_EBULLET)
                if len(self.enemies) < 40:
                    self.make_enemy("drifter", e.x + rnd(-50, 50),
                                    e.y + rnd(-50, 50))
        else:
            e.cd = 0.95
            self.enemy_radial(e, 26, 300.0, C_BOSS, e.t * 2.4)
            self.enemy_radial(e, 12, 190.0, C_EBULLET, -e.t)
            if self.rng.random() < 0.35:
                self.make_enemy("rusher", e.x, e.y)

    def build_grid(self):
        g = {}
        for e in self.enemies:
            if e.hp <= 0:
                continue
            key = (int(e.x // 96), int(e.y // 96))
            b = g.get(key)
            if b is None:
                g[key] = [e]
            else:
                b.append(e)
        self.grid = g

    def step_bullets(self, dt):
        for b in self.bullets:
            if b.dead:
                continue
            if b.homing > 0:
                tgt = self.nearest_enemy(b.x, b.y, 320)
                if tgt is not None:
                    want = math.atan2(tgt.y - b.y, tgt.x - b.x)
                    cur = math.atan2(b.vy, b.vx)
                    diff = (want - cur + math.pi) % TAU - math.pi
                    cur += clamp(diff, -b.homing * dt, b.homing * dt)
                    sp = math.hypot(b.vx, b.vy)
                    b.vx = math.cos(cur) * sp
                    b.vy = math.sin(cur) * sp
            b.x += b.vx * dt
            b.y += b.vy * dt
            b.life -= dt
            if b.life <= 0:
                b.dead = True
                continue
            if b.x < -60 or b.x > W + 60 or b.y < -60 or b.y > H + 60:
                if b.ricochet > 0:
                    b.ricochet -= 1
                    if b.x < 0 or b.x > W:
                        b.vx = -b.vx
                        b.x = clamp(b.x, 2, W - 2)
                    if b.y < 0 or b.y > H:
                        b.vy = -b.vy
                        b.y = clamp(b.y, 2, H - 2)
                    self.burst(b.x, b.y, 2, b.color, 70, 0.2, 1.8)
                else:
                    b.dead = True
        if self.bullets:
            self.bullets = [b for b in self.bullets if not b.dead]

        p = self.p
        for b in self.ebullets:
            if b.dead:
                continue
            b.x += b.vx * dt
            b.y += b.vy * dt
            b.life -= dt
            if (b.life <= 0 or b.x < -40 or b.x > W + 40
                    or b.y < -40 or b.y > H + 40):
                b.dead = True
                continue
            if p.inv <= 0:
                if (b.x - p.x) ** 2 + (b.y - p.y) ** 2 < (b.r + p.r) ** 2:
                    b.dead = True
                    self.hurt_player(b.dmg)
        if self.ebullets:
            self.ebullets = [b for b in self.ebullets if not b.dead]

    def step_shards(self, dt):
        p = self.p
        mag = p.st["magnet"]
        for s in self.shards:
            s.life -= dt
            dx = p.x - s.x
            dy = p.y - s.y
            d = math.hypot(dx, dy) or 1.0
            if d < mag:
                pull = (1.0 - d / mag) * 1500.0
                s.vx += dx / d * pull * dt
                s.vy += dy / d * pull * dt
            s.x += s.vx * dt
            s.y += s.vy * dt
            s.vx *= (1.0 - 1.2 * dt)
            s.vy *= (1.0 - 1.2 * dt)
            if d < p.r + 8:
                s.life = 0.0
                self.score += s.value
                self.pop_text(p.x, p.y - 26, f"+{s.value}", C_SHARD)
                self.burst(p.x, p.y, 3, C_SHARD, 70, 0.25, 1.6)
                self.audio.play("hit", 0.12)
        if self.shards:
            self.shards = [s for s in self.shards if s.life > 0]

    def step_collisions(self, dt):
        p = self.p
        for b in self.bullets:
            if b.dead or not self.grid:
                continue
            kx = int(b.x // 96)
            ky = int(b.y // 96)
            for gx in (kx - 1, kx, kx + 1):
                for gy in (ky - 1, ky, ky + 1):
                    bucket = self.grid.get((gx, gy))
                    if not bucket:
                        continue
                    for e in bucket:
                        if e.hp <= 0 or e in b.hits:
                            continue
                        rr = e.r + b.r
                        if (e.x - b.x) ** 2 + (e.y - b.y) ** 2 <= rr * rr:
                            b.hits.append(e)
                            self.on_bullet_hit(b, e)
                            if b.dead:
                                break
                    if b.dead:
                        break
                if b.dead:
                    break

        if p.inv <= 0:
            for e in self.enemies:
                if e.hp <= 0:
                    continue
                rr = e.r + p.r
                if (e.x - p.x) ** 2 + (e.y - p.y) ** 2 <= rr * rr:
                    self.hurt_player(e.dmg)
                    if p.st["thorns"] > 0:
                        self.damage_enemy(e, p.st["thorns"], C_PLAYER,
                                          knock=(p.x - e.x, p.y - e.y))
                    break

    def on_bullet_hit(self, b, e):
        crit = self.rng.random() < b.crit
        dmg = b.dmg * (2.0 if crit else 1.0)
        self.damage_enemy(e, dmg, b.color, knock=(b.vx * 0.02, b.vy * 0.02),
                          crit=crit)
        self.audio.play("hit", 0.045)
        self.shake = max(self.shake, 1.6)

        if not crit:
            self.burst(b.x, b.y, 3, b.color, 100, 0.22, 1.7)

        if b.explode > 0:
            self.explode(b.x, b.y, b.explode, b.dmg * 0.55)

        if b.chain > 0:
            tgt = self.nearest_enemy(b.x, b.y, 190, exclude=e)
            if tgt is not None and tgt.hp > 0:
                self.chain_fx.append((b.x, b.y, tgt.x, tgt.y, 0.12))
                self.damage_enemy(tgt, b.dmg * 0.5 * b.chain, "#9fe8ff")
                self.burst(tgt.x, tgt.y, 4, "#9fe8ff", 120, 0.25, 1.8)

        if b.pierce > 0:
            b.pierce -= 1
            b.dmg *= 0.82
        else:
            b.dead = True

    def explode(self, x, y, radius, dmg):
        self.ring(x, y, 10, C_GOLD, radius * 4.0, 0.28)
        self.burst(x, y, 8, C_GOLD, 160, 0.35, 2.4)
        self.shake = max(self.shake, 3.2)
        for e in list(self.enemies):
            if e.hp <= 0:
                continue
            if (e.x - x) ** 2 + (e.y - y) ** 2 < (radius + e.r) ** 2:
                dx = e.x - x
                dy = e.y - y
                d = math.hypot(dx, dy) or 1.0
                self.damage_enemy(e, dmg, C_GOLD,
                                  knock=(dx / d * 120, dy / d * 120))

    def hurt_player(self, dmg):
        p = self.p
        if p.inv > 0 or self.opts.god:
            return
        p.hp -= dmg
        p.inv = 0.9
        self.shake = 12.0
        self.hitstop = max(self.hitstop, 0.05)
        self.combo = 0
        self.audio.play("hurt", 0.2)
        self.burst(p.x, p.y, 16, C_BAD, 220, 0.5, 2.6)
        self.pop_text(p.x, p.y - 30, f"-{int(dmg)}", C_BAD)
        # 충격파로 주변 적을 밀어내 즉사 콤보를 방지
        for e in self.enemies:
            dx = e.x - p.x
            dy = e.y - p.y
            d = math.hypot(dx, dy) or 1.0
            if d < 150:
                f = (1.0 - d / 150) * 320.0
                e.vx += dx / d * f
                e.vy += dy / d * f
        self.ring(p.x, p.y, 16, C_BAD, 260, 0.35)
        if p.hp <= 0:
            p.hp = 0
            self.game_over()

    def game_over(self):
        self.state = "dead"
        self.dead_t = 0.0
        self._show_hud(False)
        self.audio.play("dead", 1.0)
        self.shake = 22.0
        for i in range(5):
            self.ring(self.p.x, self.p.y, 20, C_BAD, 200 + i * 80, 0.8)
        self.burst(self.p.x, self.p.y, 50, C_BAD, 380, 1.0, 3.2)

        if self.score > self.save["best_score"]:
            is_record = True
            self.save["best_score"] = self.score
        else:
            is_record = False
        self.save["best_wave"] = max(self.save["best_wave"], self.wave)
        write_save(self.save)

        c = self.c
        c.delete("scr")
        c.create_rectangle(0, 0, W, H, fill="#04060e", outline="", tags="scr")
        c.create_text(W / 2, 200, text="SYSTEM FAILURE", fill=C_BAD,
                      font=F_HUGE, tags="scr")
        c.create_text(W / 2, 262,
                      text=f"WAVE {self.wave}  ·  SCORE {self.score:,}",
                      fill=C_TEXT, font=F_MID, tags="scr")
        c.create_text(W / 2, 312,
                      text=f"처치 {self.kills}  ·  최고 점수 {self.save['best_score']:,}",
                      fill=C_DIM, font=F_UI, tags="scr")
        if is_record:
            c.create_text(W / 2, 356, text="★ NEW RECORD ★", fill=C_GOLD,
                          font=F_MID, tags="scr")
        c.create_text(W / 2, 420, text="R 또는 클릭  ·  재시작",
                      fill=C_PLAYER, font=F_BIG, tags="scr")
        c.create_text(W / 2, 460, text="ESC  ·  타이틀", fill=C_DIM,
                      font=F_UI, tags="scr")

    # ------------------------------------------------------------- 웨이브
    def check_wave(self, dt):
        if self.state != "play":
            return
        if self.spawn_queue or self.enemies:
            self.wave_done_t = 0.0
            return
        self.wave_done_t += dt
        if self.wave_done_t > 0.75:
            self.open_upgrade()

    def open_upgrade(self):
        self.state = "upgrade"
        self.auto_pick_t = 0.0
        self._show_hud(False)
        self.audio.play("wave", 0.2)
        self.card_opts = self.roll_upgrades(3)
        self.build_upgrade_screen()

    def roll_upgrades(self, n):
        pool = []
        for u in UPGRADES:
            if self.levels.get(u[0], 0) >= u[4]:
                continue
            pool.append((u, UP_WEIGHT[u[3]]))
        opts = []
        while pool and len(opts) < n:
            total = sum(w for _, w in pool)
            r = self.rng.random() * total
            acc = 0.0
            picked = len(pool) - 1
            for i, (u, w) in enumerate(pool):
                acc += w
                if r <= acc:
                    picked = i
                    break
            opts.append(pool[picked][0])
            pool.pop(picked)
        while len(opts) < n:
            opts.append(("bonus", "잔여 신호", "점수 +250 즉시 획득", 0, 99))
        return opts

    def build_upgrade_screen(self):
        c = self.c
        c.delete("scr")
        c.create_rectangle(0, 0, W, H, fill="#04060e", stipple="gray50",
                           outline="", tags="scr")
        c.create_text(W / 2, 104, text=f"WAVE {self.wave} CLEAR",
                      fill=C_HP, font=F_BIG, tags="scr")
        c.create_text(W / 2, 146,
                      text="업그레이드 1장을 선택하세요  (1 / 2 / 3 또는 클릭)",
                      fill=C_DIM, font=F_UI, tags="scr")

        self.cards = []
        n = len(self.card_opts)
        cw, ch, gap = 300, 300, 36
        total = n * cw + (n - 1) * gap
        x0 = (W - total) / 2
        y0 = 210
        for i, u in enumerate(self.card_opts):
            uid, name, desc, rarity, mx = u
            x = x0 + i * (cw + gap)
            col = RARITY_COL[rarity]
            lv = self.levels.get(uid, 0)
            c.create_rectangle(x, y0, x + cw, y0 + ch, fill="#080e1a",
                               outline=col, width=2, tags="scr")
            c.create_rectangle(x + 6, y0 + 6, x + cw - 6, y0 + ch - 6,
                               fill="", outline="#16233a", tags="scr")
            c.create_text(x + 24, y0 + 34, text=f"[{i + 1}]", fill=col,
                          font=F_HUD, anchor="w", tags="scr")
            c.create_text(x + cw - 24, y0 + 34, text=RARITY_NAME[rarity],
                          fill=col, font=F_MONO, anchor="e", tags="scr")
            self._card_glyph(x + cw / 2, y0 + 82, 19, rarity, col)
            c.create_text(x + cw / 2, y0 + 136, text=name, fill=C_TEXT,
                          font=F_BIG, tags="scr")
            c.create_text(x + cw / 2, y0 + 198, text=desc, fill=C_DIM,
                          font=F_UI, width=cw - 48, justify="center", tags="scr")
            if mx < 90:
                c.create_text(x + cw / 2, y0 + 256,
                              text=f"LV {lv} → {lv + 1} / {mx}",
                              fill="#3f5a75", font=F_NUM, tags="scr")
            else:
                c.create_text(x + cw / 2, y0 + 256, text="즉시 적용",
                              fill="#3f5a75", font=F_NUM, tags="scr")
            self.cards.append((x, y0, x + cw, y0 + ch))
        c.create_text(W / 2, H - 46,
                      text=f"SCORE {self.score:,}   ·   처치 {self.kills}",
                      fill=C_DIM, font=F_UI, tags="scr")

    def _card_glyph(self, x, y, r, rarity, col):
        c = self.c
        outer = self._poly(6, x, y, r, -math.pi / 2)
        c.create_polygon(*[v for pt in outer for v in pt], fill="",
                         outline=col, width=2, tags="scr")
        if rarity >= 2:
            for off in (0.0, math.pi / 3.0):
                tri = self._poly(3, x, y, r * 0.66, -math.pi / 2 + off)
                c.create_polygon(*[v for pt in tri for v in pt], fill=col,
                                 outline="", tags="scr")
        else:
            n = 4 if rarity == 1 else 3
            inner = self._poly(n, x, y, r * 0.6, -math.pi / 2)
            c.create_polygon(*[v for pt in inner for v in pt], fill=col,
                             outline="", tags="scr")

    def pick_upgrade(self, idx):
        if idx >= len(self.card_opts):
            return
        u = self.card_opts[idx]
        if u[0] == "bonus":
            self.score += 250
        else:
            apply_upgrade(self, u[0])
        self.audio.play("pick", 0.1)
        self.c.delete("scr")
        self.cards = []
        self.card_opts = []
        self._show_hud(True)
        self.start_wave(self.wave + 1)

    def _draw_pause(self):
        c = self.c
        c.delete("ovl")
        if not self.paused:
            return
        c.create_rectangle(0, 0, W, H, fill="#04060e", stipple="gray50",
                           outline="", tags="ovl")
        c.create_text(W / 2, H / 2 - 60, text="PAUSED", fill=C_PLAYER,
                      font=F_HUGE, tags="ovl")
        c.create_text(W / 2, H / 2 - 10,
                      text=f"WAVE {self.wave}   ·   SCORE {self.score:,}",
                      fill=C_TEXT, font=F_MID, tags="ovl")
        c.create_text(W / 2, H / 2 + 40, text="P  ·  계속하기",
                      fill=C_DIM, font=F_UI, tags="ovl")
        c.create_text(W / 2, H / 2 + 66, text="R  ·  재시작",
                      fill=C_DIM, font=F_UI, tags="ovl")

    # ------------------------------------------------------------ 자동 플레이
    def auto_play(self, dt):
        p = self.p
        tgt = self.nearest_enemy(p.x, p.y, 2000)
        if tgt is not None:
            self.mx = tgt.x + tgt.vx * 0.08
            self.my = tgt.y + tgt.vy * 0.08
        if self.frames % 50 == 0:
            self.try_dash()
        if self.frames % 18 == 0:
            self.keys.clear()
            if p.x < W * 0.34:
                self.keys.add("d")
            elif p.x > W * 0.66:
                self.keys.add("a")
            if p.y < H * 0.30:
                self.keys.add("s")
            elif p.y > H * 0.70:
                self.keys.add("w")

    # ------------------------------------------------------------------ 렌더
    def render(self):
        c = self.c
        ox = oy = 0.0
        if self.shake > 0.2:
            ox = rnd(-1, 1) * self.shake
            oy = rnd(-1, 1) * self.shake
        self._move_background(ox, oy)

        self.pool_enemy.begin()
        self.pool_ring.begin()
        self.pool_particle.begin()
        self.pool_ebullet.begin()
        self.pool_streak.begin()
        self.pool_bullet.begin()
        self.pool_shard.begin()
        self.pool_ftext.begin()
        self.pool_line.begin()
        self.pool_drone.begin()

        if self.state in ("play", "upgrade"):
            self.draw_world(ox, oy)

        self.pool_enemy.end()
        self.pool_ring.end()
        self.pool_particle.end()
        self.pool_ebullet.end()
        self.pool_streak.end()
        self.pool_bullet.end()
        self.pool_shard.end()
        self.pool_ftext.end()
        self.pool_line.end()
        self.pool_drone.end()

        if self.state == "play":
            self._update_hud()
        if self.banner_t > 0 and self.state == "play":
            a = clamp(self.banner_t, 0, 1)
            c.itemconfigure(self.banner_item, text=self.banner,
                            state="normal", fill="#9fe8ff" if a > 0.5 else C_DIM)
            c.itemconfigure(self.banner_item2,
                            text="적을 모두 제거하세요", state="normal")
        else:
            c.itemconfigure(self.banner_item, state="hidden")
            c.itemconfigure(self.banner_item2, state="hidden")

        if self.state == "title":
            self._render_title()
        self._update_audio_hud()
        if self.show_fps:
            item = getattr(self, "_fps_item", None)
            if not item:
                self._fps_item = c.create_text(
                    12, H - 10, text="", fill="#2c4a66", font=F_MONO, anchor="sw")
                item = self._fps_item
            c.itemconfigure(item, text=f"{self.fps:5.1f} fps  "
                                       f"obj {len(self.bullets)}+{len(self.enemies)}")

    def _update_audio_hud(self):
        it = self.audio_txt
        if self.mute_t > 0:
            self.c.itemconfigure(it, state="normal",
                                 text="SOUND ON  ·  M" if not self.muted
                                 else "MUTED  ·  M",
                                 fill=C_DIM if self.muted else "#4f8fa0")
        elif self.muted:
            self.c.itemconfigure(it, state="normal", text="MUTED  ·  M",
                                 fill="#2c4457")
        else:
            self.c.itemconfigure(it, state="hidden")

    def draw_world(self, ox, oy):
        c = self.c
        p = self.p

        for s in self.shards:
            r = 3.2 + math.sin(s.life * 9.0) * 0.8
            col = C_SHARD if s.life > 3.0 else "#3d6b78"
            self.pool_shard.draw(s.x + ox, s.y + oy, r, col)
            self.pool_ring.draw(s.x + ox, s.y + oy, r + 2.4, "", outline=col)

        for e in self.enemies:
            col = E_PARAMS[e.kind]["color"]
            if e.flash > 0.35:
                col = "#ffffff"
            x = e.x + ox
            y = e.y + oy
            r = e.r
            if e.kind in ("drifter", "mini"):
                pts = self._tri(x, y, r * 1.25, e.ang)
                self.pool_enemy.draw(pts, col)
                self.pool_ring.draw(x, y, r * 0.42, "#ffd7e6")
            elif e.kind == "shooter":
                self.pool_enemy.draw(self._poly(4, x, y, r, e.t * 0.8), col)
                self.pool_enemy.draw(self._poly(4, x, y, r * 0.5, e.t * 0.8),
                                     "#7a4d00")
            elif e.kind == "splitter":
                self.pool_enemy.draw(self._poly(5, x, y, r, e.t * 0.6), col)
                self.pool_ring.draw(x, y, r * 0.55, "", outline="#e2c8ff")
            elif e.kind == "rusher":
                if e.state == 1:
                    self.pool_ring.draw(x, y, r + 10, "", outline=C_RUSH)
                self.pool_enemy.draw(self._arrow(x, y, r * 1.5, e.ang), col)
                self.pool_ring.draw(x, y, r * 0.35, "#ffe0c0")
            elif e.kind == "boss":
                spin = e.t * 0.9
                self.pool_ring.draw(x, y, r + 14, "", outline=C_BOSS)
                self.pool_ring.draw(x, y, r + 6, "", outline="#7d2f78")
                self.pool_enemy.draw(self._poly(6, x, y, r, spin), col)
                self.pool_enemy.draw(self._poly(6, x, y, r * 0.62, -spin * 1.6),
                                     "#5c1058")
                self.pool_ring.draw(x, y, r * 0.3, "#ffe6ff")

        for q in self.particles:
            t = q.life / q.maxlife
            r = q.r * (0.25 + 0.75 * t)
            self.pool_particle.draw(q.x + ox, q.y + oy, r, q.color)

        for b in self.ebullets:
            self.pool_ebullet.draw(b.x + ox, b.y + oy, b.r, "#ff9fbb")
            self.pool_ebullet.draw(b.x + ox, b.y + oy, b.r * 0.45, b.color)

        for b in self.bullets:
            tx = b.vx * 0.032
            ty = b.vy * 0.032
            self.pool_streak.draw(b.x - tx + ox, b.y - ty + oy,
                                  b.x + ox, b.y + oy, "#33500a", 6)
            self.pool_streak.draw(b.x - tx + ox, b.y - ty + oy,
                                  b.x + ox, b.y + oy, b.color, 3)
            self.pool_bullet.draw(b.x + ox, b.y + oy, b.r * 0.85, "#f4ffd6")

        for (x1, y1, x2, y2, life) in self.chain_fx:
            self.pool_line.draw(x1 + ox, y1 + oy, x2 + ox, y2 + oy,
                                "#9fe8ff", 2)

        if p.st["drone"] > 0:
            n = int(p.st["drone"])
            for i in range(n):
                a = p.drone_ang + TAU * i / n
                dx = p.x + math.cos(a) * 52 + ox
                dy = p.y + math.sin(a) * 52 + oy
                self.pool_ring.draw(dx, dy, 8.0, "", outline=C_PLAYER)
                self.pool_drone.draw(dx, dy, 4.0, C_PLAYER)

        if self.state in ("play", "upgrade"):
            self.draw_player(ox, oy)

        for ft in self.ftexts:
            if ft.life > 0.15:
                self.pool_ftext.draw(ft.x + ox, ft.y + oy, ft.text, ft.color)

    @staticmethod
    def _poly(n, x, y, r, off):
        pts = []
        for i in range(n):
            a = off + TAU * i / n
            pts.append((x + math.cos(a) * r, y + math.sin(a) * r))
        return pts

    @staticmethod
    def _tri(x, y, r, ang):
        return [(x + math.cos(ang) * r, y + math.sin(ang) * r),
                (x + math.cos(ang + 2.35) * r * 0.85,
                 y + math.sin(ang + 2.35) * r * 0.85),
                (x + math.cos(ang - 2.35) * r * 0.85,
                 y + math.sin(ang - 2.35) * r * 0.85)]

    @staticmethod
    def _arrow(x, y, r, ang):
        return [(x + math.cos(ang) * r, y + math.sin(ang) * r),
                (x + math.cos(ang + 2.5) * r * 0.62,
                 y + math.sin(ang + 2.5) * r * 0.62),
                (x + math.cos(ang + math.pi) * r * 0.45,
                 y + math.sin(ang + math.pi) * r * 0.45),
                (x + math.cos(ang - 2.5) * r * 0.62,
                 y + math.sin(ang - 2.5) * r * 0.62)]

    def draw_player(self, ox, oy):
        c = self.c
        p = self.p
        if p.inv > 0 and int(self.t * 22) % 2 == 0:
            body = "#6b8f9c"
            glow = "#8a2b3a"
        else:
            body = C_PLAYER
            glow = C_PLAYER_GLOW
        a = p.aim
        ca, sa = math.cos(a), math.sin(a)
        nose = 21.0 - p.recoil * 4.0
        x, y = p.x + ox, p.y + oy
        pts = [
            x + ca * nose, y + sa * nose,
            x + math.cos(a + 2.42) * 13.0, y + math.sin(a + 2.42) * 13.0,
            x - ca * 7.0, y - sa * 7.0,
            x + math.cos(a - 2.42) * 13.0, y + math.sin(a - 2.42) * 13.0,
        ]
        c.coords(self.ship, *pts)
        c.itemconfigure(self.ship, fill=body, outline="#e8ffff", width=1)
        self.pool_ring.draw(x, y, 25.0, "", outline=glow)
        self.pool_ring.draw(x + ca * 7.0, y + sa * 7.0, 3.0, "#eaffff")

        sp = math.hypot(p.vx, p.vy)
        if sp > 40:
            bx = x - ca * 15.0
            by = y - sa * 15.0
            rr = 3.0 + min(5.0, sp * 0.012)
            c.coords(self.thrust, bx - rr, by - rr, bx + rr, by + rr)
            c.itemconfigure(self.thrust, state="normal", fill="#1c6d7d")
        else:
            c.itemconfigure(self.thrust, state="hidden")
        c.coords(self.aim_dot, x + ca * 42 - 3, y + sa * 42 - 3,
                 x + ca * 42 + 3, y + sa * 42 + 3)
        c.itemconfigure(self.aim_dot, outline=glow)

    # ------------------------------------------------------------------ 루프
    def loop(self):
        try:
            now = time.perf_counter()
            raw = now - self.last
            self.last = now
            if raw > 0.15:
                raw = 0.15
            self.acc += raw
            steps = 0
            while self.acc >= DT and steps < 3:
                self.step(DT)
                self.acc -= DT
                steps += 1
            if steps >= 3:
                self.acc = 0.0
            self.fps = self.fps * 0.92 + (1.0 / max(raw, 1e-4)) * 0.08
            if steps:
                self.render()
            self.root.after(5, self.loop)
        except tk.TclError:
            try:
                if self.root.winfo_exists():
                    traceback.print_exc()
            except Exception:
                pass
        except Exception:
            traceback.print_exc()
            try:
                self.root.destroy()
            except Exception:
                pass

    def selftest_run(self):
        """창/실시간 대기 없이 고정 스텝으로 빠르게 시뮬레이션한다."""
        t0 = time.perf_counter()
        while self.selftest_left > 0:
            try:
                self.step(DT)
                if self.frames % 3 == 0:
                    self.render()
            except Exception:
                traceback.print_exc()
                break
            self.selftest_left -= 1
            if self.state == "dead":
                self.dead_t += DT
                if self.dead_t > 1.2:
                    self.new_game()
            if self.frames % 600 == 0:
                print(f"[dbg] f={self.frames} st={self.state} w={self.wave} "
                      f"e={len(self.enemies)} b={len(self.bullets)} "
                      f"eb={len(self.ebullets)} cards={len(self.card_opts)} "
                      f"hp={self.p.hp:.0f}", flush=True)
        el = time.perf_counter() - t0
        sim = self.frames / 60.0
        self.fps = self.frames / max(el, 1e-6)
        print(f"[perf] {self.frames} frames / {el:.2f}s  "
              f"({sim:.1f}s 시뮬레이션, {self.fps:.0f} step/s)")
        print(f"[selftest] wave={self.wave} score={self.score} "
              f"kills={self.kills} state={self.state} "
              f"enemies={len(self.enemies)} bullets={len(self.bullets)}")
        try:
            self.root.destroy()
        except Exception:
            pass


# ----------------------------------------------------------------------------
def main(argv=None):
    ap = argparse.ArgumentParser(description="NEON VOID")
    ap.add_argument("--no-sound", action="store_true", help="소리 끄기")
    ap.add_argument("--fps", action="store_true", help="FPS 오버레이")
    ap.add_argument("--seed", type=int, default=None, help="난수 시드")
    ap.add_argument("--selftest", type=int, default=0,
                    help="N프레임 자동 플레이 후 종료")
    ap.add_argument("--god", action="store_true",
                    help="검증용 무적 (자동 플레이와 함께 사용)")
    ap.add_argument("--volume", type=float, default=0.9,
                    help="전체 볼륨 0.0 ~ 1.0 (기본 0.9)")
    opts = ap.parse_args(argv)
    opts.volume = clamp(opts.volume, 0.0, 1.0)

    root = tk.Tk()
    game = Game(root, opts)
    if opts.selftest:
        game.selftest_run()
    else:
        root.mainloop()
    return 0


if __name__ == "__main__":
    sys.exit(main())
