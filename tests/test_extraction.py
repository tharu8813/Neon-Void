"""OS extraction: 4 clean sectors, a single copy, and durable idempotency."""
from __future__ import annotations

from dataclasses import replace
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from neonvoid import database_storage as storage
from neonvoid.extraction import (
    commit_extraction_once, eligible_software, prepare_extraction,
    select_starting_software, starting_software_levels,
)
from neonvoid.metaprogression import (
    AntivirusDatabase, REQUIRED_SECTOR_IDS, close_run_once,
)


def _clean_run(store: storage.AntivirusDatabaseStore, run_id: str):
    for sector in REQUIRED_SECTOR_IDS:
        for event, wave in (("wave_cleared", 5), ("boss_killed", None),
                            ("sector_cleared", None)):
            _, result = store.award_progress_once(
                run_id=run_id, event=event, sector_id=sector, wave_index=wave)
            assert result.applied, (run_id, sector, event, result)
    _, result = store.award_progress_once(run_id=run_id, event="os_cleaned")
    assert result.applied, result


class ExtractionTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.path = Path(directory.name) / "antivirus_db.json"
        self.store = storage.AntivirusDatabaseStore(self.path)
        self.sectors = tuple(REQUIRED_SECTOR_IDS)

    def _commit(self, run_id="os", software_levels=None, selected="rapid", **flags):
        return self.store.commit_extraction_once(
            run_id=run_id, clean_sector_ids=self.sectors,
            software_levels={"rapid": 2} if software_levels is None else software_levels,
            selected_software_id=selected, **flags)

    def test_four_clean_sectors_and_os_receipt_required(self):
        db = self.store.load()
        offer = prepare_extraction(db, run_id="os", clean_sector_ids=self.sectors,
                                   software_levels={"rapid": 2})
        self.assertEqual(offer.reason, "sectors_not_clean")
        db, result = self._commit()
        self.assertFalse(result.applied)
        self.assertFalse(self.path.exists())
        for sector in self.sectors:
            for event, wave in (("wave_cleared", 5), ("boss_killed", None),
                                ("sector_cleared", None)):
                self.store.award_progress_once(run_id="os", event=event,
                                              sector_id=sector, wave_index=wave)
        db, result = self._commit()
        self.assertEqual(result.reason, "sectors_not_clean")
        self.store.award_progress_once(run_id="os", event="os_cleaned")
        db, result = self.store.commit_extraction_once(
            run_id="os", clean_sector_ids=self.sectors[:-1],
            software_levels={"rapid": 2}, selected_software_id="rapid")
        self.assertEqual(result.reason, "sectors_not_clean")
        offer = prepare_extraction(db, run_id="os", clean_sector_ids=self.sectors,
                                   software_levels={"rapid": 2})
        self.assertTrue(offer.allowed)
        self.assertEqual(offer.eligible_software, ("rapid",))

    def test_success_extracts_only_one_copy_not_run_stack(self):
        _clean_run(self.store, "os")
        db, result = self._commit()
        self.assertEqual((result.applied, result.reason, result.software_id),
                         (True, "extracted", "rapid"))
        self.assertEqual(db.software_archive, {"rapid": 1})
        self.assertEqual(db.processed_extractions, {"os": "rapid"})
        self.assertEqual(db.closed_runs, {"os": "extracted"})
        self.assertEqual(self.store.load(), db)
        self.assertEqual(starting_software_levels(db, "rapid"), {"rapid": 1})
        self.assertEqual(starting_software_levels(db, None), {})
        self.assertEqual(select_starting_software(db, "rapid"), "rapid")
        with self.assertRaises(ValueError):
            select_starting_software(db, "power")
        with self.assertRaises(ValueError):
            select_starting_software(db, "not_existing")

    def test_duplicate_extraction_new_selection_cannot_award(self):
        _clean_run(self.store, "os")
        before, result = self._commit()
        self.assertTrue(result.applied)
        after, result = self._commit(software_levels={"power": 1}, selected="power")
        self.assertEqual((result.applied, result.reason), (False, "already_extracted"))
        self.assertEqual(after, before)
        self.assertEqual(self.store.load().software_archive, {"rapid": 1})

    def test_was_defeated_or_abandoned_no_extraction(self):
        for status in ("dead", "abandoned"):
            run = status
            _clean_run(self.store, run)
            before = self.store.load()
            after, result = self._commit(run, **({"was_defeated": True}
                                                 if status == "dead" else {"abandoned": True}))
            self.assertFalse(result.applied)
            self.assertEqual(result.reason, "run_failed_or_closed")
            self.assertEqual(after, before)
            after, outcome = self.store.close_run_once(run_id=run, status=status)
            self.assertTrue(outcome.applied)
            after, result = self._commit(run)
            self.assertEqual(result.reason, "run_failed_or_closed")
            self.assertEqual(after.software_archive, {})
            self.assertGreater(after.xp, 0)  # earned XP remains forever

    def test_empty_software_not_claimed_and_can_recover(self):
        _clean_run(self.store, "os")
        db, result = self._commit(software_levels={}, selected="rapid")
        self.assertEqual(result.reason, "no_software")
        self.assertNotIn("os", db.closed_runs)
        db, result = self._commit(software_levels={"power": 1}, selected="power")
        self.assertTrue(result.applied)
        self.assertEqual(db.software_archive, {"power": 1})

    def test_invalid_selection_and_catalog_validation(self):
        _clean_run(self.store, "os")
        before, result = self._commit(software_levels={"rapid": 1}, selected="power")
        self.assertEqual(result.reason, "invalid_selection")
        self.assertFalse(result.applied)
        self.assertNotIn("os", before.closed_runs)
        for levels in ({"fake_module": 1}, {"rapid": -1},
                       {"rapid": 7}, {"rapid": True}, {"rapid": 1.5}):
            with self.subTest(levels=levels), self.assertRaises(ValueError):
                self._commit(software_levels=levels)
        with self.assertRaises(ValueError):
            self._commit(was_defeated=1)

    def test_max_archive_cap_and_alternative(self):
        _clean_run(self.store, "one")
        self.assertTrue(self._commit("one", {"rear": 1}, "rear")[1].applied)
        _clean_run(self.store, "two")
        db, result = self._commit("two", {"rear": 1}, "rear")
        self.assertEqual(result.reason, "archive_full")
        self.assertEqual(db.software_archive["rear"], 1)
        db, result = self._commit("two", {"rear": 1, "rapid": 1}, "rapid")
        self.assertTrue(result.applied)
        self.assertEqual(db.software_archive, {"rear": 1, "rapid": 1})
        self.assertEqual(eligible_software({"rear": 1, "power": 2}, db.software_archive),
                         ("power",))

    def test_extraction_save_failure_does_not_publish_or_consume(self):
        _clean_run(self.store, "os")
        before = self.store.load()
        with patch.object(storage.os, "replace", side_effect=OSError("no space")):
            with self.assertLogs("neonvoid.database_storage", "WARNING"):
                after, result = self._commit()
        self.assertFalse(result.applied)
        self.assertEqual(result.reason, "save_failed")
        self.assertEqual(after, before)
        self.assertEqual(self.store.load(), before)
        after, result = self._commit()
        self.assertTrue(result.applied)
        self.assertEqual(after.software_archive["rapid"], 1)

    def test_pure_transitions_do_not_mutate_and_reject_partial(self):
        db = AntivirusDatabase()
        _, status = close_run_once(db, run_id="aborted", status="abandoned")
        self.assertTrue(status.applied)
        new, status = commit_extraction_once(
            db, run_id="os", clean_sector_ids=self.sectors,
            software_levels={"rapid": 1}, selected_software_id="rapid")
        self.assertFalse(status.applied)
        self.assertIs(new, db)
        self.assertEqual(db.software_archive, {})
        self.assertEqual(db.processed_extractions, {})


if __name__ == "__main__":
    unittest.main()
