using System;
using NeonVoid.Missions.Generation;

namespace NeonVoid.Missions
{
    /// <summary>
    /// The seed each hardware area is generated from, derived from the mission's own.
    /// <para>
    /// docs/04 section 5 lists <c>EnterHardware(area, seed, genVer)</c> with the retry policy
    /// 동일 seed/genVer 재사용, and docs/01 section 6 check 4 makes
    /// <c>(seed, hardwareId, generatorVersion)</c> the key a map must be reproducible from. Both
    /// need the area's seed to be a function of the mission's rather than something rolled when
    /// the portal is walked through: a seed drawn at entry could not be re-used by a retry, and
    /// re-entering an area would rebuild it differently.
    /// </para>
    /// <para>
    /// <b>Why not just the mission seed plus the hardware's index.</b> An xorshift started one
    /// apart produces visibly similar early values, so CPU and GPU would generate maps that felt
    /// like each other - the same mistake <c>RunIdentity</c> documents for its own streams. The
    /// hardware is mixed in, not added.
    /// </para>
    /// Pure C#: no Unity types, no state.
    /// </summary>
    public static class AreaSeeds
    {
        /// <summary>
        /// An odd constant with a well-spread bit pattern - the golden ratio in 32 bits, as
        /// xorshift mixers conventionally use. Any odd constant works; this one is recognisable,
        /// which matters more than its exact value.
        /// </summary>
        private const uint Mix = 0x9E3779B9u;

        /// <summary>The seed this hardware area's map is generated from.</summary>
        public static uint For(uint missionSeed, HardwareId hardware)
        {
            if (!HardwareIds.IsArea(hardware))
                throw new ArgumentOutOfRangeException(nameof(hardware), hardware, "Not a hardware area.");

            unchecked
            {
                uint mixed = missionSeed ^ (uint)((int)hardware * (int)Mix);
                mixed ^= mixed << 13;
                mixed ^= mixed >> 17;
                mixed ^= mixed << 5;

                // Zero is a legitimate seed for the generator, which guards it - but two areas
                // landing on it would share a map, so it is nudged.
                return mixed == 0u ? Mix : mixed;
            }
        }

        /// <summary>
        /// The whole spec for one area of one mission: its seed, its objective count and its size.
        /// </summary>
        /// <param name="missionSeed">The run's <see cref="RandomStream.Mission"/> seed.</param>
        /// <param name="hardware">Which area.</param>
        /// <param name="objectiveCount">
        /// 소목표 for this area. [초안] docs/01 gives 2~3; <see cref="DraftObjectiveCountFor"/>
        /// rolls it from the same seed when the caller has no opinion.
        /// </param>
        public static AreaSpec SpecFor(uint missionSeed, HardwareId hardware, int objectiveCount) =>
            new AreaSpec(hardware, For(missionSeed, hardware), objectiveCount);

        /// <summary>
        /// [초안] 소목표 for this area, from its own seed, inside docs/01's 2~3.
        /// <para>
        /// <b>No size modifier.</b> docs/01 section 6 check 5 mentions 맵 크기 축소·확대 환경, but
        /// none of section 5's six [확정] environment kinds names a size change - so which
        /// environment shrinks a map is not something the documents say, and picking one here
        /// would be inventing it. Areas are built at nominal size until an approved environment
        /// asks otherwise; the generator's whole size band is exercised by its own tests.
        /// </para>
        /// </summary>
        public static int DraftObjectiveCountFor(uint missionSeed, HardwareId hardware)
        {
            uint seed = For(missionSeed, hardware);
            int span = HardwareIds.DraftMaxObjectivesPerArea - HardwareIds.DraftMinObjectivesPerArea + 1;

            // Taken from the high bits: the low bit of an xorshift state is the least mixed, and
            // a 2~3 choice made from it would correlate with the map the same state generates.
            return HardwareIds.DraftMinObjectivesPerArea + (int)((seed >> 16) % (uint)span);
        }

        /// <summary>The spec with the objective count rolled from the seed.</summary>
        public static AreaSpec DraftSpecFor(uint missionSeed, HardwareId hardware) =>
            SpecFor(missionSeed, hardware, DraftObjectiveCountFor(missionSeed, hardware));
    }
}
