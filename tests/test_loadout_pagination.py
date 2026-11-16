"""Loadout pagination keyboard and pointer actions."""
import unittest
from unittest.mock import MagicMock
from neonvoid.game import Game


class LoadoutPagingTests(unittest.TestCase):
    def test_page_navigation_is_clamped(self):
        game = Game.__new__(Game)
        game.loadout_open = True
        game.levels = {str(i): 1 for i in range(60)}
        game.loadout_page = 0
        game._draw_loadout = MagicMock()
        game._handle_key("next", pointer=True)
        self.assertEqual(game.loadout_page, 1)
        game._handle_key("next", pointer=True)
        game._handle_key("next", pointer=True)
        self.assertEqual(game.loadout_page, 2)
        game._handle_key("prior", pointer=True)
        self.assertEqual(game.loadout_page, 1)
        game._handle_key("left")
        game._handle_key("left")
        self.assertEqual(game.loadout_page, 0)

    def test_page_navigation_with_no_installed_software(self):
        game = Game.__new__(Game)
        game.loadout_open = True
        game.levels = {}
        game.loadout_page = 0
        game._draw_loadout = MagicMock()
        game._handle_key("next", pointer=True)
        self.assertEqual(game.loadout_page, 0)


if __name__ == "__main__":
    unittest.main()
