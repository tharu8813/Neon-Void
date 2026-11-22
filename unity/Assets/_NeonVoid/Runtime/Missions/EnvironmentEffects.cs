using NeonVoid.Missions.Generation;

namespace NeonVoid.Missions
{
    /// <summary>
    /// What an environment actually does to an area.
    /// <para>
    /// <b>The kinds were [확정] and the effects were not happening.</b> docs/00 section 7 names
    /// six environments <i>with their effects in the same sentence</i>: 스택오버플로우(적 증가),
    /// 터널감염(미학습 소지 플러그인 1~2개 비활성/역효과), 확산(맵 확대), 초기상태(맵 축소).
    /// <see cref="MissionBoard"/> has been rolling one per hardware and showing it on the gate,
    /// and nothing read it - the board offered a choice between six labels that played
    /// identically. That is the "applied but has no effect" shape this project keeps finding,
    /// and an environment is the worst place for it, because it is the thing the player is
    /// supposed to be choosing between.
    /// </para>
    /// <para>
    /// <b>The directions are the document's; the magnitudes are [초안].</b> 확산 makes the map
    /// bigger and 초기상태 makes it smaller - that is stated. By how much is not, so the numbers
    /// are named constants here and nowhere else.
    /// </para>
    /// <para>
    /// 변이발생 is not here: it already has an implementation of its own
    /// (<see cref="AreaMutation"/>), it is the one environment with a level, and folding it in
    /// would give it a second definition to drift from the first.
    /// </para>
    /// Pure C#, no Unity types.
    /// </summary>
    public readonly struct EnvironmentEffects
    {
        /// <summary>[초안] 확산: how much bigger the map gets.</summary>
        public const int DraftSpreadSizePercent = 140;

        /// <summary>[초안] 초기상태: how much smaller.</summary>
        public const int DraftInitialStateSizePercent = 70;

        /// <summary>[초안] 스택오버플로우: how many more enemies, at the same map size.</summary>
        public const int DraftStackOverflowEnemyPercent = 150;

        /// <summary>Nothing changed.</summary>
        public const int NominalPercent = 100;

        private EnvironmentEffects(int sizePercent, int enemyPercent)
        {
            SizePercent = sizePercent;
            EnemyPercent = enemyPercent;
        }

        /// <summary>The map size, as a percentage of nominal.</summary>
        public int SizePercent { get; }

        /// <summary>The enemy budget, as a percentage of what the map's size already gives.</summary>
        public int EnemyPercent { get; }

        /// <summary>Whether this environment changes anything about the area it builds.</summary>
        public bool ChangesAnything =>
            SizePercent != NominalPercent || EnemyPercent != NominalPercent;

        /// <summary>
        /// An environment that changes nothing about the map: 이상없음, 변이발생 (which has its
        /// own implementation) and 터널감염 (which changes the player, not the area).
        /// </summary>
        public static EnvironmentEffects None => new EnvironmentEffects(NominalPercent, NominalPercent);

        /// <summary>
        /// What this environment does.
        /// </summary>
        /// <param name="seed">
        /// The area's own seed. Nothing reads it yet - every effect above is a fixed [초안]
        /// magnitude - and it is a parameter because the next effect that varies per area must
        /// take it from this seed rather than a fresh one, or the area would stop being
        /// reproducible from its key.
        /// </param>
        public static EnvironmentEffects For(EnvironmentKind environment, uint seed)
        {
            switch (environment)
            {
                case EnvironmentKind.Spread:
                    return new EnvironmentEffects(DraftSpreadSizePercent, NominalPercent);

                case EnvironmentKind.InitialState:
                    return new EnvironmentEffects(DraftInitialStateSizePercent, NominalPercent);

                case EnvironmentKind.StackOverflow:
                    // Deliberately not a size change: 적 증가 is more enemies in the same room,
                    // which plays differently from a bigger map that happens to hold more.
                    return new EnvironmentEffects(NominalPercent, DraftStackOverflowEnemyPercent);

                // 터널감염 is the one environment that does nothing to the AREA: it disables
                // 미학습 소지 플러그인 1~2개, which is a change to the player. That needs a way
                // to switch a carried card off for a while, and this build has none - a card is
                // carried or it is not. Returning a count nothing could act on would be the
                // exact failure this type exists to fix, so it is left undone and said here.
                default:
                    return None;
            }
        }

        /// <summary>What this environment does to an area, from the area's conditions.</summary>
        public static EnvironmentEffects For(in AreaConditions conditions, uint seed) =>
            For(conditions.Environment, seed);

        /// <summary>
        /// The spec this environment asks for, clamped to what the generator can build.
        /// <para>
        /// Clamped rather than refused: the band exists because the generator cannot place a
        /// boss room and an entrance in an arbitrarily small grid, and an environment is not a
        /// good enough reason to hand it a size it cannot honour.
        /// </para>
        /// </summary>
        public AreaSpec ApplyTo(in AreaSpec spec)
        {
            int size = Clamp(
                spec.SizeModifierPercent * SizePercent / NominalPercent,
                AreaSpec.DraftMinSizeModifierPercent,
                AreaSpec.DraftMaxSizeModifierPercent);

            return spec.AtSize(size).WithEnemies(
                Clamp(EnemyPercent, AreaSpec.MinEnemyPercent, AreaSpec.MaxEnemyPercent));
        }

        private static int Clamp(int value, int min, int max) =>
            value < min ? min : (value > max ? max : value);

        public override string ToString() =>
            ChangesAnything ? $"맵 {SizePercent}% · 적 {EnemyPercent}%" : "영향 없음";
    }
}
