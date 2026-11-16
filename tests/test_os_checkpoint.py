"""Durable OS checkpoint validation, atomicity and cross-process safety."""
from __future__ import annotations

import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from neonvoid.content.sectors import SectorCampaign
from neonvoid.os_checkpoint import (
    CheckpointCorruptError, CheckpointConflictError, CheckpointStore,
    MAX_CHECKPOINT_BYTES, validate_checkpoint,
)


def sample_checkpoint():
    return {
        "schema_version": 1, "revision": 1,
        "campaign": SectorCampaign("os-test", seed=12).checkpoint(),
        "scene": "hub", "sector": None,
        "hub_position": [1200.0, 780.0],
        "player_position": [1200.0, 780.0],
        "deployed_level": 1,
        "player": {"hp": 100.0, "mana": 100.0, "charges": 1,
                   "fire_cd": 0.0, "pulse_cd": 0.0,
                   "guard_cd": 0.0, "guard_t": 0.0},
        "software_levels": {},
        "pending_cards": [],
        "infection": {"player_infection": 0.0, "os_integrity": 100.0},
        "purify_uses": 3, "salvage_claimed": False,
        "score": 0, "kills": 0, "combo": 0, "run_kills": 0,
        "run_boss_kills": 0, "run_best_combo": 0, "elapsed_frames": 0,
    }


class CheckpointTests(unittest.TestCase):
    def test_round_trip_revision_backup_and_clear(self):
        with tempfile.TemporaryDirectory() as directory:
            store = CheckpointStore(Path(directory) / "os_checkpoint.json")
            self.assertIsNone(store.load())
            payload = sample_checkpoint()
            self.assertEqual(store.save(payload, expected_revision=None), 1)
            self.assertEqual(store.load()["campaign"]["run_id"], "os-test")
            payload["score"] = 42
            self.assertEqual(store.save(payload, expected_revision=1), 2)
            self.assertEqual(store.load()["score"], 42)
            backup = Path(str(store.path) + ".bak")
            self.assertEqual(json.loads(backup.read_text("utf-8"))["score"], 0)
            self.assertTrue(store.clear(expected_revision=2))
            self.assertIsNone(store.load())
            self.assertTrue(store.clear(expected_revision=2))

    def test_stale_revision_and_run_replacement_refused(self):
        with tempfile.TemporaryDirectory() as directory:
            store = CheckpointStore(Path(directory) / "os_checkpoint.json")
            payload = sample_checkpoint()
            store.save(payload, expected_revision=None)
            with self.assertRaises(CheckpointConflictError):
                store.save(payload, expected_revision=None)
            with self.assertRaises(CheckpointConflictError):
                store.clear(expected_revision=9)
            with self.assertRaises(CheckpointConflictError):
                store.save(payload, expected_revision=2)
            payload["campaign"]["run_id"] = "another-os"
            with self.assertRaises(CheckpointConflictError):
                store.save(payload, expected_revision=1)
            self.assertEqual(store.load()["campaign"]["run_id"], "os-test")

    def test_reject_unsafe_schema_and_wave_state(self):
        original = sample_checkpoint()
        invalid = [
            ("schema_version", 0), ("schema_version", 2), ("revision", True),
            ("sector", "not-a-sector"), ("scene", "unknown"), ("score", -1),
            ("kills", 1.0), ("purify_uses", 7), ("salvage_claimed", 0),
            ("deployed_level", 31), ("player_position", [float("nan"), 0]),
            ("player_position", [20000, 0]), ("software_levels", {"fake": 1}),
            ("software_levels", {"power": 999}),
            ("infection", {"player_infection": 120, "os_integrity": 30}),
            ("player", {"hp": 100}),
        ]
        for field, value in invalid:
            with self.subTest(field=field, value=value):
                data = sample_checkpoint()
                data[field] = value
                with self.assertRaises(ValueError):
                    validate_checkpoint(data)
        invalid_sector = sample_checkpoint()
        invalid_sector["scene"] = "upgrade"
        invalid_sector["sector"] = "cpu"
        invalid_sector["pending_cards"] = ["power", "hull", "bonus"]
        with self.assertRaises(ValueError):
            validate_checkpoint(invalid_sector)  # No completed CPU wave.
        invalid_sector["scene"] = "hub"
        with self.assertRaises(ValueError):
            validate_checkpoint(invalid_sector)  # Hub with active sector.

    def test_upgrade_cards_only_after_valid_completed_wave(self):
        data = sample_checkpoint()
        data["campaign"]["completed_waves"]["CPU"] = 3
        data["scene"] = "upgrade"
        data["sector"] = "cpu"
        data["player_position"] = [200.0, 360.0]
        data["pending_cards"] = ["power", "rapid", "bonus"]
        data["software_levels"] = {"hull": 2, "mana": 1}
        data["player"]["hp"] = 123
        data["player"]["mana"] = 120
        validate_checkpoint(data)
        data["pending_cards"] = []
        with self.assertRaises(ValueError):
            validate_checkpoint(data)

    def test_corrupted_bytes_stay_preserved_until_explicit_archive(self):
        with tempfile.TemporaryDirectory() as directory:
            store = CheckpointStore(Path(directory) / "os_checkpoint.json")
            store.path.write_bytes(b"{broken checkpoint")
            with self.assertRaises(CheckpointCorruptError):
                store.load()
            self.assertEqual(store.path.read_bytes(), b"{broken checkpoint")
            with self.assertRaises(CheckpointCorruptError):
                store.save(sample_checkpoint(), expected_revision=None)
            preserved = store.archive_corrupt()
            self.assertTrue(preserved.exists())
            self.assertEqual(preserved.read_bytes(), b"{broken checkpoint")
            self.assertIsNone(store.load())
            self.assertEqual(store.save(sample_checkpoint(), expected_revision=None), 1)

    def test_oversized_and_nonfinite_payloads_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            store = CheckpointStore(Path(directory) / "os_checkpoint.json")
            store.path.write_bytes(b"x" * (MAX_CHECKPOINT_BYTES + 1))
            with self.assertRaises(CheckpointCorruptError):
                store.load()
        data = sample_checkpoint()
        data["player"]["hp"] = float("inf")
        with self.assertRaises(ValueError):
            validate_checkpoint(data)

    def test_atomic_failure_leaves_last_valid_checkpoint(self):
        with tempfile.TemporaryDirectory() as directory:
            store = CheckpointStore(Path(directory) / "os_checkpoint.json")
            self.assertEqual(store.save(sample_checkpoint(), expected_revision=None), 1)
            data = sample_checkpoint()
            data["score"] = 900
            from neonvoid import os_checkpoint as module
            original = module.os.replace

            def fail_primary(source, target):
                if Path(target) == store.path:
                    raise OSError("simulated disk failure")
                return original(source, target)

            with patch.object(module.os, "replace", side_effect=fail_primary):
                self.assertIsNone(store.save(data, expected_revision=1))
            self.assertEqual(store.load()["revision"], 1)
            self.assertEqual(store.load()["score"], 0)
            self.assertEqual(store.save(data, expected_revision=1), 2)

    def test_finished_sector_progress_retained_but_no_live_wave(self):
        campaign = SectorCampaign("os-test", seed=65)
        campaign.enter_sector("CPU")
        for wave in range(1, 4):
            campaign.start_wave()
            campaign.clear_wave(hostiles_remaining=0)
        campaign.start_wave()  # unfinished wave four must not be serialized
        data = sample_checkpoint()
        data["campaign"] = campaign.checkpoint()
        data["sector"] = "cpu"
        data["scene"] = "play"
        data["player_position"] = [200, 360]
        validate_checkpoint(data)
        restarted = SectorCampaign.from_checkpoint(data["campaign"])
        restarted.enter_sector("CPU")
        self.assertEqual(restarted.start_wave().wave_index, 4)
        self.assertEqual(restarted.progress["CPU"].completed_waves, 3)
        self.assertEqual(restarted.progress["MEMORY"].completed_waves, 0)


if __name__ == "__main__":
    unittest.main()
