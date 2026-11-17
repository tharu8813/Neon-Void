# Gameplay balance validation protocol (2026-11-16)

## Reward contract
Each of the four sectors has four software-pick waves followed by a boss. A first-time boss clear now grants one deterministic thematic module chosen from the first non-maxed candidate in `BOSS_SIGNATURE_SOFTWARE`. It still restores up to 15% maximum HP. The new reward is **current-run software**, not an extra permanent extraction slot. The extraction terminal still requires OS CLEAN and archives **exactly one software ID at rank 1**. The selector does not reroll on CLEAN re-entry or after checkpoint restore.

The early-wave local software preference (1.8× relative weight) remains probabilistic. Boss signature is a fixed guaranteed reward, subject to stack caps.

## Numerical baselines (not measured player results)
- Confirmed XP: 20 per completed wave, 75 per boss, 50 per cleaned sector, 200 per OS cleaned. Full run: `4 × (5 × 20 + 75 + 50) + 200 = 1,100 XP`; four CPU waves then abandon: 80 XP.
- Fresh-start level stat formulas: HP `100 + 2 × (level - 1)`, bullet damage `10 + 0.2 × (level - 1)`, base armor `4 + 0.15 × (level - 1)`, mana `100 + level - 1`.
- Baseline mana regeneration 6 MP/s; Q cost 35 MP, base cooldown 7s; E cost 25 MP, base cooldown 12s. Real uptime depends on decisions and survival.
- The E guard skill adds 20 armor and 1.8 HP/s regeneration while active. Damage reduction depends on armor penetration and incoming hit parameters.

## Reproducible in-game tests
Back up `save.json`, `profile.json`, `settings.json`, `antivirus_db.json`, `os_checkpoint.json`, and use separate **test profiles**. Keep the same game version, seed, hardware, no god mode, display and control settings. Record build decisions, damage and actual elapsed wall-clock seconds.

1. **Shooting build:** power, rapid, critical. Log card count, damage, hit frequency and boss kill time.
2. **Area skill build:** focus, mana efficiency, regeneration, cooldown. Log Q frequency, mana deficits, and total wave time.
3. **Survival build:** armor, regeneration, guard duration. Log E timing, hits endured, recovery and clear time.

Repeat at least three runs per build per level (1, 6, 30). Compare median TTK for the *same boss*, deaths, HP and mana at start and end, total run time, FPS and p95 render latency, infection levels and purification usage. Level-dependent damage/HP alone do **not** establish real boss TTK.

For XP fairness, compare (a) stopping after CPU 4 waves vs (b) completing all four regions, using **actual XP / elapsed real minute**. Include movement, load time, failed attempts, station use and terminal interactions. Assess dominant farming strategies before changing XP rewards.

## Remaining validation and release gate
Automated tests can verify selection caps, checkpoint consistency and no duplicate claims; they cannot establish fun, reaction-time readability or actual FPS. Do not rebalance XP, skill damage or mana economy solely from theoretical formulas. Actual Windows controller/mouse feel, audio and sustained performance remain in [QA #24](https://github.com/tharu8813/Neon-Void/issues/24). Track the real measurements and balancing decision in [#31](https://github.com/tharu8813/Neon-Void/issues/31).
