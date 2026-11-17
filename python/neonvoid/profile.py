"""Versioned player run history, independent of the original save.json file."""

from __future__ import annotations

from dataclasses import asdict, dataclass, field
import json
import logging
import math
import os
import tempfile
from typing import Callable

from .constants import PROFILE_FILE
from .events import EventBus, GameEvent
from .storage import quarantine_corrupt

SCHEMA_VERSION = 2
MAX_RECENT_RUNS = 20
LOG = logging.getLogger(__name__)


def _nonnegative_int(value: object, label: str) -> int:
    if type(value) is not int or value < 0:
        raise ValueError(f"{label} must be a nonnegative integer")
    return value


def _duration(value: object) -> float:
    if type(value) not in (int, float) or not math.isfinite(value) or value < 0:
        raise ValueError("duration_seconds must be finite and nonnegative")
    return round(float(value), 3)


@dataclass
class RunRecord:
    status: str = "dead"
    score: int = 0
    wave: int = 0
    kills: int = 0
    boss_kills: int = 0
    best_combo: int = 0
    duration_seconds: float = 0.0


@dataclass
class PlayerProfile:
    total_kills: int = 0
    boss_kills: int = 0
    total_score: int = 0
    completed_runs: int = 0
    abandoned_runs: int = 0
    extracted_runs: int = 0
    best_combo: int = 0
    recent_runs: list[RunRecord] = field(default_factory=list)

    def record_run(self, run: RunRecord) -> None:
        if run.status not in ("dead", "abandoned", "extracted"):
            raise ValueError("Unsupported run status")
        self.total_kills += run.kills
        self.boss_kills += run.boss_kills
        self.total_score += run.score
        self.best_combo = max(self.best_combo, run.best_combo)
        if run.status == "dead":
            self.completed_runs += 1
        elif run.status == "abandoned":
            self.abandoned_runs += 1
        else:
            self.extracted_runs += 1
        self.recent_runs.insert(0, run)
        del self.recent_runs[MAX_RECENT_RUNS:]


def _parse_run(value: object) -> RunRecord:
    if not isinstance(value, dict):
        raise ValueError("Recent run must be an object")
    status = value.get("status", "dead")
    if status not in ("dead", "abandoned", "extracted"):
        raise ValueError("Unknown run status")
    keys = ("score", "wave", "kills", "boss_kills", "best_combo")
    data = {key: _nonnegative_int(value.get(key, 0), key) for key in keys}
    return RunRecord(status=status,
                     duration_seconds=_duration(value.get("duration_seconds", 0.0)),
                     **data)


def _parse_profile(value: object) -> PlayerProfile:
    if not isinstance(value, dict):
        raise ValueError("Profile must be a JSON object")
    version = value.get("schema_version")
    if type(version) is not int or version not in (1, SCHEMA_VERSION):
        raise ValueError(f"Unsupported profile schema version: {version!r}")
    keys = ("total_kills", "boss_kills", "total_score",
            "completed_runs", "abandoned_runs", "best_combo",
            "extracted_runs")
    data = {key: _nonnegative_int(value.get(key, 0), key) for key in keys}
    runs = value.get("recent_runs", [])
    if not isinstance(runs, list):
        raise ValueError("recent_runs must be a list")
    if len(runs) > MAX_RECENT_RUNS:
        raise ValueError("recent_runs exceeds maximum length")
    return PlayerProfile(recent_runs=[_parse_run(run) for run in runs], **data)


def load_profile(path: str | os.PathLike | None = None) -> PlayerProfile:
    """Missing/corrupt profiles load as an empty profile without touching save.json."""
    location = os.fspath(path) if path is not None else PROFILE_FILE
    try:
        with open(location, "r", encoding="utf-8") as stream:
            return _parse_profile(json.load(stream))
    except FileNotFoundError:
        return PlayerProfile()
    except (OSError, ValueError, TypeError, OverflowError) as exc:
        LOG.warning("Player profile load failed (%s): %s", location, exc)
        # 손상된 원본은 그대로 두지 않고 옆으로 옮겨 보존한다.
        quarantine_corrupt(location)
        return PlayerProfile()


def save_profile(profile: PlayerProfile, path: str | os.PathLike | None = None) -> bool:
    """Write the validated player profile atomically, returning whether it persisted."""
    location = os.fspath(path) if path is not None else PROFILE_FILE
    tmp = None
    try:
        obj = {"schema_version": SCHEMA_VERSION, **asdict(profile)}
        _parse_profile(obj)
        with tempfile.NamedTemporaryFile(
            mode="w", encoding="utf-8", dir=os.path.dirname(os.path.abspath(location)),
            prefix=".neon-profile-", suffix=".tmp", delete=False,
        ) as stream:
            tmp = stream.name
            json.dump(obj, stream, ensure_ascii=False, indent=2, allow_nan=False)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(tmp, location)
        return True
    except (OSError, ValueError, TypeError) as exc:
        LOG.warning("Player profile save failed (%s): %s", location, exc)
        return False
    finally:
        if tmp is not None and os.path.exists(tmp):
            try:
                os.unlink(tmp)
            except OSError:
                pass


class ProfileTracker:
    """Collect lifecycle events into a bounded profile without touching game physics."""

    def __init__(self, events: EventBus, profile: PlayerProfile,
                 persist: Callable[[PlayerProfile], bool] | None = None):
        self.profile = profile
        self.persist = persist
        self.active = False
        self.run_kills = 0
        self.run_boss_kills = 0
        self.run_best_combo = 0
        for name in ("run_started", "enemy_killed", "run_ended",
                     "run_abandoned", "run_extracted"):
            events.subscribe(name, self.handle)

    def handle(self, event: GameEvent) -> None:
        if event.name == "run_started":
            self.active = True
            self.run_kills = 0
            self.run_boss_kills = 0
            self.run_best_combo = 0
        elif event.name == "enemy_killed" and self.active:
            self.run_kills += 1
            if event.payload.get("kind") == "boss" or event.payload.get("boss") is True:
                self.run_boss_kills += 1
            self.run_best_combo = max(self.run_best_combo,
                                      int(event.payload.get("combo", 0)))
        elif event.name in ("run_ended", "run_abandoned", "run_extracted") and self.active:
            self.active = False
            duration_frames = max(0, int(event.payload.get("duration_frames", 0)))
            record = RunRecord(
                status={"run_ended": "dead", "run_abandoned": "abandoned",
                        "run_extracted": "extracted"}[event.name],
                score=max(0, int(event.payload.get("score", 0))),
                wave=max(0, int(event.payload.get("wave", 0))),
                kills=self.run_kills, boss_kills=self.run_boss_kills,
                best_combo=self.run_best_combo,
                duration_seconds=round(duration_frames / 60.0, 3),
            )
            self.profile.record_run(record)
            if self.persist is not None:
                self.persist(self.profile)
