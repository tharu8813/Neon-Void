using System;
using System.IO;
using System.Text;

namespace NeonVoid.Progression.Save
{
    /// <summary>
    /// The only way the save store touches storage.
    /// <para>
    /// It exists so the fault injection docs/04 section 7 demands can actually be performed:
    /// 파일 쓰기 권한 없음, 임시 파일 손상, 이름 충돌, 중단된 checksum, a process killed between
    /// two steps. None of those can be provoked reliably against a real disk, and a save system
    /// whose failure paths have never run is a save system whose failure paths do not work.
    /// </para>
    /// <para>
    /// Every method may throw <see cref="SaveIoException"/>; the store treats a throw as a
    /// failed step and never as a silent success.
    /// </para>
    /// </summary>
    public interface ISaveFileSystem
    {
        bool Exists(string path);

        string ReadAllText(string path);

        /// <summary>Writes the whole file and flushes it as far as the platform allows.</summary>
        void WriteAllText(string path, string contents);

        /// <summary>
        /// Replaces <paramref name="destination"/> with <paramref name="temp"/>, keeping the
        /// previous contents at <paramref name="backup"/>. As close to atomic as the platform
        /// offers; see <see cref="DiskSaveFileSystem"/> for what that means in practice.
        /// </summary>
        void Replace(string temp, string destination, string backup);

        /// <summary>Renames without replacing. Throws if the destination exists.</summary>
        void Move(string from, string to);

        void Delete(string path);

        void EnsureDirectory(string directory);
    }

    /// <summary>A storage step that did not happen. Always carries the path.</summary>
    public sealed class SaveIoException : Exception
    {
        public SaveIoException(string message, Exception inner = null) : base(message, inner)
        {
        }
    }

    /// <summary>
    /// The real one, on top of <see cref="System.IO"/>.
    /// <para>
    /// <b>What "atomic" honestly means here.</b> docs/04 section 5 asks for atomic
    /// replace/rename "가능한 플랫폼 범위 확인", and this is that range:
    /// <see cref="File.Replace(string,string,string)"/> is a single filesystem operation on
    /// NTFS and on APFS/ext4 through rename, so a reader sees either the old snapshot or the
    /// new one and never a half-written file. What is <i>not</i> guaranteed is the directory
    /// entry reaching the platter: .NET can fsync a file handle
    /// (<see cref="FileStream.Flush(bool)"/>) but has no portable way to fsync the directory,
    /// so a power cut in the microseconds around the rename can in principle leave the old
    /// snapshot. That is the safe direction - a lost commit, recoverable by its absence from
    /// the receipt log - and it is stated here rather than quietly assumed away.
    /// </para>
    /// </summary>
    public sealed class DiskSaveFileSystem : ISaveFileSystem
    {
        public bool Exists(string path)
        {
            LegacyDataGuard.Assert(path);
            return File.Exists(path);
        }

        public string ReadAllText(string path)
        {
            LegacyDataGuard.Assert(path);
            try
            {
                return File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception e)
            {
                throw new SaveIoException($"could not read '{path}': {e.Message}", e);
            }
        }

        public void WriteAllText(string path, string contents)
        {
            LegacyDataGuard.Assert(path);
            try
            {
                // Written through a stream rather than File.WriteAllText so the flush is
                // explicit: step 4 of docs/04's transaction is "임시 파일 완전 쓰기 →
                // flush/fsync", and a buffered write that returns before the bytes are down
                // would make the checksum verification that follows it meaningless.
                using (var stream = new FileStream(
                    path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                {
                    writer.Write(contents);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }
            }
            catch (Exception e)
            {
                throw new SaveIoException($"could not write '{path}': {e.Message}", e);
            }
        }

        public void Replace(string temp, string destination, string backup)
        {
            LegacyDataGuard.Assert(temp);
            LegacyDataGuard.Assert(destination);
            LegacyDataGuard.Assert(backup);

            try
            {
                if (!File.Exists(destination))
                {
                    // Nothing to replace or back up: the first commit of a new slot.
                    File.Move(temp, destination);
                    return;
                }

                if (!string.IsNullOrEmpty(backup) && File.Exists(backup))
                    File.Delete(backup);

                File.Replace(temp, destination, backup);
            }
            catch (Exception e)
            {
                throw new SaveIoException($"could not replace '{destination}': {e.Message}", e);
            }
        }

        public void Move(string from, string to)
        {
            LegacyDataGuard.Assert(from);
            LegacyDataGuard.Assert(to);

            try
            {
                if (File.Exists(to))
                    throw new IOException($"'{to}' already exists");

                File.Move(from, to);
            }
            catch (Exception e)
            {
                throw new SaveIoException($"could not move '{from}' to '{to}': {e.Message}", e);
            }
        }

        public void Delete(string path)
        {
            LegacyDataGuard.Assert(path);
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception e)
            {
                throw new SaveIoException($"could not delete '{path}': {e.Message}", e);
            }
        }

        public void EnsureDirectory(string directory)
        {
            try
            {
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
            }
            catch (Exception e)
            {
                throw new SaveIoException($"could not create '{directory}': {e.Message}", e);
            }
        }
    }
}
