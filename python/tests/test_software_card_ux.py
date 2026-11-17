"""Software-card previews and keyboard selection preserve real game rules."""
import unittest
from types import SimpleNamespace
from unittest.mock import MagicMock

from neonvoid.content.upgrades import UP_BY_ID
from neonvoid.entities import Player
from neonvoid.game import Game
from neonvoid.view import VisualMixin


class SoftwareCardPreviewTests(unittest.TestCase):
    def setUp(self):
        self.view = VisualMixin()
        self.view.p = Player()

    def test_cooldown_previews_show_actual_skill_seconds(self):
        before = dict(self.view.p.st)
        rows = self.view._upgrade_comparison("cool")
        self.assertEqual(rows, [
            ("충격파 대기", "7.0s → 6.4s"),
            ("보호막 대기", "12.0s → 10.9s"),
        ])
        self.assertEqual(before, self.view.p.st)

    def test_mana_previews_show_actual_cast_costs(self):
        rows = self.view._upgrade_comparison("eco")
        self.assertEqual(rows, [
            ("충격파 소모", "35.0 → 31.5 MP"),
            ("보호막 소모", "25.0 → 22.5 MP"),
        ])
        self.assertEqual(self.view.p.st["skill_efficiency"], 1.0)

    def test_previews_account_for_existing_software(self):
        self.view.p.st["skill_cooldown"] = 0.91
        rows = self.view._upgrade_comparison("cool")
        self.assertEqual(rows, [
            ("충격파 대기", "6.4s → 5.8s"),
            ("보호막 대기", "10.9s → 9.9s"),
        ])


class SoftwareCardKeyboardTests(unittest.TestCase):
    def setUp(self):
        g = Game.__new__(Game)
        g.checkpoint_prompt_open = False
        g.salvage_open = False
        g.extraction_open = False
        g.database_open = False
        g.profile_open = False
        g.settings_open = False
        g.loadout_open = False
        g.state = "upgrade"
        g.paused = False
        g.keys = set()
        g.hover_card = -1
        g.settings = SimpleNamespace(bindings={"pause": "p"})
        g.card_opts = [UP_BY_ID[uid] for uid in ("rapid", "power", "cool")]
        g.pick_upgrade = MagicMock()
        self.game = g

    def test_arrow_navigation_wraps_and_enter_picks_highlight(self):
        g = self.game
        g._handle_key("left")
        self.assertEqual(g.hover_card, 2)
        g._handle_key("right")
        self.assertEqual(g.hover_card, 0)
        g._handle_key("right")
        self.assertEqual(g.hover_card, 1)
        g._handle_key("return")
        g.pick_upgrade.assert_called_once_with(1)

    def test_enter_without_selection_does_not_choose_card(self):
        self.game._handle_key("return")
        self.game.pick_upgrade.assert_not_called()

    def test_numeric_shortcut_still_selects_directly(self):
        self.game._handle_key("3")
        self.game.pick_upgrade.assert_called_once_with(2)


if __name__ == "__main__":
    unittest.main()
