"""Canvas HUD and rendering mixin."""

from __future__ import annotations

import math
import tkinter as tk
from .content.enemies import E_PARAMS
from .controls import ACTION_ORDER, ACTION_LABELS
from .content.upgrades import UP_BY_ID
from .constants import (W, H, DT, TAU, clamp, rnd, C_GRID, C_STAR, C_PLAYER, C_PLAYER_GLOW, C_RUSH, C_BOSS, C_SHARD, C_TEXT, C_DIM, C_HP, C_BAD, C_GOLD, F_TITLE, F_HUGE, F_BIG, F_MID, F_UI, F_HUD, F_NUM, F_MONO, RARITY_COL, RARITY_NAME)

class VisualMixin:
    """Uses the Game object's state without owning a Tk root."""

    # ------------------------------------------------------------------ 배경
    def _build_background(self):
        c = self.c
        for _ in range(52):
            x = rnd(0, W)
            y = rnd(0, H)
            r = rnd(0.7, 1.9)
            it = c.create_oval(x - r, y - r, x + r, y + r,
                               fill=C_STAR, outline="")
            self.stars.append((it, x, y, rnd(0.15, 0.5),
                               rnd(0, TAU), rnd(0.7, 2.1), rnd(3.0, 11.0)))
        for x in range(0, W + 1, 80):
            it = c.create_line(x, 0, x, H, fill=C_GRID)
            self.grid_items.append((it, 0, x))
        for y in range(0, H + 1, 80):
            it = c.create_line(0, y, W, y, fill=C_GRID)
            self.grid_items.append((it, 1, y))
        # 스캔라인 + 저체력 비네트 + 화면 플래시
        self.scanline = c.create_line(0, 0, W, 0, fill="#0c2131", width=2)
        self.vig_items = [
            c.create_rectangle(0, 0, W, 22, fill=C_BAD, outline="", stipple="gray12",
                               state="hidden"),
            c.create_rectangle(0, H - 22, W, H, fill=C_BAD, outline="", stipple="gray12",
                               state="hidden"),
            c.create_rectangle(0, 0, 22, H, fill=C_BAD, outline="", stipple="gray12",
                               state="hidden"),
            c.create_rectangle(W - 22, 0, W, H, fill=C_BAD, outline="", stipple="gray12",
                               state="hidden"),
        ]
        self.flash_rect = c.create_rectangle(0, 0, W, H, fill=C_PLAYER,
                                             outline="", stipple="gray25",
                                             state="hidden", tags=("fx",))
        self.sweep_band = c.create_rectangle(0, 0, W, 0, fill=C_PLAYER,
                                              outline="", stipple="gray12",
                                              state="hidden", tags=("fx",))
        self.sweep_line = c.create_line(0, 0, W, 0, fill=C_PLAYER, width=2,
                                         state="hidden", tags=("fx",))

    def _move_background(self, ox, oy):
        c = self.c
        u = self.ui_t
        for it, x, y, pr, ph, tw, dr in self.stars:
            r = (0.55 + pr * 2.1) * (0.62 + 0.38 * math.sin(u * tw + ph))
            sx = x + ox * 0.35
            sy = ((y + u * dr) % (H + 16)) - 8 + oy * 0.35
            c.coords(it, sx - r, sy - r, sx + r, sy + r)
        for it, kind, pos in self.grid_items:
            if kind == 0:
                c.coords(it, pos + ox, oy, pos + ox, H + oy)
            else:
                c.coords(it, ox, pos + oy, W + ox, pos + oy)
        sy = (u * 34.0) % H
        c.coords(self.scanline, ox, sy + oy, W + ox, sy + oy)
        c.itemconfigure(self.scanline, state="hidden" if self.settings.reduced_effects
                        else "normal")

    # --------------------------------------------------------------- 커서
    def _build_cursor(self):
        c = self.c
        self.cursor_halo = c.create_oval(0, 0, 0, 0, outline="#245665",
                                         width=2, state="hidden", tags=("cursor",))
        self.cursor_trail_items = [
            c.create_oval(0, 0, 0, 0, fill=C_PLAYER, outline="",
                          state="hidden", tags=("cursor",))
            for _ in range(8)
        ]
        self.cursor_arcs = [
            c.create_arc(0, 0, 0, 0, start=0, extent=62, style="arc",
                         outline=C_PLAYER, width=2, state="hidden", tags=("cursor",))
            for _ in range(3)
        ]
        self.cursor_ticks = [
            c.create_line(0, 0, 0, 0, fill=C_PLAYER, width=2,
                          state="hidden", tags=("cursor",))
            for _ in range(4)
        ]
        self.cursor_cross = [
            c.create_line(0, 0, 0, 0, fill=C_PLAYER, width=1,
                          state="hidden", tags=("cursor",))
            for _ in range(2)
        ]
        self.cursor_dot = c.create_oval(0, 0, 0, 0, fill="#ffffff", outline="",
                                        state="hidden", tags=("cursor",))
        self.cursor_hit_marks = [
            c.create_line(0, 0, 0, 0, fill="#ffffff", width=2,
                          state="hidden", tags=("cursor",))
            for _ in range(4)
        ]

    def _on_pointer_enter(self, ev=None):
        self.cursor_on = True
        self.c.configure(cursor="none")

    def _on_pointer_leave(self, ev=None):
        self.cursor_on = False
        self.cursor_trail.clear()
        try:
            self.c.configure(cursor="")
        except tk.TclError:
            pass

    def _on_focus_out(self, ev=None):
        self.keys.clear()
        self.cursor_on = False
        self.cursor_trail.clear()
        try:
            self.c.configure(cursor="")
        except tk.TclError:
            pass

    def draw_cursor(self):
        c = self.c
        items = ([self.cursor_halo] + self.cursor_trail_items + self.cursor_arcs
                 + self.cursor_ticks + self.cursor_cross
                 + [self.cursor_dot] + self.cursor_hit_marks)
        if not self.cursor_on:
            for it in items:
                c.itemconfigure(it, state="hidden")
            return
        x, y = self.mx, self.my
        u = self.ui_t
        p = self.p
        dash = clamp(p.dash_t / 0.145, 0, 1) if p.dash_t > 0 else 0.0
        hot = False
        for e in self.enemies:
            if e.hp > 0 and (e.x - x) ** 2 + (e.y - y) ** 2 < (e.r + 8) ** 2:
                hot = True
                break
        if self.state == "upgrade" and self.hover_card >= 0:
            hot = True
        self.cursor_hot = hot
        if hot:
            col = C_GOLD if self.state == "upgrade" else C_BAD
        elif self.state == "title":
            col = "#7ef0ff"
        else:
            col = C_PLAYER
        heat = clamp(p.recoil, 0, 1) if self.state == "play" else 0.0
        self.cursor_spin = getattr(self, "cursor_spin", 0.0) + \
            (0.9 + heat * 7.5 + dash * 13.0) * (1.0 / 60.0)
        rad = 26.0 + 2.2 * math.sin(u * 3.8) + heat * 3.5 + dash * 12.0
        if hot:
            rad += 1.0 + 0.8 * math.sin(u * 12.0)
        # 큰 외곽 링과 커서 잔상은 재사용 Canvas 아이템으로 그린다.
        halo_r = rad + 13.0 + 1.8 * math.sin(u * 2.9)
        c.coords(self.cursor_halo, x - halo_r, y - halo_r,
                 x + halo_r, y + halo_r)
        c.itemconfigure(self.cursor_halo,
                        outline="#874b2b" if hot else "#245665", state="normal")
        for i, it in enumerate(self.cursor_trail_items):
            if i >= len(self.cursor_trail):
                c.itemconfigure(it, state="hidden")
                continue
            tx, ty, life = self.cursor_trail[-1 - i]
            size = 2.0 + life * 12.0
            c.coords(it, tx - size, ty - size, tx + size, ty + size)
            c.itemconfigure(it, fill=col if i < 3 else "#245665", state="normal")
        # 회전하는 3개의 호
        for i, it in enumerate(self.cursor_arcs):
            a0 = math.degrees(self.cursor_spin) + i * 120.0
            c.coords(it, x - rad, y - rad, x + rad, y + rad)
            c.itemconfigure(it, start=a0, extent=62, outline=col,
                            width=2 if not hot else 3, state="normal")
        # 바깥 4방향 틱
        for i, it in enumerate(self.cursor_ticks):
            a = self.cursor_spin * 0.55 + TAU * i / 4.0
            r0, r1 = rad + 3.0, rad + 12.0
            c.coords(it, x + math.cos(a) * r0, y + math.sin(a) * r0,
                     x + math.cos(a) * r1, y + math.sin(a) * r1)
            c.itemconfigure(it, fill=col, state="normal")
        # 중앙 십자선 (작은 간격)
        gap = 5.8 + heat * 3.0 + dash * 5.0
        r2 = 18.0 + dash * 8.0
        c.coords(self.cursor_cross[0], x - r2, y, x - gap, y)
        c.coords(self.cursor_cross[1], x + gap, y, x + r2, y)
        for it in self.cursor_cross:
            c.itemconfigure(it, fill=col, state="normal")
        dr = 2.0 + 0.5 * math.sin(u * 9.0)
        c.coords(self.cursor_dot, x - dr, y - dr, x + dr, y + dr)
        c.itemconfigure(self.cursor_dot, fill="#ffffff" if not hot else col,
                        state="normal")
        # 명중 표시(히트마커)
        if self.cursor_hit > 0:
            k = clamp(self.cursor_hit / 0.15, 0, 1)
            for i, it in enumerate(self.cursor_hit_marks):
                a = math.pi / 4.0 + math.pi / 2.0 * i
                r0 = 13.0 + (1.0 - k) * 7.0
                r1 = 23.0 + (1.0 - k) * 10.0
                c.coords(it, x + math.cos(a) * r0, y + math.sin(a) * r0,
                         x + math.cos(a) * r1, y + math.sin(a) * r1)
                c.itemconfigure(it, fill="#ffffff" if hot else C_GOLD,
                                state="normal")
        else:
            for it in self.cursor_hit_marks:
                c.itemconfigure(it, state="hidden")

    # ------------------------------------------------------------------- HUD
    def _build_hud(self):
        c = self.c
        self.hp_bg = c.create_rectangle(24, 22, 264, 34, fill="#0d1626",
                                        outline="#1d2c46")
        self.hp_chip = c.create_rectangle(24, 22, 264, 34, fill="#7a2038",
                                          outline="")
        self.hp_fg = c.create_rectangle(24, 22, 264, 34, fill=C_HP, outline="")
        self.hp_txt = c.create_text(144, 28, text="100", fill="#04121a",
                                    font=("Consolas", 9, "bold"))
        self.wave_txt = c.create_text(W - 24, 26, text="WAVE 1",
                                      fill=C_TEXT, font=F_BIG, anchor="e")
        self.score_txt = c.create_text(W - 24, 58, text="0",
                                       fill=C_DIM, font=F_NUM, anchor="e")
        self.combo_txt = c.create_text(W / 2, 26, text="", fill=C_GOLD,
                                       font=F_BIG, anchor="center")
        self.dash_pip_bgs = [
            c.create_rectangle(0, 0, 0, 0, fill="#0b1a26",
                               outline="#16303e") for _ in range(4)
        ]
        self.dash_pips = [
            c.create_rectangle(0, 0, 0, 0, fill=C_PLAYER, outline="")
            for _ in range(4)
        ]
        self.upg_txt = c.create_text(24, H - 24, text="", fill=C_DIM,
                                     font=F_MONO, anchor="sw", justify="left")
        self.boss_bg = c.create_rectangle(W / 2 - 300, 84, W / 2 + 300, 96,
                                          fill="#1a0a1e", outline="#3a1a42")
        self.boss_fg = c.create_rectangle(W / 2 - 300, 84, W / 2 + 300, 96,
                                          fill=C_BOSS, outline="")
        self.boss_txt = c.create_text(W / 2, 62, text="OVERSEER", fill=C_BOSS,
                                      font=F_MID, anchor="center")
        self.hud = [self.hp_bg, self.hp_chip, self.hp_fg, self.hp_txt,
                    self.wave_txt, self.score_txt, self.combo_txt, self.upg_txt]
        self.hud += self.dash_pip_bgs
        self.hud += self.dash_pips
        self.hud += [self.boss_bg, self.boss_fg, self.boss_txt]
        self._show_hud(False)

        self.banner_item = c.create_text(W / 2, H / 2 - 150, text="",
                                         fill=C_TEXT, font=F_HUGE)
        self.banner_item2 = c.create_text(W / 2, H / 2 - 112, text="",
                                          fill=C_DIM, font=F_UI)
        self.banner_rule = c.create_line(W / 2, H / 2 - 128, W / 2, H / 2 - 128,
                                         fill=C_PLAYER, width=2)
        # 사운드 상태 표시 (HUD와 무관하게 항상 보임)
        self.audio_txt = c.create_text(W - 24, H - 24, text="", fill=C_DIM,
                                       font=F_MONO, anchor="se")

    def _show_hud(self, on):
        st = "normal" if on else "hidden"
        for it in self.hud:
            self.c.itemconfigure(it, state=st)

    def _update_hud(self):
        c = self.c
        p = self.p
        ratio = clamp(p.hp / p.maxhp, 0, 1)
        # 잔상 체력바: 피해를 입으면 붉은 바가 천천히 따라 줄어든다
        if ratio < self.hp_ratio_chip:
            self.hp_ratio_chip += (ratio - self.hp_ratio_chip) * min(1.0, 3.2 * DT)
            if self.hp_ratio_chip - ratio < 0.004:
                self.hp_ratio_chip = ratio
        else:
            self.hp_ratio_chip = ratio
        c.coords(self.hp_chip, 26, 24, 26 + 236 * self.hp_ratio_chip, 32)
        c.coords(self.hp_fg, 26, 24, 26 + 236 * ratio, 32)
        if ratio > 0.5:
            col = C_HP
        elif ratio > 0.25:
            col = C_GOLD
        else:
            col = C_BAD if math.sin(self.ui_t * 9.0) > -0.2 else "#ff9bb0"
        c.itemconfigure(self.hp_fg, fill=col)
        c.itemconfigure(self.hp_txt, text=f"{int(p.hp)} / {int(p.maxhp)}")

        # 점수 카운트업
        d = self.score - self.score_shown
        if abs(d) < 1.0:
            self.score_shown = float(self.score)
        else:
            self.score_shown += d * min(1.0, 9.0 * DT)
        c.itemconfigure(self.score_txt, text=f"{int(self.score_shown):,}",
                        fill=C_GOLD if abs(d) > 1.0 else C_DIM)

        # 웨이브 팝
        c.itemconfigure(self.wave_txt, text=f"WAVE {self.wave}")
        c.coords(self.wave_txt, W - 24, 26 - 9.0 * self.wave_pop)
        c.itemconfigure(self.wave_txt,
                        fill="#ffffff" if self.wave_pop > 0.45 else C_TEXT)

        if self.combo > 1:
            c.coords(self.combo_txt, W / 2, 26 - 8.0 * self.combo_pop)
            c.itemconfigure(self.combo_txt,
                            text=f"x{self.combo_mult():.1f}  {self.combo} COMBO",
                            fill="#ffffff" if self.combo_pop > 0.35 else C_GOLD,
                            state="normal")
        else:
            c.itemconfigure(self.combo_txt, state="hidden")

        n = int(p.st["dash_max"])
        for i in range(4):
            bg = self.dash_pip_bgs[i]
            fg = self.dash_pips[i]
            if i >= n:
                c.itemconfigure(bg, state="hidden")
                c.itemconfigure(fg, state="hidden")
                continue
            x0 = 24 + i * 30
            c.coords(bg, x0, 44, x0 + 24, 50)
            c.itemconfigure(bg, state="normal")
            if i < p.charges:
                fill = C_PLAYER
                if p.charges >= n and math.sin(self.ui_t * 6.4) > 0.7:
                    fill = "#b8ffff"
                if self.dash_ready_t > 0:
                    fill = "#ffffff" if self.dash_ready_t > 0.35 else fill
                c.coords(fg, x0, 44, x0 + 24, 50)
                c.itemconfigure(fg, fill=fill, state="normal")
            elif i == p.charges and p.charge_t > 0:
                w = 24.0 * clamp(p.charge_t / max(p.dash_cd, 0.01), 0, 1)
                c.coords(fg, x0, 44, x0 + max(1.0, w), 50)
                c.itemconfigure(fg, fill="#2b7f8f", state="normal")
            else:
                c.itemconfigure(fg, state="hidden")

        if self.levels:
            parts = []
            for uid, lv in sorted(self.levels.items(), key=lambda kv: -kv[1]):
                parts.append(f"{UP_BY_ID[uid][1]} x{lv}")
            c.itemconfigure(self.upg_txt, text=" · ".join(parts[:7]))
        else:
            c.itemconfigure(self.upg_txt, text="")

        if self.boss is not None and self.boss.hp > 0:
            r = clamp(self.boss.hp / self.boss.maxhp, 0, 1)
            c.coords(self.boss_bg, W / 2 - 300, 84, W / 2 + 300, 96)
            c.coords(self.boss_fg, W / 2 - 298, 86,
                     W / 2 - 298 + 596 * r, 94)
            pulse = 0.75 + 0.25 * math.sin(self.ui_t * 6.0)
            c.itemconfigure(self.boss_fg,
                            fill=C_BOSS if pulse > 0.85 else "#ff8cf6")
            for it in (self.boss_bg, self.boss_fg, self.boss_txt):
                c.itemconfigure(it, state="normal")
        else:
            for it in (self.boss_bg, self.boss_fg, self.boss_txt):
                c.itemconfigure(it, state="hidden")

    # ----------------------------------------------------------------- 타이틀
    def _build_title(self):
        c = self.c
        c.delete("scr")
        self.title_age = 0.0
        self.hover_card = -1
        self._show_hud(False)
        # 색수차 글리치용 복제 레이어
        self.title_glitch_a = c.create_text(W / 2 + 4, 190, text="NEON VOID",
                                            fill="#ff2c66", font=F_TITLE,
                                            state="hidden", tags="scr")
        self.title_glitch_b = c.create_text(W / 2 - 4, 190, text="NEON VOID",
                                            fill="#24d7ff", font=F_TITLE,
                                            state="hidden", tags="scr")
        self.title_logo = c.create_text(W / 2, 190, text="NEON VOID", fill=C_PLAYER,
                                        font=F_TITLE, tags="scr")
        self.title_sub = c.create_text(W / 2, 250, text="", fill=C_DIM,
                                       font=F_MID, tags="scr")
        self.title_line = c.create_line(W / 2 - 240, 278, W / 2 + 240, 278,
                                        fill="#1d4a55", width=2, tags="scr")
        lines = [
            "설정한 이동 키 / 방향키  ·  이동",
            "마우스  ·  조준 (자동 사격)",
            f"{self.settings.bindings['dash'].upper()} / Shift  ·  이동 방향 대시 (무적)",
            f"{self.settings.bindings['pause'].upper()}  ·  일시정지      R  ·  재시작      M  ·  음소거",
        ]
        for i, t in enumerate(lines):
            c.create_text(W / 2, 330 + i * 26, text=t, fill=C_TEXT,
                          font=F_UI, tags="scr")
        c.create_text(W / 2, 480,
                      text="웨이브를 넘길 때마다 업그레이드 1장을 고르세요",
                      fill=C_DIM, font=F_UI, tags="scr")
        best = self.save["best_score"]
        c.create_text(W / 2, 512,
                      text=f"최고 기록  {best:,}  ·  최고 웨이브  {self.save['best_wave']}",
                      fill=C_GOLD, font=F_HUD, tags="scr")
        c.create_text(W / 2, 555,
                      text=f"누적 처치 {self.profile.total_kills:,}   ·   보스 {self.profile.boss_kills:,}   ·   완료 {self.profile.completed_runs:,}회",
                      fill=C_DIM, font=F_UI, tags="scr")
        self.title_press = c.create_text(W / 2, 600, text="PRESS  ENTER",
                                         fill=C_PLAYER, font=F_BIG, tags="scr")
        c.create_rectangle(W / 2 - 125, 624, W / 2 + 125, 666,
                           outline="#27576c", fill="#081827", width=1, tags="scr")
        c.create_text(W / 2, 645, text="O  ·  게임 설정",
                      fill=C_TEXT, font=F_UI, tags="scr")
        c.create_text(W / 2, 692, text="PYTHON  ·  NEON VOID",
                      fill="#22374d", font=F_MONO, tags="scr")
        # 커서가 항상 위에 오도록
        c.tag_raise("cursor")

    def _render_title(self):
        c = self.c
        u = self.ui_t
        age = self.title_age
        bob = math.sin(u * 1.7) * 5.0
        sway = math.sin(u * 0.9) * 3.0
        glitch = (u % 3.9) < 0.14
        gx = rnd(-5, 5) if glitch else 0.0
        gy = rnd(-2, 2) if glitch else 0.0
        c.coords(self.title_logo, W / 2 + sway + gx, 190 + bob + gy)
        c.itemconfigure(self.title_logo,
                        fill="#ffffff" if glitch else C_PLAYER)
        if glitch:
            c.coords(self.title_glitch_a, W / 2 + sway + gx + 5, 190 + bob - 2)
            c.coords(self.title_glitch_b, W / 2 + sway + gx - 5, 190 + bob + 2)
            c.itemconfigure(self.title_glitch_a, state="normal")
            c.itemconfigure(self.title_glitch_b, state="normal")
        else:
            c.itemconfigure(self.title_glitch_a, state="hidden")
            c.itemconfigure(self.title_glitch_b, state="hidden")

        full = "네온 로그라이크 탄막 슈터"
        n = int(clamp(age * 13.0, 0, len(full)))
        caret = "▌" if (age > len(full) / 13.0 and int(u * 3.0) % 2 == 0) else ""
        c.itemconfigure(self.title_sub, text=full[:n] + caret)

        v = 0.5 + 0.5 * math.sin(u * 3.4)
        col = "#6ff2ff" if v > 0.5 else "#2c6b78"
        c.itemconfigure(self.title_press, fill=col)
        c.coords(self.title_press, W / 2, 600 + math.sin(u * 3.0) * 4.0)
        x = (u * 220) % (W + 200) - 100
        c.coords(self.title_line, max(W / 2 - 240, x), 278,
                 min(W / 2 + 240, x + 120), 278)

    def build_upgrade_screen(self):
        c = self.c
        c.delete("scr")
        c.create_rectangle(0, 0, W, H, fill="#04060e", stipple="gray50",
                           outline="", tags="scr")
        c.create_text(W / 2, 104, text=f"WAVE {self.wave} CLEAR",
                      fill=C_HP, font=F_BIG, tags="scr")
        c.create_text(W / 2, 146,
                      text="업그레이드 1장을 선택하세요  (1 / 2 / 3 또는 클릭)",
                      fill=C_DIM, font=F_UI, tags="scr")

        self.cards = []
        self.card_anim = []
        self.card_yoff = []
        self.card_borders = []
        n = len(self.card_opts)
        cw, ch, gap = 300, 300, 36
        total = n * cw + (n - 1) * gap
        x0 = (W - total) / 2
        y0 = 210
        for i, u in enumerate(self.card_opts):
            uid, name, desc, rarity, mx = u
            x = x0 + i * (cw + gap)
            col = RARITY_COL[rarity]
            lv = self.levels.get(uid, 0)
            before = set(c.find_all())
            border = c.create_rectangle(x, y0, x + cw, y0 + ch,
                                        fill="#080e1a", outline=col,
                                        width=2, tags="scr")
            c.create_rectangle(x + 6, y0 + 6, x + cw - 6, y0 + ch - 6,
                               fill="", outline="#16233a", tags="scr")
            c.create_text(x + 24, y0 + 34, text=f"[{i + 1}]", fill=col,
                          font=F_HUD, anchor="w", tags="scr")
            c.create_text(x + cw - 24, y0 + 34, text=RARITY_NAME[rarity],
                          fill=col, font=F_MONO, anchor="e", tags="scr")
            self._card_glyph(x + cw / 2, y0 + 82, 19, rarity, col)
            c.create_text(x + cw / 2, y0 + 136, text=name, fill=C_TEXT,
                          font=F_BIG, tags="scr")
            c.create_text(x + cw / 2, y0 + 198, text=desc, fill=C_DIM,
                          font=F_UI, width=cw - 48, justify="center", tags="scr")
            if mx < 90:
                c.create_text(x + cw / 2, y0 + 256,
                              text=f"LV {lv} → {lv + 1} / {mx}",
                              fill="#3f5a75", font=F_NUM, tags="scr")
            else:
                c.create_text(x + cw / 2, y0 + 256, text="즉시 적용",
                              fill="#3f5a75", font=F_NUM, tags="scr")
            self.cards.append((x, y0, x + cw, y0 + ch))
            self.card_borders.append(border)
            self.card_anim.append(0.0)
            off = 58.0 + i * 24.0
            self.card_yoff.append(off)
            tag = f"upgrade-card-{i}"
            for item in set(c.find_all()) - before:
                c.addtag_withtag(tag, item)
            c.move(tag, 0, off)
        c.create_text(W / 2, H - 46,
                      text=f"SCORE {self.score:,}   ·   처치 {self.kills}",
                      fill=C_DIM, font=F_UI, tags="scr")

    def _animate_upgrade_cards(self):
        c = self.c
        for i, border in enumerate(self.card_borders):
            progress = clamp((self.card_t - i * 0.09) / 0.42, 0.0, 1.0)
            eased = 1.0 - (1.0 - progress) ** 3
            self.card_anim[i] = eased
            hover = i == self.hover_card and progress > 0.5
            bob = math.sin(self.ui_t * 2.3 + i) * 1.6 if progress >= 1 else 0.0
            offset = (58.0 + i * 24.0) * (1.0 - eased) + bob
            if hover:
                offset -= 11.0
            c.move(f"upgrade-card-{i}", 0, offset - self.card_yoff[i])
            self.card_yoff[i] = offset
            c.itemconfigure(border, width=3 if hover else 2,
                            outline="#ffffff" if hover else
                            RARITY_COL[self.card_opts[i][3]])

    def _card_glyph(self, x, y, r, rarity, col):
        c = self.c
        outer = self._poly(6, x, y, r, -math.pi / 2)
        c.create_polygon(*[v for pt in outer for v in pt], fill="",
                         outline=col, width=2, tags="scr")
        if rarity >= 2:
            for off in (0.0, math.pi / 3.0):
                tri = self._poly(3, x, y, r * 0.66, -math.pi / 2 + off)
                c.create_polygon(*[v for pt in tri for v in pt], fill=col,
                                 outline="", tags="scr")
        else:
            n = 4 if rarity == 1 else 3
            inner = self._poly(n, x, y, r * 0.6, -math.pi / 2)
            c.create_polygon(*[v for pt in inner for v in pt], fill=col,
                             outline="", tags="scr")

    def _draw_pause(self):
        c = self.c
        c.delete("ovl")
        if not self.paused:
            return
        c.create_rectangle(0, 0, W, H, fill="#04060e", stipple="gray50",
                           outline="", tags="ovl")
        self.pause_title = c.create_text(W / 2, H / 2 - 60, text="PAUSED",
                                         fill=C_PLAYER, font=F_HUGE, tags="ovl")
        c.create_text(W / 2, H / 2 - 10,
                      text=f"WAVE {self.wave}   ·   SCORE {self.score:,}",
                      fill=C_TEXT, font=F_MID, tags="ovl")
        c.create_text(W / 2, H / 2 + 40, text=f"{self.settings.bindings['pause'].upper()}  ·  계속하기",
                      fill=C_DIM, font=F_UI, tags="ovl")
        c.create_text(W / 2, H / 2 + 66, text="R  ·  재시작",
                      fill=C_DIM, font=F_UI, tags="ovl")
        c.create_text(W / 2, H / 2 + 108, text="O  ·  게임 설정",
                      fill=C_PLAYER, font=F_UI, tags="ovl")


    # --------------------------------------------------------------- 환경 설정
    def _draw_settings(self):
        if not self.settings_open:
            return
        c = self.c
        c.delete("cfg")
        c.create_rectangle(0, 0, W, H, fill="#030912", outline="",
                           stipple="gray75", tags="cfg")
        c.create_rectangle(260, 90, W - 260, 673, fill="#071320",
                           outline="#2a5166", width=2, tags="cfg")
        is_controls = self.settings_page == "controls"
        c.create_text(W / 2, 139,
                      text="KEY BINDINGS" if is_controls else "SETTINGS",
                      fill=C_PLAYER, font=F_HUGE, tags="cfg")
        c.create_text(W / 2, 185,
                      text="방향키 / WASD로 선택 · Enter로 키 변경"
                      if is_controls else
                      "방향키 / WASD로 선택 · 좌우로 변경",
                      fill=C_DIM, font=F_UI, tags="cfg")

        if is_controls:
            items = [
                (ACTION_LABELS[action], self.settings.bindings[action].upper())
                for action in ACTION_ORDER
            ]
            first_y, row_step, row_height = 206, 56, 46
        else:
            prefs = self.settings
            items = [
                ("전체 볼륨", f"{round(prefs.volume * 100)}%"),
                ("음소거", "켜짐" if prefs.muted else "꺼짐"),
                ("연출 줄이기", "켜짐" if prefs.reduced_effects else "꺼짐"),
                ("FPS 표시", "켜짐" if self.show_fps else "꺼짐"),
            ]
            first_y, row_step, row_height = 225, 74, 59

        self.settings_hits = []
        for index, (title, value) in enumerate(items):
            x0, x1 = 320, W - 320
            y0 = first_y + row_step * index
            y1 = y0 + row_height
            self.settings_hits.append((x0, y0, x1, y1))
            selected = index == self.settings_selected
            rebinding = (is_controls and self.rebinding_action == ACTION_ORDER[index])
            border = C_GOLD if rebinding else (C_PLAYER if selected else "#1b3247")
            c.create_rectangle(x0, y0, x1, y1,
                               outline=border, fill="#0b2030" if selected
                               else "#081826", width=2 if selected else 1,
                               tags="cfg")
            c.create_text(x0 + 25, (y0 + y1) / 2, text=title, fill=C_TEXT,
                          font=F_MID, anchor="w", tags="cfg")
            c.create_text(x1 - 24, (y0 + y1) / 2,
                          text="입력 대기…" if rebinding else
                          (f"[ {value} ]" if is_controls else f"◀  {value}  ▶"),
                          fill=C_GOLD if selected else C_DIM,
                          font=F_UI, anchor="e", tags="cfg")

        next_page = "일반 설정" if is_controls else "키 설정"
        c.create_text(W / 2, 569, text=f"TAB  ·  {next_page}",
                      fill=C_GOLD, font=F_UI, tags="cfg")
        c.create_text(W / 2, 611, text="ESC / O  ·  돌아가기",
                      fill=C_PLAYER, font=F_MID, tags="cfg")
        hint = ("Enter / 클릭: 키 등록   ·   Delete: 기본값 복원   ·   Shift/방향키 보조 조작 유지"
                if is_controls else
                "설정 변경 즉시 저장   ·   볼륨은 Windows 믹서에서 적용")
        c.create_text(W / 2, 638, text=hint, fill=C_DIM, font=F_MONO, tags="cfg")
        if self.settings_notice:
            c.create_text(W / 2, 658, text=self.settings_notice,
                          fill=C_GOLD, font=F_MONO, tags="cfg")
        c.tag_raise("cfg")
        c.tag_raise("cursor")

    # ------------------------------------------------------------------ 렌더
    def render(self):
        c = self.c
        ox = oy = 0.0
        if self.shake > 0.2 and not self.settings.reduced_effects:
            ox = rnd(-1, 1) * self.shake
            oy = rnd(-1, 1) * self.shake
        self._move_background(ox, oy)

        self.pool_enemy.begin()
        self.pool_ring.begin()
        self.pool_particle.begin()
        self.pool_ebullet.begin()
        self.pool_streak.begin()
        self.pool_bullet.begin()
        self.pool_shard.begin()
        self.pool_ftext.begin()
        self.pool_line.begin()
        self.pool_drone.begin()

        if self.state in ("play", "upgrade"):
            self.draw_world(ox, oy)

        self.pool_enemy.end()
        self.pool_ring.end()
        self.pool_particle.end()
        self.pool_ebullet.end()
        self.pool_streak.end()
        self.pool_bullet.end()
        self.pool_shard.end()
        self.pool_ftext.end()
        self.pool_line.end()
        self.pool_drone.end()

        if self.state == "play":
            self._update_hud()
        if self.banner_t > 0 and self.state == "play":
            remaining = clamp(self.banner_t / max(self.banner_total, 0.01), 0, 1)
            appeared = clamp((1.0 - remaining) / 0.22, 0, 1)
            eased = 1.0 - (1.0 - appeared) ** 3
            y = H / 2 - 150 - (1.0 - eased) * 35.0
            c.coords(self.banner_item, W / 2, y)
            c.coords(self.banner_item2, W / 2, y + 38)
            c.coords(self.banner_rule, W / 2 - 140 * eased, y + 23,
                     W / 2 + 140 * eased, y + 23)
            c.itemconfigure(self.banner_item, text=self.banner,
                            state="normal", fill="#9fe8ff" if remaining > 0.2 else C_DIM)
            c.itemconfigure(self.banner_item2, text="적을 모두 제거하세요",
                            state="normal")
            c.itemconfigure(self.banner_rule, state="normal")
        else:
            for it in (self.banner_item, self.banner_item2, self.banner_rule):
                c.itemconfigure(it, state="hidden")

        if self.state == "title":
            self._render_title()
        elif self.state == "upgrade":
            self._animate_upgrade_cards()
        elif self.state == "dead":
            self._animate_game_over()
        if self.paused and self.state == "play":
            self._animate_pause()
        self._update_audio_hud()
        if self.show_fps:
            item = getattr(self, "_fps_item", None)
            if not item:
                self._fps_item = c.create_text(
                    12, H - 10, text="", fill="#2c4a66", font=F_MONO, anchor="sw")
                item = self._fps_item
            c.itemconfigure(item, text=f"{self.fps:5.1f} fps  "
                                       f"obj {len(self.bullets)}+{len(self.enemies)}",
                            state="normal")
        elif getattr(self, "_fps_item", None):
            c.itemconfigure(self._fps_item, state="hidden")
        self._render_screen_fx()
        self.draw_cursor()
        c.tag_raise("cursor")

    def _animate_pause(self):
        appear = clamp(self.pause_t / 0.24, 0, 1)
        ease = 1.0 - (1.0 - appear) ** 3
        self.c.coords(self.pause_title, W / 2, H / 2 - 60 - (1 - ease) * 24)
        self.c.itemconfigure(self.pause_title,
                             fill="#ffffff" if appear < 0.45 else C_PLAYER)

    def _animate_game_over(self):
        appear = clamp(self.go_t / 0.48, 0, 1)
        ease = 1.0 - (1.0 - appear) ** 3
        self.c.coords(self.dead_title, W / 2, 200 + (1.0 - ease) * 45)
        self.c.coords(self.dead_restart, W / 2,
                      420 + math.sin(self.ui_t * 3.2) * 4.0)
        self.c.itemconfigure(self.dead_restart,
                             fill=C_PLAYER if int(self.ui_t * 2) % 2 else "#b5faff")

    def _render_screen_fx(self):
        c = self.c
        if self.flash_t > 0:
            c.itemconfigure(self.flash_rect, fill=self.flash_col,
                            stipple="gray12" if self.flash_t < self.flash_dur * 0.5
                            else "gray25", state="normal")
        else:
            c.itemconfigure(self.flash_rect, state="hidden")
        if self.sweep_t > 0:
            progress = 1.0 - self.sweep_t / max(self.sweep_dur, 0.01)
            y = -40 + progress * (H + 80)
            c.coords(self.sweep_band, 0, y - 20, W, y + 20)
            c.coords(self.sweep_line, 0, y, W, y)
            for it in (self.sweep_band, self.sweep_line):
                c.itemconfigure(it, fill=self.sweep_col, state="normal")
        else:
            c.itemconfigure(self.sweep_band, state="hidden")
            c.itemconfigure(self.sweep_line, state="hidden")
        c.tag_raise("fx")

    def _update_audio_hud(self):
        it = self.audio_txt
        if self.mute_t > 0:
            self.c.itemconfigure(it, state="normal",
                                 text="SOUND ON  ·  M" if not self.muted
                                 else "MUTED  ·  M",
                                 fill=C_DIM if self.muted else "#4f8fa0")
        elif self.muted:
            self.c.itemconfigure(it, state="normal", text="MUTED  ·  M",
                                 fill="#2c4457")
        else:
            self.c.itemconfigure(it, state="hidden")

    def draw_world(self, ox, oy):
        c = self.c
        p = self.p

        for s in self.shards:
            r = 3.2 + math.sin(s.life * 9.0) * 0.8
            col = C_SHARD if s.life > 3.0 else "#3d6b78"
            self.pool_shard.draw(s.x + ox, s.y + oy, r, col)
            self.pool_ring.draw(s.x + ox, s.y + oy, r + 2.4, "", outline=col)

        for e in self.enemies:
            col = E_PARAMS[e.kind]["color"]
            if e.flash > 0.35:
                col = "#ffffff"
            x = e.x + ox
            y = e.y + oy
            r = e.r
            if e.kind in ("drifter", "mini"):
                pts = self._tri(x, y, r * 1.25, e.ang)
                self.pool_enemy.draw(pts, col)
                self.pool_ring.draw(x, y, r * 0.42, "#ffd7e6")
            elif e.kind == "shooter":
                self.pool_enemy.draw(self._poly(4, x, y, r, e.t * 0.8), col)
                self.pool_enemy.draw(self._poly(4, x, y, r * 0.5, e.t * 0.8),
                                     "#7a4d00")
            elif e.kind == "splitter":
                self.pool_enemy.draw(self._poly(5, x, y, r, e.t * 0.6), col)
                self.pool_ring.draw(x, y, r * 0.55, "", outline="#e2c8ff")
            elif e.kind == "rusher":
                if e.state == 1:
                    self.pool_ring.draw(x, y, r + 10, "", outline=C_RUSH)
                self.pool_enemy.draw(self._arrow(x, y, r * 1.5, e.ang), col)
                self.pool_ring.draw(x, y, r * 0.35, "#ffe0c0")
            elif e.kind == "boss":
                spin = e.t * 0.9
                self.pool_ring.draw(x, y, r + 14, "", outline=C_BOSS)
                self.pool_ring.draw(x, y, r + 6, "", outline="#7d2f78")
                self.pool_enemy.draw(self._poly(6, x, y, r, spin), col)
                self.pool_enemy.draw(self._poly(6, x, y, r * 0.62, -spin * 1.6),
                                     "#5c1058")
                self.pool_ring.draw(x, y, r * 0.3, "#ffe6ff")

        for q in self.particles:
            t = q.life / q.maxlife
            r = q.r * (0.25 + 0.75 * t)
            self.pool_particle.draw(q.x + ox, q.y + oy, r, q.color)

        for b in self.ebullets:
            self.pool_ebullet.draw(b.x + ox, b.y + oy, b.r, "#ff9fbb")
            self.pool_ebullet.draw(b.x + ox, b.y + oy, b.r * 0.45, b.color)

        for b in self.bullets:
            tx = b.vx * 0.032
            ty = b.vy * 0.032
            self.pool_streak.draw(b.x - tx + ox, b.y - ty + oy,
                                  b.x + ox, b.y + oy, "#33500a", 6)
            self.pool_streak.draw(b.x - tx + ox, b.y - ty + oy,
                                  b.x + ox, b.y + oy, b.color, 3)
            self.pool_bullet.draw(b.x + ox, b.y + oy, b.r * 0.85, "#f4ffd6")

        for (x1, y1, x2, y2, life) in self.chain_fx:
            self.pool_line.draw(x1 + ox, y1 + oy, x2 + ox, y2 + oy,
                                "#9fe8ff", 2)

        if p.st["drone"] > 0:
            n = int(p.st["drone"])
            for i in range(n):
                a = p.drone_ang + TAU * i / n
                dx = p.x + math.cos(a) * 52 + ox
                dy = p.y + math.sin(a) * 52 + oy
                self.pool_ring.draw(dx, dy, 8.0, "", outline=C_PLAYER)
                self.pool_drone.draw(dx, dy, 4.0, C_PLAYER)

        if self.state in ("play", "upgrade"):
            self.draw_player(ox, oy)

        for ft in self.ftexts:
            if ft.life > 0.15:
                self.pool_ftext.draw(ft.x + ox, ft.y + oy, ft.text, ft.color)

    @staticmethod
    def _poly(n, x, y, r, off):
        pts = []
        for i in range(n):
            a = off + TAU * i / n
            pts.append((x + math.cos(a) * r, y + math.sin(a) * r))
        return pts

    @staticmethod
    def _tri(x, y, r, ang):
        return [(x + math.cos(ang) * r, y + math.sin(ang) * r),
                (x + math.cos(ang + 2.35) * r * 0.85,
                 y + math.sin(ang + 2.35) * r * 0.85),
                (x + math.cos(ang - 2.35) * r * 0.85,
                 y + math.sin(ang - 2.35) * r * 0.85)]

    @staticmethod
    def _arrow(x, y, r, ang):
        return [(x + math.cos(ang) * r, y + math.sin(ang) * r),
                (x + math.cos(ang + 2.5) * r * 0.62,
                 y + math.sin(ang + 2.5) * r * 0.62),
                (x + math.cos(ang + math.pi) * r * 0.45,
                 y + math.sin(ang + math.pi) * r * 0.45),
                (x + math.cos(ang - 2.5) * r * 0.62,
                 y + math.sin(ang - 2.5) * r * 0.62)]

    def draw_player(self, ox, oy):
        c = self.c
        p = self.p
        if p.inv > 0 and int(self.t * 22) % 2 == 0:
            body = "#6b8f9c"
            glow = "#8a2b3a"
        else:
            body = C_PLAYER
            glow = C_PLAYER_GLOW
        a = p.aim
        ca, sa = math.cos(a), math.sin(a)
        nose = 21.0 - p.recoil * 4.0
        x, y = p.x + ox, p.y + oy
        pts = [
            x + ca * nose, y + sa * nose,
            x + math.cos(a + 2.42) * 13.0, y + math.sin(a + 2.42) * 13.0,
            x - ca * 7.0, y - sa * 7.0,
            x + math.cos(a - 2.42) * 13.0, y + math.sin(a - 2.42) * 13.0,
        ]
        c.coords(self.ship, *pts)
        c.itemconfigure(self.ship, fill=body, outline="#e8ffff", width=1)
        if p.dash_t > 0:
            dash_k = clamp(p.dash_t / 0.145, 0, 1)
            self.pool_ring.draw(x, y, 27.0 + (1 - dash_k) * 21.0,
                                "", outline=C_PLAYER)
        self.pool_ring.draw(x, y, 25.0, "", outline=glow)
        self.pool_ring.draw(x + ca * 7.0, y + sa * 7.0, 3.0, "#eaffff")

        sp = math.hypot(p.vx, p.vy)
        if sp > 40:
            bx = x - ca * 15.0
            by = y - sa * 15.0
            rr = 3.0 + min(5.0, sp * 0.012)
            c.coords(self.thrust, bx - rr, by - rr, bx + rr, by + rr)
            c.itemconfigure(self.thrust, state="normal", fill="#1c6d7d")
        else:
            c.itemconfigure(self.thrust, state="hidden")
        c.coords(self.aim_dot, x + ca * 42 - 3, y + sa * 42 - 3,
                 x + ca * 42 + 3, y + sa * 42 + 3)
        c.itemconfigure(self.aim_dot, outline=glow)

    # ------------------------------------------------------------------ 루프
