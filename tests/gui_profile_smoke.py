"""Real Tk integration check for run abandonment and profile tracking."""
from __future__ import annotations

import tkinter as tk
from types import SimpleNamespace

from neonvoid.game import Game


def main():
    root = tk.Tk()
    opts = SimpleNamespace(selftest=1, fps=False, seed=100,
                           no_sound=True, volume=None, god=True)
    try:
        game = Game(root, opts)
        game.new_game()
        assert game.profile_tracker.active
        game.events.emit("enemy_killed", kind="boss", combo=6)
        game.new_game()  # Restart abandons the previous run without double-counting.
        assert game.profile.abandoned_runs == 1
        assert game.profile.boss_kills == 1
        assert game.profile.best_combo == 6
        assert game.profile_tracker.active
        game.close_game()  # Window close abandons the new run.
        assert game.profile.abandoned_runs == 2
        print("[gui-profile] restarts and window close recorded correctly")
    finally:
        try:
            root.destroy()
        except tk.TclError:
            pass


if __name__ == "__main__":
    main()
