using System.Collections.Generic;
using NeonVoid.Plugins;
using NeonVoid.Progression.Save;

namespace NeonVoid.UI
{
    /// <summary>One owned plugin instance, as the 플러그인 데이터베이스 터미널 shows it.</summary>
    public readonly struct PluginDatabaseRow
    {
        internal PluginDatabaseRow(
            string instanceId, string documentKey, string proposalId, string name, string effect,
            PluginRarity rarity, PluginHolding holding, bool known)
        {
            InstanceId = instanceId ?? string.Empty;
            DocumentKey = documentKey ?? string.Empty;
            ProposalId = proposalId ?? string.Empty;
            Name = name ?? string.Empty;
            Effect = effect ?? string.Empty;
            Rarity = rarity;
            Holding = holding;
            IsKnownHere = known;
        }

        /// <summary>What a move command names. The save's own id for this instance.</summary>
        public string InstanceId { get; }

        /// <summary>The stable key inside the id, or empty for an id from before the keys.</summary>
        public string DocumentKey { get; }

        /// <summary>docs/03's development id, or empty when this build cannot name the key.</summary>
        public string ProposalId { get; }

        /// <summary>What the player reads.</summary>
        public string Name { get; }

        /// <summary>One line of what it does, or empty.</summary>
        public string Effect { get; }

        public PluginRarity Rarity { get; }

        /// <summary>보관 DB or 출전 소지.</summary>
        public PluginHolding Holding { get; }

        /// <summary>
        /// Whether this build knows what the instance is. False for an id written before the
        /// document keys existed, and for one written by a later build.
        /// </summary>
        public bool IsKnownHere { get; }

        /// <summary>Where it would go if moved. [확정] docs/04 section 3.4: it is a move.</summary>
        public PluginHolding MovesTo =>
            Holding == PluginHolding.Archive ? PluginHolding.Loadout : PluginHolding.Archive;

        public override string ToString() => $"{Name} ({Holding})";
    }

    /// <summary>
    /// What the 플러그인 데이터베이스 터미널 draws: every instance the vaccine owns, on both sides.
    /// <para>
    /// <b>One list, not two.</b> [확정] docs/04 section 3.4: 보관 DB에서 출전 목록으로 꺼내면
    /// DB에서 사라진다 - 복사가 아니라 동일 소유 인스턴스의 이동. Two lists side by side invite
    /// reading it as a copy; one list where each row says which side it is on, and moving it
    /// changes that word, is the same rule drawn.
    /// </para>
    /// <para>
    /// <b>An instance this build cannot name is still shown.</b> It is the player's - written by
    /// a build before the document keys, or by a later one. Hiding it would make their own
    /// property disappear from the only screen that lists it, and moving it works regardless:
    /// a move names the instance and nothing else.
    /// </para>
    /// Pure C#: no Unity types, so the whole screen's content is testable without a scene.
    /// </summary>
    public readonly struct PluginDatabaseSheet
    {
        private static readonly PluginDatabaseRow[] NoRows = new PluginDatabaseRow[0];

        private readonly PluginDatabaseRow[] _rows;
        private readonly string _unavailable;

        private PluginDatabaseSheet(PluginDatabaseRow[] rows, bool shown, bool canMove, string unavailable)
        {
            _rows = rows;
            Shown = shown;
            CanMove = canMove;
            _unavailable = unavailable;
        }

        /// <summary>Nothing to draw: no save, or no vaccine.</summary>
        public static PluginDatabaseSheet Closed => new PluginDatabaseSheet(NoRows, false, false, string.Empty);

        /// <summary>Every owned instance, 보관 DB first, then 출전.</summary>
        public IReadOnlyList<PluginDatabaseRow> Rows => _rows ?? NoRows;

        /// <summary>Whether the terminal has anything to show at all.</summary>
        public bool Shown { get; }

        /// <summary>Whether a move would be written. False with no store, or a halted one.</summary>
        public bool CanMove { get; }

        /// <summary>Why moving is unavailable, or empty.</summary>
        public string Unavailable => _unavailable ?? string.Empty;

        /// <summary>How many sit in the 보관 DB.</summary>
        public int ArchiveCount => CountIn(PluginHolding.Archive);

        /// <summary>
        /// How many are 출전 소지. [확정] docs/04 section 3.3: 출전 개수 제한 없음, so this is
        /// a number to show and never a limit to enforce.
        /// </summary>
        public int LoadoutCount => CountIn(PluginHolding.Loadout);

        /// <summary>Whether the vaccine owns nothing at all yet.</summary>
        public bool IsEmpty => Rows.Count == 0;

        private int CountIn(PluginHolding holding)
        {
            IReadOnlyList<PluginDatabaseRow> rows = Rows;
            int count = 0;
            for (int i = 0; i < rows.Count; ++i)
            {
                if (rows[i].Holding == holding)
                    count++;
            }

            return count;
        }

        /// <summary>
        /// Builds the sheet from a committed snapshot.
        /// </summary>
        /// <param name="canMove">Whether the save would take a move.</param>
        /// <param name="unavailable">Why not, when it would not.</param>
        public static PluginDatabaseSheet Build(VaccineSaveState vaccine, bool canMove, string unavailable)
        {
            if (vaccine == null)
                return Closed;

            var rows = new List<PluginDatabaseRow>();
            Append(rows, vaccine.archivePluginInstanceIds, PluginHolding.Archive);
            Append(rows, vaccine.carriedPluginInstanceIds, PluginHolding.Loadout);

            return new PluginDatabaseSheet(
                rows.ToArray(), shown: true, canMove: canMove, unavailable: unavailable ?? string.Empty);
        }

        private static void Append(
            List<PluginDatabaseRow> into, List<string> instanceIds, PluginHolding holding)
        {
            if (instanceIds == null)
                return;

            for (int i = 0; i < instanceIds.Count; ++i)
            {
                PluginInstanceId id = PluginInstanceId.Parse(instanceIds[i]);
                string proposalId = id.ProposalId;

                if (proposalId.Length == 0)
                {
                    // Named by its id, which is at least true. An empty row would read as a
                    // bug; "알 수 없는 플러그인" reads as what it is.
                    into.Add(new PluginDatabaseRow(
                        id.Value, id.DocumentKey, string.Empty, "알 수 없는 플러그인",
                        "이 빌드가 이름을 알지 못하는 인스턴스입니다.", PluginRarity.Common, holding,
                        known: false));
                    continue;
                }

                PluginCardText text = PluginCardTexts.For(proposalId);
                into.Add(new PluginDatabaseRow(
                    id.Value, id.DocumentKey, proposalId, text.Name, text.Effect,
                    RarityOf(proposalId), holding, known: true));
            }
        }

        /// <summary>
        /// The grade, read from the catalogue rather than stored. docs/04 keeps the save free of
        /// anything about what a plugin IS, so the grade belongs to this build's data and not to
        /// the player's file.
        /// </summary>
        private static PluginRarity RarityOf(string proposalId)
        {
            IReadOnlyList<PluginDefinition> all = PluginCatalog.All();
            for (int i = 0; i < all.Count; ++i)
            {
                if (all[i].ProposalId == proposalId)
                    return all[i].Rarity;
            }

            return PluginRarity.Common;
        }

        /// <summary>What the terminal calls each side.</summary>
        public static string LabelOf(PluginHolding holding) =>
            holding == PluginHolding.Archive ? "보관 DB" : "출전";
    }
}
