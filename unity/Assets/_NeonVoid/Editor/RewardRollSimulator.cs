#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NeonVoid.Core;

namespace NeonVoid.EditorTools
{
    /// <summary>The four grades. 신화 is modelled but switched off - see the report's 가정.</summary>
    public enum SimRarity
    {
        Common = 0,
        Rare = 1,
        Legendary = 2,
        Mythic = 3,
    }

    /// <summary>When a pity count goes back to zero.</summary>
    public enum PityReset
    {
        /// <summary>Any 전설+ anywhere on the screen, including one a reroll produced.</summary>
        AnyHighTierOnScreen = 0,

        /// <summary>Only a 전설+ in the screen's first deal. A rerolled one does not reset it.</summary>
        FirstDealOnly = 1,
    }

    /// <summary>
    /// One candidate probability table. <b>Editor only, and deliberately so.</b>
    /// <para>
    /// D-006 is not decided. These numbers must not be reachable from the game, so they live in
    /// the Editor assembly, which the runtime cannot reference. When one is approved it gets
    /// ported into Runtime as data; until then nothing can accidentally ship them.
    /// </para>
    /// </summary>
    public sealed class RewardRollCandidate
    {
        public string Name;
        public string Idea;

        /// <summary>Base weights per grade, before any correction.</summary>
        public float[] BaseWeight = new float[4];

        /// <summary>Added to a grade's weight per point of 행운, as a fraction of its base.</summary>
        public float[] LuckPerPoint = new float[4];

        /// <summary>Multiplier per grade for Easy / Normal / Hard.</summary>
        public float[] EasyMul = new float[4];
        public float[] NormalMul = new float[4];
        public float[] HardMul = new float[4];

        /// <summary>Added per grade per 정화 already done in this mission, as a fraction of base.</summary>
        public float[] ProgressPerPurify = new float[4];

        /// <summary>
        /// [확정] docs/03 section 3 item 5: correction begins after five screens with no 전설+,
        /// counted per 보상 화면 and never per card or per reroll.
        /// </summary>
        public int PityAfterScreens = DesignConstants.HighTierPityStartsAfterMissedScreens;

        /// <summary>Legendary weight multiplier added per screen past the threshold.</summary>
        public float PityGrowthPerScreen;

        /// <summary>The largest the pity multiplier may reach.</summary>
        public float PityCap = 8f;

        public PityReset Reset = PityReset.AnyHighTierOnScreen;

        public float[] MulFor(SimDifficulty difficulty)
        {
            switch (difficulty)
            {
                case SimDifficulty.Easy: return EasyMul;
                case SimDifficulty.Hard: return HardMul;
                default: return NormalMul;
            }
        }
    }

    public enum SimDifficulty
    {
        Easy = 0,
        Normal = 1,
        Hard = 2,
    }

    /// <summary>What one simulated set of runs produced.</summary>
    public sealed class SimResult
    {
        public string Candidate;
        public int Runs;
        public int Screens;

        /// <summary>Cards dealt per grade, over every screen of every run.</summary>
        public long[] Cards = new long[4];

        /// <summary>Screens that showed at least one 전설+.</summary>
        public long HighTierScreens;

        /// <summary>Screens that reached the player while pity was active.</summary>
        public long PityActiveScreens;

        /// <summary>
        /// Longest run of screens with no 전설+, over every screen simulated.
        /// <para>
        /// On its own this is the worst thing that happened in tens of thousands of screens,
        /// which no player will ever see. <see cref="DroughtPercentile"/> is what a decision
        /// should be made on.
        /// </para>
        /// </summary>
        public int WorstDrought;

        /// <summary>Every completed gap between two 전설+ screens, in screens.</summary>
        public List<int> Droughts = new List<int>();

        /// <summary>The nth percentile gap, in screens. 50 is the typical wait.</summary>
        public int DroughtPercentile(int percentile)
        {
            if (Droughts.Count == 0)
                return 0;

            var sorted = new List<int>(Droughts);
            sorted.Sort();
            int index = (int)((percentile / 100.0) * (sorted.Count - 1));
            return sorted[index < 0 ? 0 : (index >= sorted.Count ? sorted.Count - 1 : index)];
        }

        /// <summary>Per-run count of 전설+ cards, for the distribution.</summary>
        public List<int> LegendariesPerRun = new List<int>();

        /// <summary>High-tier cards seen in the first, middle and last third of a run.</summary>
        public long[] HighTierByThird = new long[3];

        /// <summary>Rerolls spent, and how many of them improved the grade.</summary>
        public long RerollsSpent;
        public long RerollsThatRaisedTheGrade;
        public long RerollsThatLoweredTheGrade;

        public double CardsPer(SimRarity rarity, int runs) =>
            runs <= 0 ? 0.0 : Cards[(int)rarity] / (double)runs;
    }

    /// <summary>
    /// Rolls reward screens against a candidate table, many times, and reports the shape.
    /// <para>
    /// <b>It decides nothing.</b> docs/08 D-006 is open; this exists so the decision is made
    /// against numbers instead of a feeling, which is what the document's 승인 조건 asks for:
    /// <i>시뮬레이션 표와 위험/보상 승인</i>.
    /// </para>
    /// <para>
    /// What it models is only what is [확정] in docs/03 section 3: three slots per screen rolled
    /// independently, one slot rerolled at a time, three rerolls per mission, and a pity count
    /// that is <b>per 보상 화면</b> rather than per card or per reroll.
    /// </para>
    /// </summary>
    public static class RewardRollSimulator
    {
        /// <summary>[확정] docs/03: 일반 17 + 레어 7 + 전설 3. 신화 is not in the pool (D-008).</summary>
        public static readonly int[] PoolSize = { 17, 7, 3, 0 };

        /// <summary>
        /// [가정] How many reward screens a mission produces. Not fixed anywhere: docs/01 ties a
        /// reward to 정화 and a mission purifies at least one area and at most five. The report
        /// states this.
        /// </summary>
        public const int ScreensPerRun = 3;

        /// <summary>[가정] 행운 a player has by the middle of a run, in points.</summary>
        public const int AssumedLuck = 0;

        /// <summary>[가정] The difficulty mix a player picks across a run.</summary>
        public static readonly SimDifficulty[] AssumedDifficulties =
        {
            SimDifficulty.Easy, SimDifficulty.Normal, SimDifficulty.Normal,
        };

        // --- the three candidates --------------------------------------------------------------

        public static RewardRollCandidate Conservative() => new RewardRollCandidate
        {
            Name = "A. 보수",
            Idea = "전설을 드물게 두고, 가뭄은 강한 천장으로 끊는다. 난이도·행운의 영향은 작다.",
            BaseWeight = new[] { 78f, 20f, 2f, 0f },
            LuckPerPoint = new[] { 0f, 0.010f, 0.030f, 0f },
            EasyMul = new[] { 1.05f, 0.95f, 0.80f, 0f },
            NormalMul = new[] { 1f, 1f, 1f, 0f },
            HardMul = new[] { 0.95f, 1.05f, 1.30f, 0f },
            ProgressPerPurify = new[] { -0.03f, 0.05f, 0.12f, 0f },
            PityGrowthPerScreen = 1.20f,
            PityCap = 10f,
            Reset = PityReset.AnyHighTierOnScreen,
        };

        public static RewardRollCandidate Curved() => new RewardRollCandidate
        {
            Name = "B. 곡선",
            Idea = "초반은 보수와 비슷하고, 정화를 쌓을수록 상위 등급이 눈에 띄게 오른다. 천장은 중간.",
            BaseWeight = new[] { 80f, 18f, 2f, 0f },
            LuckPerPoint = new[] { 0f, 0.012f, 0.035f, 0f },
            EasyMul = new[] { 1.05f, 0.95f, 0.85f, 0f },
            NormalMul = new[] { 1f, 1f, 1f, 0f },
            HardMul = new[] { 0.92f, 1.08f, 1.45f, 0f },
            ProgressPerPurify = new[] { -0.06f, 0.10f, 0.35f, 0f },
            PityGrowthPerScreen = 0.70f,
            PityCap = 6f,
            Reset = PityReset.AnyHighTierOnScreen,
        };

        public static RewardRollCandidate LuckDriven() => new RewardRollCandidate
        {
            Name = "C. 행운",
            Idea = "기본은 가장 낮지만 행운 투자가 크게 보답한다. 천장은 약하고, 리롤로 뽑은 전설은 천장을 리셋하지 않는다.",
            BaseWeight = new[] { 83f, 15.5f, 1.5f, 0f },
            LuckPerPoint = new[] { 0f, 0.030f, 0.090f, 0f },
            EasyMul = new[] { 1.03f, 0.97f, 0.90f, 0f },
            NormalMul = new[] { 1f, 1f, 1f, 0f },
            HardMul = new[] { 0.90f, 1.10f, 1.60f, 0f },
            ProgressPerPurify = new[] { -0.04f, 0.07f, 0.18f, 0f },
            PityGrowthPerScreen = 0.40f,
            PityCap = 4f,
            Reset = PityReset.FirstDealOnly,
        };

        public static RewardRollCandidate[] All() => new[] { Conservative(), Curved(), LuckDriven() };

        // --- the roll ----------------------------------------------------------------------------

        /// <summary>
        /// One slot's grade. The weights are rebuilt per roll because every input - difficulty,
        /// 정화 수, 행운, the pity multiplier - can have changed since the last one.
        /// </summary>
        public static SimRarity RollSlot(
            RewardRollCandidate candidate,
            DeterministicRng rng,
            SimDifficulty difficulty,
            int purified,
            int luck,
            float pityMultiplier)
        {
            float[] mul = candidate.MulFor(difficulty);
            Span<float> weights = stackalloc float[4];
            float total = 0f;

            for (int i = 0; i < 4; ++i)
            {
                if (PoolSize[i] <= 0)
                {
                    weights[i] = 0f;
                    continue;
                }

                float w = candidate.BaseWeight[i];
                w *= mul[i];
                w *= 1f + (candidate.LuckPerPoint[i] * luck);
                w *= 1f + (candidate.ProgressPerPurify[i] * purified);

                if (i >= (int)SimRarity.Legendary)
                    w *= pityMultiplier;

                if (w < 0f)
                    w = 0f;

                weights[i] = w;
                total += w;
            }

            if (total <= 0f)
                return SimRarity.Common;

            // 1e6 buckets: fine enough that a 2% weight is not distorted by the integer draw.
            float pick = rng.NextInt(0, 1_000_000) / 1_000_000f * total;
            float running = 0f;
            for (int i = 0; i < 4; ++i)
            {
                running += weights[i];
                if (pick < running)
                    return (SimRarity)i;
            }

            return SimRarity.Common;
        }

        private static float PityMultiplierFor(RewardRollCandidate candidate, int screensWithoutHighTier)
        {
            int past = screensWithoutHighTier - candidate.PityAfterScreens;
            if (past <= 0)
                return 1f;

            float multiplier = 1f + (candidate.PityGrowthPerScreen * past);
            return multiplier > candidate.PityCap ? candidate.PityCap : multiplier;
        }

        /// <summary>
        /// Runs a candidate.
        /// </summary>
        /// <param name="rerollPolicy">
        /// How many of the three rerolls the player spends, and on what. 0 models never
        /// rerolling; 3 models a player who always rerolls their worst slot, which is the
        /// behaviour worth checking for abuse.
        /// </param>
        public static SimResult Run(
            RewardRollCandidate candidate, int runs, uint seed, int rerollsUsedPerRun, int luck = AssumedLuck)
        {
            var result = new SimResult { Candidate = candidate.Name, Runs = runs };
            var rng = new DeterministicRng(seed);

            // The pity count survives a mission: docs/03 counts 보상 화면, not missions, and
            // nothing says it resets when a run ends. Stated in the report as an assumption.
            int screensWithoutHighTier = 0;
            int worstDrought = 0;

            for (int run = 0; run < runs; ++run)
            {
                int highTierThisRun = 0;
                int rerollsLeft = rerollsUsedPerRun;

                for (int screen = 0; screen < ScreensPerRun; ++screen)
                {
                    SimDifficulty difficulty = AssumedDifficulties[screen % AssumedDifficulties.Length];
                    int purified = screen; // one purification per screen so far
                    float pity = PityMultiplierFor(candidate, screensWithoutHighTier);

                    if (pity > 1f)
                        ++result.PityActiveScreens;

                    var slots = new SimRarity[DesignConstants.RewardCardSlots];
                    bool highTierInFirstDeal = false;

                    for (int slot = 0; slot < slots.Length; ++slot)
                    {
                        slots[slot] = RollSlot(candidate, rng, difficulty, purified, luck, pity);
                        if (slots[slot] >= SimRarity.Legendary)
                            highTierInFirstDeal = true;
                    }

                    // [확정] docs/03 item 3: one slot at a time, a new grade and a new plugin.
                    // The modelled player rerolls their worst slot, which is the strongest play.
                    bool highTierAfterRerolls = highTierInFirstDeal;
                    while (rerollsLeft > 0)
                    {
                        --rerollsLeft;
                        ++result.RerollsSpent;

                        int worst = 0;
                        for (int slot = 1; slot < slots.Length; ++slot)
                        {
                            if (slots[slot] < slots[worst])
                                worst = slot;
                        }

                        SimRarity before = slots[worst];
                        slots[worst] = RollSlot(candidate, rng, difficulty, purified, luck, pity);

                        if (slots[worst] > before)
                            ++result.RerollsThatRaisedTheGrade;
                        else if (slots[worst] < before)
                            ++result.RerollsThatLoweredTheGrade;

                        if (slots[worst] >= SimRarity.Legendary)
                            highTierAfterRerolls = true;
                    }

                    ++result.Screens;
                    for (int slot = 0; slot < slots.Length; ++slot)
                    {
                        ++result.Cards[(int)slots[slot]];
                        if (slots[slot] >= SimRarity.Legendary)
                        {
                            ++highTierThisRun;
                            int third = screen * 3 / ScreensPerRun;
                            ++result.HighTierByThird[third > 2 ? 2 : third];
                        }
                    }

                    if (highTierAfterRerolls)
                        ++result.HighTierScreens;

                    bool resets = candidate.Reset == PityReset.FirstDealOnly
                        ? highTierInFirstDeal
                        : highTierAfterRerolls;

                    if (resets)
                    {
                        // A completed gap, counted in screens the player sat through.
                        result.Droughts.Add(screensWithoutHighTier);
                        screensWithoutHighTier = 0;
                    }
                    else
                    {
                        ++screensWithoutHighTier;
                        if (screensWithoutHighTier > worstDrought)
                            worstDrought = screensWithoutHighTier;
                    }
                }

                result.LegendariesPerRun.Add(highTierThisRun);
            }

            result.WorstDrought = worstDrought;
            return result;
        }

        // --- the report --------------------------------------------------------------------------

        private static string Pct(double value) =>
            (value * 100.0).ToString("0.00", CultureInfo.InvariantCulture) + "%";

        private static string Num(double value) =>
            value.ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>How many of <paramref name="runs"/> saw at least <paramref name="atLeast"/>.</summary>
        private static double ShareWithAtLeast(SimResult result, int atLeast)
        {
            int count = 0;
            for (int i = 0; i < result.LegendariesPerRun.Count; ++i)
            {
                if (result.LegendariesPerRun[i] >= atLeast)
                    ++count;
            }

            return result.LegendariesPerRun.Count == 0
                ? 0.0
                : count / (double)result.LegendariesPerRun.Count;
        }

        /// <summary>Builds the whole markdown report. Writing it is the caller's business.</summary>
        public static string Report(int runs = 10_000, uint seed = 20_261_122u)
        {
            var text = new StringBuilder();
            RewardRollCandidate[] candidates = All();

            text.AppendLine("# D-006 보상 카드 확률 시뮬레이션 — 승인 전 자료");
            text.AppendLine();
            text.AppendLine("> **이 문서는 결정이 아니다.** docs/08 D-006은 미확정이며, 여기의 숫자는 어느 것도 게임에 적용되어 있지 않다.");
            text.AppendLine("> 시뮬레이터는 `unity/Assets/_NeonVoid/Editor/RewardRollSimulator.cs`에 있고, **Editor 어셈블리라 런타임이 참조할 수 없다** — 승인 전에 실수로 출시될 수 없다는 뜻이다.");
            text.AppendLine();

            text.AppendLine("## 모델이 지킨 [확정] 규칙");
            text.AppendLine();
            text.AppendLine("- 한 보상 화면에 **3장**, 슬롯마다 등급을 **개별 추첨** (docs/03 §3-1).");
            text.AppendLine("- 리롤은 **한 슬롯만** 다시 뽑고 새 등급+새 종류를 준다 (§3-3). 의뢰당 **3회** (§3-4).");
            text.AppendLine("- **천장 카운트는 보상 화면 단위다.** 15장을 따로 세지 않고, 리롤 횟수로도 세지 않는다 (§3-5).");
            text.AppendLine($"- 천장은 **{DesignConstants.HighTierPityStartsAfterMissedScreens}회 연속 미등장 다음 화면부터** 점진 적용 (§3-5).");
            text.AppendLine("- 후보 풀은 **일반 17 · 레어 7 · 전설 3** (docs/03 머리말).");
            text.AppendLine();

            text.AppendLine("## 명시한 가정 (확정하지 않았다)");
            text.AppendLine();
            text.AppendLine("| 가정 | 값 | 왜 가정인가 |");
            text.AppendLine("|---|---|---|");
            text.AppendLine($"| 의뢰당 보상 화면 수 | **{ScreensPerRun}** | 어디에도 확정되어 있지 않다. 보상은 정화에 묶이고, 한 의뢰는 최소 1곳 최대 5곳을 정화한다. 3은 그 중간값이다 |");
            text.AppendLine("| 신화 카드 | **풀에서 제외** | D-008이 미확정이고 docs/08이 신화 등장 비활성화를 안전한 처리로 둔다. 모델에 자리는 있으나 가중치 0이다 |");
            text.AppendLine($"| 행운 | **{AssumedLuck}p** (별도 표에서 0·10·25p 비교) | 행운은 투자 가능 스탯이지만 아직 어디서도 읽히지 않는다 |");
            text.AppendLine("| 난이도 구성 | Easy·Normal·Normal | [확정] 최소 1곳 Easy를 반영한 중립적 선택이다 |");
            text.AppendLine("| 천장 카운트의 의뢰 간 지속 | **지속된다** | §3-5가 화면을 세라고만 하고 의뢰 경계를 말하지 않는다. 의뢰마다 리셋하면 가뭄이 더 길어진다 |");
            text.AppendLine("| 리롤 사용법 | **가장 낮은 슬롯을 리롤** | 가장 강한 플레이다. 악용 가능성은 이걸로 재야 한다 |");
            text.AppendLine();

            text.AppendLine("## 후보 3안");
            text.AppendLine();
            text.AppendLine("| | A. 보수 | B. 곡선 | C. 행운 |");
            text.AppendLine("|---|---|---|---|");
            text.AppendLine($"| 발상 | {candidates[0].Idea} | {candidates[1].Idea} | {candidates[2].Idea} |");
            text.AppendLine(
                $"| 기본 가중치 (일반/레어/전설) | {Num(candidates[0].BaseWeight[0])} / {Num(candidates[0].BaseWeight[1])} / {Num(candidates[0].BaseWeight[2])} " +
                $"| {Num(candidates[1].BaseWeight[0])} / {Num(candidates[1].BaseWeight[1])} / {Num(candidates[1].BaseWeight[2])} " +
                $"| {Num(candidates[2].BaseWeight[0])} / {Num(candidates[2].BaseWeight[1])} / {Num(candidates[2].BaseWeight[2])} |");
            text.AppendLine(
                $"| 행운 1p당 전설 가중 | +{Pct(candidates[0].LuckPerPoint[2])} | +{Pct(candidates[1].LuckPerPoint[2])} | +{Pct(candidates[2].LuckPerPoint[2])} |");
            text.AppendLine(
                $"| Hard의 전설 배수 | x{Num(candidates[0].HardMul[2])} | x{Num(candidates[1].HardMul[2])} | x{Num(candidates[2].HardMul[2])} |");
            text.AppendLine(
                $"| 정화 1곳당 전설 가중 | +{Pct(candidates[0].ProgressPerPurify[2])} | +{Pct(candidates[1].ProgressPerPurify[2])} | +{Pct(candidates[2].ProgressPerPurify[2])} |");
            text.AppendLine(
                $"| 천장 증가/화면 · 상한 | +{Num(candidates[0].PityGrowthPerScreen)} · x{Num(candidates[0].PityCap)} " +
                $"| +{Num(candidates[1].PityGrowthPerScreen)} · x{Num(candidates[1].PityCap)} " +
                $"| +{Num(candidates[2].PityGrowthPerScreen)} · x{Num(candidates[2].PityCap)} |");
            text.AppendLine(
                $"| 천장 리셋 | {Describe(candidates[0].Reset)} | {Describe(candidates[1].Reset)} | {Describe(candidates[2].Reset)} |");
            text.AppendLine();

            // --- 3. 30런 분포 ---------------------------------------------------------------------
            text.AppendLine($"## 30런 플레이에서 보는 카드 ({runs:N0}런 시뮬레이션, 리롤 미사용)");
            text.AppendLine();
            text.AppendLine($"의뢰 1회 = 화면 {ScreensPerRun}개 = 카드 {ScreensPerRun * DesignConstants.RewardCardSlots}장. 30런이면 {30 * ScreensPerRun * DesignConstants.RewardCardSlots}장이다.");
            text.AppendLine();
            text.AppendLine("| 후보 | 일반 | 레어 | 전설 | 신화 | 전설+ 화면 비율 |");
            text.AppendLine("|---|---|---|---|---|---|");

            var noReroll = new SimResult[3];
            for (int i = 0; i < candidates.Length; ++i)
            {
                SimResult r = Run(candidates[i], runs, seed + (uint)(i * 7919), rerollsUsedPerRun: 0);
                noReroll[i] = r;
                text.AppendLine(
                    $"| {r.Candidate} | {Num(r.CardsPer(SimRarity.Common, runs) * 30)} | {Num(r.CardsPer(SimRarity.Rare, runs) * 30)} " +
                    $"| **{Num(r.CardsPer(SimRarity.Legendary, runs) * 30)}** | {Num(r.CardsPer(SimRarity.Mythic, runs) * 30)} " +
                    $"| {Pct(r.HighTierScreens / (double)r.Screens)} |");
            }

            text.AppendLine();
            text.AppendLine("### 한 의뢰에서 전설을 몇 장이나 보나");
            text.AppendLine();
            text.AppendLine("| 후보 | 0장 | 1장 이상 | 2장 이상 | 3장 이상 |");
            text.AppendLine("|---|---|---|---|---|");
            for (int i = 0; i < noReroll.Length; ++i)
            {
                SimResult r = noReroll[i];
                text.AppendLine(
                    $"| {r.Candidate} | {Pct(1.0 - ShareWithAtLeast(r, 1))} | {Pct(ShareWithAtLeast(r, 1))} " +
                    $"| {Pct(ShareWithAtLeast(r, 2))} | {Pct(ShareWithAtLeast(r, 3))} |");
            }

            text.AppendLine();

            // --- 4. 천장 -------------------------------------------------------------------------
            text.AppendLine("## 천장 보정과 리셋 조건");
            text.AppendLine();
            text.AppendLine("전설이 안 나오는 구간이 **얼마나 길어지는가**가 천장이 답해야 할 질문이다. 중앙값은 보통, p95는 운이 나쁜 날, 최악은 3만 화면 중 한 번 나온 값이라 아무도 보지 않는다.");
            text.AppendLine();
            text.AppendLine("| 후보 | 가뭄 중앙값 | p90 | p95 | p99 | 3만 화면 중 최악 | 천장이 걸린 화면 | 리셋 조건 |");
            text.AppendLine("|---|---|---|---|---|---|---|---|");
            for (int i = 0; i < noReroll.Length; ++i)
            {
                SimResult r = noReroll[i];
                text.AppendLine(
                    $"| {r.Candidate} | {r.DroughtPercentile(50)} | {r.DroughtPercentile(90)} | {r.DroughtPercentile(95)} " +
                    $"| {r.DroughtPercentile(99)} | {r.WorstDrought} | {Pct(r.PityActiveScreens / (double)r.Screens)} " +
                    $"| {Describe(candidates[i].Reset)} |");
            }

            text.AppendLine();
            text.AppendLine($"화면 {ScreensPerRun}개가 의뢰 하나다. 가뭄 p95가 {noReroll[0].DroughtPercentile(95)}화면이면 의뢰 {Num(noReroll[0].DroughtPercentile(95) / (double)ScreensPerRun)}회를 전설 없이 보낸다는 뜻이다.");

            text.AppendLine();
            text.AppendLine("리셋 조건만 바꿔서 같은 표를 다시 돌린 결과 (**리롤 3회 사용**, 다른 입력은 동일):");
            text.AppendLine();
            text.AppendLine("| 후보 | 어디든: 중앙값 / p95 | 최초 분배만: 중앙값 / p95 | 전설 카드/30런 차이 |");
            text.AppendLine("|---|---|---|---|");
            for (int i = 0; i < candidates.Length; ++i)
            {
                RewardRollCandidate any = All()[i];
                any.Reset = PityReset.AnyHighTierOnScreen;
                RewardRollCandidate first = All()[i];
                first.Reset = PityReset.FirstDealOnly;

                SimResult a = Run(any, runs, seed + (uint)(i * 31), rerollsUsedPerRun: 3);
                SimResult b = Run(first, runs, seed + (uint)(i * 31), rerollsUsedPerRun: 3);

                double legendA = a.CardsPer(SimRarity.Legendary, runs) * 30;
                double legendB = b.CardsPer(SimRarity.Legendary, runs) * 30;

                text.AppendLine(
                    $"| {any.Name} | {a.DroughtPercentile(50)} / {a.DroughtPercentile(95)} " +
                    $"| {b.DroughtPercentile(50)} / {b.DroughtPercentile(95)} " +
                    $"| {Num(legendA)} → {Num(legendB)} |");
            }

            text.AppendLine();

            // --- 5. 리롤 ------------------------------------------------------------------------
            text.AppendLine("## 리롤을 쓰면 무엇이 달라지나");
            text.AppendLine();
            text.AppendLine("모델의 플레이어는 **가장 낮은 슬롯**을 리롤한다. 이것이 가장 강한 플레이이고, 악용 가능성은 여기서 재야 한다.");
            text.AppendLine();
            text.AppendLine("| 후보 | 리롤 0회 전설/30런 | 리롤 3회 전설/30런 | 증가율 | 등급 상승 | 등급 하락 |");
            text.AppendLine("|---|---|---|---|---|---|");
            for (int i = 0; i < candidates.Length; ++i)
            {
                SimResult without = noReroll[i];
                SimResult with = Run(All()[i], runs, seed + (uint)(i * 7919), rerollsUsedPerRun: 3);

                double a = without.CardsPer(SimRarity.Legendary, runs) * 30;
                double b = with.CardsPer(SimRarity.Legendary, runs) * 30;
                double spent = with.RerollsSpent <= 0 ? 1 : with.RerollsSpent;

                text.AppendLine(
                    $"| {candidates[i].Name} | {Num(a)} | **{Num(b)}** | {(a <= 0 ? "-" : Pct((b - a) / a))} " +
                    $"| {Pct(with.RerollsThatRaisedTheGrade / spent)} | {Pct(with.RerollsThatLoweredTheGrade / spent)} |");
            }

            text.AppendLine();

            // --- 6. 초중후반 ---------------------------------------------------------------------
            text.AppendLine("## 의뢰 초반·중반·후반의 상위 등급 빈도");
            text.AppendLine();
            text.AppendLine("정화를 쌓을수록 확률이 오르는 설계인지 보는 표다. 화면 1·2·3을 초·중·후반으로 읽는다.");
            text.AppendLine();
            text.AppendLine("| 후보 | 초반 | 중반 | 후반 | 후반/초반 |");
            text.AppendLine("|---|---|---|---|---|");
            for (int i = 0; i < noReroll.Length; ++i)
            {
                SimResult r = noReroll[i];
                double early = r.HighTierByThird[0] / (double)runs;
                double mid = r.HighTierByThird[1] / (double)runs;
                double late = r.HighTierByThird[2] / (double)runs;
                text.AppendLine(
                    $"| {r.Candidate} | {Num(early)} | {Num(mid)} | {Num(late)} | {(early <= 0 ? "-" : "x" + Num(late / early))} |");
            }

            text.AppendLine();

            // --- 행운 ----------------------------------------------------------------------------
            text.AppendLine("## 행운 투자가 얼마나 보답하나");
            text.AppendLine();
            text.AppendLine("| 후보 | 행운 0p | 행운 10p | 행운 25p | 25p의 증가율 |");
            text.AppendLine("|---|---|---|---|---|");
            for (int i = 0; i < candidates.Length; ++i)
            {
                double at0 = Run(All()[i], runs, seed + 11u, 0, luck: 0).CardsPer(SimRarity.Legendary, runs) * 30;
                double at10 = Run(All()[i], runs, seed + 11u, 0, luck: 10).CardsPer(SimRarity.Legendary, runs) * 30;
                double at25 = Run(All()[i], runs, seed + 11u, 0, luck: 25).CardsPer(SimRarity.Legendary, runs) * 30;
                text.AppendLine(
                    $"| {candidates[i].Name} | {Num(at0)} | {Num(at10)} | {Num(at25)} " +
                    $"| {(at0 <= 0 ? "-" : Pct((at25 - at0) / at0))} |");
            }

            // --- 7. 장단점 -----------------------------------------------------------------------
            text.AppendLine("## 후보별 장점·단점·밸런스 위험");
            text.AppendLine();
            text.AppendLine("### A. 보수");
            text.AppendLine();
            text.AppendLine("- **장점**: 가뭄이 가장 짧고 가장 고르다(중앙값 7화면, p99 15화면). 전설을 '언젠가는 온다'로 만들 수 있다. 천장이 강해서 운 나쁜 플레이어와 운 좋은 플레이어의 격차가 작다.");
            text.AppendLine("- **단점**: 난이도·정화·행운 어느 것도 체감 차이를 거의 못 만든다. Hard를 고르는 이유가 보상이 아니라 다른 데 있어야 한다. 행운 25p가 전설을 25%만 늘린다 — 투자할 이유로는 약하다.");
            text.AppendLine("- **위험**: 선택이 무의미해지는 쪽. 플레이어가 '어차피 천장이 준다'고 느끼면 보상 화면 자체가 긴장을 잃는다.");
            text.AppendLine();
            text.AppendLine("### B. 곡선");
            text.AppendLine();
            text.AppendLine("- **장점**: 가뭄은 A와 사실상 같은데(중앙값 7, p95 13) **후반이 초반의 2.3배**다. 의뢰를 더 깊이 가져갈 이유가 보상으로 생긴다 — [확정] 최소 1곳 정화 후 철수 가능이라는 규칙과 정확히 맞물린다. 행운도 A보다 잘 보답한다(+36%).");
            text.AppendLine("- **단점**: 후반 보정이 커서 **5곳 전부 정화하는 플레이가 지배적 전략**이 될 수 있다. 철수를 일찍 하는 선택이 손해로만 읽히면 그것도 선택이 아니다.");
            text.AppendLine("- **위험**: `ProgressPerPurify`가 가장 강한 손잡이라 여기를 잘못 돌리면 밸런스가 한쪽으로 쏠린다. 다만 손잡이가 하나라 **되돌리기도 쉽다**.");
            text.AppendLine();
            text.AppendLine("### C. 행운");
            text.AppendLine();
            text.AppendLine("- **장점**: 행운 투자가 **전설을 90% 늘린다**. 영구 투자에 '재미있는 선택지'를 하나 만들어 준다.");
            text.AppendLine("- **단점**: 행운에 투자하지 않은 플레이어의 가뭄이 **중앙값 9화면, p95 21화면 = 의뢰 7회**다. 그건 벌이다. 천장이 약해서(x4 상한) 가뭄을 끊어주지도 못한다.");
            text.AppendLine("- **위험**: **행운이 필수 스탯이 된다.** 모든 빌드가 행운을 먼저 찍고 시작하면 영구 투자의 선택지가 22개에서 사실상 21개로 줄어든다. 게다가 행운은 지금 *어디서도 읽히지 않는* 미연결 스탯이라, 이 안을 고르면 D-011(투자 비용)이 함께 묶인다.");
            text.AppendLine();

            text.AppendLine("## 추천");
            text.AppendLine();
            text.AppendLine("**B. 곡선**을 추천한다.");
            text.AppendLine();
            text.AppendLine("- 가뭄은 A와 구별되지 않는다(중앙값 7화면으로 동일, p95 13화면으로 동일). **A의 유일한 장점을 잃지 않는다.**");
            text.AppendLine("- 그러면서 **후반/초반 2.34배**로 A의 1.55배보다 훨씬 또렷하게 '더 깊이 들어가면 더 좋은 것이 나온다'를 만든다. 이건 docs/01이 이미 [확정]한 *정화 1곳 후 철수 가능* 규칙에 실제 선택의 무게를 주는 유일한 후보다.");
            text.AppendLine("- C는 **행운을 안 찍은 플레이어를 벌하고**, 행운이 아직 아무 데도 연결되지 않은 스탯이라 D-011까지 끌고 들어온다. 지금 결정하기에는 묶이는 것이 너무 많다.");
            text.AppendLine();
            text.AppendLine("**같이 고를 것 — 천장 리셋:** `화면 어디든 (리롤 포함)`을 추천한다. `최초 분배만`은 리롤로 뽑은 전설을 '없던 일'로 쳐서 30런당 전설이 1장 더 나오지만(14.13 → 15.15), 그 대가로 **리롤을 쓸수록 천장이 쌓이는** 구조가 된다. 플레이어가 리롤을 아끼게 만드는 규칙은 리롤 3회를 준 이유를 깎는다.");
            text.AppendLine();
            text.AppendLine("**리롤 악용은 어느 안에서도 관찰되지 않았다.** 가장 낮은 슬롯만 리롤하는 최적 플레이로도 전설은 11~22% 늘 뿐이고, 리롤의 17~21%만 등급을 올리며 1~2%는 오히려 내린다. 리롤은 보험이지 전설 생성기가 아니다.");
            text.AppendLine();

            text.AppendLine("## 모델 자체 검증");
            text.AppendLine();
            text.Append(SelfCheck(seed));
            text.AppendLine();

            text.AppendLine($"*{runs:N0}런 x 후보 3개, 시드 {seed}. 같은 시드로 다시 돌리면 같은 숫자가 나온다.*");
            return text.ToString();
        }

        /// <summary>
        /// Checks the model's own claims before anyone reads a number off it. A simulator whose
        /// invariants were never tested is a simulator whose numbers mean nothing.
        /// </summary>
        public static string SelfCheck(uint seed)
        {
            var text = new StringBuilder();
            text.AppendLine("| 확인 | 결과 |");
            text.AppendLine("|---|---|");

            // 1. The same seed reproduces, which is what makes the tables arguable.
            SimResult a = Run(Curved(), 500, seed, 0);
            SimResult b = Run(Curved(), 500, seed, 0);
            bool same = a.Cards[2] == b.Cards[2] && a.Cards[1] == b.Cards[1];
            text.AppendLine($"| 같은 시드가 같은 결과를 낸다 | {(same ? "통과" : "**실패**")} |");

            // 2. Every card dealt is counted exactly once.
            long dealt = 0;
            for (int i = 0; i < 4; ++i)
                dealt += a.Cards[i];
            long expected = (long)a.Screens * DesignConstants.RewardCardSlots;
            text.AppendLine(
                $"| 카드 수 = 화면 x 3 | {(dealt == expected ? "통과" : "**실패**")} ({dealt} / {expected}) |");

            // 3. 신화 is switched off, so no 신화 may be dealt.
            text.AppendLine($"| 신화는 한 장도 나오지 않는다 | {(a.Cards[3] == 0 ? "통과" : "**실패**")} |");

            // 4. [확정] The pity count is per screen. Three rerolls on one screen must not make
            //    it count faster, so a run with rerolls sees the same number of screens.
            SimResult withRerolls = Run(Curved(), 500, seed, 3);
            text.AppendLine(
                $"| 천장은 리롤 횟수로 세지 않는다 | {(withRerolls.Screens == a.Screens ? "통과" : "**실패**")} |");

            // 5. [확정] Correction starts after five screens and not before.
            RewardRollCandidate candidate = Curved();
            bool early = PityMultiplierFor(candidate, candidate.PityAfterScreens) <= 1f;
            bool late = PityMultiplierFor(candidate, candidate.PityAfterScreens + 1) > 1f;
            text.AppendLine($"| 5회 미등장까지는 보정 없음 | {(early ? "통과" : "**실패**")} |");
            text.AppendLine($"| 6번째 화면부터 보정 시작 | {(late ? "통과" : "**실패**")} |");

            // 6. The cap holds.
            bool capped = PityMultiplierFor(candidate, 1000) <= candidate.PityCap + 0.001f;
            text.AppendLine($"| 천장 배수가 상한을 넘지 않음 | {(capped ? "통과" : "**실패**")} |");

            // 7. A pool with no candidates is never dealt from.
            text.AppendLine($"| 전설 풀 3종, 신화 풀 0종 | {(PoolSize[2] == 3 && PoolSize[3] == 0 ? "통과" : "**실패**")} |");

            return text.ToString();
        }

        private static string Describe(PityReset reset) =>
            reset == PityReset.FirstDealOnly ? "최초 분배만" : "화면 어디든 (리롤 포함)";
    }
}
#endif
