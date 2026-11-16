"""Opt-in-friendly process footprint metric with no third-party dependencies."""
from __future__ import annotations

import os
import sys


def resident_memory_mib() -> float | None:
    """Current process RSS on Windows/Linux; None if not available.

    This includes native Tk and Python allocations, unlike tracemalloc's
    Python-only accounting. Query at most once per second in the HUD.
    """
    try:
        if sys.platform == "win32":
            import ctypes
            from ctypes import wintypes

            class MemoryCounters(ctypes.Structure):
                _fields_ = [
                    ("cb", wintypes.DWORD),
                    ("PageFaultCount", wintypes.DWORD),
                    ("PeakWorkingSetSize", ctypes.c_size_t),
                    ("WorkingSetSize", ctypes.c_size_t),
                    ("QuotaPeakPagedPoolUsage", ctypes.c_size_t),
                    ("QuotaPagedPoolUsage", ctypes.c_size_t),
                    ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t),
                    ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
                    ("PagefileUsage", ctypes.c_size_t),
                    ("PeakPagefileUsage", ctypes.c_size_t),
                ]

            counters = MemoryCounters()
            counters.cb = ctypes.sizeof(MemoryCounters)
            kernel = ctypes.windll.kernel32
            kernel.GetCurrentProcess.restype = wintypes.HANDLE
            psapi = ctypes.windll.psapi
            psapi.GetProcessMemoryInfo.argtypes = (
                wintypes.HANDLE, ctypes.POINTER(MemoryCounters), wintypes.DWORD
            )
            psapi.GetProcessMemoryInfo.restype = wintypes.BOOL
            process = kernel.GetCurrentProcess()
            if psapi.GetProcessMemoryInfo(
                    process, ctypes.byref(counters), counters.cb):
                return counters.WorkingSetSize / (1024 * 1024)
        elif sys.platform.startswith("linux"):
            with open("/proc/self/statm", encoding="ascii") as stream:
                resident_pages = int(stream.read().split()[1])
            return resident_pages * os.sysconf("SC_PAGE_SIZE") / (1024 * 1024)
    except (OSError, ValueError, IndexError, AttributeError):
        pass
    return None


class RenderedFrameRate:
    """Count actual render submissions instead of Tk timer callbacks.

    The 60 Hz simulation and UI timers may be called at different rates.
    Only a submitted render is counted. This does not measure the monitor's
    presented frames, which Tk Canvas cannot report.
    """

    def __init__(self, interval: float = 0.5):
        if interval <= 0:
            raise ValueError("interval must be positive")
        self.interval = interval
        self.started_at: float | None = None
        self.count = 0

    def submitted(self, now: float) -> float | None:
        if self.started_at is None:
            self.started_at = now
            self.count = 0
            return None
        self.count += 1
        elapsed = now - self.started_at
        if elapsed < self.interval:
            return None
        rate = self.count / elapsed if elapsed > 0 else 0.0
        self.count = 0
        self.started_at = now
        return rate
