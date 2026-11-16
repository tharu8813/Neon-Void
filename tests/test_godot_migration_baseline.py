"""Godot P0: behavior invariants and READ-ONLY synthetic legacy save fixtures.

This test deliberately never opens the user's default save paths for writing.
It checks pinned migration assumptions; it is not a pixel/Windows parity test.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from neonvoid.combat import mitigate_damage
from neonvoid.constants import DT, H, W
from neonvoid.content.enemies import E_PARAMS
from neonvoid.content.sectors import SECTOR_IDS, SECTORS, SectorCampaign, plan_sector_wave
from neonvoid.content.upgrades import UPGRADES
from neonvoid.controls import DEFAULT_BINDINGS, dash_vector, movement_vector
from neonvoid.database_storage import load_database
from neonvoid.display import Viewport
from neonvoid.entities import Player
from neonvoid.metaprogression import base_stats_for_level, xp_threshold
from neonvoid.os_checkpoint import validate_checkpoint
from neonvoid.profile import load_profile
from neonvoid.settings import load_settings
from neonvoid.storage import load_save
from neonvoid.world.session import START, WORLD_H, WORLD_W, camera_for

FIXTURES = Path(__file__).parent / "fixtures" / "godot_migration"
FILES = ("save.json", "settings.json", "profile.json",
         "antivirus_db.json", "os_checkpoint.json")
SOFTWARE_IDS = (
    "rapid", "power", "pierce", "split", "homing", "boom", "crit", "vamp",
    "thorn", "dash2", "dashb", "slow", "magnet", "drone", "chain", "rear",
    "hull", "ricochet", "armor", "breach", "mana", "regen", "burn",
    "toxin", "leech", "focus", "cool", "eco", "sustain", "duration"
)
ENEMY_IDS = (
    "drifter", "mini", "shooter", "splitter", "rusher", "boss",
    "overheater", "worm", "trojan", "ransomware", "rootkit",
    "packet_swarm", "guardian", "overseer", "replicator",
    "encryptor", "zero_day"
)


class GodotMigrationBaselineTests(unittest.TestCase):
    def test_fixed_step_stats_and_combat_parity(self):
        self.assertEqual((W, H), (1280, 720))
        self.assertAlmostEqual(DT, 1 / 60)
        p = Player()
        self.assertEqual((p.x, p.y, p.r, p.maxhp, p.maxmana),
                         (640.0, 360.0, 13.0, 100.0, 100.0))
        for name, value in (("speed", 322.0), ("rate", 5.4), ("dmg", 10.0),
                            ("bspeed", 880.0), ("armor", 4.0),
                            ("mana_regen", 6.0), ("crit", 0.05)):
            self.assertEqual(p.st[name], value)
        self.assertEqual((p.dash_cd, p.charges), (1.05, 1))
        self.assertAlmostEqual(mitigate_damage(100, 100), 50)
        self.assertEqual(base_stats_for_level(1),
                         {"maxhp": 100.0, "dmg": 10.0,
                          "armor": 4.0, "maxmana": 100.0})
        self.assertEqual(base_stats_for_level(2),
                         {"maxhp": 102.0, "dmg": 10.2,
                          "armor": 4.15, "maxmana": 101.0})
        self.assertEqual((xp_threshold(1), xp_threshold(2)), (100, 150))

    def test_inputs_use_movement_not_pointer_for_dash(self):
        self.assertEqual(DEFAULT_BINDINGS["dash"], "space")
        self.assertEqual(DEFAULT_BINDINGS["interact"], "f")
        self.assertEqual(DEFAULT_BINDINGS["skill_pulse"], "q")
        self.assertEqual(DEFAULT_BINDINGS["skill_guard"], "e")
        self.assertEqual(dash_vector({"d"}, (-200, 0), (0, -1)), (1, 0))
        self.assertEqual(dash_vector(set(), (0, 80), (-1, 0)), (0, 1))
        self.assertEqual(dash_vector(set(), (0, 0), (0, 0)), (0, -1))
        self.assertAlmostEqual(sum(t*t for t in movement_vector({"w", "d"})), 1)

    def test_camera_and_screen_pointer_inverse(self):
        self.assertEqual((WORLD_W, WORLD_H, START),
                         (2400.0, 1560.0, (1200.0, 780.0)))
        self.assertEqual(camera_for(1200, 780), (560, 420))
        for w, h, expected_scale, expected_top in (
            (960, 540, .75, 0), (1280, 720, 1, 0),
            (1920, 1080, 1.5, 0), (1024, 768, .8, 96),
        ):
            with self.subTest(size=(w, h)):
                view = Viewport.fit(w, h)
                self.assertAlmostEqual(view.scale, expected_scale)
                self.assertAlmostEqual(view.top, expected_top)
                for point in ((0, 0), (640, 360), (1280, 720)):
                    a, b = view.to_game(*view.to_screen(*point))
                    self.assertAlmostEqual(a, point[0])
                    self.assertAlmostEqual(b, point[1])

    def test_content_ids_and_seeded_sector_wave(self):
        self.assertEqual(tuple(u[0] for u in UPGRADES), SOFTWARE_IDS)
        self.assertEqual(tuple(E_PARAMS), ENEMY_IDS)
        self.assertEqual(tuple(SECTOR_IDS), ("CPU", "MEMORY", "STORAGE", "NETWORK"))
        self.assertEqual(
            {k: s.boss_kind for k, s in SECTORS.items()},
            {"CPU": "overseer", "MEMORY": "replicator",
             "STORAGE": "encryptor", "NETWORK": "zero_day"},
        )
        for sid in SECTOR_IDS:
            self.assertEqual(len(SECTORS[sid].waves), 5)
            a = plan_sector_wave(sid, wave_index=5, seed=42)
            b = plan_sector_wave(sid, wave_index=5, seed=42)
            self.assertEqual(a.spawns, b.spawns)
            self.assertEqual(a.boss_kind, SECTORS[sid].boss_kind)

    def test_synthetic_json_fixtures_validate_without_mutation(self):
        original_hashes = {
            name: hashlib.sha256((FIXTURES / name).read_bytes()).hexdigest()
            for name in FILES
        }
        with tempfile.TemporaryDirectory(prefix="neonvoid-p0-") as tmp:
            copied = {}
            for name in FILES:
                target = Path(tmp) / name
                target.write_bytes((FIXTURES / name).read_bytes())
                copied[name] = target
            with patch("neonvoid.storage.SAVE_FILE", str(copied["save.json"])):
                self.assertEqual(load_save(),
                                 {"best_score": 250, "best_wave": 5, "runs": 2})
            settings = load_settings(copied["settings.json"])
            self.assertEqual((settings.window_width, settings.window_height), (1280, 720))
            self.assertEqual(settings.bindings, DEFAULT_BINDINGS)
            profile = load_profile(copied["profile.json"])
            self.assertEqual((profile.extracted_runs, len(profile.recent_runs)), (1, 1))
            db = load_database(copied["antivirus_db.json"])
            self.assertEqual((db.antivirus_level, db.xp), (1, 0))
            checkpoint = json.loads(copied["os_checkpoint.json"].read_text("utf-8"))
            validate_checkpoint(checkpoint)
            campaign = SectorCampaign.from_checkpoint(checkpoint["campaign"])
            self.assertEqual(campaign.run_id, "p0-synthetic-run-001")
            self.assertEqual(campaign.seed, 42)
            self.assertEqual(checkpoint["scene"], "hub")
            self.assertEqual(campaign.progress["CPU"].completed_waves, 0)
            self.assertEqual(set(copied[name].name for name in FILES), set(FILES))
            for name in FILES:
                self.assertEqual(hashlib.sha256(copied[name].read_bytes()).hexdigest(),
                                 original_hashes[name])
        for name in FILES:
            self.assertEqual(hashlib.sha256((FIXTURES / name).read_bytes()).hexdigest(),
                             original_hashes[name])


if __name__ == "__main__":
    unittest.main()
