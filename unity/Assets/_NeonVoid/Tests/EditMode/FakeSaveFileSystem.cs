using System;
using System.Collections.Generic;
using NeonVoid.Progression.Save;

namespace NeonVoid.Tests
{
    /// <summary>
    /// A storage layer that can be made to fail exactly where docs/04 section 7 says it must be
    /// tested: 파일 쓰기 권한 없음, 임시 파일 손상, 이름 충돌, 중단된 checksum, 프로세스 kill
    /// 직전/직후.
    /// <para>
    /// These faults cannot be provoked on a real disk on demand, and a save system whose
    /// recovery paths have never executed does not have recovery paths - it has code shaped
    /// like them. Everything here is in memory, so a test also physically cannot reach the
    /// player's save directory.
    /// </para>
    /// </summary>
    public sealed class FakeSaveFileSystem : ISaveFileSystem
    {
        private readonly Dictionary<string, string> _files =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private readonly HashSet<string> _directories = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Paths that refuse to be written, standing in for a permissions failure.</summary>
        public readonly HashSet<string> WriteDenied = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Paths that refuse to be read.</summary>
        public readonly HashSet<string> ReadDenied = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Replace throws instead of replacing, as if the process died mid-rename.</summary>
        public bool ReplaceFails { get; set; }

        /// <summary>
        /// Every write is truncated to its first half before being stored, which is what a torn
        /// write looks like to the reader that comes after it.
        /// </summary>
        public bool TearWrites { get; set; }

        public int WriteCount { get; private set; }

        public int ReplaceCount { get; private set; }

        public IReadOnlyCollection<string> Paths => _files.Keys;

        public bool Exists(string path)
        {
            LegacyDataGuard.Assert(path);
            return _files.ContainsKey(path);
        }

        public string ReadAllText(string path)
        {
            LegacyDataGuard.Assert(path);
            if (ReadDenied.Contains(path))
                throw new SaveIoException($"read denied: {path}");
            if (!_files.TryGetValue(path, out string contents))
                throw new SaveIoException($"no such file: {path}");

            return contents;
        }

        public void WriteAllText(string path, string contents)
        {
            LegacyDataGuard.Assert(path);
            if (WriteDenied.Contains(path))
                throw new SaveIoException($"write denied: {path}");

            WriteCount++;
            _files[path] = TearWrites ? contents.Substring(0, contents.Length / 2) : contents;
        }

        public void Replace(string temp, string destination, string backup)
        {
            LegacyDataGuard.Assert(temp);
            LegacyDataGuard.Assert(destination);
            LegacyDataGuard.Assert(backup);

            if (ReplaceFails)
                throw new SaveIoException($"replace failed: {destination}");
            if (!_files.TryGetValue(temp, out string incoming))
                throw new SaveIoException($"no such file: {temp}");

            ReplaceCount++;
            if (_files.TryGetValue(destination, out string previous) && !string.IsNullOrEmpty(backup))
                _files[backup] = previous;

            _files[destination] = incoming;
            _files.Remove(temp);
        }

        public void Move(string from, string to)
        {
            LegacyDataGuard.Assert(from);
            LegacyDataGuard.Assert(to);

            if (!_files.TryGetValue(from, out string contents))
                throw new SaveIoException($"no such file: {from}");
            if (_files.ContainsKey(to))
                throw new SaveIoException($"already exists: {to}");

            _files[to] = contents;
            _files.Remove(from);
        }

        public void Delete(string path)
        {
            LegacyDataGuard.Assert(path);
            _files.Remove(path);
        }

        public void EnsureDirectory(string directory) => _directories.Add(directory);

        // --- test helpers -------------------------------------------------------------------

        /// <summary>Puts bytes somewhere directly, bypassing the store. Used to plant damage.</summary>
        public void Plant(string path, string contents) => _files[path] = contents;

        public string Peek(string path) => _files.TryGetValue(path, out string c) ? c : null;

        /// <summary>
        /// Replaces one substring in a stored file. Used to change a value without breaking the
        /// JSON, which is what a save edited by hand - or a single rotted byte inside a number -
        /// looks like, and the case the checksum is for.
        /// </summary>
        public void Rewrite(string path, string find, string replaceWith)
        {
            string contents = _files[path];
            if (!contents.Contains(find))
                throw new InvalidOperationException($"'{find}' is not in {path}; the test is not testing what it thinks.");

            _files[path] = contents.Replace(find, replaceWith);
        }

        /// <summary>Leaves the document valid and the checksum wrong.</summary>
        public void BreakChecksum(string path)
        {
            const string field = "\"snapshotChecksum\":\"";
            string contents = _files[path];
            int at = contents.IndexOf(field, StringComparison.Ordinal);
            if (at < 0)
                throw new InvalidOperationException($"{path} has no checksum field to break.");

            int start = at + field.Length;
            int end = contents.IndexOf('"', start);
            _files[path] = contents.Substring(0, start) + new string('0', end - start) + contents.Substring(end);
        }
    }
}
