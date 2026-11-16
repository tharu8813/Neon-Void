"""Validated, versioned player preferences; no extra packages required."""

from __future__ import annotations

from dataclasses import asdict, dataclass, field
import json
import logging
import math
import os
import tempfile

from .constants import SETTINGS_FILE
from .controls import (DEFAULT_BINDINGS, migrate_v2_bindings,
                       migrate_v3_bindings, validate_bindings)

SCHEMA_VERSION = 4
LOG = logging.getLogger(__name__)


@dataclass
class Settings:
    volume: float = 0.9
    muted: bool = False
    show_fps: bool = False
    reduced_effects: bool = False
    bindings: dict[str, str] = field(default_factory=lambda: DEFAULT_BINDINGS.copy())


def _validated(data: object) -> Settings:
    if not isinstance(data, dict):
        raise ValueError("Settings must be a JSON object")
    version = data.get("schema_version", 1)
    if type(version) is not int or version not in (1, 2, 3, SCHEMA_VERSION):
        raise ValueError(f"Unsupported settings schema: {version!r}")
    value = data.get("volume", 0.9)
    if type(value) not in (int, float) or not math.isfinite(value):
        raise ValueError("Invalid settings volume")
    flags = {}
    for name in ("muted", "show_fps", "reduced_effects"):
        flag = data.get(name, False)
        if type(flag) is not bool:
            raise ValueError(f"Invalid settings boolean: {name}")
        flags[name] = flag
    # Preserve version-2 custom controls even if they used Q or E.
    if version == 1:
        bindings = DEFAULT_BINDINGS.copy()
    elif version == 2:
        bindings = migrate_v2_bindings(data.get("bindings"))
    elif version == 3:
        bindings = migrate_v3_bindings(data.get("bindings"))
    else:
        bindings = validate_bindings(data.get("bindings"))
    return Settings(volume=max(0.0, min(1.0, float(value))),
                    bindings=bindings, **flags)


def load_settings(path: str | os.PathLike | None = None) -> Settings:
    """Read preferences; a missing or invalid file uses safe defaults."""
    location = os.fspath(path) if path is not None else SETTINGS_FILE
    try:
        with open(location, "r", encoding="utf-8") as stream:
            return _validated(json.load(stream))
    except FileNotFoundError:
        return Settings()
    except (OSError, ValueError, TypeError, OverflowError) as exc:
        LOG.warning("Settings load failed (%s): %s", location, exc)
        return Settings()


def save_settings(settings: Settings, path: str | os.PathLike | None = None) -> bool:
    """Atomically replace settings so a crash cannot leave half-written JSON."""
    location = os.fspath(path) if path is not None else SETTINGS_FILE
    temp = None
    try:
        payload = asdict(settings)
        payload["schema_version"] = SCHEMA_VERSION
        # Ensure callers cannot write invalid values even if Settings was mutated.
        normalized = _validated(payload)
        payload = {"schema_version": SCHEMA_VERSION, **asdict(normalized)}
        with tempfile.NamedTemporaryFile(
            mode="w", encoding="utf-8",
            dir=os.path.dirname(os.path.abspath(location)),
            prefix=".neon-settings-", suffix=".tmp", delete=False,
        ) as stream:
            temp = stream.name
            json.dump(payload, stream, ensure_ascii=False, indent=2, allow_nan=False)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temp, location)
        return True
    except (OSError, ValueError, TypeError) as exc:
        LOG.warning("Settings save failed (%s): %s", location, exc)
        return False
    finally:
        if temp is not None and os.path.exists(temp):
            try:
                os.unlink(temp)
            except OSError:
                pass
