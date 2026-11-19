namespace NeonVoid.Core
{
    /// <summary>
    /// Confirmed design invariants only. All gameplay balance numbers must live in data.
    /// This is a contract header, not a complete 60 Hz combat simulation.
    /// </summary>
    public static class DesignConstants
    {
        public const int SimulationTicksPerSecond = 60;
        public const int HardwareCount = 5;
        public const int InitialCopies = 3;
        public const int ActiveSkillSlots = 3;
        public const int RewardCardSlots = 3;
        public const int InitialRewardRerolls = 3;
        public const int HighTierPityStartsAfterMissedScreens = 5;
        public const int LearnablePluginsPerSuccessfulRun = 2;
        public const float AreaOrPeriodicOmnivampEfficiency = 0.33f;
    }
}
