"""Reduced-effects setting suppresses combat HUD color pulsing."""
import unittest
from types import SimpleNamespace
from unittest.mock import MagicMock
from neonvoid.view import VisualMixin
from neonvoid.constants import C_PLAYER


class ReducedEffectsHudTests(unittest.TestCase):
    def test_ready_skill_bar_is_stable_when_effects_reduced(self):
        view = VisualMixin()
        view.c = MagicMock()
        view.settings = SimpleNamespace(reduced_effects=True)
        view.ui_t = 0.0
        view._skill_bar(1, 2, 10, 100, 0, 7, C_PLAYER)
        first = view.c.itemconfigure.call_args_list[-2].kwargs["fill"]
        view.ui_t = 0.5
        view._skill_bar(1, 2, 10, 100, 0, 7, C_PLAYER)
        second = view.c.itemconfigure.call_args_list[-2].kwargs["fill"]
        self.assertEqual(first, C_PLAYER)
        self.assertEqual(second, C_PLAYER)

    def test_cooldown_bar_keeps_progress_in_reduced_mode(self):
        view = VisualMixin()
        view.c = MagicMock()
        view.settings = SimpleNamespace(reduced_effects=True)
        view.ui_t = 0
        view._skill_bar(1, 2, 10, 110, 3, 6, C_PLAYER)
        view.c.coords.assert_called_with(2, 678, 682, 60, 682)


if __name__ == "__main__":
    unittest.main()
