# Neon Void Rework · Windows QA build

**This executable is a dedicated test build, not the original `NeonVoid3D.exe` and not a release.** The regular Windows artifact launches the original four-sector Godot game. This `NeonVoidReworkQA.exe` launches `scenes/rework/integration/rework_windows_qa.tscn` instead, which is the B/C/D/E five-hardware rework and A's persistent integration.

## Dedicated E save
- The QA scene uses **only** E's rework namespace under `user://rework/v1/rework-slot-windows-qa-20261118`. It does not migrate/delete/overwrite the legacy game save, the original Godot scene, or the other `integration-pilot` slot.
- Progress remains on the same machine between QA EXE relaunches. The game UI shows the saved revision `rN` and the hub's five tabs show database, investments and three equipped skill slots. If a save is corrupt or a mission was interrupted, the app deliberately blocks unsafe resume rather than silently repairing or replacing it.
- The QA slot's vaccine and skill IDs are not a recommended official balance. The three current B skill definitions and their numbers remain explicitly provisional.

## QA setup and gameplay
1. Extract the **`NeonVoidReworkQA-windows`** artifact ZIP and launch `NeonVoidReworkQA.exe`. Check that the upper-left status starts with **리워크 QA 전용**. If you see the original CPU/MEMORY/STORAGE/NETWORK campaign instead, that is the wrong executable.
2. In the walkable hub, click **QA 전용 · E 저장으로 테스트 스킬 3종 장착** once. The button carries out real E `learn_skill` x3 and `equip_skills` transactions and verifies durable readback. The UI says **E 저장 확인 rN**, and repeat clicks cannot grant duplicate skills. This is a QA-only shortcut to test provisional skills without inventing a production rewards system.
3. Press Tab and inspect the skill tab: the saved **Arc Pulse, Penetrator, Root Packet** skill IDs should appear. Leave the panel and deploy via the in-world terminal. Use **Q/E/C** in actual B 60Hz combat, observe the skill bar, mana reduction and nonzero cooldown. Test near an enemy rather than firing at a wall.
4. Complete a hardware mission, including its actual objective, enemy wave and B boss. Check the E-authenticated result, level and XP; use the physical spawn return pad to return to the motherboard if you want to continue to another hardware.
5. For final extraction, go to a **cleansed** hardware's extraction pad, choose up to two eligible ordinary/rare plugins, then confirm the result. Only completed extractions can be safely restarted; intentionally interrupted missions are not automatically resumed.
6. Return to the hub, note **saved rN**, vaccine level, XP, permanent investments, carried/archived plugins and equipped skills; close the EXE normally and reopen **the same QA EXE**. Check the same E slot contents and `rN`. You can test investments in the five-tab hub if the vaccine has actually earned enough growth fragments.

## Report
- What was visible in the upper-left status, especially **리워크 QA 전용**?
- Was the 3-skill kit button available? Did pressing it show an E save revision?
- Can Q/E/C cast, reduce mana and start skill cooldown? Any rejected slot or unexpected fail-closed message?
- After a completed extraction and restart, did XP, level, investments, equipped skills and earned plugins remain unchanged?
- For issues, send the **region**, reproduction sequence and screenshot or brief screen recording.

**Limits**: headless CI covers single-run 5-region objectives and actual B projectile receipts, but does not measure physical input latency, audio, VFX, real 60 fps performance or full player-operated pacing. Neither QA shortcut nor this binary is a release-ready skill unlock policy.
