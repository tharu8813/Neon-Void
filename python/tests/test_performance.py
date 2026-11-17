"""Real OS process footprint metric (Linux procfs or Windows PSAPI)."""
import unittest
import sys

from neonvoid.performance import resident_memory_mib, RenderedFrameRate


class PerformanceTests(unittest.TestCase):
    def test_rss_is_finite_positive_or_unavailable(self):
        value = resident_memory_mib()
        if sys.platform == "win32" or sys.platform.startswith("linux"):
            self.assertIsNotNone(value)
        self.assertTrue(value is None or (0 < value < 1_000_000))


class FrameRateTests(unittest.TestCase):
    def test_only_submitted_frames_count(self):
        meter = RenderedFrameRate(interval=0.5)
        self.assertIsNone(meter.submitted(0.0))
        for n in range(1, 30):
            self.assertIsNone(meter.submitted(n / 60))
        self.assertAlmostEqual(meter.submitted(0.5), 60.0)
        # Unrendered Tk timer wakeups are never passed to the meter.
        self.assertIsNone(meter.submitted(0.5 + 1 / 60))
        self.assertAlmostEqual(meter.submitted(1.0), 4.0)

    def test_reject_invalid_interval(self):
        with self.assertRaises(ValueError):
            RenderedFrameRate(interval=0.0)


if __name__ == "__main__":
    unittest.main()
