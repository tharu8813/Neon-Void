# Hardware objective/environment rules, D-owned

Isolated proposal for #98, companion to PR #101's seeded planner and map, PR #103's physical hub/mainboard.

## Interface to B combat

`hardware_objectives.gd` creates an object via `Objectives.new(run_id, region_id)`; B is authoritative for validating kill/defense tick/dodge/purge/repair/interaction/transfer events and calls `apply_verified_event(objective_id, event_kind, unique_event_receipt, positive_amount)`. Each objective has a tunable threshold and kind in `SPECS`; all goals must complete before `boss_unlocked=true`.

`objective_completed(Dictionary)` emits exactly once with `schema:1, type:"objective_completed",run_id,region_id,objective_id,receipt_id`. D-to-D adapter should pass that to PR #101 `ExtractionState.confirm_objective()` without bypassing B validation. B then validates boss defeat and D calls `confirm_boss_defeated`. A handles scene transitions and related UI. **E alone awards rewards and persists transactions**.

The hardware specs are **draft gameplay placeholders**, NOT final balancing. CPU: defend 2400 verified 60Hz ticks, kill 10; GPU: disable 3 emitters, dodge 8 hazards; RAM: purge 4, verify 2; SSD: capture 3 nodes, transfer for 1800 ticks; HDD: repair 4, index 3. The target of 8–12 minutes per hardware is **not tested**.

## Interface to B/C/E environment

`environment_rules.gd.describe(region_plan,carried_plugins,seed)` turns planned environmental conditions into pure deterministic descriptors, **not effects**:
- `normal`: unchanged
- `mutation`: stage 1–5, prototype mutant chance/stat scaling and 1–2 ordinary/rare enemy plugin slots (C selection required)
- `stack_overflow`: prototype enemy spawn multiplier 1.6
- `tunnel_infection`: selected 1–2 **explicitly unlearned** plugin IDs (or fewer if none eligible); unknown learning status fails closed. Proposed suspend until exit, not yet approved by C/E. Only C/B apply to live build; E owns saved learned status.
- `spread`: large map; `initial`: small map. PR #101 map generator already supports these.
- Single environment per hardware remains the unconfirmed GDD co-occurrence policy. Do not silently combine effects.

## Tests and release gate

`godot --headless --path . --script res://tests/rework/world/hardware_rules_smoke.gd`

A owns CI wiring and must add this new smoke to Linux/Windows checks. Existing root/legacy/saves unchanged. Windows QA must validate gameplay feel and all five hardware encounters, plugin survival during tunnel infection, visible UI warnings, enemy spawn rates, goal pacing, and actual 8–12-minute target.
