using System.Collections.Generic;
using NeonVoid.Combat;
using NeonVoid.Progression;

namespace NeonVoid.UI
{
    /// <summary>
    /// One row of the 백신 성장 터미널: what the stat is now, and what one more point would
    /// make it.
    /// <para>
    /// [확정] docs/05 section 2 asks the 영구 스탯 screen for 구매 전후 값, and forbids
    /// 저장 전 값 확정 표시. So <see cref="Current"/> and <see cref="Preview"/> are two fields
    /// and never one: the preview is what a purchase would produce, it is not the stat, and a
    /// row that overwrote the current reading with it would be stating a value the save has not
    /// taken.
    /// </para>
    /// </summary>
    public readonly struct GrowthSheetRow
    {
        internal GrowthSheetRow(
            InvestableStat stat,
            string name,
            int steps,
            int cost,
            bool connected,
            string unavailable,
            bool affordable,
            string current,
            string preview,
            string currentReduction,
            string previewReduction)
        {
            Stat = stat;
            Name = name;
            Steps = steps;
            Cost = cost;
            Connected = connected;
            Unavailable = unavailable ?? string.Empty;
            Affordable = affordable;
            Current = current ?? string.Empty;
            Preview = preview ?? string.Empty;
            CurrentReduction = currentReduction ?? string.Empty;
            PreviewReduction = previewReduction ?? string.Empty;
        }

        public InvestableStat Stat { get; }

        /// <summary>The document's own name for the stat.</summary>
        public string Name { get; }

        /// <summary>Steps already bought.</summary>
        public int Steps { get; }

        /// <summary>[초안] GDD v1 section 13.1's 소비 포인트 for one step.</summary>
        public int Cost { get; }

        /// <summary>Whether spending a point here would change anything.</summary>
        public bool Connected { get; }

        /// <summary>Why not, when it would not. Empty when it would.</summary>
        public string Unavailable { get; }

        /// <summary>Connected, and the vaccine has the points. Only then is a purchase offered.</summary>
        public bool Affordable { get; }

        /// <summary>The stat as it is, in its own unit.</summary>
        public string Current { get; }

        /// <summary>
        /// What it would be after one more step. <b>Not the stat.</b> Empty for a row that
        /// cannot be bought, because there is nothing to preview.
        /// </summary>
        public string Preview { get; }

        /// <summary>실제 방어감소율 now, for the two resistance rows. Empty for every other.</summary>
        public string CurrentReduction { get; }

        /// <summary>실제 방어감소율 after one more step, for the two resistance rows.</summary>
        public string PreviewReduction { get; }

        /// <summary>Whether a 구매 후 value exists to draw beside the current one.</summary>
        public bool HasPreview => Preview.Length > 0;

        public override string ToString() =>
            $"{Name} x{Steps} · {Current}" + (HasPreview ? $" -> {Preview}" : string.Empty);
    }

    /// <summary>
    /// The 백신 성장 터미널's whole screen, computed from committed state.
    /// <para>
    /// [확정] docs/05 section 2: 보유 포인트, 구매 전후 값, 실제 방어감소율, 무료 재분배, and
    /// 저장 전 값 확정 표시 금지. The previews here come from running the real
    /// <see cref="StatInvestments.Apply"/> over an allocation with one more step, rather than
    /// adding the table's increment to the current number - for 치명타 피해 and the two 관통률
    /// rows the increment is not in the stat's own unit, and a hand-rolled preview would be
    /// wrong in exactly the places a player would check it.
    /// </para>
    /// <para>
    /// Every row of GDD v1 section 13.1 is listed, the seven nothing applies included. They say
    /// why instead of offering a purchase, because a table that hid them would look shorter
    /// than it is.
    /// </para>
    /// </summary>
    public readonly struct GrowthSheet
    {
        private static readonly GrowthSheetRow[] NoRows = new GrowthSheetRow[0];

        private GrowthSheet(
            bool shown,
            bool canInvest,
            string unavailable,
            int unusedPoints,
            int pointsSpent,
            string refusal,
            GrowthSheetRow[] rows)
        {
            Shown = shown;
            CanInvest = canInvest;
            Unavailable = unavailable ?? string.Empty;
            UnusedPoints = unusedPoints;
            PointsSpent = pointsSpent;
            Refusal = refusal ?? string.Empty;
            _rows = rows ?? NoRows;
        }

        private readonly GrowthSheetRow[] _rows;

        /// <summary>The player is not at the terminal. Nothing is drawn.</summary>
        public static readonly GrowthSheet Closed = default;

        /// <summary>Whether the terminal's screen is up.</summary>
        public bool Shown { get; }

        /// <summary>True when a purchase would actually be written.</summary>
        public bool CanInvest { get; }

        /// <summary>
        /// Why nothing can be bought, when nothing can. Said rather than hidden: a terminal
        /// whose buttons silently did nothing would read as broken.
        /// </summary>
        public string Unavailable { get; }

        /// <summary>보유 포인트.</summary>
        public int UnusedPoints { get; }

        /// <summary>이미 투자한 포인트, which 무료 재분배 returns in full.</summary>
        public int PointsSpent { get; }

        /// <summary>What the last refused purchase said. Empty after one that succeeded.</summary>
        public string Refusal { get; }

        /// <summary>Every row of the table, in the table's order.</summary>
        /// <summary>Null-coalesced: <see cref="Closed"/> is a default struct, whose array is null.</summary>
        public IReadOnlyList<GrowthSheetRow> Rows => _rows ?? NoRows;

        /// <summary>
        /// Reads the terminal's screen off the growth director.
        /// <para>
        /// <paramref name="open"/> is the caller's business: docs/00 section 6 makes the hub
        /// 실제 3D 공간, so this is what a terminal shows the player standing at it and not a
        /// menu that can be opened anywhere.
        /// </para>
        /// </summary>
        public static GrowthSheet From(VaccineGrowthDirector growth, bool open)
        {
            if (!open || growth == null)
                return Closed;

            return Build(
                growth.Uninvested,
                growth.Allocation,
                growth.UnusedStatPoints,
                growth.PointsSpent,
                growth.CanInvest,
                growth.LastRefusal);
        }

        /// <summary>
        /// The same screen from plain values. Separate from <see cref="From"/> so the whole
        /// sheet - every preview, every refusal, every unit - can be checked without a scene,
        /// a save file or a MonoBehaviour.
        /// </summary>
        public static GrowthSheet Build(
            in CombatStats uninvested,
            IReadOnlyList<VaccineStatInvestment> allocation,
            int unusedPoints,
            int pointsSpent,
            bool canInvest,
            string refusal)
        {
            CombatStats now = StatInvestments.Apply(uninvested, allocation);

            StatInvestmentStep[] table = StatInvestments.All;
            var rows = new GrowthSheetRow[table.Length];

            for (int i = 0; i < table.Length; ++i)
            {
                StatInvestmentStep step = table[i];
                int steps = StepsIn(allocation, step.Stat);
                bool affordable =
                    step.IsConnected && canInvest && unusedPoints >= step.DraftPointCost;

                string current = step.IsConnected
                    ? StatInvestments.Format(step.Stat, StatInvestments.ValueOf(now, step.Stat))
                    : string.Empty;

                string preview = string.Empty;
                string currentReduction = string.Empty;
                string previewReduction = string.Empty;

                if (step.IsConnected)
                {
                    // The preview runs the real application over one more step rather than
                    // adding the table's increment to the current number.
                    CombatStats after = StatInvestments.Apply(
                        uninvested, WithOneMoreStep(allocation, step));
                    float afterValue = StatInvestments.ValueOf(after, step.Stat);

                    if (affordable)
                        preview = StatInvestments.Format(step.Stat, afterValue);

                    if (StatInvestments.HasDamageReduction(step.Stat))
                    {
                        float nowValue = StatInvestments.ValueOf(now, step.Stat);
                        currentReduction = Percent(StatInvestments.DamageReductionPercent(nowValue));

                        if (affordable)
                            previewReduction =
                                Percent(StatInvestments.DamageReductionPercent(afterValue));
                    }
                }

                rows[i] = new GrowthSheetRow(
                    step.Stat,
                    step.DisplayName,
                    steps,
                    step.DraftPointCost,
                    step.IsConnected,
                    step.IsConnected ? string.Empty : StatInvestments.WhyNotConnected(step.Stat),
                    affordable,
                    current,
                    preview,
                    currentReduction,
                    previewReduction);
            }

            return new GrowthSheet(
                shown: true,
                canInvest: canInvest,
                unavailable: canInvest ? string.Empty : "저장이 커밋하지 않으므로 투자할 수 없습니다.",
                unusedPoints: unusedPoints,
                pointsSpent: pointsSpent,
                refusal: refusal,
                rows: rows);
        }

        private static string Percent(float value) => $"{value:0.#}%";

        /// <summary>
        /// The allocation with one more step on this stat, paid at today's cost. Only ever fed
        /// to <see cref="StatInvestments.Apply"/>, which reads the steps; the points matter to
        /// a refund, and nothing here is refunded.
        /// </summary>
        private static VaccineStatInvestment[] WithOneMoreStep(
            IReadOnlyList<VaccineStatInvestment> allocation, in StatInvestmentStep step)
        {
            int count = allocation?.Count ?? 0;

            for (int i = 0; i < count; ++i)
            {
                if (allocation[i].Stat != step.Stat)
                    continue;

                var grown = new VaccineStatInvestment[count];
                for (int j = 0; j < count; ++j)
                    grown[j] = allocation[j];

                grown[i] = new VaccineStatInvestment(
                    step.Stat, allocation[i].Steps + 1, allocation[i].PointsSpent + step.DraftPointCost);
                return grown;
            }

            var added = new VaccineStatInvestment[count + 1];
            for (int j = 0; j < count; ++j)
                added[j] = allocation[j];

            added[count] = new VaccineStatInvestment(step.Stat, 1, step.DraftPointCost);
            return added;
        }

        private static int StepsIn(
            IReadOnlyList<VaccineStatInvestment> allocation, InvestableStat stat)
        {
            int count = allocation?.Count ?? 0;
            for (int i = 0; i < count; ++i)
            {
                if (allocation[i].Stat == stat)
                    return allocation[i].Steps;
            }

            return 0;
        }

        public override string ToString() =>
            Shown ? $"성장 · 미사용 {UnusedPoints}p · 투자 {PointsSpent}p · {Rows.Count}행" : "closed";
    }
}
