using System;
using NeonVoid.Core;

namespace NeonVoid.Missions
{
    /// <summary>
    /// 변이발생: which of an area's enemies the environment has strengthened, and by how much.
    /// <para>
    /// [확정] docs/01 section 5: <i>변이발생: 일부 개체 능력 상승/일반·레어 플러그인 1~2개 획득.</i>
    /// The first half is what this is. The second half - the plugins - needs a reward commit
    /// that D-006 and D-007 still hold open, so a Mutation area does not hand anything out.
    /// </para>
    /// <para>
    /// <b>일부, and a test holds that it stays 일부.</b> The whole point of the environment is
    /// that a mutated area reads differently while you are in it: if every enemy were boosted,
    /// the player would be fighting a tougher enemy type rather than a changed room, which is
    /// what difficulty would do - and docs/01 section 3 leaves 난이도에 따른 HP/피해 배율
    /// [미확정] precisely so that nobody ships it by accident.
    /// </para>
    /// <para>
    /// <b>정예 are never mutated on top.</b> docs/01 section 4 already describes
    /// <c>enemy.elite</c> as 환경 변이·강인함 - the elite IS the mutated one - so stacking the
    /// environment onto it would multiply the same idea twice and produce a Lv.5 elite nothing
    /// in the pacing budget accounts for.
    /// </para>
    /// <para>
    /// <b>It changes no XP.</b> GDD v1 section 5.4 fixes an area's 의뢰 XP budget by cause -
    /// 8 per normal kill, 32 per 정예 - and a mutated enemy is still a normal kill. An
    /// environment that paid more would make 변이발생 the farming environment and quietly
    /// double some areas' budgets.
    /// </para>
    /// Pure C#: no Unity types, no state.
    /// </summary>
    public static class AreaMutation
    {
        /// <summary>No mutation. <see cref="EnvironmentKind.Clear"/> and the other four kinds.</summary>
        public const int NoMutation = 0;

        /// <summary>
        /// [초안] share of an area's normal enemies that mutate, per 변이 단계. Lv.1 touches a
        /// tenth of them and Lv.5 half - enough that a high level is unmistakable, little
        /// enough that most of the room still reads as the area's own enemies.
        /// </summary>
        public const int DraftMutatedPercentPerLevel = 10;

        /// <summary>
        /// [초안] a mutated enemy's health, as a percentage added per 변이 단계. Lv.5 is +75%,
        /// which is below the 정예 multiplier on purpose: a mutated normal is a harder normal,
        /// not a second elite.
        /// </summary>
        public const int DraftHealthPercentPerLevel = 15;

        /// <summary>[초안] a mutated enemy's attack damage, added per 변이 단계. Lv.5 is +50%.</summary>
        public const int DraftDamagePercentPerLevel = 10;

        /// <summary>
        /// [초안] the share that may never be exceeded whatever the level. The draft curve stops
        /// at 50%, so this is not reachable today - it is here so that retuning
        /// <see cref="DraftMutatedPercentPerLevel"/> upward cannot quietly turn 일부 into 전부.
        /// </summary>
        public const int MaxMutatedPercent = 60;

        /// <summary>How many of <paramref name="normalEnemies"/> this level mutates.</summary>
        public static int MutatedCount(int normalEnemies, int mutationLevel)
        {
            if (normalEnemies <= 0 || mutationLevel <= NoMutation)
                return 0;

            RequireLevel(mutationLevel);

            int percent = DraftMutatedPercentPerLevel * mutationLevel;
            if (percent > MaxMutatedPercent)
                percent = MaxMutatedPercent;

            // Round half up, then keep at least one: an area announced as 변이발생 Lv.1 with
            // nothing mutated in it would be a lie told by the portal.
            int count = ((normalEnemies * percent) + 50) / 100;
            if (count < 1)
                count = 1;

            return count > normalEnemies ? normalEnemies : count;
        }

        /// <summary>
        /// Which spawns mutate, as a flag per spawn, chosen deterministically.
        /// <para>
        /// <b>Deterministic from the area's own seed</b>, so the same
        /// <c>(seed, hardwareId, generatorVersion)</c> plus the same environment produces the
        /// same room - docs/01 section 6 check 4. It is a separate decision from the terrain on
        /// purpose: the generator never sees the environment, so a Clear area and a Mutation
        /// area of the same seed are the same map with different enemies in it, which is what
        /// lets a player recognise a hardware they have played before.
        /// </para>
        /// </summary>
        /// <param name="isElite">
        /// One flag per spawn. Elites are skipped - see the type's note - and they are not
        /// counted toward the share either, so 변이 Lv.3 means three tenths of the normals.
        /// </param>
        public static bool[] Choose(uint areaSeed, bool[] isElite, int mutationLevel)
        {
            if (isElite == null)
                throw new ArgumentNullException(nameof(isElite));

            var mutated = new bool[isElite.Length];
            if (mutationLevel <= NoMutation)
                return mutated;

            RequireLevel(mutationLevel);

            // The normals, in spawn order.
            var candidates = new int[isElite.Length];
            int normals = 0;
            for (int i = 0; i < isElite.Length; ++i)
            {
                if (!isElite[i])
                    candidates[normals++] = i;
            }

            int wanted = MutatedCount(normals, mutationLevel);
            if (wanted <= 0)
                return mutated;

            // A separate stream from the one the generator used, mixed with the level, so two
            // levels of the same seed do not mutate a nested subset - Lv.2 should not be Lv.1
            // plus one more, or the environment would read as a dial rather than a condition.
            var rng = new DeterministicRng(MixSeed(areaSeed, mutationLevel));

            // Fisher-Yates over the candidate indices, then take the first `wanted`. Shuffling
            // rather than rolling per enemy, so the count is exact: a per-enemy roll would make
            // 'Lv.1 mutated nothing this time' a thing that happens.
            for (int i = normals - 1; i > 0; --i)
            {
                int j = rng.NextInt(0, i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            for (int i = 0; i < wanted; ++i)
                mutated[candidates[i]] = true;

            return mutated;
        }

        /// <summary>[초안] a mutated enemy's health multiplier.</summary>
        public static float HealthMultiplier(int mutationLevel) =>
            Multiplier(mutationLevel, DraftHealthPercentPerLevel);

        /// <summary>[초안] a mutated enemy's attack damage multiplier.</summary>
        public static float DamageMultiplier(int mutationLevel) =>
            Multiplier(mutationLevel, DraftDamagePercentPerLevel);

        /// <summary>The 변이 단계 these conditions carry, or 0.</summary>
        public static int LevelOf(in AreaConditions conditions) =>
            conditions.Environment == EnvironmentKind.Mutation ? conditions.MutationLevel : NoMutation;

        private static float Multiplier(int mutationLevel, int percentPerLevel)
        {
            if (mutationLevel <= NoMutation)
                return 1f;

            RequireLevel(mutationLevel);
            return 1f + ((percentPerLevel * mutationLevel) / 100f);
        }

        private static uint MixSeed(uint areaSeed, int mutationLevel)
        {
            unchecked
            {
                uint mixed = areaSeed ^ (uint)(mutationLevel * 0x85EBCA6B);
                mixed ^= mixed << 13;
                mixed ^= mixed >> 17;
                mixed ^= mixed << 5;
                return mixed == 0u ? 0x85EBCA6Bu : mixed;
            }
        }

        private static void RequireLevel(int mutationLevel)
        {
            if (mutationLevel < AreaConditions.MinMutationLevel || mutationLevel > AreaConditions.MaxMutationLevel)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mutationLevel), mutationLevel,
                    $"[확정] 변이발생 is Lv.{AreaConditions.MinMutationLevel}~{AreaConditions.MaxMutationLevel}.");
            }
        }
    }
}
