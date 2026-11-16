# Building and testing the Windows standalone app

NEON VOID keeps the normal `python neon_void.py` source workflow. The optional Windows executable is a **standalone Python bundle**, not a setup installer or signed release. Runtime does not require installing Python.

## Build on a Windows machine

```powershell
python -m pip install "pyinstaller>=6.16,<7"
python -m unittest discover -s tests -v
python -m PyInstaller --noconfirm --clean --onefile --windowed --name NeonVoid neon_void.py
```

Output: `dist/NeonVoid.exe`. The repository ignores the temporary `build/` and `dist/` output directories. The Tk/Windows libraries are bundled by PyInstaller.

## CI and artifact

The `Windows standalone build` GitHub Actions workflow runs on pull requests affecting application source, on pushes to main, and manually via the **Actions → Windows standalone build → Run workflow** button. Its Windows runner:

1. Compiles Python sources and runs the unit tests.
2. Builds a onefile, windowed executable using PyInstaller.
3. Launches the executable with `--version` and a 360-frame `--selftest` using `Start-Process -Wait -PassThru` to inspect exit codes.
4. Uploads `NeonVoid-windows-x64.zip` as a downloadable Actions artifact (retained for 14 days).

The CI bundle launch test validates startup and automated simulation. It **does not validate sound playback, real keyboard layouts, display scaling, graphics quality or Windows SmartScreen behavior**. A real Windows manual playtest is required before distribution to players.

## Save location and updates

- Running from Python source: `save.json`, `settings.json`, `profile.json`, `antivirus_db.json`, and `os_checkpoint.json` are next to `neon_void.py`, preserving existing user data.
- Running the bundled EXE: all five files are stored in **`%LOCALAPPDATA%\\NeonVoid`**. If LocalAppData is unavailable, `~\\.neonvoid` is used as a fallback.
- The directories are intentionally different. To transfer your existing records into the EXE build, copy the files from the source directory to the new data directory with the game **closed**. Back up existing files first.
- When an error is written to the windowless executable's Python stderr, it goes to `errors.log` in that data directory. This is not yet a complete crash-reporting or telemetry system.
- Builds are not signed, and the workflow does not publish GitHub releases automatically.

## Troubleshooting

If a bundled game fails to open, inspect `%LOCALAPPDATA%\\NeonVoid\\errors.log` and the Windows build workflow logs. Avoid publishing binaries that have not been manually run on an actual Windows desktop. If a player reports that their best score reset, check whether they switched from the source save directory to the EXE save directory.
