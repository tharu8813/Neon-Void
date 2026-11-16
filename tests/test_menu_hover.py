"""Menu hover must never leak across overlays or retain stale pointer state."""

import unittest
from types import SimpleNamespace
from unittest.mock import MagicMock

from neonvoid.view import VisualMixin
from neonvoid.constants import C_PLAYER


class MenuHoverTests(unittest.TestCase):
    def make_view(self):
        view = VisualMixin()
        view.c = MagicMock()
        view.c.find_withtag.return_value = (1, 2)
        view.c.gettags.side_effect = (
            lambda item: ("ovl", "menu-button", "action:escape", "menu-primary")
            if item == 1 else ("scr", "menu-button", "action:return", "menu-secondary")
        )
        view.c.coords.return_value = (100, 100, 200, 150)
        view.salvage_open = view.extraction_open = view.database_open = False
        view.profile_open = view.loadout_open = view.settings_open = False
        view.paused = True
        view.checkpoint_prompt_open = False
        view.rebinding_action = None
        view.keys = set()
        view.cursor_trail = []
        return view

    def test_only_active_overlay_button_is_hit(self):
        view = self.make_view()
        self.assertEqual(view._menu_action_at(120, 120), "escape")
        view._update_menu_hover(120, 120)
        view.c.itemconfigure.assert_called_once_with(
            1, fill="#185064", outline=C_PLAYER, width=2)

    def test_hover_clears_on_pointer_leave_and_focus_loss(self):
        view = self.make_view()
        view._on_pointer_leave()
        self.assertFalse(view.cursor_on)
        self.assertEqual(view.c.itemconfigure.call_args.kwargs["fill"], "#10313d")
        view.c.itemconfigure.reset_mock()
        view.keys.add("w")
        view._on_focus_out()
        self.assertFalse(view.keys)
        self.assertFalse(view.cursor_on)
        self.assertEqual(view.c.itemconfigure.call_args.kwargs["fill"], "#10313d")

    def test_rebinding_disables_hover(self):
        view = self.make_view()
        view.rebinding_action = "dash"
        view._update_menu_hover(120, 120)
        self.assertEqual(view.c.itemconfigure.call_args.kwargs["fill"], "#10313d")


if __name__ == "__main__":
    unittest.main()
