using System;
using System.Collections.Generic;
using NeonVoid.Combat;

namespace NeonVoid.Progression
{
    /// <summary>
    /// What a 영구 능력치 포인트 can be spent on. GDD v1 section 13.1's table, one entry per row -
    /// except the two 관통 rows, which each say 선택한 종류 and are therefore two choices.
    /// </summary>
    public enum InvestableStat
    {
        MaxHealth = 0,
        AttackDamage = 1,
        AbilityPower = 2,
        PhysicalArmor = 3,
        MagicResistance = 4,
        MoveSpeed = 5,
        AttacksPerSecond = 6,
        ProjectileSpeed = 7,
        CritChance = 8,
        CritDamage = 9,
        SkillHaste = 10,
        Luck = 11,
        MaxMana = 12,
        ManaRegen = 13,
        ProjectileSize = 14,
        PhysicalPenFlat = 15,
        MagicPenFlat = 16,
        PhysicalPenPercent = 17,
        MagicPenPercent = 18,
        OnHitLifesteal = 19,
        Omnivamp = 20,
        Tenacity = 21,
    }

    /// <summary>One stat's row in the table.</summary>
    public readonly struct StatInvestmentStep
    {
        public readonly InvestableStat Stat;

        /// <summary>
        /// The key this stat is written under in a save file. Stable forever: a player's
        /// allocation is stored against it, and renaming one would silently move their points
        /// onto a different stat or lose them.
        /// </summary>
        public readonly string SaveKey;

        /// <summary>What the terminal calls it, in the document's own words.</summary>
        public readonly string DisplayName;

        /// <summary>[초안] GDD v1 section 13.1's 1회 증가량.</summary>
        public readonly float DraftIncrementPerStep;

        /// <summary>[초안] GDD v1 section 13.1's 소비 포인트.</summary>
        public readonly int DraftPointCost;

        /// <summary>
        /// Whether spending a point here changes anything yet. See
        /// <see cref="StatInvestments.WhyNotConnected"/>; an investment in a stat nothing
        /// applies is refused rather than taken.
        /// </summary>
        public readonly bool IsConnected;

        internal StatInvestmentStep(
            InvestableStat stat, string saveKey, string displayName, float increment, int cost,
            bool connected)
        {
            Stat = stat;
            SaveKey = saveKey;
            DisplayName = displayName;
            DraftIncrementPerStep = increment;
            DraftPointCost = cost;
            IsConnected = connected;
        }

        public override string ToString() =>
            $"{DisplayName} +{DraftIncrementPerStep} / {DraftPointCost}p{(IsConnected ? string.Empty : " (미연결)")}";
    }

    /// <summary>One stat's share of a vaccine's allocation.</summary>
    public readonly struct VaccineStatInvestment
    {
        public readonly InvestableStat Stat;

        /// <summary>How many times it was invested in. The effect is this times the increment.</summary>
        public readonly int Steps;

        /// <summary>
        /// Points actually paid. Stored beside the steps rather than derived from them, and
        /// that is deliberate: [초안] costs will be retuned (D-011), and a refund computed from
        /// today's cost would hand back more or less than was paid. 재분배는 무료 means the
        /// player gets their points back, not a different number of them.
        /// </summary>
        public readonly int PointsSpent;

        public VaccineStatInvestment(InvestableStat stat, int steps, int pointsSpent)
        {
            Stat = stat;
            Steps = steps;
            PointsSpent = pointsSpent;
        }

        public override string ToString() =>
            $"{StatInvestments.SaveKeyOf(Stat)} x{Steps} ({PointsSpent}p)";
    }

    /// <summary>
    /// 영구 능력치 포인트 투자: the table, what each step is worth, and how an allocation reaches
    /// the player's stats.
    /// <para>
    /// [확정] docs/00 section 8 and GDD v1 section 4.3: 백신 레벨업마다 영구 능력치 포인트를
    /// 지급하고, 허브에서 재분배 비용 없이 언제든 초기화·재투자할 수 있다. The numbers are all
    /// [초안] - GDD v1 section 13.1 says so itself, and D-011 holds the balance open - so they
    /// live here as named constants rather than anywhere they would be mistaken for approved.
    /// </para>
    /// <para>
    /// <b>Six of the twenty-two rows are refused, not quietly stored.</b> 이동속도, 발사속도,
    /// 탄환속도, 행운, 최대 마나, 초당 마나 재생 and 탄환 크기 are real rows in the table, but
    /// they are not fields of <see cref="CombatStats"/> - they live in the motion config, the
    /// auto-attacker and the mana pool, each of which reads its own serialized value rather
    /// than the actor's stat block. Taking a player's point for one of them would spend
    /// something they cannot get back on nothing at all, so the table carries the row, says why
    /// it is not connected, and the command refuses it.
    /// </para>
    /// <para>
    /// <b>Application order.</b> docs/02 section 2: base → E 승인 영구 투자 → 플러그인/버프·디버프
    /// → 캡·검증 → 최종 전투 스냅샷, recomputed from the original rather than accumulated. This
    /// produces the second arrow, as a pure function of the first, so a re-entry cannot apply
    /// the same investment twice.
    /// </para>
    /// Pure C#: no Unity types beyond the stat block, no state, no storage.
    /// </summary>
    public static class StatInvestments
    {
        // [초안] GDD v1 section 13.1, 1회 증가량.
        public const float DraftMaxHealthPerStep = 5f;
        public const float DraftAttackDamagePerStep = 0.25f;
        public const float DraftAbilityPowerPerStep = 1f;
        public const float DraftArmorPerStep = 2f;
        public const float DraftMagicResistPerStep = 2f;
        public const float DraftMoveSpeedPerStep = 0.05f;
        public const float DraftAttacksPerSecondPerStep = 0.05f;
        public const float DraftProjectileSpeedPerStep = 0.02f;
        public const float DraftCritChancePerStep = 0.5f;
        public const float DraftCritDamagePerStep = 1f;
        public const float DraftSkillHastePerStep = 2f;
        public const float DraftLuckPerStep = 0.5f;
        public const float DraftMaxManaPerStep = 5f;
        public const float DraftManaRegenPerStep = 0.25f;
        public const float DraftProjectileSizePerStep = 1f;
        public const float DraftFlatPenPerStep = 0.5f;
        public const float DraftPercentPenPerStep = 0.25f;
        public const float DraftOnHitLifestealPerStep = 0.25f;
        public const float DraftOmnivampPerStep = 0.25f;
        public const float DraftTenacityPerStep = 1f;

        // [초안] GDD v1 section 13.1, 소비 포인트.
        public const int DraftOrdinaryCost = 1;
        public const int DraftUncommonCost = 2;
        public const int DraftOmnivampCost = 3;

        /// <summary>
        /// 치명타 피해 is a multiplier in the stat block and %p in the table, so a step is one
        /// percentage point expressed as hundredths. Named rather than written as 0.01 at the
        /// call site, because the two units looking alike is how a 100x error gets in.
        /// </summary>
        public const float CritDamagePercentPointsToMultiplier = 0.01f;

        /// <summary>
        /// 비율 관통 is %p in the table and a 0..1 fraction in the stat block, for the same
        /// reason.
        /// </summary>
        public const float PercentPointsToFraction = 0.01f;

        private static readonly StatInvestmentStep[] Table =
        {
            Row(InvestableStat.MaxHealth, "maxHealth", "최대 체력", DraftMaxHealthPerStep, DraftOrdinaryCost, true),
            Row(InvestableStat.AttackDamage, "attackDamage", "공격력", DraftAttackDamagePerStep, DraftOrdinaryCost, true),
            Row(InvestableStat.AbilityPower, "abilityPower", "주문력", DraftAbilityPowerPerStep, DraftOrdinaryCost, true),
            Row(InvestableStat.PhysicalArmor, "physicalArmor", "물리 방어력", DraftArmorPerStep, DraftOrdinaryCost, true),
            Row(InvestableStat.MagicResistance, "magicResistance", "마법 방어력", DraftMagicResistPerStep, DraftOrdinaryCost, true),
            Row(InvestableStat.MoveSpeed, "moveSpeed", "이동속도", DraftMoveSpeedPerStep, DraftOrdinaryCost, false),
            Row(InvestableStat.AttacksPerSecond, "attacksPerSecond", "발사속도", DraftAttacksPerSecondPerStep, DraftOrdinaryCost, false),
            Row(InvestableStat.ProjectileSpeed, "projectileSpeed", "탄환속도", DraftProjectileSpeedPerStep, DraftOrdinaryCost, false),
            Row(InvestableStat.CritChance, "critChance", "치명타 확률", DraftCritChancePerStep, DraftOrdinaryCost, true),
            Row(InvestableStat.CritDamage, "critDamage", "치명타 피해", DraftCritDamagePerStep, DraftOrdinaryCost, true),
            Row(InvestableStat.SkillHaste, "skillHaste", "스킬 가속", DraftSkillHastePerStep, DraftOrdinaryCost, true),
            Row(InvestableStat.Luck, "luck", "행운", DraftLuckPerStep, DraftOrdinaryCost, false),
            Row(InvestableStat.MaxMana, "maxMana", "최대 마나", DraftMaxManaPerStep, DraftOrdinaryCost, false),
            Row(InvestableStat.ManaRegen, "manaRegen", "초당 마나 재생", DraftManaRegenPerStep, DraftOrdinaryCost, false),
            Row(InvestableStat.ProjectileSize, "projectileSize", "탄환 크기", DraftProjectileSizePerStep, DraftUncommonCost, false),
            Row(InvestableStat.PhysicalPenFlat, "physicalPenFlat", "고정 물리 관통", DraftFlatPenPerStep, DraftUncommonCost, true),
            Row(InvestableStat.MagicPenFlat, "magicPenFlat", "고정 마법 관통", DraftFlatPenPerStep, DraftUncommonCost, true),
            Row(InvestableStat.PhysicalPenPercent, "physicalPenPercent", "비율 물리 관통", DraftPercentPenPerStep, DraftUncommonCost, true),
            Row(InvestableStat.MagicPenPercent, "magicPenPercent", "비율 마법 관통", DraftPercentPenPerStep, DraftUncommonCost, true),
            Row(InvestableStat.OnHitLifesteal, "onHitLifesteal", "적중시 흡혈", DraftOnHitLifestealPerStep, DraftUncommonCost, true),
            Row(InvestableStat.Omnivamp, "omnivamp", "모든 피해 흡혈", DraftOmnivampPerStep, DraftOmnivampCost, true),
            Row(InvestableStat.Tenacity, "tenacity", "강인함", DraftTenacityPerStep, DraftUncommonCost, true),
        };

        /// <summary>Every row of GDD v1 section 13.1, connected or not.</summary>
        public static StatInvestmentStep[] All => Table;

        public static StatInvestmentStep For(InvestableStat stat)
        {
            for (int i = 0; i < Table.Length; ++i)
            {
                if (Table[i].Stat == stat)
                    return Table[i];
            }

            throw new ArgumentOutOfRangeException(nameof(stat), stat, "Not an investable stat.");
        }

        public static string SaveKeyOf(InvestableStat stat) => For(stat).SaveKey;

        public static int CostOf(InvestableStat stat) => For(stat).DraftPointCost;

        public static bool IsConnected(InvestableStat stat) => For(stat).IsConnected;

        /// <summary>The stat a save key names, or false when this build does not know it.</summary>
        public static bool TryParse(string saveKey, out InvestableStat stat)
        {
            for (int i = 0; i < Table.Length; ++i)
            {
                if (string.Equals(Table[i].SaveKey, saveKey, StringComparison.Ordinal))
                {
                    stat = Table[i].Stat;
                    return true;
                }
            }

            stat = default;
            return false;
        }

        /// <summary>
        /// Why spending a point here would buy nothing yet, or empty when it buys something.
        /// </summary>
        public static string WhyNotConnected(InvestableStat stat)
        {
            if (IsConnected(stat))
                return string.Empty;

            switch (stat)
            {
                case InvestableStat.MoveSpeed:
                    return "이동속도는 CombatStats가 아니라 PlayerMotionConfig가 들고 있다";
                case InvestableStat.AttacksPerSecond:
                case InvestableStat.ProjectileSpeed:
                case InvestableStat.ProjectileSize:
                    return "발사·탄환 수치는 AutoAttacker의 직렬화 값이고 전투 스탯 블록에 없다";
                case InvestableStat.Luck:
                    return "행운은 보상 확률에 쓰이는데 보상 테이블이 미확정이다 (D-006)";
                case InvestableStat.MaxMana:
                case InvestableStat.ManaRegen:
                    return "마나는 ManaPool이 자기 값을 들고 있고 전투 스탯 블록에 없다";
                default:
                    return "이 수치를 읽는 곳이 아직 없다";
            }
        }

        /// <summary>
        /// docs/02 section 2's second arrow: base → 영구 투자. A pure function of the base
        /// block, so re-entering an area recomputes rather than accumulating - the document
        /// asks for 원본 기반 재계산 by name.
        /// </summary>
        public static CombatStats Apply(in CombatStats baseStats, IReadOnlyList<VaccineStatInvestment> allocation)
        {
            CombatStats result = baseStats;
            if (allocation == null)
                return result;

            for (int i = 0; i < allocation.Count; ++i)
            {
                VaccineStatInvestment invested = allocation[i];
                if (invested.Steps <= 0)
                    continue;

                StatInvestmentStep row = For(invested.Stat);
                if (!row.IsConnected)
                {
                    // Unreachable through the save command, which refuses these. Skipped rather
                    // than thrown so that a save written by a later build - one where the row IS
                    // connected - still loads in this one with that row doing nothing.
                    continue;
                }

                float amount = row.DraftIncrementPerStep * invested.Steps;
                switch (invested.Stat)
                {
                    case InvestableStat.MaxHealth: result.MaxHealth += amount; break;
                    case InvestableStat.AttackDamage: result.AttackDamage += amount; break;
                    case InvestableStat.AbilityPower: result.AbilityPower += amount; break;
                    case InvestableStat.PhysicalArmor: result.PhysicalArmor += amount; break;
                    case InvestableStat.MagicResistance: result.MagicResistance += amount; break;
                    case InvestableStat.CritChance: result.CritChancePercent += amount; break;
                    case InvestableStat.CritDamage:
                        result.CritDamageMultiplier += amount * CritDamagePercentPointsToMultiplier;
                        break;
                    case InvestableStat.SkillHaste: result.SkillHaste += amount; break;
                    case InvestableStat.PhysicalPenFlat: result.PhysicalPenFlat += amount; break;
                    case InvestableStat.MagicPenFlat: result.MagicPenFlat += amount; break;
                    case InvestableStat.PhysicalPenPercent:
                        result.PhysicalPenPercent += amount * PercentPointsToFraction;
                        break;
                    case InvestableStat.MagicPenPercent:
                        result.MagicPenPercent += amount * PercentPointsToFraction;
                        break;
                    case InvestableStat.OnHitLifesteal: result.OnHitLifestealPercent += amount; break;
                    case InvestableStat.Omnivamp: result.OmnivampPercent += amount; break;
                    case InvestableStat.Tenacity: result.TenacityPercent += amount; break;

                    default:
                        throw new InvalidOperationException(
                            $"{invested.Stat} says it is connected but nothing applies it.");
                }
            }

            return result;
        }

        private static StatInvestmentStep Row(
            InvestableStat stat, string key, string name, float increment, int cost, bool connected) =>
            new StatInvestmentStep(stat, key, name, increment, cost, connected);
    }
}
