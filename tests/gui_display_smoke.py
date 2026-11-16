"""Real Tk smoke: aspect ratios, mapped mouse hit tests, fullscreen, font scale."""
from __future__ import annotations

import math
import time
import tkinter as tk
from types import SimpleNamespace
import tracemalloc

from neonvoid.constants import W, H
from neonvoid.game import Game


def real_mouse(game, x, y):
    px, py = game.c.viewport.to_screen(x, y)
    event = SimpleNamespace(x=px, y=py)
    game._screen_mouse_down(event)
    game._screen_mouse_up(event)


def main():
    root = tk.Tk()
    opts = SimpleNamespace(selftest=1, fps=False, seed=91,
                           no_sound=True, god=True, volume=None)
    try:
        game = Game(root, opts)
        game.c.set_viewport_size(960, 540)
        assert math.isclose(game.c.viewport.scale, 0.75)
        game.render()
        assert game.c.coords(game.hp_bg) == [24.0, 22.0, 264.0, 34.0]
        native = tk.Canvas.coords(game.c, game.hp_bg)
        assert math.isclose(native[2], 198, abs_tol=0.2)
        assert game.c.find_overlapping(24, 22, 264, 34)
        assert game.c.itemcget(game.hp_bg, "width")
        assert math.isclose(game.c.viewport.to_game(*game.c.viewport.to_screen(640, 360))[0],
                            640.0)

        # 4:3 and tall layout keep logical 1280x720 centered (black bars).
        game.c.set_viewport_size(960, 700)
        assert game.c.viewport.top > 0
        assert not game.c.viewport.contains(0, 0)
        assert len(game.c.find_withtag("viewport-letterbox")) == 4
        assert math.isclose(game.c.coords(game.hp_bg)[2], 264, abs_tol=0.001)
        # Settings overlay on the TITLE must still receive mapped clicks.
        real_mouse(game, 760, 620)
        assert game.settings_open
        assert len(game.settings_hits) == 6
        game.render()
        assert game.c.coords(game.settings_rule)[2] >= 261
        # Click an on-screen setting row via REAL pixel coords.
        real_mouse(game, 760, 236)
        assert game.settings_selected == 0
        real_mouse(game, 760, 236)
        assert game.settings.volume == 1.0
        game.change_setting(4)
        assert game.settings.display_mode == "fullscreen"
        game.change_setting(4)
        assert game.settings.display_mode == "borderless"
        game.change_setting(4)
        assert game.settings.display_mode == "windowed"
        game.on_key(SimpleNamespace(keysym="F11", state=0))
        assert game.settings.display_mode == "fullscreen"
        game.on_key(SimpleNamespace(keysym="F11", state=0))
        assert game.settings.display_mode == "windowed"
        game.on_key(SimpleNamespace(keysym="Return", state=0x0008))
        assert game.settings.display_mode == "borderless"
        game.on_key(SimpleNamespace(keysym="Return", state=0x0008))
        assert game.settings.display_mode == "windowed"
        game.change_setting(5, -1)
        assert (game.settings.window_width, game.settings.window_height) == (960, 540)
        assert game.c.coords(game.hp_bg)[0] == 24
        game._draw_settings()
        game.render()
        # A mapped click on the right option area selects no unrelated item.
        real_mouse(game, 760, 502)
        assert game.settings_selected == 5
        game.close_settings()

        # Normalized input reaches a real in-world terminal after resize.
        game.state = "hub"
        game.p.x, game.p.y = 820, 610
        game.interact_hub()
        assert game.database_open
        game.render()
        real_mouse(game, 640, 296)
        assert game.database_selected >= 0
        game._handle_database_key("Escape")
        # Pointer outside letterbox cannot shoot or click through the bars.
        game.c.set_viewport_size(1000, 700)
        game.state = "play"
        game.mouse_down = False
        game._screen_mouse_down(SimpleNamespace(x=0, y=0))
        assert not game.mouse_down
        game._screen_motion(SimpleNamespace(x=0, y=0))
        assert not game.cursor_on
        real_mouse(game, 640, 360)
        assert game.mouse_down is False  # released by helper
        assert game.mx >= 0 and game.my >= 0

        # Simulate Tk point-to-pixel scaling values corresponding to 100–200%
        # without claiming to reproduce physical Windows per-monitor DPI.
        original_dpi = root.tk.call("tk", "scaling")
        try:
            for dpi in (1.0, 1.25, 1.5, 2.0):
                root.tk.call("tk", "scaling", dpi)
                game.c.set_viewport_size(960, 540)
                game.render()
                self_px = game.c.viewport.to_screen(640, 360)
                self_logical = game.c.viewport.to_game(*self_px)
                assert all(math.isclose(actual, desired, abs_tol=0.01)
                           for actual, desired in zip(self_logical, (640, 360)))
                assert math.isclose(game.c.coords(game.hp_bg)[0], 24,
                                    abs_tol=0.001)
        finally:
            root.tk.call("tk", "scaling", original_dpi)

        # Stress repeated resizes: no new canvas objects/font spec leaks.
        game.render()
        baseline = len(game.c.find_all())
        for width, height in ((1920, 1080), (720, 405), (1120, 1000),
                              (1280, 720), (960, 700)):
            game.c.set_viewport_size(width, height)
            for _ in range(3):
                game.render()
            assert len(game.c.find_all()) == baseline
        start = time.perf_counter()
        tracemalloc.start()
        for _ in range(30):
            game.render()
        current, peak = tracemalloc.get_traced_memory()
        tracemalloc.stop()
        elapsed = 1000 * (time.perf_counter()-start) / 30
        print(f"[gui-display] viewport scale {game.c.viewport.scale:.3f}, "
              f"canvas {baseline}, render {elapsed:.2f} ms, "
              f"Python alloc peak {peak/1024/1024:.2f} MiB, "
              f"click & font/letterbox/fullscreen passed")
    finally:
        root.destroy()


if __name__ == "__main__":
    main()
