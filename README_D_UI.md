# P7 / Neon Void Godot Control UI (owner D)

Preview without game state: open res://scenes/ui/ui_preview.tscn in Godot.
It uses synthetic data and never reads/writes legacy save paths.

## Integration

Instantiate res://scenes/ui/ui_shell.tscn under the running game.
Do NOT edit the 3D camera or gameplay to attach the UI.
Call update_game_state(snapshot) with a read-only Dictionary. Fields:

- hp, maxhp, mana, maxmana, dash_charges, dash_max, pulse_cd, guard_cd
- stats {dmg, armor, armor_pen, rate, mana_regen, skill_power}
- sector, wave, infection, integrity, boss_hp, boss_maxhp, boss_name,
  status, show_fps, fps, software_levels, purify_uses, os_clean
- settings {volume, muted, show_fps, reduced_effects, display_mode,
  window_width, window_height, bindings: {up,down,left,right,dash,pause,
  skill_pulse,skill_guard,interact}}
- sector_progress {CPU,MEMORY,STORAGE,NETWORK}, checkpoint_valid,
  checkpoint_summary, database {antivirus_level,xp,xp_to_next,clean_runs,
  software_archive}, extraction_options, profile, error_message

Call set_gameplay_active(bool) and open_menu(name,payload). Rewards use
show_reward_cards(ids, software_levels), followed by the combat owner's
SoftwareSystem.claim_offer(index) and complete_reward_selection() on success.

Observe these signals. They are REQUESTS, not finished actions:
- action_requested(action,payload), pause_requested, resume_requested
- software_card_requested(index)
- setting_change_requested(key,value), binding_change_requested(action,key)

The gameplay owner validates and executes requests. Storage #61 validates
and saves settings/checkpoints. UI does not modify any save format or player
state, and it never uses the legacy persistence path. Custom cursor only hides
the OS cursor in gameplay; it restores the pointer when a menu opens.

## Screen coverage

title, pause, settings, controls, database, profile, diagnostics, deployment,
sectors, purification, os_clean, extraction, checkpoint, death, loadout,
error; the 3-card reward screen belongs to P6.

## Current blockers

P1 #65, P3 #56, P5 #58 and P6 #59 integration approval/CI/QA. Windows
DPI, pointer hit testing, Korean font metrics and screenshots require physical
Windows verification. Headless tests cannot establish those properties.
