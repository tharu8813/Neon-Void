"""Persistent paths for source runs and frozen Windows binaries.

The PyInstaller onefile directory is transient. Writing user data there would
silently lose it at the next launch, so frozen builds use LocalAppData.
"""

from __future__ import annotations

import os
from pathlib import Path
import sys


def data_directory(*, frozen: bool | None = None,
                   local_appdata: str | None = None,
                   home: str | None = None) -> str:
    """Return a stable writable directory in frozen builds.

    Unfrozen source execution intentionally preserves the original repository
    root location. Injectable arguments support tests without a frozen bundle.
    """
    if frozen is None:
        frozen = bool(getattr(sys, "frozen", False))
    if not frozen:
        source_root = Path(__file__).resolve().parent.parent
        # The Godot-root layout moved Python sources under python/.
        # Preserve the historical repository-root JSON location so existing
        # saves stay visible without a risky automatic migration.
        repo_root = source_root.parent
        if source_root.name == "python" and (repo_root / "project.godot").is_file():
            return str(repo_root)
        return str(source_root)

    home_dir = Path(home) if home is not None else Path.home()
    appdata = local_appdata if local_appdata is not None else os.environ.get("LOCALAPPDATA")
    candidates = ([Path(appdata) / "NeonVoid"] if appdata else [])
    candidates.append(home_dir / ".neonvoid")
    error = None
    for path in candidates:
        try:
            path.mkdir(parents=True, exist_ok=True)
            return str(path)
        except OSError as exc:
            error = exc
    raise OSError("Cannot create player data directory") from error
