using System.Collections.Generic;
using NeonVoid.Combat;
using NeonVoid.Combat.Skills;
using NeonVoid.Core;
using NUnit.Framework;
using UnityEditor;

namespace NeonVoid.Tests
{
    /// <summary>
    /// [확정] D-009, approved 2026-11-22: 스킬 풀 7종, 고정 3종으로 시작, 허브 터미널에서 교체.
    /// <para>
    /// Read off the assets the game actually loads, the way <c>ShippedInputAssetTests</c> reads
    /// the input asset: a pool that is seven in a document and six on disk is six.
    /// </para>
    /// </summary>
    public sealed class SkillPoolTests
    {
        private const string SkillDirectory = "Assets/_NeonVoid/Content/Skills";

        private static IReadOnlyList<SkillAsset> Pool()
        {
            string[] guids = AssetDatabase.FindAssets("t:SkillAsset", new[] { SkillDirectory });
            var assets = new List<SkillAsset>();

            for (int i = 0; i < guids.Length; ++i)
            {
                var asset = AssetDatabase.LoadAssetAtPath<SkillAsset>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));

                if (asset != null)
                    assets.Add(asset);
            }

            return assets;
        }

        private static SkillAsset Find(string documentKey)
        {
            IReadOnlyList<SkillAsset> pool = Pool();
            for (int i = 0; i < pool.Count; ++i)
            {
                if (pool[i].DocumentKey == documentKey)
                    return pool[i];
            }

            return null;
        }

        // --- the pool ---------------------------------------------------------------------

        [Test]
        public void ThePoolIsSeven()
        {
            // [확정] D-009: docs/03's six plus 데이터 외피.
            Assert.AreEqual(7, Pool().Count);
        }

        [Test]
        public void EverySkillTheDocumentNamesExists()
        {
            string[] wanted =
            {
                "skill.arc_pulse", "skill.firewall_field", "skill.packet_blink",
                "skill.patch_burst", "skill.cleanse", "skill.lockon",
                "skill.data_shell",
            };

            for (int i = 0; i < wanted.Length; ++i)
                Assert.IsNotNull(Find(wanted[i]), $"{wanted[i]} has no asset");
        }

        [Test]
        public void NoTwoSkillsShareAnIdOrADocumentKey()
        {
            // The id is what a save would store a learned skill under, and the key is what the
            // starting set names. Either colliding would make two skills one.
            var ids = new HashSet<int>();
            var keys = new HashSet<string>();

            foreach (SkillAsset asset in Pool())
            {
                Assert.IsTrue(ids.Add(asset.Definition.SkillId), $"{asset.name} shares a SkillId");
                Assert.IsTrue(keys.Add(asset.DocumentKey), $"{asset.name} shares a DocumentKey");
            }
        }

        [Test]
        public void EverySkillPassesItsOwnValidation()
        {
            foreach (SkillAsset asset in Pool())
            {
                Assert.DoesNotThrow(
                    () => asset.Shape.Validate(asset.Definition.IsMovementSkill), asset.name);
            }
        }

        // --- 고정 3종 ------------------------------------------------------------------------

        [Test]
        public void TheStartingSetIsThreeAndFillsTheSlots()
        {
            // [확정] docs/03 section 6: 별도 3슬롯.
            Assert.AreEqual(DesignConstants.ActiveSkillSlots, StartingSkills.DocumentKeys.Length);
            Assert.AreEqual(3, StartingSkills.DocumentKeys.Length);
        }

        [Test]
        public void EveryStartingSkillExists()
        {
            // A starting slot whose asset is missing is three skills becoming two, and the
            // player would never know which one they lost.
            for (int i = 0; i < StartingSkills.DocumentKeys.Length; ++i)
                Assert.IsNotNull(Find(StartingSkills.DocumentKeys[i]), StartingSkills.DocumentKeys[i]);
        }

        [Test]
        public void TheStartingThreeCoverAttackEscapeAndSurvival()
        {
            // [초안] which three. The set is meant to answer the three things a first run needs
            // and nothing else, so a later change that made two of them the same kind would be
            // a change worth noticing.
            Assert.AreEqual(SkillEffectKind.ConeDamage, Find(StartingSkills.Attack).Shape.Kind);
            Assert.AreEqual(SkillEffectKind.Teleport, Find(StartingSkills.Escape).Shape.Kind);
            Assert.AreEqual(SkillEffectKind.SelfShield, Find(StartingSkills.Survive).Shape.Kind);
        }

        [Test]
        public void TheStartingSetNamesItsSkillsRatherThanTakingWhateverIsFirst()
        {
            // The failure this replaces: the scene equipped authored[0..2], so adding a file
            // changed what a new vaccine started with and nothing said so.
            var keys = new HashSet<string>(StartingSkills.DocumentKeys);

            Assert.AreEqual(3, keys.Count, "the starting set repeats a skill");
            foreach (string key in keys)
                Assert.IsTrue(key.StartsWith("skill."), key);
        }

        // --- 데이터 외피 ------------------------------------------------------------------------

        [Test]
        public void TheShieldSkillGrantsAShieldThatExpires()
        {
            // [확정] D-009 added it because D-004 settled every rule a shield obeys while
            // leaving the player no way to raise one.
            SkillAsset shell = Find("skill.data_shell");

            Assert.AreEqual(SkillEffectKind.SelfShield, shell.Shape.Kind);
            Assert.Greater(shell.Shape.Field.DurationTicks, 0, "a 보호막 that never expires");
            Assert.Greater(shell.Definition.ManaCost, 0f);
            Assert.IsFalse(shell.Definition.IsMovementSkill, "속박 must not deny a shield");
        }

        [Test]
        public void AShieldSkillWithNoLifetimeIsRefused()
        {
            // Refused rather than given a default: a permanent shield is a different feature
            // from the one D-009 asked for.
            var shape = new SkillShape
            {
                Kind = SkillEffectKind.SelfShield,
                Field = new FieldSchedule { DurationTicks = 0 },
            };

            Assert.Throws<System.InvalidOperationException>(() => shape.Validate(false));
        }

        [Test]
        public void TheShieldIsSmallerThanTheHealAndCheaperToGetWrong()
        {
            // A shield that is up before the hit is worth more than a heal after it, so it is
            // deliberately the smaller number. [초안] both, and D-011 will retune them.
            SkillAsset shell = Find("skill.data_shell");
            SkillAsset patch = Find("skill.patch_burst");

            Assert.Less(shell.Definition.Damage.Flat, patch.Definition.Damage.Flat);
            Assert.Less(shell.Definition.BaseCooldownTicks, patch.Definition.BaseCooldownTicks);
        }
    }
}
