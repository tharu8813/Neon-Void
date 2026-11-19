"""Console entrypoint."""

from __future__ import annotations

import argparse
import os
import sys
import tkinter as tk
from . import __version__
from .constants import DATA_DIRECTORY, clamp
from .content.stages import DEFAULT_STAGE_ID, STAGES
from .game import Game

# ----------------------------------------------------------------------------
def configure_frozen_streams():
    """PyInstaller windowed applications may have no stdout or stderr."""
    if not getattr(sys, "frozen", False):
        return
    if sys.stdout is None:
        sys.stdout = open(os.devnull, "w", encoding="utf-8")
    if sys.stderr is None:
        try:
            sys.stderr = open(os.path.join(DATA_DIRECTORY, "errors.log"), "a",
                              encoding="utf-8", buffering=1)
        except OSError:
            sys.stderr = open(os.devnull, "w", encoding="utf-8")


def main(argv=None):
    configure_frozen_streams()
    ap = argparse.ArgumentParser(description="NEON VOID")
    ap.add_argument("--no-sound", action="store_true", help="소리 끄기")
    ap.add_argument("--fps", action="store_true", help="FPS 오버레이")
    ap.add_argument("--seed", type=int, default=None, help="난수 시드")
    ap.add_argument("--stage", choices=tuple(STAGES), default=DEFAULT_STAGE_ID,
                    help="스테이지 콘텐츠 ID (현재는 void만 제공)")
    ap.add_argument("--selftest", type=int, default=0,
                    help="N프레임 자동 플레이 후 종료")
    ap.add_argument("--god", action="store_true",
                    help="검증용 무적 (자동 플레이와 함께 사용)")
    ap.add_argument("--volume", type=float, default=None,
                    help="전체 볼륨 0.0 ~ 1.0 (기본 0.9)")
    ap.add_argument("--version", action="version", version=f"NEON VOID {__version__}")
    opts = ap.parse_args(argv)
    if opts.volume is not None:
        opts.volume = clamp(opts.volume, 0.0, 1.0)

    root = tk.Tk()
    game = Game(root, opts)
    if opts.selftest:
        return 0 if game.selftest_run() else 1
    else:
        root.mainloop()
    return 0


if __name__ == "__main__":
    sys.exit(main())
