"""Verify the Godot catalog matches the legacy Python modules (read-only)."""
import json
from pathlib import Path
from types import SimpleNamespace
import unittest
from neonvoid.content.upgrades import UPGRADES, UP_WEIGHT, apply_upgrade
from neonvoid.entities import Player

PATH = Path(__file__).resolve().parents[2] / "scripts/software/catalog.json"


class GodotSoftwareParityTests(unittest.TestCase):
    def test_metadata(self):
        rows = json.loads(PATH.read_text("utf-8"))["entries"]
        self.assertEqual(len(rows), 30)
        self.assertEqual([x["id"] for x in rows], [u[0] for u in UPGRADES])
        for row, (uid, name, desc, tier, cap) in zip(rows, UPGRADES):
            self.assertEqual((row["id"], row["name"], row["description"], row["tier"], row["max_stack"]),
                             (uid, name, desc, tier, cap))
            self.assertEqual(row["weight"], UP_WEIGHT[tier])

    def test_effects_at_every_stack(self):
        for row in json.loads(PATH.read_text("utf-8"))["entries"]:
            with self.subTest(uid=row["id"]):
                p = Player()
                g = SimpleNamespace(p=p, levels={})
                st = p.st.copy()
                hp, maxhp, mana, maxmana, charges = p.hp, p.maxhp, p.mana, p.maxmana, p.charges
                for _ in range(row["max_stack"]):
                    apply_upgrade(g, row["id"])
                    for effect in row["effects"]:
                        key, op, value = effect["stat"], effect["op"], effect["value"]
                        if key == "maxhp":
                            maxhp += value
                            hp = min(maxhp, hp + value)
                        elif key == "maxmana":
                            maxmana += value
                            mana = min(maxmana, mana + value)
                        else:
                            if op == "add":
                                st[key] += value
                            elif op == "mul":
                                st[key] *= value
                            elif op == "cap_add":
                                st[key] = min(effect["cap"], st[key] + value)
                            else:
                                self.fail("Invalid effect op " + op)
                            if key == "dash_max":
                                charges += int(value)
                    for key, value in st.items():
                        self.assertAlmostEqual(p.st[key], value, places=8)
                    self.assertAlmostEqual(p.hp, hp)
                    self.assertAlmostEqual(p.maxhp, maxhp)
                    self.assertAlmostEqual(p.mana, mana)
                    self.assertAlmostEqual(p.maxmana, maxmana)
                    self.assertEqual(p.charges, charges)
