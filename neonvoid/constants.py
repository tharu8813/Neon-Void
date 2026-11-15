"""Game constants and visual theme."""

from __future__ import annotations

import math
import os
import random

# ----------------------------------------------------------------------------
# 상수
# ----------------------------------------------------------------------------
W, H = 1280, 720
DT = 1.0 / 60.0
TAU = math.pi * 2.0
SAVE_FILE = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "save.json")
SETTINGS_FILE = os.path.join(os.path.dirname(SAVE_FILE), "settings.json")

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
