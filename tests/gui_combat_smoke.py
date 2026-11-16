"""Real-Tk stat HUD, skill input, statuses and software choice smoke test."""
from __future__ import annotations

import tkinter as tk
from types import SimpleNamespace

from neonvoid.constants import DT
from neonvoid.content.upgrades import UP_BY_ID, apply_upgrade
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
        pause_key = game.settings.bindings["pause"]
        press(game, pause_key)
        press(game, "Tab")
        assert game.paused and game.loadout_open
        assert not game.c.find_withtag("loadout-item")
        press(game, "Escape")
        assert game.paused and not game.loadout_open
        press(game, pause_key)
        release(game, pause_key)
        assert not game.paused
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
        game.p.hp = 40
        apply_upgrade(game, "burn")
        game.open_upgrade()
        game.card_opts = [UP_BY_ID[uid] for uid in ("hull", "burn", "split")]
        game.build_upgrade_screen()
        game.render()
        names = [game.c.itemcget(item, "text") for item in game.c.find_withtag("scr")
                 if game.c.type(item) == "text"]
        assert any("소프트웨어 1장을 선택" in name for name in names)
        assert "40.0 → 60.0" in names
        assert "18% → 36%" in names
        assert "1 → 3" in names
        assert game.p.maxhp == 100 and game.p.hp == 40
        assert game.levels == {"burn": 1}
        game.on_mouse_down(SimpleNamespace(x=1025, y=673))
        assert game.loadout_open and game.state == "upgrade"
        items = game.c.find_withtag("loadout-item")
        assert [game.c.itemcget(item, "text") for item in items] == ["점화 프로토콜"]
        press(game, "1")
        game.on_mouse_down(SimpleNamespace(x=280, y=380))
        game.step(1)
        assert game.state == "upgrade" and game.wave == 1
        assert game.levels == {"burn": 1} and game.p.hp == 40
        game.on_mouse_down(SimpleNamespace(x=1050, y=650))
        assert not game.loadout_open and game.state == "upgrade"
        game.pick_upgrade(-1)
        assert game.state == "upgrade"
        x, y, right, bottom = game.cards[0]
        game.on_mouse_down(SimpleNamespace(
            x=(x + right) / 2, y=(y + bottom) / 2 + game.card_yoff[0]))
        game.render()
        assert game.wave == 2 and game.p.maxhp == 120 and game.p.hp == 60
        assert "외골격 Lv.1 설치 완료" in game.c.itemcget(game.banner_item2, "text")
        print("[gui-combat] skills, HUD, status, software screen passed")
    finally:
        root.destroy()


if __name__ == "__main__":
    main()
