using System;

namespace NeonVoid.Missions.Generation
{
    /// <summary>
    /// What one hardware area is generated from. docs/01 section 6 names the fields it must
    /// carry; these are the ones that have a meaning today.
    /// <para>
    /// <b>What is missing and why.</b> The document's list also has <c>difficultyId</c>,
    /// <c>environmentIds</c> and <c>rewardRolls</c>. Nothing selects a difficulty or an
    /// environment yet - that is docs/01 section 5's 쉬움 보장 algorithm, which is its own piece -
    /// and the reward table is undecided (D-006). A field for each of them would be a field every
    /// caller fills with a placeholder, and a placeholder that travels in a generation key is
    /// worse than an absence: it would make <c>(seed, hardwareId, generatorVersion)</c> look like
    /// it already covered things it does not. They join when their selectors exist, and
    /// <see cref="GeneratorVersion"/> is what records that the map changed when they do.
    /// </para>
    /// Pure C#: no Unity types, no randomness of its own.
    /// </summary>
    public readonly struct AreaSpec
    {
        /// <summary>
        /// Bumped whenever a change would move terrain or spawns for the same seed. docs/01
        /// section 6 check 4 makes <c>(seed, hardwareId, generatorVersion)</c> the key that must
        /// reproduce a map, so the version is what lets a receipt say which generator built it.
        /// </summary>
        public const int CurrentGeneratorVersion = 1;

        /// <summary>100 is the nominal size. docs/01 allows 맵 크기 축소·확대 as an environment.</summary>
        public const int NominalSizeModifierPercent = 100;

        /// <summary>[초안] the smallest 축소 the generator will build.</summary>
        public const int DraftMinSizeModifierPercent = 60;

        /// <summary>[초안] the largest 확대 the generator will build.</summary>
        public const int DraftMaxSizeModifierPercent = 160;

        /// <summary>[초안] docs/01 section 1 gives 2~3개 소목표 per area.</summary>
        public const int DraftMinObjectives = HardwareIds.DraftMinObjectivesPerArea;

        /// <summary>[초안] the upper end of 2~3개 소목표.</summary>
        public const int DraftMaxObjectives = HardwareIds.DraftMaxObjectivesPerArea;

        /// <summary>A hard ceiling, so a caller's bad number fails here and not as a vast map.</summary>
        public const int MaxObjectives = 6;

        public readonly HardwareId Hardware;
        public readonly int GeneratorVersion;
        public readonly uint Seed;

        /// <summary>Percent of the nominal map size. 100 is nominal.</summary>
        public readonly int SizeModifierPercent;

        /// <summary>
        /// 소목표 to place, each in its own room. The number the area will then require of the
        /// player, so it is an input rather than something the generator chooses: the mission
        /// decides how long an area is, and the map obeys.
        /// </summary>
        public readonly int ObjectiveCount;

        public AreaSpec(
            HardwareId hardware,
            uint seed,
            int objectiveCount,
            int sizeModifierPercent = NominalSizeModifierPercent,
            int generatorVersion = CurrentGeneratorVersion)
        {
            if (!HardwareIds.IsArea(hardware))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(hardware), hardware, "An area is generated for one of the five hardware areas.");
            }

            if (objectiveCount < 1 || objectiveCount > MaxObjectives)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(objectiveCount), objectiveCount,
                    $"An area holds 1 to {MaxObjectives} objectives; docs/01 drafts {DraftMinObjectives}~{DraftMaxObjectives}. " +
                    "None would open the boss on entry (section 6.6).");
            }

            if (sizeModifierPercent < DraftMinSizeModifierPercent || sizeModifierPercent > DraftMaxSizeModifierPercent)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sizeModifierPercent), sizeModifierPercent,
                    $"[초안] the generator builds between {DraftMinSizeModifierPercent}% and " +
                    $"{DraftMaxSizeModifierPercent}% of nominal size.");
            }

            if (generatorVersion < 1)
                throw new ArgumentOutOfRangeException(nameof(generatorVersion), generatorVersion, "Versions start at 1.");

            Hardware = hardware;
            Seed = seed;
            ObjectiveCount = objectiveCount;
            SizeModifierPercent = sizeModifierPercent;
            GeneratorVersion = generatorVersion;
        }

        /// <summary>
        /// The same area at another size, for the 맵 크기 축소·확대 environments. Everything else
        /// is kept, so the pair differs by exactly the thing under test.
        /// </summary>
        public AreaSpec AtSize(int sizeModifierPercent) =>
            new AreaSpec(Hardware, Seed, ObjectiveCount, sizeModifierPercent, GeneratorVersion);

        /// <summary>The same area from another seed.</summary>
        public AreaSpec WithSeed(uint seed) =>
            new AreaSpec(Hardware, seed, ObjectiveCount, SizeModifierPercent, GeneratorVersion);

        /// <summary>
        /// The key docs/01 section 6 check 4 requires a map to be reproducible from. Written as
        /// text so a receipt and a log can carry it unchanged.
        /// </summary>
        public string GenerationKey =>
            $"{HardwareIds.SaveKeyFor(Hardware)}:{Seed}:v{GeneratorVersion}:size{SizeModifierPercent}:obj{ObjectiveCount}";

        public override string ToString() => GenerationKey;
    }
}
