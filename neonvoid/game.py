"""Simulation and game flow."""

from __future__ import annotations

import math
import random
import time
import traceback
import tkinter as tk
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
from .world.session import (WorldSession, START, CPU_EXIT, SECTOR_NAMES,
                            move_on_world, camera_for)

class Game(VisualMixin):
    def __init__(self, root, opts):
        self.root = root
        self.events = EventBus()
        self.opts = opts
        self.stage = get_stage(getattr(opts, "stage", DEFAULT_STAGE_ID))
        self.auto = bool(opts.selftest)
        self.selftest_left = int(opts.selftest or 0)
        self.settings = Settings() if self.auto else load_settings()
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
        self.combo_mark = 0

        root.title("NEON VOID")
        root.configure(bg=C_BG)
        root.resizable(False, False)

        self.c = tk.Canvas(root, width=W, height=H, bg=C_BG,
                           highlightthickness=0, bd=0)
        self.c.pack()
        if self.auto:
            # 자동 검증 중에는 창을 띄우지 않는다
            root.withdraw()

        self.save = load_save()
        self.profile = PlayerProfile() if self.auto else load_profile()
        self.profile_tracker = ProfileTracker(
            self.events, self.profile, persist=None if self.auto else save_profile
        )
        self.run_start_frame = 0
        self.rng = random.Random(opts.seed)
        self.world = WorldSession()
        self.world_notice = '백신 코어 온라인 · 배포 단말기를 찾아 이동하세요'
        self.world_notice_t = 4.0

        self.keys = set()
        self.mx, self.my = W / 2, H / 2 - 120
        self.mouse_down = False

        root.protocol("WM_DELETE_WINDOW", self.close_game)
        root.bind("<KeyPress>", self.on_key)
        root.bind("<KeyRelease>", self.on_key_up)
        root.bind("<FocusOut>", self._on_focus_out)
        self.c.bind("<Motion>", self.on_motion)
        self.c.bind("<ButtonPress-1>", self.on_mouse_down)
        self.c.bind("<ButtonRelease-1>", self.on_mouse_up)
        # 기본 OS 커서를 숨기고 게임 전용 커서를 그린다
        self.c.configure(cursor="none")
        self.c.bind("<Enter>", self._on_pointer_enter)
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

        # 렌더 순서대로 풀 생성
        self.pool_enemy = PolyPool(self.c, 200, 8, fill=C_ENEMY, outline="")
        self.pool_ring = OvalPool(self.c, 90, fill="", outline=C_BOSS)
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
        self.banner_t = 0.0
        self.kills = 0
        self.boss = None
        self.cards = []
        self.card_opts = []
        self.title_pulse = 0.0
        self.auto_pick_t = 0.0
        self.dead_t = 0.0
        self._drone_cd = 0.0

        if self.auto:
            self._build_title()
        self._build_cursor()
        if not self.auto:
            self.loop()

    # ---------------------------------------------------------- 실행 기록
    def _abandon_active_run(self):
        if self.profile_tracker.active:
            self.events.emit("run_abandoned", score=self.score, wave=self.wave,
                             duration_frames=self.frames - self.run_start_frame)

    def close_game(self):
        """Record a quit from an active run, then destroy the window."""
        self._abandon_active_run()
        self.root.destroy()

    # --------------------------------------------------------------- 설정
    def open_settings(self):
        """Show the overlay only from the title or a paused run."""
        if self.settings_open:
            return
        if self.state not in ("title", "hub") and not (self.state == "play" and self.paused):
            return
        self.settings_open = True
        self.settings_page = "general"
        self.settings_selected = 0
        self.rebinding_action = None
        self.settings_notice = ""
        self.keys.clear()
        self._draw_settings()

    def close_settings(self):
        if self.settings_open:
            self.settings_open = False
            self.rebinding_action = None
            self.settings_notice = ""
            self.settings_hits = []
            self.c.delete("cfg")
            self.keys.clear()

    def apply_settings(self):
        """Apply visual/audio preferences immediately and persist changes."""
        self.audio.set_volume(self.settings.volume)
        self.muted = self.audio.set_muted(self.settings.muted)
        self.show_fps = bool(self.opts.fps or self.settings.show_fps)
        self.mute_t = 1.8
        if not self.auto:
            save_settings(self.settings)
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
            self.settings.show_fps = not self.settings.show_fps
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

        count = len(ACTION_ORDER) if self.settings_page == "controls" else 4
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
        if self.settings_open:
            self._handle_settings_key(key)
            return

        if key == "o" and (self.state in ("title", "hub") or
                           (self.state == "play" and self.paused)):
            self.open_settings()
            return

        was_held = key in self.keys
        self.keys.add(key)

        # System commands remain fixed, so configuration can never hide them.
        if key == "m":
            if not was_held:
                self.settings.muted = not self.settings.muted
                self.apply_settings()
            return

        if self.state == "title":
            if key in ("return", "kp_enter", "space"):
                self.new_game()
            elif key == "escape":
                self.close_game()
            return

        if key == self.settings.bindings["pause"] or (
            key == "escape" and self.state in ("play", "hub")):
            if self.state in ("play", "hub") and not was_held:
                self.paused = not self.paused
                self.pause_t = 0.0
                self._draw_pause()
            return

        if self.state == "dead":
            if key in ("r", "return", "space", "escape"):
                if self.auto:
                    self.new_game()
                else:
                    self.return_to_base()
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
                if self.world.active_sector == "cpu" and (
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
        if (ev.x - self.mx) ** 2 + (ev.y - self.my) ** 2 > 49:
            self.cursor_trail.append((self.mx, self.my, 0.26))
            self.cursor_trail = self.cursor_trail[-8:]
        self.mx, self.my = ev.x, ev.y
        self.cursor_on = True
        self.c.configure(cursor="none")
        if self.settings_open:
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
        self.mouse_down = True
        if self.settings_open:
            if 556 <= ev.y <= 590 and W / 2 - 180 <= ev.x <= W / 2 + 180:
                self.settings_page = ("controls" if self.settings_page == "general"
                                      else "general")
                self.settings_selected = 0
                self.settings_notice = ""
                self._draw_settings()
                return
            if 593 <= ev.y <= 640 and W / 2 - 180 <= ev.x <= W / 2 + 180:
                self.close_settings()
                return
            for idx, (x0, y0, x1, y1) in enumerate(self.settings_hits):
                if x0 <= ev.x <= x1 and y0 <= ev.y <= y1:
                    self.settings_selected = idx
                    if self.settings_page == "controls":
                        self.rebinding_action = ACTION_ORDER[idx]
                        self.settings_notice = "새로 사용할 키를 누르세요. Esc는 취소."
                        self._draw_settings()
                    else:
                        direction = -1 if idx == 0 and ev.x < W / 2 else 1
                        self.change_setting(idx, direction)
                    break
            return
        if self.state == "title":
            if W / 2 - 150 <= ev.x <= W / 2 + 150 and 623 <= ev.y <= 668:
                self.open_settings()
            else:
                self.new_game()
        elif self.state == "dead":
            if self.auto:
                self.new_game()
            else:
                self.return_to_base()
        elif self.state == "play" and self.paused:
            if W / 2 - 160 <= ev.x <= W / 2 + 160 and H / 2 + 85 <= ev.y <= H / 2 + 128:
                self.open_settings()
        elif self.state == "upgrade":
            for idx, (x0, y0, x1, y1) in enumerate(self.cards):
                off = self.card_yoff[idx] if idx < len(self.card_yoff) else 0.0
                if x0 <= ev.x <= x1 and y0 + off <= ev.y <= y1 + off:
                    self.pick_upgrade(idx)
                    break

    def on_mouse_up(self, ev):
        self.mouse_down = False

    # ------------------------------------------------------------------ 상태
    def new_game(self, *, start_combat=True):
        self._abandon_active_run()
        self.run_start_frame = self.frames
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
        self.combo_mark = 0
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

    # ------------------------------------------------------ 플레이 가능한 월드
    def announce_world(self, message, duration=3.0):
        self.world_notice = message
        self.world_notice_t = duration

    def return_to_base(self, abandon=False):
        if abandon:
            self._abandon_active_run()
        self.world.reset_to_base()
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
        self.levels.clear()
        self.c.delete("scr")
        self.c.delete("ovl")
        self._show_hud(False)
        self.state = "hub"
        self.announce_world("백신 복구 완료 · 배포 단말기를 찾아주세요")
        self.events.emit("returned_to_base")

    def interact_hub(self):
        node = self.world.nearest(self.p.x, self.p.y)
        if node is None:
            self.announce_world("가까운 터미널이나 게이트로 이동해 상호작용하세요", 1.8)
            return
        if self.world.room == "base":
            if node.id == "deploy":
                self.new_game(start_combat=False)
                self.world.deploy()
                self.p.x, self.p.y = START
                self.keys.clear()
                self.events.emit("os_deployed")
                self.announce_world("OS에 배포되었습니다 · CPU CORE 게이트가 활성화됐습니다")
            elif node.id == "settings":
                self.open_settings()
            elif node.id == "database":
                self.announce_world(
                    "백신 데이터베이스: 영구 XP/소프트웨어 API는 #12에서 통합 예정")
            else:
                self.announce_world("진단 준비 · 실전 전투는 OS 배포 후 시작할 수 있습니다")
            return

        if node.id == "cpu":
            self.enter_sector("cpu")
        elif node.id in SECTOR_NAMES:
            self.announce_world(f"{SECTOR_NAMES[node.id]}: 구역 콘텐츠 연결 대기 (#11)")
        elif node.id == "status":
            done = sum(x in self.world.sector_clean for x in SECTOR_NAMES)
            self.announce_world(f"OS STATUS · 정화된 구역 {done}/4 · "
                                "모든 지역 정화 후 철수 가능")

    def enter_sector(self, sector_id):
        self.world.hub_position = (self.p.x, self.p.y)
        wave = self.world.enter_sector(sector_id)
        self.state = "play"
        self.keys.clear()
        self.p.x, self.p.y = CPU_EXIT[0] + 95, CPU_EXIT[1]
        self.p.vx = self.p.vy = 0.0
        self.c.delete("scr")
        self.c.delete("ovl")
        self._show_hud(True)
        self.events.emit("sector_entered", sector_id=sector_id)
        if wave <= 5 and sector_id not in self.world.sector_clean:
            self.start_wave(wave)
        else:
            self.wave = 5
            self.spawn_queue = []
            self.enemies = []
            self.banner = f"{SECTOR_NAMES[sector_id]} · CLEAN"
            self.banner_t = 2.0
            self.banner_total = self.banner_t

    def leave_sector(self):
        sector_id = self.world.leave_sector()
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
        self.wave_done_t = 0.0
        self.c.delete("scr")
        self._show_hud(False)
        self.announce_world(f"{SECTOR_NAMES[sector_id]}에서 복귀 · "
                            f"완료한 웨이브 {self.world.sector_waves[sector_id]}/5")
        self.events.emit("sector_exited", sector_id=sector_id,
                         cleared_waves=self.world.sector_waves[sector_id])

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

    def start_wave(self, n):
        """Apply the stage director's deterministic, data-driven wave plan."""
        plan = plan_wave(n, self.rng, self.stage)
        self.wave = n
        self.spawn_queue = list(plan.spawns)
        self.wave_done_t = 0.0
        self.banner = plan.banner
        self.banner_t = plan.banner_seconds
        self.audio.play(plan.audio_cue, plan.audio_gap)
        self.state = "play"
        self.banner_total = self.banner_t
        self.wave_pop = 1.0
        self.screen_sweep(0.32, C_BOSS if plan.boss else C_PLAYER)
        self.events.emit("wave_started", wave=n, boss=plan.boss,
                         stage_id=plan.stage_id)

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
        if kind == "boss":
            e.maxhp = prm["hp"] + 380 * (self.wave // 5 - 1)
            e.cd = 1.6
        else:
            e.maxhp = prm["hp"] * (1.0 + 0.13 * (self.wave - 1)) * scale
        e.maxhp = max(4.0, e.maxhp)
        e.hp = e.maxhp
        e.t = 0.0
        e.state = 0
        e.cd = rnd(1.0, 2.4) if kind == "shooter" else 0.0
        e.flash = 0.0
        if kind == "boss":
            e.x, e.y = W / 2, -80.0
            self.boss = e
        self.enemies.append(e)
        if kind != "boss":
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

    def enemy_shoot(self, e, count=1, spread=0.16, spd=330.0, color=None):
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
            self.ebullets.append(b)
        self.audio.play("eshoot", 0.14)

    def enemy_radial(self, e, count, spd=250.0, col=None, offset=0.0):
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
        actual = dmg if dot else mitigate_damage(dmg, e.armor, pen)
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
        if e.kind == "boss":
            self.boss = None
            self.shake = 26.0
            self.hitstop = 0.16
            self.audio.play("boom", 0.2)
            for i in range(6):
                self.ring(e.x, e.y, 18, C_BOSS, 260 + i * 60, 0.7)
            self.burst(e.x, e.y, 60, C_BOSS, 420, 0.9, 3.4)
            self.pop_text(e.x, e.y - 40, "OVERSEER DOWN", C_GOLD)
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

        if e.kind == "splitter":
            for _ in range(2):
                m = self.make_enemy("mini", e.x + rnd(-14, 14), e.y + rnd(-14, 14))
                m.vx = rnd(-120, 120)
                m.vy = rnd(-120, 120)

        if e.kind != "mini" or self.rng.random() < 0.3:
            if self.rng.random() < 0.55:
                self.shards.append(Shard(e.x, e.y, int(e.score * 0.6)))

        if e in self.enemies:
            self.enemies.remove(e)
        self.events.emit("enemy_killed", kind=e.kind, score=self.score,
                         kills=self.kills, combo=self.combo)

    def _enemy_color(self, e):
        if e.flash > 0.4:
            return "#ffffff"
        return E_PARAMS[e.kind]["color"]

    # ------------------------------------------------------------------ 스텝
    def step(self, dt):
        self.t += dt
        self.frames += 1
        self.title_pulse += dt
        self._tick_ui(dt)

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
        if self.state == "hub":
            if not self.paused and not self.settings_open:
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
        p.pulse_cd = max(0.0, p.pulse_cd - dt)
        p.guard_cd = max(0.0, p.guard_cd - dt)
        p.guard_t = max(0.0, p.guard_t - dt)
        p.mana = min(p.maxmana, p.mana + st["mana_regen"] * dt)
        if p.hp > 0:
            heal_rate = st["hp_regen"] + (1.8 if p.guard_t > 0 else 0.0)
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
        best = None
        bd = radius * radius
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
            if t <= 0 and alive < 88:
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
            if e.kind in ("shooter", "splitter"):
                e.t += dt
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

            e.x += e.vx * dt
            e.y += e.vy * dt

            if k != "boss":
                e.x = clamp(e.x, -30, W + 30)
                e.y = clamp(e.y, -30, H + 30)

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
                    self.hurt_player(b.dmg)
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
                    self.hurt_player(e.dmg)
                    if p.st["thorns"] > 0:
                        self.damage_enemy(e, p.st["thorns"], C_PLAYER,
                                          knock=(p.x - e.x, p.y - e.y))
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

    def hurt_player(self, dmg):
        p = self.p
        if p.inv > 0 or self.opts.god:
            return
        actual = mitigate_damage(dmg, effective_armor(p.st["armor"], p.guard_t))
        p.hp -= actual
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

        if self.score > self.save["best_score"]:
            is_record = True
            self.save["best_score"] = self.score
        else:
            is_record = False
        self.save["best_wave"] = max(self.save["best_wave"], self.wave)
        write_save(self.save)
        self.events.emit("run_ended", score=self.score, wave=self.wave,
                         record=is_record,
                         duration_frames=self.frames - self.run_start_frame)

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
        self.dead_restart = c.create_text(W / 2, 420,
                                          text=("R 또는 클릭  ·  재시작" if self.auto
                                                else "ENTER 또는 클릭 · 백신 허브 복귀"),
                                          fill=C_PLAYER, font=F_BIG, tags="scr")
        c.create_text(W / 2, 460,
                      text="ESC  ·  타이틀" if self.auto else
                      "ESC  ·  백신 시스템 허브", fill=C_DIM,
                      font=F_UI, tags="scr")

    # ------------------------------------------------------------- 웨이브
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
            if self.world.active_sector == "cpu":
                if self.world.wave_cleared("cpu", self.wave):
                    self.events.emit("wave_cleared", sector_id="cpu",
                                     wave_index=self.wave)
                    if self.wave == 5:
                        self.events.emit("sector_cleared", sector_id="cpu")
                        self.banner = "CPU CORE · CLEAN  ·  F: RETURN"
                        self.banner_t = 3.0
                        self.banner_total = self.banner_t
                        self.wave_done_t = 0.0
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

    def roll_upgrades(self, n):
        pool = []
        for u in UPGRADES:
            if self.levels.get(u[0], 0) >= u[4]:
                continue
            pool.append((u, UP_WEIGHT[u[3]]))
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
        if idx >= len(self.card_opts):
            return
        u = self.card_opts[idx]
        if u[0] == "bonus":
            self.score += 250
        else:
            apply_upgrade(self, u[0])
        self.audio.play("pick", 0.1)
        self.screen_flash(0.17, C_PLAYER)
        self.events.emit("upgrade_picked", upgrade=u[0], wave=self.wave)
        self.events.emit("software_picked", software=u[0], wave=self.wave)
        self.c.delete("scr")
        self.cards = []
        self.card_opts = []
        self._show_hud(True)
        self.start_wave(self.wave + 1)

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
                self.render()
            self.root.after(5, self.loop)
        except tk.TclError:
            try:
                if self.root.winfo_exists():
                    traceback.print_exc()
            except Exception:
                pass
        except Exception:
            traceback.print_exc()
            try:
                self.root.destroy()
            except Exception:
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
