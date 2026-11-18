# E Rework progression/persistence contract (draft for A review)

Owner: E / #99. No existing save files or Godot root scenes modified.

## Namespaces and isolation

- New saves: `user://rework/v1/rework-slot-<slot_id>/state.json`, **not** the P8 `user://persistence/slot-v1` directory and never a Python folder.
- Slot IDs use filesystem-safe ASCII letters/digits/`_`/`-`/`.`. Game IDs additionally allow `:` in receipts/offer IDs (as C uses `run_id:offer_index:slot`), max 100 characters. Do not use `:` in a slot ID.
- `State.new_slot(slot_id, vaccine_id, balance_config)` creates an in-memory version 1 payload. `Gateway.initialize(...)` refuses existing or corrupt slots. No implicit legacy import.
- Global settings belong to A and are intentionally excluded from vaccine destruction.

## Public typed-by-schema API

`scripts/rework/persistence/progression_gateway.gd` exposes:

1. `Gateway.snapshot(slot_path) -> {ok, revision, snapshot}`; treat the returned snapshot as read-only.
2. `Gateway.transact_command(slot_path, command: Dictionary, expected_revision: int = -1) -> {applied, revision?, event?, reason?}`.
3. `Store.default_slot_path(slot_id)` and `Gateway.initialize(slot_id, vaccine_id, config={})`.
4. `Store.restore_backup(slot_path, explicit_consent=true)` restores only the **latest verified committed recovery copy**, not the older `.bak`. `Store.recover_interrupted_commit(slot_path, explicit_consent=true)` explicitly rolls forward a verified in-flight intent. Both are **offline recovery actions**, not automatic startup fallbacks. A lock surviving a crash must be diagnosed offline before `release_orphan_lock_offline(slot, exact_owner_token, true)`.

All regular commands except `choose_new_vaccine` carry `action`, `vaccine_id`, `generation`. Mission-scoped commands also carry `run_id`. Every award or irreversible action carries **globally unique per-vaccine** `receipt_id` (e.g. `run-abc.CPU.clear`); never reuse it on another mission. Replaying a committed receipt is rejected without changing the revision.

| Command action | Fields beyond identity/generation | Primary producer |
| --- | --- | --- |
| `start_mission` | `run_id`, `seed` (nonnegative unique seed supplied by D) | D |
| `mission_xp` | `run_id`, `receipt_id`, `amount` | D/B via A |
| `pending_reward` | `run_id`, `receipt_id`, `xp`, `fragments` | D |
| `objective_completed` | `run_id`, `receipt_id`, `objective_id` | D |
| `hardware_cleaned` | `run_id`, `receipt_id`, `hardware_id` in CPU/GPU/RAM/SSD/HDD | D |
| `plugin_picked` | `run_id`, `receipt_id`, `offer_id` (C 3-card screen ID), `plugin_id`, `rarity`, `level`, `offer_source` (levelup/hardware/event) | C |
| `extract` | `run_id`, `receipt_id`, `learn_plugin_ids` (max 2) | D + C + A |
| `die` | `run_id`, `receipt_id` | D/B through A |
| `archive_plugin`, `withdraw_plugin` | `plugin_id`, `receipt_id` (hub only) | A UI |
| `reallocate_stats` | `receipt_id`, `allocations` stat ID -> purchased increments (hub only) | A UI |
| `learn_skill` | `receipt_id`, `skill_id`, optional `equip_on_discovery` (up to 3 known/new IDs) | B via A |
| `equip_skills` | `receipt_id`, `equipped` array up to 3 known IDs (hub only) | A/B |
| `choose_new_vaccine` | `new_vaccine_id` (only after final destruction) | A UI |

Example D event:
```gdscript
var result = Gateway.transact_command(slot_path, {
    "action": "hardware_cleaned", "vaccine_id": current_vaccine_id,
    "generation": current_generation, "run_id": current_run_id,
    "receipt_id": current_run_id + ".CPU.clear", "hardware_id": "CPU"
})
if result.get("applied", false):
    # Now and only now acknowledge the committed clean reward to UI/world.
    pass
```

Level-up plugin offers are counted in `mission.pending_plugin_offers`. A C selection with `offer_source="levelup"` consumes exactly one. E additionally records `mission.claimed_offer_ids`, so two different slot receipts from the **same** C `offer_id` can never grant two plugins. C owns random generation and plugin max-level checks. C `choose_card(slot)` returns `offer_id`, `receipt_id`, `plugin_id`, `rarity`, `level`, `learnable`; A maps those fields to the E `plugin_picked` command and adds `offer_source`. Temporary plugin offers from hardware/event use a different `offer_source`.

## Domain invariants and GDD draft knobs

- Persistent vaccine XP thresholds: `180 + 90 * (level - 1)` [draft v0.1].
- Per-mission XP thresholds: `100 + 30 * (level - 1)` [draft v0.1].
- XP and points earned during a mission are `uncommitted_*` until successful extraction. Death commits floor(20% + 10% per cleaned hardware, capped 70%) of each pending balance.
- Leveling vaccine grants 100 point fragments (1 whole point) [draft v0.1]; sub-point fragments survive. Free redistribution is bounded by the total earned fragments. B owns stat effect coefficients.
- Successful extraction requires at least one cleaned hardware; five cleaned hardware additionally grant 220 XP [draft v0.1]. Hardware cleared grants 120 permanent XP, objective completed 15 [draft v0.1]. D/B separately send mission XP, so no double-counting.
- New mission resets mission level to 1. Existing carried learned plugins stay physically carried until deposited. Death deletes all carried; DB-stored ones survive while copies remain.
- On death 1,2,3, copies remaining 2,1,0; death 4 destroys only the vaccine object and sets `selection_required=true`. A must switch to the selection screen. Old generation events are rejected after a new vaccine is selected.
- Newly discovered skills may be equipped during a mission **only in the same `learn_skill` transaction** using `equip_on_discovery`. Other loadout swaps are hub-only.\n- Newly learnable plugins on successful extraction: common/rare only, max 2, level preserved. They remain in carried inventory; hub deposit is an explicit operation. Legendary/mythic are never archived.
- Archive and carried are mutually exclusive maps. Repeated copies are not synthesized. Memory cap is not artificially enforced beyond save safety limits.

## Storage guarantees and failure semantics

- The new slot is created only if absent. A checksum-verified JSON envelope (`state.json`) stores a single revision and all committed receipts/XP/DB updates. `lifetime_fence.json` prevents an irrevocable clone decrement or destroyed vaccine from being rolled back.
- Under an exclusive per-slot lock the writer: validates primary + lifetime fence + any old intent, computes a pure state change, writes its verified prior `state.json.bak`, writes **the full proposed new envelope** to `state.json.intent`, advances the monotonic lifetime fence first when necessary, promotes the proposed primary via atomic rename, verifies it, writes an identical `state.json.recovery` (latest committed recovery copy), then deletes the intent. Only after these steps does `{applied:true}` acknowledge the operation.
- `state.json.bak` is a diagnostic **previous-revision** copy. Restoring it could remove the latest receipt and re-award a prize. **Never auto-promote it** and never treat it as a safe substitute for `state.json.recovery`.
- **Crash before intent:** the previous primary and latest recovery copy remain valid; no receipt was committed.
- **Crash after intent but before primary:** reads/writes fail closed (`intent_requires_reconciliation` or `lifetime_fence_violation`). With explicit offline consent, `recover_interrupted_commit()` checks matching previous-revision backup, stable slot IDs, strictly monotonic receipts/level/clones, intended future revision, and lifetime fence, then **rolls forward** the exact intended saved state. A/D must not translate an app crash into an additional death.
- **Crash after primary but before recovery-copy/acknowledgment:** the primary contains the award, and the intent remains. On next transaction the committed intent is verified against primary, the latest recovery copy is repaired, and the old receipt is rejected as duplicate. No second payout or additional clone cost.
- **Corrupted primary without intent:** only the *same-revision*, checksum-verified `state.json.recovery` may be restored with explicit operator consent. The previous-revision `.bak` is intentionally NOT used. Future formats, mismatched lifetime fences, missing/corrupt latest copy, or unreconciled intent block recovery rather than silently losing progression.
- Owner locks are not stolen automatically. Offline recovery requires that **every game process be stopped**; an operator must supply the actual owner token and consent. Temp files, partial initializations and damaged files are left for offline diagnosis rather than cleaning arbitrary paths.
- Storage is capped at 16 MiB per JSON envelope; there is no plugin-count quota, but writes beyond safe storage bounds fail without mutating the primary. Integer values are limited to exact IEEE-754 range (2^53 - 1).
- Legacy Python five files and the old Godot P8 slot are never auto-imported or overwritten. Import/migration is a future opt-in, copy-first task owned by A after a separate security review.

### Recovery UX requirement for A

An interrupted save must **freeze the affected slot** and show a clear recovery-required state, not grant another reward, rerun the mission, or silently reset clone/XP. The CLI/API recovery consent parameter is only an offline safety gate; A must provide a user-facing diagnosis and explicit backup preservation policy before a shipped recovery button exists. These recovery procedures are **not yet validated on physical Windows hardware**.

## Actual C/D branch compatibility (requires A to wire)

- **C #102:** `PluginOfferEngine.choose_card(slot)` returns `{offer_id, receipt_id, plugin_id, rarity, level, learnable}`; `receipt_id` is `run_id:offer_index:slot`. E accepts colons and rejects two different claims carrying the same `offer_id` within one mission. A appends the chosen `offer_source` (levelup, hardware or event); keep C's receipt string unchanged.
- **D #101:** `ExtractionState.world_fact` type `objective_completed` maps to E `objective_completed` with `objective_id = region_id + "." + objective_id` to avoid collisions; `hardware_cleansed` maps to E `hardware_cleaned` with `hardware_id = region_id`. The D `boss_defeated` signal is **not** a separate E clean reward: D already emits one hardware clean after validating its boss and objectives.
- **D terminal events:** `mission_extracted` maps to E `extract` only *after* the player has chosen 0–2 eligible newly learned plugins; `mission_failed` maps to E `die`. Reuse the exact D `receipt_id` (`world:<run_id>:extraction` or `world:<run_id>:death`) and the current vaccine/generation from E snapshot.
- **Failed commit handoff:** C `choose_card` and D `request_extraction/report_death` can advance their *in-memory* states before E has acknowledged durable persistence. A must retain the immutable event payload until E reports `applied=true`, and on failure freeze/reconcile the session rather than dropping the reward, advancing UI, or rerolling another receipt. E's receipt ledger is not a substitute for integration-side acknowledgement handling.
- **Mission seeds:** D controls distinct mission/map/encounter seed generation. E persists the top-level run seed and cleaned IDs; granular world metadata fields are reserved for the A/D integration adapter and are not yet wired.

## [미확정] integration decisions for A

1. Recovery of an **already learned** plugin also found among new mission rewards, and whether this affects the 2-new-learning quota. Current code rejects duplicate learning, rather than duplicating or merging.
2. Crash while mission is active: E preserves the snapshot and does **not** translate crash into death. A/D must decide resume vs explicit abort after UX approval.
3. D owns fresh `run_id`, map seed, enemy generation, and cleaning truth. E rejects reused run IDs; D must supply a new seed/map after failed runs.
4. C owns plugin pity/reroll mechanics; state reserves `pity_counter` and `rerolls_left`, but these are not mutated by E's first PR.
5. A owns CI workflow changes. Run both new scripts separately on Linux/Windows, in addition to existing P8 smoke; do not take legacy P8 success as rework success.

## Headless validation commands

```sh
godot --headless --path . --editor --import --quit
godot --headless --path . --script res://tests/rework/progression/progression_smoke.gd
godot --headless --path . --script res://tests/rework/persistence/persistence_smoke.gd
```

Tests create unique **synthetic** folders under `user://rework-e-test-*`, never open actual legacy/user data, and clean only their own directories.
