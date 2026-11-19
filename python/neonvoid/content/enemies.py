"""Enemy statistics and deterministic spawn budgets.

New virus kinds have compatible stat dictionaries. Their *behavior* is exposed
by virus_catalog.py and must be driven by the world integration, rather than
silently changing the original arena AI.
"""
from __future__ import annotations

from ..constants import (C_ENEMY, C_SHOOT, C_SPLIT, C_RUSH, C_BOSS)

# Legacy enemies are intentionally byte-for-byte equivalent in their stats.
E_PARAMS = {
    "drifter": dict(hp=22, r=13, spd=108, dmg=6, armor=2, score=12, color=C_ENEMY),
    "mini": dict(hp=10, r=8, spd=158, dmg=4, armor=0, score=6, color=C_SPLIT),
    "shooter": dict(hp=34, r=15, spd=78, dmg=8, armor=8, score=22, color=C_SHOOT),
    "splitter": dict(hp=50, r=18, spd=94, dmg=9, armor=13, score=26, color=C_SPLIT),
    "rusher": dict(hp=28, r=14, spd=124, dmg=12, armor=5, score=20, color=C_RUSH),
    "boss": dict(hp=620, r=46, spd=70, dmg=18, armor=26, score=400, color=C_BOSS),
    # Four distinct OS sector threats and their support archetypes.
    "overheater": dict(hp=29, r=13, spd=172, dmg=11, armor=3, score=24, color="#ff874c"),
    "worm": dict(hp=35, r=14, spd=126, dmg=7, armor=3, score=22, color="#aa80ff"),
    "trojan": dict(hp=41, r=15, spd=109, dmg=10, armor=9, score=28, color="#f79bda"),
    "ransomware": dict(hp=76, r=19, spd=70, dmg=13, armor=24, score=46, color="#ffca69"),
    "rootkit": dict(hp=54, r=16, spd=91, dmg=12, armor=17, score=38, color="#dc70e9"),
    "packet_swarm": dict(hp=17, r=10, spd=193, dmg=6, armor=1, score=12, color="#50dbe9"),
    "guardian": dict(hp=86, r=20, spd=68, dmg=10, armor=31, score=44, color="#ffe298"),
    # Bosses are identified by their own kind, not by the legacy 'boss' value.
    "overseer": dict(hp=660, r=43, spd=103, dmg=20, armor=22, score=450, color="#ff6538"),
    "replicator": dict(hp=620, r=45, spd=92, dmg=17, armor=19, score=450, color="#b878ff"),
    "encryptor": dict(hp=780, r=48, spd=65, dmg=22, armor=38, score=480, color="#ffc05d"),
    "zero_day": dict(hp=850, r=48, spd=128, dmg=23, armor=27, score=520, color="#f05ef1"),
}
SPAWN_COST = {
    "drifter": 1.0, "shooter": 2.0, "splitter": 2.0, "rusher": 1.7,
    "overheater": 1.4, "worm": 1.6, "trojan": 2.1,
    "ransomware": 3.3, "rootkit": 2.6, "packet_swarm": 1.0,
    "guardian": 3.5,
}
