using System;
using System.IO;

namespace NeonVoid.Progression.Save
{
    /// <summary>
    /// The guard that keeps this save system away from the previous project's files.
    /// <para>
    /// CLAUDE.md and docs/04 section 4 both state the rule the same way: the five legacy Python
    /// files and the old Godot saves are never discovered, parsed, converted, overwritten or
    /// initialised by Unity. That rule is only as good as the code that enforces it, and "we
    /// just never write there" is not enforcement - a slot id taken from a text field, a root
    /// path assembled from a setting, or a future import feature would each be one typo away
    /// from writing <c>save.json</c>.
    /// </para>
    /// <para>
    /// So every path this save system is about to touch goes through here first and a legacy
    /// name throws rather than being renamed, skipped or warned about. The check is on the file
    /// name alone, so it holds wherever the directory came from.
    /// </para>
    /// </summary>
    public static class LegacyDataGuard
    {
        /// <summary>
        /// [확정] docs/04 section 4. Exactly the five files, by name. Listed here rather than
        /// pattern matched, because a pattern loose enough to catch them would also catch names
        /// this save system is allowed to use.
        /// </summary>
        public static readonly string[] ProtectedFileNames =
        {
            "save.json",
            "settings.json",
            "profile.json",
            "antivirus_db.json",
            "os_checkpoint.json",
        };

        /// <summary>Godot leftovers, protected by the same rule in CLAUDE.md.</summary>
        public static readonly string[] ProtectedExtensions = { ".godot", ".tscn", ".gd", ".tres" };

        /// <summary>True when this path names a file the save system must never touch.</summary>
        public static bool IsProtected(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string name = Path.GetFileName(path);
            for (int i = 0; i < ProtectedFileNames.Length; ++i)
            {
                if (string.Equals(name, ProtectedFileNames[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            string extension = Path.GetExtension(path);
            for (int i = 0; i < ProtectedExtensions.Length; ++i)
            {
                if (string.Equals(extension, ProtectedExtensions[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Throws if the path is protected. Called before every read as well as every write:
        /// docs/04 forbids 자동 탐색·파싱 too, so reading one of these is already the violation.
        /// </summary>
        public static void Assert(string path)
        {
            if (IsProtected(path))
            {
                throw new InvalidOperationException(
                    $"'{Path.GetFileName(path)}' is legacy user data. docs/04 section 4: Unity neither reads, " +
                    "converts nor overwrites it.");
            }
        }
    }

    /// <summary>
    /// Where one save slot's files live. docs/04 section 4 asks for
    /// <c>NeonVoidUnity/v1/</c> under the persistent data path, with the snapshot, its backup
    /// and the journal kept apart from anything else.
    /// <para>
    /// The layout is constructed from a root that is passed in, never discovered. That is what
    /// lets a test point the whole save system at a temporary directory and know it cannot have
    /// reached the real one - docs/04 requires exactly that, and the only code that reads
    /// <c>Application.persistentDataPath</c> is the small Unity-side provider, not this.
    /// </para>
    /// </summary>
    public sealed class SaveSlotLayout
    {
        /// <summary>docs/04 section 4: the product-and-version directory under the data path.</summary>
        public const string ProductDirectory = "NeonVoidUnity";
        public const string SchemaDirectory = "v1";

        private SaveSlotLayout(string directory, string slotId)
        {
            Directory = directory;
            SlotId = slotId;
            SnapshotPath = Path.Combine(directory, $"{slotId}.snapshot.json");
            BackupPath = Path.Combine(directory, $"{slotId}.snapshot.bak");
            TempPath = Path.Combine(directory, $"{slotId}.snapshot.tmp");
            QuarantinePrefix = Path.Combine(directory, $"{slotId}.snapshot.corrupt");
        }

        public string Directory { get; }

        public string SlotId { get; }

        /// <summary>The committed snapshot plus receipt log. The only file that is authoritative.</summary>
        public string SnapshotPath { get; }

        /// <summary>The last snapshot known to have verified. docs/04 section 5 step 5.</summary>
        public string BackupPath { get; }

        /// <summary>Written in full and checksummed before it ever replaces the snapshot.</summary>
        public string TempPath { get; }

        /// <summary>
        /// Prefix for a snapshot that failed verification. A corrupt file is moved aside under a
        /// numbered name rather than deleted: docs/04 forbids overwriting user data on a
        /// mismatch, and a file that might be recoverable by hand is not ours to destroy.
        /// </summary>
        public string QuarantinePrefix { get; }

        /// <summary>
        /// The layout under an explicitly given root. <paramref name="root"/> is the
        /// persistent data path or a test's temporary directory; the product and schema
        /// directories are appended here so no caller can leave them out.
        /// </summary>
        public static SaveSlotLayout Under(string root, string slotId = "slot-1")
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new ArgumentException("A save root must be given explicitly, never discovered.", nameof(root));
            if (string.IsNullOrWhiteSpace(slotId))
                throw new ArgumentException("A slot needs an id.", nameof(slotId));

            // A slot id becomes a file name, so a path separator or a traversal in it would put
            // the snapshot somewhere nobody intended - including, with enough '..', the legacy
            // directory this whole guard exists to protect.
            if (slotId.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || slotId.Contains(".."))
                throw new ArgumentException($"Slot id '{slotId}' is not a plain name.", nameof(slotId));

            string directory = Path.Combine(root, ProductDirectory, SchemaDirectory);
            var layout = new SaveSlotLayout(directory, slotId);

            // Belt and braces: the names are built from constants above, so this cannot fail
            // today. It is here because the next person to add a file to the layout gets the
            // check for free rather than having to remember it.
            LegacyDataGuard.Assert(layout.SnapshotPath);
            LegacyDataGuard.Assert(layout.BackupPath);
            LegacyDataGuard.Assert(layout.TempPath);

            return layout;
        }

        /// <summary>The nth quarantine name, so earlier evidence is never overwritten.</summary>
        public string QuarantinePath(int index) =>
            index <= 1 ? $"{QuarantinePrefix}.json" : $"{QuarantinePrefix}.{index}.json";

        public override string ToString() => SnapshotPath;
    }
}
