# Rework Plugin Library (C, issue #97)

This new isolated library does not wire legacy combat, UI, saves or project globals. A owns integration and CI; B owns damage and events; E owns durable learning and receipts.

## API

- plugin_offer_engine.gd: new(custom_entries, custom_settings); begin_mission(seed, levels); roll_offer(context); reroll_card(slot); choose_card(slot); dismiss_offer(); add_rerolls(amount); rarity_probabilities(context); offer_snapshot(); is_eligible(id); get_entry(id); get_catalog().
- Context includes cleared_hardware (0..5), difficulty_tier (nonnegative integer), luck_points (nonnegative float), all with provisional scaling in odds.proposal.v1.json.
- Exactly three independent rarity draws per screen; a single-slot reroll redraws its rarity and ID, with other cards and offer_id unchanged. Reroll can coincidentally return the same card.
- claim returns offer_id, receipt_id, plugin_id, rarity, level, learnable. For cross-mission unique receipts, A/E MUST pass a globally unique run_id as begin_mission's third argument; the default seed-based fallback is for local tests only. Duplicate plugin upgrades level; the maxed plugin is excluded on subsequent rewards. Invalid claims do not spend a receipt.
- All rewards maxed => empty cards and no claim; such a card cannot spend rerolls. Repeated roll_offer while active does not generate another screen. Independent RNG seed is supplied by A/E (do not use VFX RNG).

## Unconfirmed pity proposals

Only a settled 3-card screen increments one miss, not individual cards or rerolls. Seeing a legendary/mythic at any point within a screen, including after a reroll, clears pity on settlement; starting a mission resets pity. After five missed settled screens, the sixth gets more high rarity weight. An all-empty screen does not increase misses. Weights are eligible-rarity normalized to 100%; duplicate same-screen cards are provisionally allowed. All coefficients and reset boundaries require user review; none is approved by the GDD.

## B/E integration contract

B sends events with event_id, actor_id, target_id, damage_type, source_tags, proc_path, fixed_tick. C catalog exports immutable plugin IDs, level_effects, trigger, cooldown scope/seconds and proc_permissions. The pure plugin_proc_policy.gd mirrors B PR #100's effect_id|actor_id|target_id cycle signature: a repeat effect on a **different target** is legal, while revisiting the same effect+source+target ancestry is rejected. B's event FIFO, internal cooldown and duplicate collision checks remain authoritative. B/A must implement live effect dispatch, cooldown state, hit/DoT/crit evaluation, proc permissions and any projectile shape mixing. Empty source tag permissions are not a grant of extra proc rights.

E consumes choose_card receipt_id for an atomic exactly-once persisted selection and learns only common/rare (up to two NEW plugins on successful extraction), retaining level. Legendary/mythic mission-only; C writes no save. E owns storage/withdrawal/death, and the undecided returned-learned-plugin quota boundary must be resolved with A.

## Data

27 GDD concepts: 17 common, 7 rare, 3 legendary. Mythic definitions remain absent pending approval. Catalog includes confirmed concepts, known numbers, and distinctly tagged draft max levels and per-level upgrade texts; these definitions are not yet B-executable effects. Run standalone Godot 4 smoke with: godot --headless --path . --script res://tests/rework/plugins/plugin_reward_smoke.gd

## B PR #100 alignment review

B owns events.gd submit_root, submit_proc and drain. C must pass B's exact signature fields: effect_id, actor_id, target_id, tick, source_tags, trigger_kind, proc_path and icd_ticks. C policy uses the same signature semantics only for validation/preview and never runs a second event queue. B's allowed_proc_triggers list is the authority for derived attacks; C's data-only can_trigger_on_hit_effects=false is a conservative default pending per-plugin policy decisions. B accepts repeated effect IDs on **different targets** within one ancestry. Conversion of C trigger names to B event kind values is an integration review task for A/B/C, not implied executable code.

## Per-plugin chained hit permissions (data-only proposals)

Catalog proc_permissions now has explicit allowed_proc_triggers and can_trigger_on_hit_effects per plugin. GDD permits legal chain lightning -> explosion -> disassembly; C proposes opt-ins for chain lightning, hit explosion, 4-shot proc volley, rare extra bullet and laser. All others default deny; **the specific opt-in list is an unapproved proposal for A/B review**. Exact GDD on-hit effectiveness values are machine-readable: four-shot extra bullets 70%, rare extra bullet 80%, laser 70%. B executes and enforces these semantics only after explicit mapping; no automatic damage is introduced by this PR.


## Live-effect phase 2 (stacked PR after #102)

- data/rework/plugins/effects.proposal.v1.json defines an executable operation recipe for every 27 original plugin IDs; numbers absent from GDD are clearly marked proposals.
- plugin_effect_runtime.gd: configure(levels, seed), derived_stats(base,tick), process(event,world,sink), set_plugin_level() after E ack. It de-duplicates event IDs, tracks target ICDs and applies state only if the full authoritative sink batch succeeds. B retains its event queue and damage rules.
- plugin_b_combat_sink.gd: B damage.gd.calculate/apply_damage and statuses.gd.apply/tick/modified_stats only when B is installed by A. Uses cloned world dictionaries and publishes atomically. World input keys: actor_stats, targets[id]={health,armor,magic_resist}, target_statuses[id], target_positions[id]=Vector2/Vector3, optional lightning_base_reach. Output includes generated_events, projectiles, projectile_transforms, buffs, knockbacks, actor_shield and pending_progression_requests.
- Event inputs: event_id, kind, actor_id, target_id, tick (60Hz), source_tags, proc_path, allowed_proc_triggers; kinds on_hit, on_crit, on_hit_proc, before_damage, damage_dealt, damage_taken, skill_cast, skill_damage, dash, blink, leap, teleport, reveal, basic_attack, kill, health_threshold, plugin_acquired, tick. All source IDs require B/A mapping before use in live scene.
- plugin_offer_view.gd exposes read-only UI metadata (name, rarity style, current/next level, levels and effect, rerolls). A owns Control scene wiring.
- SAFETY CHANGE: choose_card(slot) now PREPARES a receipt only. confirm_card(receipt_id,{applied:true,receipt_id}) must follow confirmed E persistence. False/unknown ack freezes the screen, does not grant a plugin, and requires E-verified reconciliation. A #107's current integration bridge calls E after C prepare but must call C confirm_card afterward. plugin_reward_coordinator.gd provides optional direct E Gateway helper.
- GDD fixed numbers retained: periodic shield 30s, low HP 60s ICD, dash speed 2s, lightning/explosion per-target 3s, four on-hit procs trigger four shots at 70% on-hit efficiency, extra rare bullet 80%, laser 70%, execute below enemy HP 50% with 15% bonus, disassembly unlimited stacking. All other draft numbers remain configurable.

### Tests and dependencies

Existing A-owned Godot CI invokes tests/rework/plugins/plugin_reward_smoke.gd on Windows and Linux. That script now also runs plugin_effect_assertions.gd. Standalone runner: tests/rework/plugins/plugin_live_effects_smoke.gd. C-only branch tests exercise effects using an injected fake sink and E prepare/ack failure handling. When mounted on A aggregate with B, same suite additionally uses B's real damage/status implementation. Never equate C-only CI with deployed 3D combat.

A remains responsible for scene/world/projectile spawning, physics, source event registration, buff/shield consumption, camera and animations. B must review event kind/proc permissions, range and status ordering. E owns real saves, learned plugins, rare auto-award receipts, run resume and ack-loss recovery. C's rare_reward_request is not yet a durable E reward. Several attack combinations (laser plus boomerang) and actual mythic effects require design approval. Do not merge until A confirms these dependencies and manual Windows QA.
