"""Reduced-effects mode keeps wave and combo labels stationary."""
import unittest
from types import SimpleNamespace
from neonvoid.view import VisualMixin


class HudPopMotionTests(unittest.TestCase):
    def test_reduced_effects_keeps_hud_at_rest(self):
        view = VisualMixin()
        view.settings = SimpleNamespace(reduced_effects=True)
        self.assertEqual(view._hud_pop_y(1.0, 9.0), 26)
        self.assertEqual(view._hud_pop_y(0.2, 8.0), 26)

    def test_default_mode_preserves_pop_animation(self):
        view = VisualMixin()
        view.settings = SimpleNamespace(reduced_effects=False)
        self.assertEqual(view._hud_pop_y(1.0, 9.0), 17)
        self.assertEqual(view._hud_pop_y(0.5, 8.0), 22)


if __name__ == "__main__":
    unittest.main()
