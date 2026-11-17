"""Settings round-trip and validation, with no Tk window required."""
import json
import os
import tempfile
import threading
import unittest
from types import SimpleNamespace

from neonvoid.audio import Audio
from neonvoid.settings import SCHEMA_VERSION, Settings, load_settings, save_settings


class SettingsTests(unittest.TestCase):
    def test_missing_file_defaults(self):
        with tempfile.TemporaryDirectory() as directory:
            data = load_settings(os.path.join(directory, "missing.json"))
        self.assertEqual(data, Settings())

    def test_roundtrip_and_atomic_replace(self):
        with tempfile.TemporaryDirectory() as directory:
            path = os.path.join(directory, "settings.json")
            target = Settings(volume=0.5, muted=True, show_fps=True,
                              reduced_effects=True)
            self.assertTrue(save_settings(target, path))
            self.assertEqual(load_settings(path), target)
            with open(path, encoding="utf-8") as stream:
                self.assertEqual(json.load(stream)["schema_version"], SCHEMA_VERSION)
            self.assertEqual(os.listdir(directory), ["settings.json"])

    def test_volume_is_clamped_on_load_and_save(self):
        with tempfile.TemporaryDirectory() as directory:
            path = os.path.join(directory, "settings.json")
            self.assertTrue(save_settings(Settings(volume=15), path))
            self.assertEqual(load_settings(path).volume, 1.0)
            with open(path, "w", encoding="utf-8") as stream:
                json.dump({"schema_version": 1, "volume": -4}, stream)
            self.assertEqual(load_settings(path).volume, 0.0)

    def test_bad_schema_and_bad_types_use_defaults(self):
        with tempfile.TemporaryDirectory() as directory:
            path = os.path.join(directory, "settings.json")
            for data in (
                {"schema_version": 99},
                {"schema_version": True},
                {"volume": "loud"},
                {"volume": float("nan")},
                {"show_fps": 1},
                []
            ):
                with self.subTest(data=data):
                    with open(path, "w", encoding="utf-8") as stream:
                        json.dump(data, stream)
                    with self.assertLogs("neonvoid.settings", "WARNING"):
                        self.assertEqual(load_settings(path), Settings())
            with open(path, "w", encoding="utf-8") as stream:
                stream.write("not json")
            with self.assertLogs("neonvoid.settings", "WARNING"):
                self.assertEqual(load_settings(path), Settings())

    def test_invalid_write_does_not_replace_old_file(self):
        with tempfile.TemporaryDirectory() as directory:
            path = os.path.join(directory, "settings.json")
            self.assertTrue(save_settings(Settings(), path))
            with self.assertLogs("neonvoid.settings", "WARNING"):
                self.assertFalse(save_settings(Settings(volume=float("nan")), path))
            self.assertEqual(load_settings(path), Settings())
            self.assertEqual(os.listdir(directory), ["settings.json"])

    def test_live_audio_mixer_controls(self):
        audio = Audio(enabled=False, volume=0.9)
        fake = SimpleNamespace(lock=threading.Lock(), music_target=0.0,
                               muted=False, voices=[1, 2])
        audio.mixer = fake
        self.assertEqual(audio.set_volume(0.4), 0.4)
        self.assertAlmostEqual(fake.music_target, 0.12)
        self.assertTrue(audio.set_muted(True))
        self.assertTrue(fake.muted)
        self.assertFalse(fake.voices)
        self.assertFalse(audio.toggle_mute())
        self.assertFalse(fake.muted)


if __name__ == "__main__":
    unittest.main()
