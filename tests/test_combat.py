"""Pure combat math plus headless game-method regression tests."""
from __future__ import annotations

import math
import random
import unittest
from types import SimpleNamespace

from neonvoid.combat import (
    DamageOverTime, add_dot, advance_dots, effective_armor,
    mitigate_damage, skill_cooldown, skill_cost,
)
from neonvoid.content.enemies import E_PARAMS
from neonvoid.content.upgrades import UPGRADES, apply_upgrade
from neonvoid.entities import Bullet, Enemy, Player
from neonvoid.events import EventBus
from neonvoid.game import Game
from neonvoid.world.session import WorldSession


def stub_game():
    g = Game.__new__(Game)
    g.p = Player()
    g.state = "play"
    g.world = WorldSession()
    g.paused = False
    g.enemies = []
    g.shake = 0.0
    g.cursor_hit = 0.0
    g.frames = 0
    g.rng = random.Random(7)
    g.audio = SimpleNamespace(play=lambda *args: None)
    g.events = EventBus()
    g.ring = lambda *args: None
    g.burst = lambda *args: None
    g.pop_text = lambda *args: None
    return g


class CombatTests(unittest.TestCase):
    def test_armor_penetration_and_nonnegative_damage(self):
        self.assertAlmostEqual(mitigate_damage(100, 100), 50)
        self.assertAlmostEqual(mitigate_damage(100, 100, 50), 100 * 100 / 150)
        self.assertEqual(mitigate_damage(100, 20, 30), 100)
        self.assertEqual(mitigate_damage(-10, 100), 0)
        with self.assertRaises(ValueError):
            mitigate_damage(float("nan"), 5)

    def test_status_stacks_cap_refresh_and_fractional_expiry(self):
        effects = []
        for _ in range(5):
            add_dot(effects, "burn", 4, 3)
        self.assertEqual(len(effects), 1)
        self.assertEqual(effects[0].stacks, 3)
        self.assertAlmostEqual(sum(x[1] for x in advance_dots(effects, 2)), 24)
        add_dot(effects, "burn", 5, 3)
        self.assertEqual(effects[0].stacks, 3)
        self.assertAlmostEqual(sum(x[1] for x in advance_dots(effects, 4)), 45)
        self.assertEqual(effects, [])
        with self.assertRaises(ValueError):
            advance_dots([], -1)

    def test_dots_have_independent_kinds_and_bounded_lifetimes(self):
        effects = []
        add_dot(effects, "burn", 3, 2)
        add_dot(effects, "toxin", 2, 4)
        self.assertEqual({k for k, _ in advance_dots(effects, 1)}, {"burn", "toxin"})
        self.assertEqual({k for k, _ in advance_dots(effects, 2)}, {"burn", "toxin"})
        self.assertEqual([v.kind for v in effects], ["toxin"])
        self.assertEqual(advance_dots(effects, 1), [("toxin", 2.0)])
        self.assertEqual(effects, [])

    def test_software_catalog_and_stats_are_real(self):
        ids = {u[0] for u in UPGRADES}
        self.assertTrue({"burn", "toxin", "armor", "breach", "mana", "focus",
                         "sustain", "duration", "regen", "leech"} <= ids)
        g = SimpleNamespace(p=Player(), levels={})
        for uid in ("armor", "breach", "mana", "regen", "burn", "toxin",
                    "leech", "focus", "cool", "eco", "sustain", "duration"):
            apply_upgrade(g, uid)
        self.assertGreater(g.p.st["armor"], 4)
        self.assertEqual(g.p.st["armor_pen"], 4)
        self.assertEqual(g.p.maxmana, 125)
        self.assertGreater(g.p.st["burn_chance"], 0)
        self.assertGreater(g.p.st["toxin_chance"], 0)
        self.assertGreater(g.p.st["skill_power"], 1)
        self.assertGreater(g.p.st["hp_regen"], 0)
        self.assertGreater(g.p.st["effect_duration"], 1)
        self.assertLess(skill_cost(35, g.p.st["skill_efficiency"]), 35)
        self.assertLess(skill_cooldown(7, g.p.st["skill_cooldown"]), 7)

    def test_enemy_catalog_has_armor(self):
        self.assertTrue(all("armor" in params and params["armor"] >= 0
                            for params in E_PARAMS.values()))

    def test_shield_requires_mana_and_respects_cooldown(self):
        g = stub_game()
        g.p.mana = 24
        self.assertFalse(g.try_cast_skill("guard"))
        self.assertEqual(g.p.guard_cd, 0)
        g.p.mana = 100
        self.assertTrue(g.try_cast_skill("guard"))
        self.assertEqual(g.p.mana, 75)
        self.assertGreater(g.p.guard_t, 0)
        self.assertEqual(effective_armor(g.p.st["armor"], g.p.guard_t), 24)
        self.assertFalse(g.try_cast_skill("guard"))
        self.assertEqual(g.p.mana, 75)
        g.paused = True
        self.assertFalse(g.try_cast_skill("pulse"))

    def test_pulse_hits_only_nearby_and_applies_shock(self):
        g = stub_game()
        near = Enemy()
        near.x, near.y, near.hp = g.p.x + 45, g.p.y, 100
        far = Enemy()
        far.x, far.y, far.hp = g.p.x + 300, g.p.y, 100
        g.enemies = [near, far]
        self.assertTrue(g.try_cast_skill("pulse"))
        self.assertEqual(g.p.mana, 65)
        self.assertLess(near.hp, 100)
        self.assertEqual(far.hp, 100)
        self.assertEqual([x.kind for x in near.statuses], ["shock"])
        self.assertFalse(g.try_cast_skill("pulse"))

    def test_projectile_on_hit_effects_and_no_recursive_dot_proc(self):
        g = stub_game()
        g.p.hp = 30
        g.p.st.update(life_on_hit=0.5, burn_chance=1.0, burn_dps=4.0,
                      toxin_chance=1.0, toxin_dps=3.0)
        target = Enemy()
        target.hp = 100
        target.armor = 20
        g.enemies = [target]
        b = Bullet()
        b.dmg = 20
        b.crit = 0
        b.pierce = 0
        b.armor_pen = 5
        g.on_bullet_hit(b, target)
        self.assertAlmostEqual(target.hp, 100 - mitigate_damage(20, 20, 5))
        self.assertAlmostEqual(g.p.hp, 30.5)
        self.assertEqual({x.kind for x in target.statuses}, {"burn", "toxin"})
        before_hp = g.p.hp
        before_count = len(target.statuses)
        g.step_statuses(0.25)
        self.assertLess(target.hp, 100 - mitigate_damage(20, 20, 5))
        self.assertEqual(before_hp, g.p.hp)
        self.assertEqual(before_count, len(target.statuses))

    def test_dot_kill_called_once(self):
        g = stub_game()
        e = Enemy()
        e.hp = 2
        g.enemies = [e]
        killed = []
        g.kill_enemy = lambda enemy: killed.append(enemy)
        add_dot(e.statuses, "burn", 5, 2)
        g.step_statuses(1.0)
        self.assertEqual(killed, [e])
        g.step_statuses(1.0)
        self.assertEqual(killed, [e])

    def test_player_armor_reduces_damage(self):
        g = stub_game()
        g.opts = SimpleNamespace(god=False)
        g.hitstop = 0
        g.combo = 0
        g.p.guard_t = 2
        g.hurt_player(30)
        self.assertAlmostEqual(g.p.hp, 100 - mitigate_damage(30, 24))
        self.assertGreater(g.p.inv, 0)


if __name__ == "__main__":
    unittest.main()
