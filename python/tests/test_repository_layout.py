"""The default checkout is a Godot project; legacy Python remains isolated.

This test never reads or modifies live player progress.
"""
from pathlib import Path
import unittest
from neonvoid.runtime_paths import data_directory

PYTHON_ROOT = Path(__file__).resolve().parents[1]
REPO_ROOT = PYTHON_ROOT.parent


class RepositoryLayoutTests(unittest.TestCase):
    def test_godot_is_default_repo_root(self):
        for name in ("project.godot", "export_presets.cfg", "scenes/main.tscn",
                     "scripts/main.gd", "tests/integration_smoke.gd"):
            with self.subTest(name=name):
                self.assertTrue((REPO_ROOT / name).is_file(), name)
        self.assertFalse((REPO_ROOT / "godot" / "project.godot").exists())

    def test_python_is_self_contained(self):
        for name in ("pyproject.toml", "README.md", "neon_void.py",
                     "neonvoid/__init__.py", "tests/test_core.py",
                     "tests/fixtures/godot_migration/save.json"):
            with self.subTest(name=name):
                self.assertTrue((PYTHON_ROOT / name).is_file(), name)
        self.assertFalse((REPO_ROOT / "neon_void.py").exists())

    def test_source_saves_keep_original_location(self):
        # A folder refactor MUST NOT silently orphan existing player saves.
        self.assertEqual(Path(data_directory(frozen=False)), REPO_ROOT)
        for name in ("save.json", "settings.json", "profile.json",
                     "antivirus_db.json", "os_checkpoint.json"):
            self.assertFalse((REPO_ROOT / name).is_symlink(),
                             "source saves must not become symlinked by layout change")


if __name__ == "__main__":
    unittest.main()
