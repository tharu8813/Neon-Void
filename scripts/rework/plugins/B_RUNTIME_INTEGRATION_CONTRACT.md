# C/B production combat and E reward state contract (PR #117)

Status: DRAFT. C owns scripts/rework/plugins/ and matching plugin tests. A owns the live scene, B owns damage/status/event/physics, E owns durable transactions.

## B production adapter (not a parallel damage engine)
C plugin_b_live_runtime.gd subclasses B combat_runtime.gd. Its _resolve_event calls super._resolve_event and enqueues proc children through B bus.submit_proc. It uses B _spawn / _step_projectiles / _retire, and passive stats are injected into B _effective_stats. No B or A-owned files were edited.

A integration order: instantiate the new C subclass ONLY in opt-in rework scene; use normal B register_actor for vaccine and enemies; call attach_plugins(actor_id, E_verified_levels, seed). Drive B step_fixed(inputs) at 60Hz and render B snapshots/projectiles/notices. After A bridge choose_and_commit_plugin returns E-success and C confirm_card, call acknowledge_plugin_level once. On pending_e_rewards, use C coordinator.commit_random_rare with stable event ID and E receipt before showing reward. Death/XP receipts remain with A/E.

## Formal event mapping
- B damage with positive actual_damage => C on_hit and damage_dealt; B critical => C on_crit, same physical hit, ICD stops duplicate triggers.
- B damage from skill => C skill_damage. B damage from plugin => C on_hit_proc subject to B parent permissions.
- B basic_attack, dash, skill_cast, kill, damage_taken, periodic => corresponding C triggers (periodic maps to tick).
- B event work quota delays, not drops, outstanding events. B ancestry signature (effect ID, actor, target) allows legal different-target hops while blocking cyclic repeats.

## Measured verification boundary
Linux+Windows automatic tests iterate all 27 and use actual B combat_runtime and event FIFO. C Draft #125 updated the independent production-B subclass to use B's real damage_absorber; **26/27** now cause verifiable B actor HP/stat/status/position, actual HP mitigation or projectile state changes. The remaining 1 is the E-owned rare receipt, verified in separate real E transaction tests. This DOES NOT assert 27 playable in the A CPU scene.
Shields: nv_c_periodic_shield, nv_c_skill_shield and nv_r_dash_shield now prevent actual HP loss through B's pre-damage absorption hook, matching A's **provisional** shield-first implementation; consumed shields cannot be reused. Shield stacking/expiry, interaction with invulnerability/DoT/crit/vamp/death and balance are not final. nv_c_random_rare is E transaction, not a B attack; source+rare pair is granted once by E, never by C.
Pending A/B review: combined laser+boomerang shapes, boss control immunity, shield vs crit/armor/vamp/DoT ordering, physical CPU collision, event chain synergy, visual/audio and Windows in-game QA. Mythic proposals remain unapproved.

## E persistence / resume handoff
C plugin_offer_engine exports export_mission_state() with version, run ID, seed, JSON-safe decimal string RNG state (full signed 64-bit), levels, per-screen pity, reroll budget, offer sequence, three-card pending offer and prepared receipt.
restore_mission_state(packet, E_levels) validates a verified E loadout and freezes restored pending receipts for ledger reconciliation. A/E must atomically store the complete packet with E mission state and update it on rerolls/settled screens. E currently only reserves pity_counter and rerolls_left without full C RNG/offer atomicity, so safe real pilot resume is NOT done; do not write an independent C save file.

## Tests
Run the existing Windows+Linux CI plugin_reward_smoke.gd which now imports plugin_b_live_cases.gd; measure actual B events, HP, status, homing/laser/boomerang physics, p95/event budgets in later physical QA. A alone owns review and merges; do not automatically merge this draft.

## C/E atomic random-rare reconciliation (C Draft #125)

As of A #121, selecting `nv_c_random_rare` creates **two E receipts in one Store transaction**: the source-card receipt and `<source-receipt>:rare:0`, recorded in `mission.environment_rolls.e_rare_bonus_v1`. E alone chooses the eligible canonical rare and authorizes its level. C no longer draws or pays a secondary random rare.

- `plugin_reward_coordinator.commit_choice` uses the E source-card transaction; for this source it rereads the durable E snapshot, validates the pair, and freezes any unverified ACK rather than confirming the C offer.
- `inspect_atomic_rare(E_snapshot, source_receipt, vaccine_id, generation, run_id)` only returns a display-safe result if both receipts, policy, catalog rarity, authoritative inventory and generation/run identity match. Never build a reward UI directly from an `on_hit`, `plugin_acquired`, or `rare_reward_notice` event.
- `apply_verified_rare(runtime, offers, E_snapshot, source_receipt, ...)` synchronizes the authoritative **next** level once and returns `already_synced` for a duplicate ACK. Stale or skipped levels require a fresh complete E snapshot and A-controlled resynchronization; this function deliberately does not silently backfill multiple levels.
- Legacy `commit_random_rare` fails closed; `plugin_b_live_runtime.pending_e_rewards` is deprecated and must not be used to trigger grants.
- A pilot currently has its own E inspection and UI path. A may adopt this C read-only helper after reviewing compatibility. C cannot modify A pilot scene or E schema directly.
- The baseline single rare on source acquisition follows the provisional E #121 implementation. **Additional Lv2 count, alternate exhausted-pool fallback, and a full crash-resume policy are not approved**; C does not invent them.

### Remaining interface requests to B/A/E

1. **B shields (3 plugins):** C #125 tested actual HP mitigation for periodic/skill/dash shields using B's existing damage_absorber callback, increasing C-only B coverage from **23/27 to 26/27**. B/A still must approve stacking, expiry, crit, armor, DoT, life-steal, invulnerability and fatal-hit sequencing before calling the provisional policy final. A must not attach both C's B subclass and B's CAdapter to a single actor, since that would duplicate triggers.
2. **A scene:** instantiate the C production B subclass in the real CPU scene; use E-confirmed receipts for C/B levels and recovery UI; no additional C-side bonus dispatch.
3. **E restore:** atomically persist C RNG/cards/rerolls/pity packet with E mission checkpoints; reconcile prepared receipts before showing an interrupted offer. C's export/restore exists but does not write or independently trust a save.
4. **B/A projectile composition and bosses:** decide laser+boomerang precedence and boss control exemptions; preserve valid multi-hop procs while blocking cycles.
