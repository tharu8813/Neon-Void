using System;

namespace NeonVoid.Plugins
{
    /// <summary>
    /// The id one owned plugin instance is known by in a save: its stable document key, then a
    /// unique part.
    /// <para>
    /// <b>Why the key is inside the id rather than beside it.</b> The save holds
    /// <c>List&lt;string&gt;</c> for the archive and the loadout, and its checksum covers the
    /// serialized text - so a new field is a schema version, and a schema version is a migration
    /// over files players already have. Putting the key in the id adds nothing to the format:
    /// the lists stay lists of strings, every existing id stays valid, and nothing has to be
    /// migrated. What changes is that an id can now say what it is.
    /// </para>
    /// <para>
    /// <b>The unique part is what makes two of the same card two things.</b> [미확정] docs/04
    /// section 3 asks whether a vaccine may hold several instances of one definition at all
    /// (D-002/D-013). This format can express either answer - it does not decide it - and
    /// whoever grants an instance is where that rule belongs.
    /// </para>
    /// <para>
    /// <b>An id from an older build still parses.</b> One with no separator is an instance whose
    /// definition this build cannot name, not a broken id: those exist in saves written before
    /// the key existed, and an archive that threw on them would lock the player out of their own
    /// file.
    /// </para>
    /// </summary>
    public readonly struct PluginInstanceId
    {
        /// <summary>
        /// Between the key and the unique part. Not '.', which appears inside keys, and not '-',
        /// which appears inside GUID forms.
        /// </summary>
        public const char Separator = '#';

        private readonly string _value;
        private readonly string _documentKey;
        private readonly string _unique;

        private PluginInstanceId(string value, string documentKey, string unique)
        {
            _value = value ?? string.Empty;
            _documentKey = documentKey ?? string.Empty;
            _unique = unique ?? string.Empty;
        }

        // Explicit backing fields, read through ?? : default(PluginInstanceId) never runs the
        // constructor, so auto-properties would hand back null and every length check on them
        // would throw. Parse(null) returning a struct that cannot be asked questions would be
        // worse than Parse(null) throwing.

        /// <summary>The whole id, which is what a save file holds.</summary>
        public string Value => _value ?? string.Empty;

        /// <summary>The stable key, or empty for an id that carries none.</summary>
        public string DocumentKey => _documentKey ?? string.Empty;

        /// <summary>The part that makes this instance distinct from another of the same card.</summary>
        public string Unique => _unique ?? string.Empty;

        /// <summary>Whether this id says which plugin it is.</summary>
        public bool NamesADefinition => DocumentKey.Length > 0;

        /// <summary>Whether this build can say what the named definition is.</summary>
        public bool IsKnownHere => PluginDocumentKeys.Knows(DocumentKey);

        /// <summary>The card this id names, or empty.</summary>
        public string ProposalId => PluginDocumentKeys.ProposalIdFor(DocumentKey);

        /// <summary>
        /// A fresh id for an instance of <paramref name="documentKey"/>.
        /// </summary>
        /// <param name="unique">
        /// The unique part. Given rather than generated here so a test is deterministic and a
        /// caller can key it off a receipt; callers in the game pass a GUID.
        /// </param>
        public static PluginInstanceId Mint(string documentKey, string unique)
        {
            if (string.IsNullOrWhiteSpace(documentKey))
                throw new ArgumentException("An instance needs a definition to be an instance of.", nameof(documentKey));
            if (documentKey.IndexOf(Separator) >= 0)
                throw new ArgumentException($"A document key may not contain '{Separator}'.", nameof(documentKey));
            if (string.IsNullOrWhiteSpace(unique))
                throw new ArgumentException("An instance needs a unique part, or two would be one.", nameof(unique));
            if (unique.IndexOf(Separator) >= 0)
                throw new ArgumentException($"The unique part may not contain '{Separator}'.", nameof(unique));

            return new PluginInstanceId(documentKey + Separator + unique, documentKey, unique);
        }

        /// <summary>A fresh id with a GUID for its unique part.</summary>
        public static PluginInstanceId Mint(string documentKey) =>
            Mint(documentKey, Guid.NewGuid().ToString("N"));

        /// <summary>
        /// Reads an id from a save. Never throws: an id this build does not understand is still
        /// the player's, and refusing to read it would be refusing their file.
        /// </summary>
        public static PluginInstanceId Parse(string value)
        {
            if (string.IsNullOrEmpty(value))
                return default;

            int at = value.IndexOf(Separator);
            if (at <= 0 || at >= value.Length - 1)
                return new PluginInstanceId(value, string.Empty, value);

            return new PluginInstanceId(value, value.Substring(0, at), value.Substring(at + 1));
        }

        /// <summary>The document key an id carries, or empty. The common case, spelled once.</summary>
        public static string DocumentKeyOf(string value) => Parse(value).DocumentKey;

        public override string ToString() => Value;
    }
}
