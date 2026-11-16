"""One-software extraction contract for fully cleaned operating-system runs."""
from __future__ import annotations

from collections.abc import Collection, Mapping
from copy import deepcopy
from dataclasses import dataclass

from .content.upgrades import UP_BY_ID
from .metaprogression import (
    AntivirusDatabase, REQUIRED_SECTOR_IDS, _run_id, _software_id,
    validate_database,
)


@dataclass(frozen=True)
class ExtractionOffer:
    allowed: bool
    reason: str
    eligible_software: tuple[str, ...] = ()


@dataclass(frozen=True)
class ExtractionResult:
    applied: bool
    reason: str
    software_id: str | None = None


def _validate_software_levels(software_levels: Mapping[str, int]) -> None:
    if not isinstance(software_levels, Mapping):
        raise ValueError("software_levels must be a mapping")
    for uid, count in software_levels.items():
        _software_id(uid)
        if type(count) is not int or not 0 <= count <= UP_BY_ID[uid][4]:
            raise ValueError(f"invalid software level for {uid!r}")


def eligible_software(
    software_levels: Mapping[str, int], archive: Mapping[str, int]
) -> tuple[str, ...]:
    """Only current-run owned IDs with remaining archive capacity."""
    _validate_software_levels(software_levels)
    if not isinstance(archive, Mapping):
        raise ValueError("archive must be a mapping")
    for uid, count in archive.items():
        _software_id(uid)
        if type(count) is not int or not 1 <= count <= UP_BY_ID[uid][4]:
            raise ValueError(f"invalid archive count for {uid!r}")
    return tuple(sorted(uid for uid, count in software_levels.items()
                        if count >= 1 and archive.get(uid, 0) < UP_BY_ID[uid][4]))


def _fully_cleaned(db: AntivirusDatabase, run_id: str,
                   clean_sector_ids: Collection[str]) -> bool:
    if isinstance(clean_sector_ids, (str, bytes)) or not isinstance(clean_sector_ids, Collection):
        raise ValueError("clean_sector_ids must be a collection of sector IDs")
    sector_set = set(clean_sector_ids)
    if not all(isinstance(s, str) for s in sector_set):
        raise ValueError("sector IDs must be strings")
    if not REQUIRED_SECTOR_IDS.issubset(sector_set):
        return False
    receipts = db.awarded_xp_ids
    return (
        all(f"{run_id}:{sector}:sector_cleared" in receipts
            for sector in REQUIRED_SECTOR_IDS)
        and f"{run_id}:os_cleaned" in receipts
    )


def prepare_extraction(
    db: AntivirusDatabase, *, run_id: str,
    clean_sector_ids: Collection[str],
    software_levels: Mapping[str, int],
    was_defeated: bool = False, abandoned: bool = False,
) -> ExtractionOffer:
    """Check eligibility without changing or persisting the database."""
    validate_database(db)
    run_id = _run_id(run_id)
    if type(was_defeated) is not bool or type(abandoned) is not bool:
        raise ValueError("terminal run flags must be bool")
    _validate_software_levels(software_levels)
    if run_id in db.processed_extractions:
        return ExtractionOffer(False, "already_extracted")
    if was_defeated or abandoned or run_id in db.closed_runs:
        return ExtractionOffer(False, "run_failed_or_closed")
    if not _fully_cleaned(db, run_id, clean_sector_ids):
        return ExtractionOffer(False, "sectors_not_clean")
    if not any(count > 0 for count in software_levels.values()):
        return ExtractionOffer(False, "no_software")
    options = eligible_software(software_levels, db.software_archive)
    if not options:
        return ExtractionOffer(False, "archive_full")
    return ExtractionOffer(True, "ready", options)


def commit_extraction_once(
    db: AntivirusDatabase, *, run_id: str,
    clean_sector_ids: Collection[str],
    software_levels: Mapping[str, int],
    selected_software_id: str,
    was_defeated: bool = False, abandoned: bool = False,
) -> tuple[AntivirusDatabase, ExtractionResult]:
    """Return a new copy with one archive increment + a run receipt, or no change.

    To *commit* a reward durably, use AntivirusDatabaseStore.commit_extraction_once:
    it saves this transition atomically under an exclusive filesystem lock.
    """
    offer = prepare_extraction(db, run_id=run_id, clean_sector_ids=clean_sector_ids,
                               software_levels=software_levels, was_defeated=was_defeated,
                               abandoned=abandoned)
    if not offer.allowed:
        return db, ExtractionResult(False, offer.reason)
    if selected_software_id not in offer.eligible_software:
        return db, ExtractionResult(False, "invalid_selection")
    updated = deepcopy(db)
    updated.software_archive[selected_software_id] = (
        updated.software_archive.get(selected_software_id, 0) + 1
    )
    updated.processed_extractions[run_id] = selected_software_id
    updated.closed_runs[run_id] = "extracted"
    validate_database(updated)
    return updated, ExtractionResult(True, "extracted", selected_software_id)


def select_starting_software(
    db: AntivirusDatabase, software_id: str | None
) -> str | None:
    """Choose zero or ONE archived module for a new deployment, not all stacks."""
    validate_database(db)
    if software_id is None:
        return None
    _software_id(software_id)
    if db.software_archive.get(software_id, 0) < 1:
        raise ValueError("software ID is not in permanent archive")
    return software_id


def starting_software_levels(
    db: AntivirusDatabase, software_id: str | None
) -> dict[str, int]:
    """Return current-run install levels: at most one software, at level 1."""
    selected = select_starting_software(db, software_id)
    return {} if selected is None else {selected: 1}
