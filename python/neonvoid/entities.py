"""Game entity state."""

from __future__ import annotations

import math
from .constants import (W, H, rnd, C_BULLET)

# ----------------------------------------------------------------------------
# 엔티티
# ----------------------------------------------------------------------------
class Player:
    __slots__ = ("x", "y", "vx", "vy", "hp", "maxhp", "r", "aim",
                 "fire_cd", "dash_t", "dash_cd", "charges", "charge_t",
                 "inv", "dash_dir", "last_move_dir", "drone_ang", "st", "recoil",
                 "mana", "maxmana", "pulse_cd", "guard_cd", "guard_t")

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
        self.last_move_dir = (0.0, -1.0)
        self.drone_ang = 0.0
        self.recoil = 0.0
        self.mana = self.maxmana = 100.0
        self.pulse_cd = self.guard_cd = self.guard_t = 0.0
        self.st = {
            "speed": 322.0, "rate": 5.4, "dmg": 10.0, "bspeed": 880.0,
            "pierce": 0, "split": 0, "homing": 0.0, "explode": 0.0,
            "ricochet": 0, "crit": 0.05, "lifesteal": 0.0, "thorns": 0.0,
            "dash_max": 1, "dash_blast": 0.0, "slow": 0.0, "magnet": 82.0,
            "drone": 0, "chain": 0, "rear": 0,
            "armor": 4.0, "armor_pen": 0.0, "mana_regen": 6.0,
            "hp_regen": 0.0, "burn_chance": 0.0, "burn_dps": 0.0,
            "toxin_chance": 0.0, "toxin_dps": 0.0,
            "life_on_hit": 0.0, "skill_power": 1.0,
            "skill_cooldown": 1.0, "skill_efficiency": 1.0,
            "effect_duration": 1.0,
        }


class Enemy:
    __slots__ = ("kind", "x", "y", "vx", "vy", "hp", "maxhp", "r",
                 "t", "cd", "state", "ang", "flash", "score", "spd", "dmg",
                 "armor", "statuses", "guard_t", "vulnerable_t")

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
        self.armor = 0.0
        self.statuses = []
        self.guard_t = 0.0
        self.vulnerable_t = 0.0


class Bullet:
    __slots__ = ("x", "y", "vx", "vy", "r", "dmg", "life", "pierce",
                 "homing", "explode", "ricochet", "crit", "chain",
                 "color", "dead", "hits", "friendly", "armor_pen",
                 "infection_on_hit")

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
        self.armor_pen = 0.0
        self.infection_on_hit = 0.0


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
