# P7 Original Python HUD / Godot 4 Control Parity

**Current status:** code/layout + synthetic Headless checks only. Visual screenshot acceptance and Windows DPI/button hit-target QA remain blocked under #78 and #77. Source of truth is frozen `neonvoid/view.py::_build_hud, _update_hud, _render_hub, _draw_settings`, `neonvoid/constants.py` and Python original settings.

## Frozen 1280×720 HUD map (logical canvas coordinates)

| Component | Python Tk bounds | Godot Control | Status |
|---|---|---|---|
| HP frame | x24–264 / y22–34 | CombatHUD/HPFrame | Matched |
| Mana frame | x24–264 / y60–74 | CombatHUD/ManaFrame | Matched |
| Dash pips | x24 + index×30 / y44–50 | CombatHUD/DashSlot*, DashFill* | Matched |
| Live stats | x19–373 / y90–262 | CombatHUD/LiveStatsPanel | Matched |
| Left live stats | x34 / y129 | CombatHUD/StatsLeft (six rows) | Equivalent |
| Right live stats | x194 / y129 | CombatHUD/StatsRight (six rows) | Equivalent |
| Stats effect footer | x34 / y245 | CombatHUD/StatsEffects | Equivalent |
| Q/E skill panel | x(W/2)±275 / y(H−86)–(H−24) | CombatHUD/SkillPanel | Matched |
| Q/E cooldown bars | x(W/2)−224…−40, +40…+224 / y(H−42)…(H−38) | PulseCooldown, GuardCooldown | Matched |
| Infection | x(W−420)–(W−24) / y107–190 | CombatHUD/InfectionPanel | Matched |
| Wave | x(W−24) / y26, right-aligned | CombatHUD/WaveText | Matched anchoring |
| Score | x(W−24) / y58, right-aligned | CombatHUD/ScoreText | Matched anchoring |
| Boss life | x(W/2)±300 / y84–96 | BossHUD/BossBarBackground | Matched |
| Boss title | xW/2 / y62 | BossHUD/BossName | Matched |
| Installed software | x24 / y(H−24) | CombatHUD/InstalledSoftware | Matched anchoring |
| Base/OS hub panel | x12–493 / y12–75 | WorldHUD/WorldInfoPanel | Matched |
| Nearby terminal prompt | x(W/2)±270 / y(H−95)–(H−49) | WorldHUD/NearbyPanel | Matched |

The fixed map is expressed through **left, right, top, centre and bottom anchors** rather than hardcoded window pixels. Godot logical 1280×720 stretch remains responsible for native Windows display scaling. Python base/OS hub uses **WorldHUD only**. Combat, between-wave and cleared sector use **CombatHUD only**. Reward, dead and title states keep the combat HUD hidden behind their own menus.

## Presentation-only state bindings

- The A bridge `_hud_snapshot` adds `charge_t`, `dash_cd`, `guard_t`, and `nearby` without touching the B/C/E state or introducing storage fields.
- D's `Software.skill_mana_cost` and `Software.skill_cooldown` are used directly by the HUD to show **true** Q/E cost and cooldown labels, not synthetic timers.
- Two live-stats columns show damage, rate, crit, effective armor, armor pen, speed, mana regen, health regen, on-hit health, burn chance, toxin chance, and skill power. A trailing chip follows HP loss, low HP changes color, dash pips track charges, wave and score are sourced from state.
- `nearby` only affects prompt text and panel visibility. It never invokes a terminal. **F interaction still checks distance in Main._on_interact**.
- `neon_theme.gd` now uses the original palette, squared corners and Python menu style without moving feature screens out of D's control.

## Automated gates

`godot/tests/ui_smoke.gd` (D Linux/Windows CI) asserts every major frozen HUD rectangle/anchor plus correct two-column stats, mana shortage, cooldown, score/wave, BOSS HUD, base/combat visibility, in-world F prompt and menu signal isolation. `godot/tests/integration_smoke.gd` (A Linux/Windows CI) continues to cover the 4×5 combat and safe hub terminal workflow. Rendering is headless and does **not** establish actual pixel-level parity, DPI pointer accuracy, motion quality or GPU performance.

## Windows physical A/B still required

- [ ] Tk and Godot 1280×720 side-by-side screen captures: OS/base hub, CPU enemy combat, boss, software reward, pause/settings, OS CLEAN, extraction/death.
- [ ] 960×540, 1920×1080, 1024×768 and 21:9 screenshot legibility; check upper-right infection vs boss warning, Q/E bottom, left HUD vs exit gate, long strings and all hotkeys.
- [ ] 100%, 125%, 150%, 200% DPI pointer and button hitboxes: no text clipping, no invisible popups, no accidental gameplay shooting through menus.
- [ ] Original audio, timer effects, score/combo popups, dashed cooldown bars and HUD low-health vignette still need further visual/functional parity implementation (not included).
- [ ] Real GPU frame-time, actual 30–60 minute Windows play, user approval via #77 prior to main merge and release.

This P7 PR is **stacked Draft** after #80 → #79 → #76. Never merge into main before previous Drafts have been reviewed and #77 manually approved.
