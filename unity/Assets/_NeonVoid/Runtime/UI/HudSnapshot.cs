using System;

namespace NeonVoid.UI
{
    /// <summary>One skill slot, as the HUD needs it.</summary>
    public readonly struct HudSkillSlot
    {
        public readonly bool IsEquipped;
        public readonly int CooldownRemainingTicks;
        public readonly int CooldownTotalTicks;

        public HudSkillSlot(bool isEquipped, int cooldownRemainingTicks, int cooldownTotalTicks)
        {
            IsEquipped = isEquipped;
            CooldownRemainingTicks = cooldownRemainingTicks < 0 ? 0 : cooldownRemainingTicks;
            CooldownTotalTicks = cooldownTotalTicks < 0 ? 0 : cooldownTotalTicks;
        }

        public bool IsReady => IsEquipped && CooldownRemainingTicks <= 0;

        /// <summary>
        /// 0 when the skill has just been cast, 1 when it is ready. The bar fills back up as
        /// the cooldown runs down, which is the direction a player reads as "becoming usable".
        /// </summary>
        public float ReadyFraction
        {
            get
            {
                if (!IsEquipped)
                    return 0f;

                if (CooldownTotalTicks <= 0 || CooldownRemainingTicks <= 0)
                    return 1f;

                float spent = CooldownTotalTicks - CooldownRemainingTicks;
                return HudSnapshot.Clamp01(spent / CooldownTotalTicks);
            }
        }

        /// <summary>Whole seconds left, rounded up, so 0.1s left still reads as 1.</summary>
        public int SecondsLeft =>
            CooldownRemainingTicks <= 0
                ? 0
                : (CooldownRemainingTicks + HudSnapshot.TicksPerSecond - 1) / HudSnapshot.TicksPerSecond;
    }

    /// <summary>
    /// Everything the HUD draws, as plain numbers.
    /// <para>
    /// A struct between the world and the panel so the arithmetic can be tested without a
    /// panel at all. The failure this guards against is the one this project keeps finding:
    /// a bar that is computed correctly and never written, or written and never computed -
    /// and a test that only checks "the HUD exists" catches neither.
    /// </para>
    /// <para>
    /// No Unity types. The controller gathers the values; this decides what they mean.
    /// </para>
    /// </summary>
    public readonly struct HudSnapshot
    {
        public const int TicksPerSecond = 60;

        /// <summary>How many skill slots the HUD draws. [확정] docs/03 section 6: 액티브 3슬롯.</summary>
        public const int SkillSlots = 3;

        public readonly float Health;
        public readonly float HealthMax;
        public readonly float Mana;
        public readonly float ManaMax;

        /// <summary>Total 보호막 standing, and how many layers make it up.</summary>
        public readonly float Shield;
        public readonly int ShieldLayers;

        public readonly HudSkillSlot Skill0;
        public readonly HudSkillSlot Skill1;
        public readonly HudSkillSlot Skill2;

        /// <summary>Whether the player is inside a hardware area at all.</summary>
        public readonly bool InArea;
        public readonly string AreaName;
        public readonly int ObjectivesDone;
        public readonly int ObjectivesRequired;

        /// <summary>Whether a boss is standing and fighting, so its bar is worth drawing.</summary>
        public readonly bool BossUp;
        public readonly string BossName;
        public readonly float BossHealth;
        public readonly float BossHealthMax;

        /// <summary>0 to 1 on the 정화 콘솔 and the 철수 패드.</summary>
        public readonly float PurificationProgress;
        public readonly float ExtractionProgress;
        public readonly bool ExtractionArmed;

        public HudSnapshot(
            float health, float healthMax,
            float mana, float manaMax,
            float shield, int shieldLayers,
            HudSkillSlot skill0, HudSkillSlot skill1, HudSkillSlot skill2,
            bool inArea, string areaName, int objectivesDone, int objectivesRequired,
            bool bossUp, string bossName, float bossHealth, float bossHealthMax,
            float purificationProgress, float extractionProgress, bool extractionArmed)
        {
            Health = health;
            HealthMax = healthMax;
            Mana = mana;
            ManaMax = manaMax;
            Shield = shield < 0f ? 0f : shield;
            ShieldLayers = shieldLayers < 0 ? 0 : shieldLayers;
            Skill0 = skill0;
            Skill1 = skill1;
            Skill2 = skill2;
            InArea = inArea;
            AreaName = areaName ?? string.Empty;
            ObjectivesDone = objectivesDone;
            ObjectivesRequired = objectivesRequired;
            BossUp = bossUp;
            BossName = bossName ?? string.Empty;
            BossHealth = bossHealth;
            BossHealthMax = bossHealthMax;
            PurificationProgress = Clamp01(purificationProgress);
            ExtractionProgress = Clamp01(extractionProgress);
            ExtractionArmed = extractionArmed;
        }

        public float HealthFraction => Fraction(Health, HealthMax);

        public float ManaFraction => Fraction(Mana, ManaMax);

        public float BossHealthFraction => Fraction(BossHealth, BossHealthMax);

        /// <summary>Whether any 보호막 is standing, which is when the pip is worth showing.</summary>
        public bool HasShield => Shield > 0f;

        /// <summary>The skill slot at an index, for a loop over the three.</summary>
        public HudSkillSlot SkillAt(int index)
        {
            switch (index)
            {
                case 0: return Skill0;
                case 1: return Skill1;
                case 2: return Skill2;
                default: throw new ArgumentOutOfRangeException(nameof(index), index, "There are three slots.");
            }
        }

        /// <summary>"72 / 100", the way a bar's own label reads.</summary>
        public static string Pool(float current, float max) =>
            $"{Round(current)} / {Round(max)}";

        /// <summary>
        /// "2 / 3". Shown even at 0, because an area with nothing done yet still has to say
        /// how many there are.
        /// </summary>
        public string ObjectiveLine => $"{ObjectivesDone} / {ObjectivesRequired}";

        /// <summary>
        /// The 보호막 pip's text. Layers are shown only when there is more than one, because
        /// that is the only time the number tells the player anything they cannot already see.
        /// </summary>
        public string ShieldLine =>
            ShieldLayers > 1 ? $"{Round(Shield)} ({ShieldLayers})" : Round(Shield).ToString();

        internal static float Clamp01(float value)
        {
            if (float.IsNaN(value) || value < 0f)
                return 0f;

            return value > 1f ? 1f : value;
        }

        /// <summary>
        /// A pool as a fraction. A maximum of zero reads as empty rather than as full: a bar
        /// that showed a dead or unconfigured actor at 100% would be the most misleading thing
        /// on the screen.
        /// </summary>
        private static float Fraction(float current, float max) =>
            max <= 0f || float.IsNaN(max) ? 0f : Clamp01(current / max);

        private static int Round(float value)
        {
            if (float.IsNaN(value) || value <= 0f)
                return 0;

            return (int)(value + 0.5f);
        }
    }
}
