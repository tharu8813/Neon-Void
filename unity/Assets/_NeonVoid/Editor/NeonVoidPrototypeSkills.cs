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

            // [확정] D-009 (2026-11-22): the pool is seven - docs/03's six plus 데이터 외피.
            // The caster has three slots and the hub terminal is where they are swapped, so the
            // other four are authored and waiting rather than written when somebody wants them.
            //
            // The order matters: the first three are the [초안] starting set.
            string[] paths =
            {
                ArcPulse(),
                PacketBlink(),
                DataShell(),

                FirewallField(),
                PatchBurst(),
                Cleanse(),
                LockOn(),
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
        /// <summary>
        /// `skill.patch_burst` 복구 버스트 - 자신 HP 회복. docs/03's verification points are the
        /// already-full-health case and the relationship to shields.
        /// <para>
        /// The HP half only. The shield half is D-004: multi-shield absorption, expiry and
        /// lifesteal ordering are unresolved, and a shield invented here would have to be
        /// re-decided later with saves already holding it.
        /// </para>
        /// </summary>
        private static string PatchBurst() => Author(
            "SK_PatchBurst",
            "skill.patch_burst",
            "복구 버스트",
            new SkillDefinition
            {
                SkillId = 1004,
                ManaCost = 30f,
                BaseCooldownTicks = 540,        // 9 s
                IsMovementSkill = false,
                Damage = new DamageRequest
                {
                    // Read as a magnitude rather than as damage: the heal uses the same
                    // formula, so a skill's strength scales with the same stats either way.
                    Flat = 18f,                 // [초안]
                    ApRatio = 0.5f,             // [초안]
                    Type = DamageType.Magic,
                    CanCrit = false,
                },
            },
            new SkillShape { Kind = SkillEffectKind.SelfHeal },
            SkillStatusEffect.None);

        /// <summary>
        /// `skill.data_shell` 데이터 외피 - 자신에게 보호막. [확정] D-009 (2026-11-22).
        /// <para>
        /// The answer to a boss telegraph that is not running away from it. Before this the
        /// only one was 패킷 점멸, and [확정] D-004 settled every rule a shield obeys while
        /// leaving the player unable to raise one - only three plugins could.
        /// </para>
        /// <para>
        /// Deliberately smaller than 복구 버스트 heals and on a shorter cooldown: a shield that
        /// is up before the hit is worth more than a heal after it, so it costs less to get
        /// wrong. Both numbers are [초안] and D-011 will retune them.
        /// </para>
        /// </summary>
        private static string DataShell() => Author(
            "SK_DataShell",
            "skill.data_shell",
            "데이터 외피",
            new SkillDefinition
            {
                SkillId = 1007,
                ManaCost = 25f,                 // [초안]
                BaseCooldownTicks = 420,        // [초안] 7 s
                IsMovementSkill = false,
                Damage = new DamageRequest
                {
                    // A magnitude, not damage - the shield's size uses the same formula a heal
                    // does, so it scales with the same stats.
                    Flat = 14f,                 // [초안]
                    ApRatio = 0.4f,             // [초안]
                    Type = DamageType.Magic,
                    CanCrit = false,
                },
            },
            new SkillShape
            {
                Kind = SkillEffectKind.SelfShield,

                // [초안] 5 s. Long enough to cover a telegraph the player reacted to, short
                // enough that holding it for the next fight is not the play.
                Field = new FieldSchedule { DurationTicks = 300, TickIntervalTicks = 0 },
            },
            SkillStatusEffect.None);

        /// <summary>
        /// `skill.cleanse` 정화 패킷 - 특정 제어 효과 해제.
        /// <para>
        /// 정지 and 제한 are deliberately NOT listed. Both stop the cast from happening at all,
        /// so a cleanse can never remove either, and listing an effect it could never reach
        /// would be the silent kind of lie. That boundary is exactly docs/03's
        /// 사용불가 디버프 경계 for this skill, and it stays a boundary until somebody decides
        /// that one skill may ignore the cast gate.
        /// </para>
        /// </summary>
        private static string Cleanse() => Author(
            "SK_Cleanse",
            "skill.cleanse",
            "정화 패킷",
            new SkillDefinition
            {
                SkillId = 1005,
                ManaCost = 20f,
                BaseCooldownTicks = 420,        // 7 s
                IsMovementSkill = false,
                Damage = default,
            },
            new SkillShape
            {
                Kind = SkillEffectKind.CleanseControl,
                CleansedEffects = new[]
                {
                    StatusEffectId.Slow,
                    StatusEffectId.Root,
                    StatusEffectId.Malfunction,
                },
            },
            SkillStatusEffect.None);

        /// <summary>
        /// `skill.lockon` 표적 지정 - 적에게 물리 단일 타격 + 방어 디버프.
        /// <para>
        /// The debuff is 방화벽 차단, the same one G12 applies, so a lock-on followed by basic
        /// attacks hurts more - which is the point of a single-target skill in a game where
        /// everything else is a cone or a field.
        /// </para>
        /// </summary>
        private static string LockOn() => Author(
            "SK_LockOn",
            "skill.lockon",
            "표적 지정",
            new SkillDefinition
            {
                SkillId = 1006,
                ManaCost = 25f,
                BaseCooldownTicks = 360,        // 6 s
                IsMovementSkill = false,
                Damage = new DamageRequest
                {
                    Flat = 6f,                  // [초안]
                    AdRatio = 1.4f,             // [초안]
                    Type = DamageType.Physical,
                    CanCrit = true,             // a single target hit is the natural crit case
                },
            },
            new SkillShape
            {
                Kind = SkillEffectKind.SingleTarget,
                RadiusUnits = 9f,               // [초안] how far it can lock on
            },
            new SkillStatusEffect
            {
                Id = StatusEffectId.FirewallBreach,
                Magnitude = 5f,                 // [초안] resistance points, as G12 uses
                DurationTicksOverride = 240,    // [초안] 4 s, the same as G12's mark
            });

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
