# P1: Player settings

NEON VOID settings are saved in `settings.json` beside `save.json`, but the two files are independent. The original save file remains untouched by this feature.

## Current controls

- From the title: press **O** or click **게임 설정**.
- During a run: pause with **P** (or Esc), then press **O** or click the new menu entry.
- In the settings overlay: **Up/Down** (or **W/S**) chooses a row, **Left/Right** (or **A/D**) changes it, **Enter** toggles the chosen row, **Esc/O** closes.
- Clicking each row changes its setting; for volume the left/right half steps down/up by 10%.
- **M** in ordinary gameplay or on the title toggles mute and persists it.

## Available preferences

| Preference | Saved? | Behavior |
| --- | --- | --- |
| Master volume (0%–100%, 10% steps) | Yes | Changes software mixer SFX and background music gain immediately; native `winsound` fallback has no independent per-effect gain control |
| Mute | Yes | Silences the mixer and clears pending voices |
| Reduced effects | Yes | Disables screen shake and moving scanline; particles are unchanged |
| Show FPS | Yes | Toggles the frame-rate overlay immediately |

CLI options `--volume` and `--fps` override the saved preference for that invocation. The volume setting becomes persistent if changed in the settings screen. `--no-sound` always disables audio regardless of preferences.

## Persistence and compatibility

The JSON structure contains `schema_version: 1`. Missing files use defaults. Invalid files are ignored with a warning rather than crashing the game. Preferences save atomically and are excluded from Git. No extra dependencies required.

## Testing

```powershell
python -m unittest discover -s tests -v
python -m tests.gui_settings_smoke
python neon_void.py --selftest 3600 --seed 42 --god --no-sound
```

The GUI test requires a real Tk display (Linux CI runs it with Xvfb). It doesn't simulate actual mouse-driven UX judgement; confirm visually on Windows before a release.
