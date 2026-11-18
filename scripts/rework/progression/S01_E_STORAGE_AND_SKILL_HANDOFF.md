# S01 E handoff: progression, receipts and fail-closed recovery

Scope: E-owned Godot 4 rework v1 only. **Not an approved interrupted-mission resume/abandon policy.** S01 issue #151; target integration branch `integration/rework-s01-playable-core-20261118`. This contract does not change GDD decisions.

## Authority and data isolation
- `Store.default_slot_path("nv-rework-main-v1")` and `Store.default_slot_path("nv-rework-qa-20261118")` are distinct slot directories under `user://rework/v1/`. The production and QA scene assignments are A-owned. E's `Model.new_slot` creates a Level 1 vaccine with 3 clones and **no** points, skills, plugins, or XP; the QA scene explicitly seeds a **separate** fixture only if its slot does not exist. Never reuse QA credentials/slot IDs in a production scene.
- Legacy Python, classic Godot, `save.json`, `settings.json`, `profile.json`, `antivirus_db.json`, `os_checkpoint.json` are not imported or touched by E. Existing v1 save schema (`state.json`, revisioned checksum, lifetime fence, intent/recovery) remains unchanged.
- E ACK (`transact_command().applied == true`) plus a later `snapshot()` readback is the only authority to show or execute a reward. On ACK uncertainty do not attempt a different receipt; use the read-only reconciliation methods.

## Skill discovery and B/A contract
- E accepts new skill IDs only when B's nine-entry **BALANCE_DRAFT** `SkillRegistry.definition(skill_id)` exists. Unknown historic IDs remain readable, but newly minted unknown IDs are denied.
- During an active mission `learn_skill` must include `run_id`, `skill_id`, `equip_on_discovery`, and `receipt_id = "skill:<run_id>:<hardware_id>:<skill_id>"`.
- `hardware_id` is one of CPU/GPU/RAM/SSD/HDD and must be supported by an **E-ACKed `checkpoint_region` or an E-ACKed `hardware_cleaned`** in that run. The second path covers legacy A synthetic acceptance fixtures which bypass map entry but commit a real cleanse fact. E derives the valid three-card skill pool from B `SkillOffer.candidates(saved_mission.seed, hardware_id, saved_vaccine.known_skills)`; it does not trust UI-provided card contents. One skill discovery per region per run is enforced using existing immutable E receipts. A may equip the new skill in that same ACK; ordinary mid-run `equip_skills` stays forbidden.
- Historical hub-only test setup may still invoke `learn_skill` for a valid B-registered ID; this is a harness capability, **not a production free-skill UI policy**. A must not expose an unrestricted hub skill-grant action to players. If that privilege needs to be removed altogether, A/B/E must first agree on synthetic QA injection contracts and migration compatibility.

## Read-only E APIs for A
- `Gateway.snapshot(slot_path)`: checksum/fence-validated immutable copy and revision.
- `Gateway.inspect_slot(slot_path)`: `hub`, `vaccine_selection`, or `active_mission_blocked`; includes checkpoint/rare/combat observation diagnostics. An active mission **never** reports `safe_to_resume=true`.
- **New:** `Gateway.diagnose_slot(slot_path)`: `ok`, `reason`, `revision`, `state`, `files.primary/recovery/backup/intent.status+revision`, `fence_status`, `lock_present`, `lock_owner_status`, `can_issue_command`, `safe_to_resume`, `automatic_repair_permitted=false`, `recovery_policy_approved=false`, `offline_recovery_hint`. Only reads metadata; does not expose lock tokens, repair, delete, restore, or decide interrupted mission results. `lock_owner_status=metadata_valid_process_not_verified` **does not** prove an owner process is dead.
- `Gateway.reconcile_receipt(slot_path, vaccine_id, generation, run_id, receipt_id[, offer_id])`: current-generation receipt proof; `can_retry_same_receipt` is advisory only when `reconciliation_complete` and `receipt_definitely_absent` are both true.
- `Gateway.reconcile_rare_grant(...)`: require the source receipt, derived rare receipt and immutable rare grant record in the same active mission.
- `Store.recover_interrupted_commit`, `Store.restore_backup`, `Store.release_orphan_lock_offline`: existing **explicit consent/offline** tools. They must not be called from automatic launcher/scene startup.

## Recovery decision requested from A / product owner
Current committed map seed/card offer and sampled B/D observation are **not** a complete deterministic combat replay. After restart with an active mission or an unresolved intent, block new game actions and surface read-only diagnosis. Do **not** auto-resume, auto-forfeit, rebuild clones, reset the vaccine, or discard pending XP. A should obtain explicit GDD approval for a user choice design, replay requirements, escrow/clone accounting and the behavior when evidence is missing. Existing terminal intent and receipt reconciliation can be inspected offline, but are not permission to finalize a run.


## Runtime death and reward ACK contract (S01 reaffirmed)
- **B emits combat facts** (enemy defeated, player died); **D stages world facts** (objective, cleanse, extraction/death). Neither event, HP=0, an in-memory cleared flag, nor the public ACK callback by itself is a durable grant or a spent clone.
- In production A must invoke `MissionCommitBridge.record_mission_xp`, `handle_world_fact`, `choose_and_commit_plugin`, or `commit_terminal_fact` and wait for `Gateway.transact_command` to return a committed E ACK. D's `acknowledge_world_fact`, `acknowledge_death`, and `acknowledge_extraction` are called **only with that ACK**; UI/battle return or C/B award effects are delayed until then. Preview `demo_mode=true` fake ACK must never be used by the production scene.
- On write rejection, ambiguous ACK, damaged envelope, revision/lock failure: freeze, do not finalize a world death or extraction, do not deliver XP/plugins, do not consume/refund clones. Reconcile the **same** receipt read-only via E; the existence of a committed death receipt does not by itself grant an unapproved battle resume policy.
- E-only regression `_test_s01_e_ack_death_reward_gate` drives the real A bridge against a unique synthetic slot. It asserts mission XP vs vaccine XP, cleanse pending vs banked reward, death consumes only one clone, duplicate receipt refusal, failure-before-write unchanged, and uncertain post-primary ACK reconciled from E storage.

## Test coverage and remaining gates
- CI scripts: `godot --headless --path . --script res://tests/rework/progression/progression_smoke.gd` and `godot --headless --path . --script res://tests/rework/persistence/persistence_smoke.gd` (already wired into `.github/workflows/godot-ci.yml` Linux/Windows matrix).
- Existing E suite: 1–4 deaths, clone lifetime fence, newest recovery copy, interrupted intent roll-forward requiring consent, duplicate receipts, source+rare atomic pair, corrupt slot, stale process lock and last-copy failure. S01 adds B skill ID and durable region/offer proof, single-skill-per-region, production-like synthetic profile separation and nonmutating diagnosis.
- Manual Windows acceptance and forced disk-write failures on real hardware are **not** demonstrated by headless CI. A's S01 final gate must validate game entry, complete normal extraction/restart, process kill, user-visible reasons, Windows export and launch, plus the no-migration invariant.
