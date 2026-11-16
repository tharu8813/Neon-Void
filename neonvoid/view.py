"""Canvas HUD and rendering mixin."""

from __future__ import annotations

import math
import tkinter as tk
from .content.enemies import E_PARAMS
from .content.sectors import SECTORS, BOSS_SIGNATURE_SOFTWARE
from .content.virus_catalog import ENCRYPTION_PULSE_RADIUS, boss_phase
from .controls import ACTION_ORDER, ACTION_LABELS
from .content.upgrades import UP_BY_ID, preview_upgrade
from .combat import effective_armor, skill_cost, skill_cooldown
from .metaprogression import xp_progress
from .infection import InfectionStage, SPREADING_AT
from .world.session import (WORLD_W, WORLD_H, SECTOR_NAMES, AVAILABLE_SECTORS,
                            CPU_EXIT, START, PILLARS, camera_for,
                            BASE_NODES, OS_NODES)
from .constants import (W, H, DT, TAU, clamp, rnd, C_GRID, C_STAR, C_PLAYER, C_PLAYER_GLOW, C_RUSH, C_BOSS, C_SHARD, C_TEXT, C_DIM, C_HP, C_BAD, C_GOLD, F_TITLE, F_HUGE, F_BIG, F_MID, F_UI, F_HUD, F_NUM, F_MONO, RARITY_COL, RARITY_NAME)

BOSS_WINDUPS = {
    "overload_barrage": "방사형 탄막 예고",
    "encryption_pulse": "암호화 폭발 예고 · 원 밖으로 이동",
    "breach_barrage": "방어 관통 탄막 예고",
}

UPGRADE_STAT_LABELS = {
    "rate": "초당 발사", "dmg": "탄환 피해", "pierce": "추가 관통",
    "split": "전방 탄환 수", "homing": "유도 회전 속도",
    "explode": "폭발 반경", "crit": "치명타 확률",
    "lifesteal": "처치당 회복", "thorns": "반사 피해",
    "dash_max": "최대 대시 충전", "dash_blast": "대시 폭발 피해",
    "slow": "처치당 감속 시간", "magnet": "파편 수집 반경",
    "drone": "위성 드론 수", "chain": "연쇄 방전 단계",
    "rear": "후방 탄환 수", "ricochet": "벽 반사 횟수",
    "armor": "방어력", "armor_pen": "방어 관통력",
    "mana_regen": "초당 마나 회복", "burn_chance": "화상 확률",
    "burn_dps": "화상 초당 피해", "toxin_chance": "중독 확률",
    "toxin_dps": "중독 초당 피해", "life_on_hit": "명중당 회복",
    "skill_power": "충격파 배율", "skill_cooldown": "스킬 대기 배율",
    "skill_efficiency": "마나 소모 배율", "hp_regen": "초당 체력 회복",
    "effect_duration": "지속시간 배율",
}
UPGRADE_PERCENT_STATS = {"crit", "burn_chance", "toxin_chance", "skill_power",
                         "skill_cooldown", "skill_efficiency", "effect_duration"}


class VisualMixin:
    """Uses the Game object's state without owning a Tk root."""

    PROFILE_PAGE_SIZE = 7

    @staticmethod
    def _infection_hud_severity(infection):
        """Use words as well as colors to communicate OS and infection danger."""
        if (infection.stage >= InfectionStage.CRITICAL
                or infection.os_integrity <= 25):
            return "위험", "#ff5872", "#ff7082", "#1d0c16"
        if (infection.stage >= InfectionStage.DETECTED
                or infection.os_integrity <= 50):
            return "주의", "#e5a34e", "#b8824f", "#1b1718"
        return "안정", "#55cba8", "#384457", "#0b1526"

    @staticmethod
    def _boss_hud_labels(kind, phase, windup):
        """Separate boss identity from an urgent attack warning."""
        return (f"{kind.upper()}  ·  PHASE {phase}",
                f"주의: {windup}" if windup else "")

    def _hud_pop_y(self, amount, distance):
        """Pop feedback is stationary when reduced effects are enabled."""
        return 26 if self.settings.reduced_effects else 26 - distance * amount

    def _hud_software_summary(self, max_chars=49):
        """Keep the bottom-left software label within its HUD budget."""
        if not self.levels:
            return ""
        entries = [f"{UP_BY_ID[uid][1]} x{lv}" for uid, lv in
                   sorted(self.levels.items(), key=lambda kv: (-kv[1], kv[0]))]
        prefix = "소프트웨어: "
        shown = []
        for index, entry in enumerate(entries):
            candidate = " · ".join((*shown, entry))
            remaining = len(entries) - index - 1
            suffix = f" · 외 {remaining}종 [TAB]" if remaining else ""
            if len(prefix + candidate + suffix) > max_chars:
                break
            shown.append(entry)
        hidden = len(entries) - len(shown)
        if not shown:
            return f"소프트웨어 {len(entries)}종 [TAB]"
        suffix = f" · 외 {hidden}종 [TAB]" if hidden else ""
        return prefix + " · ".join(shown) + suffix


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
        u = 0.0 if self.settings.reduced_effects else self.ui_t
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
        if ev is not None:
            self.mx, self.my = ev.x, ev.y

    def _on_pointer_leave(self, ev=None):
        self._update_menu_hover(-1, -1)
        self.cursor_on = False
        self.cursor_trail.clear()
        try:
            self.c.configure(cursor="")
        except tk.TclError:
            pass

    def _on_focus_out(self, ev=None):
        self._update_menu_hover(-1, -1)
        self.keys.clear()
        self.mouse_down = False
        self.cursor_on = False
        self.cursor_trail.clear()
        try:
            self.c.configure(cursor="")
        except tk.TclError:
            pass

    def _aim_cursor(self):
        return self.state == "play" and not self.paused and not self.settings_open

    def draw_cursor(self):
        c = self.c
        items = ([self.cursor_halo] + self.cursor_trail_items + self.cursor_arcs
                 + self.cursor_ticks + self.cursor_cross
                 + [self.cursor_dot] + self.cursor_hit_marks)
        aiming = self.cursor_on and self._aim_cursor()
        c.configure(cursor="none" if aiming else "")
        if not aiming:
            for it in items:
                c.itemconfigure(it, state="hidden")
            return
        x, y = self.mx, self.my
        reduced = self.settings.reduced_effects
        u = 0.0 if reduced else self.ui_t
        p = self.p
        dash = clamp(p.dash_t / 0.145, 0, 1) if p.dash_t > 0 else 0.0
        hot = False
        for e in self.enemies:
            if e.hp > 0 and (e.x - x) ** 2 + (e.y - y) ** 2 < (e.r + 8) ** 2:
                hot = True
                break
        self.cursor_hot = hot
        if hot:
            col = C_BAD
        else:
            col = C_PLAYER
        heat = clamp(p.recoil, 0, 1) if self.state == "play" else 0.0
        self.cursor_spin = (0.0 if reduced else getattr(self, "cursor_spin", 0.0)
                            + (0.9 + heat * 7.5 + dash * 13.0) / 60.0)
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
            if reduced or i >= len(self.cursor_trail):
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
        self.mana_bg = c.create_rectangle(24, 60, 264, 74, fill="#0b1528",
                                          outline="#274e79")
        self.mana_fg = c.create_rectangle(26, 62, 262, 72, fill="#528dff",
                                          outline="")
        self.mana_txt = c.create_text(144, 67, text="MANA 100 / 100",
                                      fill="#eff5ff", font=F_MONO)
        # HUD widgets are allocated once, then updated rather than recreated.
        # LIVE STATS 패널은 구역 출구 문과 겹치지 않도록 y=262에서 끝난다.
        self.stats_bg = c.create_rectangle(19, 90, 373, 262, fill="#070f1e",
                                            outline="#21394e", width=1)
        self.stats_title = c.create_text(34, 105, text="LIVE STATS  /  실시간 능력치",
                                          fill=C_PLAYER, anchor="w", font=F_HUD)
        self.stats_left = c.create_text(34, 129, text="", anchor="nw",
                                         fill=C_TEXT, font=("Malgun Gothic", 10),
                                         justify="left")
        self.stats_right = c.create_text(194, 129, text="", anchor="nw",
                                          fill="#a4bfd7", font=("Malgun Gothic", 10),
                                          justify="left")
        self.stats_rule = c.create_line(30, 228, 362, 228, fill="#16233a")
        self.stats_effect = c.create_text(34, 245, text="", fill=C_GOLD,
                                           anchor="w", font=F_MONO)
        self.skills_bg = c.create_rectangle(W / 2 - 275, H - 86,
                                             W / 2 + 275, H - 24,
                                             fill="#07121e", outline="#28455b")
        self.skill_pulse_txt = c.create_text(W / 2 - 132, H - 55, text="",
                                              fill=C_PLAYER, font=F_HUD)
        self.skill_guard_txt = c.create_text(W / 2 + 132, H - 55, text="",
                                              fill=C_HP, font=F_HUD)
        # 스킬 쿨다운 진행 바
        self.skill_pulse_bar_bg = c.create_rectangle(W / 2 - 224, H - 42,
                                                     W / 2 - 40, H - 38,
                                                     fill="#0d2033",
                                                     outline="#16303e")
        self.skill_pulse_bar = c.create_rectangle(W / 2 - 224, H - 42,
                                                  W / 2 - 40, H - 38,
                                                  fill=C_PLAYER, outline="")
        self.skill_guard_bar_bg = c.create_rectangle(W / 2 + 40, H - 42,
                                                     W / 2 + 224, H - 38,
                                                     fill="#0d2033",
                                                     outline="#16303e")
        self.skill_guard_bar = c.create_rectangle(W / 2 + 40, H - 42,
                                                  W / 2 + 224, H - 38,
                                                  fill=C_HP, outline="")
        self.infection_bg = c.create_rectangle(
            W-420, 107, W-24, 190, fill="#0b1526", outline="#384457")
        self.infection_fill = c.create_rectangle(
            W-416, 134, W-416, 144, fill="#e8ae50", outline="")
        self.infection_text = c.create_text(
            W-411, 121, anchor="w", text="INFECTION  0% · OS 100%",
            fill="#e8c997", font=F_MONO)
        self.infection_hint = c.create_text(
            W-411, 156, anchor="w", text="", fill=C_TEXT, font=F_UI)
        self.infection_detail = c.create_text(
            W-411, 176, anchor="w", text="", fill="#92b9c4", font=F_MONO)
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
        self.boss_warning_txt = c.create_text(
            W / 2, 113, text="", fill=C_GOLD, font=F_HUD, anchor="center",
            state="hidden")
        self.hud = [self.hp_bg, self.hp_chip, self.hp_fg, self.hp_txt,
                    self.mana_bg, self.mana_fg, self.mana_txt,
                    self.stats_bg, self.stats_title, self.stats_left,
                    self.stats_right, self.stats_effect, self.stats_rule,
                    self.skills_bg,
                    self.skill_pulse_txt, self.skill_guard_txt,
                    self.skill_pulse_bar_bg, self.skill_pulse_bar,
                    self.skill_guard_bar_bg, self.skill_guard_bar,
                    self.wave_txt, self.score_txt, self.combo_txt, self.upg_txt,
                    self.infection_bg, self.infection_fill, self.infection_text,
                    self.infection_hint, self.infection_detail]
        self.hud += self.dash_pip_bgs
        self.hud += self.dash_pips
        self.hud += [self.boss_bg, self.boss_fg, self.boss_txt,
                     self.boss_warning_txt]
        self._show_hud(False)

        self.banner_item = c.create_text(W / 2, H / 2 - 150, text="",
                                         fill=C_TEXT, font=F_HUGE)
        self.banner_item2 = c.create_text(W / 2, H / 2 - 112, text="",
                                          fill=C_DIM, font=F_UI)
        self.banner_rule = c.create_line(W / 2, H / 2 - 128, W / 2, H / 2 - 128,
                                         fill=C_PLAYER, width=2)
        # 사운드 상태 표시 (HUD와 무관하게 항상 보임)
        # 다른 화면(허브 등)이 그려도 가려지지 않도록 항상 최상단 태그를 쓴다.
        self.audio_txt = c.create_text(W - 24, H - 24, text="", fill=C_DIM,
                                       font=F_MONO, anchor="se",
                                       tags=("hud-top",))
        # 어느 화면에서든 볼 수 있는 알림 메시지
        self.message_txt = c.create_text(W / 2, H - 46, text="", fill=C_BAD,
                                         font=F_UI, state="hidden",
                                         tags=("hud-top",))
        self.message_bg = c.create_rectangle(0, 0, 0, 0, fill="#2a1018",
                                             outline="#ff7082", width=2,
                                             state="hidden", tags=("hud-top",))

    def _show_hud(self, on):
        st = "normal" if on else "hidden"
        for it in self.hud:
            self.c.itemconfigure(it, state=st)

    def _skill_bar(self, bg, bar, x0, x1, remaining, total, color):
        """Fill a skill cooldown bar; a ready skill glows instead of filling."""
        c = self.c
        top, bottom = H - 42, H - 38
        if remaining > 0 and total > 0:
            ratio = clamp(remaining / total, 0.0, 1.0)
            c.coords(bar, x0, top, x0 + (x1 - x0) * ratio, bottom)
            c.itemconfigure(bar, fill="#2b7f8f")
            c.itemconfigure(bg, fill="#0d2033")
        else:
            c.coords(bar, x0, top, x1, bottom)
            glow = 0.6 + 0.4 * math.sin(self.ui_t * 6.4)
            ready_color = (color if self.settings.reduced_effects else
                           "#ffffff" if glow > 0.86 else color)
            c.itemconfigure(bar, fill=ready_color)
            c.itemconfigure(bg, fill="#102b34")

    def _update_danger_vignette(self, hp_ratio):
        """Red edge bands that thicken and pulse as the player nears death.

        Tk canvas has no alpha blending, so intensity is expressed with the
        stipple density of the band and its thickness.
        """
        c = self.c
        danger = clamp(1.0 - (hp_ratio / 0.35), 0.0, 1.0)
        if danger <= 0.0 or self.settings.reduced_effects:
            for item in self.vig_items:
                c.itemconfigure(item, state="hidden")
            return
        beat = 0.5 + 0.5 * math.sin(self.ui_t * 5.6)
        level = danger * (0.55 + 0.45 * beat)
        stipple = ("gray50" if level > 0.66 else
                   "gray25" if level > 0.33 else "gray12")
        band = 18.0 + 26.0 * danger
        c.coords(self.vig_items[0], 0, 0, W, band)
        c.coords(self.vig_items[1], 0, H - band, W, H)
        c.coords(self.vig_items[2], 0, 0, band, H)
        c.coords(self.vig_items[3], W - band, 0, W, H)
        for item in self.vig_items:
            c.itemconfigure(item, state="normal", stipple=stipple)

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
            col = (C_BAD if self.settings.reduced_effects or
                   math.sin(self.ui_t * 9.0) > -0.2 else "#ff9bb0")
        c.itemconfigure(self.hp_fg, fill=col)
        c.itemconfigure(self.hp_txt, text=f"{int(p.hp)} / {int(p.maxhp)}")
        st = p.st
        mana_ratio = clamp(p.mana / max(1.0, p.maxmana), 0, 1)
        c.coords(self.mana_fg, 26, 62, 26 + 236 * mana_ratio, 72)
        c.itemconfigure(self.mana_fg, fill="#88b0ff" if mana_ratio > 0.25
                        else "#8c638e")
        c.itemconfigure(self.mana_txt,
                        text=f"MANA  {p.mana:4.0f} / {p.maxmana:.0f}")
        current_armor = effective_armor(st["armor"], p.guard_t)
        left = ("공격력   {:6.1f}\n연사 속도 {:5.1f}/s\n치명타   {:5.0f}%\n"
                "방어력   {:6.1f}\n방어 관통 {:6.1f}\n이동 속도 {:6.0f}").format(
                    st["dmg"], st["rate"], st["crit"] * 100,
                    current_armor, st["armor_pen"], st["speed"])
        right = ("마나 회복 {:5.1f}/s\n체력 재생 {:5.1f}/s\n적중 회복 {:5.1f}\n"
                 "화상 확률 {:5.0f}%\n중독 확률 {:5.0f}%\n스킬 위력 {:5.0f}%").format(
                    st["mana_regen"], st["hp_regen"] + (1.8 if p.guard_t > 0 else 0),
                    st["life_on_hit"], st["burn_chance"] * 100,
                    st["toxin_chance"] * 100, st["skill_power"] * 100)
        c.itemconfigure(self.stats_left, text=left)
        c.itemconfigure(self.stats_right, text=right)
        effect = f"방어막 {p.guard_t:.1f}s" if p.guard_t > 0 else "상시 효과 활성"
        c.itemconfigure(self.stats_effect,
                        text=f"{effect}  ·  지속 {st['effect_duration'] * 100:.0f}%")
        qcost = skill_cost(35.0, st["skill_efficiency"])
        ecost = skill_cost(25.0, st["skill_efficiency"])
        qstate = f"{p.pulse_cd:.1f}s" if p.pulse_cd > 0 else (
            "준비" if p.mana >= qcost else "마나 부족")
        estate = f"{p.guard_cd:.1f}s" if p.guard_cd > 0 else (
            "준비" if p.mana >= ecost else "마나 부족")
        qkey = self.settings.bindings["skill_pulse"].upper()
        ekey = self.settings.bindings["skill_guard"].upper()
        c.itemconfigure(self.skill_pulse_txt,
                        text=f"[{qkey}] 충격파  {qcost:.0f} MP  ·  {qstate}",
                        fill=C_PLAYER if qstate == "준비" else C_DIM)
        c.itemconfigure(self.skill_guard_txt,
                        text=f"[{ekey}] 보호막  {ecost:.0f} MP  ·  {estate}",
                        fill=C_HP if estate == "준비" else C_DIM)
        # 쿨다운 진행 바: 남은 시간을 비율로 채우고, 준비되면 통째로 빛난다.
        pulse_cd_max = skill_cooldown(7.0, st["skill_cooldown"])
        guard_cd_max = skill_cooldown(12.0, st["skill_cooldown"])
        self._skill_bar(self.skill_pulse_bar_bg, self.skill_pulse_bar,
                        W / 2 - 224, W / 2 - 40, p.pulse_cd, pulse_cd_max,
                        C_PLAYER)
        self._skill_bar(self.skill_guard_bar_bg, self.skill_guard_bar,
                        W / 2 + 40, W / 2 + 224, p.guard_cd, guard_cd_max,
                        C_HP)

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
        c.coords(self.wave_txt, W - 24, self._hud_pop_y(self.wave_pop, 9.0))
        c.itemconfigure(self.wave_txt,
                        fill="#ffffff" if not self.settings.reduced_effects and self.wave_pop > 0.45 else C_TEXT)

        if self.combo > 1:
            c.coords(self.combo_txt, W / 2, self._hud_pop_y(self.combo_pop, 8.0))
            c.itemconfigure(self.combo_txt,
                            text=f"x{self.combo_mult():.1f}  {self.combo} COMBO",
                            fill="#ffffff" if not self.settings.reduced_effects and self.combo_pop > 0.35 else C_GOLD,
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
                if (not self.settings.reduced_effects and p.charges >= n
                        and math.sin(self.ui_t * 6.4) > 0.7):
                    fill = "#b8ffff"
                if self.dash_ready_t > 0 and not self.settings.reduced_effects:
                    fill = "#ffffff" if self.dash_ready_t > 0.35 else fill
                c.coords(fg, x0, 44, x0 + 24, 50)
                c.itemconfigure(fg, fill=fill, state="normal")
            elif i == p.charges and p.charge_t > 0:
                w = 24.0 * clamp(p.charge_t / max(p.dash_cd, 0.01), 0, 1)
                c.coords(fg, x0, 44, x0 + max(1.0, w), 50)
                c.itemconfigure(fg, fill="#2b7f8f", state="normal")
            else:
                c.itemconfigure(fg, state="hidden")

        c.itemconfigure(self.upg_txt, text=self._hud_software_summary())

        if self.world.active_sector is not None:
            infection = self.infection
            percentage = max(0.0, min(1.0, infection.infection / 100.0))
            c.coords(self.infection_fill, W-416, 134,
                     W-416 + 387*percentage, 144)
            severity, color, outline, background = self._infection_hud_severity(
                infection)
            c.itemconfigure(self.infection_fill, fill=color)
            c.itemconfigure(self.infection_text,
                            text=f"{severity} · 감염 {infection.infection:.0f}%"
                                 f" · OS {infection.os_integrity:.0f}%",
                            fill=color)
            safe = self.world.active_sector in self.world.sector_clean
            critical = severity == "위험"
            if safe:
                hint = ("정화 완료 · OS 무결성 위험 · 허브 복구 권장"
                        if infection.os_integrity <= 25 else
                        "정화 완료 · 구역 내 감염 압력 없음")
            elif critical:
                key = self.settings.bindings["interact"].upper()
                hint = f"위험 · 왼쪽 출구에서 [{key}]로 복귀"
            elif infection.needs_purification:
                hint = "무결성 감소 중 · 허브 정화 권장"
            else:
                hint = f"감염 {SPREADING_AT:.0f}%부터 무결성 감소"
            c.itemconfigure(self.infection_hint, text=hint,
                            fill=C_BAD if critical else C_TEXT)
            if safe:
                detail = "정화 완료 · OS 감염 압력 없음"
            else:
                pressure = infection.integrity_pressure_per_second()
                purge = (self.current_encounter.definition.purify_on_clear
                         if self.current_encounter is not None else 0.0)
                seconds = infection.seconds_until_integrity_loss()
                if seconds is None:
                    detail = f"현재 무결성 감소 없음 · 클리어 정화 최대 {purge:.0f}%p"
                else:
                    detail = (f"현속 −{pressure:.1f}%/s · 소진 약 {seconds:.0f}s"
                              f" · 클리어 정화 {purge:.0f}%p")
            c.itemconfigure(self.infection_detail, text=detail)
            c.itemconfigure(self.infection_bg,
                            outline=outline, fill=background)
            for obj in (self.infection_bg, self.infection_fill,
                        self.infection_text, self.infection_hint,
                        self.infection_detail):
                c.itemconfigure(obj, state="normal")
        else:
            for obj in (self.infection_bg, self.infection_fill,
                        self.infection_text, self.infection_hint,
                        self.infection_detail):
                c.itemconfigure(obj, state="hidden")

        # 저체력 비네트: 체력이 낮을수록 화면 가장자리가 붉게 물든다.
        self._update_danger_vignette(ratio)

        if self.boss is not None and self.boss.hp > 0:
            r = clamp(self.boss.hp / self.boss.maxhp, 0, 1)
            c.coords(self.boss_bg, W / 2 - 300, 84, W / 2 + 300, 96)
            c.coords(self.boss_fg, W / 2 - 298, 86,
                     W / 2 - 298 + 596 * r, 94)
            pulse = 0.75 + 0.25 * math.sin(self.ui_t * 6.0)
            c.itemconfigure(self.boss_fg,
                            fill=C_BOSS if self.settings.reduced_effects or
                            pulse > 0.85 else "#ff8cf6")
            phase = (max(1, self.boss.state) if self.boss.kind == "boss"
                     else boss_phase(r))
            windup = next((BOSS_WINDUPS[action.behavior]
                           for _, source, action in self.virus_pending
                           if source is self.boss and action.behavior in BOSS_WINDUPS),
                          None)
            label, warning = self._boss_hud_labels(self.boss.kind, phase, windup)
            c.itemconfigure(self.boss_txt, text=label, fill=C_BOSS)
            c.itemconfigure(self.boss_warning_txt, text=warning,
                            state="normal" if warning else "hidden")
            for it in (self.boss_bg, self.boss_fg, self.boss_txt):
                c.itemconfigure(it, state="normal")
        else:
            for it in (self.boss_bg, self.boss_fg, self.boss_txt,
                       self.boss_warning_txt):
                c.itemconfigure(it, state="hidden")

    # ----------------------------------------------------------------- 타이틀
    def _menu_button(self, layer, key, x0, y0, x1, y1, label, shortcut, *, primary=False):
        c = self.c
        c.create_rectangle(x0, y0, x1, y1, width=2 if primary else 1,
                           outline=C_PLAYER if primary else "#31536b",
                           fill="#10313d" if primary else "#0a1826",
                           tags=(layer, "menu-button", f"action:{key}",
                                 "menu-primary" if primary else "menu-secondary"))
        c.create_text(x0 + 17, (y0 + y1) / 2, text=label, anchor="w",
                      fill=C_TEXT, font=F_HUD, tags=layer)
        c.create_text(x1 - 16, (y0 + y1) / 2, text=shortcut, anchor="e",
                      fill=C_PLAYER, font=F_MONO, tags=layer)

    def _menu_buttons(self):
        layer = ("salvageui" if self.salvage_open else
                 "extractui" if self.extraction_open else
                 "dbui" if self.database_open else
                 "profileui" if self.profile_open else
                 "loadoutui" if self.loadout_open else
                 "cfg" if self.settings_open else "ovl" if self.paused else "scr")
        for button in self.c.find_withtag("menu-button"):
            tags = self.c.gettags(button)
            if layer in tags:
                yield button, tags[2].removeprefix("action:")

    def _menu_action_at(self, x, y):
        for button, key in self._menu_buttons():
            x0, y0, x1, y1 = self.c.coords(button)
            if x0 <= x <= x1 and y0 <= y <= y1:
                return key
        return None

    def _update_menu_hover(self, x, y):
        """Keep all modal buttons visually responsive without rebuilding screens.

        Only the active layer participates; hidden menus must never appear
        interactive through an overlay. Disabled during key rebinding.
        """
        active = None if (getattr(self, "checkpoint_prompt_open", False)
                          or getattr(self, "rebinding_action", None)) else self._menu_action_at(x, y)
        for button, key in self._menu_buttons():
            primary = "menu-primary" in self.c.gettags(button)
            hovered = key == active
            self.c.itemconfigure(
                button,
                fill=("#185064" if hovered else
                      "#10313d" if primary else "#0a1826"),
                outline=(C_PLAYER if hovered or primary else "#31536b"),
                width=2 if hovered or primary else 1,
            )

    def _build_title(self):
        c = self.c
        c.delete("scr")
        self.title_age = 0.0
        self.hover_card = -1
        self._show_hud(False)
        c.create_line(420, 278, 860, 278, fill="#244454", tags="scr")
        c.create_text(W / 2, 123, text="네온 로그라이크 탄막 슈터",
                      fill=C_PLAYER, font=F_HUD, tags="scr")
        # 색수차 글리치용 복제 레이어
        self.title_glitch_a = c.create_text(W / 2 + 4, 190, text="NEON VOID",
                                            fill="#ff2c66", font=F_TITLE,
                                            state="hidden", tags="scr")
        self.title_glitch_b = c.create_text(W / 2 - 4, 190, text="NEON VOID",
                                            fill="#24d7ff", font=F_TITLE,
                                            state="hidden", tags="scr")
        self.title_logo = c.create_text(W / 2, 190, text="NEON VOID", fill=C_PLAYER,
                                        font=F_TITLE, tags="scr")
        c.create_text(W / 2, 245, text="웨이브를 돌파하고 소프트웨어로 무장을 강화하세요",
                      fill=C_TEXT, font=F_UI, tags="scr")
        self.title_line = c.create_line(420, 278, 420, 278,
                                        fill=C_PLAYER, width=2, tags="scr")
        lines = [
            "설정한 이동 키 / 방향키  ·  이동",
            f"마우스 · 조준  |  {self.settings.bindings['skill_pulse'].upper()} 충격파 / "
            f"{self.settings.bindings['skill_guard'].upper()} 보호막",
            f"{self.settings.bindings['dash'].upper()} / Shift  ·  이동 방향 대시 (무적)",
            f"{self.settings.bindings['pause'].upper()}  ·  일시정지      R  ·  재시작      M  ·  음소거",
        ]
        for i, t in enumerate(lines):
            c.create_text(W / 2, 330 + i * 26, text=t, fill=C_TEXT,
                          font=F_UI, tags="scr")
        c.create_text(W / 2, 306, text="기본 조작", fill=C_PLAYER, font=F_HUD, tags="scr")
        best = self.save["best_score"]
        c.create_text(W / 2, 512,
                      text=f"최고 기록  {best:,}  ·  최고 웨이브  {self.save['best_wave']}",
                      fill=C_GOLD, font=F_HUD, tags="scr")
        c.create_text(W / 2, 555,
                      text=f"누적 처치 {self.profile.total_kills:,}   ·   보스 {self.profile.boss_kills:,}   ·   완료 {self.profile.completed_runs:,}회",
                      fill=C_DIM, font=F_UI, tags="scr")
        self._menu_button("scr", "return", 420, 594, 690, 646,
                          "전투 시작", "ENTER", primary=True)
        self._menu_button("scr", "o", 706, 594, 860, 646, "게임 설정", "O")
        # 커서가 항상 위에 오도록
        c.tag_raise("cursor")

    def _render_title(self):
        c = self.c
        reduced = self.settings.reduced_effects
        u = 0.0 if reduced else self.title_age
        bob = math.sin(u * 1.7) * 5.0
        sway = math.sin(u * 0.9) * 3.0
        glitch = not reduced and 3.76 < u % 3.9
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

        x = max(420, 420 + (u * 180 % 540) - 100)
        right = min(860, 420 + u * 180 % 540)
        c.coords(self.title_line, x, 278, right, 278)
        c.itemconfigure(self.title_line, state="hidden" if reduced else "normal")

    def build_upgrade_screen(self):
        c = self.c
        c.delete("scr")
        c.create_rectangle(0, 0, W, H, fill="#04060e", stipple="gray50",
                           outline="", tags="scr")
        c.create_text(W / 2, 104, text=f"WAVE {self.wave} CLEAR",
                      fill=C_HP, font=F_BIG, tags="scr")
        c.create_text(W / 2, 143,
                      text="소프트웨어 선택: 1 / 2 / 3 · ← → + Enter · 마우스 클릭",
                      fill=C_TEXT, font=F_UI, tags="scr")
        reward = ""
        if self.world.active_sector in SECTOR_NAMES:
            if self.last_wave_purified is not None:
                reward = f" · 이번 웨이브 감염 정화 −{self.last_wave_purified:.1f}%p"
            else:
                sector = SECTORS[self.world.active_sector.upper()]
                reward = (f" · 정화 보상 최대 "
                          f"{sector.waves[self.wave - 1].purify_on_clear:.0f}%p")
        c.create_text(W / 2, 179, text=(
            f"체력 {self.p.hp:.0f}/{self.p.maxhp:.0f}  ·  "
            f"마나 {self.p.mana:.0f}/{self.p.maxmana:.0f}  ·  "
            f"설치 {len(self.levels)}종{reward}"),
            fill=C_PLAYER, font=F_HUD, tags="scr")

        self.cards = []
        self.card_anim = []
        self.card_yoff = []
        self.card_borders = []
        n = len(self.card_opts)
        cw, ch, gap = 300, 330, 36
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
            self._card_glyph(x + cw / 2, y0 + 77, 19, rarity, col)
            c.create_text(x + cw / 2, y0 + 119, text=name, fill=C_TEXT,
                          font=F_BIG, tags="scr")
            c.create_text(x + cw / 2, y0 + 163, text=desc, fill="#a3bbd2",
                          font=F_UI, width=cw - 48, justify="center", tags="scr")
            c.create_rectangle(x + 22, y0 + 201, x + cw - 22, y0 + 274,
                               fill="#101d2d", outline="", tags="scr")
            changes = self._upgrade_comparison(uid)
            first_row_y = 237 if len(changes) == 1 else 222
            for row, (label, change) in enumerate(changes):
                y = y0 + first_row_y + row * 29
                c.create_text(x + 32, y, text=label, anchor="w", fill=C_TEXT,
                              font=F_UI, tags="scr")
                c.create_text(x + cw - 32, y, text=change, anchor="e", fill=col,
                              font=F_MONO, tags="scr")
            level = "즉시 적용" if uid == "bonus" else f"LV {lv} → {lv + 1} / {mx}"
            c.create_text(x + cw / 2, y0 + 301, text=level,
                          fill="#91a9c2", font=F_HUD, tags="scr")
            self.cards.append((x, y0, x + cw, y0 + ch))
            self.card_borders.append(border)
            self.card_anim.append(0.0)
            off = 0.0 if self.settings.reduced_effects else 58.0 + i * 24.0
            self.card_yoff.append(off)
            tag = f"software-card-{i}"
            for item in set(c.find_all()) - before:
                c.addtag_withtag(tag, item)
            c.move(tag, 0, off)
        c.create_text(W / 2, H - 46,
                      text=f"SCORE {self.score:,}   ·   처치 {self.kills}",
                      fill=C_DIM, font=F_UI, tags="scr")
        self._menu_button("scr", "tab", W - 348, H - 65, W - 154, H - 27,
                          "현재 장비", "TAB")

    def _upgrade_comparison(self, uid):
        if uid == "bonus":
            return [("점수", f"{self.score:,} → {self.score + 250:,}")]
        before = self.p
        after = preview_upgrade(before, uid)
        # Actual skill costs and cooldowns are more useful than internal
        # efficiency multipliers. Keep both affected skills inside two rows.
        if uid == "cool":
            return [
                (name, f"{skill_cooldown(base, before.st['skill_cooldown']):.1f}s"
                 f" → {skill_cooldown(base, after.st['skill_cooldown']):.1f}s")
                for name, base in (("충격파 대기", 7.0), ("보호막 대기", 12.0))
            ]
        if uid == "eco":
            return [
                (name, f"{skill_cost(base, before.st['skill_efficiency']):.1f}"
                 f" → {skill_cost(base, after.st['skill_efficiency']):.1f} MP")
                for name, base in (("충격파 소모", 35.0), ("보호막 소모", 25.0))
            ]
        rows = []
        for key, label in UPGRADE_STAT_LABELS.items():
            old, new = before.st[key], after.st[key]
            if old == new:
                continue
            if key in UPGRADE_PERCENT_STATS:
                change = f"{old * 100:.4g}% → {new * 100:.4g}%"
            elif key == "split":
                change = f"{1 + 2 * old} → {1 + 2 * new}"
            else:
                suffix = "초" if key == "slow" else ""
                change = f"{old:.4g} → {new:.4g}{suffix}"
            rows.append((label, change))
        for key, label in (("maxhp", "최대 체력"), ("hp", "현재 체력"),
                           ("maxmana", "최대 마나"), ("mana", "현재 마나"),
                           ("charges", "현재 대시 충전")):
            old, new = getattr(before, key), getattr(after, key)
            if old != new:
                rows.append((label, f"{old:.1f} → {new:.1f}"))
        return rows

    def _animate_upgrade_cards(self):
        c = self.c
        for i, border in enumerate(self.card_borders):
            reduced = self.settings.reduced_effects
            progress = (1.0 if reduced else
                        clamp((self.card_t - i * 0.09) / 0.42, 0.0, 1.0))
            eased = 1.0 - (1.0 - progress) ** 3
            self.card_anim[i] = eased
            hover = i == self.hover_card and progress > 0.5
            offset = (58.0 + i * 24.0) * (1.0 - eased)
            c.move(f"software-card-{i}", 0, offset - self.card_yoff[i])
            self.card_yoff[i] = offset
            c.itemconfigure(border, width=3 if hover else 2,
                            fill="#122237" if hover else "#080e1a",
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
                      text=("SYSTEM HUB · " + self.world.room.upper())
                      if self.state == "hub" else
                      f"WAVE {self.wave}   ·   SCORE {self.score:,}",
                      fill=C_TEXT, font=F_MID, tags="ovl")
        pause_key = self.settings.bindings["pause"]
        self._menu_button("ovl", pause_key, 476, 388, 804, 436,
                          "계속하기", pause_key.upper(), primary=True)
        self._menu_button("ovl", "o", 476, 448, 804, 490, "게임 설정", "O")
        self._menu_button("ovl", "tab", 476, 502, 804, 544, "현재 장비", "TAB")
        if self.state == "play":
            c.create_text(W / 2, 572, text="계속하기를 선택한 뒤 R로 허브에 복귀할 수 있습니다",
                          fill=C_TEXT, font=F_UI, tags="ovl")

    def _draw_loadout(self):
        c = self.c
        layer = "loadoutui"
        c.delete(layer)
        c.create_rectangle(0, 0, W, H, fill="#010610", stipple="gray75",
                           outline="", tags=layer)
        c.create_rectangle(78, 45, W - 78, H - 40, fill="#081522",
                           outline="#31536b", width=2, tags=layer)
        c.create_text(101, 83, text="현재 장비", anchor="w",
                      fill=C_PLAYER, font=F_HUGE, tags=layer)
        c.create_text(W - 101, 89, text=f"설치 {len(self.levels)}종", anchor="e",
                      fill=C_TEXT, font=F_HUD, tags=layer)
        p = self.p
        c.create_text(101, 123, anchor="w", fill=C_TEXT, font=F_UI, tags=layer,
                      text=(f"체력 {p.hp:.0f}/{p.maxhp:.0f}   ·   "
                            f"마나 {p.mana:.0f}/{p.maxmana:.0f}   ·   "
                            f"탄환 피해 {p.st['dmg']:.1f}   ·   "
                            f"초당 발사 {p.st['rate']:.1f}   ·   "
                            f"기본 방어력 {p.st['armor']:.1f}"))
        if self.levels:
            installed = sorted(self.levels, key=lambda uid: (-self.levels[uid], uid))
            page_size = 27
            page_count = max(1, (len(installed) + page_size - 1) // page_size)
            self.loadout_page = min(max(0, getattr(self, "loadout_page", 0)), page_count - 1)
            start = self.loadout_page * page_size
            for index, uid in enumerate(installed[start:start + page_size]):
                row, col = divmod(index, 3)
                x, y = 101 + col * 363, 147 + row * 48
                _, name, description, rarity, cap = UP_BY_ID[uid]
                c.create_rectangle(x, y, x + 352, y + 44, fill="#102333",
                                   outline="", tags=layer)
                c.create_text(x + 12, y + 12, text=name, anchor="w", fill=C_TEXT,
                              font=F_HUD, tags=(layer, "loadout-item"))
                c.create_text(x + 340, y + 12, anchor="e", fill=RARITY_COL[rarity],
                              text=f"LV {self.levels[uid]}/{cap}", font=F_MONO, tags=layer)
                c.create_text(x + 12, y + 30, text=description, anchor="w",
                              fill="#a3bbd2", font=F_MONO, tags=layer)
        else:
            c.create_text(W / 2, 322, text="설치한 소프트웨어가 없습니다",
                          fill=C_TEXT, font=F_MID, tags=layer)
            c.create_text(W / 2, 358, text="웨이브 보상으로 패시브 효과와 능력치를 추가할 수 있습니다.",
                          fill="#a3bbd2", font=F_UI, tags=layer)
        if self.levels and page_count > 1:
            c.create_text(104, H - 69,
                          text=f"{self.loadout_page + 1} / {page_count} 페이지  ·  "
                               "← → / Page Up·Down으로 이동",
                          anchor="w", fill=C_DIM, font=F_UI, tags=layer)
            self._menu_button(layer, "prior", 101, H - 55, 238, H - 20,
                              "이전", "PgUp")
            self._menu_button(layer, "next", 250, H - 55, 387, H - 20,
                              "다음", "PgDn")
        back = "카드 선택으로 돌아가기" if self.state == "upgrade" else "일시정지로 돌아가기"
        self._menu_button(layer, "escape", W - 367, H - 87, W - 101, H - 50,
                          back, "ESC", primary=True)
        c.tag_raise(layer)

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
        self.settings_rule = c.create_line(261, 91, 261, 91, fill=C_PLAYER,
                                           width=2, tags="cfg")
        is_controls = self.settings_page == "controls"
        c.create_text(W / 2, 139,
                      text="KEY BINDINGS" if is_controls else "SETTINGS",
                      fill=C_PLAYER, font=F_HUGE, tags="cfg")
        c.create_text(W / 2, 185,
                      text="↑↓ / WASD 선택 · Home/End 첫/끝 · Enter 키 변경"
                      if is_controls else
                      "↑↓ / WASD 선택 · Home/End 첫/끝 · ←→ 값 변경",
                      fill=C_DIM, font=F_UI, tags="cfg")

        if is_controls:
            items = [
                (ACTION_LABELS[action], self.settings.bindings[action].upper())
                for action in ACTION_ORDER
            ]
            first_y, row_step, row_height = 201, 39, 33
        else:
            prefs = self.settings
            items = [
                ("전체 볼륨", f"{round(prefs.volume * 100)}%"),
                ("음소거", "켜짐" if prefs.muted else "꺼짐"),
                ("연출 줄이기", "켜짐" if prefs.reduced_effects else "꺼짐"),
                ("FPS 표시", "실행 옵션으로 켜짐" if self.opts.fps else
                 "켜짐" if prefs.show_fps else "꺼짐"),
                ("화면 모드", {"windowed": "창 모드",
                             "fullscreen": "전체화면",
                             "borderless": "테두리 없는 창"}[prefs.display_mode]),
                ("창 크기", f"{prefs.window_width} × {prefs.window_height}"
                 if prefs.display_mode == "windowed" else
                 f"{prefs.window_width} × {prefs.window_height} (창 복귀 시)"),
            ]
            first_y, row_step, row_height = 213, 54, 46

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
        self._menu_button("cfg", "tab", 320, 568, 626, 608, next_page, "TAB")
        self._menu_button("cfg", "escape", 654, 568, 960, 608,
                          "돌아가기", "ESC / O")
        hint = ("Enter / 클릭: 키 등록 · Delete: 기본값 복원 · F11: 전체화면"
                if is_controls else
                "F11 전체화면 · Alt+Enter 테두리 없는 창 · 창 모서리 드래그 가능")
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
        self.pool_warning.begin()
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
        self.pool_warning.end()
        self.pool_particle.end()
        self.pool_ebullet.end()
        self.pool_streak.end()
        self.pool_bullet.end()
        self.pool_shard.end()
        self.pool_ftext.end()
        self.pool_line.end()
        self.pool_drone.end()

        if self.state == "hub":
            self._render_hub()
        else:
            # 허브 장면은 전투/터미널 화면에서 반드시 숨겨져야 한다.
            self._hide_hub_scene()
        if self.state in ("play", "upgrade") and self.world.active_sector:
            self._render_sector_gate()
        else:
            # 구역을 벗어나거나 다른 화면으로 넘어가면 출구 문도 숨긴다.
            self._hide_sector_gate()

        if self.state == "play":
            self._update_hud()
        if self.banner_t > 0 and self.state == "play":
            remaining = clamp(self.banner_t / max(self.banner_total, 0.01), 0, 1)
            appeared = (1.0 if self.settings.reduced_effects else
                        clamp((1.0 - remaining) / 0.22, 0, 1))
            eased = 1.0 - (1.0 - appeared) ** 3
            y = H / 2 - 150 - (1.0 - eased) * 35.0
            c.coords(self.banner_item, W / 2, y)
            c.coords(self.banner_item2, W / 2, y + 38)
            c.coords(self.banner_rule, W / 2 - 140 * eased, y + 23,
                     W / 2 + 140 * eased, y + 23)
            c.itemconfigure(self.banner_item, text=self.banner,
                            state="normal", fill="#9fe8ff" if remaining > 0.2 else C_DIM)
            detail = "적을 모두 제거하세요"
            if self.world.active_sector in self.world.sector_clean:
                key = self.settings.bindings["interact"].upper()
                detail = f"왼쪽 출구에서 [{key}]로 OS 허브 복귀"
                if self.sector_clear_heal > 0:
                    detail += f"  ·  체력 +{self.sector_clear_heal:.1f}"
                if self.sector_clear_reward:
                    detail += f"  ·  보스: {self.sector_clear_reward}"
            elif getattr(self, "wave_combat_help", False):
                dash = self.settings.bindings["dash"].upper()
                detail = f"마우스로 조준하면 자동 사격  ·  [{dash}] / Shift로 회피"
            elif self.banner_upgrade:
                detail = self.banner_upgrade
            c.itemconfigure(self.banner_item2, text=detail, state="normal")
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
        if self.paused and self.state in ("play", "hub"):
            self._animate_pause()
        if self.settings_open:
            progress = (1.0 if self.settings.reduced_effects else
                        clamp((self.ui_t - self.settings_opened_at) / 0.28, 0, 1))
            c.coords(self.settings_rule, 261, 91,
                     261 + (W - 522) * (1 - (1 - progress) ** 3), 91)
        # Hover and primary styling share a single source of truth.
        # Canvas width reflects the *current* hover state, not button type.
        # Using it to infer primary buttons leaves secondary buttons highlighted.
        self._update_menu_hover(
            self.mx if self.cursor_on else -1,
            self.my if self.cursor_on else -1,
        )
        self._update_audio_hud()
        self._update_message()
        self._animate_terminals()
        if self.show_fps:
            item = getattr(self, "_fps_item", None)
            if not item:
                self._fps_item = c.create_text(
                    12, H - 10, text="", fill="#2c4a66", font=F_MONO,
                    anchor="sw", tags=("hud-top",))
                item = self._fps_item
            c.itemconfigure(item, text=(
                f"{self.fps:5.1f} fps · render {self.render_ms:4.1f}ms "
                f"p95 {self.render_p95_ms:4.1f}ms · canvas {self.canvas_items:,}"
                f" · RSS {self.memory_mib:.0f} MiB" if self.memory_mib is not None else
                f"{self.fps:5.1f} fps · render {self.render_ms:4.1f}ms · "
                f"p95 {self.render_p95_ms:4.1f}ms · canvas {self.canvas_items:,}"
            ), state="normal")
        elif getattr(self, "_fps_item", None):
            c.itemconfigure(self._fps_item, state="hidden")
        self._render_screen_fx()
        # 화면 연출보다 위, 메뉴/터미널보다 아래에 놓이는 항상 최상단 HUD.
        c.tag_raise("hud-top")
        c.tag_raise("ovl")
        c.tag_raise("cfg")
        c.tag_raise("loadoutui")
        c.tag_raise("dbui")
        c.tag_raise("profileui")
        c.tag_raise("extractui")
        c.tag_raise("salvageui")
        c.tag_raise("resumeui")
        c.tag_raise("term-fx")
        c.tag_raise(self.message_bg)
        c.tag_raise(self.message_txt)
        self.draw_cursor()
        c.tag_raise("cursor")
        c.raise_letterbox()

    # ----------------------------------------------------------- 인월드 허브
    HUB_GRID_STEP = 120
    HUB_MAX_LINKS = 8
    HUB_DUST = 34
    HUB_TRAIL = 10

    def _build_hub_scene(self):
        """Allocate every hub canvas item exactly once.

        The hub is the main play surface, so it is rebuilt only when the room
        changes. Per-frame work is limited to moving, resizing and recolouring
        these items, which keeps the frame budget free for animation and
        guarantees that HUD overlays can never be buried by world geometry.
        """
        c = self.c
        self._hub_items = []
        self._hub_room = None

        self.hub_bg = c.create_rectangle(0, 0, W, H, fill="#071322",
                                         outline="", tags="hub-world")
        self._hub_items.append(self.hub_bg)

        # World grid: a fixed pool that slides with the camera.
        self.hub_grid_v = []
        for _ in range(W // self.HUB_GRID_STEP + 3):
            item = c.create_line(0, 0, 0, H, fill="#153043", tags="hub-world")
            self.hub_grid_v.append(item)
            self._hub_items.append(item)
        self.hub_grid_h = []
        for _ in range(H // self.HUB_GRID_STEP + 3):
            item = c.create_line(0, 0, W, 0, fill="#153043", tags="hub-world")
            self.hub_grid_h.append(item)
            self._hub_items.append(item)

        # Conduits from the central core to each node.
        self.hub_links = []
        for _ in range(self.HUB_MAX_LINKS):
            conduit = c.create_line(0, 0, 0, 0, fill="#19384b", width=12,
                                    tags="hub-world")
            data = c.create_line(0, 0, 0, 0, fill="#19485d", width=2,
                                 dash=(12, 13), tags="hub-world")
            pulse = c.create_oval(0, 0, 0, 0, fill=C_PLAYER, outline="",
                                  state="hidden", tags="hub-world")
            self.hub_links.append((conduit, data, pulse))
            self._hub_items.extend((conduit, data, pulse))

        # Central core: two rings, a rotating sweep and a breathing light.
        self.hub_core_outer = c.create_oval(0, 0, 0, 0, outline="#1a5260",
                                            width=4, tags="hub-world")
        self.hub_core_inner = c.create_oval(0, 0, 0, 0, outline="#2a7a8b",
                                            width=2, tags="hub-world")
        self.hub_core_sweep = c.create_arc(0, 0, 0, 0, start=0, extent=54,
                                          style="arc", outline="#2f7f92",
                                          width=2, tags="hub-world")
        self.hub_core_light = c.create_oval(0, 0, 0, 0, fill="#7ec9db",
                                            outline="", tags="hub-world")
        self.hub_core_label = c.create_text(0, 0, text="", fill="#7ec9db",
                                            font=F_MID, tags="hub-world")
        self._hub_items.extend((self.hub_core_outer, self.hub_core_inner,
                                self.hub_core_sweep, self.hub_core_light,
                                self.hub_core_label))

        # One decoration slot per node id, reused between rooms.
        self.hub_nodes = {}
        for node in BASE_NODES + OS_NODES:
            ring = c.create_oval(0, 0, 0, 0, fill="#102c37", outline="#65e9fc",
                                 width=2, tags="hub-world")
            halo = c.create_oval(0, 0, 0, 0, outline="#34718b", width=2,
                                 state="hidden", tags="hub-world")
            pulse = c.create_oval(0, 0, 0, 0, outline="#65e9fc", width=2,
                                  state="hidden", tags="hub-world")
            sweep = c.create_arc(0, 0, 0, 0, start=0, extent=40, style="arc",
                                 outline="#3d9fb4", width=2, state="hidden",
                                 tags="hub-world")
            light = c.create_oval(0, 0, 0, 0, fill="#65e9fc", outline="",
                                  tags="hub-world")
            label = c.create_text(0, 0, text="", fill="#65e9fc", font=F_HUD,
                                  tags="hub-world")
            caption = c.create_text(0, 0, text="", fill="#779ab3", font=F_MONO,
                                    tags="hub-world")
            self.hub_nodes[node.id] = {
                "ring": ring, "halo": halo, "pulse": pulse, "sweep": sweep,
                "light": light, "label": label, "caption": caption,
            }
            self._hub_items.extend((ring, halo, pulse, sweep, light, label,
                                    caption))

        # Environmental pillars (matching the hub collision rectangles).
        self.hub_pillars = []
        for _ in PILLARS:
            item = c.create_rectangle(0, 0, 0, 0, fill="#15384c",
                                      outline="#3f7185", width=2,
                                      tags="hub-world")
            self.hub_pillars.append(item)
            self._hub_items.append(item)

        # Player avatar: hull, aura and core light.
        self.hub_player_ring = c.create_oval(0, 0, 0, 0, outline="#237a92",
                                             width=2, tags="hub-world")
        self.hub_player_body = c.create_polygon(0, 0, 0, 0, 0, 0, 0, 0,
                                                fill="#70f0ff",
                                                outline="#d9ffff", width=2,
                                                tags="hub-world")
        self.hub_player_core = c.create_oval(0, 0, 0, 0, fill="#f6ffff",
                                             outline="", tags="hub-world")
        self._hub_items.extend((self.hub_player_ring, self.hub_player_body,
                                self.hub_player_core))

        # Motion trail behind the player while moving.
        self.hub_trail = []
        for _ in range(self.HUB_TRAIL):
            item = c.create_oval(0, 0, 0, 0, fill="#2c8ea3", outline="",
                                 state="hidden", tags="hub-world")
            self.hub_trail.append(item)
            self._hub_items.append(item)

        # Ambient dust with camera parallax.
        self.hub_dust = []
        for index in range(self.HUB_DUST):
            item = c.create_oval(0, 0, 0, 0, fill="#1d3d52", outline="",
                                 state="hidden", tags="hub-world")
            self.hub_dust.append({
                "item": item,
                "x": rnd(0, W), "y": rnd(0, H),
                "r": rnd(0.6, 2.0),
                "speed": rnd(4.0, 14.0),
                "phase": rnd(0.0, TAU),
                "depth": rnd(0.25, 1.0),
                "index": index,
            })
            self._hub_items.append(item)

        # Info panel, interaction prompt and transient notices.
        self.hub_panel = c.create_rectangle(12, 12, 493, 75, fill="#061322",
                                            outline="#284c5c",
                                            tags="hub-world")
        self.hub_panel_title = c.create_text(28, 29, anchor="w", text="",
                                            fill="#72ecf8", font=F_MID,
                                            tags="hub-world")
        self.hub_panel_detail = c.create_text(28, 56, anchor="w", text="",
                                              fill="#98b8ca", font=F_UI,
                                              tags="hub-world")
        self.hub_hint = c.create_text(W - 17, 27, anchor="e", text="",
                                      fill="#6c9cb0", font=F_UI,
                                      tags="hub-world")
        self.hub_prompt_bg = c.create_rectangle(W / 2 - 270, H - 95,
                                                W / 2 + 270, H - 49,
                                                fill="#0a2736", outline="#397b93",
                                                state="hidden", tags="hub-world")
        self.hub_prompt = c.create_text(W / 2, H - 72, text="", fill="#c6f8ff",
                                        font=F_UI, state="hidden",
                                        tags="hub-world")
        self.hub_prompt_node = None
        self.hub_prompt_at = 0.0
        self.hub_notice = c.create_text(W / 2, H - 23, text="", fill="#ffd37c",
                                        font=F_UI, state="hidden",
                                        tags="hub-world")
        self._hub_items.extend((self.hub_panel, self.hub_panel_title,
                                self.hub_panel_detail, self.hub_hint,
                                self.hub_prompt_bg, self.hub_prompt,
                                self.hub_notice))

        # Extraction celebration overlay.
        self.hub_extract_ring = c.create_oval(0, 0, 0, 0, outline="#56e7bd",
                                              width=3, state="hidden",
                                              tags=("hub-world", "extraction-vfx"))
        self.hub_extract_panel = c.create_rectangle(
            12, 88, 493, 180, fill="#081c23", outline="#397f71",
            state="hidden", tags="hub-world")
        self.hub_extract_title = c.create_text(28, 111, text="철수 완료",
                                               anchor="w", fill="#caffea", font=F_MID,
                                               state="hidden",
                                               tags=("hub-world", "extraction-vfx"))
        self.hub_extract_sub = c.create_text(28, 149, text="",
                                             anchor="w", fill="#a9e0cf", font=F_UI,
                                             state="hidden",
                                             tags=("hub-world", "extraction-vfx"))
        self._hub_items.extend((self.hub_extract_ring, self.hub_extract_panel,
                                self.hub_extract_title,
                                self.hub_extract_sub))

        self._hub_visible = True
        self._hide_hub_scene()

    def _hide_hub_scene(self):
        """Collapse the whole hub scene to a single state change."""
        if not getattr(self, "_hub_visible", False):
            return
        for item in self._hub_items:
            self.c.itemconfigure(item, state="hidden")
        self._hub_visible = False
        self.hub_prompt_node = None

    def _render_hub(self):
        """Animate the scrolling 2400x1560 in-world hub.

        Nothing is created or deleted here: the scene was allocated once by
        `_build_hub_scene`, so every frame only repositions and recolours the
        existing items.
        """
        c = self.c
        world = self.world
        p = self.p
        reduced = self.settings.reduced_effects
        u = 0.0 if reduced else self.ui_t
        cx, cy = camera_for(p.x, p.y, W, H)
        base = world.room == "base"

        if self._hub_room != world.room:
            self._hub_room = world.room
        for item in self._hub_items:
            c.itemconfigure(item, state="normal")
        self._hub_visible = True

        # --- 배경 / 그리드 -------------------------------------------------
        c.itemconfigure(self.hub_bg,
                        fill="#071322" if base else "#090f20")
        grid_col = "#153043" if base else "#27243e"
        step = self.HUB_GRID_STEP
        first_x = int(cx // step) * step
        index = 0
        for wx in range(first_x, int(cx + W) + step, step):
            if index >= len(self.hub_grid_v):
                break
            item = self.hub_grid_v[index]
            x = wx - cx
            c.coords(item, x, 0, x, H)
            c.itemconfigure(item, fill=grid_col)
            index += 1
        for item in self.hub_grid_v[index:]:
            c.itemconfigure(item, state="hidden")
        first_y = int(cy // step) * step
        index = 0
        for wy in range(first_y, int(cy + H) + step, step):
            if index >= len(self.hub_grid_h):
                break
            item = self.hub_grid_h[index]
            y = wy - cy
            c.coords(item, 0, y, W, y)
            c.itemconfigure(item, fill=grid_col)
            index += 1
        for item in self.hub_grid_h[index:]:
            c.itemconfigure(item, state="hidden")

        # --- 중앙 코어 -----------------------------------------------------
        # 백신 허브에서는 코어 자체가 중앙 랜드마크지만, OS 허브에서는 같은
        # 자리에 SYSTEM STATUS 단말기가 있으므로 코어는 옅은 접합 반경으로만
        # 그려 두 시설이 겹쳐 읽히지 않게 한다.
        hx, hy = START[0] - cx, START[1] - cy
        breathe = 1.0 + (0.0 if reduced else 0.045 * math.sin(u * 1.9))
        if base:
            core_r = 72.0 * breathe
            c.itemconfigure(self.hub_core_outer, state="normal",
                            outline="#1a5260", width=4)
            c.itemconfigure(self.hub_core_inner, state="normal",
                            outline="#2a7a8b", width=2)
            c.itemconfigure(self.hub_core_sweep, state="normal",
                            outline="#2f7f92")
            c.itemconfigure(self.hub_core_light, state="normal", fill="#7ec9db")
            c.itemconfigure(self.hub_core_label, state="normal",
                            text="ANTIVIRUS CORE")
            inner_r = 47.0 * (2.0 - breathe)
            c.coords(self.hub_core_inner, hx - inner_r, hy - inner_r,
                     hx + inner_r, hy + inner_r)
            c.coords(self.hub_core_label, hx, hy - 96)
        else:
            core_r = 132.0 * breathe
            c.itemconfigure(self.hub_core_outer, state="normal",
                            outline="#16233a", width=2)
            c.itemconfigure(self.hub_core_inner, state="hidden")
            c.itemconfigure(self.hub_core_sweep, state="normal",
                            outline="#1f2c46")
            c.itemconfigure(self.hub_core_light, state="hidden")
            c.itemconfigure(self.hub_core_label, state="hidden")
        c.coords(self.hub_core_outer, hx - core_r, hy - core_r,
                 hx + core_r, hy + core_r)
        if not reduced:
            c.itemconfigure(self.hub_core_sweep, start=(u * 52.0) % 360.0)
            c.coords(self.hub_core_sweep, hx - core_r, hy - core_r,
                     hx + core_r, hy + core_r)
        if base:
            light_r = 7.0 + (0.0 if reduced else 2.4 * math.sin(u * 2.6))
            c.coords(self.hub_core_light, hx - light_r, hy - light_r,
                     hx + light_r, hy + light_r)

        # --- 노드와 통로 ---------------------------------------------------
        near = world.nearest(p.x, p.y)
        nodes = world.nodes()
        # 이전 방의 노드는 숨겨야 두 방의 단말기가 동시에 보이지 않는다.
        active_ids = {node.id for node in nodes}
        for node_id, slot in self.hub_nodes.items():
            if node_id in active_ids:
                continue
            for key in ("ring", "halo", "pulse", "sweep", "light", "label",
                        "caption"):
                c.itemconfigure(slot[key], state="hidden")
        for link_index, (conduit, data, pulse) in enumerate(self.hub_links):
            if link_index >= len(nodes):
                for item in (conduit, data, pulse):
                    c.itemconfigure(item, state="hidden")
                continue
            node = nodes[link_index]
            sx, sy = node.x - cx, node.y - cy
            c.itemconfigure(conduit, state="normal",
                            fill="#19384b" if base else "#29314c")
            c.coords(conduit, hx, hy, sx, sy)
            c.itemconfigure(data, state="normal",
                            fill="#19485d" if base else "#40415d")
            c.coords(data, hx, hy, sx, sy)
            # 데이터 펄스가 코어에서 노드로 흐른다.
            if reduced:
                c.itemconfigure(pulse, state="hidden")
            else:
                travel = (u * 0.42 + link_index * 0.23) % 1.0
                px = hx + (sx - hx) * travel
                py = hy + (sy - hy) * travel
                pr = 4.2 + 1.6 * math.sin(u * 6.0 + link_index)
                c.coords(pulse, px - pr, py - pr, px + pr, py + pr)
                c.itemconfigure(pulse, state="normal",
                                fill="#8ff6ff" if base else "#c3a3ff")

        for node in nodes:
            slot = self.hub_nodes[node.id]
            sx, sy = node.x - cx, node.y - cy
            visible = -170 < sx < W + 170 and -170 < sy < H + 170
            if not visible:
                for key in ("ring", "halo", "pulse", "sweep", "light", "label",
                            "caption"):
                    c.itemconfigure(slot[key], state="hidden")
                continue
            available = (base or node.id not in SECTOR_NAMES
                         or node.id in AVAILABLE_SECTORS)
            cleaned = node.id in world.sector_clean
            is_sector = node.id in SECTOR_NAMES
            color = ("#42d7aa" if cleaned else
                     "#65e9fc" if available else "#62647e")
            highlight = node == near
            # 사용 가능한 노드는 천천히 숨쉬듯 크기가 변한다.
            wave = 0.0 if reduced else math.sin(u * 2.2 + node.x * 0.01)
            radius = (43.0 if highlight else 38.0) + wave * (2.6 if available else 0.0)
            c.itemconfigure(slot["ring"], state="normal",
                            fill="#102c37" if available else "#171727",
                            outline=color, width=3 if highlight else 2)
            c.coords(slot["ring"], sx - radius, sy - radius,
                     sx + radius, sy + radius)
            # 레이더 스윕: 구역 게이트는 회전하는 탐지 스캔을 그린다.
            if is_sector and available and not reduced:
                c.itemconfigure(slot["sweep"], state="normal", outline=color)
                c.coords(slot["sweep"], sx - radius - 7, sy - radius - 7,
                         sx + radius + 7, sy + radius + 7)
                c.itemconfigure(slot["sweep"], start=(u * 118.0) % 360.0)
            else:
                c.itemconfigure(slot["sweep"], state="hidden")
            # 근접 하이라이트 링은 바깥으로 퍼져 사라진다.
            if highlight:
                grow = (u * 0.85) % 1.0
                hr = radius + 9.0 + grow * 17.0
                c.itemconfigure(slot["halo"], state="normal",
                                outline="#34718b")
                c.coords(slot["halo"], sx - hr, sy - hr, sx + hr, sy + hr)
            else:
                c.itemconfigure(slot["halo"], state="hidden")
            c.itemconfigure(slot["pulse"], state="hidden")
            glow = 5.0 + (2.2 if highlight else 0.0) + (0.0 if reduced else wave * 1.2)
            c.coords(slot["light"], sx - glow, sy - glow, sx + glow, sy + glow)
            c.itemconfigure(slot["light"], fill=color)
            c.coords(slot["label"], sx, sy - 64)
            c.itemconfigure(slot["label"], text=node.label, fill=color)
            if cleaned:
                caption = "CLEAN"
            elif is_sector:
                danger = SECTORS[node.id.upper()].danger
                caption = f"위험 {danger}/5  ·  웨이브 {world.sector_waves[node.id]}/5"
            else:
                caption = (f"CHARGES {self.purify_uses}" if node.id == "purify"
                           else "ACTIVE TERMINAL")
            c.coords(slot["caption"], sx, sy + 64)
            c.itemconfigure(slot["caption"], text=caption,
                            fill="#779ab3" if available else "#6b6b85")

        # --- 구조물 --------------------------------------------------------
        for index, (x0, y0, x1, y1) in enumerate(PILLARS):
            if index >= len(self.hub_pillars):
                break
            item = self.hub_pillars[index]
            c.itemconfigure(item, state="normal")
            c.coords(item, x0 - cx, y0 - cy, x1 - cx, y1 - cy)

        # --- 플레이어 ------------------------------------------------------
        px, py = p.x - cx, p.y - cy
        pulse = math.sin(u * 2.1)
        bob = 0.0 if reduced else 2.4 * math.sin(u * 3.1)
        body = self._arrow(px, py, 19.0 + 1.3 * pulse, p.aim)
        c.coords(self.hub_player_body, *[value for pt in body for value in pt])
        aura = 29.0 + (0.0 if reduced else 2.2 * math.sin(u * 3.4))
        c.coords(self.hub_player_ring, px - aura, py - aura,
                 px + aura, py + aura)
        c.itemconfigure(self.hub_player_ring,
                        outline="#2f93ad" if reduced else "#237a92")
        c.coords(self.hub_player_core, px - 4, py - 4 + bob, px + 4, py + 4 + bob)
        # 이동 잔상
        speed = math.hypot(p.vx, p.vy)
        if reduced or speed < 40.0:
            for item in self.hub_trail:
                c.itemconfigure(item, state="hidden")
        else:
            for index, item in enumerate(self.hub_trail):
                lag = (index + 1) * 0.035
                tx = px - p.vx * lag
                ty = py - p.vy * lag
                tr = 6.0 + 4.0 * (1.0 - index / len(self.hub_trail))
                c.coords(item, tx - tr, ty - tr, tx + tr, ty + tr)
                c.itemconfigure(item, state="normal",
                                fill="#2c8ea3" if index < 3 else "#1d5f70")

        # --- 먼지 입자 -----------------------------------------------------
        for mote in self.hub_dust:
            item = mote["item"]
            drift = 0.0 if reduced else u * mote["speed"]
            mx = (mote["x"] - cx * 0.06 * mote["depth"] - drift * 0.35) % (W + 40) - 20
            my = (mote["y"] - cy * 0.06 * mote["depth"] - drift) % (H + 40) - 20
            twinkle = 0.55 + 0.45 * math.sin(u * 1.6 + mote["phase"])
            r = mote["r"] * (0.7 + 0.3 * twinkle)
            c.coords(item, mx - r, my - r, mx + r, my + r)
            c.itemconfigure(item, state="normal",
                            fill="#24506b" if twinkle > 0.6 else "#17364a")

        # --- 정보 패널 -----------------------------------------------------
        c.itemconfigure(self.hub_panel_title,
                        text="SYSTEM HUB" if base else
                        ("OPERATING SYSTEM  /  CLEAN" if world.os_clean else
                         "OPERATING SYSTEM  /  INFECTED"))
        if base:
            detail = (f"백신 Lv.{self.database.antivirus_level} · "
                      f"XP {self.database.xp:,} · "
                      f"영구 소프트웨어 {len(self.database.software_archive)}종")
        else:
            detail = (f"정화 {len(world.sector_clean)}/4 · "
                      f"감염 {self.infection.infection:.0f}% · "
                      f"OS 무결성 {self.infection.os_integrity:.0f}%")
        c.itemconfigure(self.hub_panel_detail, text=detail)
        bindings = self.settings.bindings
        movement = "/".join(bindings[action].upper()
                            for action in ("up", "left", "down", "right"))
        c.itemconfigure(self.hub_hint, text=f"이동 {movement} 또는 방향키  ·  "
                        f"대시 {bindings['dash'].upper()} / Shift  ·  설정 O")

        # --- 상호작용 프롬프트 ---------------------------------------------
        action_key = bindings["interact"].upper()
        if near != self.hub_prompt_node:
            self.hub_prompt_node = near
            self.hub_prompt_at = self.ui_t
        route_hint = ("자유 진입 · 게이트별 위험과 소프트웨어 경향을 비교하세요"
                      if not base and not world.os_clean else "")
        if near:
            prompt = f"[{action_key}]  {near.label}  ·  상호작용"
            if near.id in SECTOR_NAMES:
                spec = SECTORS[near.id.upper()]
                if near.id in world.sector_clean:
                    prompt = f"[{action_key}] {near.label} · 정화 완료"
                else:
                    module = UP_BY_ID[BOSS_SIGNATURE_SOFTWARE[near.id.upper()][0]][1]
                    prompt = (f"[{action_key}] {near.label} · 위험 {spec.danger}/5"
                              f"\n{spec.tactics_hint} · 카드 {spec.reward_hint}↑ · 보스 {module}")
            elif near.id == "status":
                if world.os_clean:
                    prompt = f"[{action_key}]  OS 철수 및 소프트웨어 보관"
                elif route_hint:
                    prompt = route_hint
            elif near.id == "purify":
                infection, repair = self.purification_preview()
                if not self.purify_uses:
                    prompt = "정화 충전을 모두 사용했습니다"
                elif infection or repair:
                    after = self.infection.preview_purification(infection, repair)
                    stage = ("정상", "감지", "확산", "위기")[after.stage]
                    pressure = after.integrity_pressure_per_second()
                    prompt = (f"[{action_key}] 정화 · 감염 −{infection:.1f}%p · "
                              f"OS +{repair:.1f}%p"
                              f"\n정화 후 {after.infection:.0f}% ({stage}) · "
                              f"OS {after.os_integrity:.0f}% · "
                              f"지속 감소 {pressure:.1f}%/s · "
                              f"남은 {self.purify_uses - 1}회")
                else:
                    prompt = "정화 불필요  ·  감염 없음 / OS 무결성 정상"
            elif near.id == "diagnostics":
                prompt = f"[{action_key}]  전투 기록 및 누적 통계"
            progress = (1.0 if reduced else
                        clamp((self.ui_t - self.hub_prompt_at) / 0.2, 0, 1))
            offset = 12 * (1 - progress) ** 3
            expanded = "\n" in prompt
            c.coords(self.hub_prompt_bg, W / 2 - 320 if expanded else W / 2 - 270,
                     H - (114 if expanded else 95) + offset,
                     W / 2 + 320 if expanded else W / 2 + 270,
                     H - (39 if expanded else 49) + offset)
            c.coords(self.hub_prompt, W / 2,
                     H - (77 if expanded else 72) + offset)
            c.itemconfigure(self.hub_prompt_bg, state="normal", outline="#4a90a8")
            c.itemconfigure(self.hub_prompt, state="normal", text=prompt, fill="#c6f8ff")
        else:
            c.itemconfigure(self.hub_prompt_bg, state="hidden")
            objective = ("배포 단말기에서 백신을 배포하세요" if base else
                         "철수 단말기에서 소프트웨어를 영구 보관하세요" if world.os_clean else
                         route_hint or "구역 게이트로 이동해 감염원을 정화하세요")
            c.coords(self.hub_prompt, W / 2, H - 72)
            c.itemconfigure(self.hub_prompt, state="normal", text=objective, fill=C_TEXT)

        # --- 알림 ----------------------------------------------------------
        if self.world_notice_t > 0:
            c.itemconfigure(self.hub_notice, state="normal",
                            text=self.world_notice)
            fade = clamp(self.world_notice_t / 0.6, 0.0, 1.0)
            c.itemconfigure(self.hub_notice,
                            fill="#ffd37c" if fade > 0.35 else "#8a7448")
        else:
            c.itemconfigure(self.hub_notice, state="hidden")

        # --- 철수 연출 ------------------------------------------------------
        if base and self.extraction_fx_t > 0:
            growth = 1.0 - min(1.0, self.extraction_fx_t / 2.5)
            radius = 30 + 210 * growth
            c.itemconfigure(self.hub_extract_ring, state="hidden" if reduced else "normal")
            c.coords(self.hub_extract_ring, px - radius, py - radius,
                     px + radius, py + radius)
        else:
            c.itemconfigure(self.hub_extract_ring, state="hidden")
        summary_state = "normal" if base and self.extraction_summary else "hidden"
        c.itemconfigure(self.hub_extract_panel, state=summary_state)
        c.itemconfigure(self.hub_extract_title, state=summary_state)
        c.itemconfigure(self.hub_extract_sub, state=summary_state,
                        text=self.extraction_summary)


    def _build_sector_gate(self):
        """Allocate the in-sector exit door once; frames only animate it."""
        c = self.c
        self._gate_items = []
        self.gate_frame = c.create_rectangle(24, 0, 70, 0, fill="#0a2531",
                                             outline="#327e8b", width=3,
                                             tags="sector-gate")
        self.gate_bar = c.create_line(48, 0, 48, 0, fill="#327e8b", width=3,
                                      tags="sector-gate")
        self.gate_scan = c.create_line(30, 0, 64, 0, fill="#9ff5e0", width=2,
                                       state="hidden", tags="sector-gate")
        self.gate_glow = c.create_oval(16, 0, 78, 0, outline="#78f0cf",
                                       width=2, state="hidden",
                                       tags="sector-gate")
        self.gate_label = c.create_text(84, 0, anchor="w", text="",
                                        fill="#327e8b", font=F_HUD,
                                        tags="sector-gate")
        self.gate_prompt = c.create_text(84, 0, anchor="w", text="",
                                          fill="#d4fbff", font=F_UI,
                                          state="hidden", tags="sector-gate")
        self.gate_warning_bg = c.create_rectangle(W / 2 - 310, H - 176,
                                                  W / 2 + 310, H - 133,
                                                  fill="#32121c",
                                                  outline="#ff7082", width=2,
                                                  state="hidden",
                                                  tags=("sector-gate",
                                                        "db-close-warning"))
        self.gate_warning = c.create_text(W / 2, H - 155,
                                           text="런 종료 기록 저장 실패 · "
                                                "R을 다시 눌러 저장 재시도",
                                           fill="#ffe3e4", font=F_UI,
                                           state="hidden",
                                           tags=("sector-gate",
                                                 "db-close-warning"))
        self._gate_items.extend((self.gate_frame, self.gate_bar,
                                 self.gate_scan, self.gate_glow,
                                 self.gate_label, self.gate_prompt,
                                 self.gate_warning_bg, self.gate_warning))
        self._gate_visible = True
        self._hide_sector_gate()

    def _hide_sector_gate(self):
        if not getattr(self, "_gate_visible", False):
            return
        for item in self._gate_items:
            self.c.itemconfigure(item, state="hidden")
        self._gate_visible = False

    def _render_sector_gate(self):
        """Sector exits require deliberate movement and interaction."""
        c = self.c
        if self.world.active_sector not in SECTOR_NAMES:
            self._hide_sector_gate()
            return
        u = self.ui_t
        reduced = self.settings.reduced_effects
        x, y = CPU_EXIT
        near = math.hypot(self.p.x - x, self.p.y - y) < 112.0
        tone = "#78f0cf" if near else "#327e8b"

        c.itemconfigure(self.gate_frame, state="normal", outline=tone,
                        fill="#0d2f3d" if near else "#0a2531")
        c.coords(self.gate_frame, 24, y - 70, 70, y + 70)
        c.itemconfigure(self.gate_bar, state="normal", fill=tone)
        c.coords(self.gate_bar, 47, y - 50, 47, y + 50)
        # 문을 따라 내려오는 스캔 광선
        if reduced:
            c.itemconfigure(self.gate_scan, state="hidden")
            c.itemconfigure(self.gate_glow, state="hidden")
        else:
            scan = ((u * 0.55) % 1.0)
            sy = y - 58 + scan * 116.0
            c.coords(self.gate_scan, 30, sy, 64, sy)
            c.itemconfigure(self.gate_scan, state="normal",
                            fill="#9ff5e0" if near else "#3f8b96")
            if near:
                grow = (u * 1.1) % 1.0
                gr = 34.0 + grow * 26.0
                c.coords(self.gate_glow, 47 - gr, y - gr, 47 + gr, y + gr)
                c.itemconfigure(self.gate_glow, state="normal",
                                outline="#78f0cf")
            else:
                c.itemconfigure(self.gate_glow, state="hidden")

        c.coords(self.gate_label, 84, y - 10)
        c.itemconfigure(self.gate_label, state="normal", fill=tone,
                        text=f"{SECTOR_NAMES[self.world.active_sector]} · EXIT")
        if near:
            c.coords(self.gate_prompt, 84, y + 14)
            c.itemconfigure(self.gate_prompt, state="normal",
                            text=(f"[{self.settings.bindings['interact'].upper()}] 복귀 · " +
                                  ("정화 완료" if self.world.active_sector in self.world.sector_clean
                                   else "현재 웨이브 재시작 / 확정 보상 유지")))
        else:
            c.itemconfigure(self.gate_prompt, state="hidden")

        if self.database_close_error:
            c.itemconfigure(self.gate_warning_bg, state="normal")
            c.itemconfigure(self.gate_warning, state="normal")
        else:
            c.itemconfigure(self.gate_warning_bg, state="hidden")
            c.itemconfigure(self.gate_warning, state="hidden")
        self._gate_visible = True

    # ------------------------------------------------- 인월드 터미널 공통 연출
    def _build_terminal_fx(self):
        """Persistent CRT scan band and selection marker for the terminals.

        The terminal bodies are rebuilt whenever the selection changes, so the
        animated layer lives in its own items that are never recreated.
        """
        c = self.c
        self.term_scan = c.create_rectangle(0, 0, W, 0, fill=C_PLAYER,
                                            outline="", stipple="gray12",
                                            state="hidden", tags=("term-fx",))
        self.term_marker = c.create_rectangle(0, 0, 0, 0, fill=C_PLAYER,
                                              outline="", state="hidden",
                                              tags=("term-fx",))
        self.term_marker_glow = c.create_rectangle(0, 0, 0, 0, fill="",
                                                   outline=C_PLAYER, width=2,
                                                   state="hidden",
                                                   tags=("term-fx",))

    def _animate_terminals(self):
        """Sweep a CRT band and pulse the selection marker on open terminals."""
        c = self.c
        if self.database_open:
            active = "db"
        elif self.extraction_open:
            active = "extract"
        elif self.salvage_open:
            active = "salvage"
        else:
            active = None
        if active is None:
            for item in (self.term_scan, self.term_marker,
                         self.term_marker_glow):
                c.itemconfigure(item, state="hidden")
            return
        # 패널 위를 천천히 지나가는 CRT 스캔 밴드
        reduced = self.settings.reduced_effects
        y = (self.ui_t * 52.0) % (H + 140.0) - 70.0
        c.coords(self.term_scan, 0, y, W, y + 26)
        c.itemconfigure(self.term_scan, state="hidden" if reduced else "normal")
        box = self.term_marker_boxes.get(active)
        if not box:
            c.itemconfigure(self.term_marker, state="hidden")
            c.itemconfigure(self.term_marker_glow, state="hidden")
            return
        x0, y0, x1, y1 = box
        sway = 0.0 if reduced else math.sin(self.ui_t * 5.0) * 2.0
        c.coords(self.term_marker, x0 + sway, y0, x0 + sway + 5, y1)
        c.itemconfigure(self.term_marker, state="normal")
        c.coords(self.term_marker_glow, x0 + sway - 3, y0 - 2,
                 x0 + sway + 8, y1 + 2)
        c.itemconfigure(self.term_marker_glow, state="normal",
                        outline="#ffffff" if not reduced and math.sin(self.ui_t * 5.0) > 0.6
                        else C_PLAYER)

    # -------------------------------------------------------- 인월드 단말기
    def _draw_profile_terminal(self):
        c = self.c
        layer = "profileui"
        c.delete(layer)
        c.create_rectangle(0, 0, W, H, fill="#010610", stipple="gray75",
                           outline="", tags=layer)
        c.create_rectangle(174, 58, W - 174, H - 58, fill="#081522",
                           outline="#31536b", width=2, tags=layer)
        c.create_text(214, 107, text="전투 기록", anchor="w",
                      fill=C_PLAYER, font=F_HUGE, tags=layer)
        c.create_text(214, 145, text="최근 도전 결과와 누적 통계", anchor="w",
                      fill="#a3bbd2", font=F_UI, tags=layer)

        profile = self.profile
        c.create_rectangle(208, 170, W - 208, 232, fill="#102333",
                           outline="", tags=layer)
        c.create_text(225, 189, anchor="w", fill=C_TEXT, font=F_HUD, tags=layer,
                      text=(f"최고 점수 {self.save['best_score']:,}   ·   "
                            f"누적 처치 {profile.total_kills:,}   ·   "
                            f"누적 보스 {profile.boss_kills:,}   ·   "
                            f"최고 콤보 {profile.best_combo:,}"))
        c.create_text(225, 214, anchor="w", fill="#a3bbd2", font=F_UI, tags=layer,
                      text=(f"철수 성공 {profile.extracted_runs:,}   ·   "
                            f"전투 불능 {profile.completed_runs:,}   ·   "
                            f"미완료 {profile.abandoned_runs:,}"))

        columns = (("결과", 225), ("점수", 500), ("종료 웨이브", 620),
                   ("처치", 708), ("보스", 796), ("최고 콤보", 914), ("경과 시간", 1050))
        start = self.profile_page * self.PROFILE_PAGE_SIZE
        runs = profile.recent_runs[start:start + self.PROFILE_PAGE_SIZE]
        if runs:
            for i, (label, x) in enumerate(columns):
                c.create_text(x, 257, text=label, anchor="e" if i else "w",
                              fill="#a3bbd2", font=F_UI, tags=layer)
            results = {"extracted": ("철수 성공", C_HP), "dead": ("전투 불능", C_BAD),
                       "abandoned": ("미완료", "#a3bbd2")}
            for row, run in enumerate(runs):
                y = 290 + row * 39
                c.create_rectangle(208, y - 17, W - 208, y + 17,
                                   fill="#0e2031" if row % 2 == 0 else "#0a1a29",
                                   outline="", tags=layer)
                status, status_color = results[run.status]
                minutes, seconds = divmod(int(run.duration_seconds), 60)
                values = (status, f"{run.score:,}", str(run.wave), str(run.kills),
                          str(run.boss_kills), str(run.best_combo), f"{minutes:02d}:{seconds:02d}")
                for i, (value, (_, x)) in enumerate(zip(values, columns)):
                    c.create_text(x, y, text=value, anchor="e" if i else "w",
                                  fill=C_TEXT if i else status_color,
                                  font=F_NUM if i else F_HUD,
                                  tags=(layer, "profile-row"))
            pages = self._profile_page_count()
            c.create_text(550, 607, text=(f"최근 {len(profile.recent_runs)}회  ·  "
                                         f"{self.profile_page + 1}/{pages}"),
                          fill=C_TEXT, font=F_UI, tags=layer)
            if self.profile_page:
                self._menu_button(layer, "left", 208, 587, 357, 627, "이전", "←")
            if self.profile_page + 1 < pages:
                self._menu_button(layer, "right", 708, 587, 857, 627, "다음", "→")
        else:
            c.create_text(W / 2, 378, text="아직 전투 기록이 없습니다",
                          fill=C_TEXT, font=F_MID, tags=layer)
            c.create_text(W / 2, 414, text="OS 배포 후 도전을 마치면 여기에 결과가 표시됩니다.",
                          fill="#a3bbd2", font=F_UI, tags=layer)
        self._menu_button(layer, "escape", W - 407, 587, W - 208, 627,
                          "허브로 돌아가기", "ESC", primary=True)
        c.tag_raise(layer)

    def _draw_checkpoint_prompt(self):
        """In-world gate prompt; OS progress is never selected from title GUI."""
        c = self.c
        c.delete("resumeui")
        if not self.checkpoint_prompt_open:
            return
        data = self.checkpoint_pending
        c.create_rectangle(0, 0, W, H, fill="#020711", stipple="gray75",
                           outline="", tags="resumeui")
        c.create_rectangle(265, 133, W-265, 570, fill="#0a1e2b",
                           outline="#3d93b3", width=2, tags="resumeui")
        c.create_text(W/2, 178, text="OS RECOVERY  ·  ANTIVIRUS DEPLOYMENT",
                      fill="#8beaf7", font=F_BIG, tags="resumeui")
        if data is None:
            c.create_text(
                W/2, 226,
                text="중단된 OS 기록을 안전하게 읽지 못했습니다.",
                fill="#ffb1ad", font=F_UI, tags="resumeui")
            c.create_text(
                W/2, 254,
                text="손상된 원본은 .corrupt 파일로 보존하고 새 OS를 시작할 수 있습니다.",
                fill="#b6cbd2", font=F_UI, tags="resumeui")
        else:
            progress = data["campaign"]["completed_waves"]
            total = sum(progress.values())
            c.create_text(
                W/2, 220,
                text=("사망한 운영체제 기록 · 이어하기 불가" if data["scene"] == "dead"
                      else f"RUN {data['campaign']['run_id'][:16]}  ·  진행 {total}/20 웨이브"),
                fill="#c3e7f1", font=F_MID, tags="resumeui")
            c.create_text(
                W/2, 255,
                text=("CPU {CPU}/5   MEMORY {MEMORY}/5   "
                      "STORAGE {STORAGE}/5   NETWORK {NETWORK}/5").format(**progress),
                fill="#8cbac9", font=F_UI, tags="resumeui")

        labels = (
            ("이전 운영체제 이어하기" if data is not None and
             data["scene"] != "dead" else "이어하기 불가"),
            ("이전 OS 종료 기록 확정 후 새 배포" if data is not None and
             data["scene"] == "dead" else
             "이전 OS 포기 후 새 운영체제 배포" if data is not None else
             "손상된 기록 보관 후 새 운영체제 배포"),
        )
        for i, label in enumerate(labels):
            cy = 323 + i*72
            selected = self.checkpoint_choice == i
            c.create_rectangle(324, cy-26, W-324, cy+25,
                               fill="#1b4d5d" if selected else "#122836",
                               outline="#76e6ef" if selected else "#395768",
                               width=2 if selected else 1,
                               tags="resumeui")
            c.create_text(W/2, cy, text=label,
                          fill="#ffffff" if selected else "#9fb9c3",
                          font=F_UI, tags="resumeui")
        c.create_rectangle(492, 451, 788, 501, fill="#235569",
                           outline="#83dce9", width=2, tags="resumeui")
        c.create_text(W/2, 475, text="ENTER / 클릭 · 확인",
                      fill="#e4ffff", font=F_UI, tags="resumeui")
        c.create_text(W/2, 534,
                      text="↑↓ 선택 · ESC 또는 상호작용 키로 취소",
                      fill="#8aa8b5", font=F_UI, tags="resumeui")
        c.tag_raise("resumeui")

    def _draw_database_terminal(self):
        c = self.c
        c.delete("dbui")
        self.term_marker_boxes["db"] = None
        if not self.database_open:
            return
        c.create_rectangle(0, 0, W, H, fill="#010610", stipple="gray75",
                           outline="", tags="dbui")
        c.create_rectangle(244, 72, W-244, H-69, fill="#071926",
                           outline="#3382a0", width=2, tags="dbui")
        c.create_text(W/2, 116, text="ANTIVIRUS DATABASE",
                      fill="#81e9f6", font=F_HUGE, tags="dbui")
        level = self.database.antivirus_level
        current, required = xp_progress(self.database)
        xp_label = "MAX LEVEL" if required == 0 else (
            f"XP {current:,} / {required:,} · 누적 {self.database.xp:,}")
        c.create_text(W/2, 162, text=f"LEVEL {level}  ·  {xp_label}",
                      fill="#ffd17f", font=F_MID, tags="dbui")
        # 레벨 진행 바: 다음 단계까지 얼마나 채워졌는지 한눈에 보인다.
        bar_w = 340.0
        bar_x = W / 2 - bar_w / 2
        c.create_rectangle(bar_x, 184, bar_x + bar_w, 194, fill="#0c2431",
                           outline="#244b5c", tags="dbui")
        if required > 0:
            ratio = clamp(current / float(required), 0.0, 1.0)
            c.create_rectangle(bar_x + 2, 186,
                               bar_x + 2 + (bar_w - 4) * ratio, 192,
                               fill="#ffd17f", outline="", tags="dbui")
        else:
            c.create_rectangle(bar_x + 2, 186, bar_x + bar_w - 2, 192,
                               fill="#ffd17f", outline="", tags="dbui")
        c.create_text(W/2, 216,
                      text="다음 OS에 1종 장착 · 방향키 또는 클릭으로 선택",
                      fill="#96b8ca", font=F_UI, tags="dbui")
        options = self._database_options()
        sel = self.database_selected
        first = max(0, min(sel-3, len(options)-7))
        end = min(len(options), first+7)
        for index in range(first, end):
            uid = options[index]
            y = 252 + (index-first)*44
            selected = index == sel
            c.create_rectangle(326, y-15, W-326, y+21,
                               fill="#154050" if selected else "#0c2431",
                               outline="#5dd9e5" if selected else "#244b5c",
                               width=2 if selected else 1,
                               tags=("dbui", "terminal-row", f"row:{index}"))
            if selected:
                self.term_marker_boxes["db"] = (332, y - 12, 332, y + 18)
            label = ("기본 장비로 시작" if uid is None else
                     f"{UP_BY_ID[uid][1]}  ·  보관 {self.database.software_archive[uid]}개")
            c.create_text(W/2, y+3, text=label,
                          fill="#e7ffff" if selected else "#accad6",
                          font=F_UI, tags="dbui")
        self._menu_button("dbui", "return", 326, 570, 630, 609,
                          f"장비 적용  ·  {sel+1} / {len(options)}", "ENTER", primary=True)
        self._menu_button("dbui", "escape", 650, 570, W-326, 609, "돌아가기", "ESC")
        if self.database_error is not None:
            c.create_text(W/2, 642, text="데이터베이스 읽기 오류 · 영구 저장 비활성",
                          fill="#ff6b82", font=F_UI, tags="dbui")
        c.tag_raise("dbui")

    def _draw_extraction_terminal(self):
        c = self.c
        c.delete("extractui")
        self.term_marker_boxes["extract"] = None
        if not self.extraction_open:
            return
        c.create_rectangle(0, 0, W, H, fill="#010610", stipple="gray75",
                           outline="", tags="extractui")
        c.create_rectangle(237, 65, W-237, H-64, fill="#0b2026",
                           outline="#56d6b2", width=3, tags="extractui")
        c.create_text(W/2, 111, text="OS STATUS: CLEAN",
                      fill="#81f5cb", font=F_HUGE, tags="extractui")
        c.create_text(W/2, 164, text="ALL SECTORS PURIFIED  ·  ANTIVIRUS EXTRACTION",
                      fill="#95d8da", font=F_UI, tags="extractui")
        c.create_text(W/2, 203,
                      text="영구 보존할 소프트웨어 1개를 방향키 또는 클릭으로 선택하세요",
                      fill="#e1f8f1", font=F_UI, tags="extractui")
        options = self.extraction_options
        sel = self.extraction_selected
        first = max(0, min(sel-3, len(options)-7))
        end = min(len(options), first+7)
        for index in range(first, end):
            uid = options[index]
            y = 241 + (index-first)*44
            selected = index == sel
            c.create_rectangle(312, y-14, W-312, y+21,
                               fill="#185044" if selected else "#0b302d",
                               outline="#70f0ce" if selected else "#285c55",
                               width=2 if selected else 1,
                               tags=("extractui", "terminal-row", f"row:{index}"))
            if selected:
                self.term_marker_boxes["extract"] = (318, y - 11, 318, y + 18)
            c.create_text(W/2, y+4,
                          text=UP_BY_ID[uid][1],
                          fill="#ffffff" if selected else "#9ccabe",
                          font=F_UI, tags="extractui")
        self._menu_button("extractui", "return", 312, 555, 630, 593,
                          f"철수 확정  ·  {sel+1} / {len(options)}", "ENTER", primary=True)
        self._menu_button("extractui", "escape", 650, 555, W-312, 593,
                          "철수 취소", "ESC")
        c.create_text(W/2, 609,
                      text="선택한 모듈 1개만 영구 보존 · 나머지 런 소프트웨어 초기화",
                      fill="#b5d4cc", font=F_UI, tags="extractui")
        c.tag_raise("extractui")

    def _draw_salvage_terminal(self):
        c = self.c
        c.delete("salvageui")
        self.term_marker_boxes["salvage"] = None
        if not self.salvage_open:
            return
        c.create_rectangle(0, 0, W, H, fill="#010711", stipple="gray75",
                           outline="", tags="salvageui")
        c.create_rectangle(235, 67, W-235, H-67, fill="#10202b",
                           outline="#e2b36f", width=2, tags="salvageui")
        c.create_text(W/2, 112, text="OS CLEAN  ·  SOFTWARE RECOVERY",
                      fill="#ffd89a", font=F_BIG, tags="salvageui")
        if self.salvage_options:
            c.create_text(W/2, 154,
                          text="철수 가능한 소프트웨어가 없습니다. 정화 보상으로 1종 회수 가능",
                          fill="#c5dfdf", font=F_UI, tags="salvageui")
            c.create_text(W/2, 185,
                          text="방향키 또는 클릭으로 1종 선택 · 회수 후 철수 단말기로 이동",
                          fill="#9cbbc6", font=F_UI, tags="salvageui")
            first = max(0, min(self.salvage_selected-3, len(self.salvage_options)-7))
            end = min(len(self.salvage_options), first+7)
            for i in range(first, end):
                uid = self.salvage_options[i]
                y = 235 + (i-first)*46
                selected = i == self.salvage_selected
                c.create_rectangle(310, y-14, W-310, y+22,
                                   fill="#59422a" if selected else "#242b35",
                                   outline="#edc182" if selected else "#565764",
                                   width=2 if selected else 1,
                                   tags=("salvageui", "terminal-row", f"row:{i}"))
                if selected:
                    self.term_marker_boxes["salvage"] = (316, y - 11, 316, y + 19)
                c.create_text(W/2, y+3, text=UP_BY_ID[uid][1],
                              fill="#fff4d6" if selected else "#c2cbce",
                              font=F_UI, tags="salvageui")
            label = f"소프트웨어 회수  ·  {self.salvage_selected+1} / {len(self.salvage_options)}"
        else:
            c.create_text(W/2, 285,
                          text=("이미 이 OS에서 소프트웨어 회수 기회를 사용했습니다."
                                if self.salvage_claimed else
                                "영구 소프트웨어 보관 한도에 모두 도달했습니다."),
                          fill="#ffd89a", font=F_MID, tags="salvageui")
            c.create_text(W/2, 340,
                          text="보관 한도 우회나 가짜 철수 보상 없이 종료할 수 있습니다.",
                          fill="#c3d7dc", font=F_UI, tags="salvageui")
            label = "확정 XP를 유지하고 OS 떠나기"
        self._menu_button("salvageui", "return", 310, 570, 630, 612,
                          label, "ENTER", primary=True)
        self._menu_button("salvageui", "escape", 650, 570, W-310, 612,
                          "돌아가기", "ESC")
        if self.database_close_error:
            c.create_text(W/2, 667,
                          text="종료 기록 저장 실패 · Enter로 재시도",
                          fill="#ff8793", font=F_UI, tags="salvageui")
        c.tag_raise("salvageui")

    def _animate_pause(self):
        appear = (1.0 if self.settings.reduced_effects else
                  clamp(self.pause_t / 0.24, 0, 1))
        ease = 1.0 - (1.0 - appear) ** 3
        self.c.coords(self.pause_title, W / 2, H / 2 - 60 - (1 - ease) * 24)
        self.c.itemconfigure(self.pause_title,
                             fill="#ffffff" if appear < 0.45 else C_PLAYER)

    def _animate_game_over(self):
        appear = (1.0 if self.settings.reduced_effects else
                  clamp(self.go_t / 0.48, 0, 1))
        ease = 1.0 - (1.0 - appear) ** 3
        self.c.coords(self.dead_title, W / 2, 200 + (1.0 - ease) * 45)

    def _render_screen_fx(self):
        c = self.c
        if self.flash_t > 0 and not self.settings.reduced_effects:
            c.itemconfigure(self.flash_rect, fill=self.flash_col,
                            stipple="gray12" if self.flash_t < self.flash_dur * 0.5
                            else "gray25", state="normal")
        else:
            c.itemconfigure(self.flash_rect, state="hidden")
        if self.sweep_t > 0 and not self.settings.reduced_effects:
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

    def _update_message(self):
        """Show a blocking message on any screen (errors the player must see)."""
        c = self.c
        text = getattr(self, "message", "")
        if not text or getattr(self, "message_t", 0.0) <= 0:
            c.itemconfigure(self.message_txt, state="hidden")
            c.itemconfigure(self.message_bg, state="hidden")
            return
        c.itemconfigure(self.message_txt, state="normal", text=text)
        width = min(W - 120, 24 + len(text) * 7.6)
        c.coords(self.message_bg, W / 2 - width / 2, H - 62,
                 W / 2 + width / 2, H - 30)
        c.itemconfigure(self.message_bg, state="normal")

    def draw_world(self, ox, oy):
        c = self.c
        p = self.p

        for remaining, source, action in self.virus_pending:
            if source.hp <= 0:
                continue
            radius = (ENCRYPTION_PULSE_RADIUS if action.behavior == "encryption_pulse"
                      else source.r + 20)
            imminent = remaining <= 0.18 and not self.settings.reduced_effects
            self.pool_warning.draw(source.x + ox, source.y + oy, radius, "",
                                   outline=C_BAD if imminent else C_GOLD)

        for s in self.shards:
            r = 3.2 + math.sin(s.life * 9.0) * 0.8
            col = C_SHARD if s.life > 3.0 else "#3d6b78"
            self.pool_shard.draw(s.x + ox, s.y + oy, r, col)
            self.pool_ring.draw(s.x + ox, s.y + oy, r + 2.4, "", outline=col)

        for e in self.enemies:
            col = E_PARAMS[e.kind]["color"]
            if e.statuses and self.frames % 12 < 6:
                col = C_RUSH if any(s.kind == "burn" for s in e.statuses) else C_HP
            if e.flash > 0.35:
                col = "#ffffff"
            elif e.vulnerable_t > 0:
                col = "#89ffd1"
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
            elif e.kind in ("boss", "overseer", "replicator",
                             "encryptor", "zero_day"):
                spin = e.t * 0.9
                self.pool_ring.draw(x, y, r + 14, "", outline=C_BOSS)
                self.pool_ring.draw(x, y, r + 6, "", outline="#7d2f78")
                self.pool_enemy.draw(self._poly(6, x, y, r, spin), col)
                self.pool_enemy.draw(self._poly(6, x, y, r * 0.62, -spin * 1.6),
                                     "#5c1058")
                self.pool_ring.draw(x, y, r * 0.3, "#ffe6ff")
            elif e.kind in ("overheater", "worm", "trojan", "ransomware",
                            "rootkit", "packet_swarm", "guardian"):
                sides = {"overheater": 3, "worm": 5, "trojan": 4,
                         "ransomware": 6, "rootkit": 8,
                         "packet_swarm": 3, "guardian": 7}[e.kind]
                self.pool_enemy.draw(self._poly(sides, x, y, r, e.t*0.4), col)
                self.pool_ring.draw(x, y, r*0.43, "", outline="#bad6e4")
                if e.guard_t > 0:
                    self.pool_ring.draw(x, y, r + 7, "", outline=C_GOLD)
            if e.vulnerable_t > 0:
                self.pool_ring.draw(x, y, r + 10, "", outline="#89ffd1")

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
        if p.guard_t > 0:
            self.pool_ring.draw(x, y, 32 + math.sin(self.ui_t * 7) * 3,
                                "", outline=C_HP)
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
