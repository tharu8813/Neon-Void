"""Live Tk integration across all sectors, boss confirmation and infection."""
from __future__ import annotations

import tkinter as tk
from types import SimpleNamespace
from unittest.mock import patch

from neonvoid.constants import DT
from neonvoid.content.virus_catalog import ENCRYPTION_PULSE_RADIUS
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
        game.render()
        assert "CPU CORE" in game.c.itemcget(game.hub_prompt, "text")
        assert "위험 2/5" in game.c.itemcget(game.hub_nodes["cpu"]["caption"], "text")
        game.p.x, game.p.y = 1200, 1050
        game.interact_hub()
        assert game.purify_uses == 3
        game.infection.apply_infection(40)
        game.infection.os_integrity = 95
        game.interact_hub()
        assert game.purify_uses == 2
        assert game.infection.infection == 8 and game.infection.os_integrity == 100
        game.settings.bindings["interact"] = "g"
        cleared = []
        game.events.subscribe("os_cleaned", lambda e: cleared.append(e))

        # Clear in a non-scripted order. Every entry requires proximity to a gate.
        for sector in ("cpu", "network", "memory", "storage"):
            game.p.x, game.p.y = GATES[sector]
            game.interact_hub()
            assert game.state == "play"
            assert game.world.active_sector == sector
            if sector == "cpu":
                game.render()
                assert "자동 사격" in game.c.itemcget(game.banner_item2, "text")
                assert game.banner_t == 3.2
                infection_before = game.infection.infection
                game.infection.player_infection = 55
                game.render()
                assert "무결성 감소 중" in game.c.itemcget(game.infection_hint, "text")
                game.infection.player_infection = 75
                game.settings.reduced_effects = True
                game.render()
                assert "[G]" in game.c.itemcget(game.infection_hint, "text")
                assert game.c.itemcget(game.infection_bg, "outline") == "#ff7082"
                game.infection.player_infection = infection_before
                game.settings.reduced_effects = False
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
                    if sector == "storage":
                        boss.x, boss.y = 640, 360
                        game.step_virus_patterns(3.0)
                        _, source, action = game.virus_pending[0]
                        assert source is boss and action.behavior == "encryption_pulse"
                        game.settings.reduced_effects = True
                        game.render()
                        warning = game.pool_warning.items[0]
                        assert game.c.itemcget(warning, "state") == "normal"
                        radius = ENCRYPTION_PULSE_RADIUS
                        assert game.c.coords(warning) == [640 - radius, 360 - radius,
                                                          640 + radius, 360 + radius]
                        assert "원 밖으로 이동" in game.c.itemcget(game.boss_txt, "text")
                        with patch.object(game, "hurt_player") as hurt:
                            game.p.x, game.p.y = boss.x + radius, boss.y
                            game._execute_virus_action(boss, action)
                            hurt.assert_not_called()
                            game.p.x -= 1
                            game._execute_virus_action(boss, action)
                            hurt.assert_called_once()
                        game.settings.reduced_effects = False
                    game.damage_enemy(boss, boss.hp * 4 + 1)
                    assert game.boss_defeated
                    game.p.hp = game.p.maxhp * 0.4
                game.check_wave(0.8)
                assert game.world.sector_waves[sector] == wave
                assert game.campaign.progress[sector.upper()].completed_waves == wave
                if wave < 5:
                    assert game.state == "upgrade"
                    game.pick_upgrade(0)
                else:
                    assert sector in game.world.sector_clean
                    assert game.state == "play"
                    assert abs(game.p.hp - game.p.maxhp * 0.55) < 1e-9
            game.render()
            assert "[G]" in game.c.itemcget(game.banner_item2, "text")
            assert "감염 압력 없음" in game.c.itemcget(game.infection_hint, "text")
            assert game.pool_warning.n == 0
            game.p.st["hp_regen"] = 0.5
            game.p.mana = 20
            before_hp = game.p.hp
            assert not game.try_cast_skill("guard")
            for _ in range(60):
                game.step(DT)
            assert game.p.hp == before_hp and game.p.mana == 20
            game.p.x, game.p.y = CPU_EXIT
            game.leave_sector()
            assert game.state == "hub"
            if sector == "cpu":
                game.p.x, game.p.y = 1200, 780
                game.render()
                assert "오른쪽 MEMORY SECTOR" in game.c.itemcget(game.hub_prompt, "text")
                game.p.x, game.p.y = GATES[sector]
            game.interact_hub()
            assert game.p.hp == before_hp
            game.leave_sector()
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
