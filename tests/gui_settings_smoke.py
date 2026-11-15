"""Real-Tk smoke check for settings overlay and input isolation."""
from __future__ import annotations

import tkinter as tk
from types import SimpleNamespace

from neonvoid.game import Game


def key(game, name):
    game.on_key(SimpleNamespace(keysym=name))


def main():
    root = tk.Tk()
    options = SimpleNamespace(selftest=1, fps=False, seed=42,
                              no_sound=True, volume=None, god=True)
    try:
        game = Game(root, options)
        assert game.state == "title"
        key(game, "o")
        assert game.settings_open
        assert len(game.settings_hits) == 4
        start_volume = game.settings.volume
        key(game, "Left")
        assert game.settings.volume < start_volume
        key(game, "Down")
        key(game, "Return")
        assert game.settings.muted
        key(game, "Down")
        key(game, "Return")
        assert game.settings.reduced_effects
        key(game, "Down")
        key(game, "Return")
        assert game.show_fps
        game.render()
        key(game, "Escape")
        assert not game.settings_open
        key(game, "Return")
        assert game.state == "play"
        key(game, "p")
        assert game.paused
        key(game, "o")
        assert game.settings_open
        assert game.paused
        game.render()
        key(game, "o")
        assert not game.settings_open
        assert game.paused
        key(game, "p")
        assert not game.paused
        game.render()
        print("[gui-settings] title and pause settings passed")
    finally:
        root.destroy()


if __name__ == "__main__":
    main()
