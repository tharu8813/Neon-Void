using NeonVoid.Combat.Skills;
using NeonVoid.Core;
using NeonVoid.Missions;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// [확정] D-009 (2026-11-22): 교체는 허브의 스킬 장착 터미널에서. Where that happens is the
    /// hub's business; what happens when a skill is already in another slot is this.
    /// </summary>
    public sealed class SkillSlotTests
    {
        private const string Arc = "skill.arc_pulse";
        private const string Blink = "skill.packet_blink";
        private const string Shell = "skill.data_shell";
        private const string Lock = "skill.lockon";

        private static string[] Starting() => new[] { Arc, Blink, Shell };

        [Test]
        public void ThreeSlotsAndNoMore()
        {
            // [확정] docs/03 section 6: 별도 3슬롯.
            Assert.AreEqual(DesignConstants.ActiveSkillSlots, SkillSlots.None().Length);
            Assert.AreEqual(3, SkillSlots.None().Length);
        }

        [Test]
        public void AnEmptySlotTakesASkill()
        {
            SkillEquipResult result = SkillSlots.Equip(SkillSlots.None(), 1, Lock);

            Assert.AreEqual(SkillEquipVerdict.Equipped, result.Verdict);
            Assert.AreEqual(Lock, result.Slots[1]);
            Assert.AreEqual(SkillSlots.Empty, result.Slots[0]);
            Assert.IsTrue(result.Changed);
        }

        [Test]
        public void AFilledSlotIsReplaced()
        {
            SkillEquipResult result = SkillSlots.Equip(Starting(), 0, Lock);

            Assert.AreEqual(SkillEquipVerdict.Equipped, result.Verdict);
            Assert.AreEqual(Lock, result.Slots[0]);
            Assert.AreEqual(Blink, result.Slots[1], "another slot changed");
            Assert.AreEqual(Shell, result.Slots[2]);
        }

        [Test]
        public void ASkillAlreadyInAnotherSlotSwapsRatherThanDuplicates()
        {
            // Two buttons for one skill is one fewer skill than the player has slots for.
            SkillEquipResult result = SkillSlots.Equip(Starting(), 0, Shell);

            Assert.AreEqual(SkillEquipVerdict.Swapped, result.Verdict);
            Assert.AreEqual(2, result.SwappedWith);
            Assert.AreEqual(Shell, result.Slots[0]);
            Assert.AreEqual(Arc, result.Slots[2], "the displaced skill was thrown away");
            Assert.AreEqual(Blink, result.Slots[1], "an unrelated slot moved");
        }

        [Test]
        public void NoSkillEverSitsInTwoSlots()
        {
            // The property the swap exists to keep, checked over every pair.
            string[] slots = Starting();

            for (int from = 0; from < 3; ++from)
            {
                for (int to = 0; to < 3; ++to)
                {
                    SkillEquipResult result = SkillSlots.Equip(slots, to, slots[from]);

                    for (int a = 0; a < 3; ++a)
                    {
                        for (int b = a + 1; b < 3; ++b)
                        {
                            Assert.AreNotEqual(
                                result.Slots[a], result.Slots[b],
                                $"equipping slot {from}'s skill into {to} duplicated it");
                        }
                    }
                }
            }
        }

        [Test]
        public void PuttingASkillWhereItAlreadyIsChangesNothing()
        {
            SkillEquipResult result = SkillSlots.Equip(Starting(), 1, Blink);

            Assert.AreEqual(SkillEquipVerdict.AlreadyThere, result.Verdict);
            Assert.IsFalse(result.Changed);
            CollectionAssert.AreEqual(Starting(), result.Slots);
        }

        [Test]
        public void AnEmptyKeyClearsTheSlot()
        {
            SkillEquipResult result = SkillSlots.Equip(Starting(), 2, SkillSlots.Empty);

            Assert.AreEqual(SkillEquipVerdict.Cleared, result.Verdict);
            Assert.AreEqual(SkillSlots.Empty, result.Slots[2]);
            Assert.AreEqual(Arc, result.Slots[0]);
        }

        [Test]
        public void ASlotThatDoesNotExistIsRefused()
        {
            Assert.AreEqual(SkillEquipVerdict.NoSuchSlot, SkillSlots.Equip(Starting(), -1, Lock).Verdict);
            Assert.AreEqual(SkillEquipVerdict.NoSuchSlot, SkillSlots.Equip(Starting(), 3, Lock).Verdict);
            Assert.IsFalse(SkillSlots.Equip(Starting(), 3, Lock).Changed);
        }

        [Test]
        public void TheOriginalIsNeverModified()
        {
            // The caller holds what the caster has; an equip that edited it in place would
            // change the slots before anything wrote them.
            string[] original = Starting();
            SkillSlots.Equip(original, 0, Lock);

            CollectionAssert.AreEqual(Starting(), original);
        }

        [Test]
        public void ANullArrayIsThreeEmptySlots()
        {
            SkillEquipResult result = SkillSlots.Equip(null, 0, Arc);

            Assert.AreEqual(DesignConstants.ActiveSkillSlots, result.Slots.Length);
            Assert.AreEqual(Arc, result.Slots[0]);
        }

        [Test]
        public void FindingASkillSaysWhichSlot()
        {
            Assert.AreEqual(2, SkillSlots.IndexOf(Starting(), Shell));
            Assert.AreEqual(-1, SkillSlots.IndexOf(Starting(), Lock));
            Assert.IsTrue(SkillSlots.Holds(Starting(), Arc));
            Assert.IsFalse(SkillSlots.Holds(Starting(), Lock));
        }

        // --- the terminal ---------------------------------------------------------------------

        [Test]
        public void TheSkillTerminalIsConnectedNow()
        {
            // [확정] D-009 answered what it was waiting for. Before this it refused with
            // "미연결 · 스킬 풀 미확정".
            Assert.IsTrue(HubTerminals.IsConnected(HubTerminalKind.Skills));
            Assert.IsEmpty(HubTerminals.PendingLabel(HubTerminalKind.Skills));
            Assert.IsEmpty(HubTerminals.WhyNotConnected(HubTerminalKind.Skills));
        }

        [Test]
        public void TheDatabaseTerminalIsStillHonestlyUnconnected()
        {
            // The one that is still waiting says so, and says why.
            Assert.IsFalse(HubTerminals.IsConnected(HubTerminalKind.Database));
            Assert.IsNotEmpty(HubTerminals.PendingLabel(HubTerminalKind.Database));
            Assert.IsNotEmpty(HubTerminals.WhyNotConnected(HubTerminalKind.Database));
        }

        [Test]
        public void ThreeOfTheFourTerminalsWork()
        {
            int connected = 0;
            for (int i = 0; i < HubTerminals.All.Length; ++i)
            {
                if (HubTerminals.IsConnected(HubTerminals.All[i]))
                    ++connected;
            }

            Assert.AreEqual(4, HubTerminals.All.Length, "[확정] docs/00 fixes the set at four");
            Assert.AreEqual(3, connected);
        }
    }
}
