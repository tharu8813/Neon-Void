using System;
using NeonVoid.Core;

namespace NeonVoid.Missions
{
    /// <summary>
    /// The five hardware areas. [확정] docs/01: CPU/GPU/RAM/SSD/HDD, and C01 in docs/08 settles
    /// that these replace the old four OS sectors rather than being them renamed.
    /// <para>
    /// An enum is safe here, unlike for plugins: docs/03's plugin ids are explicitly
    /// <i>proposal</i> ids awaiting an approved stable id, so writing one into a save would make
    /// it permanent by accident. These five are the approved content of the game.
    /// </para>
    /// </summary>
    public enum HardwareId
    {
        /// <summary>No area. The value a defaulted field has, never a place to be.</summary>
        None = 0,

        /// <summary>수비·타깃 우선순위. 코어 워처.</summary>
        Cpu = 1,

        /// <summary>회피·조준·공간 읽기. 렌더 옵저버.</summary>
        Gpu = 2,

        /// <summary>진위 구분·적응. 메모리 리치.</summary>
        Ram = 3,

        /// <summary>빠른 이동·구간 연결. 스트림 인터셉터.</summary>
        Ssd = 4,

        /// <summary>위치 관리·장기전. 섹터 콜로서스.</summary>
        Hdd = 5,
    }

    /// <summary>
    /// The hardware list and the keys it is recorded under.
    /// <para>
    /// <b>The save keys are written out, not derived from the enum names.</b> A purification lives
    /// in permanent state as the receipt <c>cleanse:{runId}:{areaKey}</c>, so an area's key is
    /// part of a file somebody's save already holds. Deriving it from
    /// <see cref="Enum.ToString()"/> would mean that renaming the enum member - a refactor with
    /// no apparent consequences - silently made every committed purification belong to an area
    /// that no longer exists, and the player would be able to purify it again. The mapping is
    /// therefore explicit and a test pins the exact strings.
    /// </para>
    /// </summary>
    public static class HardwareIds
    {
        /// <summary>
        /// Every area, in the order docs/01 section 2 lists them. The order is for display and
        /// iteration only: [확정] docs/01 says 입장 순서는 자유.
        /// </summary>
        public static readonly HardwareId[] All =
        {
            HardwareId.Cpu, HardwareId.Gpu, HardwareId.Ram, HardwareId.Ssd, HardwareId.Hdd,
        };

        /// <summary>
        /// [초안] docs/01 section 1: 2~3개 소목표 per area. The flow refuses an area with none -
        /// docs/01 section 6.6 requires the boss and the extraction to stay shut until the
        /// objectives are done, and zero objectives would open both immediately - but the count
        /// itself is a draft the area plan supplies.
        /// </summary>
        public const int DraftMinObjectivesPerArea = 2;

        /// <summary>[초안] docs/01 section 1: the upper end of 2~3개 소목표.</summary>
        public const int DraftMaxObjectivesPerArea = 3;

        /// <summary>The key this area is recorded under in permanent state. Never renamed.</summary>
        public static string SaveKeyFor(HardwareId area)
        {
            switch (area)
            {
                case HardwareId.Cpu: return "cpu";
                case HardwareId.Gpu: return "gpu";
                case HardwareId.Ram: return "ram";
                case HardwareId.Ssd: return "ssd";
                case HardwareId.Hdd: return "hdd";
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(area), area, "Only a real hardware area has a save key.");
            }
        }

        /// <summary>Whether this is one of the five areas rather than <see cref="HardwareId.None"/>.</summary>
        public static bool IsArea(HardwareId area) =>
            area != HardwareId.None && Enum.IsDefined(typeof(HardwareId), area);

        /// <summary>
        /// Holds the list against <see cref="DesignConstants.HardwareCount"/>. Called by a test;
        /// the two are [확정] statements of the same fact and must not drift.
        /// </summary>
        public static bool CountMatchesDesign => All.Length == DesignConstants.HardwareCount;
    }
}
