"""Reusable tkinter Canvas object pools."""

from __future__ import annotations



# ----------------------------------------------------------------------------
# 캔버스 아이템 풀 (매 프레임 생성/삭제 대신 재사용)
# ----------------------------------------------------------------------------
class OvalPool:
    def __init__(self, canvas, count, **kw):
        self.c = canvas
        self.items = [
            canvas.create_oval(-30, -30, -30, -30, state="hidden", **kw)
            for _ in range(count)
        ]
        self.n = 0
        self.prev = 0

    def begin(self):
        self.prev = self.n
        self.n = 0

    def draw(self, x, y, r, fill, outline=""):
        if self.n >= len(self.items):
            return None
        it = self.items[self.n]
        self.n += 1
        self.c.coords(it, x - r, y - r, x + r, y + r)
        self.c.itemconfigure(it, fill=fill, outline=outline, state="normal")
        return it

    def end(self):
        for k in range(self.n, self.prev):
            self.c.itemconfigure(self.items[k], state="hidden")


class PolyPool:
    """모든 아이템을 같은 정점 수로 만들고, 남는 정점은 마지막 점에 겹쳐 둔다."""

    def __init__(self, canvas, count, npoints=8, **kw):
        self.c = canvas
        self.np = npoints
        self.items = [
            canvas.create_polygon(*([-40, -40] * npoints), state="hidden", **kw)
            for _ in range(count)
        ]
        self.n = 0
        self.prev = 0

    def begin(self):
        self.prev = self.n
        self.n = 0

    def draw(self, pts, fill, outline="", width=1):
        if self.n >= len(self.items) or not pts:
            return None
        it = self.items[self.n]
        self.n += 1
        coords = []
        for i in range(self.np):
            p = pts[i] if i < len(pts) else pts[-1]
            coords.append(p[0])
            coords.append(p[1])
        self.c.coords(it, *coords)
        self.c.itemconfigure(it, fill=fill, outline=outline,
                             width=width, state="normal")
        return it

    def end(self):
        for k in range(self.n, self.prev):
            self.c.itemconfigure(self.items[k], state="hidden")


class TextPool:
    def __init__(self, canvas, count, font, **kw):
        self.c = canvas
        self.items = [
            canvas.create_text(-40, -40, text="", state="hidden", font=font, **kw)
            for _ in range(count)
        ]
        self.n = 0
        self.prev = 0

    def draw(self, x, y, text, fill):
        if self.n >= len(self.items):
            return None
        it = self.items[self.n]
        self.n += 1
        self.c.coords(it, x, y)
        self.c.itemconfigure(it, text=text, fill=fill, state="normal")
        return it

    def begin(self):
        self.prev = self.n
        self.n = 0

    def end(self):
        for k in range(self.n, self.prev):
            self.c.itemconfigure(self.items[k], state="hidden")


class LinePool:
    def __init__(self, canvas, count, **kw):
        self.c = canvas
        self.items = [
            canvas.create_line(0, 0, 0, 0, state="hidden", **kw) for _ in range(count)
        ]
        self.n = 0
        self.prev = 0

    def begin(self):
        self.prev = self.n
        self.n = 0

    def draw(self, x1, y1, x2, y2, fill, width=2):
        if self.n >= len(self.items):
            return None
        it = self.items[self.n]
        self.n += 1
        self.c.coords(it, x1, y1, x2, y2)
        self.c.itemconfigure(it, fill=fill, width=width, state="normal")
        return it

    def end(self):
        for k in range(self.n, self.prev):
            self.c.itemconfigure(self.items[k], state="hidden")
