"""Headless sector, progression and virus behavior contract tests."""
from __future__ import annotations

import random
import unittest

from neonvoid.content.enemies import E_PARAMS, SPAWN_COST
from neonvoid.content.upgrades import UP_BY_ID
from neonvoid.content.sectors import (
    FINAL_WAVE, SECTOR_IDS, SECTORS, SectorCampaign, get_sector,
    plan_sector_wave, sector_software_multiplier,
)
from neonvoid.content.stages import get_stage, plan_wave
from neonvoid.content.virus_catalog import (
    MAX_LIVE_ENEMIES, MAX_REPLICATIONS_PER_WAVE, PATTERNS,
    VirusRuntime, boss_phase, infection_for_hit,
)
from neonvoid.events import EventBus


class SectorContentTests(unittest.TestCase):
    def test_four_areas_have_distinct_bosses_and_accessible_gates(self):
        self.assertEqual(set(SECTORS), set(SECTOR_IDS))
        self.assertEqual({s.boss_kind for s in SECTORS.values()},
                         {"overseer", "replicator", "encryptor", "zero_day"})
        self.assertEqual(len({s.entry_gate for s in SECTORS.values()}), 4)
        self.assertEqual(len({s.exit_gate for s in SECTORS.values()}), 4)
        for sid in SECTOR_IDS:
            sector = get_sector(sid.lower())
            self.assertEqual(sector.sector_id, sid)
            self.assertTrue(sector.unlocked)
            self.assertEqual(len(sector.waves), 5)
            self.assertIs(get_stage(sid), sector.stage)
            self.assertEqual([w.wave_index for w in sector.waves], [1, 2, 3, 4, 5])
            self.assertEqual(sector.clear_condition, "five_waves_and_boss")

    def test_sector_tactical_hints_and_software_bias_are_valid(self):
        focus_sets = []
        for sid in SECTOR_IDS:
            spec = get_sector(sid)
            self.assertTrue(spec.tactics_hint)
            self.assertTrue(spec.reward_hint)
            self.assertTrue(spec.software_focus)
            self.assertTrue(set(spec.software_focus) <= set(UP_BY_ID))
            focus_sets.append(spec.software_focus)
            for uid in spec.software_focus:
                self.assertGreater(sector_software_multiplier(sid.lower(), uid), 1.0)
        self.assertEqual(len(set(focus_sets)), 4)
        self.assertEqual(sector_software_multiplier(None, "power"), 1.0)
        self.assertEqual(sector_software_multiplier("MEMORY", "power"), 1.0)

    def test_twenty_independent_plans_and_boss_cadence(self):
        for sid in SECTOR_IDS:
            for wave in range(1, 6):
                with self.subTest(sector=sid, wave=wave):
                    encounter = plan_sector_wave(sid, wave, seed=42)
                    self.assertEqual(encounter.sector_id, sid)
                    self.assertEqual(encounter.wave_index, wave)
                    self.assertEqual(encounter.wave.stage_id, sid)
                    self.assertEqual(encounter.wave.boss, wave == 5)
                    self.assertTrue(encounter.spawns)
                    self.assertLessEqual(len(encounter.spawns), 200)
                    self.assertTrue(all(kind in E_PARAMS for _, kind in encounter.spawns))
                    self.assertTrue(all(t >= 0 for t, _ in encounter.spawns))
                    if wave == 5:
                        self.assertEqual(encounter.boss_kind, get_sector(sid).boss_kind)
                        self.assertEqual(encounter.spawns[0][1], encounter.boss_kind)
                        self.assertEqual(encounter.clear_condition, "defeat_boss_and_hostiles")
                    else:
                        self.assertIsNone(encounter.boss_kind)
                        self.assertEqual(encounter.clear_condition, "eliminate_hostiles")

    def test_deterministic_across_seeds_visitation_and_reentry(self):
        for seed in (0, 42, 98765, -13):
            forward = [plan_sector_wave(s, w, seed=seed) for s in SECTOR_IDS for w in range(1, 6)]
            backward = [plan_sector_wave(s, w, seed=seed) for s in reversed(SECTOR_IDS)
                        for w in range(5, 0, -1)]
            self.assertEqual(set(forward), set(backward))
        self.assertNotEqual(plan_sector_wave("CPU", 4, seed=1).spawns,
                            plan_sector_wave("CPU", 4, seed=2).spawns)

    def test_region_combat_difference_and_stable_void(self):
        self.assertNotEqual(get_sector("CPU").stage.enemy_unlocks,
                            get_sector("NETWORK").stage.enemy_unlocks)
        self.assertLess(E_PARAMS["worm"]["armor"], E_PARAMS["ransomware"]["armor"])
        self.assertGreater(E_PARAMS["overheater"]["spd"], E_PARAMS["ransomware"]["spd"])
        for sid in SECTOR_IDS:
            self.assertEqual(get_sector(sid).stage.enemy_unlocks[0][0], 1)
            self.assertIn(get_sector(sid).stage.fallback_kind, SPAWN_COST)
        self.assertEqual(get_stage().id, "void")
        self.assertEqual(plan_wave(5, random.Random(42)).spawns[0], (0.7, "boss"))

    def test_invalid_ids_waves_and_seed(self):
        for sid in ("", "UNKNOWN", None):
            with self.assertRaises(ValueError):
                get_sector(sid)
        for wave in (0, 6, -2, 2.3, True):
            with self.assertRaises(ValueError):
                plan_sector_wave("CPU", wave)
        with self.assertRaises(ValueError):
            plan_sector_wave("CPU", 1, seed="1")


class CampaignTests(unittest.TestCase):
    def test_leave_retries_incomplete_but_retains_confirmed_waves(self):
        bus = EventBus()
        seen = []
        for name in ("sector_entered", "sector_wave_started", "wave_cleared",
                     "sector_cleared", "os_cleaned"):
            bus.subscribe(name, lambda ev: seen.append((ev.name, ev.payload)))
        campaign = SectorCampaign("run-001", seed=19, events=bus)
        campaign.enter_sector("MEMORY")
        first = campaign.start_wave()
        self.assertFalse(campaign.clear_wave(hostiles_remaining=1))
        self.assertTrue(campaign.clear_wave(hostiles_remaining=0))
        self.assertFalse(campaign.clear_wave(hostiles_remaining=0))
        second = campaign.start_wave()
        campaign.leave_sector()
        self.assertEqual(campaign.progress["MEMORY"].completed_waves, 1)
        campaign.enter_sector("CPU")
        self.assertEqual(campaign.start_wave().wave_index, 1)
        campaign.leave_sector()
        campaign.enter_sector("MEMORY")
        self.assertEqual(campaign.start_wave(), second)
        campaign.leave_sector()
        self.assertEqual(len([name for name, _ in seen if name == "wave_cleared"]), 1)
        self.assertFalse(campaign.os_clean)

    def test_all_bosses_required_and_events_exactly_once(self):
        bus = EventBus()
        events = []
        for name in ("wave_cleared", "sector_cleared", "os_cleaned"):
            bus.subscribe(name, lambda event: events.append(event))
        campaign = SectorCampaign("run-final", events=bus)
        for sid in SECTOR_IDS:
            campaign.enter_sector(sid)
            for wave in range(1, 6):
                plan = campaign.start_wave()
                self.assertEqual(plan.wave_index, wave)
                if wave == 5:
                    self.assertFalse(campaign.clear_wave(hostiles_remaining=0,
                                                         boss_defeated=False))
                self.assertTrue(campaign.clear_wave(hostiles_remaining=0,
                                                    boss_defeated=wave == 5))
            self.assertTrue(campaign.progress[sid].is_clean)
            with self.assertRaises(ValueError):
                campaign.start_wave()
            campaign.leave_sector()
        self.assertTrue(campaign.os_clean)
        self.assertEqual(len([e for e in events if e.name == "wave_cleared"]), 20)
        self.assertEqual(len([e for e in events if e.name == "sector_cleared"]), 4)
        self.assertEqual(len([e for e in events if e.name == "os_cleaned"]), 1)
        self.assertTrue(all(e.payload["run_id"] == "run-final" for e in events))

    def test_checkpoint_restores_only_cleared_waves(self):
        campaign = SectorCampaign("run-checkpoint", seed=8)
        campaign.enter_sector("CPU")
        campaign.start_wave()
        self.assertTrue(campaign.clear_wave(hostiles_remaining=0))
        campaign.start_wave()
        snapshot = campaign.checkpoint()
        self.assertEqual(snapshot["completed_waves"]["CPU"], 1)
        restored = SectorCampaign.from_checkpoint(snapshot)
        self.assertIsNone(restored.active_sector)
        restored.enter_sector("CPU")
        self.assertEqual(restored.start_wave().wave_index, 2)
        snapshot["completed_waves"]["CPU"] = 999
        with self.assertRaises(ValueError):
            SectorCampaign.from_checkpoint(snapshot)


    def test_malformed_checkpoint_is_rejected_consistently(self):
        campaign = SectorCampaign("run-validate", seed=21)
        snapshot = campaign.checkpoint()
        invalid = [
            None, {}, {"completed_waves": snapshot["completed_waves"]},
            {"run_id": "", "seed": 21,
             "completed_waves": snapshot["completed_waves"]},
            {"run_id": "run-validate", "seed": True,
             "completed_waves": snapshot["completed_waves"]},
            {"run_id": "run-validate", "seed": "21",
             "completed_waves": snapshot["completed_waves"]},
            {"run_id": "run-validate", "seed": 21,
             "completed_waves": []},
            {"run_id": "run-validate", "seed": 21,
             "completed_waves": {"CPU": 5}},
        ]
        for data in invalid:
            with self.subTest(data=str(data)[:70]):
                with self.assertRaises(ValueError):
                    SectorCampaign.from_checkpoint(data)
        self.assertEqual(SectorCampaign.from_checkpoint(snapshot).checkpoint(),
                         snapshot)


class VirusBehaviorTests(unittest.TestCase):
    def test_catalog_coverage_and_nonblocking_actions(self):
        for sid in SECTOR_IDS:
            kinds = [kind for _, kind in get_sector(sid).stage.enemy_unlocks]
            kinds.append(get_sector(sid).boss_kind)
            for kind in kinds:
                if kind in PATTERNS:
                    p = PATTERNS[kind]
                    self.assertGreater(p.cooldown, 0)
                    self.assertGreaterEqual(infection_for_hit(kind), 0)
                    self.assertNotIn(p.behavior, {"stun", "slow_player", "lock_input",
                                                   "blind", "disable_skill"})
        self.assertEqual(boss_phase(1), 1)
        self.assertEqual(boss_phase(0.5), 2)
        self.assertEqual(boss_phase(0.1), 3)

    def test_worm_replication_capped_and_paused(self):
        runtime = VirusRuntime()
        self.assertFalse(runtime.step("worm-1", "worm", 20, active_enemy_count=10,
                                      paused=True))
        self.assertFalse(runtime.states)
        self.assertFalse(runtime.step("worm-1", "worm", 0, active_enemy_count=10))
        self.assertFalse(runtime.states)
        actions = runtime.step("worm-1", "worm", 20, active_enemy_count=10)
        self.assertLessEqual(sum(a.count for a in actions), 2)
        self.assertEqual(sum(a.count for a in actions), 2)
        self.assertTrue(all(a.spawn_kind == "worm" for a in actions))
        self.assertEqual(runtime.step("worm-1", "worm", 10**7,
                                      active_enemy_count=10), ())
        runtime.reset_wave()
        self.assertEqual(runtime.total_replications, 0)
        self.assertFalse(runtime.states)

    def test_global_cap_and_custom_attack_variants(self):
        runtime = VirusRuntime()
        for i in range(40):
            runtime.step(f"replicator-{i}", "replicator", 100,
                         active_enemy_count=2)
        self.assertLessEqual(runtime.total_replications, MAX_REPLICATIONS_PER_WAVE)
        self.assertFalse(runtime.step("blocked", "worm", 100,
                                      active_enemy_count=MAX_LIVE_ENEMIES))
        runtime.reset_wave()
        rootkit = runtime.step("root-1", "rootkit", 3.0, active_enemy_count=1)
        self.assertEqual(rootkit[0].behavior, "armor_piercing_volley")
        self.assertGreater(rootkit[0].armor_pen, 0)
        ransomware = runtime.step("ransom-1", "ransomware", 3.8, active_enemy_count=1)
        self.assertEqual(ransomware[0].behavior, "encryption_pulse")
        runtime.forget("ransom-1")
        self.assertNotIn("ransom-1", runtime.states)

    def test_invalid_steps_are_rejected(self):
        runtime = VirusRuntime()
        for delta in (-1, float("nan"), float("inf")):
            with self.assertRaises(ValueError):
                runtime.step("a", "worm", delta, active_enemy_count=1)
        with self.assertRaises(ValueError):
            runtime.step("", "worm", 1, active_enemy_count=1)
        with self.assertRaises(ValueError):
            runtime.step("a", "worm", 1, active_enemy_count=-1)
        with self.assertRaises(ValueError):
            runtime.step("a", "not-virus", 1, active_enemy_count=1)


if __name__ == "__main__":
    unittest.main()
