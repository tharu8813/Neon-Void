#!/usr/bin/env python3
"""Compatibility launcher for the modular NEON VOID game."""
from neonvoid.audio import Audio, _Mixer, _build_effects, _build_music
from neonvoid.cli import main
from neonvoid.constants import *  # Legacy public symbols
from neonvoid.content.enemies import E_PARAMS, SPAWN_COST
from neonvoid.content.upgrades import UPGRADES, UP_BY_ID, UP_WEIGHT, apply_upgrade
from neonvoid.entities import Player, Enemy, Bullet, Particle, Shard, FText
from neonvoid.events import EventBus, GameEvent
from neonvoid.game import Game
from neonvoid.render_pool import OvalPool, PolyPool, TextPool, LinePool
from neonvoid.storage import load_save, write_save

if __name__ == "__main__":
    raise SystemExit(main())
