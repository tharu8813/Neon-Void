"""Player infection and OS integrity pressure, independent from combat debuffs.

Infection never modifies movement, visibility, skill use, HP, or player input.
All values are clamped, time is simulated in seconds, and paused updates do
not advance either infection or integrity pressure.
"""
from __future__ import annotations

from dataclasses import dataclass
from enum import IntEnum
import math

MAX_INFECTION = 100.0
MAX_INTEGRITY = 100.0
DETECTED_AT = 25.0
SPREADING_AT = 50.0
CRITICAL_AT = 75.0
# OS integrity pressure per simulated second above each threshold.
SPREADING_PRESSURE = 0.30
CRITICAL_EXTRA_PRESSURE = 0.70


class InfectionStage(IntEnum):
    NORMAL = 0
    DETECTED = 1
    SPREADING = 2
    CRITICAL = 3


def _amount(value: float, name: str) -> float:
    if type(value) not in (int, float):
        raise ValueError(f"{name} must be finite and nonnegative")
    try:
        number = float(value)
    except OverflowError as exc:
        raise ValueError(f"{name} must be finite and nonnegative") from exc
    if not math.isfinite(number) or number < 0:
        raise ValueError(f"{name} must be finite and nonnegative")
    return number


def _bounded(value: float, maximum: float) -> float:
    return min(maximum, max(0.0, value))


def _seconds_above(start: float, end: float, threshold: float, dt: float) -> float:
    """Integrate threshold pressure under a constant linear infection rate."""
    if dt == 0:
        return 0.0
    if start >= threshold and end >= threshold:
        return dt
    if start <= threshold and end <= threshold:
        return 0.0
    if start == end:
        return dt if start >= threshold else 0.0
    crossing = max(0.0, min(dt, dt * (threshold - start) / (end - start)))
    return dt - crossing if end > start else crossing


@dataclass(frozen=True)
class InfectionTick:
    player_infection: float
    infection_stage: InfectionStage
    os_integrity: float
    integrity_lost: float
    needs_purification: bool


@dataclass
class InfectionState:
    player_infection: float = 0.0
    os_integrity: float = 100.0

    def __setattr__(self, name: str, value: float) -> None:
        """Enforce 0..100 even when an integration assigns a public field.

        The world/UI is allowed to read or restore these attributes, but a
        malformed save or a plugin must not poison a later tick or snapshot.
        """
        if name in ("player_infection", "os_integrity"):
            value = _bounded(_amount(value, name), MAX_INFECTION if name ==
                             "player_infection" else MAX_INTEGRITY)
        object.__setattr__(self, name, value)

    def __post_init__(self) -> None:
        self.player_infection = _bounded(_amount(self.player_infection, "player_infection"),
                                         MAX_INFECTION)
        self.os_integrity = _bounded(_amount(self.os_integrity, "os_integrity"),
                                     MAX_INTEGRITY)

    @property
    def infection(self) -> float:
        return self.player_infection

    @property
    def stage(self) -> InfectionStage:
        amount = self.player_infection
        if amount >= CRITICAL_AT:
            return InfectionStage.CRITICAL
        if amount >= SPREADING_AT:
            return InfectionStage.SPREADING
        if amount >= DETECTED_AT:
            return InfectionStage.DETECTED
        return InfectionStage.NORMAL

    @property
    def needs_purification(self) -> bool:
        return self.stage >= InfectionStage.SPREADING

    def apply_infection(self, amount: float, *, paused: bool = False) -> float:
        """Apply a confirmed infection hit; return the actual amount added."""
        amount = _amount(amount, "amount")
        if paused or amount == 0:
            return 0.0
        previous = self.player_infection
        self.player_infection = _bounded(previous + amount, MAX_INFECTION)
        return self.player_infection - previous

    def purify(self, amount: float, *, paused: bool = False) -> float:
        """Remove infection via an in-world purification station or reward."""
        amount = _amount(amount, "amount")
        if paused or amount == 0:
            return 0.0
        previous = self.player_infection
        self.player_infection = _bounded(previous - amount, MAX_INFECTION)
        return previous - self.player_infection

    def repair_integrity(self, amount: float, *, paused: bool = False) -> float:
        """Restore OS integrity without changing player combat statistics."""
        amount = _amount(amount, "amount")
        if paused or amount == 0:
            return 0.0
        previous = self.os_integrity
        self.os_integrity = _bounded(previous + amount, MAX_INTEGRITY)
        return self.os_integrity - previous

    def tick(self, dt: float, *, exposure_per_second: float = 0.0,
             purification_per_second: float = 0.0,
             paused: bool = False) -> InfectionTick:
        """Advance simulated time, integrating environmental exposure/pressure.

        During a fixed-rate interval, threshold-crossing time is computed
        analytically. Thus 60 x 1/60s and 1 x 1s give equivalent results.
        Integrity reaching zero is a world risk signal, NOT instant death.
        """
        dt = _amount(dt, "dt")
        exposure = _amount(exposure_per_second, "exposure_per_second")
        purification = _amount(purification_per_second, "purification_per_second")
        lost = 0.0
        if not paused and dt:
            start = self.player_infection
            end_unbounded = start + (exposure - purification) * dt
            high_time = _seconds_above(start, end_unbounded, SPREADING_AT, dt)
            critical_time = _seconds_above(start, end_unbounded, CRITICAL_AT, dt)
            lost = min(self.os_integrity, SPREADING_PRESSURE * high_time
                       + CRITICAL_EXTRA_PRESSURE * critical_time)
            self.player_infection = _bounded(end_unbounded, MAX_INFECTION)
            self.os_integrity = _bounded(self.os_integrity - lost, MAX_INTEGRITY)
        return InfectionTick(self.player_infection, self.stage, self.os_integrity,
                             lost, self.needs_purification)

    def snapshot(self) -> dict[str, float]:
        return {"player_infection": self.player_infection,
                "os_integrity": self.os_integrity}

    @classmethod
    def from_snapshot(cls, value: dict[str, float]) -> "InfectionState":
        if (not isinstance(value, dict)
                or "player_infection" not in value
                or "os_integrity" not in value):
            raise ValueError("Invalid infection snapshot")
        return cls(player_infection=value["player_infection"],
                   os_integrity=value["os_integrity"])

    def reset_for_new_os(self) -> None:
        """Start a fresh deployment, not a temporary sector transition."""
        self.player_infection = 0.0
        self.os_integrity = MAX_INTEGRITY
