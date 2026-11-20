using System;
using System.Text;

namespace NeonVoid.Progression.Save
{
    /// <summary>
    /// The checksum docs/04 section 5 step 4 requires, over the serialized snapshot.
    /// <para>
    /// <b>This detects corruption, not tampering, and does not pretend otherwise.</b> FNV-1a is
    /// a non-cryptographic hash: anyone editing their own save file on their own machine can
    /// recompute it, and nothing here tries to stop them. What it catches is the failure docs/04
    /// actually names - 중단된 checksum, 임시 파일 손상, torn writes - where a file is half
    /// written or a byte has rotted and the alternative is loading nonsense as if it were
    /// progress. A SHA-256 would catch the same cases and cost a dependency and a mental model
    /// ("so the save is signed?") that is not true.
    /// </para>
    /// </summary>
    public static class SaveChecksum
    {
        private const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        /// <summary>
        /// 16 lowercase hex digits over the UTF-8 bytes of <paramref name="text"/>. Computed
        /// from the encoded bytes rather than from chars so that the number does not depend on
        /// how a platform happens to represent a string in memory.
        /// </summary>
        public static string Of(string text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            byte[] bytes = Encoding.UTF8.GetBytes(text);
            ulong hash = Offset;
            unchecked
            {
                for (int i = 0; i < bytes.Length; ++i)
                {
                    hash ^= bytes[i];
                    hash *= Prime;
                }
            }

            return hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Constant-shape comparison that treats a missing checksum as a mismatch. A file whose
        /// checksum field is blank is not "unverified and probably fine": the writer always
        /// fills it, so a blank one means the file was not written by this store.
        /// </summary>
        public static bool Matches(string expected, string actual) =>
            !string.IsNullOrEmpty(expected)
            && !string.IsNullOrEmpty(actual)
            && string.Equals(expected, actual, StringComparison.Ordinal);
    }
}
