using System;
using NeonVoid.Core;

namespace NeonVoid.Missions
{
    /// <summary>
    /// What a mission offers at the motherboard: the five hardware areas, each with a difficulty
    /// and an environment.
    /// </summary>
    public readonly struct MissionBoardPlan
    {
        private readonly AreaConditions[] _areas;

        /// <summary>
        /// Which area was forced to Easy by the 쉬움 보장 rule, or -1 when the roll already had
        /// one. Kept so the correction is visible - a guarantee that quietly rewrote a roll would
        /// be indistinguishable from a roll that never needed it.
        /// </summary>
        public readonly int CorrectedIndex;

        public MissionBoardPlan(AreaConditions[] areas, int correctedIndex)
        {
            _areas = areas ?? throw new ArgumentNullException(nameof(areas));
            CorrectedIndex = correctedIndex;
        }

        /// <summary>The areas, in <see cref="HardwareIds.All"/>'s order.</summary>
        public AreaConditions this[int index] => _areas[index];

        public int Count => _areas.Length;

        /// <summary>This area's conditions.</summary>
        public AreaConditions For(HardwareId hardware)
        {
            for (int i = 0; i < _areas.Length; ++i)
            {
                if (_areas[i].Hardware == hardware)
                    return _areas[i];
            }

            throw new ArgumentOutOfRangeException(nameof(hardware), hardware, "Not on this board.");
        }

        /// <summary>How many areas are Easy. [확정] never zero.</summary>
        public int EasyCount
        {
            get
            {
                int easy = 0;
                for (int i = 0; i < _areas.Length; ++i)
                {
                    if (_areas[i].Difficulty == HardwareDifficulty.Easy)
                        ++easy;
                }

                return easy;
            }
        }

        /// <summary>Whether the 쉬움 보장 rule had to step in.</summary>
        public bool WasCorrected => CorrectedIndex >= 0;

        public override string ToString()
        {
            var parts = new string[_areas.Length];
            for (int i = 0; i < _areas.Length; ++i)
                parts[i] = _areas[i].GateLine();

            string corrected = WasCorrected ? $" (쉬움 보장: {_areas[CorrectedIndex].Hardware})" : string.Empty;
            return string.Join(" | ", parts) + corrected;
        }
    }

    /// <summary>
    /// Picks each hardware area's difficulty and environment from the mission seed, and keeps
    /// docs/01 section 5's 쉬움 보장 promise.
    /// <para>
    /// The document states the algorithm and its test: <i>missionSeed로 5개 하드웨어 난이도 선정 →
    /// 결과에 Easy가 하나도 없으면 결정적인 규칙으로 1개를 Easy로 보정(어느 하드웨어인지는 시드
    /// RNG로 선택). 10,000개 시드에서 최소 1개 항상 통과해야 함. 난이도를 전부 Easy로 강제하지
    /// 않음.</i> That is implemented literally, including the last clause: the correction touches
    /// exactly one area, so a mission that rolled four Hard areas keeps all four.
    /// </para>
    /// <para>
    /// <b>Selection only. Nothing here has an effect yet.</b> A difficulty cannot change enemy
    /// health or damage, because 난이도에 따른 실제 HP/피해 배율 is [미확정] in docs/01 section 3;
    /// 터널감염 cannot disable a plugin, because the document asks for 학습 출전분을 예외로 하는
    /// 상태 태그 that the loadout does not have. Building either would be shipping an undecided
    /// rule. What this does is give the motherboard something true to show and the rest something
    /// real to read when those are decided.
    /// </para>
    /// <para>
    /// Seeded from the run's <see cref="RandomStream.Mission"/> stream like the map, and on its
    /// own generator: [확정] docs/06 section 4 keeps the streams apart so that drawing a reward
    /// cannot change what the board offers.
    /// </para>
    /// Pure C#: no Unity types, no storage.
    /// </summary>
    public static class MissionBoard
    {
        // [초안] difficulty weights. The 쉬움 보장 rule is [확정]; how often Easy comes up on its
        // own is a tuning dial, and D-011 owns the difficulty economy.
        public const int DraftEasyWeight = 30;
        public const int DraftNormalWeight = 45;
        public const int DraftHardWeight = 25;

        // [초안] environment weights, over docs/01's six [확정] kinds. 이상없음 is the most common
        // because a mission where every area is afflicted leaves nowhere to recover.
        public const int DraftClearWeight = 30;
        public const int DraftMutationWeight = 25;
        public const int DraftStackOverflowWeight = 12;
        public const int DraftTunnelInfectionWeight = 12;
        public const int DraftSpreadWeight = 11;
        public const int DraftInitialStateWeight = 10;

        /// <summary>
        /// The board for one mission. The same seed always gives the same board - the motherboard
        /// panel, the hardware gates and whatever later saves the mission all have to agree about
        /// what was offered.
        /// </summary>
        public static MissionBoardPlan Select(uint missionSeed)
        {
            var rng = new DeterministicRng(missionSeed);
            var areas = new AreaConditions[DesignConstants.HardwareCount];

            for (int i = 0; i < areas.Length; ++i)
            {
                HardwareId hardware = HardwareIds.All[i];
                HardwareDifficulty difficulty = RollDifficulty(rng);
                EnvironmentKind environment = RollEnvironment(rng);
                int mutationLevel = environment == EnvironmentKind.Mutation
                    ? rng.NextInt(AreaConditions.MinMutationLevel, AreaConditions.MaxMutationLevel + 1)
                    : 0;

                areas[i] = new AreaConditions(hardware, difficulty, environment, mutationLevel);
            }

            // 쉬움 보장. The roll is consumed unconditionally rather than only when it is needed,
            // so the generator's position does not depend on the outcome - otherwise two seeds
            // that differed only in whether they needed the correction would diverge in
            // everything drawn afterwards, and the board would stop being reproducible from its
            // own key alone.
            int chosen = rng.NextInt(0, areas.Length);
            int corrected = -1;

            if (!HasEasy(areas))
            {
                areas[chosen] = areas[chosen].At(HardwareDifficulty.Easy);
                corrected = chosen;
            }

            return new MissionBoardPlan(areas, corrected);
        }

        /// <summary>
        /// Holds docs/01 section 5's promise on one board. Called by the selector's tests and
        /// available to anything that reads a board it did not make.
        /// </summary>
        public static bool KeepsTheEasyGuarantee(in MissionBoardPlan plan) => plan.EasyCount >= 1;

        private static bool HasEasy(AreaConditions[] areas)
        {
            for (int i = 0; i < areas.Length; ++i)
            {
                if (areas[i].Difficulty == HardwareDifficulty.Easy)
                    return true;
            }

            return false;
        }

        private static HardwareDifficulty RollDifficulty(DeterministicRng rng)
        {
            int roll = rng.NextInt(0, DraftEasyWeight + DraftNormalWeight + DraftHardWeight);
            if (roll < DraftEasyWeight)
                return HardwareDifficulty.Easy;

            return roll < DraftEasyWeight + DraftNormalWeight
                ? HardwareDifficulty.Normal
                : HardwareDifficulty.Hard;
        }

        private static EnvironmentKind RollEnvironment(DeterministicRng rng)
        {
            int total = DraftClearWeight + DraftMutationWeight + DraftStackOverflowWeight
                        + DraftTunnelInfectionWeight + DraftSpreadWeight + DraftInitialStateWeight;
            int roll = rng.NextInt(0, total);

            int cumulative = DraftClearWeight;
            if (roll < cumulative)
                return EnvironmentKind.Clear;

            cumulative += DraftMutationWeight;
            if (roll < cumulative)
                return EnvironmentKind.Mutation;

            cumulative += DraftStackOverflowWeight;
            if (roll < cumulative)
                return EnvironmentKind.StackOverflow;

            cumulative += DraftTunnelInfectionWeight;
            if (roll < cumulative)
                return EnvironmentKind.TunnelInfection;

            cumulative += DraftSpreadWeight;
            if (roll < cumulative)
                return EnvironmentKind.Spread;

            return EnvironmentKind.InitialState;
        }
    }
}
