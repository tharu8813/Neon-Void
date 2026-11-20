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
        /// The persistent data path, refused if it looks like the legacy location.
        /// <para>
        /// Throwing is deliberate. The alternative - picking a different directory and carrying
        /// on - would mean a build that silently saves somewhere nobody expected, and the thing
        /// being guarded against is writing into the previous project's data.
        /// </para>
        /// </summary>
        public static string Resolve()
        {
            string root = Application.persistentDataPath;
            if (string.IsNullOrWhiteSpace(root))
                throw new InvalidOperationException("Unity reported no persistent data path, so there is nowhere to save.");

            string[] parts = root.Replace('\\', '/').Split('/');
            for (int i = 0; i < parts.Length; ++i)
            {
                if (string.Equals(parts[i], LegacyDirectoryName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"The persistent data path '{root}' passes through '{LegacyDirectoryName}', which is the " +
                        "legacy Python save location. docs/04 section 4 forbids writing there.");
                }
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
