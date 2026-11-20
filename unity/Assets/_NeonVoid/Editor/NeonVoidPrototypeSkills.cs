#if UNITY_EDITOR
using System.IO;
using NeonVoid.Combat;
using NeonVoid.Combat.Skills;
using NeonVoid.Combat.Status;
using UnityEditor;
using UnityEngine;

namespace NeonVoid.EditorTools
{
    /// <summary>
    /// Authors the prototype skill assets. docs/03 section 6 asks for two or three skills from
    /// its pool to be wired up, with the numbers replaceable at any time - so they are assets,
    /// and this is the one place that writes their draft values.
    /// <para>
    /// Three were chosen because each one reaches a rule the project had built, tested and
    /// never run in play: the cone proves cursor-aimed magic damage and a control effect
    /// shortened by tenacity, the field is the only area-and-periodic effect and therefore the
    /// only way omnivamp efficiency 0.33 is observable, and the blink is the only movement
    /// skill and therefore the only thing 속박 (Root) can deny while leaving other casts alone.
    /// </para>
    /// <para>
    /// Regeneration rewrites the values of existing assets instead of replacing them, so the
    /// GUIDs survive and the scene keeps its references.
    /// </para>
    /// </summary>
    public static class NeonVoidPrototypeSkills
    {
        public const string SkillDirectory = "Assets/_NeonVoid/Content/Skills";

        /// <summary>
        /// Writes the three prototype skill assets, in Q/E/R order, and returns their paths.
        /// <para>
        /// Paths, not references, and this is not fussiness. Creating the assets happens before
        /// the scene is built, and opening a new scene unloads assets that nothing in the scene
        /// refers to yet. The reference the creating code holds then becomes Unity's "fake
        /// null": it is still a live C# object, it passes a null check, it assigns to a
        /// serialized property without complaint, and it saves into the scene as a null. The
        /// result was three empty skill slots with a clean log and no skills in play. A path
        /// cannot go stale, so the caller loads from it once the scene exists.
        /// </para>
        /// </summary>
        public static string[] CreateOrUpdateAll()
        {
            Directory.CreateDirectory(SkillDirectory);
            // The AssetDatabase has to know the folder exists before anything is created in it.
            AssetDatabase.Refresh();

            string[] paths =
            {
                ArcPulse(),
                FirewallField(),
                PacketBlink(),
            };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return paths;
        }

        /// <summary>
        /// Loads the assets at those paths. Call this once the scene exists, immediately before
        /// the references are assigned.
        /// </summary>
        public static SkillAsset[] Load(string[] paths)
        {
            var skills = new SkillAsset[paths != null ? paths.Length : 0];
            for (int i = 0; i < skills.Length; ++i)
            {
                skills[i] = AssetDatabase.LoadAssetAtPath<SkillAsset>(paths[i]);
                if (skills[i] == null)
                    Debug.LogError($"Skill asset at {paths[i]} could not be loaded; the slot will be empty.");
            }

            return skills;
        }

        /// <summary>
        /// `skill.arc_pulse` - frontal cone of magic damage with a short slow.
        /// Verification points from docs/03: cursor aiming, mana spend, tenacity on control.
        /// </summary>
        private static string ArcPulse() => Author(
            "SK_ArcPulse",
            "skill.arc_pulse",
            "아크 펄스",
            new SkillDefinition
            {
                SkillId = 1001,
                ManaCost = 25f,
                BaseCooldownTicks = 300,       // 5 s
                IsMovementSkill = false,
                Damage = new DamageRequest
                {
                    Flat = 10f,
                    AdRatio = 0f,
                    ApRatio = 0.8f,
                    Type = DamageType.Magic,
                    CanCrit = false,
                    IsArea = true,             // several enemies can stand in one cone
                    IsPeriodic = false,
                },
            },
            new SkillShape
            {
                Kind = SkillEffectKind.ConeDamage,
                RadiusUnits = 5f,
                ConeHalfAngleDegrees = 45f,
            },
            new SkillStatusEffect
            {
                Id = StatusEffectId.Slow,
                Magnitude = 0.3f,
                DurationTicksOverride = -1,    // the catalogue duration, like every other slow
            });

        /// <summary>
        /// `skill.firewall_field` - a field that burns and slows. Its verification point in
        /// docs/03 is the one this whole skill exists for: 광역+지속 흡혈 효율 33% 한 번.
        /// </summary>
        private static string FirewallField() => Author(
            "SK_FirewallField",
            "skill.firewall_field",
            "방화벽 필드",
            new SkillDefinition
            {
                SkillId = 1002,
                ManaCost = 40f,
                BaseCooldownTicks = 480,       // 8 s
                IsMovementSkill = false,
                Damage = new DamageRequest
                {
                    Flat = 4f,
                    AdRatio = 0f,
                    ApRatio = 0.25f,
                    Type = DamageType.Magic,
                    CanCrit = false,
                    IsArea = true,
                    IsPeriodic = true,
                },
            },
            new SkillShape
            {
                Kind = SkillEffectKind.PersistentField,
                RadiusUnits = 3f,
                Field = FieldSchedule.Of(300, 30),   // 5 s, damaging twice a second
            },
            new SkillStatusEffect
            {
                Id = StatusEffectId.Slow,
                Magnitude = 0.25f,
                DurationTicksOverride = 45,    // shorter than the catalogue: it reapplies constantly
            });

        /// <summary>
        /// `skill.packet_blink` - a short teleport. Marked as a movement skill, which is what
        /// lets 속박 deny it; SkillShape.Validate refuses a teleport without that flag.
        /// </summary>
        private static string PacketBlink() => Author(
            "SK_PacketBlink",
            "skill.packet_blink",
            "패킷 점멸",
            new SkillDefinition
            {
                SkillId = 1003,
                ManaCost = 20f,
                BaseCooldownTicks = 240,       // 4 s
                IsMovementSkill = true,
                Damage = default,              // it deals none
            },
            new SkillShape
            {
                Kind = SkillEffectKind.Teleport,
                TeleportMaxDistanceUnits = 6f,
            },
            SkillStatusEffect.None);

        /// <summary>Writes one skill asset and returns its path, never a live reference.</summary>
        private static string Author(
            string fileName,
            string documentKey,
            string displayName,
            SkillDefinition definition,
            SkillShape shape,
            SkillStatusEffect status)
        {
            string path = $"{SkillDirectory}/{fileName}.asset";
            SkillAsset asset = AssetDatabase.LoadAssetAtPath<SkillAsset>(path);
            bool isNew = asset == null;

            if (isNew)
                asset = ScriptableObject.CreateInstance<SkillAsset>();

            asset.Configure(documentKey, displayName, definition, shape, status);

            if (isNew)
                AssetDatabase.CreateAsset(asset, path);
            else
                EditorUtility.SetDirty(asset);

            return path;
        }
    }
}
#endif
