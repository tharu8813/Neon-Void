"""Four independently replayable OS sectors and headless campaign progression.

Sector content is descriptive; the world owner renders gates, enemies and
terminal interactions. This module does not mutate Game or award XP.
"""
from __future__ import annotations

from dataclasses import dataclass, field
import hashlib
import random

from .stages import StageDefinition, WavePlan, plan_wave, validate_stage
from ..events import EventBus

SECTOR_IDS = ("CPU", "MEMORY", "STORAGE", "NETWORK")
FINAL_WAVE = 5


@dataclass(frozen=True)
class WaveDefinition:
    wave_index: int
    objective: str
    feature: str
    danger: int
    exposure_per_second: float = 0.0
    purify_on_clear: float = 0.0


@dataclass(frozen=True)
class SectorDefinition:
    sector_id: str
    name: str
    danger: int
    feature: str
    entry_gate: str
    exit_gate: str
    stage: StageDefinition
    waves: tuple[WaveDefinition, ...]
    unlocked: bool = True
    required: bool = True
    clear_condition: str = "five_waves_and_boss"

    @property
    def boss_kind(self) -> str:
        return self.stage.boss_kind


@dataclass(frozen=True)
class SectorEncounter:
    """A wave plan with explicit world/UI integration metadata."""
    sector_id: str
    wave_index: int
    boss_kind: str | None
    clear_condition: str
    definition: WaveDefinition
    wave: WavePlan

    @property
    def spawns(self) -> tuple[tuple[float, str], ...]:
        return self.wave.spawns


def _waves(features: tuple[str, ...], *, danger: int,
           exposure: float, purge: float) -> tuple[WaveDefinition, ...]:
    return tuple(
        WaveDefinition(index, "defeat_boss_and_hostiles" if index == FINAL_WAVE
                       else "eliminate_hostiles", feature, min(5, danger + (index - 1) // 2),
                       round(exposure * (1 + 0.22 * (index - 1)), 3), purge)
        for index, feature in enumerate(features, start=1)
    )


SECTORS: dict[str, SectorDefinition] = {
    "CPU": SectorDefinition(
        "CPU", "CPU CORE", 2, "Overload and fast melee pressure",
        "gate_cpu", "exit_cpu",
        StageDefinition("CPU", "CPU CORE", base_budget=9.0, budget_per_wave=3.5,
                        enemy_unlocks=((1, "overheater"), (2, "rusher"),
                                       (3, "trojan"), (4, "packet_swarm")),
                        fallback_kind="overheater", boss_kind="overseer",
                        boss_supports=("overheater", "rusher")),
        _waves(("Heat leak", "Clock surge", "Thermal throttle",
                "Overclock cascade", "OVERSEER control core"),
               danger=2, exposure=0.30, purge=7.0),
    ),
    "MEMORY": SectorDefinition(
        "MEMORY", "MEMORY SECTOR", 3, "Limited worm replication",
        "gate_memory", "exit_memory",
        StageDefinition("MEMORY", "MEMORY SECTOR", base_budget=8.0, budget_per_wave=3.0,
                        enemy_unlocks=((1, "worm"), (2, "splitter"),
                                       (3, "trojan"), (4, "rootkit")),
                        fallback_kind="worm", boss_kind="replicator",
                        boss_supports=("worm", "trojan")),
        _waves(("Memory leak", "Parasite threads", "Fork bomb",
                "Page corruption", "REPLICATOR hive"),
               danger=3, exposure=0.45, purge=9.0),
    ),
    "STORAGE": SectorDefinition(
        "STORAGE", "STORAGE VAULT", 3, "Encryption and armored defenders",
        "gate_storage", "exit_storage",
        StageDefinition("STORAGE", "STORAGE VAULT", base_budget=7.0, budget_per_wave=3.0,
                        enemy_unlocks=((1, "ransomware"), (2, "guardian"),
                                       (3, "rootkit"), (4, "trojan")),
                        fallback_kind="ransomware", boss_kind="encryptor",
                        boss_supports=("ransomware", "guardian")),
        _waves(("File lock", "Integrity scan", "Vault lockdown",
                "Encryption storm", "ENCRYPTOR keykeeper"),
               danger=3, exposure=0.40, purge=10.0),
    ),
    "NETWORK": SectorDefinition(
        "NETWORK", "NETWORK GRID", 5, "Mixed swarms, projectiles and intrusion",
        "gate_network", "exit_network",
        StageDefinition("NETWORK", "NETWORK GRID", base_budget=10.0, budget_per_wave=4.2,
                        enemy_unlocks=((1, "packet_swarm"), (2, "trojan"),
                                       (3, "rootkit"), (4, "worm"),
                                       (4, "overheater")),
                        fallback_kind="packet_swarm", boss_kind="zero_day",
                        boss_supports=("packet_swarm", "rootkit", "trojan")),
        _waves(("Packet flood", "Man-in-the-middle", "Remote execution",
                "Botnet convergence", "ZERO-DAY outbreak"),
               danger=4, exposure=0.65, purge=12.0),
    ),
}


def get_sector(sector_id: str) -> SectorDefinition:
    """Resolve the stable upper-case sector ID; accept lowercase for callers."""
    if not isinstance(sector_id, str):
        raise ValueError("sector_id must be a sector name")
    key = sector_id.upper()
    try:
        sector = SECTORS[key]
    except KeyError as exc:
        raise ValueError(f"Unknown sector: {sector_id}") from exc
    validate_stage(sector.stage)
    return sector


def plan_sector_wave(sector_id: str, wave_index: int, *,
                     seed: int = 0) -> SectorEncounter:
    """Plan a wave independently of visitation order or previous RNG calls.

    The same (sector, wave, seed) yields the same immutable spawn schedule,
    including when the wave is retried after leaving its sector.
    """
    sector = get_sector(sector_id)
    if type(wave_index) is not int or not 1 <= wave_index <= FINAL_WAVE:
        raise ValueError("Sector wave_index must be between 1 and 5")
    if type(seed) is not int:
        raise ValueError("seed must be an integer")
    stable_bytes = hashlib.sha256(
        f"{seed}:{sector.sector_id}:{wave_index}".encode("ascii")).digest()
    rng = random.Random(int.from_bytes(stable_bytes[:8], "big"))
    wave = plan_wave(wave_index, rng, sector.stage)
    definition = sector.waves[wave_index - 1]
    return SectorEncounter(
        sector.sector_id, wave_index,
        sector.boss_kind if wave.boss else None,
        definition.objective, definition, wave)


@dataclass
class SectorProgress:
    completed_waves: int = 0
    active_wave: int | None = None

    @property
    def is_clean(self) -> bool:
        return self.completed_waves == FINAL_WAVE


class SectorCampaign:
    """Four independent progress tracks for one OS instance.

    Successful wave completion advances once. Leaving a sector discards only
    its in-progress wave. XP/reward idempotency belongs to metaprogression.
    """

    def __init__(self, run_id: str, *, seed: int = 0,
                 events: EventBus | None = None):
        if not isinstance(run_id, str) or not run_id:
            raise ValueError("run_id is required")
        if type(seed) is not int:
            raise ValueError("seed must be an integer")
        self.run_id = run_id
        self.seed = seed
        self.events = events if events is not None else EventBus()
        self.progress: dict[str, SectorProgress] = {
            sector_id: SectorProgress() for sector_id in SECTOR_IDS}
        self.active_sector: str | None = None
        self._os_clean_emitted = False

    @property
    def os_clean(self) -> bool:
        return all(self.progress[sector_id].is_clean for sector_id in SECTOR_IDS)

    def enter_sector(self, sector_id: str) -> SectorProgress:
        sector = get_sector(sector_id)
        if not sector.unlocked:
            raise ValueError(f"Sector locked: {sector.sector_id}")
        if self.active_sector is not None and self.active_sector != sector.sector_id:
            self.leave_sector()
        self.active_sector = sector.sector_id
        self.events.emit("sector_entered", run_id=self.run_id,
                         sector_id=sector.sector_id,
                         completed_waves=self.progress[sector.sector_id].completed_waves)
        return self.progress[sector.sector_id]

    def start_wave(self) -> SectorEncounter:
        if self.active_sector is None:
            raise ValueError("Enter a sector before starting a wave")
        state = self.progress[self.active_sector]
        if state.is_clean or state.active_wave is not None:
            raise ValueError("No new wave is available")
        wave_index = state.completed_waves + 1
        encounter = plan_sector_wave(self.active_sector, wave_index, seed=self.seed)
        state.active_wave = wave_index
        self.events.emit("sector_wave_started", run_id=self.run_id,
                         sector_id=self.active_sector, wave_index=wave_index,
                         boss_kind=encounter.boss_kind)
        return encounter

    def clear_wave(self, *, hostiles_remaining: int,
                   boss_defeated: bool = False) -> bool:
        """Accept an A-owned kill verification, returning True only on completion.

        Boss defeat and zero remaining enemies are both required for wave 5.
        A premature/duplicate call does not award or advance anything.
        """
        if self.active_sector is None:
            raise ValueError("No active sector")
        state = self.progress[self.active_sector]
        if state.active_wave is None:
            return False
        if type(hostiles_remaining) is not int or hostiles_remaining < 0:
            raise ValueError("hostiles_remaining must be a nonnegative integer")
        if hostiles_remaining != 0 or (state.active_wave == FINAL_WAVE
                                        and not boss_defeated):
            return False
        wave_index = state.active_wave
        sector_id = self.active_sector
        state.active_wave = None
        state.completed_waves = wave_index
        self.events.emit("wave_cleared", run_id=self.run_id,
                         sector_id=sector_id, wave_index=wave_index,
                         boss_kind=get_sector(sector_id).boss_kind if wave_index == FINAL_WAVE else None,
                         purify_on_clear=get_sector(sector_id).waves[wave_index - 1].purify_on_clear)
        if state.is_clean:
            self.events.emit("sector_cleared", run_id=self.run_id,
                             sector_id=sector_id, boss_kind=get_sector(sector_id).boss_kind)
        if self.os_clean and not self._os_clean_emitted:
            self._os_clean_emitted = True
            self.events.emit("os_cleaned", run_id=self.run_id)
        return True

    def leave_sector(self) -> None:
        if self.active_sector is None:
            return
        sector_id = self.active_sector
        self.progress[sector_id].active_wave = None
        self.active_sector = None
        self.events.emit("sector_left", run_id=self.run_id, sector_id=sector_id,
                         completed_waves=self.progress[sector_id].completed_waves)

    def checkpoint(self) -> dict[str, object]:
        """Checkpoint only confirmed waves; an active wave must be retried."""
        return {"run_id": self.run_id, "seed": self.seed,
                "completed_waves": {sid: p.completed_waves
                                    for sid, p in self.progress.items()}}

    @classmethod
    def from_checkpoint(cls, data: dict[str, object],
                        *, events: EventBus | None = None) -> "SectorCampaign":
        if (not isinstance(data, dict)
                or not isinstance(data.get("run_id"), str)
                or not data["run_id"]
                or type(data.get("seed")) is not int
                or not isinstance(data.get("completed_waves"), dict)):
            raise ValueError("Invalid sector checkpoint")
        campaign = cls(data["run_id"], seed=data["seed"], events=events)
        completed = data["completed_waves"]
        if set(completed) != set(SECTOR_IDS):
            raise ValueError("Missing/unknown sector in checkpoint")
        for sid in SECTOR_IDS:
            value = completed[sid]
            if type(value) is not int or not 0 <= value <= FINAL_WAVE:
                raise ValueError("Invalid completed wave count")
            campaign.progress[sid].completed_waves = value
        campaign._os_clean_emitted = campaign.os_clean
        return campaign
