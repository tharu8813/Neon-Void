"""Backward-compatible save persistence."""

from __future__ import annotations



import json
import logging
import os
import tempfile
from .constants import SAVE_FILE


def quarantine_corrupt(path) -> str | None:
    """Move a damaged file aside instead of silently destroying it.

    A truncated or garbled save/profile/settings file is valuable: the player
    may want to repair it by hand. The original is renamed to `<name>.corrupt`
    so the next successful write cannot erase the evidence.
    """
    try:
        if not os.path.exists(path):
            return None
        target = f"{path}.corrupt"
        index = 1
        while os.path.exists(target):
            target = f"{path}.corrupt.{index}"
            index += 1
        os.replace(path, target)
        return target
    except OSError:
        return None


def load_save():
    """Read the original save.json schema and keep legacy defaults."""
    try:
        with open(SAVE_FILE, "r", encoding="utf-8") as stream:
            data = json.load(stream)
        if isinstance(data, dict):
            return {"best_score": int(data.get("best_score", 0)),
                    "best_wave": int(data.get("best_wave", 0)),
                    "runs": int(data.get("runs", 0))}
    except FileNotFoundError:
        pass
    except (OSError, ValueError, TypeError) as exc:
        logging.getLogger(__name__).warning(
            "Save file is damaged (%s): %s", SAVE_FILE, exc)
        quarantine_corrupt(SAVE_FILE)
    return {"best_score": 0, "best_wave": 0, "runs": 0}


def write_save(data):
    """Atomically replace the original save.json, avoiding partial writes."""
    tmp = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="w", encoding="utf-8", dir=os.path.dirname(SAVE_FILE),
            prefix=".neon-save-", suffix=".tmp", delete=False
        ) as stream:
            tmp = stream.name
            json.dump(data, stream, ensure_ascii=False, indent=2)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(tmp, SAVE_FILE)
    except (OSError, TypeError, ValueError) as exc:
        logging.getLogger(__name__).warning("Could not save progress: %s", exc)
    finally:
        if tmp and os.path.exists(tmp):
            try:
                os.unlink(tmp)
            except OSError:
                pass
