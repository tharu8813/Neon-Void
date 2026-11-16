"""Real-Tk world traversal, CPU combat, retreat and completion smoke."""
from __future__ import annotations

import tkinter as tk
from types import SimpleNamespace

from neonvoid.constants import DT
from neonvoid.game import Game
from neonvoid.world.session import CPU_EXIT


def press(game, name):
    game.on_key(SimpleNamespace(keysym=name))


def release(game, name):
    game.on_key_up(SimpleNamespace(keysym=name))


def main():
    root = tk.Tk()
    opts = SimpleNamespace(selftest=1, fps=False, seed=5, no_sound=True,
                           god=True, volume=None)
    try:
        game = Game(root, opts)
        # Headless selftest retains legacy title; exercise the real hub state.
        game.state = "hub"
        game.c.delete("scr")
        assert game.world.room == "base"
        game.render()
        assert game.c.find_withtag("hub-world")
        x, y = game.p.x, game.p.y
        press(game, "d")
        for _ in range(35):
            game.step(DT)
        release(game, "d")
        assert game.p.x > x
        assert game.p.y == y

        # Settings are reachable without starting a run.
        press(game, "o")
        assert game.settings_open
        game.render()
        press(game, "Escape")
        assert not game.settings_open

        game.p.x, game.p.y = 1580, 780
        press(game, game.settings.bindings["interact"])
        assert game.world.room == "os" and game.state == "hub"
        assert game.profile_tracker.active
        game.render()
        assert game.c.find_withtag("hub-world")
        game.p.x, game.p.y = 1200, 270
        press(game, game.settings.bindings["interact"])
        assert game.state == "play" and game.world.active_sector == "cpu"
        assert game.wave == 1
        game.render()
        assert game.c.find_withtag("sector-gate")

        # Leaving an unfinished wave resets only the unfinished encounter.
        game.p.x, game.p.y = CPU_EXIT
        press(game, game.settings.bindings["interact"])
        assert game.state == "hub" and game.world.active_sector is None
        assert game.world.sector_waves["cpu"] == 0
        assert not game.spawn_queue and not game.enemies
        game.p.x, game.p.y = 1200, 270
        press(game, game.settings.bindings["interact"])
        assert game.wave == 1

        for wave in range(1, 6):
            game.spawn_queue.clear()
            game.enemies.clear()
            game.check_wave(0.8)
            assert game.world.sector_waves["cpu"] == wave
            if wave < 5:
                assert game.state == "upgrade"
                game.pick_upgrade(0)
                assert game.wave == wave + 1
            else:
                assert game.state == "play"
                assert "cpu" in game.world.sector_clean
                assert not game.world.os_clean
        game.check_wave(1)
        assert game.world.sector_waves["cpu"] == 5

        game.p.x, game.p.y = CPU_EXIT
        press(game, game.settings.bindings["interact"])
        assert game.state == "hub" and game.world.room == "os"
        game.p.x, game.p.y = 1960, 780
        game.interact_hub()
        assert game.state == "hub"
        assert game.world.active_sector is None
        assert "연결 대기" in game.world_notice
        game.render()
        print("[gui-world] hub, settings, deploy, CPU retreat/5 waves and sealed gates passed")
    finally:
        root.destroy()


if __name__ == "__main__":
    main()
