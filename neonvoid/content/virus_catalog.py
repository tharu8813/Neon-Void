"""Bounded, simulation-time virus attack patterns for sector integration.

This is a pure action planner. The A/world owner consumes actions and creates
actual bullets, charges, shields and infection collisions in the game loop.
No pattern changes player controls, aim, visibility, speed or skill access.
"""
from __future__ import annotations

from dataclasses import dataclass
import math

MAX_LIVE_ENEMIES = 64
MAX_REPLICATIONS_PER_WAVE = 24
MAX_ACTIONS_PER_STEP = 6
MAX_RUNTIME_SOURCES = 128


@dataclass(frozen=True)
class VirusPattern:
    enemy_kind: str
    behavior: str
    cooldown: float
    power: float
    projectile_count: int = 0
    armor_pen: float = 0.0
    infection_on_hit: float = 0.0
    spawn_kind: str | None = None
    spawn_per_cast: int = 0
    spawn_limit_per_source: int = 0
    telegraph_seconds: float = 0.0


PATTERNS: dict[str, VirusPattern] = {
    "overheater": VirusPattern("overheater", "telegraphed_charge", 2.4, 190,
                               infection_on_hit=2, telegraph_seconds=0.45),
    "worm": VirusPattern("worm", "replicate", 3.2, 1,
                         infection_on_hit=3, spawn_kind="worm",
                         spawn_per_cast=1, spawn_limit_per_source=2),
    "trojan": VirusPattern("trojan", "feint_volley", 2.8, 300,
                           projectile_count=3, infection_on_hit=4,
                           telegraph_seconds=0.55),
    "ransomware": VirusPattern("ransomware", "encryption_pulse", 3.8, 7,
                               infection_on_hit=7, telegraph_seconds=0.8),
    "rootkit": VirusPattern("rootkit", "armor_piercing_volley", 3.0, 320,
                            projectile_count=4, armor_pen=12,
                            infection_on_hit=5, telegraph_seconds=0.35),
    "packet_swarm": VirusPattern("packet_swarm", "quick_volley", 2.2, 340,
                                 projectile_count=2, infection_on_hit=2),
    "guardian": VirusPattern("guardian", "armor_pulse", 4.2, 12,
                             infection_on_hit=2, telegraph_seconds=0.6),
    "overseer": VirusPattern("overseer", "overload_barrage", 2.5, 350,
                             projectile_count=10, infection_on_hit=5,
                             telegraph_seconds=0.5),
    "replicator": VirusPattern("replicator", "replicate", 3.1, 1,
                               infection_on_hit=6, spawn_kind="worm",
                               spawn_per_cast=2, spawn_limit_per_source=10),
    "encryptor": VirusPattern("encryptor", "encryption_pulse", 3.0, 13,
                              infection_on_hit=9, telegraph_seconds=0.7),
    "zero_day": VirusPattern("zero_day", "breach_barrage", 2.0, 405,
                             projectile_count=12, armor_pen=16,
                             infection_on_hit=9, telegraph_seconds=0.35),
}


@dataclass(frozen=True)
class VirusAction:
    source_id: str
    enemy_kind: str
    behavior: str
    phase: int
    count: int = 0
    power: float = 0.0
    projectile_count: int = 0
    armor_pen: float = 0.0
    infection_on_hit: float = 0.0
    spawn_kind: str | None = None
    telegraph_seconds: float = 0.0


@dataclass
class VirusState:
    enemy_kind: str
    elapsed: float = 0.0
    spawned_total: int = 0


def get_virus_pattern(enemy_kind: str) -> VirusPattern:
    try:
        return PATTERNS[enemy_kind]
    except KeyError as exc:
        raise ValueError(f"Unknown virus: {enemy_kind}") from exc


def infection_for_hit(enemy_kind: str) -> float:
    """Infection to apply after a *confirmed* contact or projectile hit."""
    return get_virus_pattern(enemy_kind).infection_on_hit


def boss_phase(hp_fraction: float) -> int:
    if not math.isfinite(hp_fraction) or not 0.0 <= hp_fraction <= 1.0:
        raise ValueError("hp_fraction must be within [0, 1]")
    return 1 if hp_fraction > 2 / 3 else (2 if hp_fraction > 1 / 3 else 3)


class VirusRuntime:
    """Deterministic per-enemy cadence with strict replication safety budgets.

    Call reset_wave() at the start of each encounter; forget() after a kill.
    The runtime emits requests, not side effects. Only honor spawn requests
    after verifying the current enemy cap in the game-world owner.
    """

    def __init__(self):
        self.states: dict[str, VirusState] = {}
        self.total_replications = 0

    def reset_wave(self) -> None:
        self.states.clear()
        self.total_replications = 0

    def forget(self, source_id: str) -> None:
        self.states.pop(source_id, None)

    def step(self, source_id: str, enemy_kind: str, dt: float, *,
             active_enemy_count: int, hp_fraction: float = 1.0,
             paused: bool = False) -> tuple[VirusAction, ...]:
        if not isinstance(source_id, str) or not source_id:
            raise ValueError("source_id must be a nonempty string")
        if not math.isfinite(dt) or dt < 0:
            raise ValueError("dt must be finite and nonnegative")
        if type(active_enemy_count) is not int or active_enemy_count < 0:
            raise ValueError("active_enemy_count must be nonnegative")
        pattern = get_virus_pattern(enemy_kind)
        phase = boss_phase(hp_fraction)
        if paused or dt == 0.0:
            return ()
        if source_id not in self.states:
            if len(self.states) >= MAX_RUNTIME_SOURCES:
                return ()
            self.states[source_id] = VirusState(enemy_kind)
        state = self.states[source_id]
        if state.enemy_kind != enemy_kind:
            raise ValueError("source_id cannot change virus kind")
        # Faster attacks in boss phases 2 and 3; normal enemies stay phase 1.
        is_boss = enemy_kind in {"overseer", "replicator", "encryptor", "zero_day"}
        interval = pattern.cooldown / (1 + (phase - 1) * 0.2 if is_boss else 1)
        state.elapsed += dt
        actions = []
        living = active_enemy_count
        while state.elapsed >= interval and len(actions) < MAX_ACTIONS_PER_STEP:
            state.elapsed -= interval
            count = 0
            if pattern.spawn_kind is not None:
                count = min(pattern.spawn_per_cast,
                            pattern.spawn_limit_per_source - state.spawned_total,
                            MAX_REPLICATIONS_PER_WAVE - self.total_replications,
                            MAX_LIVE_ENEMIES - living)
                if count <= 0:
                    continue
                state.spawned_total += count
                self.total_replications += count
                living += count
            actions.append(VirusAction(
                source_id, enemy_kind, pattern.behavior,
                phase if is_boss else 1, count=count,
                power=pattern.power,
                projectile_count=pattern.projectile_count
                                 + (phase - 1) * 2 if is_boss else pattern.projectile_count,
                armor_pen=pattern.armor_pen,
                infection_on_hit=pattern.infection_on_hit,
                spawn_kind=pattern.spawn_kind,
                telegraph_seconds=pattern.telegraph_seconds))
        if state.elapsed >= interval:
            # Drop pathological dt backlogs rather than firing dozens of
            # replicated projectiles or performing unbounded loops.
            state.elapsed %= interval
        return tuple(actions)
