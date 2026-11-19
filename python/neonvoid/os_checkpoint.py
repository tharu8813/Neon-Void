"""Validated, atomic OS checkpoint snapshots for a single active deployment.

A checkpoint is NOT a substitute for the authoritative antivirus_db.json XP /
extraction receipts. Active enemies/bullets are deliberately not serialized:
an unfinished wave restarts from its deterministic B-owned wave plan.
"""
from __future__ import annotations

from contextlib import contextmanager
import json
import math
import os
from pathlib import Path
import tempfile
import time

from .content.sectors import SectorCampaign, SECTOR_IDS
from .content.upgrades import UP_BY_ID
from .infection import InfectionState
from .metaprogression import MAX_ANTIVIRUS_LEVEL, base_stats_for_level
from .runtime_paths import data_directory
from .world.session import SECTOR_IDS as WORLD_SECTORS, WORLD_W, WORLD_H

SCHEMA_VERSION = 1
MAX_CHECKPOINT_BYTES = 128 * 1024
MAX_COUNTER = 1_000_000_000_000
MAX_REVISION = 1_000_000_000
EXPECTED_FIELDS = frozenset((
    "schema_version", "revision", "campaign", "scene", "sector",
    "hub_position", "player_position", "deployed_level", "player",
    "software_levels", "pending_cards", "infection", "purify_uses",
    "salvage_claimed", "score", "kills", "combo", "run_kills",
    "run_boss_kills", "run_best_combo", "elapsed_frames",
))
PLAYER_FIELDS = frozenset((
    "hp", "mana", "charges", "fire_cd", "pulse_cd", "guard_cd", "guard_t",
))
SCENES = frozenset(("hub", "play", "upgrade", "dead"))


class CheckpointCorruptError(ValueError):
    """An existing file is unsafe, invalid, oversized, or unsupported."""


class CheckpointConflictError(RuntimeError):
    """A second process changed or replaced the checkpoint after load."""


class CheckpointStorageError(OSError):
    """Checkpoint cannot be reliably read, written, or archived."""


def checkpoint_path() -> Path:
    return Path(data_directory()) / "os_checkpoint.json"


def _number(value, name, low, high):
    if type(value) not in (int, float):
        raise ValueError(f"{name} must be a finite number")
    try:
        value = float(value)
    except (OverflowError, ValueError) as exc:
        raise ValueError(f"{name} must be finite") from exc
    if not math.isfinite(value) or not low <= value <= high:
        raise ValueError(f"{name} outside supported range")
    return value


def _integer(value, name, low, high):
    if type(value) is not int or not low <= value <= high:
        raise ValueError(f"{name} must be an integer between {low} and {high}")
    return value


def _position(value, name, xmax, ymax):
    if (not isinstance(value, list) or len(value) != 2):
        raise ValueError(f"{name} must be a two-number position")
    _number(value[0], f"{name}[0]", 0, xmax)
    _number(value[1], f"{name}[1]", 0, ymax)


def validate_checkpoint(data: object) -> dict:
    """Validate entire schema and internal invariants before modifying Game.

    Reject unknown fields, booleans disguised as numbers, incompatible versions,
    impossible software stacks, invalid wave transitions, and invalid positions.
    """
    if not isinstance(data, dict) or set(data) != EXPECTED_FIELDS:
        raise ValueError("Checkpoint has missing or unexpected fields")
    if _integer(data["schema_version"], "schema_version", 1, 1) != SCHEMA_VERSION:
        raise ValueError("Unsupported checkpoint version")
    _integer(data["revision"], "revision", 1, MAX_REVISION)
    if data["scene"] not in SCENES:
        raise ValueError("Invalid checkpoint scene")
    if data["sector"] is not None and data["sector"] not in WORLD_SECTORS:
        raise ValueError("Invalid checkpoint sector")
    if (data["scene"] == "hub") != (data["sector"] is None):
        raise ValueError("Hub must not have a sector and combat must have one")
    _position(data["hub_position"], "hub_position", WORLD_W, WORLD_H)
    _position(data["player_position"], "player_position",
              WORLD_W if data["scene"] == "hub" else 1280,
              WORLD_H if data["scene"] == "hub" else 720)

    campaign = SectorCampaign.from_checkpoint(data["campaign"])
    if campaign.os_clean and data["scene"] == "upgrade":
        raise ValueError("CLEAN operating system cannot await an upgrade")
    sector = data["sector"]
    waves = campaign.checkpoint()["completed_waves"]
    if sector is not None:
        completed = waves[sector.upper()]
        if data["scene"] == "upgrade" and not 1 <= completed < 5:
            raise ValueError("Only a completed non-final wave can offer an upgrade")

    level = _integer(data["deployed_level"], "deployed_level",
                     1, MAX_ANTIVIRUS_LEVEL)
    if not isinstance(data["software_levels"], dict):
        raise ValueError("software_levels must be an object")
    for uid, count in data["software_levels"].items():
        if uid not in UP_BY_ID:
            raise ValueError("Unknown software in checkpoint")
        _integer(count, f"software[{uid}]", 1, UP_BY_ID[uid][4])

    cards = data["pending_cards"]
    if not isinstance(cards, list) or len(cards) > 3:
        raise ValueError("Invalid pending software choices")
    if data["scene"] == "upgrade" and len(cards) != 3:
        raise ValueError("Upgrade selection needs three choices")
    if data["scene"] != "upgrade" and cards:
        raise ValueError("Non-upgrade checkpoint cannot contain cards")
    for uid in cards:
        if type(uid) is not str or (uid not in UP_BY_ID and uid != "bonus"):
            raise ValueError("Invalid upgrade choice")

    player = data["player"]
    if not isinstance(player, dict) or set(player) != PLAYER_FIELDS:
        raise ValueError("Invalid saved player attributes")
    stats = base_stats_for_level(level)
    maxhp = stats["maxhp"] + 20 * data["software_levels"].get("hull", 0)
    maxmana = stats["maxmana"] + 25 * data["software_levels"].get("mana", 0)
    _number(player["hp"], "hp", 0 if data["scene"] == "dead" else 0.001, maxhp)
    _number(player["mana"], "mana", 0, maxmana)
    max_charges = 1 + data["software_levels"].get("dash2", 0)
    _integer(player["charges"], "dash charges", 0, max_charges)
    for key in ("fire_cd", "pulse_cd", "guard_cd", "guard_t"):
        _number(player[key], key, 0, 3600)

    if not isinstance(data["infection"], dict) or set(data["infection"]) != {
            "player_infection", "os_integrity"}:
        raise ValueError("Invalid infection payload")
    for key in ("player_infection", "os_integrity"):
        _number(data["infection"][key], key, 0, 100)
    InfectionState.from_snapshot(data["infection"])
    _integer(data["purify_uses"], "purify_uses", 0, 3)
    if type(data["salvage_claimed"]) is not bool:
        raise ValueError("salvage_claimed must be a boolean")
    for key in ("score", "kills", "combo", "run_kills", "run_boss_kills",
                "run_best_combo", "elapsed_frames"):
        _integer(data[key], key, 0, MAX_COUNTER)
    if data["run_kills"] > data["kills"]:
        raise ValueError("Run kills exceed actual combat kills")
    return data


def _read(location: Path) -> dict | None:
    try:
        with location.open("rb") as stream:
            raw = stream.read(MAX_CHECKPOINT_BYTES + 1)
        if len(raw) > MAX_CHECKPOINT_BYTES:
            raise CheckpointCorruptError("Checkpoint exceeds 128 KiB limit")
        return validate_checkpoint(json.loads(raw.decode("utf-8")))
    except FileNotFoundError:
        return None
    except (ValueError, TypeError, UnicodeError) as exc:
        raise CheckpointCorruptError(f"Invalid checkpoint at {location}: {exc}") from exc
    except OSError as exc:
        raise CheckpointStorageError(f"Cannot read checkpoint at {location}: {exc}") from exc


def _replace(location: Path, payload: bytes):
    location.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(
            dir=location.parent, prefix=".neon-checkpoint-", suffix=".tmp",
            delete=False,
        ) as stream:
            temporary = stream.name
            stream.write(payload)
            stream.flush()
            os.fsync(stream.fileno())
        for attempt in range(4):
            try:
                os.replace(temporary, location)
                break
            except PermissionError as exc:
                if getattr(exc, "winerror", None) not in (5, 32, 33) or attempt == 3:
                    raise
                time.sleep(0.01 * (2 ** attempt))
        temporary = None
        if os.name != "nt":
            try:
                fd = os.open(location.parent, os.O_RDONLY)
                try:
                    os.fsync(fd)
                finally:
                    os.close(fd)
            except OSError:
                pass
    finally:
        if temporary is not None:
            try:
                os.unlink(temporary)
            except FileNotFoundError:
                pass


@contextmanager
def _file_lock(location: Path):
    location.parent.mkdir(parents=True, exist_ok=True)
    with Path(str(location) + ".lock").open("a+b") as stream:
        if os.name == "nt":
            import msvcrt
            stream.seek(0)
            msvcrt.locking(stream.fileno(), msvcrt.LK_LOCK, 1)
        else:
            import fcntl
            fcntl.flock(stream.fileno(), fcntl.LOCK_EX)
        try:
            yield
        finally:
            if os.name == "nt":
                stream.seek(0)
                msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                fcntl.flock(stream.fileno(), fcntl.LOCK_UN)


class CheckpointStore:
    """Revision-checked writes avoid two game processes corrupting one run."""

    def __init__(self, path: str | os.PathLike | None = None):
        self.path = Path(path) if path is not None else checkpoint_path()

    def load(self) -> dict | None:
        return _read(self.path)

    def save(self, snapshot: dict, *, expected_revision: int | None) -> int | None:
        if expected_revision is not None:
            _integer(expected_revision, "expected_revision", 1, MAX_REVISION - 1)
        data = dict(snapshot)
        data["schema_version"] = SCHEMA_VERSION
        data["revision"] = (expected_revision or 0) + 1
        validate_checkpoint(data)
        raw = (json.dumps(data, ensure_ascii=False, sort_keys=True,
                          allow_nan=False, separators=(",", ":")).encode("utf-8") + b"\n")
        if len(raw) > MAX_CHECKPOINT_BYTES:
            raise ValueError("Checkpoint too large")
        try:
            with _file_lock(self.path):
                current = self.load()
                actual = current["revision"] if current is not None else None
                if actual != expected_revision:
                    raise CheckpointConflictError("Checkpoint revision was changed externally")
                if current is not None and current["campaign"]["run_id"] != data["campaign"]["run_id"]:
                    raise CheckpointConflictError("Checkpoint belongs to another OS")
                if current is not None:
                    previous = self.path.read_bytes()
                    _replace(Path(str(self.path) + ".bak"), previous)
                _replace(self.path, raw)
            return data["revision"]
        except OSError:
            return None

    def clear(self, *, expected_revision: int | None = None) -> bool:
        try:
            with _file_lock(self.path):
                current = self.load()
                if current is None:
                    return True
                if expected_revision is not None and current["revision"] != expected_revision:
                    raise CheckpointConflictError("Cannot delete another process's checkpoint")
                self.path.unlink()
            return True
        except OSError:
            return False

    def archive_corrupt(self) -> Path | None:
        """Keep damaged bytes for manual recovery; do not overwrite silently."""
        try:
            with _file_lock(self.path):
                if not self.path.exists():
                    return None
                destination = Path(str(self.path) + ".corrupt")
                index = 2
                while destination.exists():
                    destination = Path(str(self.path) + f".corrupt.{index}")
                    index += 1
                self.path.replace(destination)
                return destination
        except OSError as exc:
            raise CheckpointStorageError(f"Cannot preserve corrupt checkpoint: {exc}") from exc
