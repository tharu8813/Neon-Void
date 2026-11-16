"""Real OS process footprint metric (Linux procfs or Windows PSAPI)."""
import unittest
import sys

from neonvoid.performance import resident_memory_mib


class PerformanceTests(unittest.TestCase):
    def test_rss_is_finite_positive_or_unavailable(self):
        value = resident_memory_mib()
        if sys.platform == "win32" or sys.platform.startswith("linux"):
            self.assertIsNotNone(value)
        self.assertTrue(value is None or (0 < value < 1_000_000))


if __name__ == "__main__":
    unittest.main()
