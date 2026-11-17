"""Regression tests for rendering pool visibility and style reuse.

No Tk display is required; the fake records Canvas command traffic.
"""
import unittest

from neonvoid.render_pool import OvalPool, PolyPool, TextPool, LinePool


class FakeCanvas:
    def __init__(self):
        self.created = []
        self.changed = []
        self.moved = []

    def _create(self, kind, args, opts):
        index = len(self.created) + 1
        self.created.append((kind, index, args, opts))
        return index

    def create_oval(self, *args, **opts):
        return self._create("oval", args, opts)

    def create_polygon(self, *args, **opts):
        return self._create("polygon", args, opts)

    def create_text(self, *args, **opts):
        return self._create("text", args, opts)

    def create_line(self, *args, **opts):
        return self._create("line", args, opts)

    def coords(self, item, *coordinates):
        self.moved.append((item, coordinates))

    def itemconfigure(self, item, **opts):
        self.changed.append((item, opts))


class RenderPoolTests(unittest.TestCase):
    def test_oval_reuses_style_and_restores_hidden_item(self):
        canvas = FakeCanvas()
        pool = OvalPool(canvas, 1, fill="#aabbcc", outline="")
        pool.begin()
        pool.draw(10, 20, 3, "#aabbcc")
        pool.end()
        self.assertEqual(canvas.changed, [(1, {"state": "normal"})])

        canvas.changed.clear()
        pool.begin()
        pool.draw(20, 30, 3, "#aabbcc")
        pool.end()
        self.assertEqual(canvas.changed, [])

        pool.begin()
        pool.end()
        self.assertEqual(canvas.changed, [(1, {"state": "hidden"})])

        canvas.changed.clear()
        pool.begin()
        pool.draw(20, 30, 3, "#ccbb00")
        pool.end()
        self.assertEqual(canvas.changed, [
            (1, {"fill": "#ccbb00", "state": "normal"})])

    def test_poly_width_and_padding(self):
        canvas = FakeCanvas()
        pool = PolyPool(canvas, 1, npoints=4, fill="#fff")
        pool.begin()
        pool.draw([(1, 2), (3, 4), (5, 6)], "#fff", width=2)
        pool.end()
        self.assertEqual(canvas.moved[-1][1], (1, 2, 3, 4, 5, 6, 5, 6))
        self.assertEqual(canvas.changed[-1][1]["width"], 2)

        canvas.changed.clear()
        pool.begin()
        pool.draw([(2, 3), (4, 5), (6, 7)], "#fff", width=2)
        pool.end()
        self.assertEqual(canvas.changed, [])

        pool.begin()
        pool.draw([(2, 3), (4, 5), (6, 7)], "#fff", width=3)
        pool.end()
        self.assertEqual(canvas.changed, [(1, {"width": 3})])

    def test_text_and_line_update_only_changed_fields(self):
        for pool_factory, draw, changed in [
            (lambda c: TextPool(c, 1, ("Consolas", 10)),
             lambda p, text, color: p.draw(0, 0, text, color), ("A", "B")),
            (lambda c: LinePool(c, 1, fill="#fff"),
             lambda p, text, color: p.draw(0, 0, 10, 10, color, 2), ("A", "B")),
        ]:
            with self.subTest(pool_factory=pool_factory):
                canvas = FakeCanvas()
                pool = pool_factory(canvas)
                pool.begin()
                draw(pool, changed[0], "#fff")
                pool.end()
                canvas.changed.clear()
                pool.begin()
                draw(pool, changed[0], "#fff")
                pool.end()
                self.assertEqual(canvas.changed, [])
                pool.begin()
                draw(pool, changed[1], "#000")
                pool.end()
                self.assertTrue(any("fill" in config for _, config in canvas.changed))

    def test_overflow_does_not_create_new_canvas_items(self):
        canvas = FakeCanvas()
        pool = OvalPool(canvas, 1)
        pool.begin()
        self.assertIsNotNone(pool.draw(1, 1, 1, "#fff"))
        self.assertIsNone(pool.draw(1, 1, 1, "#fff"))
        pool.end()
        self.assertEqual(len(canvas.created), 1)


if __name__ == "__main__":
    unittest.main()
