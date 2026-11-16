"""Antivirus DB serialization, XP proof, and crash/retry safety."""
from __future__ import annotations

from concurrent.futures import ThreadPoolExecutor
from dataclasses import replace
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from neonvoid import database_storage as storage
from neonvoid.metaprogression import (
    AntivirusDatabase, MAX_ANTIVIRUS_LEVEL, REQUIRED_SECTOR_IDS,
    award_progress_once, base_stats_for_level, close_run_once,
    database_from_dict, database_to_dict, grant_xp_once, level_from_xp,
    validate_database, xp_progress, xp_threshold,
)


class ProgressionTests(unittest.TestCase):
    def test_initial_cap_and_exact_stats(self):
        self.assertEqual(AntivirusDatabase().antivirus_level, 1)
        self.assertEqual(MAX_ANTIVIRUS_LEVEL, 30)
        self.assertEqual(xp_threshold(1), 100)
        self.assertEqual(xp_threshold(2), 150)
        self.assertEqual(base_stats_for_level(1),
                         {"maxhp": 100.0, "dmg": 10.0, "armor": 4.0, "maxmana": 100.0})
        self.assertEqual(base_stats_for_level(2),
                         {"maxhp": 102.0, "dmg": 10.2, "armor": 4.15, "maxmana": 101.0})
        self.assertEqual(base_stats_for_level(30),
                         {"maxhp": 158.0, "dmg": 15.8, "armor": 8.35, "maxmana": 129.0})
        for bad in (True, 0, -1, 31, 2.0):
            with self.subTest(bad=bad), self.assertRaises(ValueError):
                base_stats_for_level(bad)
            with self.assertRaises(ValueError):
                xp_threshold(bad)

    def test_cumulative_xp_and_level_cap(self):
        db = AntivirusDatabase()
        db2, result = grant_xp_once(db, run_id="os1", event_id="cpu:wave:1", amount=99)
        self.assertTrue(result.applied)
        self.assertEqual(db.xp, 0)
        self.assertEqual(xp_progress(db2), (99, 100))
        db3, result = grant_xp_once(db2, run_id="os1", event_id="cpu:wave:2", amount=1)
        self.assertEqual((result.level_before, result.level_after), (1, 2))
        self.assertEqual(xp_progress(db3), (0, 150))
        required = sum(xp_threshold(level) for level in range(1, 30))
        db4, result = grant_xp_once(db3, run_id="os1", event_id="large", amount=required * 3)
        self.assertTrue(result.applied)
        self.assertEqual(db4.antivirus_level, 30)
        self.assertEqual(xp_progress(db4), (0, 0))
        self.assertEqual(level_from_xp(db4.xp), 30)

    def test_duplicate_xp_and_terminal_run(self):
        db = AntivirusDatabase()
        first, result = grant_xp_once(db, run_id="same", event_id="cpu:wave:1", amount=40)
        self.assertTrue(result.applied)
        second, result = grant_xp_once(first, run_id="same", event_id="cpu:wave:1", amount=999)
        self.assertIs(second, first)
        self.assertEqual(result.reason, "duplicate")
        dead, result = close_run_once(first, run_id="same", status="dead")
        self.assertTrue(result.applied)
        after, result = grant_xp_once(dead, run_id="same", event_id="cpu:wave:2", amount=20)
        self.assertIs(after, dead)
        self.assertEqual(result.reason, "run_closed")
        self.assertEqual(after.xp, 40)
        again, result = close_run_once(dead, run_id="same", status="abandoned")
        self.assertIs(again, dead)
        self.assertFalse(result.applied)

    def test_award_receipt_prerequisites_and_independent_sectors(self):
        db = AntivirusDatabase()
        db, res = award_progress_once(db, run_id="os", event="os_cleaned")
        self.assertEqual(res.reason, "prerequisites_missing")
        for sector in REQUIRED_SECTOR_IDS:
            db, res = award_progress_once(db, run_id="os", event="sector_cleared", sector_id=sector)
            self.assertFalse(res.applied)
            for wave in range(1, 6):
                db, res = award_progress_once(db, run_id="os", event="wave_cleared",
                                               sector_id=sector, wave_index=wave)
                self.assertTrue(res.applied)
            db, res = award_progress_once(db, run_id="os", event="boss_killed", sector_id=sector)
            self.assertTrue(res.applied)
            db, res = award_progress_once(db, run_id="os", event="sector_cleared", sector_id=sector)
            self.assertTrue(res.applied)
            db2, res = award_progress_once(db, run_id="os", event="sector_cleared", sector_id=sector)
            self.assertIs(db2, db)
            self.assertEqual(res.reason, "duplicate")
        db, res = award_progress_once(db, run_id="os", event="os_cleaned")
        self.assertTrue(res.applied)
        self.assertEqual(db.xp, 4 * (5 * 20 + 75 + 50) + 200)
        db2, res = award_progress_once(db, run_id="os", event="os_cleaned")
        self.assertEqual((res.applied, res.reason), (False, "duplicate"))

    def test_invalid_event_values(self):
        for kwargs in (
            {"run_id": "bad:run", "event_id": "ok", "amount": 1},
            {"run_id": "ok", "event_id": "bad event", "amount": 1},
            {"run_id": "ok", "event_id": "ok", "amount": True},
            {"run_id": "ok", "event_id": "ok", "amount": 0},
        ):
            with self.subTest(kwargs=kwargs), self.assertRaises(ValueError):
                grant_xp_once(AntivirusDatabase(), **kwargs)
        with self.assertRaises(ValueError):
            award_progress_once(AntivirusDatabase(), run_id="ok", event="wave_cleared",
                                sector_id="cpu_core", wave_index=True)
        with self.assertRaises(ValueError):
            award_progress_once(AntivirusDatabase(), run_id="ok", event="not_real")
        with self.assertRaises(ValueError):
            close_run_once(AntivirusDatabase(), run_id="ok", status="extracted")

    def test_json_validation_rejects_bad_payloads(self):
        valid = database_to_dict(AntivirusDatabase())
        invalid = [
            {**valid, "schema_version": True},
            {**valid, "schema_version": 2},
            {**valid, "xp": -1},
            {**valid, "xp": True},
            {**valid, "xp": float("nan")},
            {**valid, "antivirus_level": 2},
            {**valid, "software_archive": {"fake": 1}},
            {**valid, "software_archive": {"rapid": 7}},
            {**valid, "software_archive": {"rapid": True}},
            {**valid, "awarded_xp_ids": {"os:wave": 3}, "xp": 1},
            {**valid, "processed_extractions": {"os": "rapid"}},
            {**valid, "closed_runs": {"os": "extracted"}},
            {**valid, "closed_runs": {"os": "unknown"}},
            {**valid, "closed_runs": {"bad:run": "dead"}},
            {**valid, "unexpected": "field"},
        ]
        for raw in invalid:
            with self.subTest(raw=raw), self.assertRaises(ValueError):
                database_from_dict(raw)
        with self.assertRaises(ValueError):
            validate_database(replace(AntivirusDatabase(), xp=3))


class StorageTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.path = Path(self.directory.name) / "antivirus_db.json"
        self.store = storage.AntivirusDatabaseStore(self.path)

    def test_missing_file_starts_fresh_and_does_not_touch_legacy_files(self):
        for legacy in ("save.json", "profile.json", "settings.json"):
            (self.path.parent / legacy).write_bytes(b"unchanged: \x00 payload")
        self.assertEqual(self.store.load(), AntivirusDatabase())
        self.assertFalse(self.path.exists())
        db, result = self.store.grant_xp_once(run_id="a", event_id="start", amount=100)
        self.assertTrue(result.applied)
        self.assertEqual(db.antivirus_level, 2)
        self.assertEqual(self.store.load(), db)
        for legacy in ("save.json", "profile.json", "settings.json"):
            self.assertEqual((self.path.parent / legacy).read_bytes(), b"unchanged: \x00 payload")

    def test_roundtrip_atomic_backup_and_manual_recovery(self):
        first, _ = self.store.grant_xp_once(run_id="a", event_id="wave:1", amount=20)
        second, _ = self.store.grant_xp_once(run_id="a", event_id="wave:2", amount=30)
        self.assertEqual(self.store.load(), second)
        self.assertEqual(storage.load_database_backup(self.path), first)
        self.path.write_text("{ broken", encoding="utf-8")
        with self.assertRaises(storage.DatabaseCorruptError):
            self.store.load()
        with self.assertLogs("neonvoid.database_storage", "WARNING"):
            self.assertFalse(storage.save_database(AntivirusDatabase(), self.path))
        self.assertEqual(self.path.read_text(encoding="utf-8"), "{ broken")
        self.assertEqual(storage.load_database_backup(self.path), first)

    def test_invalid_bytes_corrupt_schema_and_invalid_save(self):
        self.path.write_bytes(b"\xff\xfe")
        with self.assertRaises(storage.DatabaseCorruptError):
            self.store.load()
        self.path.write_text(json.dumps({"schema_version": 999}))
        with self.assertRaises(storage.DatabaseCorruptError):
            self.store.load()
        self.path.unlink()
        with self.assertLogs("neonvoid.database_storage", "WARNING"):
            self.assertFalse(storage.save_database(replace(AntivirusDatabase(), xp=1), self.path))
        self.assertFalse(self.path.exists())

    def test_replace_failure_is_not_a_grant_and_retry_succeeds(self):
        self.store.grant_xp_once(run_id="os", event_id="wave:1", amount=20)
        original = self.store.load()
        with patch.object(storage.os, "replace", side_effect=OSError("disk full")):
            with self.assertLogs("neonvoid.database_storage", "WARNING"):
                after, result = self.store.grant_xp_once(run_id="os", event_id="wave:2", amount=20)
        self.assertFalse(result.applied)
        self.assertEqual(result.reason, "save_failed")
        self.assertEqual(after, original)
        self.assertEqual(self.store.load(), original)
        after, result = self.store.grant_xp_once(run_id="os", event_id="wave:2", amount=20)
        self.assertTrue(result.applied)
        self.assertEqual(after.xp, 40)
        _, duplicate = self.store.grant_xp_once(run_id="os", event_id="wave:2", amount=20)
        self.assertEqual(duplicate.reason, "duplicate")

    def test_multiple_store_objects_reload_under_lock(self):
        other = storage.AntivirusDatabaseStore(self.path)
        self.store.grant_xp_once(run_id="os", event_id="first", amount=11)
        other.grant_xp_once(run_id="os", event_id="second", amount=19)
        db = self.store.load()
        self.assertEqual(db.xp, 30)
        self.assertEqual(len(db.awarded_xp_ids), 2)

    def test_concurrent_same_receipt_only_once(self):
        def award(_):
            _, result = storage.AntivirusDatabaseStore(self.path).grant_xp_once(
                run_id="threaded", event_id="single", amount=25)
            return result
        with ThreadPoolExecutor(max_workers=5) as pool:
            outcomes = list(pool.map(award, range(12)))
        self.assertEqual(sum(outcome.applied for outcome in outcomes), 1)
        self.assertEqual(self.store.load().xp, 25)

    def test_terminal_run_persists_prior_xp(self):
        db, _ = self.store.grant_xp_once(run_id="dead", event_id="earned", amount=25)
        db, outcome = self.store.close_run_once(run_id="dead", status="abandoned")
        self.assertTrue(outcome.applied)
        after, outcome = self.store.grant_xp_once(run_id="dead", event_id="more", amount=99)
        self.assertFalse(outcome.applied)
        self.assertEqual(after.xp, 25)
        self.assertEqual(self.store.load().closed_runs["dead"], "abandoned")


if __name__ == "__main__":
    unittest.main()
