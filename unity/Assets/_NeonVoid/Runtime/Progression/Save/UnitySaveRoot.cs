using System;
using UnityEngine;

namespace NeonVoid.Progression.Save
{
    /// <summary>
    /// The one place that asks Unity where the player's files go.
    /// <para>
    /// It is a separate file for a reason that docs/04 section 4 states as a test requirement:
    /// <i>테스트용 저장 서비스는 반드시 임시 디렉터리를 주입받으며 실제 사용자 저장 디렉터리에
    /// 접근하면 테스트 실패</i>. <see cref="SaveStore"/> and <see cref="SaveSlotLayout"/> take a
    /// root as an argument and have no way to discover one, so a test that never calls this
    /// class cannot reach a real save by accident - not because it was careful, but because the
    /// path is not available to it.
    /// </para>
    /// <para>
    /// <b>The legacy location is not this one.</b> The previous Python build kept its five files
    /// in the project root or <c>%LOCALAPPDATA%\NeonVoid</c>. Unity's
    /// <see cref="Application.persistentDataPath"/> is a per-company, per-product directory
    /// (<c>%USERPROFILE%\AppData\LocalLow\&lt;company&gt;\&lt;product&gt;</c> on Windows) and the
    /// product and schema directories are appended on top of it, so the two cannot collide.
    /// <see cref="Resolve"/> checks that rather than asserting it.
    /// </para>
    /// </summary>
    public static class UnitySaveRoot
    {
        /// <summary>The legacy directory name, which this root must never be or sit inside.</summary>
        public const string LegacyDirectoryName = "NeonVoid";

        /// <summary>
        /// True when this path is, or sits inside, the previous project's save directory:
        /// <c>%LOCALAPPDATA%\NeonVoid</c>.
        /// <para>
        /// <b>Why this is not "any folder called NeonVoid".</b> That was the first version of
        /// this check and it was a trap. Unity's persistent data path is
        /// <c>AppData\LocalLow\&lt;company&gt;\&lt;product&gt;</c>, so naming the product
        /// NeonVoid - the obvious thing to do, and currently unset - would have made this refuse
        /// the game's own save directory and stop saving entirely, with an error blaming the
        /// legacy data policy. The legacy location is under <c>AppData\Local</c>, which is a
        /// different directory from <c>AppData\LocalLow</c>, so the check is on that exact pair
        /// of segments.
        /// </para>
        /// <para>
        /// The real protection is not this - it is <see cref="LegacyDataGuard"/>, which refuses
        /// the five legacy file names on every read and write wherever they appear. This only
        /// catches a wrong root before anything is attempted, which is a clearer error than a
        /// refused write later.
        /// </para>
        /// </summary>
        public static bool IsLegacyRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string normalized = path.Replace('\\', '/').ToLowerInvariant().TrimEnd('/');
            string legacy = "/appdata/local/" + LegacyDirectoryName.ToLowerInvariant();

            return normalized.EndsWith(legacy, StringComparison.Ordinal)
                || normalized.Contains(legacy + "/");
        }

        /// <summary>
        /// The persistent data path, refused if it is the legacy location.
        /// <para>
        /// Throwing is deliberate. The alternative - picking a different directory and carrying
        /// on - would mean a build that silently saves somewhere nobody expected, and the thing
        /// being guarded against is writing into the previous project's data.
        /// </para>
        /// </summary>
        /// <summary>
        /// The folder name a 샌드박스 저장 sits in, beside the player's real save rather than
        /// inside it.
        /// </summary>
        public const string SandboxDirectoryName = "sandbox";

        /// <summary>
        /// A root for writes that must not reach the player's real files.
        /// <para>
        /// <b>Why a separate root rather than a separate slot id.</b> A slot id only changes a
        /// file name, so a sandbox slot would sit in the same directory as the real one and a
        /// mistake in a name would land on top of real data. A root of its own puts the whole
        /// product/schema tree somewhere else, so nothing a sandbox writes can be a real save
        /// file even if every name below it were wrong.
        /// </para>
        /// <para>
        /// It is under the persistent data path, never the legacy location: <see cref="Resolve"/>
        /// has already refused that, and <see cref="LegacyDataGuard"/> still refuses the five
        /// legacy file names wherever they appear.
        /// </para>
        /// </summary>
        public static string SandboxUnder(string realRoot)
        {
            if (string.IsNullOrWhiteSpace(realRoot))
                throw new ArgumentException("A sandbox needs a root to sit beside.", nameof(realRoot));

            if (IsLegacyRoot(realRoot))
            {
                throw new InvalidOperationException(
                    $"'{realRoot}' is the legacy Python save location; a sandbox must not be put inside it.");
            }

            return System.IO.Path.Combine(realRoot, SandboxDirectoryName);
        }

        public static string Resolve()
        {
            string root = Application.persistentDataPath;
            if (string.IsNullOrWhiteSpace(root))
                throw new InvalidOperationException("Unity reported no persistent data path, so there is nowhere to save.");

            if (IsLegacyRoot(root))
            {
                throw new InvalidOperationException(
                    $"The persistent data path '{root}' is the legacy Python save location " +
                    $"(%LOCALAPPDATA%\\{LegacyDirectoryName}). docs/04 section 4 forbids writing there.");
            }

            return root;
        }

        /// <summary>A store over the real save directory. The only non-test construction path.</summary>
        public static SaveStore OpenPlayerStore(string slotId = "slot-1")
        {
            var store = new SaveStore(new DiskSaveFileSystem(), SaveSlotLayout.Under(Resolve(), slotId));
            store.Open();
            return store;
        }
    }
}
