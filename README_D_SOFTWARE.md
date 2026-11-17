# P6 / Software (owner D)

All 30 software definitions and effects mirror the legacy Python code. The
standalone SoftwareSystem provides roll_offers(3, sector_id, rng), claim_offer,
apply_software, restore_levels, boss_signature and award_boss_signature.

Tier weights: 1.0 / 0.42 / 0.13. Sector focus: 1.8x. If all 30 are capped,
reward selection returns three 'bonus' cards (+250 score each).

Combat owns damage application/score and calls into the module. The reward
picker emits card_selected; the inventory is a separate paginated scene.
No persistence is written. Call restore_boss_claims(clean_sectors) when restoring
from checkpoint to prevent duplicate signature rewards.

Limitations: The same seed does not result in identical Python and Godot RNG
bitstreams yet. Screenshots, mouse hit-testing, DPI and end-to-end combat parity
require #57/#58/#61 and real Windows QA.

Checks: python -m unittest tests.test_godot_software_parity
and godot --headless --path . --script res://tests/software_smoke.gd
