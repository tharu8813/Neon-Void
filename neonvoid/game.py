"""Simulation and game flow."""

from __future__ import annotations

import math
import random
import time
import traceback
import tkinter as tk
from types import SimpleNamespace
from collections import deque
from .constants import (W, H, DT, TAU, clamp, rnd, C_BG, C_PLAYER, C_BULLET, C_ENEMY, C_SHOOT, C_RUSH, C_BOSS, C_EBULLET, C_SHARD, C_TEXT, C_DIM, C_BAD, C_HP, C_GOLD, F_HUGE, F_BIG, F_MID, F_UI, F_DMG)
from .audio import Audio
from .content.enemies import E_PARAMS, SPAWN_COST
from .content.stages import DEFAULT_STAGE_ID, get_stage, plan_wave
from .content.upgrades import UPGRADES, UP_BY_ID, UP_WEIGHT, apply_upgrade
from .combat import (add_dot, advance_dots, mitigate_damage, skill_cost,
                     skill_cooldown, effective_armor)
from .entities import Player, Enemy, Bullet, Particle, Shard, FText
from .controls import (ACTION_ORDER, DEFAULT_BINDINGS, rebind_action,
                       movement_vector, dash_vector)
from .events import EventBus
from .profile import PlayerProfile, ProfileTracker, load_profile, save_profile
from .render_pool import OvalPool, PolyPool, TextPool, LinePool
from .settings import Settings, load_settings, save_settings
from .storage import load_save, write_save
from .view import VisualMixin
from .display import ScaledCanvas
from .performance import resident_memory_mib
from .settings import DISPLAY_MODES, MIN_WIDTH, MIN_HEIGHT
from .world.session import (WorldSession, START, CPU_EXIT, SECTOR_NAMES,
                            move_on_world, camera_for)
from .content.sectors import SectorCampaign, sector_software_multiplier
from .content.virus_catalog import (ENCRYPTION_PULSE_RADIUS, PATTERNS,
                                    VirusRuntime, MAX_LIVE_ENEMIES)
from .infection import InfectionState
from uuid import uuid4
from .metaprogression import (AntivirusDatabase, award_progress_once,
                              close_run_once, base_stats_for_level, xp_progress)
from .database_storage import (AntivirusDatabaseStore, DatabaseCorruptError,
                               DatabaseStorageError)
from .extraction import (prepare_extraction, commit_extraction_once,
                         select_starting_software, starting_software_levels)
from .os_checkpoint import (CheckpointStore, CheckpointCorruptError,
                            CheckpointConflictError, CheckpointStorageError,
                            validate_checkpoint)

class Game(VisualMixin):
    def __init__(self, root, opts):
        self.root = root
        self.events = EventBus()
        self.opts = opts
        self.stage = get_stage(getattr(opts, "stage", DEFAULT_STAGE_ID))
        self.auto = bool(opts.selftest)
        # 자동 검증(--selftest)·GUI 스모크가 실제 플레이어 저장 파일을
        # 덮어쓰지 않도록, 실행 방식이 결정되는 시점에 고정한다.
        self.persist_progress = not self.auto
        self.selftest_left = int(opts.selftest or 0)
        self.settings = (Settings() if not self.persist_progress else load_settings())
        self.settings_open = False
        self.settings_page = "general"
        self.settings_selected = 0
        self.rebinding_action = None
        self.settings_notice = ""
        self.settings_hits = []
        self.show_fps = bool(opts.fps or self.settings.show_fps)
        cli_volume = getattr(opts, "volume", None)
        self.audio = Audio(enabled=not opts.no_sound and not self.auto,
                           volume=self.settings.volume if cli_volume is None else cli_volume)
        self.settings.volume = self.audio.volume
        self.audio.set_muted(self.settings.muted)
        self.music_started = False
        self.muted = self.settings.muted
        self.mute_t = 0.0
        self.lowhp_t = 0.0

        root.title("NEON VOID")
        root.configure(bg=C_BG)
        root.resizable(True, True)
        root.minsize(MIN_WIDTH, MIN_HEIGHT)
        self._resize_save_after = None
        self._display_change = False
        self._window_presets = ((960, 540), (1280, 720),
                                (1600, 900), (1920, 1080))
        self.render_samples = deque(maxlen=240)
        self.render_ms = 0.0
        self.render_p95_ms = 0.0
        self.canvas_items = 0
        self.memory_mib = None
        self.c = ScaledCanvas(root)
        self.c.pack(fill="both", expand=True)
        self.c.bind("<Configure>", self._on_canvas_resize)
        self._apply_display_mode(initial=True)
        if self.auto:
            # 자동 검증 중에는 창을 띄우지 않는다
            root.withdraw()

        # 자동 검증(--selftest)·GUI 스모크는 실제 플레이어 저장 파일을
        # 덮어쓰면 안 되므로 메모리 전용 기록을 사용한다.
        self.save = ({"best_score": 0, "best_wave": 0, "runs": 0}
                     if not self.persist_progress else load_save())
        self.profile = (PlayerProfile() if not self.persist_progress else load_profile())
        self.profile_tracker = ProfileTracker(
            self.events, self.profile, persist=(None if not self.persist_progress else save_profile)
        )
        self.run_start_frame = 0
        self.rng = random.Random(opts.seed)
        if opts.seed is not None:
            # 연출용 전역 난수도 같은 시드로 고정해야 --seed 리플레이가
            # 화면까지 동일하게 재현된다.
            random.seed(opts.seed)
        self.world = WorldSession()
        self.campaign = None
        self.current_encounter = None
        self.virus_runtime = VirusRuntime()
        self.virus_pending = []
        self.boss_defeated = False
        self.first_combat_help_shown = False
        self.wave_combat_help = False
        self.infection = InfectionState()
        self.purify_uses = 3
        self.database_store = (None if not self.persist_progress else AntivirusDatabaseStore())
        self.database_error = None
        try:
            self.database = (self.database_store.load() if self.database_store
                             else AntivirusDatabase())
        except (DatabaseCorruptError, DatabaseStorageError, OSError) as exc:
            self.database_error = f"{type(exc).__name__}: {exc}"
            self.database_store = None
            # Fail closed: gameplay can still run, but permanent rewards are
            # disabled until the corrupt DB is manually repaired or restored.
            self.database = AntivirusDatabase()
        self.starting_software_id = None
        self.database_open = False
        self.profile_open = False
        self.profile_page = 0
        self.loadout_open = False
        self.database_selected = 0
        self.extraction_open = False
        self.extraction_options = ()
        self.extraction_selected = 0
        self.events.subscribe("wave_cleared", self._on_sector_progress)
        self.events.subscribe("sector_cleared", self._on_sector_progress)
        self.events.subscribe("os_cleaned", self._on_sector_progress)
        self.world_notice = '백신 코어 온라인 · 배포 단말기를 찾아 이동하세요'
        self.world_notice_t = 4.0
        self.message = ""
        self.message_t = 0.0
        self.extraction_fx_t = 0.0
        self.extraction_summary = ""
        self.database_close_error = None
        self.salvage_open = False
        self.salvage_options = ()
        self.salvage_selected = 0
        self.salvage_claimed = False
        # Separate OS checkpoint: never modify the authoritative antivirus DB
        # or existing legacy saves. Selftests remain in-memory by default.
        self.checkpoint_store = (CheckpointStore() if self.persist_progress else None)
        self.checkpoint_pending = None
        self.checkpoint_revision = None
        self.checkpoint_error = None
        self.checkpoint_prompt_open = False
        self.checkpoint_choice = 0
        self.checkpoint_level = 1
        self.checkpoint_timer = 0.0

        self.keys = set()
        self.mx, self.my = W / 2, H / 2 - 120
        self.mouse_down = False

        root.protocol("WM_DELETE_WINDOW", self.close_game)
        root.bind("<KeyPress>", self.on_key)
        root.bind("<KeyRelease>", self.on_key_up)
        root.bind("<FocusOut>", self._on_focus_out)
        self.c.bind("<Motion>", self._screen_motion)
        self.c.bind("<ButtonPress-1>", self._screen_mouse_down)
        self.c.bind("<ButtonRelease-1>", self._screen_mouse_up)
        # 기본 OS 커서를 숨기고 게임 전용 커서를 그린다
        self.c.configure(cursor="none")
        self.c.bind("<Enter>", self._screen_pointer_enter)
        self.c.bind("<Leave>", self._on_pointer_leave)

        # 애니메이션/연출 상태
        self.ui_t = 0.0
        self.cursor_on = True
        self.cursor_hit = 0.0
        self.cursor_hot = False
        self.cursor_trail = []
        self.hover_card = -1
        self.flash_t = 0.0
        self.flash_dur = 0.3
        self.flash_col = C_PLAYER
        self.sweep_t = 0.0
        self.sweep_dur = 0.55
        self.sweep_col = C_PLAYER
        self.hp_ratio_chip = 1.0
        self.score_shown = 0.0
        self.wave_pop = 0.0
        self.combo_pop = 0.0
        self.dash_ready_t = 0.0
        self.prev_charges = 1
        self.banner_total = 1.0
        self.card_t = 0.0
        self.card_anim = []
        self.card_yoff = []
        self.card_borders = []
        self.title_age = 0.0
        self.pause_t = 0.0
        self.go_t = 0.0
        self.go_score = 0
        self.go_kills = 0
        self.go_record = False

        # 배경 (가장 아래 레이어)
        self.stars = []
        self.grid_items = []
        self._build_background()

        # 인월드 허브 장면은 한 번만 만들고, 매 프레임은 위치/색만 갱신한다.
        self._build_hub_scene()
        # 구역 출구 문도 한 번만 만들어 프레임마다 재생성하지 않는다.
        self._build_sector_gate()
        # 인월드 터미널 공통 연출(CRT 스캔/선택 표시)
        self._build_terminal_fx()
        self.term_marker_boxes = {"db": None, "extract": None, "salvage": None}

        # 렌더 순서대로 풀 생성
        self.pool_enemy = PolyPool(self.c, 200, 8, fill=C_ENEMY, outline="")
        self.pool_ring = OvalPool(self.c, 90, fill="", outline=C_BOSS)
        self.pool_warning = OvalPool(self.c, 96, width=2)
        self.pool_particle = OvalPool(self.c, 300, fill="#ffffff", outline="")
        self.pool_ebullet = OvalPool(self.c, 140, fill=C_EBULLET, outline="")
        self.pool_streak = LinePool(self.c, 240, capstyle="round", fill=C_BULLET)
        self.pool_bullet = OvalPool(self.c, 60, fill=C_BULLET, outline="")
        self.pool_shard = OvalPool(self.c, 70, fill=C_SHARD, outline="")
        self.pool_ftext = TextPool(self.c, 40, F_DMG, anchor="center")
        self.pool_line = LinePool(self.c, 16, capstyle="round", fill=C_BULLET)
        self.pool_drone = OvalPool(self.c, 8, fill=C_PLAYER, outline="")

        # 플레이어 표시
        self.ship = self.c.create_polygon(0, 0, 0, 0, 0, 0,
                                          fill=C_PLAYER, outline=C_PLAYER, width=1)
        self.thrust = self.c.create_oval(0, 0, 0, 0, fill="#1c6d7d", outline="")
        self.aim_dot = self.c.create_oval(0, 0, 0, 0, outline=C_PLAYER, width=1)

        # HUD
        self._build_hud()

        self.state = "title" if self.auto else "hub"
        self.t = 0.0
        self.acc = 0.0
        self.last = time.perf_counter()
        self.fps = 60.0
        self.frames = 0

        self.p = Player()
        self.p.x, self.p.y = START
        self.enemies = []
        self.bullets = []
        self.ebullets = []
        self.particles = []
        self.shards = []
        self.ftexts = []
        self.grid = {}
        self.chain_fx = []

        self.wave = 0
        self.score = 0
        self.combo = 0
        self.combo_t = 0.0
        self.levels = {}
        self.spawn_queue = []
        self.wave_done_t = 0.0
        self.hitstop = 0.0
        self.slow = 0.0
        self.shake = 0.0
        self.paused = False
        self.banner = ""
        self.banner_upgrade = ""
        self.banner_t = 0.0
        self.kills = 0
        self.boss = None
        self.cards = []
        self.card_opts = []
        self.auto_pick_t = 0.0
        self.dead_t = 0.0
        self._drone_cd = 0.0

        if self.auto:
            self._build_title()
        self._build_cursor()
        if self.checkpoint_store is not None:
            self._discover_checkpoint()
        if not self.auto:
            self.loop()

    # --------------------------------------------------------------- 화면 모드
    def _apply_display_mode(self, initial=False):
        """Change only the viewport/window; combat keeps logical 1280x720."""
        mode = self.settings.display_mode
        self._display_change = True
        try:
            if mode == "windowed":
                self.root.overrideredirect(False)
                self.root.attributes("-fullscreen", False)
                screenw = max(MIN_WIDTH, self.root.winfo_screenwidth() - 55)
                screenh = max(MIN_HEIGHT, self.root.winfo_screenheight() - 100)
                width = min(self.settings.window_width, screenw)
                height = min(self.settings.window_height, screenh)
                self.root.geometry(f"{width}x{height}")
            elif mode == "fullscreen":
                self.root.overrideredirect(False)
                self.root.attributes("-fullscreen", True)
            else:
                self.root.attributes("-fullscreen", False)
                self.root.overrideredirect(True)
                self.root.geometry(
                    f"{self.root.winfo_screenwidth()}x"
                    f"{self.root.winfo_screenheight()}+0+0"
                )
        except tk.TclError:
            # Some WMs lack borderless/fullscreen. Stay usable in windowed mode.
            self.settings.display_mode = "windowed"
            self.root.overrideredirect(False)
            self.root.attributes("-fullscreen", False)
            self.root.geometry(f"{self.settings.window_width}x"
                               f"{self.settings.window_height}")
        finally:
            self._display_change = False
        if not initial and self.settings_open:
            self._draw_settings()

    def _on_canvas_resize(self, event):
        if event.width < 1 or event.height < 1:
            return
        self.c.set_viewport_size(event.width, event.height)
        if (self.persist_progress and not self._display_change
                and self.settings.display_mode == "windowed"):
            if self._resize_save_after is not None:
                self.root.after_cancel(self._resize_save_after)
            self._resize_save_after = self.root.after(900, self._save_window_size)

    def _save_window_size(self):
        self._resize_save_after = None
        if self.settings.display_mode != "windowed":
            return
        width, height = self.c.winfo_width(), self.c.winfo_height()
        if width < MIN_WIDTH or height < MIN_HEIGHT:
            return
        if (width, height) != (self.settings.window_width,
                               self.settings.window_height):
            self.settings.window_width = width
            self.settings.window_height = height
            if self.persist_progress:
                save_settings(self.settings)

    def toggle_fullscreen(self):
        if self.settings.display_mode == "windowed":
            self._save_window_size()
        self.settings.display_mode = ("windowed" if self.settings.display_mode
                                      == "fullscreen" else "fullscreen")
        self._apply_display_mode()
        self.apply_settings()

    def toggle_borderless(self):
        if self.settings.display_mode == "windowed":
            self._save_window_size()
        self.settings.display_mode = ("windowed" if self.settings.display_mode
                                      == "borderless" else "borderless")
        self._apply_display_mode()
        self.apply_settings()

    def _screen_event(self, event):
        x, y = self.c.game_point(event)
        return SimpleNamespace(x=x, y=y)

    def _screen_pointer_enter(self, event):
        if self.c.is_game_point(event):
            self.c.configure(cursor="none")
            self._on_pointer_enter(self._screen_event(event))
        else:
            self._on_pointer_leave(event)

    def _screen_motion(self, event):
        if self.c.is_game_point(event):
            self.c.configure(cursor="none")
            self.on_motion(self._screen_event(event))
        else:
            self._on_pointer_leave(event)

    def _screen_mouse_down(self, event):
        if self.c.is_game_point(event):
            self.on_mouse_down(self._screen_event(event))
        else:
            self.mouse_down = False

    def _screen_mouse_up(self, event):
        self.on_mouse_up(self._screen_event(event))

    # --------------------------------------------------- OS checkpoint recovery
    def _discover_checkpoint(self):
        """Boot in the safe base; offer resumption only at the deploy terminal."""
        if self.checkpoint_store is None:
            return
        try:
            payload = self.checkpoint_store.load()
        except (CheckpointCorruptError, CheckpointStorageError, OSError) as exc:
            self.checkpoint_error = f"{type(exc).__name__}: {exc}"
            self.announce_world(
                "OS 진행 저장 파일 손상/읽기 실패 · 배포 단말기에서 복구 선택", 8)
            return
        if payload is None:
            return
        self.checkpoint_revision = payload["revision"]
        if self.database_error is not None:
            self.checkpoint_pending = payload
            self.checkpoint_error = "백신 DB 오류 · 영구 보상 복구 전 재개 불가"
            return
        run_id = payload["campaign"]["run_id"]
        try:
            current = self.database_store.load() if self.database_store else self.database
        except (DatabaseCorruptError, DatabaseStorageError, OSError) as exc:
            self.checkpoint_error = f"{type(exc).__name__}: {exc}"
            return
        self.database = current
        if run_id in current.closed_runs:
            # Crash between extraction/death receipt and checkpoint unlink.
            # Never resurrect a run that C already marked terminal.
            if self._clear_checkpoint():
                self.announce_world("종료된 OS 기록 정리 완료 · 새 운영체제 배포 가능", 5)
            return
        self.checkpoint_pending = payload
        self.announce_world("중단된 운영체제 발견 · 배포 단말기에서 이어하기 선택", 8)

    def _checkpoint_payload(self):
        if (self.campaign is None or self.world.room != "os" or
                self.state not in ("hub", "play", "upgrade", "dead")):
            return None
        p = self.p
        hub = self.state == "hub"
        sector = None if hub else self.world.active_sector
        if not hub and sector is None:
            return None
        return {
            "schema_version": 1, "revision": self.checkpoint_revision or 1,
            "campaign": self.campaign.checkpoint(),
            "scene": self.state, "sector": sector,
            "hub_position": list(self.world.hub_position),
            "player_position": [
                max(0.0, min(float(p.x), 2400.0 if hub else float(W))),
                max(0.0, min(float(p.y), 1560.0 if hub else float(H))),
            ],
            "deployed_level": self.checkpoint_level,
            "player": {
                "hp": float(p.hp), "mana": float(p.mana),
                "charges": int(p.charges),
                "fire_cd": max(0.0, float(p.fire_cd)),
                "pulse_cd": max(0.0, float(p.pulse_cd)),
                "guard_cd": max(0.0, float(p.guard_cd)),
                "guard_t": max(0.0, float(p.guard_t)),
            },
            "software_levels": dict(self.levels),
            "pending_cards": [item[0] for item in self.card_opts]
                if self.state == "upgrade" else [],
            "infection": self.infection.snapshot(),
            "purify_uses": self.purify_uses,
            "salvage_claimed": self.salvage_claimed,
            "score": self.score, "kills": self.kills, "combo": self.combo,
            "run_kills": self.profile_tracker.run_kills,
            "run_boss_kills": self.profile_tracker.run_boss_kills,
            "run_best_combo": self.profile_tracker.run_best_combo,
            "elapsed_frames": max(0, self.frames - self.run_start_frame),
        }

    def _persist_checkpoint(self):
        if self.checkpoint_store is None:
            return True
        payload = self._checkpoint_payload()
        if payload is None:
            return True
        try:
            revision = self.checkpoint_store.save(
                payload, expected_revision=self.checkpoint_revision)
        except (CheckpointConflictError, CheckpointCorruptError,
                CheckpointStorageError, ValueError, OSError) as exc:
            self.checkpoint_error = f"{type(exc).__name__}: {exc}"
            self.show_message("OS 저장 충돌/오류 · 다른 게임 창과 저장 파일을 확인하세요")
            return False
        if revision is None:
            self.show_message("OS 진행도 저장 실패 · 데이터는 아직 확정되지 않았습니다")
            return False
        self.checkpoint_revision = revision
        self.checkpoint_error = None
        self.checkpoint_timer = 0.0
        return True

    def _clear_checkpoint(self):
        if self.checkpoint_store is None:
            self.checkpoint_pending = None
            self.checkpoint_revision = None
            return True
        if self.checkpoint_revision is None:
            return True  # Do not delete a checkpoint that we never owned.
        try:
            removed = self.checkpoint_store.clear(
                expected_revision=self.checkpoint_revision)
        except (CheckpointConflictError, CheckpointCorruptError,
                CheckpointStorageError, ValueError, OSError) as exc:
            self.checkpoint_error = f"{type(exc).__name__}: {exc}"
            self.show_message("OS 체크포인트를 정리하지 못했습니다 · 재실행 시 확인 필요")
            return False
        if not removed:
            self.show_message("OS 체크포인트 삭제 실패 · 재실행 시 다시 확인")
            return False
        self.checkpoint_revision = None
        self.checkpoint_pending = None
        return True

    def _open_checkpoint_prompt(self):
        self.checkpoint_prompt_open = True
        self.checkpoint_choice = 0 if self.checkpoint_pending is not None else 1
        self.keys.clear()
        self._draw_checkpoint_prompt()

    def _abandon_pending_checkpoint(self):
        if self.database_error is not None:
            self.show_message("백신 DB 손상 · 기존 OS 기록 보호를 위해 배포 취소")
            return False
        data = self.checkpoint_pending
        if data is None:
            if self.checkpoint_error is not None:
                # Corrupt bytes are never silently overwritten; archive first.
                try:
                    path = self.checkpoint_store.archive_corrupt()
                except (CheckpointStorageError, OSError) as exc:
                    self.show_message(f"손상 파일 보관 실패: {exc}")
                    return False
                self.checkpoint_revision = None
                self.checkpoint_error = None
                if path is not None:
                    self.announce_world("손상된 OS 파일을 .corrupt로 보관했습니다", 5)
            return True
        run_id = data["campaign"]["run_id"]
        status = "dead" if data["scene"] == "dead" else "abandoned"
        try:
            if self.database_store is None:
                updated, result = close_run_once(
                    self.database, run_id=run_id, status=status)
            else:
                updated, result = self.database_store.close_run_once(
                    run_id=run_id, status=status)
        except (DatabaseCorruptError, DatabaseStorageError, OSError, ValueError) as exc:
            self.show_message(f"이전 OS 중단 기록 저장 실패: {exc}")
            return False
        if not (result.applied or result.reason == "run_closed"):
            self.show_message("이전 OS 중단 기록 저장 실패 · 다시 시도하세요")
            return False
        self.database = updated
        if not self._clear_checkpoint():
            return False
        return True

    def _handle_checkpoint_key(self, key):
        if key in ("escape", self.settings.bindings["interact"]):
            self.checkpoint_prompt_open = False
            self.c.delete("resumeui")
            self.keys.clear()
            return
        if key in ("up", "down", "w", "s"):
            self.checkpoint_choice = 1 - self.checkpoint_choice
            self._draw_checkpoint_prompt()
        elif key in ("return", "kp_enter", "space"):
            if self.checkpoint_choice == 0 and self.checkpoint_pending is not None:
                if self.checkpoint_pending["scene"] == "dead":
                    self.show_message("전투에서 사망한 OS는 이어할 수 없습니다 · 새 OS 선택")
                    return
                if self._resume_checkpoint(self.checkpoint_pending):
                    self.checkpoint_prompt_open = False
                    self.c.delete("resumeui")
                return
            if not self._abandon_pending_checkpoint():
                return
            self.checkpoint_prompt_open = False
            self.c.delete("resumeui")
            self.keys.clear()
            self._deploy_new_os()

    def _resume_checkpoint(self, data):
        """Rehydrate the same OS id; only unfinished enemies/waves are replayed."""
        try:
            validate_checkpoint(data)
            if data["scene"] == "dead":
                self.show_message("종료된 OS는 복원할 수 없습니다")
                return False
            fresh = self.database_store.load() if self.database_store else self.database
            if data["campaign"]["run_id"] in fresh.closed_runs:
                self.show_message("이미 종료된 OS입니다 · 새 운영체제를 배포하세요")
                self.database = fresh
                self._clear_checkpoint()
                return False
            campaign = SectorCampaign.from_checkpoint(
                data["campaign"], events=self.events)
        except (ValueError, DatabaseCorruptError, DatabaseStorageError, OSError) as exc:
            self.show_message(f"OS 복구 검증 실패: {exc}")
            return False
        self.database = fresh

        # C's signed-off receipts can be one event newer than the checkpoint
        # when the process crashed after XP commit but before OS file fsync.
        advanced = False
        for name, progress in campaign.progress.items():
            db_sid = self._DB_SECTOR_IDS[name]
            confirmed = 0
            for idx in range(1, 6):
                if f"{campaign.run_id}:{db_sid}:wave:{idx}" not in fresh.awarded_xp_ids:
                    break
                confirmed = idx
            if confirmed > progress.completed_waves:
                progress.completed_waves = confirmed
                advanced = True
        campaign._os_clean_emitted = campaign.os_clean
        self.campaign = campaign
        self.world.room = "os"
        self.world.active_sector = None
        self.world.hub_position = tuple(data["hub_position"])
        self.world.sector_waves = {
            name.lower(): progress.completed_waves
            for name, progress in campaign.progress.items()}
        self.world.sector_clean = {
            sid.lower() for sid, progress in campaign.progress.items()
            if progress.is_clean
        }
        self.checkpoint_level = data["deployed_level"]
        base = base_stats_for_level(self.checkpoint_level)
        self.p = Player()
        self.p.maxhp = self.p.hp = base["maxhp"]
        self.p.st["dmg"], self.p.st["armor"] = base["dmg"], base["armor"]
        self.p.maxmana = self.p.mana = base["maxmana"]
        self.levels = {}
        for uid, count in sorted(data["software_levels"].items()):
            for _ in range(count):
                apply_upgrade(self, uid)
        for field in ("hp", "mana", "charges", "fire_cd",
                      "pulse_cd", "guard_cd", "guard_t"):
            setattr(self.p, field, data["player"][field])
        self.p.x, self.p.y = data["player_position"]
        self.p.vx = self.p.vy = 0.0
        self.infection = InfectionState.from_snapshot(data["infection"])
        self.purify_uses = data["purify_uses"]
        self.salvage_claimed = data["salvage_claimed"]
        self.score, self.kills, self.combo = (
            data["score"], data["kills"], data["combo"])
        self.combo_t = 0.0
        self.boss = None
        self.boss_defeated = False
        self.enemies.clear()
        self.bullets.clear()
        self.ebullets.clear()
        self.spawn_queue.clear()
        self.particles.clear()
        self.shards.clear()
        self.chain_fx.clear()
        self.ftexts.clear()
        self.virus_runtime.reset_wave()
        self.virus_pending.clear()
        self.current_encounter = None
        self.card_opts = []
        self.cards = []
        self.paused = False
        self.keys.clear()
        self.run_start_frame = self.frames - data["elapsed_frames"]
        self.events.emit("run_started", runs=self.save["runs"])
        self.profile_tracker.run_kills = data["run_kills"]
        self.profile_tracker.run_boss_kills = data["run_boss_kills"]
        self.profile_tracker.run_best_combo = data["run_best_combo"]
        if not self.music_started:
            self.music_started = True
            self.audio.start_music()

        sector = data["sector"]
        self.state = "hub"
        self.checkpoint_revision = data["revision"]
        self._show_hud(False)
        if sector is None:
            self.p.x, self.p.y = data["player_position"]
        else:
            self.world.active_sector = sector
            campaign.enter_sector(sector.upper())
            completed = campaign.progress[sector.upper()].completed_waves
            if completed == 5:
                self.state = "play"
                self.wave = 5
                self._show_hud(True)
                self._show_sector_clear(sector)
            elif (data["scene"] == "upgrade" or advanced and
                  completed >= 1 and completed != data["campaign"]["completed_waves"][sector.upper()]):
                self.state = "upgrade"
                self.wave = completed
                if (data["scene"] == "upgrade" and not advanced and
                        completed == data["campaign"]["completed_waves"][sector.upper()]):
                    ids = data["pending_cards"]
                else:
                    ids = [item[0] for item in self.roll_upgrades(3)]
                self.card_opts = [UP_BY_ID[uid] if uid != "bonus" else
                                  ("bonus", "잔여 신호", "점수 +250 즉시 획득", 0, 99)
                                  for uid in ids]
                self.build_upgrade_screen()
            else:
                self.start_wave(completed + 1)

        self.checkpoint_revision = data["revision"]
        self.checkpoint_pending = None
        self.checkpoint_error = None
        self.checkpoint_prompt_open = False
        self.c.delete("resumeui")
        self.sync_campaign_rewards()
        self._persist_checkpoint()
        self.announce_world("이전 OS 진행도를 복구했습니다 · 미완료 웨이브만 재시도", 6)
        return True

    # --------------------------------------------------- 영구 백신 DB / 보상
    _DB_SECTOR_IDS = {
        "CPU": "cpu_core",
        "MEMORY": "memory_sector",
        "STORAGE": "storage_vault",
        "NETWORK": "network_grid",
    }

    def _reward_db(self, run_id, event, sector_id=None, wave_index=None):
        """Persist exactly-once sector rewards; never claim success on I/O error."""
        if self.database_error is not None:
            return None
        try:
            arguments = dict(run_id=run_id, event=event,
                             sector_id=sector_id, wave_index=wave_index)
            if self.database_store is None:
                updated, result = award_progress_once(self.database, **arguments)
            else:
                updated, result = self.database_store.award_progress_once(**arguments)
        except (DatabaseCorruptError, DatabaseStorageError, OSError, ValueError) as exc:
            self.database_error = f"{type(exc).__name__}: {exc}"
            self.announce_world("백신 DB 보상 기록 오류 · 데이터베이스를 확인하세요", 5)
            self.show_message("백신 DB 보상 기록 오류 · 영구 보상이 저장되지 않았습니다")
            return None
        if result.applied:
            self.database = updated
            self.events.emit("database_xp_awarded", run_id=run_id,
                             event=event, amount=result.amount)
            if result.level_after > result.level_before:
                self.events.emit("database_leveled", run_id=run_id,
                                 level=result.level_after)
                self.announce_world(
                    f"백신 데이터베이스 LEVEL {result.level_after} · 다음 배포부터 능력치 상승",
                    4)
        elif result.reason == "save_failed":
            self.announce_world("경험치 저장 실패 · 재시도 시 복구할 수 있습니다", 5)
        return result

    def _on_sector_progress(self, event):
        """B's campaign events are the verified prerequisite chain for C."""
        if self.campaign is None or event.payload.get("run_id") != self.campaign.run_id:
            return
        sector = event.payload.get("sector_id")
        db_sector = self._DB_SECTOR_IDS.get(sector)
        if event.name == "wave_cleared" and db_sector:
            wave_index = event.payload["wave_index"]
            self._reward_db(self.campaign.run_id, "wave_cleared",
                            sector_id=db_sector, wave_index=wave_index)
            if wave_index == 5 and event.payload.get("boss_kind"):
                self._reward_db(self.campaign.run_id, "boss_killed",
                                sector_id=db_sector)
        elif event.name == "sector_cleared" and db_sector:
            self._reward_db(self.campaign.run_id, "sector_cleared",
                            sector_id=db_sector)
        elif event.name == "os_cleaned":
            self._reward_db(self.campaign.run_id, "os_cleaned")

    def sync_campaign_rewards(self):
        """Retry only previously confirmed progress when an earlier save failed.

        The persistent idempotency receipts prevent replay from duplicating XP.
        Unfinished waves cannot be awarded by this reconciliation step.
        """
        if self.campaign is None or self.database_error is not None:
            return
        for sector, state in self.campaign.progress.items():
            db_sector = self._DB_SECTOR_IDS.get(sector)
            if db_sector is None:
                continue
            for wave in range(1, state.completed_waves + 1):
                self._reward_db(self.campaign.run_id, "wave_cleared",
                                sector_id=db_sector, wave_index=wave)
            if state.is_clean:
                self._reward_db(self.campaign.run_id, "boss_killed",
                                sector_id=db_sector)
                self._reward_db(self.campaign.run_id, "sector_cleared",
                                sector_id=db_sector)
        if self.campaign.os_clean:
            self._reward_db(self.campaign.run_id, "os_cleaned")

    def _close_database_run(self, status):
        """Never mistake a failed terminal receipt write for successful closure.

        Retry a transient failure a bounded number of times; retain the
        campaign on failure so the user can explicitly retry or quit after
        seeing an error. A previously closed run is an idempotent success.
        """
        if self.campaign is None:
            self.database_close_error = None
            return True
        if self.database_error is not None:
            self.database_close_error = self.database_error
            self.announce_world("백신 DB 오류 · 런 종료 기록 미확정", 6)
            self.show_message("백신 DB 오류 · 런 종료 기록이 확정되지 않았습니다")
            return False
        for _ in range(3):
            try:
                if self.database_store is None:
                    updated, result = close_run_once(
                        self.database, run_id=self.campaign.run_id, status=status)
                else:
                    updated, result = self.database_store.close_run_once(
                        run_id=self.campaign.run_id, status=status)
            except (DatabaseCorruptError, DatabaseStorageError, OSError) as exc:
                self.database_error = f"{type(exc).__name__}: {exc}"
                self.database_close_error = self.database_error
                self.announce_world("백신 DB 종료 기록 손상 · 복구 후 재시도", 6)
                return False
            if result.applied or result.reason == "run_closed":
                self.database = updated
                self.database_close_error = None
                return True
            if result.reason != "save_failed":
                self.database_close_error = result.reason
                self.announce_world("백신 DB 종료 기록 거부 · " + result.reason, 6)
                self.show_message("백신 DB 종료 기록 거부 · " + result.reason)
                return False
        self.database_close_error = "save_failed"
        self.announce_world("런 종료 기록을 저장하지 못했습니다 · Enter/R 재시도", 6)
        self.show_message("런 종료 기록을 저장하지 못했습니다 · Enter/R 로 재시도")
        return False

    def _database_options(self):
        return (None,) + tuple(sorted(self.database.software_archive))

    def open_database_terminal(self):
        self.database_open = True
        self.database_selected = 0
        self.keys.clear()
        self._draw_database_terminal()

    def open_profile_terminal(self):
        self.profile_open = True
        self.profile_page = 0
        self.keys.clear()
        self._draw_profile_terminal()

    def _profile_page_count(self):
        count = len(self.profile.recent_runs)
        size = self.PROFILE_PAGE_SIZE
        return max(1, (count + size - 1) // size)

    def _handle_profile_key(self, key):
        if key in ("escape", self.settings.bindings["interact"]):
            self.profile_open = False
            self.c.delete("profileui")
            self.keys.clear()
        elif key in ("left", "right"):
            step = -1 if key == "left" else 1
            page = max(0, min(self._profile_page_count() - 1, self.profile_page + step))
            if page != self.profile_page:
                self.profile_page = page
                self._draw_profile_terminal()

    def _handle_database_key(self, key):
        if key in ("escape", self.settings.bindings["interact"], "o"):
            self.database_open = False
            self.c.delete("dbui")
            self.keys.clear()
            return
        options = self._database_options()
        if key in ("up", "w"):
            self.database_selected = (self.database_selected - 1) % len(options)
        elif key in ("down", "s"):
            self.database_selected = (self.database_selected + 1) % len(options)
        elif key in ("return", "kp_enter", "space"):
            chosen = options[self.database_selected]
            try:
                self.starting_software_id = select_starting_software(
                    self.database, chosen)
                self.announce_world(
                    f"다음 배포 소프트웨어: {UP_BY_ID[chosen][1] if chosen else '기본 장비'}")
            except ValueError:
                self.announce_world("영구 보관되지 않은 소프트웨어입니다")
            self.database_open = False
            self.c.delete("dbui")
            self.keys.clear()
            return
        self._draw_database_terminal()

    def open_extraction_terminal(self):
        """Only the in-world OS CLEAN terminal can offer a single extraction."""
        if not self.world.os_clean or self.campaign is None:
            self.announce_world("모든 시스템 구역을 정화한 후 철수할 수 있습니다")
            return
        self.sync_campaign_rewards()
        if self.database_error is not None:
            self.announce_world("백신 DB 손상/저장 오류 · 철수를 완료할 수 없습니다", 5)
            return
        try:
            fresh = self.database_store.load() if self.database_store else self.database
            offer = prepare_extraction(
                fresh, run_id=self.campaign.run_id,
                clean_sector_ids=self._DB_SECTOR_IDS.values(),
                software_levels=dict(self.levels))
        except (DatabaseCorruptError, DatabaseStorageError, OSError, ValueError) as exc:
            self.announce_world(f"철수 검증 실패: {type(exc).__name__}", 5)
            return
        if not offer.allowed:
            if offer.reason in ("no_software", "archive_full"):
                self.database = fresh
                self.open_salvage_terminal(offer.reason)
            else:
                self.announce_world(f"철수 불가: {offer.reason}", 5)
            return
        self.database = fresh
        self.extraction_options = offer.eligible_software
        self.extraction_selected = 0
        self.extraction_open = True
        self.keys.clear()
        self._draw_extraction_terminal()

    def open_salvage_terminal(self, reason):
        """A legal, one-time OS CLEAN software-recovery choice, not a C reward.

        Only shown after C verifies all durable clean receipts. The selected
        existing module is added via the same apply_upgrade path as wave loot,
        then C independently checks it is owned before allowing extraction.
        If the *entire* archive is capped, the only choice is explicit
        no-reward return; never bypass permanent archive stack limits.
        """
        if reason not in ("no_software", "archive_full"):
            raise ValueError("Not a recoverable software situation")
        candidates = ()
        if not self.salvage_claimed:
            candidates = tuple(sorted(
                uid for uid in UP_BY_ID
                if self.levels.get(uid, 0) == 0 and
                self.database.software_archive.get(uid, 0) < UP_BY_ID[uid][4]
            ))
        self.salvage_options = candidates
        self.salvage_selected = 0
        self.salvage_open = True
        self.keys.clear()
        self._draw_salvage_terminal()

    def _handle_salvage_key(self, key):
        if key in ("escape", self.settings.bindings["interact"]):
            self.salvage_open = False
            self.c.delete("salvageui")
            self.keys.clear()
            return
        if key in ("up", "w") and self.salvage_options:
            self.salvage_selected = (self.salvage_selected-1) % len(self.salvage_options)
        elif key in ("down", "s") and self.salvage_options:
            self.salvage_selected = (self.salvage_selected+1) % len(self.salvage_options)
        elif key in ("return", "kp_enter", "space"):
            if not self.salvage_options:
                if self.return_to_base(abandon=True):
                    self.announce_world(
                        "OS CLEAN · 보관 한도 도달 · 보상 없이 복귀 / XP 유지", 6)
                else:
                    self._draw_salvage_terminal()
                return
            uid = self.salvage_options[self.salvage_selected]
            try:
                latest = self.database_store.load() if self.database_store else self.database
            except (DatabaseCorruptError, DatabaseStorageError, OSError) as exc:
                self.database_error = f"{type(exc).__name__}: {exc}"
                self.show_message("소프트웨어 회수 불가 · DB 읽기 오류", 5)
                return
            if (latest.software_archive.get(uid, 0) >= UP_BY_ID[uid][4] or
                    self.levels.get(uid, 0) != 0):
                self.database = latest
                self.show_message("보관 한도가 변경됐습니다 · 다시 확인하세요", 5)
                self.open_salvage_terminal("archive_full")
                return
            # Apply an actual available game upgrade, never edit the C archive.
            apply_upgrade(self, uid)
            self.salvage_claimed = True
            self._persist_checkpoint()
            self.salvage_open = False
            self.c.delete("salvageui")
            self.keys.clear()
            self.announce_world(
                f"OS CLEAN 소프트웨어 회수: {UP_BY_ID[uid][1]} · 이제 철수 선택 가능", 5)
            self.open_extraction_terminal()
            return
        self._draw_salvage_terminal()

    def _handle_extraction_key(self, key):
        if key == "escape" or key == self.settings.bindings["interact"]:
            self.extraction_open = False
            self.c.delete("extractui")
            self.keys.clear()
            return
        if key in ("up", "w"):
            self.extraction_selected = (self.extraction_selected - 1) % len(self.extraction_options)
        elif key in ("down", "s"):
            self.extraction_selected = (self.extraction_selected + 1) % len(self.extraction_options)
        elif key in ("return", "kp_enter", "space"):
            software_id = self.extraction_options[self.extraction_selected]
            run_id = self.campaign.run_id
            kwargs = dict(
                run_id=run_id, clean_sector_ids=self._DB_SECTOR_IDS.values(),
                software_levels=dict(self.levels),
                selected_software_id=software_id,
            )
            try:
                if self.database_store is None:
                    updated, result = commit_extraction_once(self.database, **kwargs)
                else:
                    updated, result = self.database_store.commit_extraction_once(**kwargs)
            except (DatabaseCorruptError, DatabaseStorageError, OSError, ValueError) as exc:
                self.show_message(f"영구 보관 저장 오류: {type(exc).__name__}", 5)
                return
            if not result.applied:
                self.show_message(f"철수 저장 거부: {result.reason}", 5)
                return
            # The game may leave the OS ONLY after the durable receipt exists.
            self.database = updated
            self.extraction_open = False
            self.c.delete("extractui")
            self.keys.clear()
            self.events.emit("extraction_committed", run_id=run_id,
                             software=software_id, level=self.database.antivirus_level)
            is_record = self._record_best()
            run_xp = sum(amount for receipt, amount in updated.awarded_xp_ids.items()
                         if receipt.startswith(f"{run_id}:"))
            self.extraction_summary = (
                f"점수 {self.score:,}  ·  정화 {len(self.world.sector_clean)}/4  ·  XP +{run_xp:,}"
                f"{'  ·  최고 점수' if is_record else ''}\n"
                f"영구 보관  {UP_BY_ID[software_id][1]}")
            self.events.emit(
                "run_extracted", score=self.score, wave=self.wave,
                duration_frames=self.frames - self.run_start_frame)
            self.return_to_base()
            self.extraction_fx_t = 2.5
            self.screen_flash(0.38, C_HP)
            self.screen_sweep(1.2, C_HP)
            self.announce_world(
                f"OS CLEAN · 백신 철수 완료 · {UP_BY_ID[software_id][1]} 영구 보관",
                6)
            return
        self._draw_extraction_terminal()

    # ---------------------------------------------------------- 실행 기록
    def _abandon_active_run(self):
        if self.campaign is not None and not self._close_database_run("abandoned"):
            return False
        if self.profile_tracker.active:
            self.events.emit("run_abandoned", score=self.score, wave=self.wave,
                             duration_frames=self.frames - self.run_start_frame)
        return True

    def close_game(self):
        """Ordinary quit SUSPENDS an OS; explicit abandon/death closes its DB."""
        if (self.campaign is not None and self.state in ("hub", "play", "upgrade")
                and self.checkpoint_store is not None):
            if not self._persist_checkpoint():
                from tkinter import messagebox
                if not messagebox.askyesno(
                    "NEON VOID · OS 저장 실패",
                    "진행 중인 운영체제를 저장하지 못했습니다.\n"
                    "아니오: 게임으로 돌아가 저장 재시도\n"
                    "예: 이번 OS를 포기하고 종료 (확정 XP 유지)",
                    parent=self.root,
                ):
                    return
                if not self._abandon_active_run():
                    self.show_message("포기 기록 저장도 실패했습니다 · 다시 시도하세요")
                    return
                self._clear_checkpoint()
        else:
            closed = (self._close_database_run("dead") if
                      self.campaign is not None and self.state == "dead" else
                      self._abandon_active_run())
            if not closed and not self.auto:
                from tkinter import messagebox
                if not messagebox.askyesno(
                    "NEON VOID · 저장 실패",
                    "운영체제 종료 기록을 백신 데이터베이스에 저장하지 못했습니다. "
                    "다시 시도하려면 '아니오'를 누르세요. "
                    "저장되지 않은 종료 기록을 포기하고 종료할까요?",
                    parent=self.root,
                ):
                    return
        try:
            self.audio.close()
        except Exception:
            pass
        self.root.destroy()

    # --------------------------------------------------------------- 설정
    def open_settings(self):
        """Show the overlay only from the title or a paused run."""
        if self.settings_open:
            return
        if self.state not in ("title", "hub") and not (self.state == "play" and self.paused):
            return
        self.settings_open = True
        self.settings_opened_at = self.ui_t
        self.settings_page = "general"
        self.settings_selected = 0
        self.rebinding_action = None
        self.settings_notice = ""
        self.keys.clear()
        self.mouse_down = False
        self._draw_settings()

    def close_settings(self):
        if self.settings_open:
            self.settings_open = False
            self.rebinding_action = None
            self.settings_notice = ""
            self.settings_hits = []
            self.c.delete("cfg")
            self.keys.clear()
            if self.state == "title":
                self._build_title()
            elif self.paused:
                self._draw_pause()

    def apply_settings(self):
        """Apply visual/audio preferences immediately and persist changes."""
        self.audio.set_volume(self.settings.volume)
        self.muted = self.audio.set_muted(self.settings.muted)
        self.show_fps = bool(self.opts.fps or self.settings.show_fps)
        self.mute_t = 1.8
        if self.persist_progress and not save_settings(self.settings):
            if self.settings_open:
                self.settings_notice = "설정을 저장하지 못했습니다 · 현재 실행에만 적용됩니다."
            else:
                self.show_message("설정을 저장하지 못했습니다 · 현재 실행에만 적용됩니다.")
        if self.settings_open:
            self._draw_settings()

    def change_setting(self, index, direction=1):
        if index == 0:
            old_step = round(self.settings.volume * 10)
            new_step = max(0, min(10, old_step + direction))
            self.settings.volume = new_step / 10.0
        elif index == 1:
            self.settings.muted = not self.settings.muted
        elif index == 2:
            self.settings.reduced_effects = not self.settings.reduced_effects
        elif index == 3:
            if self.opts.fps:
                return
            self.settings.show_fps = not self.settings.show_fps
        elif index == 4:
            if self.settings.display_mode == "windowed":
                self._save_window_size()
            i = DISPLAY_MODES.index(self.settings.display_mode)
            self.settings.display_mode = DISPLAY_MODES[(i + direction) % len(DISPLAY_MODES)]
            self._apply_display_mode()
        elif index == 5:
            old = (self.settings.window_width, self.settings.window_height)
            position = min(range(len(self._window_presets)),
                           key=lambda i: abs(self._window_presets[i][0] - old[0])
                           + abs(self._window_presets[i][1] - old[1]))
            width, height = self._window_presets[
                (position + direction) % len(self._window_presets)]
            self.settings.window_width = width
            self.settings.window_height = height
            if self.settings.display_mode == "windowed":
                self._apply_display_mode()
        else:
            return
        self.apply_settings()

    def _handle_settings_key(self, key):
        if self.rebinding_action is not None:
            if key == "escape":
                self.rebinding_action = None
                self.settings_notice = "키 변경을 취소했습니다."
            else:
                try:
                    self.settings.bindings = rebind_action(
                        self.settings.bindings, self.rebinding_action, key
                    )
                except ValueError:
                    self.settings_notice = "사용할 수 없거나 이미 지정된 키입니다."
                else:
                    self.settings_notice = "변경한 키를 저장했습니다."
                    self.rebinding_action = None
                    self.apply_settings()
            if self.rebinding_action is not None or key == "escape":
                self._draw_settings()
            return

        if key in ("escape", "o"):
            self.close_settings()
            return
        if key == "tab":
            self.settings_page = ("controls" if self.settings_page == "general"
                                  else "general")
            self.settings_selected = 0
            self.settings_notice = ""
            self._draw_settings()
            return

        count = len(ACTION_ORDER) if self.settings_page == "controls" else 6
        if key in ("up", "w"):
            self.settings_selected = (self.settings_selected - 1) % count
            self._draw_settings()
        elif key in ("down", "s"):
            self.settings_selected = (self.settings_selected + 1) % count
            self._draw_settings()
        elif self.settings_page == "controls":
            if key in ("return", "kp_enter", "space"):
                self.rebinding_action = ACTION_ORDER[self.settings_selected]
                self.settings_notice = "새로 사용할 키를 누르세요. Esc는 취소."
                self._draw_settings()
            elif key in ("delete", "backspace"):
                self.settings.bindings = DEFAULT_BINDINGS.copy()
                self.settings_notice = "기본 키 설정을 복원했습니다."
                self.keys.clear()
                self.apply_settings()
        elif key in ("left", "a"):
            self.change_setting(self.settings_selected, -1)
        elif key in ("right", "d", "return", "kp_enter", "space"):
            self.change_setting(self.settings_selected, 1)

    # ------------------------------------------------------------------ 입력
    def on_key(self, ev):
        key = ev.keysym.lower()
        if self.rebinding_action is None:
            if key == "f11":
                self.toggle_fullscreen()
                return
            if key in ("return", "kp_enter") and (getattr(ev, "state", 0) & 0x0008):
                self.toggle_borderless()
                return
        self._handle_key(key)

    def _handle_key(self, key, *, pointer=False):
        if self.checkpoint_prompt_open:
            self._handle_checkpoint_key(key)
            return
        if self.salvage_open:
            self._handle_salvage_key(key)
            return
        if self.extraction_open:
            self._handle_extraction_key(key)
            return
        if self.database_open:
            self._handle_database_key(key)
            return
        if self.profile_open:
            self._handle_profile_key(key)
            return
        if self.settings_open:
            self._handle_settings_key(key)
            return
        if self.loadout_open:
            if key == "escape":
                self.loadout_open = False
                self.c.delete("loadoutui")
                self.keys.clear()
            return

        if key == "tab" and (self.state == "upgrade" or
                             (self.paused and self.state in ("play", "hub"))):
            self.loadout_open = True
            self.keys.clear()
            self.hover_card = -1
            self._draw_loadout()
            return

        if key == "o" and (self.state in ("title", "hub") or
                           (self.state == "play" and self.paused)):
            self.open_settings()
            return

        was_held = not pointer and key in self.keys
        if not pointer:
            self.keys.add(key)

        # System commands remain fixed, so configuration can never hide them.
        if key == "m":
            if not was_held:
                self.settings.muted = not self.settings.muted
                self.apply_settings()
            return

        if self.state == "title":
            if key in ("return", "kp_enter", "space"):
                if not self.new_game():
                    self.show_message(
                        "런 종료 기록을 저장하지 못했습니다 · 저장 후 다시 시도하세요")
            elif key == "escape":
                self.close_game()
            return

        if key == self.settings.bindings["pause"] or (
            key == "escape" and self.state in ("play", "hub")):
            if self.state in ("play", "hub") and not was_held:
                self.paused = not self.paused
                self.mouse_down = False
                self.pause_t = 0.0
                self._draw_pause()
            return

        if self.state == "dead":
            if key in ("r", "return", "space", "escape"):
                if self.auto:
                    self.new_game()
                elif not self.return_to_base():
                    self.show_message(
                        "런 종료 기록을 저장하지 못했습니다 · Enter로 다시 시도하세요")
            return

        if self.state == "hub":
            if self.paused:
                return
            if key == self.settings.bindings["interact"] and not was_held:
                self.interact_hub()
            elif key == self.settings.bindings["dash"] or key in ("shift_l", "shift_r"):
                if not was_held:
                    self.try_dash()
            return

        if self.state == "upgrade":
            if key in ("1", "2", "3"):
                index = int(key) - 1
                if index < len(self.card_opts):
                    self.pick_upgrade(index)
            return

        if self.state == "play" and not self.paused:
            if key == self.settings.bindings["interact"] and not was_held:
                if self.world.active_sector in SECTOR_NAMES and (
                    math.hypot(self.p.x - CPU_EXIT[0],
                               self.p.y - CPU_EXIT[1]) < 112.0):
                    self.leave_sector()
                return
            if key == self.settings.bindings["dash"] or key in ("shift_l", "shift_r"):
                if not was_held:
                    self.try_dash()
            elif key == self.settings.bindings["skill_pulse"] and not was_held:
                self.try_cast_skill("pulse")
            elif key == self.settings.bindings["skill_guard"] and not was_held:
                self.try_cast_skill("guard")
            elif key == "r" and not was_held:
                if self.auto:
                    self.new_game()
                else:
                    self.return_to_base(abandon=True)

    def on_key_up(self, ev):
        self.keys.discard(ev.keysym.lower())

    def on_motion(self, ev):
        if (self._aim_cursor() and not self.settings.reduced_effects
                and (ev.x - self.mx) ** 2 + (ev.y - self.my) ** 2 > 49):
            self.cursor_trail.append((self.mx, self.my, 0.26))
            self.cursor_trail = self.cursor_trail[-8:]
        self.mx, self.my = ev.x, ev.y
        self.cursor_on = True
        if self.loadout_open:
            return
        if self.settings_open:
            if self.rebinding_action is not None:
                return
            for idx, (x0, y0, x1, y1) in enumerate(self.settings_hits):
                if x0 <= ev.x <= x1 and y0 <= ev.y <= y1:
                    if self.settings_selected != idx:
                        self.settings_selected = idx
                        self._draw_settings()
                    break
            return
        if self.state == "upgrade":
            self.hover_card = -1
            for idx, (x0, y0, x1, y1) in enumerate(self.cards):
                off = self.card_yoff[idx] if idx < len(self.card_yoff) else 0.0
                if x0 <= ev.x <= x1 and y0 + off <= ev.y <= y1 + off:
                    self.hover_card = idx
                    break
        else:
            self.hover_card = -1

    def on_mouse_down(self, ev):
        if self.checkpoint_prompt_open:
            if 300 <= ev.x <= W-300 and 288 <= ev.y <= 414:
                self.checkpoint_choice = 0 if ev.y < 354 else 1
                self._draw_checkpoint_prompt()
            elif 490 <= ev.x <= 790 and 450 <= ev.y <= 510:
                self._handle_checkpoint_key("return")
            return
        self.mouse_down = self._aim_cursor()
        action = self._menu_action_at(ev.x, ev.y)
        if action is not None:
            if self.rebinding_action is not None and action != "escape":
                return
            self._handle_key(action, pointer=True)
            return
        if self.loadout_open:
            return
        if self.salvage_open:
            self._select_terminal_row("salvageui", "salvage_selected",
                                      self._draw_salvage_terminal, ev.x, ev.y)
            return
        if self.extraction_open:
            self._select_terminal_row("extractui", "extraction_selected",
                                      self._draw_extraction_terminal, ev.x, ev.y)
            return
        if self.database_open:
            self._select_terminal_row("dbui", "database_selected",
                                      self._draw_database_terminal, ev.x, ev.y)
            return
        if self.settings_open:
            if self.rebinding_action is not None:
                return
            for idx, (x0, y0, x1, y1) in enumerate(self.settings_hits):
                if x0 <= ev.x <= x1 and y0 <= ev.y <= y1:
                    self.settings_selected = idx
                    if self.settings_page == "controls":
                        self.rebinding_action = ACTION_ORDER[idx]
                        self.settings_notice = "새로 사용할 키를 누르세요. Esc는 취소."
                        self._draw_settings()
                    else:
                        direction = -1 if idx in (0, 4, 5) and ev.x < W / 2 else 1
                        self.change_setting(idx, direction)
                    break
            return
        if self.state == "upgrade":
            for idx, (x0, y0, x1, y1) in enumerate(self.cards):
                off = self.card_yoff[idx] if idx < len(self.card_yoff) else 0.0
                if x0 <= ev.x <= x1 and y0 + off <= ev.y <= y1 + off:
                    self.pick_upgrade(idx)
                    break

    def on_mouse_up(self, ev):
        self.mouse_down = False

    def _select_terminal_row(self, layer, field, redraw, x, y):
        for item in self.c.find_overlapping(x, y, x, y):
            tags = self.c.gettags(item)
            if layer in tags and "terminal-row" in tags:
                setattr(self, field, int(tags[2].removeprefix("row:")))
                redraw()
                return

    # ------------------------------------------------------------------ 상태
    def new_game(self, *, start_combat=True):
        if not self._abandon_active_run():
            return False
        self.run_start_frame = self.frames
        self.extraction_fx_t = 0.0
        self.extraction_summary = ""
        # 이전 런의 캠페인/바이러스 상태를 남겨두면 재사용된 메모리 주소로
        # 바이러스 종류 충돌이 발생할 수 있으므로 반드시 초기화한다.
        self.campaign = None
        self.current_encounter = None
        self.virus_runtime.reset_wave()
        self.virus_pending.clear()
        self.boss_defeated = False
        c = self.c
        c.delete("scr")
        c.delete("ovl")
        self._show_hud(True)
        self.state = "play"
        self.paused = False
        self.p = Player()
        self.enemies = []
        self.bullets = []
        self.ebullets = []
        self.particles = []
        self.shards = []
        self.ftexts = []
        self.chain_fx = []
        self.levels = {}
        self.wave = 0
        self.score = 0
        self.combo = 0
        self.combo_t = 0.0
        self.hitstop = 0.0
        self.slow = 0.0
        self.shake = 0.0
        self.kills = 0
        self.boss = None
        self.spawn_queue = []
        self.wave_done_t = 0.0
        self.banner = ""
        self.banner_t = 0.0
        self.dead_t = 0.0
        self.lowhp_t = 0.0
        self.hp_ratio_chip = 1.0
        self.score_shown = 0.0
        self.wave_pop = 0.0
        self.combo_pop = 0.0
        self.dash_ready_t = 0.0
        self.prev_charges = 1
        self.hover_card = -1
        self.card_anim = []
        self.card_yoff = []
        self.card_borders = []
        self.cursor_trail = []
        self.pause_t = 0.0
        self.go_t = 0.0
        self.go_score = 0
        self.go_kills = 0
        self.go_record = False
        self.cursor_hit = 0.0
        self.card_t = 0.0
        self.screen_flash(0.32, C_PLAYER)
        self.screen_sweep(0.5, C_PLAYER)
        self.save["runs"] += 1
        if self.persist_progress:
            write_save(self.save)
        self.events.emit("run_started", runs=self.save["runs"])
        if not self.music_started:
            self.music_started = True
            self.audio.start_music()
        if start_combat:
            self.start_wave(1)
        else:
            self.state = "hub"
            self._show_hud(False)
        return True

    # ------------------------------------------------------ 플레이 가능한 월드
    def announce_world(self, message, duration=3.0):
        self.world_notice = message
        self.world_notice_t = duration

    def show_message(self, message, duration=6.0):
        """Surface an error on every screen, not only inside the hub."""
        self.message = message
        self.message_t = duration

    def return_to_base(self, abandon=False):
        if abandon:
            if not self._abandon_active_run():
                return False
        elif self.state == "dead" and self.campaign is not None:
            if not self._close_database_run("dead"):
                return False
        self.world.reset_to_base()
        self.database_open = False
        self.profile_open = False
        self.loadout_open = False
        self.extraction_open = False
        self.salvage_open = False
        self.c.delete("salvageui")
        self.c.delete("dbui")
        self.c.delete("profileui")
        self.c.delete("loadoutui")
        self.c.delete("extractui")
        self.campaign = None
        self.current_encounter = None
        self.virus_runtime.reset_wave()
        self.virus_pending.clear()
        self.boss_defeated = False
        self.infection.reset_for_new_os()
        self.purify_uses = 3
        self.keys.clear()
        self.paused = False
        self.p = Player()
        self.p.x, self.p.y = START
        self.enemies.clear()
        self.bullets.clear()
        self.ebullets.clear()
        self.spawn_queue.clear()
        self.particles.clear()
        self.shards.clear()
        self.chain_fx.clear()
        self.ftexts.clear()
        self.levels.clear()
        # 허브 HUD에 이전 런의 수치가 남지 않도록 점수/웨이브도 초기화한다.
        self.wave = 0
        self.score = 0
        self.kills = 0
        self.combo = 0
        self.combo_t = 0.0
        self.checkpoint_timer = 0.0
        self.hitstop = 0.0
        self.slow = 0.0
        self.shake = 0.0
        self.boss = None
        self.wave_done_t = 0.0
        self.c.delete("scr")
        self.c.delete("ovl")
        self._show_hud(False)
        self.state = "hub"
        self.announce_world("백신 복구 완료 · 배포 단말기를 찾아주세요")
        self.events.emit("returned_to_base")
        self._clear_checkpoint()
        return True

    def _deploy_new_os(self):
        """Start a fresh OS only after old checkpoint was explicitly discarded."""
        if self.checkpoint_pending is not None or self.checkpoint_revision is not None:
            self.show_message("진행 중인 OS가 있습니다 · 이어하기 또는 새 도전 선택")
            return False
        if self.database_error is not None:
            self.show_message("백신 DB 저장 상태를 먼저 복구해야 새 OS를 배포할 수 있습니다")
            return False
        if self.database_store is not None and self.database_error is None:
            try:
                self.database = self.database_store.load()
            except (DatabaseCorruptError, DatabaseStorageError, OSError) as exc:
                self.database_error = f"{type(exc).__name__}: {exc}"
                self.announce_world("백신 DB 읽기 오류 · 보상 저장 불가", 5)
                return False
        try:
            starting = starting_software_levels(
                self.database, self.starting_software_id)
        except ValueError:
            self.starting_software_id = None
            starting = {}
        base = base_stats_for_level(self.database.antivirus_level)
        if not self.new_game(start_combat=False):
            self.announce_world("이전 런 기록을 저장하지 못했습니다 · 다시 시도하세요")
            return False
        self.checkpoint_level = self.database.antivirus_level
        self.p.maxhp = self.p.hp = base["maxhp"]
        self.p.st["dmg"] = base["dmg"]
        self.p.st["armor"] = base["armor"]
        self.p.maxmana = self.p.mana = base["maxmana"]
        for uid in starting:
            apply_upgrade(self, uid)
        self.world.deploy()
        self.first_combat_help_shown = False
        self.wave_combat_help = False
        run_id = uuid4().hex
        seed = self.opts.seed if self.opts.seed is not None else self.rng.randrange(2**31)
        self.campaign = SectorCampaign(run_id, seed=seed, events=self.events)
        self.infection.reset_for_new_os()
        self.purify_uses = 3
        self.salvage_claimed = False
        self.salvage_open = False
        self.p.x, self.p.y = START
        self.keys.clear()
        self.events.emit("os_deployed")
        if not self._persist_checkpoint():
            self.announce_world("OS는 시작됐지만 진행도 저장에 실패했습니다", 6)
        else:
            self.announce_world(
                f"백신 Lv.{self.database.antivirus_level} 배포 완료 · "
                f"시작 소프트웨어 {next(iter(starting), '기본 무장')}", 4)
        return True

    def interact_hub(self):
        node = self.world.nearest(self.p.x, self.p.y)
        if node is None:
            self.announce_world("가까운 터미널이나 게이트로 이동해 상호작용하세요", 1.8)
            return
        if self.world.room == "base":
            if node.id == "deploy":
                if self.checkpoint_pending is not None or self.checkpoint_error is not None:
                    self._open_checkpoint_prompt()
                else:
                    self._deploy_new_os()
            elif node.id == "settings":
                self.open_settings()
            elif node.id == "database":
                self.open_database_terminal()
            elif node.id == "diagnostics":
                self.open_profile_terminal()
            return

        if node.id in SECTOR_NAMES:
            self.enter_sector(node.id)
        elif node.id == "purify":
            cleaned, healed = self.purification_preview()
            if self.purify_uses <= 0:
                self.announce_world("정화 노드 사용 횟수를 모두 소모했습니다")
            elif not (cleaned or healed):
                self.announce_world("감염 없음 · OS 무결성 정상 · 정화 충전 유지")
            else:
                self.infection.purify(cleaned)
                self.infection.repair_integrity(healed)
                self.purify_uses -= 1
                self.announce_world(
                    f"백신 정화 −{cleaned:.1f}% · OS 무결성 +{healed:.1f}% "
                    f"· 남은 사용 {self.purify_uses}회")
                self._persist_checkpoint()
        elif node.id == "status":
            done = sum(x in self.world.sector_clean for x in SECTOR_NAMES)
            if self.world.os_clean:
                self.open_extraction_terminal()
            else:
                self.announce_world(
                    f"OS STATUS · 정화 {done}/4 · 네 구역을 모두 정화하세요")

    def purification_preview(self):
        return (min(self.infection.infection, 32.0),
                min(100.0 - self.infection.os_integrity, 9.0))

    def enter_sector(self, sector_id):
        self.world.hub_position = (self.p.x, self.p.y)
        wave = self.world.enter_sector(sector_id)
        if self.campaign is None:
            raise ValueError("An OS campaign must be deployed before sector entry")
        self.campaign.enter_sector(sector_id.upper())
        self.state = "play"
        self.keys.clear()
        self.p.x, self.p.y = CPU_EXIT[0] + 95, CPU_EXIT[1]
        self.p.vx = self.p.vy = 0.0
        self.c.delete("scr")
        self.c.delete("ovl")
        self._show_hud(True)
        self.events.emit("sector_entered", sector_id=sector_id.upper())
        if wave <= 5 and sector_id not in self.world.sector_clean:
            self.start_wave(wave)
        else:
            self.wave = 5
            self.spawn_queue = []
            self.enemies = []
            self._show_sector_clear(sector_id)
        self._persist_checkpoint()

    def leave_sector(self):
        sector_id = self.world.leave_sector()
        if self.campaign is not None:
            self.campaign.leave_sector()
        self.current_encounter = None
        self.virus_runtime.reset_wave()
        self.virus_pending.clear()
        self.boss = None
        self.state = "hub"
        self.keys.clear()
        self.p.x, self.p.y = self.world.hub_position
        self.p.vx = self.p.vy = 0.0
        # Abort only the unfinished wave. Its clear rewards were never issued.
        self.spawn_queue.clear()
        self.enemies.clear()
        self.bullets.clear()
        self.ebullets.clear()
        self.shards.clear()
        self.chain_fx.clear()
        self.particles.clear()
        self.ftexts.clear()
        self.wave_done_t = 0.0
        self.c.delete("scr")
        self._show_hud(False)
        self.announce_world(f"{SECTOR_NAMES[sector_id]}에서 복귀 · "
                            f"완료한 웨이브 {self.world.sector_waves[sector_id]}/5")
        self.events.emit("sector_exited", sector_id=sector_id,
                         cleared_waves=self.world.sector_waves[sector_id])
        self._persist_checkpoint()

    def step_hub(self, dt):
        p = self.p
        p.dash_t = max(0.0, p.dash_t - dt)
        p.charge_t += dt if p.charges < p.st["dash_max"] else 0.0
        if p.charge_t >= p.dash_cd and p.charges < p.st["dash_max"]:
            p.charge_t = 0.0
            p.charges += 1
        direction = movement_vector(self.keys, self.settings.bindings)
        if direction != (0.0, 0.0):
            p.last_move_dir = direction
        if p.dash_t > 0:
            p.vx, p.vy = (p.dash_dir[0] * 940.0,
                          p.dash_dir[1] * 940.0)
        else:
            factor = min(1.0, 9.0 * dt)
            p.vx += (direction[0] * p.st["speed"] - p.vx) * factor
            p.vy += (direction[1] * p.st["speed"] - p.vy) * factor
        p.x, p.y = move_on_world(p.x, p.y, p.vx * dt, p.vy * dt, p.r)
        cam_x, cam_y = camera_for(p.x, p.y, W, H)
        p.aim = math.atan2(self.my - (p.y - cam_y),
                           self.mx - (p.x - cam_x))
        p.inv = max(0.0, p.inv - dt)

    def start_wave(self, n, *, installed=""):
        """Run a B-defined sector plan, or legacy void in --selftest mode."""
        if self.world.active_sector is not None and self.campaign is not None:
            encounter = self.campaign.start_wave()
            if encounter.wave_index != n:
                raise ValueError("Sector wave progress diverged")
            self.current_encounter = encounter
            self.boss_defeated = False
            self.virus_runtime.reset_wave()
            self.virus_pending.clear()
            plan = encounter.wave
        else:
            plan = plan_wave(n, self.rng, self.stage)
        self.wave = n
        self.spawn_queue = list(plan.spawns)
        self.wave_done_t = 0.0
        self.banner = plan.banner
        self.banner_upgrade = installed
        self.wave_combat_help = (n == 1 and self.world.active_sector in SECTOR_NAMES
                                 and not self.first_combat_help_shown)
        if self.wave_combat_help:
            self.first_combat_help_shown = True
        self.banner_t = 3.2 if self.wave_combat_help else plan.banner_seconds
        if installed:
            self.banner_t = max(2.4, self.banner_t)
        self.audio.play(plan.audio_cue, plan.audio_gap)
        self.state = "play"
        self.banner_total = self.banner_t
        self.wave_pop = 1.0
        self.screen_sweep(0.32, C_BOSS if plan.boss else C_PLAYER)
        self.events.emit("wave_started", wave=n, boss=plan.boss,
                         stage_id=plan.stage_id)
        self._persist_checkpoint()

    def combo_mult(self):
        return 1.0 + min(self.combo, 40) * 0.1

    # ------------------------------------------------------------------ 스폰
    def spawn_point(self):
        side = self.rng.randrange(4)
        if side == 0:
            return rnd(60, W - 60), -50.0
        if side == 1:
            return rnd(60, W - 60), H + 50.0
        if side == 2:
            return -50.0, rnd(60, H - 60)
        return W + 50.0, rnd(60, H - 60)

    def make_enemy(self, kind, x=None, y=None, scale=1.0):
        prm = E_PARAMS[kind]
        e = Enemy()
        e.kind = kind
        if x is None or y is None:
            e.x, e.y = self.spawn_point()
        else:
            e.x, e.y = x, y
        e.r = prm["r"]
        e.spd = prm["spd"]
        e.dmg = prm["dmg"]
        e.armor = prm["armor"] * (1.0 + 0.025 * max(0, self.wave - 1))
        e.score = prm["score"]
        is_boss = kind == "boss" or kind in {
            "overseer", "replicator", "encryptor", "zero_day"}
        if kind == "boss":
            e.maxhp = prm["hp"] + 380 * (self.wave // 5 - 1)
        elif is_boss:
            e.maxhp = prm["hp"] * (1.0 + 0.06 * (self.wave - 1))
        else:
            e.maxhp = prm["hp"] * (1.0 + 0.13 * (self.wave - 1)) * scale
        e.maxhp = max(4.0, e.maxhp)
        e.hp = e.maxhp
        e.t = 0.0
        e.state = 0
        e.cd = rnd(1.0, 2.4) if kind == "shooter" else (
            1.6 if is_boss else 0.0)
        e.flash = 0.0
        if is_boss:
            e.x, e.y = W / 2, -80.0
            self.boss = e
        self.enemies.append(e)
        if not is_boss:
            self.burst(e.x, e.y, 6, prm["color"], 90, 0.35, 2.2)
        return e

    # ------------------------------------------------------------- 이펙트
    def burst(self, x, y, n, color, spd=160, life=0.45, size=2.4, drag=2.6):
        if len(self.particles) > 380:
            return
        for _ in range(n):
            a = rnd(0, TAU)
            s = rnd(spd * 0.35, spd)
            self.particles.append(Particle(
                x, y, math.cos(a) * s, math.sin(a) * s,
                rnd(life * 0.55, life), rnd(0.9, size), color, drag))

    def ring(self, x, y, n, color, spd=240, life=0.4):
        for i in range(n):
            a = TAU * i / n
            self.particles.append(Particle(
                x, y, math.cos(a) * spd, math.sin(a) * spd,
                life, 2.2, color, 3.4))

    def pop_text(self, x, y, text, color):
        if len(self.ftexts) < 40:
            self.ftexts.append(FText(x, y, text, color))

    def screen_flash(self, dur=0.3, color=C_PLAYER):
        self.flash_t = dur
        self.flash_dur = max(dur, 0.01)
        self.flash_col = color

    def screen_sweep(self, dur=0.55, color=C_PLAYER):
        self.sweep_t = dur
        self.sweep_dur = max(dur, 0.01)
        self.sweep_col = color

    # --------------------------------------------------------------- 대시
    def try_dash(self):
        p = self.p
        if p.dash_t > 0 or p.charges <= 0:
            return
        p.dash_dir = dash_vector(self.keys, (p.vx, p.vy), p.last_move_dir,
                                 self.settings.bindings)
        p.dash_t = 0.145
        p.inv = max(p.inv, 0.32)
        p.charges -= 1
        self.audio.play("dash", 0.1)
        self.burst(p.x, p.y, 14, C_PLAYER, 180, 0.32, 2.2)
        self.ring(p.x, p.y, 18, C_PLAYER, 210, 0.24)
        self.shake = max(self.shake, 2.2)
        if p.st["dash_blast"] > 0:
            self.dash_blast(p)

    def dash_blast(self, p):
        dmg = p.st["dash_blast"]
        for e in list(self.enemies):
            if math.hypot(e.x - p.x, e.y - p.y) < 78 + e.r:
                self.damage_enemy(e, dmg, C_PLAYER,
                                  knock=(p.dash_dir[0] * 140,
                                         p.dash_dir[1] * 140))

    # --------------------------------------------------------------- 액티브 스킬
    def try_cast_skill(self, kind):
        """Return False without spending mana if the skill cannot be used."""
        if self.state != "play" or self.paused:
            return False
        if self.world.active_sector in self.world.sector_clean:
            return False
        p = self.p
        if kind == "pulse":
            cd_attr, base_cost, base_cd = "pulse_cd", 35.0, 7.0
        elif kind == "guard":
            cd_attr, base_cost, base_cd = "guard_cd", 25.0, 12.0
        else:
            return False
        cost = skill_cost(base_cost, p.st["skill_efficiency"])
        if getattr(p, cd_attr) > 0 or p.mana + 1e-9 < cost:
            return False
        p.mana = max(0.0, p.mana - cost)
        setattr(p, cd_attr, skill_cooldown(base_cd, p.st["skill_cooldown"]))
        if kind == "pulse":
            radius = 155.0
            self.ring(p.x, p.y, 22, C_PLAYER, 400, 0.45)
            self.burst(p.x, p.y, 22, C_PLAYER, 260, 0.38, 2.6)
            for e in tuple(self.enemies):
                if e.hp <= 0 or math.hypot(e.x - p.x, e.y - p.y) > radius + e.r:
                    continue
                self.damage_enemy(e, 22.0 * p.st["skill_power"], C_PLAYER)
                if e.hp > 0:
                    add_dot(e.statuses, "shock", 6.0 * p.st["skill_power"],
                            3.0 * p.st["effect_duration"])
            self.shake = max(self.shake, 5.0)
        else:
            p.guard_t = 4.0 * p.st["effect_duration"]
            self.ring(p.x, p.y, 20, C_HP, 230, 0.5)
            self.burst(p.x, p.y, 12, C_HP, 115, 0.3, 2.0)
        self.audio.play("dash", 0.1)
        self.events.emit("skill_cast", skill=kind, mana=p.mana)
        return True

    # --------------------------------------------------------------- 사격
    def shoot(self, ax, ay, angle, dmg_mult=1.0, radius_mul=1.0):
        p = self.p
        st = p.st
        b = Bullet()
        b.x, b.y = ax, ay
        spd = st["bspeed"]
        b.vx = math.cos(angle) * spd
        b.vy = math.sin(angle) * spd
        b.dmg = st["dmg"] * dmg_mult
        b.r = 3.6 * radius_mul
        b.life = 1.4
        b.pierce = int(st["pierce"])
        b.homing = st["homing"]
        b.explode = st["explode"]
        b.ricochet = int(st["ricochet"])
        b.crit = st["crit"]
        b.armor_pen = st["armor_pen"]
        b.chain = int(st["chain"])
        b.color = C_BULLET if dmg_mult >= 1.0 else "#8fe0ff"
        self.bullets.append(b)

    def player_shoot(self):
        p = self.p
        st = p.st
        p.fire_cd -= DT
        if p.fire_cd > 0:
            return
        p.fire_cd = 1.0 / max(0.8, st["rate"])
        p.recoil = 1.0
        ax = p.x + math.cos(p.aim) * 16
        ay = p.y + math.sin(p.aim) * 16
        self.shoot(ax, ay, p.aim)
        n = int(st["split"])
        for i in range(n):
            off = 0.14 * (i + 1)
            self.shoot(ax, ay, p.aim + off, 0.85)
            self.shoot(ax, ay, p.aim - off, 0.85)
        if st["rear"]:
            self.shoot(p.x, p.y, p.aim + math.pi, 0.7)
        self.audio.play("shoot", 0.075)

    def enemy_shoot(self, e, count=1, spread=0.16, spd=330.0, color=None,
                    *, armor_pen=0.0, infection_on_hit=0.0):
        p = self.p
        base = math.atan2(p.y - e.y, p.x - e.x)
        col = color or C_EBULLET
        for i in range(count):
            a = base + (i - (count - 1) / 2.0) * spread
            b = Bullet()
            b.friendly = False
            b.x, b.y = e.x + math.cos(a) * (e.r + 2), e.y + math.sin(a) * (e.r + 2)
            b.vx = math.cos(a) * spd
            b.vy = math.sin(a) * spd
            b.dmg = 9.0
            b.r = 4.2
            b.life = 6.0
            b.color = col
            b.armor_pen = armor_pen
            b.infection_on_hit = infection_on_hit
            if len(self.ebullets) < 260:
                self.ebullets.append(b)
        self.audio.play("eshoot", 0.14)

    def enemy_radial(self, e, count, spd=250.0, col=None, offset=0.0,
                     *, armor_pen=0.0, infection_on_hit=0.0):
        col = col or C_BOSS
        for i in range(count):
            a = offset + TAU * i / count
            b = Bullet()
            b.friendly = False
            b.x, b.y = e.x + math.cos(a) * (e.r + 4), e.y + math.sin(a) * (e.r + 4)
            b.vx = math.cos(a) * spd
            b.vy = math.sin(a) * spd
            b.dmg = 10.0
            b.r = 4.6
            b.life = 7.0
            b.color = col
            b.armor_pen = armor_pen
            b.infection_on_hit = infection_on_hit
            if len(self.ebullets) < 260:
                self.ebullets.append(b)
        self.burst(e.x, e.y, 8, col, 140, 0.35, 2.4)
        self.audio.play("hshot", 0.2)

    # --------------------------------------------------------------- 피해
    def damage_enemy(self, e, dmg, color=C_BULLET, knock=None, crit=False,
                     armor_pen=None, dot=False, show=True):
        """One damage path for bullets, abilities, splash and status ticks."""
        if e.hp <= 0 or dmg <= 0:
            return 0.0
        pen = self.p.st["armor_pen"] if armor_pen is None else armor_pen
        armor = e.armor + (12.0 if e.guard_t > 0 else 0.0)
        # STORAGE: surviving a telegraphed encryption pulse exposes its core.
        # Players can dash beyond the radius then punish the short window.
        if e.kind in ("ransomware", "encryptor") and e.vulnerable_t > 0:
            armor = max(0.0, armor - 24.0)
        actual = dmg if dot else mitigate_damage(dmg, armor, pen)
        e.hp -= actual
        if not dot:
            e.flash = 1.0
        if knock:
            e.vx += knock[0]
            e.vy += knock[1]
        if show:
            self.pop_text(e.x + rnd(-6, 6), e.y - e.r - 4,
                          f"{int(round(actual))}" + ("!" if crit else ""),
                          C_GOLD if crit else color)
        if e.hp <= 0:
            self.kill_enemy(e)
        return actual

    def kill_enemy(self, e):
        is_boss = e.kind == "boss" or e.kind in {
            "overseer", "replicator", "encryptor", "zero_day"}
        if is_boss:
            if self.current_encounter is not None and (
                self.current_encounter.boss_kind == e.kind):
                self.boss_defeated = True
            self.boss = None
            self.shake = 26.0
            self.hitstop = 0.16
            self.audio.play("boom", 0.2)
            for i in range(6):
                self.ring(e.x, e.y, 18, C_BOSS, 260 + i * 60, 0.7)
            self.burst(e.x, e.y, 60, C_BOSS, 420, 0.9, 3.4)
            self.pop_text(e.x, e.y - 40, f"{e.kind.upper()} DOWN", C_GOLD)
        else:
            self.shake = max(self.shake, 5.0)
            self.hitstop = max(self.hitstop, 0.028)
            self.burst(e.x, e.y, 14, self._enemy_color(e), 200, 0.5, 2.6)
            self.ring(e.x, e.y, 8, self._enemy_color(e), 190, 0.3)
            self.audio.play("kill", 0.06)

        self.combo += 1
        self.combo_pop = 1.0
        self.combo_t = 2.6
        self.score += int(e.score * self.combo_mult())
        self.kills += 1
        if self.combo % 10 == 0:
            self.audio.play("combo", 0.15)
            self.pop_text(self.p.x, self.p.y - 52, f"{self.combo} COMBO",
                          C_GOLD)

        st = self.p.st
        if st["lifesteal"] > 0:
            self.p.hp = min(self.p.maxhp, self.p.hp + st["lifesteal"] * 0.5)
        if st["slow"] > 0:
            self.slow = min(0.6, self.slow + st["slow"])

        # Release the dead enemy slot before adding child enemies. Otherwise a
        # splitter killed at the 64-enemy cap temporarily creates 66 enemies.
        if e in self.enemies:
            self.enemies.remove(e)
        if e.kind == "splitter":
            cap = MAX_LIVE_ENEMIES if self.current_encounter is not None else 88
            available = max(0, cap - len(self.enemies))
            for _ in range(min(2, available)):
                m = self.make_enemy("mini", e.x + rnd(-14, 14), e.y + rnd(-14, 14))
                m.vx = rnd(-120, 120)
                m.vy = rnd(-120, 120)

        if e.kind != "mini" or self.rng.random() < 0.3:
            if self.rng.random() < 0.55:
                self.shards.append(Shard(e.x, e.y, int(e.score * 0.6)))

        if e.kind in PATTERNS:
            self.virus_runtime.forget(str(id(e)))
        self.events.emit("enemy_killed", kind=e.kind, boss=is_boss,
                         score=self.score, kills=self.kills, combo=self.combo)

    def _enemy_color(self, e):
        if e.flash > 0.4:
            return "#ffffff"
        return E_PARAMS[e.kind]["color"]

    # ------------------------------------------------------------------ 스텝
    def step(self, dt):
        self.t += dt
        self.frames += 1
        self._tick_ui(dt)
        if (self.campaign is not None and self.checkpoint_store is not None
                and self.state in ("hub", "play", "upgrade")
                and not self.paused):
            self.checkpoint_timer += dt
            if self.checkpoint_timer >= 5.0:
                self.checkpoint_timer = 0.0
                self._persist_checkpoint()

        if self.banner_t > 0:
            self.banner_t -= dt
        if self.mute_t > 0:
            self.mute_t -= dt

        if self.hitstop > 0:
            self.hitstop -= dt
            dts = dt * 0.16
        else:
            dts = dt
        if self.slow > 0:
            self.slow -= dt
            dts *= 0.45

        self.shake *= (1.0 - 7.0 * dt)
        if self.shake < 0.15:
            self.shake = 0.0

        if self.world_notice_t > 0:
            self.world_notice_t -= dt
        if self.message_t > 0:
            self.message_t = max(0.0, self.message_t - dt)
        if self.extraction_fx_t > 0:
            self.extraction_fx_t = max(0.0, self.extraction_fx_t - dt)
        if getattr(self, "os_close_retry_t", 0) > 0:
            self.os_close_retry_t = max(0.0, self.os_close_retry_t - dt)
        if self.state == "hub":
            if not (self.paused or self.settings_open or self.profile_open or
                    self.database_open or self.extraction_open or self.salvage_open
                    or self.checkpoint_prompt_open):
                self.step_hub(dt)
            self._step_fx(dts)
            return
        if self.state == "title":
            self._step_fx(dts)
            if self.auto:
                self.new_game()
            return

        if self.state == "upgrade":
            self._step_fx(dts * 0.4)
            if self.loadout_open:
                return
            self.auto_pick_t += dt
            if self.auto and self.auto_pick_t > 0.25 and self.card_opts:
                self.pick_upgrade(self.rng.randrange(len(self.card_opts)))
            return

        if self.state == "dead":
            self._step_fx(dts)
            return

        if self.paused:
            return

        self._step_fx(dts)
        self.step_player(dt, dts)
        self.step_spawns(dt)
        self.step_enemies(dts)
        if self.current_encounter is not None:
            self.step_virus_patterns(dts)
            self.infection.tick(
                dts, exposure_per_second=
                self.current_encounter.definition.exposure_per_second)
            if self.infection.os_integrity <= 0:
                # Collapse suspends combat and retries closure at most once
                # per second, preserving the same run ID and awarded XP.
                if getattr(self, "os_close_retry_t", 0) > 0:
                    return
                if not self.return_to_base(abandon=True):
                    self.os_close_retry_t = 1.0
                    return
                self.screen_flash(0.42, C_BAD)
                self.screen_sweep(0.95, C_BAD)
                self.announce_world(
                    "OS INTEGRITY 0% · 비상 철수 · 확정 XP 유지 / 소프트웨어 추출 없음",
                    6)
                self.events.emit("os_integrity_failed")
                return
        self.step_statuses(dts)
        self.build_grid()
        self.step_bullets(dts)
        self.step_shards(dts)
        self.step_collisions(dt)
        if self.combo_t > 0:
            self.combo_t -= dt
            if self.combo_t <= 0:
                self.combo = 0
        self.check_wave(dt)

        # 저체력 심박음
        if self.p.hp / self.p.maxhp < 0.3:
            self.lowhp_t -= dt
            if self.lowhp_t <= 0:
                self.lowhp_t = 1.15
                self.audio.play("lowhp", 0.7)
        else:
            self.lowhp_t = 0.0

        if self.auto:
            self.auto_play(dt)

    def _tick_ui(self, dt):
        """Animate UI even while gameplay is paused."""
        self.ui_t += dt
        self.title_age += dt if self.state == "title" else 0.0
        self.pause_t += dt if self.paused else 0.0
        self.go_t += dt if self.state == "dead" else 0.0
        self.card_t += dt if self.state == "upgrade" else 0.0
        for name in ("flash_t", "sweep_t", "cursor_hit", "wave_pop",
                     "combo_pop", "dash_ready_t"):
            setattr(self, name, max(0.0, getattr(self, name) - dt))
        if self.cursor_trail:
            self.cursor_trail = [(x, y, life - dt)
                                 for x, y, life in self.cursor_trail if life > dt]
        if self.p.charges > self.prev_charges:
            self.dash_ready_t = 0.5
        self.prev_charges = self.p.charges

    def _step_fx(self, dt):
        for q in self.particles:
            q.life -= dt
            q.x += q.vx * dt
            q.y += q.vy * dt
            f = 1.0 - q.drag * dt
            q.vx *= f
            q.vy *= f
        if self.particles:
            self.particles = [q for q in self.particles if q.life > 0]

        for ft in self.ftexts:
            ft.life -= dt
            ft.y += ft.vy * dt
            ft.vy *= (1.0 - 2.2 * dt)
        if self.ftexts:
            self.ftexts = [f for f in self.ftexts if f.life > 0]

        self.p.recoil = max(0.0, self.p.recoil - dt * 6.0)

        for i in range(len(self.chain_fx) - 1, -1, -1):
            x1, y1, x2, y2, life = self.chain_fx[i]
            life -= dt
            if life <= 0:
                self.chain_fx.pop(i)
            else:
                self.chain_fx[i] = (x1, y1, x2, y2, life)

    def step_statuses(self, dt):
        """DoT decays in game time and cannot proc itself or repeat a kill."""
        for e in tuple(self.enemies):
            for kind, damage in advance_dots(e.statuses, dt):
                if e.hp <= 0:
                    break
                color = C_RUSH if kind == "burn" else (C_HP if kind == "toxin"
                                                      else C_PLAYER)
                self.damage_enemy(e, damage, color, dot=True, show=False)
            if e.hp > 0 and e.statuses and self.frames % 18 == 0:
                status_color = C_RUSH if any(v.kind == "burn" for v in e.statuses) else C_HP
                self.burst(e.x, e.y, 2, status_color, 42, 0.18, 1.6)

    def step_player(self, dt, dts):
        p = self.p
        st = p.st
        in_combat = self.world.active_sector not in self.world.sector_clean
        if in_combat:
            p.pulse_cd = max(0.0, p.pulse_cd - dt)
            p.guard_cd = max(0.0, p.guard_cd - dt)
            p.guard_t = max(0.0, p.guard_t - dt)
            p.mana = min(p.maxmana, p.mana + st["mana_regen"] * dt)
            heal_rate = st["hp_regen"] + (1.8 if p.guard_t > 0 else 0.0)
            if p.hp > 0:
                p.hp = min(p.maxhp, p.hp + heal_rate * dt)

        p.aim = math.atan2(self.my - p.y, self.mx - p.x)

        ax, ay = movement_vector(self.keys, self.settings.bindings)
        if ax or ay:
            p.last_move_dir = (ax, ay)

        if p.dash_t > 0:
            p.dash_t -= dts
            dx, dy = p.dash_dir
            p.vx = dx * 940.0
            p.vy = dy * 940.0
            if st["dash_blast"] > 0 and self.frames % 3 == 0:
                for e in list(self.enemies):
                    if math.hypot(e.x - p.x, e.y - p.y) < 70 + e.r:
                        self.damage_enemy(e, st["dash_blast"] * 0.34, C_PLAYER,
                                          knock=(dx * 90, dy * 90))
            self.particles.append(Particle(
                p.x, p.y, rnd(-30, 30), rnd(-30, 30), 0.3, 6.0,
                "#1b93a8", 3.0))
        else:
            target_vx = ax * st["speed"]
            target_vy = ay * st["speed"]
            k = clamp(9.0 * dt, 0, 1)
            p.vx += (target_vx - p.vx) * k
            p.vy += (target_vy - p.vy) * k

        p.x += p.vx * dts
        p.y += p.vy * dts
        if p.x < p.r:
            p.x = p.r
            p.vx = 0
        elif p.x > W - p.r:
            p.x = W - p.r
            p.vx = 0
        if p.y < p.r:
            p.y = p.r
            p.vy = 0
        elif p.y > H - p.r:
            p.y = H - p.r
            p.vy = 0

        if p.inv > 0:
            p.inv -= dt

        if p.charges < st["dash_max"]:
            p.charge_t += dt
            if p.charge_t >= p.dash_cd:
                p.charge_t = 0.0
                p.charges += 1
        else:
            p.charge_t = 0.0

        if not in_combat:
            return
        self.player_shoot()

        if st["drone"] > 0:
            p.drone_ang += dt * 1.9
            self.update_drones(dt)

    def update_drones(self, dt):
        p = self.p
        n = int(p.st["drone"])
        self._drone_cd -= dt
        if self._drone_cd > 0:
            return
        self._drone_cd = 0.55
        for i in range(n):
            a = p.drone_ang + TAU * i / n
            dx = p.x + math.cos(a) * 52
            dy = p.y + math.sin(a) * 52
            tgt = self.nearest_enemy(dx, dy, 460)
            if tgt is None:
                continue
            ang = math.atan2(tgt.y - dy, tgt.x - dx)
            self.shoot(dx, dy, ang, 0.62, 0.8)

    def nearest_enemy(self, x, y, radius, exclude=None):
        """Closest live enemy within `radius`.

        Small searches reuse the 96px spatial hash built for bullet
        collisions; only very wide searches (auto-play, boss tracking) fall
        back to a linear scan, where the grid would cost more than it saves.
        """
        best = None
        bd = radius * radius
        span = int(math.ceil(radius / 96.0)) + 1
        if self.grid and span <= 4:
            kx = int(x // 96)
            ky = int(y // 96)
            for gx in range(kx - span, kx + span + 1):
                for gy in range(ky - span, ky + span + 1):
                    bucket = self.grid.get((gx, gy))
                    if not bucket:
                        continue
                    for e in bucket:
                        if e is exclude or e.hp <= 0:
                            continue
                        d = (e.x - x) ** 2 + (e.y - y) ** 2
                        if d < bd:
                            bd = d
                            best = e
            return best
        for e in self.enemies:
            if e is exclude or e.hp <= 0:
                continue
            d = (e.x - x) ** 2 + (e.y - y) ** 2
            if d < bd:
                bd = d
                best = e
        return best

    def step_spawns(self, dt):
        if not self.spawn_queue:
            return
        alive = len(self.enemies)
        rest = []
        for t, kind in self.spawn_queue:
            t -= dt
            if t <= 0 and alive < (
                    MAX_LIVE_ENEMIES if self.current_encounter else 88):
                self.make_enemy(kind)
                alive += 1
            else:
                rest.append((t, kind))
        self.spawn_queue = rest

    def step_enemies(self, dt):
        p = self.p
        for e in list(self.enemies):
            if e.hp <= 0:
                continue
            e.flash = max(0.0, e.flash - dt * 5.0)
            if e.kind in ("shooter", "splitter") or e.kind in PATTERNS:
                e.t += dt
            e.guard_t = max(0.0, e.guard_t - dt)
            e.vulnerable_t = max(0.0, e.vulnerable_t - dt)
            dx = p.x - e.x
            dy = p.y - e.y
            d = math.hypot(dx, dy) or 1.0
            ux, uy = dx / d, dy / d
            k = e.kind

            if k in ("drifter", "mini"):
                e.vx += (ux * e.spd - e.vx) * min(1.0, 4.5 * dt)
                e.vy += (uy * e.spd - e.vy) * min(1.0, 4.5 * dt)
                e.ang = math.atan2(uy, ux)

            elif k == "shooter":
                want = 330.0
                if d > want + 60:
                    mv = 1.0
                elif d < want - 60:
                    mv = -1.0
                else:
                    mv = 0.0
                e.vx += (ux * e.spd * mv - uy * e.spd * 0.55 - e.vx) * min(1.0, 3.0 * dt)
                e.vy += (uy * e.spd * mv + ux * e.spd * 0.55 - e.vy) * min(1.0, 3.0 * dt)
                e.cd -= dt
                if e.cd <= 0 and d < 700:
                    e.cd = rnd(1.9, 2.6)
                    self.enemy_shoot(e, 3, 0.18, 330.0)
                    self.burst(e.x, e.y, 3, C_SHOOT, 60, 0.2)

            elif k == "splitter":
                e.vx += (ux * e.spd - e.vx) * min(1.0, 3.4 * dt)
                e.vy += (uy * e.spd - e.vy) * min(1.0, 3.4 * dt)

            elif k == "rusher":
                if e.state == 0:
                    e.vx += (ux * e.spd - e.vx) * min(1.0, 4.0 * dt)
                    e.vy += (uy * e.spd - e.vy) * min(1.0, 4.0 * dt)
                    e.ang = math.atan2(uy, ux)
                    if d < 320:
                        e.state = 1
                        e.t = 0.55
                        e.vx *= 0.2
                        e.vy *= 0.2
                elif e.state == 1:
                    e.t -= dt
                    e.ang = math.atan2(dy, dx)
                    if e.t <= 0:
                        e.state = 2
                        e.t = 0.5
                        e.vx = math.cos(e.ang) * 780
                        e.vy = math.sin(e.ang) * 780
                        self.burst(e.x, e.y, 5, C_RUSH, 90, 0.2)
                elif e.state == 2:
                    e.t -= dt
                    if e.t <= 0:
                        e.state = 3
                        e.t = 0.9
                else:
                    e.t -= dt
                    e.vx *= (1.0 - 3.0 * dt)
                    e.vy *= (1.0 - 3.0 * dt)
                    if e.t <= 0:
                        e.state = 0

            elif k == "boss":
                self.update_boss(e, dt, d, ux, uy)

            elif k in PATTERNS:
                if k in {"overseer", "replicator", "encryptor", "zero_day"}:
                    tx = W / 2 + math.cos(e.t * 0.45) * 240
                    ty = H / 2 + math.sin(e.t * 0.7) * 130
                    e.vx += ((tx-e.x)*0.8-e.vx) * min(1.0, 2.0*dt)
                    e.vy += ((ty-e.y)*0.8-e.vy) * min(1.0, 2.0*dt)
                elif k in {"trojan", "rootkit", "packet_swarm"}:
                    move = 1 if d > 270 else (-1 if d < 180 else 0)
                    e.vx += (ux*e.spd*move-uy*e.spd*0.4-e.vx) * min(1.0, 3.0*dt)
                    e.vy += (uy*e.spd*move+ux*e.spd*0.4-e.vy) * min(1.0, 3.0*dt)
                else:
                    e.vx += (ux*e.spd-e.vx) * min(1.0, 3.5*dt)
                    e.vy += (uy*e.spd-e.vy) * min(1.0, 3.5*dt)
                e.ang = math.atan2(uy, ux)

            e.x += e.vx * dt
            e.y += e.vy * dt

            if k not in {"boss", "overseer", "replicator",
                         "encryptor", "zero_day"}:
                e.x = clamp(e.x, -30, W + 30)
                e.y = clamp(e.y, -30, H + 30)

    def step_virus_patterns(self, dt):
        """Consume bounded B-owned virus action plans into real projectiles."""
        if dt <= 0 or self.current_encounter is None:
            return
        pending = []
        for remaining, source, action in self.virus_pending:
            if source not in self.enemies or source.hp <= 0:
                continue
            remaining -= dt
            if remaining <= 0:
                self._execute_virus_action(source, action)
            else:
                pending.append((remaining, source, action))
        self.virus_pending = pending

        for enemy in tuple(self.enemies):
            if enemy.hp <= 0 or enemy.kind not in PATTERNS:
                continue
            actions = self.virus_runtime.step(
                str(id(enemy)), enemy.kind, dt,
                active_enemy_count=len(self.enemies),
                hp_fraction=max(0.0, min(1.0, enemy.hp / enemy.maxhp)))
            for action in actions:
                if action.telegraph_seconds > 0:
                    if len(self.virus_pending) >= 96:
                        continue
                    self.virus_pending.append((action.telegraph_seconds, enemy, action))
                    self.ring(enemy.x, enemy.y, 12, self._enemy_color(enemy),
                              160, max(0.25, action.telegraph_seconds))
                    warning = "복제 준비" if action.behavior == "replicate" else "!"
                    self.pop_text(enemy.x, enemy.y-enemy.r-16, warning, C_GOLD)
                else:
                    self._execute_virus_action(enemy, action)

    def _execute_virus_action(self, enemy, action):
        if enemy.hp <= 0:
            return
        behavior = action.behavior
        if behavior == "replicate" and action.spawn_kind:
            for index in range(action.count):
                if len(self.enemies) >= MAX_LIVE_ENEMIES:
                    break
                offset = self.rng.uniform(-24, 24)
                clone = self.make_enemy(action.spawn_kind,
                                        enemy.x + offset,
                                        enemy.y + self.rng.uniform(-24, 24))
                clone.hp = clone.maxhp * 0.72
            return
        if behavior == "armor_pulse":
            enemy.guard_t = 2.0
            self.ring(enemy.x, enemy.y, 14, C_GOLD, 180, 0.4)
            return
        if behavior == "telegraphed_charge":
            angle = math.atan2(self.p.y-enemy.y, self.p.x-enemy.x)
            enemy.vx = math.cos(angle) * action.power * 2.5
            enemy.vy = math.sin(angle) * action.power * 2.5
            enemy.ang = angle
            self.burst(enemy.x, enemy.y, 9, C_RUSH, 180, 0.32)
            return
        if behavior == "encryption_pulse":
            self.ring(enemy.x, enemy.y, 20, C_BAD, 340, 0.45)
            if math.hypot(enemy.x - self.p.x, enemy.y - self.p.y) < ENCRYPTION_PULSE_RADIUS:
                self.hurt_player(action.power, infection_amount=action.infection_on_hit)
            if enemy.kind in ("ransomware", "encryptor"):
                enemy.vulnerable_t = 2.25
                self.pop_text(enemy.x, enemy.y - enemy.r - 18,
                              "암호화 해제 · 약점 노출", C_PLAYER)
            return
        if behavior in ("feint_volley", "armor_piercing_volley", "quick_volley"):
            self.enemy_shoot(
                enemy, min(18, max(1, action.projectile_count)), 0.15,
                action.power, armor_pen=action.armor_pen,
                infection_on_hit=action.infection_on_hit)
            return
        if behavior in ("overload_barrage", "breach_barrage"):
            self.enemy_radial(
                enemy, min(22, max(4, action.projectile_count)),
                action.power, self._enemy_color(enemy), enemy.t * 0.55,
                armor_pen=action.armor_pen,
                infection_on_hit=action.infection_on_hit)

    def update_boss(self, e, dt, d, ux, uy):
        e.t += dt
        hp_r = e.hp / e.maxhp
        phase = 1 if hp_r > 0.66 else (2 if hp_r > 0.33 else 3)
        if phase > e.state:
            if e.state:
                self.audio.play("phase", 0.8)
                self.pop_text(e.x, e.y - 64, f"PHASE {phase}", C_BOSS)
                self.shake = max(self.shake, 15.0)
                self.ring(e.x, e.y, 22, C_BOSS, 320, 0.5)
            e.state = phase

        tx = W / 2 + math.cos(e.t * 0.55) * 300
        ty = H / 2 + math.sin(e.t * 0.85) * 150
        e.vx += ((tx - e.x) * 0.9 - e.vx) * min(1.0, 1.6 * dt)
        e.vy += ((ty - e.y) * 0.9 - e.vy) * min(1.0, 1.6 * dt)

        e.cd -= dt
        if e.cd > 0:
            return
        if phase == 1:
            e.cd = 2.1
            self.enemy_radial(e, 16, 240.0, C_BOSS, e.t)
        elif phase == 2:
            e.cd = 1.35
            self.enemy_radial(e, 22, 270.0, C_BOSS, e.t * 1.7)
            if self.rng.random() < 0.6:
                self.enemy_shoot(e, 5, 0.13, 350.0, C_EBULLET)
                if len(self.enemies) < 40:
                    self.make_enemy("drifter", e.x + rnd(-50, 50),
                                    e.y + rnd(-50, 50))
        else:
            e.cd = 0.95
            self.enemy_radial(e, 26, 300.0, C_BOSS, e.t * 2.4)
            self.enemy_radial(e, 12, 190.0, C_EBULLET, -e.t)
            if self.rng.random() < 0.35:
                self.make_enemy("rusher", e.x, e.y)

    def build_grid(self):
        g = {}
        for e in self.enemies:
            if e.hp <= 0:
                continue
            key = (int(e.x // 96), int(e.y // 96))
            b = g.get(key)
            if b is None:
                g[key] = [e]
            else:
                b.append(e)
        self.grid = g

    def step_bullets(self, dt):
        for b in self.bullets:
            if b.dead:
                continue
            if b.homing > 0:
                tgt = self.nearest_enemy(b.x, b.y, 320)
                if tgt is not None:
                    want = math.atan2(tgt.y - b.y, tgt.x - b.x)
                    cur = math.atan2(b.vy, b.vx)
                    diff = (want - cur + math.pi) % TAU - math.pi
                    cur += clamp(diff, -b.homing * dt, b.homing * dt)
                    sp = math.hypot(b.vx, b.vy)
                    b.vx = math.cos(cur) * sp
                    b.vy = math.sin(cur) * sp
            b.x += b.vx * dt
            b.y += b.vy * dt
            b.life -= dt
            if b.life <= 0:
                b.dead = True
                continue
            if b.x < -60 or b.x > W + 60 or b.y < -60 or b.y > H + 60:
                if b.ricochet > 0:
                    b.ricochet -= 1
                    if b.x < 0 or b.x > W:
                        b.vx = -b.vx
                        b.x = clamp(b.x, 2, W - 2)
                    if b.y < 0 or b.y > H:
                        b.vy = -b.vy
                        b.y = clamp(b.y, 2, H - 2)
                    self.burst(b.x, b.y, 2, b.color, 70, 0.2, 1.8)
                else:
                    b.dead = True
        if self.bullets:
            self.bullets = [b for b in self.bullets if not b.dead]

        p = self.p
        for b in self.ebullets:
            if b.dead:
                continue
            b.x += b.vx * dt
            b.y += b.vy * dt
            b.life -= dt
            if (b.life <= 0 or b.x < -40 or b.x > W + 40
                    or b.y < -40 or b.y > H + 40):
                b.dead = True
                continue
            if p.inv <= 0:
                if (b.x - p.x) ** 2 + (b.y - p.y) ** 2 < (b.r + p.r) ** 2:
                    b.dead = True
                    self.hurt_player(b.dmg, armor_pen=b.armor_pen,
                                     infection_amount=b.infection_on_hit)
        if self.ebullets:
            self.ebullets = [b for b in self.ebullets if not b.dead]

    def step_shards(self, dt):
        p = self.p
        mag = p.st["magnet"]
        for s in self.shards:
            s.life -= dt
            dx = p.x - s.x
            dy = p.y - s.y
            d = math.hypot(dx, dy) or 1.0
            if d < mag:
                pull = (1.0 - d / mag) * 1500.0
                s.vx += dx / d * pull * dt
                s.vy += dy / d * pull * dt
            s.x += s.vx * dt
            s.y += s.vy * dt
            s.vx *= (1.0 - 1.2 * dt)
            s.vy *= (1.0 - 1.2 * dt)
            if d < p.r + 8:
                s.life = 0.0
                self.score += s.value
                self.pop_text(p.x, p.y - 26, f"+{s.value}", C_SHARD)
                self.burst(p.x, p.y, 3, C_SHARD, 70, 0.25, 1.6)
                self.audio.play("hit", 0.12)
        if self.shards:
            self.shards = [s for s in self.shards if s.life > 0]

    def step_collisions(self, dt):
        p = self.p
        for b in self.bullets:
            if b.dead or not self.grid:
                continue
            kx = int(b.x // 96)
            ky = int(b.y // 96)
            for gx in (kx - 1, kx, kx + 1):
                for gy in (ky - 1, ky, ky + 1):
                    bucket = self.grid.get((gx, gy))
                    if not bucket:
                        continue
                    for e in bucket:
                        if e.hp <= 0 or e in b.hits:
                            continue
                        rr = e.r + b.r
                        if (e.x - b.x) ** 2 + (e.y - b.y) ** 2 <= rr * rr:
                            b.hits.append(e)
                            self.on_bullet_hit(b, e)
                            if b.dead:
                                break
                    if b.dead:
                        break
                if b.dead:
                    break

        if p.inv <= 0:
            for e in self.enemies:
                if e.hp <= 0:
                    continue
                rr = e.r + p.r
                if (e.x - p.x) ** 2 + (e.y - p.y) ** 2 <= rr * rr:
                    self.hurt_player(
                        e.dmg, infection_amount=(
                            PATTERNS[e.kind].infection_on_hit
                            if e.kind in PATTERNS else 0.0))
                    if p.st["thorns"] > 0:
                        # 가시 갑옷 반사는 적을 플레이어에게서 밀어내야 한다.
                        # (위치 차이를 그대로 쓰면 적이 플레이어 쪽으로 끌린다.)
                        tx, ty = e.x - p.x, e.y - p.y
                        td = math.hypot(tx, ty) or 1.0
                        self.damage_enemy(e, p.st["thorns"], C_PLAYER,
                                          knock=(tx / td * 260.0,
                                                 ty / td * 260.0))
                    break

    def on_bullet_hit(self, b, e):
        crit = self.rng.random() < b.crit
        dmg = b.dmg * (2.0 if crit else 1.0)
        self.damage_enemy(e, dmg, b.color, knock=(b.vx * 0.02, b.vy * 0.02),
                          crit=crit, armor_pen=b.armor_pen)
        # Only direct projectile hits proc on-hit effects. DoT/splash/chain cannot.
        st = self.p.st
        if st["life_on_hit"] > 0:
            self.p.hp = min(self.p.maxhp, self.p.hp + st["life_on_hit"])
        if e.hp > 0:
            if st["burn_chance"] > 0 and self.rng.random() < st["burn_chance"]:
                add_dot(e.statuses, "burn", st["burn_dps"],
                        4.0 * st["effect_duration"])
            if st["toxin_chance"] > 0 and self.rng.random() < st["toxin_chance"]:
                add_dot(e.statuses, "toxin", st["toxin_dps"],
                        5.0 * st["effect_duration"])
        self.audio.play("hit", 0.045)
        self.cursor_hit = 0.17
        self.shake = max(self.shake, 1.6)

        if not crit:
            self.burst(b.x, b.y, 3, b.color, 100, 0.22, 1.7)

        if b.explode > 0:
            self.explode(b.x, b.y, b.explode, b.dmg * 0.55)

        if b.chain > 0:
            tgt = self.nearest_enemy(b.x, b.y, 190, exclude=e)
            if tgt is not None and tgt.hp > 0:
                self.chain_fx.append((b.x, b.y, tgt.x, tgt.y, 0.12))
                self.damage_enemy(tgt, b.dmg * 0.5 * b.chain, "#9fe8ff")
                self.burst(tgt.x, tgt.y, 4, "#9fe8ff", 120, 0.25, 1.8)

        if b.pierce > 0:
            b.pierce -= 1
            b.dmg *= 0.82
        else:
            b.dead = True

    def explode(self, x, y, radius, dmg):
        self.ring(x, y, 10, C_GOLD, radius * 4.0, 0.28)
        self.burst(x, y, 8, C_GOLD, 160, 0.35, 2.4)
        self.shake = max(self.shake, 3.2)
        for e in list(self.enemies):
            if e.hp <= 0:
                continue
            if (e.x - x) ** 2 + (e.y - y) ** 2 < (radius + e.r) ** 2:
                dx = e.x - x
                dy = e.y - y
                d = math.hypot(dx, dy) or 1.0
                self.damage_enemy(e, dmg, C_GOLD,
                                  knock=(dx / d * 120, dy / d * 120))

    def hurt_player(self, dmg, *, armor_pen=0.0, infection_amount=0.0):
        p = self.p
        if p.inv > 0 or self.opts.god:
            return
        actual = mitigate_damage(dmg, effective_armor(p.st["armor"], p.guard_t),
                                 armor_pen)
        p.hp -= actual
        if getattr(self, "current_encounter", None) is not None and infection_amount > 0:
            self.infection.apply_infection(infection_amount)
        p.inv = 0.9
        self.shake = 12.0
        self.hitstop = max(self.hitstop, 0.05)
        self.combo = 0
        self.audio.play("hurt", 0.2)
        self.burst(p.x, p.y, 16, C_BAD, 220, 0.5, 2.6)
        self.pop_text(p.x, p.y - 30, f"-{actual:.1f}", C_BAD)
        # 충격파로 주변 적을 밀어내 즉사 콤보를 방지
        for e in self.enemies:
            dx = e.x - p.x
            dy = e.y - p.y
            d = math.hypot(dx, dy) or 1.0
            if d < 150:
                f = (1.0 - d / 150) * 320.0
                e.vx += dx / d * f
                e.vy += dy / d * f
        self.ring(p.x, p.y, 16, C_BAD, 260, 0.35)
        if p.hp <= 0:
            p.hp = 0
            self.game_over()

    def _record_best(self):
        is_record = self.score > self.save["best_score"]
        self.save["best_score"] = max(self.save["best_score"], self.score)
        self.save["best_wave"] = max(self.save["best_wave"], self.wave)
        if self.persist_progress:
            write_save(self.save)
        return is_record

    def game_over(self):
        self.state = "dead"
        self.go_t = 0.0
        self.screen_flash(0.38, C_BAD)
        self.dead_t = 0.0
        self._show_hud(False)
        self.audio.play("dead", 1.0)
        self.shake = 22.0
        for i in range(5):
            self.ring(self.p.x, self.p.y, 20, C_BAD, 200 + i * 80, 0.8)
        self.burst(self.p.x, self.p.y, 50, C_BAD, 380, 1.0, 3.2)

        is_record = self._record_best()
        self.events.emit("run_ended", score=self.score, wave=self.wave,
                         record=is_record,
                         duration_frames=self.frames - self.run_start_frame)
        self._persist_checkpoint()  # Durable death marker before C closes it.
        closed = self._close_database_run("dead")
        if closed:
            self._clear_checkpoint()

        c = self.c
        c.delete("scr")
        c.create_rectangle(0, 0, W, H, fill="#04060e", outline="", tags="scr")
        self.dead_title = c.create_text(W / 2, 200, text="SYSTEM FAILURE",
                                        fill=C_BAD, font=F_HUGE, tags="scr")
        c.create_text(W / 2, 262,
                      text=f"WAVE {self.wave}  ·  SCORE {self.score:,}",
                      fill=C_TEXT, font=F_MID, tags="scr")
        c.create_text(W / 2, 312,
                      text=f"처치 {self.kills}  ·  최고 점수 {self.save['best_score']:,}",
                      fill=C_DIM, font=F_UI, tags="scr")
        if is_record:
            c.create_text(W / 2, 356, text="★ NEW RECORD ★", fill=C_GOLD,
                          font=F_MID, tags="scr")
        self._menu_button("scr", "return", W / 2 - 170, 397, W / 2 + 170, 447,
                          "재시작" if self.auto else "백신 허브 복귀", "ENTER", primary=True)
        if not closed:
            c.create_text(W / 2, 505,
                          text="백신 DB 종료 기록 저장 실패 · Enter로 다시 시도하세요",
                          fill=C_BAD, font=F_UI, tags="scr")

    # ------------------------------------------------------------- 웨이브
    def _show_sector_clear(self, sector, healed=0.0):
        self.banner = f"{SECTOR_NAMES[sector]} · CLEAN"
        self.banner_t = self.banner_total = 3.0
        self.wave_done_t = 0.0
        self.sector_clear_heal = healed

    def check_wave(self, dt):
        if self.state != "play":
            return
        if (self.world.active_sector is not None and
                self.world.active_sector in self.world.sector_clean):
            return
        if self.spawn_queue or self.enemies:
            self.wave_done_t = 0.0
            return
        self.wave_done_t += dt
        if self.wave_done_t > 0.75:
            if self.current_encounter is not None:
                if not self.campaign.clear_wave(
                    hostiles_remaining=0,
                    boss_defeated=self.boss_defeated):
                    return
                sector = self.world.active_sector
                self.world.wave_cleared(sector, self.wave)
                self.infection.purify(
                    self.current_encounter.definition.purify_on_clear)
                self.current_encounter = None
                if sector in self.world.sector_clean:
                    self.ebullets.clear()
                    healed = min(self.p.maxhp - self.p.hp, self.p.maxhp * 0.15)
                    self.p.hp += healed
                    self._show_sector_clear(sector, healed)
                    self._persist_checkpoint()
                    if self.world.os_clean:
                        self.announce_world("OS CLEAN · 중앙 시스템에서 철수를 준비하세요", 4)
                    return
            self.open_upgrade()

    def open_upgrade(self):
        self.state = "upgrade"
        self.card_t = 0.0
        self.screen_flash(0.16, C_GOLD)
        self.auto_pick_t = 0.0
        self._show_hud(False)
        self.audio.play("wave", 0.2)
        self.card_opts = self.roll_upgrades(3)
        self.build_upgrade_screen()
        self._persist_checkpoint()

    def roll_upgrades(self, n):
        pool = []
        for u in UPGRADES:
            if self.levels.get(u[0], 0) >= u[4]:
                continue
            sector = self.world.active_sector
            pool.append((u, UP_WEIGHT[u[3]] *
                         sector_software_multiplier(sector, u[0])))
        opts = []
        while pool and len(opts) < n:
            total = sum(w for _, w in pool)
            r = self.rng.random() * total
            acc = 0.0
            picked = len(pool) - 1
            for i, (u, w) in enumerate(pool):
                acc += w
                if r <= acc:
                    picked = i
                    break
            opts.append(pool[picked][0])
            pool.pop(picked)
        while len(opts) < n:
            opts.append(("bonus", "잔여 신호", "점수 +250 즉시 획득", 0, 99))
        return opts

    def pick_upgrade(self, idx):
        if (self.state != "upgrade" or self.loadout_open
                or not 0 <= idx < len(self.card_opts)):
            return
        u = self.card_opts[idx]
        uid, name = u[:2]
        if uid == "bonus":
            self.score += 250
            receipt = f"{name} · 점수 +250"
        else:
            apply_upgrade(self, uid)
            receipt = f"{name} Lv.{self.levels[uid]} 설치 완료"
        self.audio.play("pick", 0.1)
        self.screen_flash(0.17, C_PLAYER)
        self.events.emit("upgrade_picked", upgrade=uid, wave=self.wave)
        self.events.emit("software_picked", software=uid, wave=self.wave)
        self.c.delete("scr")
        self.cards = []
        self.card_opts = []
        self._show_hud(True)
        self.start_wave(self.wave + 1, installed=receipt)

    # ------------------------------------------------------------ 자동 플레이
    def auto_play(self, dt):
        p = self.p
        tgt = self.nearest_enemy(p.x, p.y, 2000)
        if tgt is not None:
            self.mx = tgt.x + tgt.vx * 0.08
            self.my = tgt.y + tgt.vy * 0.08
        if self.frames % 50 == 0:
            self.try_dash()
        if self.frames % 270 == 0:
            self.try_cast_skill("pulse")
        if self.frames % 510 == 0:
            self.try_cast_skill("guard")
        if self.frames % 18 == 0:
            self.keys.clear()
            if p.x < W * 0.34:
                self.keys.add("d")
            elif p.x > W * 0.66:
                self.keys.add("a")
            if p.y < H * 0.30:
                self.keys.add("s")
            elif p.y > H * 0.70:
                self.keys.add("w")

    def loop(self):
        try:
            now = time.perf_counter()
            raw = now - self.last
            self.last = now
            if raw > 0.15:
                raw = 0.15
            self.acc += raw
            steps = 0
            while self.acc >= DT and steps < 3:
                self.step(DT)
                self.acc -= DT
                steps += 1
            if steps >= 3:
                self.acc = 0.0
            self.fps = self.fps * 0.92 + (1.0 / max(raw, 1e-4)) * 0.08
            if steps:
                begin_render = time.perf_counter()
                self.render()
                elapsed_ms = (time.perf_counter() - begin_render) * 1000
                self.render_samples.append(elapsed_ms)
                self.render_ms = sum(self.render_samples) / len(self.render_samples)
                ordered = sorted(self.render_samples)
                self.render_p95_ms = ordered[max(0, math.ceil(len(ordered) * 0.95) - 1)]
                if self.frames % 60 == 0:
                    self.canvas_items = len(self.c.find_all())
                    self.memory_mib = resident_memory_mib()
        except tk.TclError:
            # 창이 이미 닫혔거나 캔버스가 삭제된 경우다. 루프를 다시 예약하면
            # 영영 멈춰 버리므로 창이 살아 있는지만 확인하고 종료한다.
            traceback.print_exc()
            try:
                if not self.root.winfo_exists():
                    return
            except Exception:
                return
        except Exception:
            traceback.print_exc()
            # 예기치 않은 오류로 진행 중인 런의 영구 기록이 열려 있는 채로
            # 창만 사라지지 않도록 먼저 종료 영수증을 남긴다.
            try:
                self._abandon_active_run()
            except Exception:
                pass
            try:
                self.root.destroy()
            except Exception:
                pass
            return
        # 어떤 예외에서도 다음 프레임은 반드시 예약해야 창이 얼지 않는다.
        try:
            self.root.after(5, self.loop)
        except tk.TclError:
            pass

    def selftest_run(self):
        """창/실시간 대기 없이 고정 스텝으로 빠르게 시뮬레이션한다."""
        t0 = time.perf_counter()
        failed = False
        while self.selftest_left > 0:
            try:
                self.step(DT)
                if self.frames % 3 == 0:
                    self.render()
            except Exception:
                traceback.print_exc()
                failed = True
                break
            self.selftest_left -= 1
            if self.state == "dead":
                self.dead_t += DT
                if self.dead_t > 1.2:
                    self.new_game()
            if self.frames % 600 == 0:
                print(f"[dbg] f={self.frames} st={self.state} w={self.wave} "
                      f"e={len(self.enemies)} b={len(self.bullets)} "
                      f"eb={len(self.ebullets)} cards={len(self.card_opts)} "
                      f"hp={self.p.hp:.0f}", flush=True)
        el = time.perf_counter() - t0
        sim = self.frames / 60.0
        self.fps = self.frames / max(el, 1e-6)
        print(f"[perf] {self.frames} frames / {el:.2f}s  "
              f"({sim:.1f}s 시뮬레이션, {self.fps:.0f} step/s)")
        print(f"[selftest] wave={self.wave} score={self.score} "
              f"kills={self.kills} state={self.state} "
              f"enemies={len(self.enemies)} bullets={len(self.bullets)}")
        try:
            self.root.destroy()
        except Exception:
            pass
        return not failed
