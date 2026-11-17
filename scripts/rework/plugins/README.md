# Rework Plugin Library (C, issue #97)

This new isolated library does not wire legacy combat, UI, saves or project globals. A owns integration and CI; B owns damage and events; E owns durable learning and receipts.

## API

- plugin_offer_engine.gd: new(custom_entries, custom_settings); begin_mission(seed, levels); roll_offer(context); reroll_card(slot); choose_card(slot); dismiss_offer(); add_rerolls(amount); rarity_probabilities(context); offer_snapshot(); is_eligible(id); get_entry(id); get_catalog().
- Context includes cleared_hardware (0..5), difficulty_tier (nonnegative integer), luck_points (nonnegative float), all with provisional scaling in odds.proposal.v1.json.
- Exactly three independent rarity draws per screen; a single-slot reroll redraws its rarity and ID, with other cards and offer_id unchanged. Reroll can coincidentally return the same card.
- claim returns offer_id, receipt_id, plugin_id, rarity, level, learnable. Duplicate plugin upgrades level; the maxed plugin is excluded on subsequent rewards. Invalid claims do not spend a receipt.
- All rewards maxed => empty cards and no claim. Repeated roll_offer while active does not generate another screen. Independent RNG seed is supplied by A/E (do not use VFX RNG).

## Unconfirmed pity proposals

Only a settled 3-card screen increments one miss, not individual cards or rerolls. Seeing a legendary/mythic at any point within a screen, including after a reroll, clears pity on settlement; starting a mission resets pity. After five missed settled screens, the sixth gets more high rarity weight. An all-empty screen does not increase misses. Weights are eligible-rarity normalized to 100%; duplicate same-screen cards are provisionally allowed. All coefficients and reset boundaries require user review; none is approved by the GDD.

## B/E integration contract

B sends events with event_id, actor_id, target_id, damage_type, source_tags, proc_path, fixed_tick. C catalog exports immutable plugin IDs, level_effects, trigger, cooldown scope/seconds and proc_permissions. The pure plugin_proc_policy.gd mirrors B PR #100's effect_id|actor_id|target_id cycle signature: a repeat effect on a **different target** is legal, while revisiting the same effect+source+target ancestry is rejected. B's event FIFO, internal cooldown and duplicate collision checks remain authoritative. B/A must implement live effect dispatch, cooldown state, hit/DoT/crit evaluation, proc permissions and any projectile shape mixing. Empty source tag permissions are not a grant of extra proc rights.

E consumes choose_card receipt_id for an atomic exactly-once persisted selection and learns only common/rare (up to two NEW plugins on successful extraction), retaining level. Legendary/mythic mission-only; C writes no save. E owns storage/withdrawal/death, and the undecided returned-learned-plugin quota boundary must be resolved with A.

## Data

27 GDD concepts: 17 common, 7 rare, 3 legendary. Mythic definitions remain absent pending approval. Catalog includes confirmed concepts, known numbers, and distinctly tagged draft max levels and per-level upgrade texts; these definitions are not yet B-executable effects. Run standalone Godot 4 smoke with: godot --headless --path . --script res://tests/rework/plugins/plugin_reward_smoke.gd

## B PR #100 alignment review

B owns events.gd submit_root, submit_proc and drain. C must pass B's exact signature fields: effect_id, actor_id, target_id, tick, source_tags, trigger_kind, proc_path and icd_ticks. C policy uses the same signature semantics only for validation/preview and never runs a second event queue. B's allowed_proc_triggers list is the authority for derived attacks; C's data-only can_trigger_on_hit_effects=false is a conservative default pending per-plugin policy decisions. B accepts repeated effect IDs on **different targets** within one ancestry. Conversion of C trigger names to B event kind values is an integration review task for A/B/C, not implied executable code.
