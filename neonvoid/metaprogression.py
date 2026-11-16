"""Pure, versioned antivirus XP and level progression. No game/UI writes."""
from __future__ import annotations

from copy import deepcopy
from dataclasses import asdict, dataclass, field
import re
from typing import Mapping

from .content.upgrades import UP_BY_ID

SCHEMA_VERSION = 1
MAX_ANTIVIRUS_LEVEL = 30
MAX_XP = 2**63 - 1
REQUIRED_SECTOR_IDS = frozenset({
    "cpu_core", "memory_sector", "storage_vault", "network_grid",
})
XP_REWARDS = {
    "wave_cleared": 20,
    "boss_killed": 75,
    "sector_cleared": 50,
    "os_cleaned": 200,
}
_RUN_ID_RE = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,127}\Z")
_EVENT_ID_RE = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.:-]{0,191}\Z")
_CLOSED_STATUSES = frozenset({"dead", "abandoned", "extracted"})


@dataclass
class AntivirusDatabase:
    """Durable state. xp means *lifetime cumulative XP*, not unspent XP."""

    schema_version: int = SCHEMA_VERSION
    antivirus_level: int = 1
    xp: int = 0
    software_archive: dict[str, int] = field(default_factory=dict)
    awarded_xp_ids: dict[str, int] = field(default_factory=dict)
    processed_extractions: dict[str, str] = field(default_factory=dict)
    closed_runs: dict[str, str] = field(default_factory=dict)


@dataclass(frozen=True)
class RewardResult:
    applied: bool
    reason: str
    amount: int = 0
    level_before: int = 1
    level_after: int = 1


@dataclass(frozen=True)
class MutationResult:
    applied: bool
    reason: str


def _nonnegative_int(value: object, name: str, *, minimum: int = 0) -> int:
    if type(value) is not int or not minimum <= value <= MAX_XP:
        raise ValueError(f"{name} must be an integer in [{minimum}, {MAX_XP}]")
    return value


def _run_id(value: object) -> str:
    if not isinstance(value, str) or not _RUN_ID_RE.fullmatch(value):
        raise ValueError("run_id must be a stable nonempty ASCII identifier (max 128)")
    return value


def _event_id(value: object) -> str:
    if not isinstance(value, str) or not _EVENT_ID_RE.fullmatch(value):
        raise ValueError("event_id must be a stable nonempty ASCII identifier (max 192)")
    return value


def xp_threshold(level: int) -> int:
    """XP needed to advance from level to level + 1 (the level-30 cap applies)."""
    if type(level) is not int or not 1 <= level <= MAX_ANTIVIRUS_LEVEL:
        raise ValueError("invalid antivirus level")
    return 100 + 50 * (level - 1)


def level_from_xp(xp: int) -> int:
    remaining = _nonnegative_int(xp, "xp")
    level = 1
    while level < MAX_ANTIVIRUS_LEVEL and remaining >= xp_threshold(level):
        remaining -= xp_threshold(level)
        level += 1
    return level


def xp_progress(db: AntivirusDatabase) -> tuple[int, int]:
    """(XP in current level, XP required); level cap returns (0, 0)."""
    validate_database(db)
    if db.antivirus_level == MAX_ANTIVIRUS_LEVEL:
        return 0, 0
    spent = sum(xp_threshold(level) for level in range(1, db.antivirus_level))
    return db.xp - spent, xp_threshold(db.antivirus_level)


def base_stats_for_level(level: int) -> dict[str, float]:
    """Starting stats only: apply once to a NEW Player before software bonuses."""
    if type(level) is not int or not 1 <= level <= MAX_ANTIVIRUS_LEVEL:
        raise ValueError("invalid antivirus level")
    advances = level - 1
    return {
        "maxhp": float(100 + 2 * advances),
        "dmg": round(10.0 + 0.2 * advances, 6),
        "armor": round(4.0 + 0.15 * advances, 6),
        "maxmana": float(100 + advances),
    }


def _software_id(uid: object) -> str:
    if not isinstance(uid, str) or uid not in UP_BY_ID:
        raise ValueError(f"unknown software ID: {uid!r}")
    return uid


def _int_dict(value: object, name: str, *, software: bool = False) -> None:
    if not isinstance(value, dict):
        raise ValueError(f"{name} must be an object")
    for key, number in value.items():
        if software:
            _software_id(key)
            limit = UP_BY_ID[key][4]
            if type(number) is not int or not 1 <= number <= limit:
                raise ValueError(f"invalid {name}[{key!r}] (cap {limit})")
        else:
            _event_id(key)
            _nonnegative_int(number, f"{name}[{key!r}]", minimum=1)


def validate_database(db: AntivirusDatabase) -> None:
    """Fail closed for malformed data rather than silently resetting progress."""
    if not isinstance(db, AntivirusDatabase):
        raise ValueError("database must be AntivirusDatabase")
    if type(db.schema_version) is not int or db.schema_version != SCHEMA_VERSION:
        raise ValueError("unsupported database schema version")
    _nonnegative_int(db.xp, "xp")
    if type(db.antivirus_level) is not int or db.antivirus_level != level_from_xp(db.xp):
        raise ValueError("antivirus level is inconsistent with cumulative XP")
    _int_dict(db.software_archive, "software_archive", software=True)
    _int_dict(db.awarded_xp_ids, "awarded_xp_ids")
    if sum(db.awarded_xp_ids.values()) != db.xp:
        raise ValueError("XP does not match unique award receipts")
    if not isinstance(db.processed_extractions, dict) or not isinstance(db.closed_runs, dict):
        raise ValueError("run receipts must be objects")
    for run_id, uid in db.processed_extractions.items():
        _run_id(run_id)
        _software_id(uid)
        if db.closed_runs.get(run_id) != "extracted":
            raise ValueError("extraction receipt is missing terminal run state")
    for run_id, status in db.closed_runs.items():
        _run_id(run_id)
        if status not in _CLOSED_STATUSES:
            raise ValueError("invalid terminal run state")
        if (status == "extracted") != (run_id in db.processed_extractions):
            raise ValueError("terminal run state contradicts extraction receipt")
    for key in db.awarded_xp_ids:
        if ":" not in key:
            raise ValueError("XP receipt must include a run_id")
        _run_id(key.split(":", 1)[0])


def database_to_dict(db: AntivirusDatabase) -> dict:
    validate_database(db)
    return asdict(db)


def database_from_dict(raw: object) -> AntivirusDatabase:
    if not isinstance(raw, dict) or set(raw) != set(AntivirusDatabase.__dataclass_fields__):
        raise ValueError("database object has missing or unexpected fields")
    db = AntivirusDatabase(**raw)
    validate_database(db)
    return db


def grant_xp_once(
    db: AntivirusDatabase, *, run_id: str, event_id: str, amount: int
) -> tuple[AntivirusDatabase, RewardResult]:
    """Pure immutable-style transition: caller MUST persist returned copy."""
    validate_database(db)
    run_id, event_id = _run_id(run_id), _event_id(event_id)
    _nonnegative_int(amount, "amount", minimum=1)
    key = f"{run_id}:{event_id}"
    if len(key) > 256:
        raise ValueError("full XP receipt identifier is too long")
    if run_id in db.closed_runs:
        return db, RewardResult(False, "run_closed", level_before=db.antivirus_level,
                                level_after=db.antivirus_level)
    if key in db.awarded_xp_ids:
        return db, RewardResult(False, "duplicate", level_before=db.antivirus_level,
                                level_after=db.antivirus_level)
    if db.xp + amount > MAX_XP:
        raise ValueError("XP overflow")
    updated = deepcopy(db)
    before = updated.antivirus_level
    updated.xp += amount
    updated.antivirus_level = level_from_xp(updated.xp)
    updated.awarded_xp_ids[key] = amount
    return updated, RewardResult(True, "granted", amount, before, updated.antivirus_level)


def award_progress_once(
    db: AntivirusDatabase, *, run_id: str, event: str,
    sector_id: str | None = None, wave_index: int | None = None,
) -> tuple[AntivirusDatabase, RewardResult]:
    """Award known event types; prove sector/os completion from stored receipts."""
    validate_database(db)
    run_id = _run_id(run_id)
    if event not in XP_REWARDS:
        raise ValueError("unknown XP event")
    if event == "os_cleaned":
        if sector_id is not None or wave_index is not None:
            raise ValueError("os_cleaned has no sector/wave")
        required = {f"{run_id}:{sector}:sector_cleared" for sector in REQUIRED_SECTOR_IDS}
        event_id = "os_cleaned"
    else:
        if sector_id not in REQUIRED_SECTOR_IDS:
            raise ValueError("unknown sector_id")
        if event == "wave_cleared":
            if type(wave_index) is not int or not 1 <= wave_index <= 5:
                raise ValueError("wave_index must be an integer from 1 to 5")
            event_id = f"{sector_id}:wave:{wave_index}"
        else:
            if wave_index is not None:
                raise ValueError("wave_index is only for wave_cleared")
            event_id = f"{sector_id}:{event}"
        required = set()
        if event == "sector_cleared":
            required = {f"{run_id}:{sector_id}:wave:5",
                        f"{run_id}:{sector_id}:boss_killed"}
    # Idempotent retry must return duplicate before unmet-prerequisite checks.
    if f"{run_id}:{event_id}" in db.awarded_xp_ids:
        return grant_xp_once(db, run_id=run_id, event_id=event_id,
                             amount=XP_REWARDS[event])
    if not required.issubset(db.awarded_xp_ids):
        return db, RewardResult(False, "prerequisites_missing",
                                level_before=db.antivirus_level, level_after=db.antivirus_level)
    return grant_xp_once(db, run_id=run_id, event_id=event_id,
                         amount=XP_REWARDS[event])


def close_run_once(
    db: AntivirusDatabase, *, run_id: str, status: str
) -> tuple[AntivirusDatabase, MutationResult]:
    """Persist death/abandonment so the same run can never extract later."""
    validate_database(db)
    run_id = _run_id(run_id)
    if status not in ("dead", "abandoned"):
        raise ValueError("status must be dead or abandoned")
    if run_id in db.closed_runs:
        return db, MutationResult(False, "run_closed")
    updated = deepcopy(db)
    updated.closed_runs[run_id] = status
    return updated, MutationResult(True, "run_closed")
