"""Resolution-independent Tk Canvas with a 1280x720 logical coordinate space.

All game code continues to use logical positions. The actual widget tracks
the window size, fits the entire scene with letterboxing, and maps both drawn
objects AND pointer input through the same affine transform. This avoids
patching thousands of individual HUD/world/aim coordinates.
"""
from __future__ import annotations

from dataclasses import dataclass
import math
import tkinter as tk
from typing import Any

from .constants import W, H, C_BG


@dataclass(frozen=True)
class Viewport:
    width: int
    height: int
    scale: float
    left: float
    top: float

    @classmethod
    def fit(cls, width: int, height: int) -> "Viewport":
        if (type(width) is not int or type(height) is not int
                or width < 1 or height < 1):
            raise ValueError("Viewport must have positive pixel dimensions")
        scale = min(width / W, height / H)
        return cls(width, height, scale, (width - W * scale) / 2,
                   (height - H * scale) / 2)

    def to_screen(self, x: float, y: float) -> tuple[float, float]:
        return self.left + x * self.scale, self.top + y * self.scale

    def to_game(self, x: float, y: float) -> tuple[float, float]:
        return (x - self.left) / self.scale, (y - self.top) / self.scale

    def contains(self, x: float, y: float) -> bool:
        gx, gy = self.to_game(x, y)
        return 0 <= gx <= W and 0 <= gy <= H


def _flatten(values):
    if len(values) == 1 and isinstance(values[0], (tuple, list)):
        values = values[0]
    out = []
    for value in values:
        if isinstance(value, (tuple, list)):
            out.extend(_flatten(value))
        else:
            out.append(value)
    return tuple(out)


class ScaledCanvas(tk.Canvas):
    """Native Canvas, logical coordinate API, and scaled font/line metrics.

    Tk's .scale('all') alone does not scale later Canvas.coords() calls or text
    font sizes. This subclass applies the viewport to *every* creation/update,
    preserving the legacy game API. Resize transforms existing items exactly
    once and does not create new gameplay objects.
    """

    def __init__(self, parent, **kwargs):
        super().__init__(parent, width=W, height=H, bg=C_BG,
                         highlightthickness=0, bd=0, **kwargs)
        self.viewport = Viewport.fit(W, H)
        self._fonts: dict[int, Any] = {}
        self._widths: dict[int, float] = {}
        self._bars = tuple(
            tk.Canvas.create_rectangle(self, 0, 0, 0, 0, fill=C_BG,
                                       outline="", tags="viewport-letterbox")
            for _ in range(4)
        )

    def _map(self, coords):
        flat = _flatten(coords)
        v = self.viewport
        return tuple(v.left + x * v.scale if i % 2 == 0 else
                     v.top + x * v.scale for i, x in enumerate(flat))

    def _font(self, font):
        if isinstance(font, (tuple, list)) and len(font) >= 2 and type(font[1]) is int:
            base = font[1]
            size = max(1, round(abs(base) * self.viewport.scale))
            return (font[0], size if base >= 0 else -size, *font[2:])
        return font

    def _style(self, item, opts):
        """Store logical style properties so zoom doesn't accumulate rounding."""
        if "font" in opts:
            self._fonts[int(item)] = opts["font"]
        if "width" in opts:
            try:
                self._widths[int(item)] = float(opts["width"])
            except (TypeError, ValueError):
                pass

    def _draw_opts(self, opts):
        output = dict(opts)
        if "font" in output:
            output["font"] = self._font(output["font"])
        if "width" in output:
            output["width"] = float(output["width"]) * self.viewport.scale
        return output

    def _create_scaled(self, kind, coords, kw):
        creator = getattr(tk.Canvas, kind)
        item = creator(self, *self._map(coords), **self._draw_opts(kw))
        self._style(item, kw)
        return item

    def create_rectangle(self, *coords, **kw):
        return self._create_scaled("create_rectangle", coords, kw)

    def create_oval(self, *coords, **kw):
        return self._create_scaled("create_oval", coords, kw)

    def create_line(self, *coords, **kw):
        return self._create_scaled("create_line", coords, kw)

    def create_arc(self, *coords, **kw):
        return self._create_scaled("create_arc", coords, kw)

    def create_polygon(self, *coords, **kw):
        return self._create_scaled("create_polygon", coords, kw)

    def create_text(self, *coords, **kw):
        return self._create_scaled("create_text", coords, kw)

    def coords(self, item, *newcoords):
        if newcoords:
            return tk.Canvas.coords(self, item, *self._map(newcoords))
        values = tk.Canvas.coords(self, item)
        v = self.viewport
        return [(value - (v.left if i % 2 == 0 else v.top)) / v.scale
                for i, value in enumerate(values)]

    def move(self, item, dx, dy):
        return tk.Canvas.move(self, item,
                              float(dx) * self.viewport.scale,
                              float(dy) * self.viewport.scale)

    def bbox(self, *args):
        box = tk.Canvas.bbox(self, *args)
        if box is None:
            return None
        a, b = self.viewport.to_game(box[0], box[1])
        c, d = self.viewport.to_game(box[2], box[3])
        return a, b, c, d

    def find_overlapping(self, x1, y1, x2, y2):
        a, b = self.viewport.to_screen(x1, y1)
        c, d = self.viewport.to_screen(x2, y2)
        return tk.Canvas.find_overlapping(self, a, b, c, d)

    def find_closest(self, x, y, halo=None, start=None):
        a, b = self.viewport.to_screen(x, y)
        h = None if halo is None else halo * self.viewport.scale
        return tk.Canvas.find_closest(self, a, b, h, start)

    def itemconfigure(self, tagOrId, cnf=None, **kw):
        if isinstance(cnf, dict):
            kw = {**cnf, **kw}
            cnf = None
        if kw:
            for item in tk.Canvas.find_withtag(self, tagOrId):
                self._style(item, kw)
            kw = self._draw_opts(kw)
        return tk.Canvas.itemconfigure(self, tagOrId, cnf, **kw)

    itemconfig = itemconfigure

    def itemcget(self, tagOrId, option):
        # Existing menu hit-tests inspect logical line widths (2 = primary).
        if option == "width":
            for item in tk.Canvas.find_withtag(self, tagOrId):
                if item in self._widths:
                    return str(self._widths[item])
        return tk.Canvas.itemcget(self, tagOrId, option)

    def delete(self, *items):
        for value in items:
            for item in tk.Canvas.find_withtag(self, value):
                self._fonts.pop(item, None)
                self._widths.pop(item, None)
        return tk.Canvas.delete(self, *items)

    def set_viewport_size(self, width: int, height: int) -> bool:
        new = Viewport.fit(max(1, int(width)), max(1, int(height)))
        old = self.viewport
        if new == old:
            return False
        ratio = new.scale / old.scale
        tk.Canvas.scale(self, "all", 0, 0, ratio)
        tk.Canvas.move(self, "all",
                       new.left - old.left * ratio,
                       new.top - old.top * ratio)
        self.viewport = new
        for item, original in list(self._fonts.items()):
            if tk.Canvas.type(self, item) == "text":
                tk.Canvas.itemconfigure(self, item, font=self._font(original))
            else:
                self._fonts.pop(item, None)
        for item, original in list(self._widths.items()):
            if tk.Canvas.type(self, item):
                tk.Canvas.itemconfigure(self, item, width=original * new.scale)
            else:
                self._widths.pop(item, None)
        self.raise_letterbox()
        return True

    def raise_letterbox(self):
        """Opaque bars clip any bloom/artwork that leaves the logical screen."""
        v = self.viewport
        rectangles = (
            (0, 0, v.width, v.top),
            (0, v.top + H * v.scale, v.width, v.height),
            (0, 0, v.left, v.height),
            (v.left + W * v.scale, 0, v.width, v.height),
        )
        for item, coords in zip(self._bars, rectangles):
            tk.Canvas.coords(self, item, *coords)
        tk.Canvas.tag_raise(self, "viewport-letterbox")

    def game_point(self, event) -> tuple[float, float]:
        return self.viewport.to_game(event.x, event.y)

    def is_game_point(self, event) -> bool:
        return self.viewport.contains(event.x, event.y)
