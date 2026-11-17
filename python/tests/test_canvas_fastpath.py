"""No-display checks for the ScaledCanvas fast itemconfigure path."""
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

from neonvoid.display import ScaledCanvas


class CanvasConfigureTests(unittest.TestCase):
    def test_normal_item_updates_do_not_look_up_tags(self):
        fake = SimpleNamespace(_style=Mock(), _draw_opts=Mock())
        with patch("tkinter.Canvas.find_withtag") as lookup, \
             patch("tkinter.Canvas.itemconfigure", return_value=None) as configure:
            ScaledCanvas.itemconfigure(fake, 71, text="ready", state="normal")
            lookup.assert_not_called()
            fake._style.assert_not_called()
            fake._draw_opts.assert_not_called()
            configure.assert_called_once_with(
                fake, 71, None, text="ready", state="normal")

    def test_numeric_item_width_keeps_logical_style(self):
        fake = SimpleNamespace(
            _style=Mock(),
            _draw_opts=Mock(return_value={"width": 3.0}),
        )
        with patch("tkinter.Canvas.find_withtag") as lookup, \
             patch("tkinter.Canvas.itemconfigure", return_value=None) as configure:
            ScaledCanvas.itemconfigure(fake, 71, width=2)
            lookup.assert_not_called()
            fake._style.assert_called_once_with(71, {"width": 2})
            configure.assert_called_once_with(fake, 71, None, width=3.0)

    def test_tag_width_updates_all_logical_styles(self):
        fake = SimpleNamespace(
            _style=Mock(), _draw_opts=Mock(return_value={"width": 2.0}))
        with patch("tkinter.Canvas.find_withtag", return_value=(8, 9)), \
             patch("tkinter.Canvas.itemconfigure", return_value=None):
            ScaledCanvas.itemconfigure(fake, "hud", width=2)
            self.assertEqual(fake._style.call_count, 2)


if __name__ == "__main__":
    unittest.main()
