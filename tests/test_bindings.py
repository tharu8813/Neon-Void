"""Keyboard remapping, settings schema migration, and movement invariants."""
import json
import math
import os
import tempfile
import unittest

from neonvoid.controls import (
    ACTION_ORDER, DEFAULT_BINDINGS, movement_vector, dash_vector,
    rebind_action, validate_bindings,
)
from neonvoid.settings import SCHEMA_VERSION, Settings, load_settings, save_settings


class InputBindingTests(unittest.TestCase):
    def test_default_bindings_are_unique_and_valid(self):
        self.assertEqual(set(ACTION_ORDER), set(DEFAULT_BINDINGS))
        self.assertEqual(validate_bindings(DEFAULT_BINDINGS), DEFAULT_BINDINGS)

    def test_rebind_is_non_mutating_and_rejects_duplicate(self):
        old = DEFAULT_BINDINGS.copy()
        updated = rebind_action(old, "up", "Q")
        self.assertEqual(old, DEFAULT_BINDINGS)
        self.assertEqual(updated["up"], "q")
        with self.assertRaises(ValueError):
            rebind_action(updated, "down", "a")
        with self.assertRaises(ValueError):
            rebind_action(updated, "dash", "q")
        self.assertEqual(updated["down"], "s")

    def test_reserved_and_unknown_keys_fail(self):
        for key in ("m", "r", "o", "escape", "tab", "return",
                    "shift_l", "up", "1", "", "ctrl", "é"):
            with self.subTest(key=key):
                with self.assertRaises(ValueError):
                    rebind_action(DEFAULT_BINDINGS, "up", key)
        with self.assertRaises(ValueError):
            rebind_action(DEFAULT_BINDINGS, "unknown_action", "q")
        with self.assertRaises(ValueError):
            validate_bindings({"up": "w"})
        with self.assertRaises(ValueError):
            validate_bindings(dict(DEFAULT_BINDINGS, dash=42))

    def test_movement_respects_mapping_and_permanent_arrow_alternatives(self):
        changed = rebind_action(DEFAULT_BINDINGS, "up", "q")
        self.assertEqual(movement_vector({"q"}, changed), (0.0, -1.0))
        self.assertEqual(movement_vector({"w"}, changed), (0.0, 0.0))
        self.assertEqual(movement_vector({"up"}, changed), (0.0, -1.0))
        diagonal = movement_vector({"left", "q"}, changed)
        self.assertAlmostEqual(diagonal[0], -math.sqrt(0.5))
        self.assertAlmostEqual(diagonal[1], -math.sqrt(0.5))
        self.assertEqual(movement_vector({"w"}), (0.0, -1.0))

    def test_dash_uses_custom_direction_then_momentum(self):
        changed = rebind_action(DEFAULT_BINDINGS, "up", "q")
        self.assertEqual(dash_vector({"q"}, (100, 0), (1, 0), changed),
                         (0.0, -1.0))
        self.assertEqual(dash_vector(set(), (30, 0), (0, -1), changed),
                         (1.0, 0.0))
        self.assertEqual(dash_vector(set(), (0, 0), (0, -1), changed),
                         (0.0, -1.0))

    def test_upgrade_version_one_preferences_without_losing_fields(self):
        with tempfile.TemporaryDirectory() as directory:
            path = os.path.join(directory, "settings.json")
            with open(path, "w", encoding="utf-8") as stream:
                json.dump({"schema_version": 1, "volume": 0.4,
                           "muted": True, "show_fps": True,
                           "reduced_effects": True}, stream)
            loaded = load_settings(path)
            self.assertEqual(loaded.volume, 0.4)
            self.assertTrue(loaded.muted)
            self.assertTrue(loaded.show_fps)
            self.assertTrue(loaded.reduced_effects)
            self.assertEqual(loaded.bindings, DEFAULT_BINDINGS)
            loaded.bindings = rebind_action(loaded.bindings, "up", "q")
            self.assertTrue(save_settings(loaded, path))
            with open(path, encoding="utf-8") as stream:
                payload = json.load(stream)
            self.assertEqual(payload["schema_version"], SCHEMA_VERSION)
            self.assertEqual(load_settings(path).bindings["up"], "q")

    def test_invalid_version_two_controls_do_not_overwrite_old_file(self):
        with tempfile.TemporaryDirectory() as directory:
            path = os.path.join(directory, "settings.json")
            self.assertTrue(save_settings(Settings(), path))
            invalid = Settings()
            invalid.bindings["up"] = invalid.bindings["down"]
            with self.assertLogs("neonvoid.settings", "WARNING"):
                self.assertFalse(save_settings(invalid, path))
            self.assertEqual(load_settings(path), Settings())
            self.assertEqual(os.listdir(directory), ["settings.json"])
            with open(path, "w", encoding="utf-8") as stream:
                json.dump({"schema_version": 2, "volume": 0.6,
                           "bindings": {"up": "q"}}, stream)
            with self.assertLogs("neonvoid.settings", "WARNING"):
                self.assertEqual(load_settings(path), Settings())


if __name__ == "__main__":
    unittest.main()
