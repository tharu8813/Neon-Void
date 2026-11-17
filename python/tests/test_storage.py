import json
import os
import tempfile
import unittest
from unittest.mock import patch
from neonvoid import storage


class StorageTests(unittest.TestCase):
    def test_roundtrip(self):
        with tempfile.TemporaryDirectory() as directory:
            path = os.path.join(directory, "save.json")
            original = {"best_score": 345, "best_wave": 6, "runs": 4}
            with patch.object(storage, "SAVE_FILE", path):
                storage.write_save(original)
                self.assertEqual(storage.load_save(), original)
            with open(path, encoding="utf-8") as stream:
                self.assertEqual(json.load(stream), original)
            self.assertEqual(os.listdir(directory), ["save.json"])

    def test_corrupted_or_missing_file(self):
        with tempfile.TemporaryDirectory() as directory:
            path = os.path.join(directory, "save.json")
            with patch.object(storage, "SAVE_FILE", path):
                self.assertEqual(storage.load_save(),
                                 {"best_score": 0, "best_wave": 0, "runs": 0})
                with open(path, "w", encoding="utf-8") as stream:
                    stream.write("{bad")
                self.assertEqual(storage.load_save()["runs"], 0)

    def test_additional_fields_are_ignored(self):
        with tempfile.TemporaryDirectory() as directory:
            path = os.path.join(directory, "save.json")
            with open(path, "w", encoding="utf-8") as stream:
                json.dump({"best_score": 4, "best_wave": 2,
                           "runs": 1, "future_field": 3}, stream)
            with patch.object(storage, "SAVE_FILE", path):
                self.assertEqual(storage.load_save(),
                                 {"best_score": 4, "best_wave": 2, "runs": 1})


if __name__ == "__main__":
    unittest.main()
