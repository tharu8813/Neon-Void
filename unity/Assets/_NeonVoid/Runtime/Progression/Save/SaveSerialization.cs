using System;
using UnityEngine;

namespace NeonVoid.Progression.Save
{
    /// <summary>Just enough of a document to decide whether to parse the rest of it.</summary>
    [Serializable]
    internal sealed class SaveDocumentHeader
    {
        public int schemaVersion;
        public string product;
    }

    /// <summary>Why a text could not become a document.</summary>
    public enum SaveParseVerdict
    {
        Parsed = 0,

        /// <summary>Not JSON, or not a document at all.</summary>
        Unreadable = 1,

        /// <summary>Written by a different product. Never parsed further.</summary>
        ForeignProduct = 2,

        /// <summary>
        /// A newer build wrote it. [확정] docs/04 section 7: 진짜 오래된 세이브/미래 스키마
        /// 발견 → 안전 오류, never 자동 변환·삭제.
        /// </summary>
        FutureSchema = 3,

        /// <summary>An older schema. There is no migration, so this stops too.</summary>
        OlderSchema = 4,

        /// <summary>Parsed, but the checksum does not match the bytes.</summary>
        ChecksumMismatch = 5,

        /// <summary>Parsed and checksummed, but the contents break an invariant.</summary>
        Invalid = 6,
    }

    public readonly struct SaveParseResult
    {
        public readonly SaveParseVerdict Verdict;
        public readonly SaveDocument Document;
        public readonly string Detail;

        private SaveParseResult(SaveParseVerdict verdict, SaveDocument document, string detail)
        {
            Verdict = verdict;
            Document = document;
            Detail = detail ?? string.Empty;
        }

        public bool Ok => Verdict == SaveParseVerdict.Parsed;

        public static SaveParseResult Success(SaveDocument document) =>
            new SaveParseResult(SaveParseVerdict.Parsed, document, string.Empty);

        public static SaveParseResult Failure(SaveParseVerdict verdict, string detail) =>
            new SaveParseResult(verdict, null, detail);
    }

    /// <summary>
    /// Turns a document into the exact text that gets checksummed and written, and back.
    /// <para>
    /// <b>How the checksum covers the field it lives in.</b> The checksum is a field of the
    /// document, so it cannot cover itself. It is computed over the document serialized with
    /// that one field blank, and verified the same way: read the file, take the stored
    /// checksum, blank the field, serialize again, compare. That makes verification depend on
    /// serialization being an exact round trip, which is why the document holds no floating
    /// point numbers and why <see cref="Verify"/> compares the re-serialized text rather than
    /// the raw file bytes - whitespace a person added by hand while inspecting the file is not
    /// corruption of the state, and refusing to load over it would strand a save that is fine.
    /// </para>
    /// </summary>
    public static class SaveSerialization
    {
        /// <summary>
        /// The canonical text of a document, with the checksum field blank. This is what the
        /// checksum is taken over.
        /// </summary>
        public static string ToCanonicalText(SaveDocument document)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            string keep = document.snapshotChecksum;
            document.snapshotChecksum = string.Empty;
            try
            {
                return JsonUtility.ToJson(document, prettyPrint: false);
            }
            finally
            {
                document.snapshotChecksum = keep;
            }
        }

        /// <summary>Stamps the document with its own checksum and returns the text to write.</summary>
        public static string Stamp(SaveDocument document)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            document.snapshotChecksum = SaveChecksum.Of(ToCanonicalText(document));
            return JsonUtility.ToJson(document, prettyPrint: false);
        }

        /// <summary>
        /// Parses and verifies, refusing at the first thing that is wrong. The order matters:
        /// the product tag and the schema version are read from a header before the body is
        /// parsed at all, so a legacy or future file is never deserialized into this build's
        /// fields and then half understood.
        /// </summary>
        public static SaveParseResult Verify(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return SaveParseResult.Failure(SaveParseVerdict.Unreadable, "the file is empty");

            SaveDocumentHeader header;
            try
            {
                header = JsonUtility.FromJson<SaveDocumentHeader>(text);
            }
            catch (Exception e)
            {
                return SaveParseResult.Failure(SaveParseVerdict.Unreadable, e.Message);
            }

            if (header == null)
                return SaveParseResult.Failure(SaveParseVerdict.Unreadable, "the file is not a JSON object");

            if (header.product != SaveDocument.ProductTag)
            {
                return SaveParseResult.Failure(
                    SaveParseVerdict.ForeignProduct,
                    $"'{header.product}' is not a {SaveDocument.ProductTag} save; it is left untouched");
            }

            if (header.schemaVersion > SaveDocument.CurrentSchemaVersion)
            {
                return SaveParseResult.Failure(
                    SaveParseVerdict.FutureSchema,
                    $"schema {header.schemaVersion} was written by a newer build than this one " +
                    $"(it writes {SaveDocument.CurrentSchemaVersion})");
            }

            if (header.schemaVersion < SaveDocument.CurrentSchemaVersion)
            {
                return SaveParseResult.Failure(
                    SaveParseVerdict.OlderSchema,
                    $"schema {header.schemaVersion} needs a migration, and docs/04 section 4 forbids " +
                    "performing one automatically");
            }

            SaveDocument document;
            try
            {
                document = JsonUtility.FromJson<SaveDocument>(text);
            }
            catch (Exception e)
            {
                return SaveParseResult.Failure(SaveParseVerdict.Unreadable, e.Message);
            }

            if (document == null)
                return SaveParseResult.Failure(SaveParseVerdict.Unreadable, "the body did not parse");

            string stored = document.snapshotChecksum;
            string recomputed = SaveChecksum.Of(ToCanonicalText(document));
            if (!SaveChecksum.Matches(stored, recomputed))
            {
                return SaveParseResult.Failure(
                    SaveParseVerdict.ChecksumMismatch,
                    $"checksum {Describe(stored)} does not match the contents ({recomputed})");
            }

            try
            {
                document.Validate();
            }
            catch (Exception e)
            {
                return SaveParseResult.Failure(SaveParseVerdict.Invalid, e.Message);
            }

            return SaveParseResult.Success(document);
        }

        private static string Describe(string checksum) =>
            string.IsNullOrEmpty(checksum) ? "(absent)" : checksum;
    }
}
