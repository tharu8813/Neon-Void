"""Infection state and simulated-time pressure regression tests."""
from __future__ import annotations

import unittest

from neonvoid.infection import (
    CRITICAL_AT, DETECTED_AT, SPREADING_AT, InfectionStage, InfectionState,
)


class InfectionTests(unittest.TestCase):
    def test_four_stages_and_exact_boundaries(self):
        state = InfectionState()
        for amount, stage in ((0, InfectionStage.NORMAL),
                              (DETECTED_AT - 0.01, InfectionStage.NORMAL),
                              (DETECTED_AT, InfectionStage.DETECTED),
                              (SPREADING_AT, InfectionStage.SPREADING),
                              (CRITICAL_AT, InfectionStage.CRITICAL),
                              (100, InfectionStage.CRITICAL)):
            state.player_infection = amount
            self.assertEqual(state.stage, stage)
        self.assertEqual([int(s) for s in InfectionStage], [0, 1, 2, 3])

    def test_application_purification_and_integrity_clamping(self):
        state = InfectionState(99, 99)
        self.assertEqual(state.apply_infection(10), 1)
        self.assertEqual(state.player_infection, 100)
        self.assertEqual(state.apply_infection(50), 0)
        self.assertEqual(state.purify(500), 100)
        self.assertEqual(state.infection, 0)
        self.assertEqual(state.purify(5), 0)
        self.assertEqual(state.repair_integrity(50), 1)
        self.assertEqual(state.os_integrity, 100)
        state = InfectionState(999, 999)
        self.assertEqual(state.snapshot(), {"player_infection": 100,
                                            "os_integrity": 100})

    def test_independent_dt_and_pressure_threshold_crossings(self):
        one = InfectionState(45)
        many = InfectionState(45)
        one.tick(10, exposure_per_second=4)
        for _ in range(600):
            many.tick(1 / 60, exposure_per_second=4)
        self.assertAlmostEqual(one.infection, many.infection, places=9)
        self.assertAlmostEqual(one.os_integrity, many.os_integrity, places=8)
        self.assertLess(one.os_integrity, 100)
        self.assertEqual(one.stage, InfectionStage.CRITICAL)
        self.assertTrue(one.needs_purification)

    def test_purification_over_time_and_pause_freezes_all_state(self):
        state = InfectionState(80, 60)
        before = state.snapshot()
        result = state.tick(120, paused=True, exposure_per_second=100)
        self.assertEqual(state.snapshot(), before)
        self.assertEqual(result.integrity_lost, 0)
        self.assertEqual(state.apply_infection(20, paused=True), 0)
        self.assertEqual(state.purify(30, paused=True), 0)
        self.assertEqual(state.repair_integrity(10, paused=True), 0)
        state.tick(10, purification_per_second=10)
        self.assertEqual(state.infection, 0)
        self.assertLess(state.os_integrity, 60)
        self.assertEqual(state.stage, InfectionStage.NORMAL)

    def test_zero_infection_is_never_death_or_controls_effect(self):
        state = InfectionState(100, 0)
        result = state.tick(100, exposure_per_second=10)
        self.assertEqual(result.player_infection, 100)
        self.assertEqual(result.os_integrity, 0)
        self.assertEqual(result.infection_stage, InfectionStage.CRITICAL)
        self.assertFalse(hasattr(state, "player_speed"))
        self.assertFalse(hasattr(state, "input_locked"))
        state.reset_for_new_os()
        self.assertEqual(state.snapshot(), {"player_infection": 0,
                                            "os_integrity": 100})

    def test_serialization_and_invalid_inputs(self):
        state = InfectionState(24.5, 83.0)
        self.assertEqual(InfectionState.from_snapshot(state.snapshot()), state)
        for invalid in (-1, float("nan"), float("inf"), "3", True):
            with self.subTest(invalid=invalid):
                with self.assertRaises(ValueError):
                    InfectionState(invalid)
                with self.assertRaises(ValueError):
                    state.apply_infection(invalid)
                with self.assertRaises(ValueError):
                    state.purify(invalid)
                with self.assertRaises(ValueError):
                    state.repair_integrity(invalid)
                with self.assertRaises(ValueError):
                    state.tick(invalid)
                with self.assertRaises(ValueError):
                    state.tick(1, exposure_per_second=invalid)
                with self.assertRaises(ValueError):
                    state.tick(1, purification_per_second=invalid)
        with self.assertRaises(ValueError):
            InfectionState.from_snapshot(None)


if __name__ == "__main__":
    unittest.main()
