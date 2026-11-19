"""Offline read-only content integrity checks. Does not require Unity or touch saves."""
from __future__ import annotations
import hashlib
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
GDD = ROOT / "docs" / "NEON_VOID_GDD_V1.md"
EXPECTED_DOCS = [
    "00_MASTER_GDD.md", "01_WORLD_AND_CONTENT.md", "02_COMBAT_AND_STATS.md",
    "03_PLUGINS_AND_SKILLS.md", "04_PROGRESSION_AND_SAVE.md",
    "05_UI_VISUAL_AUDIO.md", "06_UNITY_ARCHITECTURE.md",
    "07_ROADMAP_QA.md", "08_DECISIONS_AND_CONFLICTS.md",
    "09_EXTERNAL_ASSETS.md", "10_FIRST_TASK.md",
]


def main() -> int:
    failures: list[str] = []
    for relative in ("README.md", "CLAUDE.md", "unity/Packages/manifest.json",
                     "unity/ProjectSettings/ProjectVersion.txt", "python/neon_void.py"):
        if not (ROOT / relative).is_file():
            failures.append(f"missing required file: {relative}")
    for name in EXPECTED_DOCS:
        doc = ROOT / "docs" / name
        if not doc.is_file():
            failures.append(f"missing GDD: {name}")
    try:
        manifest = json.loads((ROOT / "unity/Packages/manifest.json").read_text("utf-8"))
        for package in ("com.unity.inputsystem", "com.unity.render-pipelines.universal"):
            if package not in manifest["dependencies"]:
                failures.append(f"required Unity package absent: {package}")
    except (ValueError, OSError, KeyError) as exc:
        failures.append(f"bad Unity package manifest: {exc}")
    if not GDD.is_file():
        failures.append("archived GDD missing")
    else:
        print("Archived GDD sha256:", hashlib.sha256(GDD.read_bytes()).hexdigest())
    for disallowed in ("unity/.godot", "unity/project.godot"):
        if (ROOT / disallowed).exists():
            failures.append(f"Godot artifact should not be in Unity project: {disallowed}")
    if failures:
        for f in failures:
            print("FAIL:", f, file=sys.stderr)
        return 1
    print("PASS: package structure and JSON checks. NOT an editor/playable verification.")
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
