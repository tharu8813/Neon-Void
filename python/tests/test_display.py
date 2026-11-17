"""Viewport coordinate transforms and settings schema migration."""
from __future__ import annotations

import json
import math
from pathlib import Path
import tempfile
import unittest

from neonvoid.constants import W, H
from neonvoid.display import Viewport
from neonvoid.controls import DEFAULT_BINDINGS
from neonvoid.settings import Settings, load_settings, save_settings, SCHEMA_VERSION


class ViewportTests(unittest.TestCase):
    def test_identity_and_fitted_16_by_9(self):
        v = Viewport.fit(1280, 720)
        self.assertEqual(v.scale, 1)
        self.assertEqual((v.left, v.top), (0, 0))
        self.assertEqual(v.to_screen(640, 360), (640, 360))
        v = Viewport.fit(1920, 1080)
        self.assertEqual(v.scale, 1.5)
        self.assertEqual(v.to_screen(640, 360), (960, 540))
        v = Viewport.fit(960, 540)
        self.assertEqual(v.scale, 0.75)
        self.assertEqual(v.to_game(480, 270), (640, 360))

    def test_ultrawide_and_portrait_letterboxing(self):
        for width, height in ((1600, 900), (1920, 1200), (800, 600),
                              (2560, 1080), (900, 1600), (720, 405)):
            v = Viewport.fit(width, height)
            for x, y in ((0, 0), (W/2, H/2), (W, H),
                         (91, 601), (130, 200)):
                px, py = v.to_screen(x, y)
                xx, yy = v.to_game(px, py)
                self.assertAlmostEqual(xx, x, places=8)
                self.assertAlmostEqual(yy, y, places=8)
                self.assertTrue(v.contains(px, py))
            if height / width > H / W:
                self.assertGreater(v.top, 0)
                self.assertFalse(v.contains(0, 0))
            elif width / height > W / H:
                self.assertGreater(v.left, 0)
                self.assertFalse(v.contains(0, 0))

    def test_invalid_physical_viewport_rejected(self):
        for size in ((0, 720), (1280, -1), (True, 720),
                     (1280.0, 720), (1280, False)):
            with self.subTest(size=size), self.assertRaises(ValueError):
                Viewport.fit(*size)

    def test_settings_v4_migrate_preserves_user_bindings(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "settings.json"
            old = {"schema_version": 4, "volume": 0.35,
                   "muted": True, "show_fps": True,
                   "reduced_effects": True, "bindings": DEFAULT_BINDINGS}
            path.write_text(json.dumps(old), encoding="utf-8")
            restored = load_settings(path)
            self.assertEqual(restored.volume, 0.35)
            self.assertTrue(restored.muted)
            self.assertTrue(restored.show_fps)
            self.assertTrue(restored.reduced_effects)
            self.assertEqual(restored.bindings, DEFAULT_BINDINGS)
            self.assertEqual(restored.display_mode, "windowed")
            self.assertEqual((restored.window_width, restored.window_height),
                             (1280, 720))
            restored.display_mode = "borderless"
            restored.window_width, restored.window_height = 960, 540
            self.assertTrue(save_settings(restored, path))
            self.assertEqual(load_settings(path), restored)
            self.assertEqual(json.loads(path.read_text("utf-8"))["schema_version"],
                             SCHEMA_VERSION)

    def test_invalid_display_prefs_are_not_written(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "settings.json"
            self.assertTrue(save_settings(Settings(), path))
            for kw in ({"display_mode": "unknown"}, {"display_mode": 7},
                       {"window_width": True}, {"window_width": 200},
                       {"window_height": 999999}, {"window_height": 0}):
                with self.subTest(kw=kw):
                    invalid = Settings(**kw)
                    self.assertFalse(save_settings(invalid, path))
                    self.assertEqual(load_settings(path), Settings())


if __name__ == "__main__":
    unittest.main()
