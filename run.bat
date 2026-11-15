@echo off
setlocal
cd /d "%~dp0"
if exist ".venv\Scripts\pythonw.exe" (
    start "" ".venv\Scripts\pythonw.exe" "neon_void.py"
) else (
    echo venv를 찾을 수 없습니다. 먼저 python -m venv .venv 를 실행하세요.
    pause
)
