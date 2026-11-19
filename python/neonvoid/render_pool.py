"""Reusable tkinter Canvas object pools with change-only style updates."""

from __future__ import annotations


class _StyleCache:
    """Limit Tcl itemconfigure calls to real changes.

    The pool exclusively owns its items. A slot that was hidden when the
    previous frame ended needs to be shown when it becomes active again;
    otherwise its last text/colour/width can safely be retained.
    """

    def _init_styles(self, count, initial):
        self._styles = [dict(initial) for _ in range(count)]

    def _configure_slot(self, index, **style):
        old = self._styles[index]
        changed = {name: value for name, value in style.items()
                   if name not in old or old[name] != value}
        if index >= self.prev:
            changed["state"] = "normal"
        if changed:
            self.c.itemconfigure(self.items[index], **changed)
            old.update(style)


class OvalPool(_StyleCache):
    def __init__(self, canvas, count, **kw):
        self.c = canvas
        self.items = [
            canvas.create_oval(-30, -30, -30, -30, state="hidden", **kw)
            for _ in range(count)
        ]
        self._init_styles(count, kw)
        self.n = 0
        self.prev = 0

    def begin(self):
        self.prev = self.n
        self.n = 0

    def draw(self, x, y, r, fill, outline=""):
        if self.n >= len(self.items):
            return None
        index = self.n
        it = self.items[index]
        self.n += 1
        self.c.coords(it, x - r, y - r, x + r, y + r)
        self._configure_slot(index, fill=fill, outline=outline)
        return it

    def end(self):
        for k in range(self.n, self.prev):
            self.c.itemconfigure(self.items[k], state="hidden")


class PolyPool(_StyleCache):
    """Pad unused polygon vertices with repeats of the last vertex."""

    def __init__(self, canvas, count, npoints=8, **kw):
        self.c = canvas
        self.np = npoints
        self.items = [
            canvas.create_polygon(*([-40, -40] * npoints), state="hidden", **kw)
            for _ in range(count)
        ]
        self._init_styles(count, kw)
        self.n = 0
        self.prev = 0

    def begin(self):
        self.prev = self.n
        self.n = 0

    def draw(self, pts, fill, outline="", width=1):
        if self.n >= len(self.items) or not pts:
            return None
        index = self.n
        it = self.items[index]
        self.n += 1
        coords = []
        for i in range(self.np):
            p = pts[i] if i < len(pts) else pts[-1]
            coords.append(p[0])
            coords.append(p[1])
        self.c.coords(it, *coords)
        self._configure_slot(index, fill=fill, outline=outline, width=width)
        return it

    def end(self):
        for k in range(self.n, self.prev):
            self.c.itemconfigure(self.items[k], state="hidden")


class TextPool(_StyleCache):
    def __init__(self, canvas, count, font, **kw):
        self.c = canvas
        self.items = [
            canvas.create_text(-40, -40, text="", state="hidden",
                               font=font, **kw)
            for _ in range(count)
        ]
        self._init_styles(count, {"text": "", **kw})
        self.n = 0
        self.prev = 0

    def draw(self, x, y, text, fill):
        if self.n >= len(self.items):
            return None
        index = self.n
        it = self.items[index]
        self.n += 1
        self.c.coords(it, x, y)
        self._configure_slot(index, text=text, fill=fill)
        return it

    def begin(self):
        self.prev = self.n
        self.n = 0

    def end(self):
        for k in range(self.n, self.prev):
            self.c.itemconfigure(self.items[k], state="hidden")


class LinePool(_StyleCache):
    def __init__(self, canvas, count, **kw):
        self.c = canvas
        self.items = [
            canvas.create_line(0, 0, 0, 0, state="hidden", **kw)
            for _ in range(count)
        ]
        self._init_styles(count, kw)
        self.n = 0
        self.prev = 0

    def begin(self):
        self.prev = self.n
        self.n = 0

    def draw(self, x1, y1, x2, y2, fill, width=2):
        if self.n >= len(self.items):
            return None
        index = self.n
        it = self.items[index]
        self.n += 1
        self.c.coords(it, x1, y1, x2, y2)
        self._configure_slot(index, fill=fill, width=width)
        return it

    def end(self):
        for k in range(self.n, self.prev):
            self.c.itemconfigure(self.items[k], state="hidden")
