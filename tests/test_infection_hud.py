"""Infection HUD labels must reflect both infection level and OS integrity."""
import unittest

from neonvoid.infection import InfectionState
from neonvoid.view import VisualMixin


class InfectionHudSeverityTests(unittest.TestCase):
    def assert_status(self, infection, integrity, expected, color):
        state = InfectionState(
            player_infection=infection, os_integrity=integrity)
        label, fill, outline, background = VisualMixin._infection_hud_severity(
            state)
        self.assertEqual((label, fill), (expected, color))
        self.assertTrue(outline.startswith("#"))
        self.assertTrue(background.startswith("#"))

    def test_safe_infection_and_healthy_os(self):
        self.assert_status(0, 100, "안정", "#55cba8")
        self.assert_status(24, 100, "안정", "#55cba8")
        self.assert_status(25, 100, "주의", "#e5a34e")

    def test_spreading_infection_and_weak_os_warn(self):
        self.assert_status(50, 100, "주의", "#e5a34e")
        self.assert_status(0, 50, "주의", "#e5a34e")

    def test_critical_infection_or_os_integrity_warn_in_red(self):
        self.assert_status(75, 100, "위험", "#ff5872")
        self.assert_status(0, 25, "위험", "#ff5872")
        self.assert_status(100, 0, "위험", "#ff5872")


if __name__ == "__main__":
    unittest.main()
