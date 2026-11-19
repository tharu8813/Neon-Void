"""Versioned player profile and run-event tracking tests."""
import json
import os
import tempfile
import unittest

from neonvoid.events import EventBus
from neonvoid.profile import (
    MAX_RECENT_RUNS, PlayerProfile, ProfileTracker, RunRecord,
    load_profile, save_profile,
)


class PlayerProfileTests(unittest.TestCase):
    def test_roundtrip_and_no_temporary_files(self):
        with tempfile.TemporaryDirectory() as directory:
            path = os.path.join(directory, "profile.json")
            obj = PlayerProfile(total_kills=6, boss_kills=1, total_score=300,
                                best_combo=5,
                                recent_runs=[RunRecord(status="dead", score=300,
                                                       wave=5, kills=6,
                                                       boss_kills=1, best_combo=5,
                                                       duration_seconds=20.5)])
            self.assertTrue(save_profile(obj, path))
            self.assertEqual(load_profile(path), obj)
            self.assertEqual(os.listdir(directory), ["profile.json"])
            with open(path, "r", encoding="utf-8") as stream:
                self.assertEqual(json.load(stream)["schema_version"], 2)

    def test_invalid_schema_and_corrupt_file_use_defaults(self):
        with tempfile.TemporaryDirectory() as directory:
            path = os.path.join(directory, "profile.json")
            for payload in (
                {"schema_version": 3},
                {"schema_version": True},
                {"schema_version": 1, "total_kills": -3},
                {"schema_version": 1, "completed_runs": 1.2},
                {"schema_version": 1, "recent_runs": "not a list"},
                {"schema_version": 1, "recent_runs": [{"status": "fake"}]},
                {"schema_version": 1, "recent_runs": [{"duration_seconds": float("nan")}]},
            ):
                with self.subTest(payload=payload):
                    with open(path, "w", encoding="utf-8") as stream:
                        json.dump(payload, stream)
                    with self.assertLogs("neonvoid.profile", "WARNING"):
                        self.assertEqual(load_profile(path), PlayerProfile())
            with open(path, "w", encoding="utf-8") as stream:
                stream.write("invalid json")
            with self.assertLogs("neonvoid.profile", "WARNING"):
                self.assertEqual(load_profile(path), PlayerProfile())

    def test_invalid_save_preserves_previous_profile(self):
        with tempfile.TemporaryDirectory() as directory:
            path = os.path.join(directory, "profile.json")
            self.assertTrue(save_profile(PlayerProfile(total_kills=7), path))
            with self.assertLogs("neonvoid.profile", "WARNING"):
                self.assertFalse(save_profile(PlayerProfile(total_kills=-1), path))
            self.assertEqual(load_profile(path).total_kills, 7)
            self.assertEqual(os.listdir(directory), ["profile.json"])

    def test_tracker_death_and_abandonment(self):
        bus = EventBus()
        profile = PlayerProfile()
        persisted = []
        tracker = ProfileTracker(bus, profile, lambda value: persisted.append(value.total_kills) or True)
        bus.emit("run_started", runs=1)
        self.assertTrue(tracker.active)
        bus.emit("enemy_killed", kind="drifter", combo=3)
        bus.emit("enemy_killed", kind="boss", combo=6)
        bus.emit("run_ended", score=800, wave=5, duration_frames=900)
        self.assertFalse(tracker.active)
        self.assertEqual(profile.total_kills, 2)
        self.assertEqual(profile.boss_kills, 1)
        self.assertEqual(profile.completed_runs, 1)
        self.assertEqual(profile.best_combo, 6)
        self.assertEqual(profile.total_score, 800)
        self.assertEqual(profile.recent_runs[0].duration_seconds, 15)
        self.assertEqual(persisted, [2])
        bus.emit("run_ended", score=50, wave=6, duration_frames=600)
        self.assertEqual(profile.completed_runs, 1)
        bus.emit("run_started", runs=2)
        bus.emit("enemy_killed", kind="drifter", combo=1)
        bus.emit("run_abandoned", score=200, wave=2, duration_frames=120)
        self.assertEqual(profile.completed_runs, 1)
        self.assertEqual(profile.abandoned_runs, 1)
        self.assertEqual(profile.total_kills, 3)
        self.assertEqual(profile.total_score, 1000)
        self.assertEqual(profile.recent_runs[0].status, "abandoned")
        self.assertEqual(persisted, [2, 3])

    def test_v1_profile_is_migrated_and_extracted_runs_are_recorded(self):
        with tempfile.TemporaryDirectory() as directory:
            path = os.path.join(directory, "profile.json")
            with open(path, "w", encoding="utf-8") as stream:
                json.dump({"schema_version": 1, "completed_runs": 2,
                           "abandoned_runs": 1, "total_kills": 5}, stream)
            previous = load_profile(path)
            self.assertEqual(previous.completed_runs, 2)
            self.assertEqual(previous.extracted_runs, 0)
            bus = EventBus()
            tracker = ProfileTracker(bus, previous)
            bus.emit("run_started", runs=4)
            bus.emit("enemy_killed", kind="overseer", boss=True, combo=8)
            bus.emit("run_extracted", score=900, wave=5,
                     duration_frames=1200)
            self.assertEqual(previous.extracted_runs, 1)
            self.assertEqual(previous.completed_runs, 2)
            self.assertEqual(previous.boss_kills, 1)
            self.assertEqual(previous.recent_runs[0].status, "extracted")
            self.assertTrue(save_profile(previous, path))
            self.assertEqual(load_profile(path), previous)

    def test_recent_runs_are_bounded(self):
        profile = PlayerProfile()
        for i in range(MAX_RECENT_RUNS + 6):
            profile.record_run(RunRecord(score=i, status="dead"))
        self.assertEqual(len(profile.recent_runs), MAX_RECENT_RUNS)
        self.assertEqual(profile.recent_runs[0].score, MAX_RECENT_RUNS + 5)
        self.assertEqual(profile.completed_runs, MAX_RECENT_RUNS + 6)


if __name__ == "__main__":
    unittest.main()
