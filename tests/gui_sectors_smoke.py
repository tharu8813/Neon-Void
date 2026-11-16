"""Live Tk integration across all sectors, boss confirmation and infection."""
from __future__ import annotations

import tkinter as tk
from types import SimpleNamespace

from neonvoid.constants import DT
from neonvoid.game import Game
from neonvoid.world.session import CPU_EXIT


GATES = {"cpu": (1200, 270), "memory": (1960, 780),
         "storage": (1200, 1320), "network": (440, 780)}


def main():
    root = tk.Tk()
    opts = SimpleNamespace(selftest=1, fps=False, seed=71,
                           no_sound=True, god=True, volume=None)
    try:
        game = Game(root, opts)
        game.state = "hub"
        game.c.delete("scr")
        game.p.x, game.p.y = 1580, 780
        game.interact_hub()
        assert game.world.room == "os"
        cleared = []
        game.events.subscribe("os_cleaned", lambda e: cleared.append(e))

        # Clear in a non-scripted order. Every entry requires proximity to a gate.
        for sector in ("memory", "network", "cpu", "storage"):
            game.p.x, game.p.y = GATES[sector]
            game.interact_hub()
            assert game.state == "play"
            assert game.world.active_sector == sector
            for wave in range(1, 6):
                game.spawn_queue.clear()
                game.enemies.clear()
                assert game.current_encounter is not None
                assert game.current_encounter.wave_index == wave
                if wave == 1:
                    before = game.infection.infection
                    game.step(DT)
                    assert game.infection.infection > before
                    game.spawn_queue.clear()
                    game.enemies.clear()
                if wave == 5:
                    boss = game.make_enemy(game.current_encounter.boss_kind)
                    game.damage_enemy(boss, boss.hp + 1)
                    assert game.boss_defeated
                game.check_wave(0.8)
                assert game.world.sector_waves[sector] == wave
                assert game.campaign.progress[sector.upper()].completed_waves == wave
                if wave < 5:
                    assert game.state == "upgrade"
                    game.pick_upgrade(0)
                else:
                    assert sector in game.world.sector_clean
                    assert game.state == "play"
            game.render()
            game.p.x, game.p.y = CPU_EXIT
            game.leave_sector()
            assert game.state == "hub"
        assert game.world.os_clean and game.campaign.os_clean
        assert len(cleared) == 1
        game.render()
        assert game.c.find_withtag("hub-world")
        assert not game.current_encounter
        print("[gui-sectors] 4 zones, 20 waves, 4 real boss kills, infection, OS CLEAN passed")
    finally:
        root.destroy()


if __name__ == "__main__":
    main()
