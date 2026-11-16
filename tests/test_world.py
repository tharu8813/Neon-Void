"""World-only physics, sector progress and hub state invariants."""
import math
import unittest

from neonvoid.world.session import (
    AVAILABLE_SECTORS, BASE_NODES, CPU_EXIT, OS_NODES, PILLARS, SECTOR_IDS,
    START, WORLD_H, WORLD_W, WorldSession, camera_for, move_on_world,
)


class WorldSessionTests(unittest.TestCase):
    def test_fresh_world_is_base_and_sector_gates_are_explicit(self):
        world = WorldSession()
        self.assertEqual(world.room, "base")
        self.assertEqual(world.sector_waves, dict.fromkeys(SECTOR_IDS, 0))
        self.assertFalse(world.os_clean)
        self.assertEqual(AVAILABLE_SECTORS, set(SECTOR_IDS))
        self.assertEqual(world.nearest(1580, 780).id, "deploy")
        self.assertIsNone(world.nearest(130, 170))
        self.assertEqual(world.nodes(), BASE_NODES)
        world.deploy()
        self.assertEqual(world.nodes(), OS_NODES)
        self.assertEqual(world.nearest(1200, 270).id, "cpu")

    def test_world_movement_camera_limits_and_pillar_collision(self):
        self.assertEqual(move_on_world(20, 20, -900, -900), (13, 13))
        self.assertEqual(move_on_world(WORLD_W-20, WORLD_H-20, 400, 400),
                         (WORLD_W-13, WORLD_H-13))
        self.assertEqual(move_on_world(1000, 550, 55, 0), (1000, 550))
        self.assertEqual(move_on_world(1000, 550, 0, 30), (1000, 580))
        self.assertEqual(camera_for(1, 1), (0, 0))
        self.assertEqual(camera_for(WORLD_W, WORLD_H),
                         (WORLD_W-1280, WORLD_H-720))
        px, py = START
        self.assertAlmostEqual(camera_for(px, py)[0], px - 640)
        for invalid in (float("inf"), float("nan")):
            with self.assertRaises(ValueError):
                move_on_world(100, 100, invalid, 0)

    def test_sector_clears_are_sequential_and_idempotent(self):
        world = WorldSession()
        with self.assertRaises(ValueError):
            world.enter_sector("cpu")
        world.deploy()
        self.assertEqual(world.enter_sector("memory"), 1)
        self.assertEqual(world.leave_sector(), "memory")
        self.assertEqual(world.enter_sector("cpu"), 1)
        self.assertFalse(world.wave_cleared("cpu", 2))
        self.assertFalse(world.wave_cleared("cpu", 0))
        self.assertTrue(world.wave_cleared("cpu", 1))
        self.assertFalse(world.wave_cleared("cpu", 1))
        self.assertEqual(world.leave_sector(), "cpu")
        self.assertEqual(world.enter_sector("cpu"), 2)
        for wave in range(2, 6):
            self.assertTrue(world.wave_cleared("cpu", wave))
        self.assertFalse(world.wave_cleared("cpu", 5))
        self.assertIn("cpu", world.sector_clean)
        self.assertFalse(world.os_clean)
        self.assertEqual(world.leave_sector(), "cpu")
        self.assertEqual(world.enter_sector("cpu"), 5)
        self.assertFalse(world.wave_cleared("cpu", 6))

    def test_reset_and_full_os_condition(self):
        world = WorldSession()
        world.deploy()
        world.sector_clean.update(SECTOR_IDS)
        self.assertTrue(world.os_clean)
        world.reset_to_base()
        self.assertEqual(world.room, "base")
        self.assertFalse(world.os_clean)
        self.assertEqual(world.sector_waves, dict.fromkeys(SECTOR_IDS, 0))


if __name__ == "__main__":
    unittest.main()
