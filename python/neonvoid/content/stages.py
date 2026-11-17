"""Declarative arena definitions and deterministic wave planning.

The original `void` stage's budget, spawn timings and RNG call order are
intentionally unchanged. Sector stage definitions are resolved lazily so that
the classic arena does not depend on the new OS campaign.
"""
from __future__ import annotations

from dataclasses import dataclass
import math
import random

from .enemies import E_PARAMS, SPAWN_COST


@dataclass(frozen=True)
class StageDefinition:
    id: str
    name: str
    boss_interval: int = 5
    base_budget: float = 8.0
    budget_per_wave: float = 4.0
    enemy_unlocks: tuple[tuple[int, str], ...] = (
        (1, "drifter"),
        (2, "shooter"),
        (3, "splitter"),
        (4, "rusher"),
    )
    boss_kind: str = "boss"
    boss_supports: tuple[str, ...] = ("drifter", "shooter")
    fallback_kind: str = "drifter"


STAGES: dict[str, StageDefinition] = {
    "void": StageDefinition(id="void", name="NEON VOID"),
}
DEFAULT_STAGE_ID = "void"


@dataclass(frozen=True)
class WavePlan:
    stage_id: str
    wave: int
    boss: bool
    banner: str
    banner_seconds: float
    audio_cue: str
    audio_gap: float
    spawns: tuple[tuple[float, str], ...]


def validate_stage(stage: StageDefinition) -> None:
    """Reject invalid definitions before they reach active gameplay."""
    if not stage.id or not stage.name:
        raise ValueError("Stage id and name are required")
    if type(stage.boss_interval) is not int or stage.boss_interval < 1:
        raise ValueError("boss_interval must be a positive integer")
    for value in (stage.base_budget, stage.budget_per_wave):
        if type(value) not in (int, float) or not math.isfinite(value) or value < 0:
            raise ValueError("Stage budgets must be finite and nonnegative")
    if stage.boss_kind not in E_PARAMS:
        raise ValueError("Unknown stage boss kind")
    if not stage.boss_supports or any(kind not in E_PARAMS for kind in stage.boss_supports):
        raise ValueError("Invalid boss support pool")
    if stage.fallback_kind not in SPAWN_COST:
        raise ValueError("Fallback enemy requires a spawn cost")
    if not stage.enemy_unlocks or stage.enemy_unlocks[0] != (1, stage.fallback_kind):
        raise ValueError("First unlock must be the fallback enemy at wave 1")
    previous = 0
    seen = set()
    for threshold, kind in stage.enemy_unlocks:
        if (type(threshold) is not int or threshold < 1 or threshold < previous
                or kind not in SPAWN_COST or kind in seen):
            raise ValueError("Invalid enemy unlock definition")
        previous = threshold
        seen.add(kind)


def get_stage(stage_id: str = DEFAULT_STAGE_ID) -> StageDefinition:
    if stage_id in STAGES:
        stage = STAGES[stage_id]
    else:
        # Import on demand to keep the original arena catalog standalone.
        from .sectors import get_sector
        try:
            stage = get_sector(stage_id).stage
        except ValueError as exc:
            raise ValueError(f"Unknown stage: {stage_id}") from exc
    validate_stage(stage)
    return stage


def plan_wave(wave: int, rng: random.Random,
              stage: StageDefinition | None = None) -> WavePlan:
    """Return an immutable wave plan, preserving legacy RNG decisions."""
    if type(wave) is not int or wave < 1:
        raise ValueError("Wave must be a positive integer")
    if stage is None:
        stage = get_stage()
    else:
        validate_stage(stage)
    boss = wave % stage.boss_interval == 0
    spawns = []
    if boss:
        spawns.append((0.7, stage.boss_kind))
        extra = 2 + wave // stage.boss_interval
        for index in range(extra):
            spawns.append((2.2 + index * 1.4, rng.choice(stage.boss_supports)))
        return WavePlan(stage.id, wave, True, f"WAVE {wave}  ·  BOSS",
                        2.4, "boss", 0.5, tuple(spawns))

    budget = stage.base_budget + wave * stage.budget_per_wave
    pool = [kind for threshold, kind in stage.enemy_unlocks if wave >= threshold]
    at = 0.4
    guard = 0
    while budget > 0 and guard < 200:
        guard += 1
        kind = rng.choice(pool)
        cost = SPAWN_COST[kind]
        if cost > budget and kind != stage.fallback_kind:
            kind = stage.fallback_kind
            cost = SPAWN_COST[kind]
        budget -= cost
        spawns.append((at, kind))
        at += max(0.22, 0.62 - wave * 0.015)
    return WavePlan(stage.id, wave, False, f"WAVE {wave}",
                    1.6, "wave", 0.9, tuple(spawns))
