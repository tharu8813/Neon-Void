# E: atomic rare receipt and combat observation contract (Draft)

Owner: E | Ref: #99, #118, #121, #122. This change is stacked on **A #121** because
`rare_bonus.gd` loads C's authoritative `plugin_catalog.gd`, which is **not yet in standalone E #118/main**.
Keep #104 and #118 unchanged; A owns dependency ordering/merge. No automatic migration.

## P0: random rare source + bonus

- On a valid `nv_c_random_rare` choice, `Rules.plugin_picked` obtains the original
  plugin, deterministic eligible rare and two unique receipts **in one E Store transaction**.
- A source may be claimed only once. The rare ledger `mission.environment_rolls.e_rare_bonus_v1`
  is append-only, and the selected rare and level are persisted. Do not infer success
  from an error, timeout or missing ACK.
- `Gateway.reconcile_rare_grant(path, vaccine_id, generation, run_id, source_receipt, offer_id="")`
  reads a verified E state and returns:
  `pair_verified`, `receipt_committed`, `receipt_definitely_absent`,
  `reconciliation_complete`, `can_retry_same_receipt=false`, `grant`, `revision`.
  Pair verification requires the live matching mission, **both** receipts, the
  source plugin and a matching immutable recorded rare grant. Wrong run, closed
  run, destroyed vaccine, failed checksum or ambiguous state returns no proof.
- `rare_bonus.gd::policy_draft()` declares **one** baseline rare,
  **zero** Lv2 additional bonuses and **reject on exhausted candidate pool**.
  These are proposals, not player-adjustable approved balance settings. GDD
  approval is required before changing grants/exhaustion behavior.
- C must not award an additional random rare via its independent
  `rare_reward_request` path for the same source. A should call
  `reconcile_rare_grant` before recognizing an uncertain ACK.

## P1: runtime capture does NOT authorize gameplay rehydration

Already-durable E state includes run ID, vaccine generation, 5 hardware statuses,
map seeds/generator IDs, mission XP, pending vaccine XP/fragments, acquired plugin
levels, C's 3-card offer, reroll count, pity and decimal-text RNG cursor,
and pending extraction/death intent.

E now additionally offers:

```gdscript
Gateway.transact_command(slot_path, {
    "action": "checkpoint_combat_observation",
    "vaccine_id": vaccine_id,
    "generation": generation,
    "run_id": run_id,
    "receipt_id": run_id + ":combat:tick:100",
    "region_id": "CPU",
    "combat_tick": 100,
    "b_version": "B-runtime-v1",
    "d_version": "D-world-v1",
    "b_state": {
        "tick": 100, "rng_state_text": "9223372036854770000",
        "player": {}, "enemies": [], "projectiles": [],
        "skills": {}, "status_effects": []
    },
    "d_state": {
        "tick": 100, "rng_state_text": "-1234567",
        "player_transform": {}, "enemy_transforms": [],
        "world_entities": []
    }
})
```

- E requires a previously persisted matching map seed and generator version.
  B/D packets must agree on a **mission-global tick**; E stores both in one
  checksummed transaction with a stable SHA-256 digest. A later observation must
  advance that tick. Direct Store callers cannot erase or rewind it.
- This is intentionally a **diagnostic observation**, not a replay-complete
  or validated deterministic snapshot. Each producer must later define exact
  serialized fields for positions/velocity/HP/shields/targeting/AI, projectile
  age/trajectory, cooldowns, active effects, buff stacks, camera, world
  interactables, event queue, queued RNG calls and deterministic ordering.
  Source versions and catalog/odds fingerprints must match at replay time.
  A paused capture barrier and restore equivalence test are not yet available.
- `Gateway.inspect_slot` exposes
  `map_offer_checkpoint_ready`, `combat_runtime_checkpoint_present`,
  `combat_observation_snapshot`; **`technically_restorable=false`** and
  **`safe_to_resume=false`** for every active mission.
  The previous map/card readiness must not be marketed as full combat resume.
- No automatic resume, abandon, extraction, death, XP settlement, clone
  decrement, source reroll, or restoration takes place upon restart.
  User approval and A/B/D live replay integration are release blockers.
- If the precise combat tick can never be replayed, A must propose a
  **user-approved safe-checkpoint** policy, define precisely what is conserved,
  and use a new E receipt transition. Do not silently replace a failed run
  with a new run or refund a spent clone.

## P2: non-destructive failure policy

| Failure | Required behavior |
| --- | --- |
| Lost ACK after primary write | Read-only source + derived receipt check; no replay |
| Crash before primary / pending intent | Fail closed, explicit checked roll-forward only |
| Backup older than current primary | Never silently restore stale receipts or clone counts |
| Primary truncation / checksum mismatch | Block loading; preserve original bytes for diagnosis |
| Orphan lock or simultaneous writer | Deny write; explicit offline owner-token review |
| Future save format / schema | Read-only rejection; no migration or reset |
| Reused receipt / rewound tick / erased rare history | Reject transaction without revision advance |
| Vaccine destroyed / clone count spent | Lifetime fence prevents resurrection / refund |

Tests under `tests/rework/persistence/persistence_smoke.gd` use only unique synthetic
`rework-slot-*` folders; five **synthetic** legacy JSON fixture byte streams have
before/after SHA-256 checks. Do not operate on any real save path in QA.

## P3: confirmed mechanics vs unapproved details

Confirmed E rules remain: five hardware cleanup states, extraction bonus on
full cleanup, maximum two new learnable common/rare plugins on success,
manual database archive/withdraw, free hub stat reallocation,
persistent skills, configurable 20–70% death retention of **uncommitted**
rewards, three clone uses and final destruction at fourth death.

Unapproved: Lv2 random rare extra count, exhausted pool fallback, behavior for
reacquiring an already learned plugin, exact partial-extraction UX,
whether to resume an interrupted combat tick or a safe checkpoint,
and abort/refund/settlement policy. These must not be inferred from this PR.

## QA and ownership

- E: checksum, receipts, ledger, diagnostics, synthetic fault tests.
- C: authoritative catalog, max-levels, exactly-once source event and C RNG import/export.
- D: reproducible map generator identity, complete D runtime state export.
- B: complete combat/physics state and deterministic tick restore.
- A: user policy decisions, runtime wiring, UI, CI and eventual merge sequence.

Linux/Windows CI **must** be checked for the exact PR HEAD. CI simulation is
not evidence of a real power outage, multi-process stress or full Windows
gameplay restore. Keep this PR Draft until A signs off.
