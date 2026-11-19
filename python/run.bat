@echo off
setlocal
cd /d "%~dp0"
rem Optional local virtualenv; no virtualenv is required for runtime.
if exist ".venv\Scripts\pythonw.exe" (
    start "" ".venv\Scripts\pythonw.exe" "neon_void.py"
    exit /b 0
)
if exist "..\.venv\Scripts\pythonw.exe" (
    start "" "..\.venv\Scripts\pythonw.exe" "neon_void.py"
    exit /b 0
)
where py >nul 2>nul
if errorlevel 1 goto CHECK_PYTHON
py -3 "neon_void.py"
exit /b %errorlevel%
:CHECK_PYTHON
where python >nul 2>nul
if errorlevel 1 goto NO_PYTHON
python "neon_void.py"
exit /b %errorlevel%
:NO_PYTHON
echo Python 3.11 or newer is required. Install Python and try again.
pause
exit /b 1
