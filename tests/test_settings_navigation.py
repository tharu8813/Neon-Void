"""Keyboard navigation of settings overlay."""
import unittest
from unittest.mock import MagicMock
from neonvoid.game import Game


class SettingsKeyboardNavigationTests(unittest.TestCase):
    def setUp(self):
        self.game = Game.__new__(Game)
        self.game.rebinding_action = None
        self.game.settings_page = "general"
        self.game.settings_selected = 3
        self.game._draw_settings = MagicMock()

    def test_home_and_end_general_settings(self):
        self.game._handle_settings_key("home")
        self.assertEqual(self.game.settings_selected, 0)
        self.game._handle_settings_key("end")
        self.assertEqual(self.game.settings_selected, 5)
        self.assertEqual(self.game._draw_settings.call_count, 2)

    def test_home_and_end_controls(self):
        from neonvoid.settings import ACTION_ORDER
        self.game.settings_page = "controls"
        self.game._handle_settings_key("end")
        self.assertEqual(self.game.settings_selected, len(ACTION_ORDER) - 1)
        self.game._handle_settings_key("home")
        self.assertEqual(self.game.settings_selected, 0)


if __name__ == "__main__":
    unittest.main()
