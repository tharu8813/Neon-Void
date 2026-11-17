# E v1 optional mission checkpoint and recovery contract

> Refs #99 / #108. Owner: E. Adds an **optional** `mission.environment_rolls.e_checkpoint_v1`
> inside the existing revisioned E state envelope. This is **not** a schema version
> migration, does not rewrite old slots on load and does not approve mid-mission resume.

## What is already durable even without this checkpoint

`vaccine.mission` stores `run_id`, `seed`, `mission_level`, `mission_xp`,
`pending_plugin_offers`, `hardware_cleaned_ids`, `objective_ids`,
`obtained_plugins` including levels, `claimed_offer_ids`,
`generated_map_seeds`, `mission_difficulty`, `environment_rolls`,
`rerolls_left`, `pity_counter`, `uncommitted_vaccine_xp` and
`uncommitted_point_fragments`. Closing a run stores `closed_runs[run_id]`
with extracted/dead outcome, unless fourth death destroys the vaccine; the
lifetime fence then records that irreversibility.

## New E transactions (all require current vaccine ID + generation + run ID)

Every successful operation uses `Gateway.transact_command(slot_path, command)`,
adds an independent stable receipt and returns `{applied:true,revision}` only
after E's atomic/verified save. If an ACK is lost, query
`Gateway.reconcile_receipt(...)`; never select a different card or grant XP.

### 1. World map

```gdscript
{
  "action":"checkpoint_region", "vaccine_id":"vaccine-1", "generation":1,
  "run_id":"run-1", "receipt_id":"run-1:CPU:map-checkpoint",
  "region_id":"CPU", "map_seed":24680, "generator_id":"D-procedural-v1"
}
```

A region already checkpointed can **never** be regenerated with a different
seed/generator identity by E. D must return the same procedural geometry from
the seed **and** the generator version. Old world seeds must not be blindly
reinterpreted after D changes its generation algorithm. Distinct regions may
be checkpointed independently.

### 2. Plugin reward snapshot, before displaying or re-rolling any card

```gdscript
{
  "action":"checkpoint_offer", "vaccine_id":"vaccine-1", "generation":1,
  "run_id":"run-1", "receipt_id":"run-1:offer:1:show",
  "rng_state_text":"-3510564601913832024",
  "offer_snapshot":{
    "active":true, "prepared":{}, "reconciliation_required":false,
    "offer_id":"run-1:1",
    "context":{"cleared_hardware":0,"difficulty_tier":0,"luck_points":0.0},
    "cards":[
       {"slot":0,"plugin_id":"sample_a","rarity":"common","empty":false},
       {"slot":1,"plugin_id":"sample_b","rarity":"rare","empty":false},
       {"slot":2,"plugin_id":"sample_c","rarity":"common","empty":false}
    ],
    "high_seen":false,"rerolls_remaining":3,"missed_screens":0
  }
}
```

The request accepts C's `offer_snapshot()` format, but only when no card
is currently prepared/uncertain. The RNG cursor is a **decimal string** (C's
`str(_rng.state)` when available), not a JSON number, to avoid silent 64-bit
rounding. Empty cursor is allowed for existing C implementations but marks
the mission **not technically restorable for future rerolls**.

Opening a new offer requires the next `run_id:sequence`, enough pending
mission-level rewards and no unclaimed prior offer. Saving a reroll requires
the same offer, identical other two cards, exactly one decrement of rerolls,
no forged pity change and no terminal intent. The same card may legitimately
reappear after a reroll; the spent reroll is still recorded once.

After E stores a checkpointed `plugin_picked`, E verifies the **exact**
`offer_id:slot` receipt, rarity, ID and next level against the saved card.
The same transaction clears the pending offer, records a claimed offer ID,
increments/reset pity according to the saved high-card appearance and banks
the plugin level. It cannot pay an unseen card. The older uncheckpointed A
pilot still follows its existing valid receipt path, but does **not** claim
the stronger saved-card protection until A wires these commands.

**Required C changes, NOT implemented in E:** Export/import exact RNG
state, `offer_sequence`, pending cards, levels, `missed_screens`,
`rerolls_remaining`, and committed/uncertain prepared selection. On restore,
consume E's snapshot as authoritative and **never call `roll_offer` for
an already persisted offer**. C must validate the catalog/odds version and
have stable pool ordering; RNG cursor alone does not guarantee reproducible
future rolls across catalog changes. A coordinates these interfaces.

### 3. Terminal intent (optional, before a known extraction/death action)

```gdscript
{
  "action":"prepare_terminal", "vaccine_id":"vaccine-1", "generation":1,
  "run_id":"run-1", "receipt_id":"run-1:terminal:prepare",
  "outcome":"extract", "terminal_receipt_id":"run-1:terminal:commit",
  "learn_plugin_ids":["sample_b"]
}
```

The preparatory transaction grants **nothing** and is shown as
`terminal_intent_requires_reconciliation` after restart. If present,
the final `extract` or `die` transaction must match its exact outcome,
receipt and learning choices. A's previous bridge still works without a
prepared intent. Once the final commit succeeds, E closes the run and
stores its receipt/XP/DB in one state update. The fourth death leaves only
the protected destroyed-vaccine state; old events cannot revive it.

## Read-only UI and C/A ACK-loss interfaces

`Gateway.inspect_slot(slot_path)` returns:

- `ok, revision, slot_id, generation, state, snapshot`
- `safe_to_resume, requires_user_action, reason`
- `checkpoint_present, technically_restorable`
- `mission_snapshot, reward_snapshot, terminal_snapshot`

`state`: `hub`, `vaccine_selection` or `active_mission_blocked`.
Even a fully checkpointed mission has **`safe_to_resume=false`** until
resume vs abandon policy is explicitly approved. `technically_restorable`
indicates only that the map seed/version and RNG cursor are present, not
that enemy positions, runtime effects or combat clock are saved. A must
extend this contract with D/B live-world state before enabling mid-combat
resume; do not infer it from the presence of a checkpoint alone.

`Gateway.reconcile_receipt(slot_path,vaccine_id,generation,run_id,receipt_id,offer_id="")`
is read-only. It returns C-compatible `receipt_committed` or
`receipt_definitely_absent`, and `reconciliation_complete`. Proof of
absence requires an intact, authenticated E save for the **same active
vaccine generation and run**. Closed/unknown runs, differently claimed
offers, destroyed vaccines, corrupt/future saves and ambiguous intent
**never** report a safely retryable absence. If definitely absent, retry
**the exact same receipt**, subject to A's event/callback policy; do not
reroll, select a new card or create an extra reward.

## Recovery and policy barriers

- A normal restart of an active mission is **not** an enemy death.
- Store interruptions use E's existing checked `state.json.intent`,
  `state.json.recovery` and `lifetime_fence.json`. Interrupted writes
  require offline reconciliation, **not** an implicit mission reset.
- `snapshot` is a deep copy, never a writable replacement for an E commit.
- E does not modify the original Python five files, existing Godot saves,
  A's pilot, C's RNG engine, D's world or shared workflows.
- **User/A decision required**: resume at exact combat state vs resume at
  safe checkpoint vs voluntarily abandon without clone loss; if abandoning,
  decide exact pending-reward retention before E implements a new transition.
  No option is enabled by this PR.
