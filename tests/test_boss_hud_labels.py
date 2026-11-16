"""Boss HUD must separate persistent identity from urgent windup text."""
import unittest
from neonvoid.view import VisualMixin


class BossHudLabelTests(unittest.TestCase):
    def test_no_attack_keeps_warning_empty(self):
        name, warning = VisualMixin._boss_hud_labels("boss", 2, None)
        self.assertEqual(name, "BOSS  ·  PHASE 2")
        self.assertEqual(warning, "")

    def test_windup_is_a_distinct_warning(self):
        name, warning = VisualMixin._boss_hud_labels(
            "boss", 3, "암호화 폭발 예고 · 원 밖으로 이동")
        self.assertEqual(name, "BOSS  ·  PHASE 3")
        self.assertEqual(warning, "주의: 암호화 폭발 예고 · 원 밖으로 이동")


if __name__ == "__main__":
    unittest.main()
