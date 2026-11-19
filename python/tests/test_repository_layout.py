"""Python-only repository layout and legacy save-path safety checks."""
from pathlib import Path
import unittest
from neonvoid.runtime_paths import data_directory

PYTHON_ROOT = Path(__file__).resolve().parents[1]
REPO_ROOT = PYTHON_ROOT.parent


class RepositoryLayoutTests(unittest.TestCase):
    def test_python_only_layout(self):
        for name in ("pyproject.toml", "README.md", "neon_void.py",
                     "neonvoid/__init__.py", "tests/test_core.py"):
            with self.subTest(name=name):
                self.assertTrue((PYTHON_ROOT / name).is_file(), name)
        self.assertFalse((REPO_ROOT / "project.godot").exists())
        self.assertFalse((REPO_ROOT / "neon_void.py").exists())

    def test_source_saves_keep_original_location(self):
        self.assertEqual(Path(data_directory(frozen=False)), REPO_ROOT)
        for name in ("save.json", "settings.json", "profile.json",
                     "antivirus_db.json", "os_checkpoint.json"):
            self.assertFalse((REPO_ROOT / name).is_symlink())


if __name__ == "__main__":
    unittest.main()
