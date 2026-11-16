"""Real Tk + durable DB: 4-sector clean, one extraction and redeployment."""
from __future__ import annotations

import tempfile
from pathlib import Path
import tkinter as tk
from types import SimpleNamespace

from neonvoid.content.upgrades import UP_BY_ID
from neonvoid.database_storage import AntivirusDatabaseStore, save_database
from neonvoid.game import Game
from neonvoid.metaprogression import base_stats_for_level, MutationResult
from neonvoid.world.session import CPU_EXIT

GATES = {"cpu": (1200, 270), "memory": (1960, 780),
         "storage": (1200, 1320), "network": (440, 780)}


def main():
    root = tk.Tk()
    opts = SimpleNamespace(selftest=1, fps=False, seed=22,
                           no_sound=True, god=True, volume=None)
    try:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "antivirus_db.json"
            game = Game(root, opts)
            game.database_store = AntivirusDatabaseStore(path)
            game.database = game.database_store.load()
            game.state = "hub"
            game.c.delete("scr")

            # The database is an in-world terminal, not a title menu.
            game.p.x, game.p.y = 820, 610
            game.interact_hub()
            assert game.database_open
            game.render()
            assert game.c.find_withtag("dbui")
            game.on_key(SimpleNamespace(keysym="Return"))
            assert game.starting_software_id is None
            assert not game.database_open

            game.p.x, game.p.y = 1580, 780
            game.interact_hub()
            assert game.world.room == "os"
            run_id = game.campaign.run_id
            assert game.p.maxhp == 100
            # Four zones in an arbitrary order, with durable XP receipts.
            for sector in ("network", "memory", "cpu", "storage"):
                game.p.x, game.p.y = GATES[sector]
                game.interact_hub()
                assert game.world.active_sector == sector
                for wave in range(1, 6):
                    game.spawn_queue.clear()
                    game.enemies.clear()
                    if wave == 5:
                        boss = game.make_enemy(game.current_encounter.boss_kind)
                        game.damage_enemy(boss, boss.hp * 4 + 1)
                        assert game.boss_defeated
                    game.check_wave(0.8)
                    assert game.world.sector_waves[sector] == wave
                    if wave < 5:
                        game.pick_upgrade(0)
                game.p.x, game.p.y = CPU_EXIT
                game.leave_sector()

            assert game.world.os_clean
            assert game.campaign.os_clean
            assert game.profile_tracker.active
            assert game.levels
            completed_run_software = dict(game.levels)
            before = game.database_store.load()
            assert before.xp == 4 * (5*20 + 75 + 50) + 200
            assert before.antivirus_level > 1
            assert len(before.awarded_xp_ids) == 29
            game.sync_campaign_rewards()
            assert game.database_store.load().xp == before.xp
            game.p.x, game.p.y = 1200, 780
            game.interact_hub()
            assert game.extraction_open
            assert game.extraction_options
            game.render()
            assert game.c.find_withtag("extractui")
            software_id = game.extraction_options[0]
            assert software_id in completed_run_software
            game.on_key(SimpleNamespace(keysym="Return"))
            assert not game.extraction_open
            assert game.state == "hub" and game.world.room == "base"
            assert game.profile.extracted_runs == 1
            assert game.profile_tracker.active is False
            assert game.extraction_fx_t > 0
            game.render()
            assert game.c.find_withtag("extraction-vfx")
            archived = game.database_store.load()
            assert archived.software_archive == {software_id: 1}
            assert archived.processed_extractions == {run_id: software_id}
            assert archived.closed_runs[run_id] == "extracted"

            # A replay of the same request must never award two software copies.
            _, double = game.database_store.commit_extraction_once(
                run_id=run_id,
                clean_sector_ids={"cpu_core", "memory_sector",
                                  "storage_vault", "network_grid"},
                software_levels=completed_run_software,
                selected_software_id=software_id)
            assert not double.applied and double.reason == "already_extracted"
            assert game.database_store.load().software_archive == {software_id: 1}

            # Starting software is chosen at the base terminal, one copy only.
            game.p.x, game.p.y = 820, 610
            game.interact_hub()
            game.on_key(SimpleNamespace(keysym="Down"))
            game.on_key(SimpleNamespace(keysym="Return"))
            assert game.starting_software_id == software_id
            game.p.x, game.p.y = 1580, 780
            game.interact_hub()
            assert game.campaign.run_id != run_id
            stats = base_stats_for_level(archived.antivirus_level)
            assert game.p.maxhp == stats["maxhp"]
            assert game.p.st["dmg"] == stats["dmg"] + (
                4.0 if software_id == "power" else 0.0)
            assert game.p.st["armor"] >= stats["armor"]
            assert game.levels == {software_id: 1}
            assert UP_BY_ID[software_id][4] >= 1

            # Integrity collapse forces emergency extraction, not player death.
            second_run = game.campaign.run_id
            game.p.x, game.p.y = GATES["cpu"]
            game.interact_hub()
            assert game.state == "play"
            game.infection.os_integrity = 0.0
            game.step(1 / 60)
            assert game.state == "hub" and game.world.room == "base"
            assert game.campaign is None
            assert game.p.hp > 0
            after = game.database_store.load()
            assert after.xp == archived.xp
            assert after.closed_runs[second_run] == "abandoned"
            assert after.software_archive == {software_id: 1}
            assert game.profile.extracted_runs == 1

            # Rebuild valid CLEAN proof without cheating the 29 receipt checks.
            # Simulate a rare finished run that no longer owns any software.
            game.p.x, game.p.y = 1580, 780
            game.interact_hub()
            assert game.world.room == "os"
            third_run = game.campaign.run_id
            for sid in ("CPU", "MEMORY", "STORAGE", "NETWORK"):
                game.campaign.enter_sector(sid)
                for wave in range(1, 6):
                    encounter = game.campaign.start_wave()
                    assert encounter.wave_index == wave
                    assert game.campaign.clear_wave(
                        hostiles_remaining=0, boss_defeated=(wave == 5))
                game.campaign.leave_sector()
                game.world.sector_waves[sid.lower()] = 5
                game.world.sector_clean.add(sid.lower())
            assert game.world.os_clean
            assert game.database_store.load().xp == archived.xp + 1100
            game.levels.clear()
            game.p.x, game.p.y = 1200, 780
            game.open_extraction_terminal()
            assert game.salvage_open and not game.extraction_open
            assert game.salvage_options
            game.render()
            assert game.c.find_withtag("salvageui")
            recovered_id = game.salvage_options[0]
            game.on_key(SimpleNamespace(keysym="Return"))
            assert game.salvage_claimed
            assert game.levels == {recovered_id: 1}
            assert game.extraction_open and not game.salvage_open

            # Independently test the fully saturated archive. This must NOT
            # invent a C extraction reward or overflow a saved module cap.
            game.on_key(SimpleNamespace(keysym="Escape"))
            full = game.database_store.load()
            full.software_archive = {
                uid: item[4] for uid, item in UP_BY_ID.items()}
            assert save_database(full, path)
            game.open_extraction_terminal()
            assert game.salvage_open
            assert not game.salvage_options
            game.on_key(SimpleNamespace(keysym="Return"))
            assert game.world.room == "base"
            assert third_run in game.database_store.load().closed_runs
            assert game.database_store.load().closed_runs[third_run] == "abandoned"
            assert third_run not in game.database_store.load().processed_extractions

            # C warning #1: a failed close receipt is reported and retried,
            # and on persistent failure the OS run stays alive for recovery.
            game.p.x, game.p.y = 1580, 780
            game.interact_hub()
            fourth_run = game.campaign.run_id
            real_store = game.database_store

            class FailingCloseStore:
                def __init__(self, upstream):
                    self.upstream = upstream
                    self.calls = 0

                def close_run_once(self, **kwargs):
                    self.calls += 1
                    return (self.upstream.load(),
                            MutationResult(False, "save_failed"))

            fail_store = FailingCloseStore(real_store)
            game.database_store = fail_store
            assert game.return_to_base(abandon=True) is False
            assert fail_store.calls == 3
            assert game.world.room == "os" and game.campaign.run_id == fourth_run
            assert game.profile_tracker.active
            assert game.database_close_error == "save_failed"
            assert fourth_run not in real_store.load().closed_runs

            game.database_store = real_store
            assert game.return_to_base(abandon=True) is True
            assert game.database_close_error is None
            assert game.world.room == "base"
            assert real_store.load().closed_runs[fourth_run] == "abandoned"
            print("[gui-extraction] CLEAN, 29 receipts, salvage/full archive, "
                  "retriable failed close, VFX, OS collapse, next OS passed")
    finally:
        root.destroy()


if __name__ == "__main__":
    main()
