"""In-world hub geography and per-sector progress, independent of Tk/AI.

The bootstrap slice exposes a playable antivirus base, an OS junction and a
CPU gate. Remaining sector gates are visible but intentionally sealed until the
parallel sector-content PR is integrated. No unimplemented zone is represented
as cleared or playable.
"""
from __future__ import annotations

from dataclasses import dataclass, field
import math

WORLD_W = 2400.0
WORLD_H = 1560.0
SECTOR_IDS = ("cpu", "memory", "storage", "network")
AVAILABLE_SECTORS = frozenset(("cpu",))
SECTOR_NAMES = {"cpu": "CPU CORE", "memory": "MEMORY SECTOR",
                "storage": "STORAGE VAULT", "network": "NETWORK GRID"}
START = (1200.0, 780.0)
CPU_EXIT = (100.0, 360.0)


@dataclass(frozen=True)
class Node:
    id: str
    label: str
    x: float
    y: float
    radius: float = 94.0


BASE_NODES = (
    Node("deploy", "OS DEPLOYMENT", 1580, 780, 112),
    Node("database", "ANTIVIRUS DATABASE", 820, 610),
    Node("diagnostics", "DIAGNOSTICS", 820, 1000),
    Node("settings", "SETTINGS TERMINAL", 1540, 1030),
)

OS_NODES = (
    Node("cpu", SECTOR_NAMES["cpu"], 1200, 270, 120),
    Node("memory", SECTOR_NAMES["memory"], 1960, 780, 115),
    Node("storage", SECTOR_NAMES["storage"], 1200, 1320, 115),
    Node("network", SECTOR_NAMES["network"], 440, 780, 115),
    Node("status", "SYSTEM STATUS", 1200, 780, 105),
)

# Some environmental pillars provide real, simple collision in the hub.
PILLARS = (
    (1030.0, 515.0, 1070.0, 610.0),
    (1330.0, 515.0, 1370.0, 610.0),
    (1030.0, 965.0, 1070.0, 1060.0),
    (1330.0, 965.0, 1370.0, 1060.0),
)


def _overlaps_circle_rect(x: float, y: float, radius: float,
                          rect: tuple[float, float, float, float]) -> bool:
    left, top, right, bottom = rect
    px = max(left, min(right, x))
    py = max(top, min(bottom, y))
    return (x - px) ** 2 + (y - py) ** 2 < radius ** 2


def move_on_world(x: float, y: float, dx: float, dy: float,
                  radius: float = 13.0) -> tuple[float, float]:
    """Move independently along each axis, clamping edges and solid pillars."""
    if not all(math.isfinite(v) for v in (x, y, dx, dy, radius)) or radius <= 0:
        raise ValueError("Invalid world movement")
    nx = max(radius, min(WORLD_W - radius, x + dx))
    if any(_overlaps_circle_rect(nx, y, radius, r) for r in PILLARS):
        nx = x
    ny = max(radius, min(WORLD_H - radius, y + dy))
    if any(_overlaps_circle_rect(nx, ny, radius, r) for r in PILLARS):
        ny = y
    return nx, ny


def camera_for(x: float, y: float, width: float = 1280.0,
               height: float = 720.0) -> tuple[float, float]:
    """Clamp camera to the world; the player moves while the viewport follows."""
    return (max(0.0, min(WORLD_W - width, x - width * 0.5)),
            max(0.0, min(WORLD_H - height, y - height * 0.5)))


@dataclass
class WorldSession:
    room: str = "base"
    active_sector: str | None = None
    hub_position: tuple[float, float] = START
    sector_waves: dict[str, int] = field(
        default_factory=lambda: {key: 0 for key in SECTOR_IDS})
    sector_clean: set[str] = field(default_factory=set)

    @property
    def os_clean(self) -> bool:
        return set(SECTOR_IDS) == self.sector_clean

    def nodes(self) -> tuple[Node, ...]:
        return BASE_NODES if self.room == "base" else OS_NODES

    def nearest(self, x: float, y: float) -> Node | None:
        found = sorted(
            ((math.hypot(node.x - x, node.y - y), node)
             for node in self.nodes()), key=lambda item: item[0]
        )
        if found and found[0][0] <= found[0][1].radius:
            return found[0][1]
        return None

    def deploy(self) -> None:
        if self.room != "base" or self.active_sector is not None:
            raise ValueError("Can only deploy from antivirus base")
        self.room = "os"
        self.hub_position = START

    def enter_sector(self, sector_id: str) -> int:
        if self.room != "os" or self.active_sector is not None:
            raise ValueError("Enter a sector from OS hub only")
        if sector_id not in AVAILABLE_SECTORS:
            raise ValueError("Sector not yet available")
        self.active_sector = sector_id
        return min(5, self.sector_waves[sector_id] + 1)

    def leave_sector(self) -> str:
        if self.active_sector is None:
            raise ValueError("Not inside a sector")
        sector = self.active_sector
        self.active_sector = None
        return sector

    def wave_cleared(self, sector_id: str, wave: int) -> bool:
        """Only accept each wave once, in order; avoid duplicate rewards."""
        if self.active_sector != sector_id or type(wave) is not int:
            return False
        if wave != self.sector_waves[sector_id] + 1 or wave > 5:
            return False
        self.sector_waves[sector_id] = wave
        if wave == 5:
            self.sector_clean.add(sector_id)
        return True

    def reset_to_base(self) -> None:
        """Death/abandonment starts a fresh OS; no mid-run restart exploit."""
        self.room = "base"
        self.active_sector = None
        self.hub_position = START
        self.sector_waves = {key: 0 for key in SECTOR_IDS}
        self.sector_clean.clear()
