# P0 run integrity and offline write-lock recovery

This patch covers #87, #88, #89 and #90. Existing Python files and the Godot
`state.json` envelope schema are unchanged. Synthetic tests only use a unique
`user://p0-integrity-*/slot` directory.

## Run boundaries

- Fresh OS: generate a random 31-bit gameplay seed for campaign + combat; the
  `start_new_run(seed_override)` parameter is for deterministic scripted tests
  (e.g. 42), not the normal menu action.
- The seed is stored in the pre-existing checkpoint `campaign.seed` and reused
  on resume. A resumed OS never generates a new seed or fresh XP receipt.
- Fresh OS: dash charges become `dash_max`, dash/charge/invulnerability timers
  are reset. Resume retains confirmed HP, mana, charges and C skill cooldowns;
  non-serialized mid-dash/invulnerability clocks cannot be resurrected.
- The per-run maximum combo is tracked independently from its 2.6-second live
  window. The existing `run_best_combo` checkpoint/profile fields now contain
  real values. Resume preserves the peak but starts a new live combo chain.

## When saving reports `locked`

**Never automatically remove or overwrite a `.write-lock`**. On acquiring the
directory mutex, the writer now stores a versioned `owner.json` with a 128-bit
nonce, PID and creation timestamp. Only the owner with the matching nonce
releases it. An abrupt crash can still leave a lock.

For offline recovery on a COPIED/TEST SLOT:

1. Exit Neon Void and all related Godot/CI instances. Inspect your OS process
   list to confirm no writer remains. The Godot `OS.is_process_running()` API
   is documented for spawned children only; its `false` result is insufficient
   proof that a different game process is dead.
2. Back up the whole Godot slot, including the lock, current primary,
   `state.json.bak`, and optional import manifest. Do not use the original
   Python save as a test fixture.
3. Call `SaveStore._lock_owner(slot)` to inspect the PID, timestamp and nonce.
   Missing/invalid metadata or the current process's own PID must fail closed.
4. Only after confirming offline ownership, call
   `SaveStore.recover_stale_lock_offline(slot, exact_token, true)`.
   It refuses uncertain or mismatched ownership and any unreadable primary;
   it renames the old directory to `<slot>.abandoned-lock-<token>`, keeping
   forensic metadata. It compares primary/backup bytes before and after.
5. Reopen the slot and verify XP receipts, run state, settings and backup.
   A corrupt primary is **not** repaired by clearing the lock. Follow the
   independent, explicit backup-recovery procedure and review the missing
   most-recent-revision risk first.

A human can still be wrong about a process being inactive. This API does not
promise arbitrary-process liveness detection and **must never run at boot**.
Old pre-upgrade, ownerless locks remain fail-closed and require separate manual
investigation; they cannot be silently converted into an owned lock.

## Automated coverage / remaining gates

`tests/p0_run_integrity_smoke.gd` checks explicit/random seed checkpointing,
same-run resume, peak combo persistence and profile finalization, fresh-OS
resource isolation, active lock refusal, token/consent enforcement, orphaned
lock quarantine and primary/backup byte integrity. It is run on Linux/Windows
by `godot-a-integration.yml`.

Not certified by headless: physically killing a separate writer during an
atomic rename, Windows ACL/antivirus interference, power-loss durability,
actual GPU/DPI/input feel or physical speaker audio. Track these in #61/#77.
