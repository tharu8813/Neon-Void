"""Compact installed software summary for the combat HUD."""
import unittest
from neonvoid.view import VisualMixin
from neonvoid.content.upgrades import UP_BY_ID


class HudSoftwareSummaryTests(unittest.TestCase):
    def test_empty(self):
        view = VisualMixin()
        view.levels = {}
        self.assertEqual(view._hud_software_summary(), "")

    def test_shows_overflow_count_and_shortcut(self):
        view = VisualMixin()
        view.levels = {uid: 1 for uid in list(UP_BY_ID)[:15]}
        summary = view._hud_software_summary()
        self.assertLessEqual(len(summary), 49)
        self.assertIn("외 ", summary)
        self.assertIn("[TAB]", summary)

    def test_short_list_shows_names(self):
        view = VisualMixin()
        uid = next(iter(UP_BY_ID))
        view.levels = {uid: 2}
        summary = view._hud_software_summary()
        self.assertIn(UP_BY_ID[uid][1], summary)
        self.assertIn("x2", summary)
        self.assertNotIn("외 ", summary)


if __name__ == "__main__":
    unittest.main()
