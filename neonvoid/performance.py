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
