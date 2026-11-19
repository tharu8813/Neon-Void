import unittest
from types import SimpleNamespace

from neonvoid.constants import W, H, clamp
from neonvoid.content.enemies import E_PARAMS, SPAWN_COST
from neonvoid.content.upgrades import UPGRADES, apply_upgrade
from neonvoid.entities import Player
from neonvoid.controls import movement_vector, dash_vector
from neonvoid.events import EventBus
from neonvoid.game import Game
from neonvoid.view import VisualMixin


class CoreTests(unittest.TestCase):
    def test_clamp(self):
        self.assertEqual(clamp(-2, 0, 1), 0)
        self.assertEqual(clamp(3, 0, 1), 1)
        self.assertEqual(clamp(0.4, 0, 1), 0.4)

    def test_game_has_visual_mixin(self):
        self.assertTrue(issubclass(Game, VisualMixin))

    def test_player_defaults(self):
        player = Player()
        self.assertEqual((player.x, player.y), (W / 2, H / 2))
        self.assertEqual(player.hp, player.maxhp)
        self.assertGreater(player.st["rate"], 0)

    def test_keyboard_dash_ignores_aim(self):
        self.assertEqual(dash_vector({"d"}, (-100, 0), (0, -1)), (1, 0))
        self.assertEqual(dash_vector({"w"}, (0, 100), (0, 1)), (0, -1))
        self.assertEqual(dash_vector({"a", "d"}, (0, 0), (0, 1)), (0, 1))

    def test_diagonal_dash_is_normalized(self):
        import math
        x, y = dash_vector({"w", "d"}, (0, 0), (0, -1))
        self.assertAlmostEqual(math.hypot(x, y), 1.0)
        self.assertGreater(x, 0)
        self.assertLess(y, 0)

    def test_dash_fallbacks(self):
        self.assertEqual(movement_vector({"a", "d"}), (0.0, 0.0))
        self.assertEqual(dash_vector(set(), (0, 80), (-1, 0)), (0, 1))
        self.assertEqual(dash_vector(set(), (0, 0), (-1, 0)), (-1, 0))
        self.assertEqual(dash_vector(set(), (0, 0), (0, 0)), (0, -1))

    def test_upgrade_catalog(self):
        ids = [u[0] for u in UPGRADES]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(len(ids), 30)
        for uid, name, description, rarity, maximum in UPGRADES:
            self.assertTrue(uid and name and description)
            self.assertIn(rarity, (0, 1, 2))
            self.assertGreater(maximum, 0)

    def test_enemy_catalog(self):
        for kind, cost in SPAWN_COST.items():
            self.assertIn(kind, E_PARAMS)
            self.assertGreater(cost, 0)
        for entry in E_PARAMS.values():
            self.assertGreater(entry["hp"], 0)
            self.assertGreater(entry["spd"], 0)

    def test_upgrade_application(self):
        player = Player()
        game = SimpleNamespace(p=player, levels={})
        base = player.st["rate"]
        apply_upgrade(game, "rapid")
        self.assertGreater(player.st["rate"], base)
        self.assertEqual(game.levels["rapid"], 1)

    def test_event_bus(self):
        bus = EventBus()
        seen = []
        def callback(event):
            seen.append((event.name, event.payload["wave"]))
        bus.subscribe("wave_started", callback)
        bus.subscribe("wave_started", callback)
        bus.emit("wave_started", wave=5)
        self.assertEqual(seen, [("wave_started", 5)])
        bus.unsubscribe("wave_started", callback)
        bus.emit("wave_started", wave=6)
        self.assertEqual(len(seen), 1)

    def test_handler_failure_is_isolated(self):
        bus = EventBus()
        seen = []
        def fail(event):
            raise RuntimeError("test failure")
        bus.subscribe("run_started", fail)
        bus.subscribe("run_started", lambda event: seen.append(event.name))
        with self.assertLogs("neonvoid.events", level="ERROR"):
            bus.emit("run_started", runs=1)
        self.assertEqual(seen, ["run_started"])


if __name__ == "__main__":
    unittest.main()
