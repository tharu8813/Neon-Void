"""Backward-compatible save persistence."""

from __future__ import annotations



import json
import logging
import os
import tempfile
from .constants import SAVE_FILE


def load_save():
    """Read the original save.json schema and keep legacy defaults."""
    try:
        with open(SAVE_FILE, "r", encoding="utf-8") as stream:
            data = json.load(stream)
        if isinstance(data, dict):
            return {"best_score": int(data.get("best_score", 0)),
                    "best_wave": int(data.get("best_wave", 0)),
                    "runs": int(data.get("runs", 0))}
    except (OSError, ValueError, TypeError):
        pass
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
