"""Real Tk integration check for run abandonment and profile tracking."""
from __future__ import annotations

import tkinter as tk
from types import SimpleNamespace

from neonvoid.constants import DT
from neonvoid.game import Game
from neonvoid.profile import RunRecord


def terminal_text(game, tag="profileui"):
    return [game.c.itemcget(item, "text") for item in game.c.find_withtag(tag)
            if game.c.type(item) == "text"]


def main():
    root = tk.Tk()
    opts = SimpleNamespace(selftest=1, fps=False, seed=100,
                           no_sound=True, volume=None, god=True)
    try:
        game = Game(root, opts)
        game.state = "hub"
        game.c.delete("scr")
        game.p.x, game.p.y = 820, 1000
        game.settings.bindings["interact"] = "g"
        game.on_key(SimpleNamespace(keysym="g"))
        assert game.profile_open
        assert "아직 전투 기록이 없습니다" in terminal_text(game)
        game.on_key(SimpleNamespace(keysym="d"))
        game.step(DT)
        assert game.p.x == 820
        game.on_mouse_down(SimpleNamespace(x=955, y=605))
        assert not game.profile_open and not game.keys

        game.new_game()
        assert game.profile_tracker.active
        game.events.emit("enemy_killed", kind="boss", combo=6)
        game.new_game()  # Restart abandons the previous run without double-counting.
        assert game.profile.abandoned_runs == 1
        assert game.profile.boss_kills == 1
        assert game.profile.best_combo == 6
        assert game.profile_tracker.active
        assert game.return_to_base(abandon=True)
        for i in range(18):
            status = ("dead", "abandoned", "extracted")[i % 3]
            game.profile.record_run(RunRecord(status=status, score=1000 + i,
                                              duration_seconds=125.9))
        records = game.profile.recent_runs.copy()
        game.p.x, game.p.y = 820, 1000
        game.on_key(SimpleNamespace(keysym="g"))
        assert game.profile_open and game.profile_page == 0
        rows = terminal_text(game, "profile-row")
        assert len(rows) == 7 * 7 and rows[:2] == ["철수 성공", "1,017"]
        assert "02:05" in rows
        game.on_mouse_down(SimpleNamespace(x=770, y=605))
        assert game.profile_page == 1
        assert terminal_text(game, "profile-row")[1] == "1,010"
        game.on_key(SimpleNamespace(keysym="Right"))
        game.on_key(SimpleNamespace(keysym="Right"))
        assert game.profile_page == 2
        assert len(terminal_text(game, "profile-row")) == 6 * 7
        game.on_key(SimpleNamespace(keysym="g"))
        assert not game.profile_open and not game.c.find_withtag("profileui")
        assert game.profile.recent_runs == records

        abandoned_before = game.profile.abandoned_runs
        game.new_game()
        game.close_game()  # Window close abandons the new run.
        assert game.profile.abandoned_runs == abandoned_before + 1
        print("[gui-profile] run history, pagination, restarts and window close passed")
    finally:
        try:
            root.destroy()
        except tk.TclError:
            pass


if __name__ == "__main__":
    main()
