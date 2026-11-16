"""Real-Tk smoke check for settings overlay and input isolation."""
from __future__ import annotations

import tkinter as tk
from types import SimpleNamespace

from neonvoid.game import Game


def key(game, name):
    game.on_key(SimpleNamespace(keysym=name))

def click(game, x, y):
    event = SimpleNamespace(x=x, y=y)
    game.on_mouse_down(event)
    game.on_mouse_up(event)


def main():
    root = tk.Tk()
    options = SimpleNamespace(selftest=1, fps=False, seed=42,
                              no_sound=True, volume=None, god=True)
    try:
        game = Game(root, options)
        assert game.state == "title"
        click(game, 80, 80)
        assert game.state == "title"
        click(game, 760, 620)
        assert game.settings_open
        assert len(game.settings_hits) == 6
        assert any("Home/End" in game.c.itemcget(item, "text")
                   for item in game.c.find_withtag("cfg"))
        # A secondary menu button must remain a secondary button after a
        # hover and subsequent frame render, even if the cursor vanishes.
        back_button = next(item for item, action in game._menu_buttons()
                           if action == "escape")
        x0, y0, x1, y1 = game.c.coords(back_button)
        game.cursor_on = True
        game.mx, game.my = (x0 + x1) / 2, (y0 + y1) / 2
        game.render()
        assert game.c.itemcget(back_button, "fill") == "#185064"
        assert float(game.c.itemcget(back_button, "width")) == 2
        game.cursor_on = False
        game.render()
        assert game.c.itemcget(back_button, "fill") == "#0a1826"
        assert float(game.c.itemcget(back_button, "width")) == 1
        game._tick_ui(0.3)
        game.render()
        assert game.c.coords(game.settings_rule)[2] > 1000
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
        assert game.c.itemcget(game.title_line, "state") == "hidden"
        key(game, "Escape")
        assert not game.settings_open
        key(game, "Return")
        assert game.state == "play"
        assert game.message_t == 0
        key(game, "p")
        assert game.paused
        click(game, 640, 470)
        assert game.settings_open
        assert game.paused
        game.render()
        key(game, "o")
        assert not game.settings_open
        assert game.paused
        click(game, 640, 412)
        assert not game.paused
        assert not game.mouse_down
        game.render()
        print("[gui-settings] title and pause settings passed")
    finally:
        root.destroy()


if __name__ == "__main__":
    main()
