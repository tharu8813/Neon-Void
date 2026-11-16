"""End-to-end Tk checkpoint: same OS ID, completed waves and idempotent XP."""
from pathlib import Path
import tempfile
import tkinter as tk
from types import SimpleNamespace

from neonvoid.database_storage import AntivirusDatabaseStore
from neonvoid.game import Game
from neonvoid.os_checkpoint import CheckpointStore

OPTS = SimpleNamespace(selftest=1, seed=44, no_sound=True, god=True, fps=False, volume=None)


def create(cp, db):
    root = tk.Tk()
    game = Game(root, OPTS)
    game.database_store = db
    game.database = db.load()
    game.checkpoint_store = cp
    game.state = "hub"
    game.c.delete("scr")
    game._discover_checkpoint()
    return root, game


def press(game, key):
    game.on_key(SimpleNamespace(keysym=key))


def clear_waves(game, sector, gate, count):
    game.p.x, game.p.y = gate
    game.interact_hub()
    assert game.world.active_sector == sector
    for wave in range(1, count+1):
        game.spawn_queue.clear()
        game.enemies.clear()
        game.check_wave(0.8)
        assert game.world.sector_waves[sector] == wave
        assert game.state == "upgrade"
        assert game.checkpoint_store.load()["scene"] == "upgrade"
        game.pick_upgrade(0)
    game.leave_sector()


def main():
    with tempfile.TemporaryDirectory() as directory:
        cp = CheckpointStore(Path(directory) / "os_checkpoint.json")
        db = AntivirusDatabaseStore(Path(directory) / "antivirus_db.json")
        root, game = create(cp, db)
        try:
            game.p.x, game.p.y = 1580, 780
            game.interact_hub()
            run_id = game.campaign.run_id
            assert cp.load()["campaign"]["run_id"] == run_id
            clear_waves(game, "cpu", (1200, 270), 3)
            clear_waves(game, "memory", (1960, 780), 1)
            assert db.load().xp == 80
            old_levels = dict(game.levels)
            game.p.hp, game.p.mana = 73, 41
            game.infection.apply_infection(38)
            game.purify_uses = 2
            game.score = 420
            assert game._persist_checkpoint()
        finally:
            root.destroy()  # abrupt termination: do NOT close the run

        root, recovered = create(cp, db)
        try:
            assert recovered.checkpoint_pending is not None
            recovered.p.x, recovered.p.y = 1580, 780
            recovered.interact_hub()
            assert recovered.checkpoint_prompt_open
            recovered.render()
            assert recovered.c.find_withtag("resumeui")
            press(recovered, "Return")
            assert recovered.campaign.run_id == run_id
            assert not recovered.checkpoint_prompt_open
            assert recovered.world.sector_waves["cpu"] == 3
            assert recovered.world.sector_waves["memory"] == 1
            assert recovered.p.hp == 73 and recovered.p.mana == 41
            assert recovered.infection.infection == 38
            assert recovered.purify_uses == 2
            assert recovered.score == 420 and recovered.levels == old_levels
            recovered.sync_campaign_rewards()
            assert db.load().xp == 80
            recovered.p.x, recovered.p.y = 1200, 270
            recovered.interact_hub()
            assert recovered.wave == 4
            assert recovered.current_encounter.wave_index == 4
            recovered.leave_sector()
        finally:
            root.destroy()

        root, fresh = create(cp, db)
        try:
            fresh.p.x, fresh.p.y = 1580, 780
            fresh.interact_hub()
            assert fresh.checkpoint_prompt_open
            press(fresh, "Down")
            press(fresh, "Return")
            new_id = fresh.campaign.run_id
            assert new_id != run_id
            assert db.load().closed_runs[run_id] == "abandoned"
            assert db.load().xp == 80
            assert cp.load()["campaign"]["run_id"] == new_id
            fresh.p.x, fresh.p.y = 1200, 270
            fresh.interact_hub()
            fresh.spawn_queue.clear()
            fresh.enemies.clear()
            fresh.check_wave(0.8)
            assert fresh.state == "upgrade"
            choices = [item[0] for item in fresh.card_opts]
            assert cp.load()["pending_cards"] == choices
        finally:
            root.destroy()

        root, pending = create(cp, db)
        try:
            pending.p.x, pending.p.y = 1580, 780
            pending.interact_hub()
            press(pending, "Return")
            assert pending.campaign.run_id == new_id
            assert pending.state == "upgrade"
            assert [item[0] for item in pending.card_opts] == choices
            pending.pick_upgrade(0)
            assert pending.wave == 2
            assert pending.world.sector_waves["cpu"] == 1
            assert db.load().xp == 100
            _, result = db.close_run_once(run_id=new_id, status="abandoned")
            assert result.applied
        finally:
            root.destroy()

        # A death marker created before a terminal receipt can be committed
        # must not offer a free resurrection after process death.
        root, dead = create(cp, db)
        try:
            dead.p.x, dead.p.y = 1580, 780
            dead.interact_hub()
            death_id = dead.campaign.run_id
            dead.p.x, dead.p.y = 1200, 270
            dead.interact_hub()
            dead.p.hp = 0
            dead.state = "dead"
            assert dead._persist_checkpoint()
            assert cp.load()["scene"] == "dead"
        finally:
            root.destroy()
        root, death_prompt = create(cp, db)
        try:
            death_prompt.p.x, death_prompt.p.y = 1580, 780
            death_prompt.interact_hub()
            press(death_prompt, "Return")
            assert death_prompt.checkpoint_prompt_open
            assert death_prompt.campaign is None
            press(death_prompt, "Down")
            press(death_prompt, "Return")
            assert death_prompt.campaign.run_id != death_id
            assert db.load().closed_runs[death_id] == "dead"
            assert death_id not in db.load().processed_extractions
        finally:
            root.destroy()
        # The checkpoint of the NEW OS is still present and resumable.
        root, stale = create(cp, db)
        try:
            assert stale.checkpoint_pending is not None
            dead_new_id = stale.checkpoint_pending["campaign"]["run_id"]
            assert dead_new_id != death_id
            assert db.load().closed_runs[new_id] == "abandoned"
            _, result = db.close_run_once(run_id=dead_new_id, status="abandoned")
            assert result.applied
        finally:
            root.destroy()
        root, stale = create(cp, db)
        try:
            assert stale.checkpoint_pending is None
            assert cp.load() is None
            assert db.load().closed_runs[new_id] == "abandoned"
            assert db.load().xp == 100
        finally:
            root.destroy()

        # Normal window close suspends, it does not register an abandonment.
        root, graceful = create(cp, db)
        graceful.p.x, graceful.p.y = 1580, 780
        graceful.interact_hub()
        graceful_id = graceful.campaign.run_id
        assert cp.load()["campaign"]["run_id"] == graceful_id
        graceful.close_game()
        assert graceful_id not in db.load().closed_runs
        assert cp.load()["campaign"]["run_id"] == graceful_id

        # A corrupt checkpoint is never silently overwritten on startup.
        root, suspended = create(cp, db)
        try:
            assert suspended.checkpoint_pending is not None
            suspended.p.x, suspended.p.y = 1580, 780
            suspended.interact_hub()
            press(suspended, "Down")
            press(suspended, "Return")
            assert db.load().closed_runs[graceful_id] == "abandoned"
        finally:
            root.destroy()
        assert cp.load() is not None
        cp.path.write_bytes(b"checkpoint broken on purpose")
        root, corrupt = create(cp, db)
        try:
            assert corrupt.checkpoint_error is not None
            assert cp.path.read_bytes() == b"checkpoint broken on purpose"
            corrupt.p.x, corrupt.p.y = 1580, 780
            corrupt.interact_hub()
            assert corrupt.checkpoint_prompt_open
            press(corrupt, "Return")
            assert corrupt.campaign is not None
            assert corrupt.world.room == "os"
            preserved = list(Path(directory).glob("os_checkpoint.json.corrupt*"))
            assert len(preserved) == 1
            assert preserved[0].read_bytes() == b"checkpoint broken on purpose"
            print("[gui-checkpoint] crash/recovery, safe quit, death, "
                  "corrupt archive, OS restart and XP receipts passed")
        finally:
            root.destroy()


if __name__ == "__main__":
    main()
