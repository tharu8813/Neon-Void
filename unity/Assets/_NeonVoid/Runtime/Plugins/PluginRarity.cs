using System;

namespace NeonVoid.Plugins
{
    /// <summary>
    /// [확정] CLAUDE.md: the four grades are 일반 / 레어 / 전설 / 신화, and the original 27 are
    /// 17 + 7 + 3. 신화 exists as a grade but its cards do not: docs/03 section 2 keeps the four
    /// mythic ideas as drafts and D-008 leaves the real list, numbers and maximum level open.
    /// <para>
    /// '소프트웨어' is never shown to the player (CLAUDE.md); the display term is 플러그인.
    /// </para>
    /// </summary>
    public enum PluginRarity
    {
        Common = 0,
        Rare = 1,
        Legendary = 2,

        /// <summary>
        /// The grade exists; no approved card does. Granting one is refused rather than
        /// supported, see <see cref="PluginLoadout"/>.
        /// </summary>
        Mythic = 3,
    }

    public static class PluginRarityExtensions
    {
        /// <summary>How many of the original 27 belong to this grade, for data checks.</summary>
        public static int OriginalCatalogCount(this PluginRarity rarity) => rarity switch
        {
            PluginRarity.Common => 17,
            PluginRarity.Rare => 7,
            PluginRarity.Legendary => 3,
            PluginRarity.Mythic => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(rarity)),
        };
    }
}
