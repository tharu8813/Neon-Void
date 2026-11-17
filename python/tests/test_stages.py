"""Wave director regression tests: old spawn timing and RNG order must match."""
import random
import unittest
from dataclasses import replace

from neonvoid.content.enemies import SPAWN_COST
from neonvoid.content.stages import (
    DEFAULT_STAGE_ID, STAGES, StageDefinition, WavePlan,
    get_stage, plan_wave, validate_stage,
)


def legacy_spawn(wave, rng):
    """Reference port of pre-refactor Game.start_wave for regression checks."""
    if wave % 5 == 0:
        entries = [(0.7, "boss")]
        extra = 2 + wave // 5
        for i in range(extra):
            entries.append((2.2 + i * 1.4, rng.choice(["drifter", "shooter"])))
        return entries
    budget = 8.0 + wave * 4.0
    pool = ["drifter"]
    if wave >= 2:
        pool += ["shooter"]
    if wave >= 3:
        pool += ["splitter"]
    if wave >= 4:
        pool += ["rusher"]
    at = 0.4
    guard = 0
    entries = []
    while budget > 0 and guard < 200:
        guard += 1
        kind = rng.choice(pool)
        cost = SPAWN_COST[kind]
        if cost > budget and kind != "drifter":
            kind, cost = "drifter", 1.0
        budget -= cost
        entries.append((at, kind))
        at += max(0.22, 0.62 - wave * 0.015)
    return entries


class StageWaveTests(unittest.TestCase):
    def test_default_stage_exists_and_validates(self):
        self.assertIn(DEFAULT_STAGE_ID, STAGES)
        self.assertEqual(get_stage().id, DEFAULT_STAGE_ID)
        self.assertIsNone(validate_stage(get_stage()))

    def test_wave_plans_match_legacy_rng_and_timing(self):
        for seed in (0, 42, 1111, 987654):
            rng_new = random.Random(seed)
            rng_old = random.Random(seed)
            for wave in range(1, 31):
                with self.subTest(seed=seed, wave=wave):
                    plan = plan_wave(wave, rng_new)
                    old = legacy_spawn(wave, rng_old)
                    self.assertEqual(list(plan.spawns), old)
                    self.assertEqual(plan.boss, wave % 5 == 0)
                    self.assertEqual(plan.audio_cue, "boss" if plan.boss else "wave")
            self.assertEqual(rng_new.random(), rng_old.random())

    def test_wave_plan_is_immutable_and_not_a_mutable_queue(self):
        p = plan_wave(1, random.Random(13))
        self.assertIsInstance(p, WavePlan)
        self.assertIsInstance(p.spawns, tuple)
        with self.assertRaises(Exception):
            p.wave = 5

    def test_custom_stage_changes_boss_interval_without_game_code(self):
        custom = replace(get_stage(), id="test", name="Arena", boss_interval=3,
                         base_budget=5.0, budget_per_wave=2.0)
        plan = plan_wave(3, random.Random(1), custom)
        self.assertEqual(plan.stage_id, "test")
        self.assertTrue(plan.boss)
        self.assertEqual(plan.spawns[0], (0.7, "boss"))
        regular = plan_wave(2, random.Random(1), custom)
        self.assertFalse(regular.boss)
        self.assertGreater(len(regular.spawns), 0)

    def test_invalid_stages_and_wave_numbers_rejected(self):
        stage = get_stage()
        invalid = (
            replace(stage, id=""),
            replace(stage, boss_interval=0),
            replace(stage, base_budget=-1),
            replace(stage, budget_per_wave=float("nan")),
            replace(stage, enemy_unlocks=((2, "drifter"),)),
            replace(stage, enemy_unlocks=((1, "drifter"), (2, "unknown"))),
            replace(stage, boss_supports=()),
            replace(stage, boss_kind="unknown"),
        )
        for value in invalid:
            with self.subTest(stage=value):
                with self.assertRaises(ValueError):
                    validate_stage(value)
        for wave in (-1, 0, 1.5, True):
            with self.assertRaises(ValueError):
                plan_wave(wave, random.Random(0))
        with self.assertRaises(ValueError):
            get_stage("nonexistent")


if __name__ == "__main__":
    unittest.main()
