namespace NeonVoid.Plugins
{
    /// <summary>What 비상 복구 gives when it fires.</summary>
    public enum EmergencyRecoveryReading
    {
        /// <summary>회복 - heals the carrier. Worth nothing at full health.</summary>
        Heal = 0,

        /// <summary>방어 효과 - a 보호막. Worth the same whatever the carrier's health is.</summary>
        Shield = 1,
    }

    /// <summary>
    /// [미확정] docs/03 G09 비상 복구: <i>최대 체력 50%에 해당하는 회복 <b>또는 방어 효과</b></i>,
    /// with the exception column saying 회복/실드 여부 먼저 확인 필요.
    /// <para>
    /// Both readings are implemented and this constant chooses, the way
    /// <see cref="Combat.ShieldPolicy"/> held D-004's three orders until one was approved. They
    /// are not close enough to guess between: at full health a heal is worth nothing and a
    /// shield is worth all of it, so the card is either a panic button or a damage buffer
    /// depending on which is picked.
    /// </para>
    /// <para>
    /// <b>The provisional reading is 회복</b>, because the card is called 복구 and docs/03 lists
    /// it first. That is a placeholder for a decision, not the decision.
    /// </para>
    /// </summary>
    public static class EmergencyRecoveryPolicy
    {
        /// <summary>[미확정] The reading in force. One constant away from the other.</summary>
        public const EmergencyRecoveryReading Draft = EmergencyRecoveryReading.Heal;

        /// <summary>
        /// [초안] docs/03 says 최대 체력 50%에 해당하는, which is the magnitude for either
        /// reading - half the carrier's maximum health, as a heal or as a shield.
        /// </summary>
        public const float DraftMaxHealthFraction = 0.5f;

        /// <summary>[초안] How long the shield reading's 보호막 lasts. docs/03 states no time.</summary>
        public const int DraftShieldLifetimeTicks = 300;

        /// <summary>[확정] docs/03 G09: CD 60초.</summary>
        public const int StatedCooldownTicks = 3600;

        /// <summary>Whether the reading in force produces a 보호막 rather than a heal.</summary>
        public static bool IsShield => Draft == EmergencyRecoveryReading.Shield;
    }
}
