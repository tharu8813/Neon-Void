"""Enemy stats and spawn budgets."""

from __future__ import annotations

from ..constants import (C_ENEMY, C_SHOOT, C_SPLIT, C_RUSH, C_BOSS)

# 적 기본 능력치
E_PARAMS = {
    "drifter": dict(hp=22, r=13, spd=108, dmg=6, armor=2, score=12, color=C_ENEMY),
    "mini": dict(hp=10, r=8, spd=158, dmg=4, armor=0, score=6, color=C_SPLIT),
    "shooter": dict(hp=34, r=15, spd=78, dmg=8, armor=8, score=22, color=C_SHOOT),
    "splitter": dict(hp=50, r=18, spd=94, dmg=9, armor=13, score=26, color=C_SPLIT),
    "rusher": dict(hp=28, r=14, spd=124, dmg=12, armor=5, score=20, color=C_RUSH),
    "boss": dict(hp=620, r=46, spd=70, dmg=18, armor=26, score=400, color=C_BOSS),
}
SPAWN_COST = {"drifter": 1.0, "shooter": 2.0, "splitter": 2.0, "rusher": 1.7}
