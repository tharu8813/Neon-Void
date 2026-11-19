"""Combat readability and live enemy-cap regressions for sector mechanics."""
from __future__ import annotations

import random
import unittest
from types import SimpleNamespace

from neonvoid.combat import mitigate_damage
from neonvoid.content.virus_catalog import MAX_LIVE_ENEMIES, PATTERNS, VirusRuntime
from neonvoid.entities import Enemy, Player
from neonvoid.events import EventBus
from neonvoid.game import Game


def stub():
    game = Game.__new__(Game)
    game.p = Player()
    game.enemies = []
    game.wave = 2
    game.current_encounter = object()
    game.combo = 0
    game.combo_pop = 0.0
    game.combo_t = 0.0
    game.score = game.kills = 0
    game.shake = game.hitstop = game.slow = 0.0
    game.shards = []
    game.rng = random.Random(8)
    game.events = EventBus()
    game.virus_runtime = VirusRuntime()
    game.virus_pending = []
    game.audio = SimpleNamespace(play=lambda *a: None)
    game.burst = game.ring = game.pop_text = lambda *a, **k: None
    return game


class SectorCombatRegressions(unittest.TestCase):
    def test_splitter_respects_cap_even_when_full(self):
        for count in (1, 62, 63, 64, 65):
            with self.subTest(count=count):
                game = stub()
                splitter = game.make_enemy("splitter", 100, 120)
                game.enemies.extend(Enemy() for _ in range(count - 1))
                splitter.hp = 0
                game.kill_enemy(splitter)
                expected = min(MAX_LIVE_ENEMIES, count - 1 + 2)
                self.assertEqual(len(game.enemies), expected)
                self.assertEqual(sum(e.kind == "mini" for e in game.enemies),
                                 expected - (count - 1))

    def test_memory_replication_is_telegraphed_and_kill_cancels_cast(self):
        self.assertGreater(PATTERNS["worm"].telegraph_seconds, 0)
        self.assertGreater(PATTERNS["replicator"].telegraph_seconds, 0)
        game = stub()
        worm = game.make_enemy("worm", 100, 100)
        game.step_virus_patterns(3.3)
        self.assertEqual(len(game.virus_pending), 1)
        self.assertEqual(game.virus_pending[0][2].behavior, "replicate")
        worm.hp = 0
        game.enemies.remove(worm)
        game.step_virus_patterns(1.0)
        self.assertFalse(game.virus_pending)
        self.assertFalse(game.enemies)

    def test_storage_exposes_core_after_encryption_then_reseals(self):
        game = stub()
        boss = game.make_enemy("ransomware", 200, 200)
        other = game.make_enemy("ransomware", 300, 200)
        game.p.x, game.p.y = 640, 360
        action = SimpleNamespace(behavior="encryption_pulse",
                                 power=9, infection_on_hit=4)
        game._execute_virus_action(boss, action)
        self.assertEqual(boss.vulnerable_t, 2.25)
        self.assertEqual(other.vulnerable_t, 0.0)
        raw = 20.0
        self.assertGreater(
            mitigate_damage(raw, max(0.0, boss.armor - 24.0)),
            mitigate_damage(raw, other.armor),
        )
        boss.vulnerable_t = max(0.0, boss.vulnerable_t - 3.0)
        self.assertEqual(boss.vulnerable_t, 0.0)


if __name__ == "__main__":
    unittest.main()
