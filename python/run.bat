@echo off
setlocal
cd /d "%~dp0"
rem Python sources now live under python\. Reuse a pre-existing repository-root
rem virtual environment as well as a new python\.venv without modifying saves.
if exist ".venv\Scripts\pythonw.exe" (
    start "" ".venv\Scripts\pythonw.exe" "neon_void.py"
) else if exist "..\.venv\Scripts\pythonw.exe" (
    start "" "..\.venv\Scripts\pythonw.exe" "neon_void.py"
) else (
    echo venv not found. Run: py -3 -m venv .venv
    pause
)
