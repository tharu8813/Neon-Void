"""Real-Tk stat HUD, skill input, statuses and software choice smoke test."""
from __future__ import annotations

import tkinter as tk
from types import SimpleNamespace

from neonvoid.constants import DT
from neonvoid.entities import Enemy
from neonvoid.game import Game


def press(g, key):
    g.on_key(SimpleNamespace(keysym=key))


def release(g, key):
    g.on_key_up(SimpleNamespace(keysym=key))


def main():
    root = tk.Tk()
    opts = SimpleNamespace(selftest=1, fps=False, seed=2, no_sound=True,
                           volume=None, god=True)
    try:
        game = Game(root, opts)
        game.new_game()
        assert game.state == "play"
        game.render()
        assert "LIVE STATS" in game.c.itemcget(game.stats_title, "text")
        game.p.mana = 15
        press(game, game.settings.bindings["skill_pulse"])
        assert game.p.pulse_cd == 0
        release(game, game.settings.bindings["skill_pulse"])
        game.p.mana = 100
        near = Enemy()
        near.hp = 100
        near.x, near.y = game.p.x + 30, game.p.y
        game.enemies.append(near)
        press(game, game.settings.bindings["skill_pulse"])
        assert game.p.mana == 65
        assert near.statuses and near.statuses[0].kind == "shock"
        press(game, game.settings.bindings["skill_pulse"])
        assert game.p.mana == 65
        release(game, game.settings.bindings["skill_pulse"])
        press(game, game.settings.bindings["skill_guard"])
        assert game.p.guard_t > 0 and game.p.mana == 40
        release(game, game.settings.bindings["skill_guard"])
        game.step(DT)
        game.render()
        assert "마나" in game.c.itemcget(game.stats_right, "text")
        assert "보호막" in game.c.itemcget(game.skill_guard_txt, "text")
        game.open_upgrade()
        game.render()
        names = [game.c.itemcget(item, "text") for item in game.c.find_withtag("scr")
                 if game.c.type(item) == "text"]
        assert any("소프트웨어 1장을 선택" in name for name in names)
        game.pick_upgrade(0)
        game.render()
        print("[gui-combat] skills, HUD, status, software screen passed")
    finally:
        root.destroy()


if __name__ == "__main__":
    main()
