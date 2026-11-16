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

    def test_pressure_forecast_and_non_mutating_station_preview(self):
        for infection, rate in ((0, 0), (49.99, 0), (50, 0.3),
                                (74.99, 0.3), (75, 1.0), (100, 1.0)):
            with self.subTest(infection=infection):
                state = InfectionState(infection, 20)
                self.assertAlmostEqual(state.integrity_pressure_per_second(), rate)
                seconds = state.seconds_until_integrity_loss()
                if rate:
                    self.assertAlmostEqual(seconds, 20.0 / rate)
                else:
                    self.assertIsNone(seconds)
        state = InfectionState(100, 20)
        future = state.preview_purification(32, 9)
        self.assertEqual(state.snapshot(), {"player_infection": 100,
                                            "os_integrity": 20})
        self.assertEqual(future.snapshot(), {"player_infection": 68,
                                             "os_integrity": 29})
        self.assertEqual(future.stage, InfectionStage.SPREADING)
        self.assertAlmostEqual(future.integrity_pressure_per_second(), 0.3)
        for invalid in (float("nan"), -1, "9"):
            with self.assertRaises(ValueError):
                state.preview_purification(invalid, 9)

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

    def test_direct_assignment_keeps_public_state_valid(self):
        state = InfectionState()
        state.player_infection = 150
        state.os_integrity = 1000
        self.assertEqual(state.snapshot(), {"player_infection": 100,
                                            "os_integrity": 100})
        state.player_infection = 0
        state.os_integrity = 0
        self.assertEqual(state.stage, InfectionStage.NORMAL)
        self.assertEqual(state.tick(0).os_integrity, 0)
        for name in ("player_infection", "os_integrity"):
            for invalid in (-1, float("nan"), float("inf"), True, "100",
                            10**500):
                with self.subTest(name=name, invalid=str(invalid)[:30]):
                    before = state.snapshot()
                    with self.assertRaises(ValueError):
                        setattr(state, name, invalid)
                    self.assertEqual(state.snapshot(), before)

    def test_malformed_snapshot_has_consistent_error_and_no_state_change(self):
        for snapshot in ({}, {"player_infection": 20},
                         {"os_integrity": 90},
                         {"player_infection": float("nan"), "os_integrity": 90},
                         {"player_infection": 25, "os_integrity": 10**500}):
            with self.subTest(snapshot=str(snapshot)[:70]):
                with self.assertRaises(ValueError):
                    InfectionState.from_snapshot(snapshot)
        clamped = InfectionState.from_snapshot(
            {"player_infection": 500, "os_integrity": 500})
        self.assertEqual(clamped.snapshot(), {"player_infection": 100,
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
