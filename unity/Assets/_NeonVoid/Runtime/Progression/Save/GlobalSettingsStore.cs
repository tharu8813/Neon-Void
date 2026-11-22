using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonVoid.Progression.Save
{
    /// <summary>One binding the player moved off its default.</summary>
    [Serializable]
    public sealed class KeyBindingOverrideRecord
    {
        /// <summary>The binding's GUID in the action asset. Stable across reordering.</summary>
        public string bindingId = string.Empty;

        /// <summary>The control path it was moved to.</summary>
        public string path = string.Empty;

        public KeyBindingOverrideRecord Clone() =>
            new KeyBindingOverrideRecord { bindingId = bindingId, path = path };
    }

    /// <summary>
    /// 전역 설정. [확정] docs/04 section 1: 백신과 독립, and 백신 최종 파기에서도 보존.
    /// <para>
    /// That is a statement about where the bytes live, not an intention. This document is
    /// written beside the slots and never inside one, so destroying a vaccine - which rewrites
    /// exactly one slot file - cannot reach it. A test asserts that by destroying a vaccine and
    /// reading these settings back.
    /// </para>
    /// <para>
    /// Only key bindings are here so far. 그래픽·음량·언어·접근성 are the rest of docs/04's list
    /// and get fields when something reads them; an empty field no system consults would be a
    /// setting the player could change with no effect.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class GlobalSettingsDocument
    {
        /// <summary>Bumped whenever a field changes, because the checksum covers the text.</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>The same product tag the slots carry, so a foreign file is refused.</summary>
        public const string ProductTag = SaveDocument.ProductTag;

        public int schemaVersion = CurrentSchemaVersion;
        public string product = ProductTag;

        /// <summary>Only the bindings the player changed. A default binding is absent.</summary>
        public List<KeyBindingOverrideRecord> keyBindings = new List<KeyBindingOverrideRecord>();

        /// <summary>Rises by one per written change, for the same reason a slot's does.</summary>
        public int revision;

        /// <summary>FNV-1a over the canonical text with this field blank.</summary>
        public string checksum = string.Empty;

        public static GlobalSettingsDocument Fresh() => new GlobalSettingsDocument();

        public GlobalSettingsDocument Clone()
        {
            var copy = new GlobalSettingsDocument
            {
                schemaVersion = schemaVersion,
                product = product,
                revision = revision,
                checksum = checksum,
                keyBindings = new List<KeyBindingOverrideRecord>(),
            };

            if (keyBindings == null)
                return copy;

            for (int i = 0; i < keyBindings.Count; ++i)
            {
                if (keyBindings[i] != null)
                    copy.keyBindings.Add(keyBindings[i].Clone());
            }

            return copy;
        }

        /// <summary>The overrides as the input layer wants them: binding id to control path.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Overrides()
        {
            var pairs = new List<KeyValuePair<string, string>>();
            if (keyBindings == null)
                return pairs;

            for (int i = 0; i < keyBindings.Count; ++i)
            {
                KeyBindingOverrideRecord record = keyBindings[i];
                if (record == null
                    || string.IsNullOrWhiteSpace(record.bindingId)
                    || string.IsNullOrWhiteSpace(record.path))
                {
                    continue;
                }

                pairs.Add(new KeyValuePair<string, string>(record.bindingId, record.path));
            }

            return pairs;
        }

        /// <summary>Replaces the stored overrides wholesale. Resetting everything writes none.</summary>
        public void SetOverrides(IReadOnlyList<KeyValuePair<string, string>> overrides)
        {
            keyBindings = new List<KeyBindingOverrideRecord>();
            if (overrides == null)
                return;

            for (int i = 0; i < overrides.Count; ++i)
            {
                if (string.IsNullOrWhiteSpace(overrides[i].Key) || string.IsNullOrWhiteSpace(overrides[i].Value))
                    continue;

                keyBindings.Add(new KeyBindingOverrideRecord
                {
                    bindingId = overrides[i].Key,
                    path = overrides[i].Value,
                });
            }
        }

        public void Validate()
        {
            if (schemaVersion != CurrentSchemaVersion)
                throw new InvalidOperationException($"schema {schemaVersion} is not {CurrentSchemaVersion}");

            if (product != ProductTag)
                throw new InvalidOperationException($"'{product}' is not a {ProductTag} settings file");

            if (revision < 0)
                throw new InvalidOperationException("revision went backwards");

            if (keyBindings == null)
                return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < keyBindings.Count; ++i)
            {
                KeyBindingOverrideRecord record = keyBindings[i];
                if (record == null || string.IsNullOrWhiteSpace(record.bindingId))
                    throw new InvalidOperationException("a key binding override has no binding id");

                // Two overrides for one binding would make which key wins depend on list order.
                if (!seen.Add(record.bindingId))
                    throw new InvalidOperationException($"binding {record.bindingId} is overridden twice");
            }
        }
    }

    /// <summary>Where the global settings file lives, and its temp and backup neighbours.</summary>
    public sealed class GlobalSettingsLayout
    {
        /// <summary>
        /// The file name, beside the slots and inside none of them.
        /// <para>
        /// <b>Not <c>settings.json</c>.</b> That is one of the five names
        /// <see cref="LegacyDataGuard"/> protects - the previous Python build kept its own
        /// settings under it, and [확정] docs/04 section 4 forbids reading, converting or
        /// overwriting those. The guard threw on the first write, which is the system working;
        /// the name is this instead.
        /// </para>
        /// </summary>
        public const string FileName = "global-settings.json";

        private GlobalSettingsLayout(string directory)
        {
            Directory = directory;
            Path = System.IO.Path.Combine(directory, FileName);
            BackupPath = Path + ".bak";
            TempPath = Path + ".tmp";
        }

        public string Directory { get; }
        public string Path { get; }
        public string BackupPath { get; }
        public string TempPath { get; }

        /// <summary>
        /// The same product and schema directory the slots use, so one root holds one game's
        /// files - but a file, not a folder, so nothing that rewrites a slot can touch it.
        /// </summary>
        public static GlobalSettingsLayout Under(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new ArgumentException("a settings root is required", nameof(root));

            return new GlobalSettingsLayout(System.IO.Path.Combine(
                root, SaveSlotLayout.ProductDirectory, SaveSlotLayout.SchemaDirectory));
        }

        public override string ToString() => Path;
    }

    /// <summary>How opening the settings file ended.</summary>
    public enum GlobalSettingsVerdict
    {
        /// <summary>Read and verified.</summary>
        Loaded = 0,

        /// <summary>No file yet. Defaults, and the first save creates it.</summary>
        Created = 1,

        /// <summary>
        /// The file is there and unreadable. Defaults are used for this session and
        /// <b>nothing is written over it</b> until the player changes something.
        /// </summary>
        Unreadable = 2,
    }

    /// <summary>
    /// Reads and writes 전역 설정 with the transaction docs/04 section 5 asks for: write a temp
    /// file, verify it by reading it back, then replace, keeping the previous file as a backup.
    /// <para>
    /// <b>Why its own store rather than the slot's.</b> <see cref="SaveStore"/> is built around
    /// one vaccine's lifetime - run ids, death receipts, idempotency per command. Settings have
    /// none of that: there is one document, it is not a vaccine's, and [확정] docs/04 section 1
    /// says 백신 최종 파기에서도 보존. Putting them in a slot would make the one thing that must
    /// outlive a vaccine a field of the vaccine.
    /// </para>
    /// <para>
    /// <b>A corrupt file is not overwritten on sight.</b> The session runs on defaults and says
    /// so; the bytes stay where they are until the player changes a setting, at which point the
    /// backup is what they can go back to. Rewriting it at startup would destroy the only copy
    /// of whatever they had configured.
    /// </para>
    /// </summary>
    public sealed class GlobalSettingsStore
    {
        private readonly ISaveFileSystem _files;

        public GlobalSettingsStore(ISaveFileSystem files, GlobalSettingsLayout layout)
        {
            _files = files ?? throw new ArgumentNullException(nameof(files));
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            Document = GlobalSettingsDocument.Fresh();
        }

        public GlobalSettingsLayout Layout { get; }

        /// <summary>The settings in memory. Never null.</summary>
        public GlobalSettingsDocument Document { get; private set; }

        /// <summary>How the last <see cref="Open"/> ended.</summary>
        public GlobalSettingsVerdict Verdict { get; private set; }

        /// <summary>Why it could not be read, when it could not.</summary>
        public string Detail { get; private set; } = string.Empty;

        /// <summary>Reads the file, or starts from defaults when there is none.</summary>
        public GlobalSettingsVerdict Open()
        {
            Detail = string.Empty;

            if (!_files.Exists(Layout.Path))
            {
                Document = GlobalSettingsDocument.Fresh();
                Verdict = GlobalSettingsVerdict.Created;
                return Verdict;
            }

            try
            {
                string text = _files.ReadAllText(Layout.Path);
                GlobalSettingsDocument parsed = Parse(text, out string why);
                if (parsed == null)
                {
                    Document = GlobalSettingsDocument.Fresh();
                    Detail = why;
                    Verdict = GlobalSettingsVerdict.Unreadable;
                    return Verdict;
                }

                Document = parsed;
                Verdict = GlobalSettingsVerdict.Loaded;
                return Verdict;
            }
            catch (Exception e)
            {
                Document = GlobalSettingsDocument.Fresh();
                Detail = e.Message;
                Verdict = GlobalSettingsVerdict.Unreadable;
                return Verdict;
            }
        }

        /// <summary>
        /// Writes the current document. Returns false with a reason rather than throwing, so a
        /// settings screen can say that a change did not stick instead of showing it as taken.
        /// </summary>
        public bool Save(out string detail)
        {
            detail = string.Empty;

            try
            {
                Document.Validate();
            }
            catch (Exception e)
            {
                detail = e.Message;
                return false;
            }

            GlobalSettingsDocument next = Document.Clone();
            next.revision = Document.revision + 1;
            string text = Stamp(next);

            try
            {
                _files.EnsureDirectory(Layout.Directory);
                _files.Delete(Layout.TempPath);
                _files.WriteAllText(Layout.TempPath, text);

                // Read the temp file back before it replaces anything. docs/04 section 5 step 5:
                // a write that was not verified is a write that may have half happened.
                string readBack = _files.ReadAllText(Layout.TempPath);
                if (Parse(readBack, out string why) == null)
                {
                    _files.Delete(Layout.TempPath);
                    detail = $"the written file did not verify: {why}";
                    return false;
                }

                _files.Replace(Layout.TempPath, Layout.Path, Layout.BackupPath);
            }
            catch (Exception e)
            {
                detail = e.Message;
                try
                {
                    _files.Delete(Layout.TempPath);
                }
                catch (Exception)
                {
                    // The temp file is already the thing that failed; nothing to add.
                }

                return false;
            }

            Document = next;
            return true;
        }

        /// <summary>The text of a document with its own checksum in it.</summary>
        public static string Stamp(GlobalSettingsDocument document)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            document.checksum = SaveChecksum.Of(Canonical(document));
            return JsonUtility.ToJson(document, prettyPrint: false);
        }

        /// <summary>The text the checksum is taken over: the document with a blank checksum.</summary>
        public static string Canonical(GlobalSettingsDocument document)
        {
            string keep = document.checksum;
            document.checksum = string.Empty;
            try
            {
                return JsonUtility.ToJson(document, prettyPrint: false);
            }
            finally
            {
                document.checksum = keep;
            }
        }

        /// <summary>Parses and verifies, or returns null with the reason.</summary>
        public static GlobalSettingsDocument Parse(string text, out string detail)
        {
            detail = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                detail = "the file is empty";
                return null;
            }

            GlobalSettingsDocument document;
            try
            {
                document = JsonUtility.FromJson<GlobalSettingsDocument>(text);
            }
            catch (Exception e)
            {
                detail = e.Message;
                return null;
            }

            if (document == null)
            {
                detail = "the file is not a JSON object";
                return null;
            }

            if (document.product != GlobalSettingsDocument.ProductTag)
            {
                detail = $"'{document.product}' is not a {GlobalSettingsDocument.ProductTag} settings file";
                return null;
            }

            if (document.schemaVersion != GlobalSettingsDocument.CurrentSchemaVersion)
            {
                detail =
                    $"schema {document.schemaVersion} is not {GlobalSettingsDocument.CurrentSchemaVersion}, " +
                    "and docs/04 section 4 forbids migrating one automatically";
                return null;
            }

            string recomputed = SaveChecksum.Of(Canonical(document));
            if (!SaveChecksum.Matches(document.checksum, recomputed))
            {
                detail = $"checksum does not match the contents ({recomputed})";
                return null;
            }

            try
            {
                document.Validate();
            }
            catch (Exception e)
            {
                detail = e.Message;
                return null;
            }

            return document;
        }
    }
}
