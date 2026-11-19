"""Source and frozen runtime paths must keep player saves persistent."""
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from neonvoid import cli
from neonvoid import constants
from neonvoid.runtime_paths import data_directory


class RuntimePathTests(unittest.TestCase):
    def test_source_save_location_is_still_repo_root(self):
        python_root = Path(__file__).resolve().parents[1]
        root = python_root.parent
        self.assertFalse((root / "project.godot").exists())
        self.assertEqual(Path(data_directory(frozen=False)), root)
        self.assertEqual(Path(constants.SAVE_FILE), root / "save.json")
        self.assertEqual(Path(constants.SETTINGS_FILE), root / "settings.json")
        self.assertEqual(Path(constants.PROFILE_FILE), root / "profile.json")

    def test_frozen_binary_uses_stable_local_appdata(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(data_directory(frozen=True, local_appdata=directory))
            self.assertEqual(path, Path(directory) / "NeonVoid")
            self.assertTrue(path.is_dir())
            self.assertEqual(data_directory(frozen=True, local_appdata=directory),
                             str(path))

    def test_frozen_fallback_works_when_local_appdata_is_invalid(self):
        with tempfile.TemporaryDirectory() as directory:
            blocker = Path(directory) / "not-a-directory"
            blocker.write_text("blocker", encoding="utf-8")
            path = Path(data_directory(frozen=True, local_appdata=str(blocker),
                                       home=directory))
            self.assertEqual(path, Path(directory) / ".neonvoid")
            self.assertTrue(path.is_dir())

    def test_windowed_executable_missing_streams_are_redirected(self):
        with tempfile.TemporaryDirectory() as directory:
            with patch.object(cli, "DATA_DIRECTORY", directory):
                with patch.object(cli.sys, "frozen", True, create=True):
                    with patch.object(cli.sys, "stdout", None):
                        with patch.object(cli.sys, "stderr", None):
                            try:
                                cli.configure_frozen_streams()
                                self.assertIsNotNone(cli.sys.stdout)
                                self.assertIsNotNone(cli.sys.stderr)
                                print("trace from bundled executable", file=cli.sys.stderr)
                                cli.sys.stderr.flush()
                            finally:
                                cli.sys.stdout.close()
                                cli.sys.stderr.close()
            content = (Path(directory) / "errors.log").read_text(encoding="utf-8")
            self.assertIn("trace from bundled executable", content)


if __name__ == "__main__":
    unittest.main()
