"""Real-Tk control rebinding, conflict, pause and movement smoke test."""
from __future__ import annotations

import tkinter as tk
from types import SimpleNamespace

from neonvoid.constants import DT
from neonvoid.game import Game


def key(game, keysym):
    game.on_key(SimpleNamespace(keysym=keysym))


def release(game, keysym):
    game.on_key_up(SimpleNamespace(keysym=keysym))


def main():
    root = tk.Tk()
    options = SimpleNamespace(selftest=1, fps=False, seed=42,
                              no_sound=True, volume=None, god=True)
    try:
        game = Game(root, options)
        key(game, "o")
        assert game.settings_open
        key(game, "Tab")
        assert game.settings_page == "controls"
        assert len(game.settings_hits) == 9
        assert any("Home/End" in game.c.itemcget(item, "text")
                   for item in game.c.find_withtag("cfg")
                   if game.c.type(item) == "text")

        key(game, "Return")
        assert game.rebinding_action == "up"
        key(game, "g")
        assert game.rebinding_action is None
        assert game.settings.bindings["up"] == "g"

        key(game, "Down")
        assert game.settings_selected == 1
        key(game, "Return")
        key(game, "a")  # already used for left
        assert game.rebinding_action == "down"
        assert "이미" in game.settings_notice
        key(game, "Escape")
        assert game.rebinding_action is None

        for _ in range(4):
            key(game, "Down")
        assert game.settings_selected == 5
        key(game, "Return")
        key(game, "k")
        assert game.settings.bindings["pause"] == "k"

        game.render()
        key(game, "o")
        assert not game.settings_open
        key(game, "Return")
        assert game.state == "play"

        key(game, "f")
        game.step_player(DT, DT)
        assert game.p.last_move_dir == (0.0, -1.0)
        release(game, "g")

        key(game, "p")
        assert not game.paused
        release(game, "p")
        key(game, "k")
        assert game.paused
        release(game, "k")
        key(game, "o")
        assert game.settings_open
        key(game, "Tab")
        key(game, "Delete")
        assert game.settings.bindings["pause"] == "p"
        assert game.settings.bindings["up"] == "w"
        key(game, "o")
        key(game, "p")
        assert not game.paused
        game.render()
        print("[gui-bindings] capture, validation, movement, pause and reset passed")
    finally:
        root.destroy()


if __name__ == "__main__":
    main()
