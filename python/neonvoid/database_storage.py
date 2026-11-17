"""Dedicated atomic antivirus DB storage and transactional multiplayer-safe API.

Never reads/writes save.json, profile.json or settings.json.
"""
from __future__ import annotations

from contextlib import contextmanager
from dataclasses import replace
import json
import logging
import os
from pathlib import Path
import tempfile
import time
from typing import Callable, TypeVar

from .runtime_paths import data_directory
from .metaprogression import (
    AntivirusDatabase, database_from_dict, database_to_dict,
    grant_xp_once as _grant_xp_once,
    award_progress_once as _award_progress_once,
    close_run_once as _close_run_once,
)
from .extraction import commit_extraction_once as _commit_extraction_once

LOG = logging.getLogger(__name__)
DEFAULT_FILENAME = "antivirus_db.json"


class DatabaseCorruptError(ValueError):
    """Existing DB has invalid JSON, unsupported schema or inconsistent receipts."""


class DatabaseStorageError(OSError):
    """Cannot reliably read, lock or update the database."""


def database_path() -> Path:
    return Path(data_directory()) / DEFAULT_FILENAME


def _path(path: str | os.PathLike | None) -> Path:
    return Path(path) if path is not None else database_path()


def _read_existing(location: Path) -> AntivirusDatabase:
    try:
        with location.open("r", encoding="utf-8") as stream:
            return database_from_dict(json.load(stream))
    except (ValueError, TypeError, UnicodeError) as exc:
        raise DatabaseCorruptError(f"Invalid antivirus DB {location}: {exc}") from exc
    except OSError as exc:
        raise DatabaseStorageError(f"Cannot read antivirus DB {location}: {exc}") from exc


def load_database(path: str | os.PathLike | None = None) -> AntivirusDatabase:
    """Missing -> fresh in memory. Corrupt existing -> explicit error, no reset."""
    location = _path(path)
    try:
        return _read_existing(location)
    except DatabaseStorageError as exc:
        if isinstance(exc.__cause__, FileNotFoundError):
            return AntivirusDatabase()
        raise


def load_database_backup(path: str | os.PathLike | None = None) -> AntivirusDatabase:
    """Inspect LAST good previous snapshot manually; never silently restore it."""
    return _read_existing(Path(str(_path(path)) + ".bak"))


def _atomic_bytes(location: Path, data: bytes) -> None:
    temp_name = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="wb", dir=location.parent, prefix=".neon-antivirus-",
            suffix=".tmp", delete=False,
        ) as stream:
            temp_name = stream.name
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        for attempt in range(4):
            try:
                os.replace(temp_name, location)
                break
            except PermissionError as exc:
                # Another Windows process can briefly hold the file open.
                if getattr(exc, "winerror", None) not in (5, 32, 33) or attempt == 3:
                    raise
                time.sleep(0.01 * 2 ** attempt)
        temp_name = None
        if os.name != "nt":
            # fsync the containing directory when the platform permits it.
            try:
                fd = os.open(location.parent, os.O_RDONLY)
                try:
                    os.fsync(fd)
                finally:
                    os.close(fd)
            except OSError:
                pass
    finally:
        if temp_name is not None:
            try:
                os.unlink(temp_name)
            except FileNotFoundError:
                pass


def save_database(
    db: AntivirusDatabase, path: str | os.PathLike | None = None
) -> bool:
    """Validate and atomically replace; keep .bak of prior valid DB.

    Use AntivirusDatabaseStore for receipt-bearing operations. This standalone
    function cannot protect callers using stale state from another process.
    """
    location = _path(path)
    try:
        raw = json.dumps(database_to_dict(db), ensure_ascii=False, indent=2,
                         allow_nan=False, sort_keys=True).encode("utf-8") + b"\n"
        location.parent.mkdir(parents=True, exist_ok=True)
        if location.exists():
            # Never overwrite an already-corrupt DB with defaults.
            _read_existing(location)
            _atomic_bytes(Path(str(location) + ".bak"), location.read_bytes())
        _atomic_bytes(location, raw)
        return True
    except (OSError, ValueError, TypeError) as exc:
        LOG.warning("Cannot persist antivirus DB at %s: %s", location, exc)
        return False


@contextmanager
def _exclusive_lock(location: Path):
    """Cross-process advisory lock on stable sidecar file, Windows and POSIX."""
    lock_path = Path(str(location) + ".lock")
    try:
        lock_path.parent.mkdir(parents=True, exist_ok=True)
        stream = lock_path.open("a+b")
    except OSError as exc:
        raise DatabaseStorageError("Could not open antivirus DB lock") from exc
    with stream:
        try:
            if os.name == "nt":
                import msvcrt
                stream.seek(0)
                msvcrt.locking(stream.fileno(), msvcrt.LK_LOCK, 1)
            else:
                import fcntl
                fcntl.flock(stream.fileno(), fcntl.LOCK_EX)
        except OSError as exc:
            raise DatabaseStorageError("Could not acquire antivirus DB lock") from exc
        try:
            yield
        finally:
            if os.name == "nt":
                stream.seek(0)
                msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                fcntl.flock(stream.fileno(), fcntl.LOCK_UN)


_Result = TypeVar("_Result")


class AntivirusDatabaseStore:
    """Reload-under-lock -> pure state transition -> atomic save -> result.

    A success result is returned only AFTER the primary file was replaced.
    Retry the SAME run/event on failure: no duplicate receipts are possible.
    """

    def __init__(self, path: str | os.PathLike | None = None):
        self.path = _path(path)

    def load(self) -> AntivirusDatabase:
        return load_database(self.path)

    def _transaction(self, transition: Callable):
        with _exclusive_lock(self.path):
            before = load_database(self.path)
            after, outcome = transition(before)
            if not outcome.applied:
                return before, outcome
            if not save_database(after, self.path):
                return before, replace(outcome, applied=False, reason="save_failed")
            return after, outcome

    def grant_xp_once(self, *, run_id: str, event_id: str, amount: int):
        return self._transaction(
            lambda db: _grant_xp_once(db, run_id=run_id, event_id=event_id,
                                      amount=amount))

    def award_progress_once(
        self, *, run_id: str, event: str,
        sector_id: str | None = None, wave_index: int | None = None,
    ):
        return self._transaction(
            lambda db: _award_progress_once(db, run_id=run_id, event=event,
                                            sector_id=sector_id, wave_index=wave_index))

    def close_run_once(self, *, run_id: str, status: str):
        return self._transaction(
            lambda db: _close_run_once(db, run_id=run_id, status=status))

    def commit_extraction_once(
        self, *, run_id: str, clean_sector_ids,
        software_levels, selected_software_id: str,
        was_defeated: bool = False, abandoned: bool = False,
    ):
        return self._transaction(
            lambda db: _commit_extraction_once(
                db, run_id=run_id, clean_sector_ids=clean_sector_ids,
                software_levels=software_levels,
                selected_software_id=selected_software_id,
                was_defeated=was_defeated, abandoned=abandoned))
