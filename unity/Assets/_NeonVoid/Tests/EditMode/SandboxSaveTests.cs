using System;
using System.IO;
using System.Reflection;
using NeonVoid.Missions;
using NeonVoid.Progression.Save;
using NeonVoid.UI;
using NUnit.Framework;
using UnityEngine;

namespace NeonVoid.Tests
{
    /// <summary>
    /// 샌드박스 저장: a real store in a path of its own, so a 의뢰 can be finished without the
    /// player's save being touched.
    /// <para>
    /// <b>Why it had to exist.</b> D-020 switched real writes off by default, which was right -
    /// a death with writes on spends a 복제 and [확정] docs/04 section 2 makes 복제 회복
    /// impossible. But an extraction is permanent in every part, so
    /// <see cref="PrototypeMissionCommits"/> refuses 철수 when there is no store - also right.
    /// Together those two correct rules meant <b>a run could never be completed</b> in the
    /// shipped scenes. The sandbox is the third state that was missing: written down, and
    /// disposable.
    /// </para>
    /// <para>
    /// Every path here is built from a temporary root. [확정] docs/04 section 4: 테스트용 저장
    /// 서비스는 반드시 임시 디렉터리를 주입받으며 실제 사용자 저장 디렉터리에 접근하면 테스트
    /// 실패. Nothing in this file calls <see cref="UnitySaveRoot.Resolve"/>.
    /// </para>
    /// </summary>
    public sealed class SandboxSaveTests
    {
        private static string FakeRoot => Path.Combine(Path.GetTempPath(), "nv-sandbox-tests");

        // --- where it sits ---------------------------------------------------------------------

        [Test]
        public void TheSandboxIsBesideTheRealRootAndNotTheRealRoot()
        {
            string sandbox = UnitySaveRoot.SandboxUnder(FakeRoot);

            Assert.AreNotEqual(FakeRoot, sandbox);
            StringAssert.StartsWith(FakeRoot, sandbox, "the sandbox left the save root entirely");
            Assert.AreEqual(UnitySaveRoot.SandboxDirectoryName, new DirectoryInfo(sandbox).Name);
        }

        [Test]
        public void TheSandboxAndTheRealSaveShareNoDirectory()
        {
            // The whole point of a separate root rather than a separate slot id: no sandbox file
            // can ever land on a real one, even if every name below were wrong.
            SaveSlotLayout real = SaveSlotLayout.Under(FakeRoot);
            SaveSlotLayout sandbox = SaveSlotLayout.Under(UnitySaveRoot.SandboxUnder(FakeRoot));

            Assert.AreNotEqual(real.Directory, sandbox.Directory);
            Assert.AreNotEqual(real.SnapshotPath, sandbox.SnapshotPath);
            Assert.AreNotEqual(real.BackupPath, sandbox.BackupPath);
            Assert.AreNotEqual(real.TempPath, sandbox.TempPath);
        }

        [Test]
        public void ASandboxInsideTheLegacyDirectoryIsRefused()
        {
            // The legacy Python files are read-only to this project in every direction. A
            // sandbox is still a write, so it gets the same refusal a real save would.
            string legacy = Path.Combine("C:", "Users", "someone", "AppData", "Local", "NeonVoid");

            Assert.Throws<InvalidOperationException>(() => UnitySaveRoot.SandboxUnder(legacy));
            Assert.Throws<InvalidOperationException>(
                () => UnitySaveRoot.SandboxUnder(Path.Combine(legacy, "deeper")));
        }

        [Test]
        public void ASandboxNeedsARootToSitBeside()
        {
            Assert.Throws<ArgumentException>(() => UnitySaveRoot.SandboxUnder(null));
            Assert.Throws<ArgumentException>(() => UnitySaveRoot.SandboxUnder("   "));
        }

        [Test]
        public void TheSandboxFilesAreNotLegacyUserData()
        {
            // LegacyDataGuard refuses the five Python names wherever they appear; SaveSlotLayout
            // already asserts it, and this says out loud that the sandbox is covered too.
            SaveSlotLayout sandbox = SaveSlotLayout.Under(UnitySaveRoot.SandboxUnder(FakeRoot));

            Assert.IsFalse(LegacyDataGuard.IsProtected(sandbox.SnapshotPath));
            Assert.IsFalse(LegacyDataGuard.IsProtected(sandbox.BackupPath));
            Assert.IsFalse(LegacyDataGuard.IsProtected(sandbox.TempPath));
            Assert.IsFalse(LegacyDataGuard.IsProtected(sandbox.QuarantinePath(1)));
        }

        // --- what the component defaults to ------------------------------------------------------

        [Test]
        public void AFreshDirectorPrefersTheSandboxOverWritingNothing()
        {
            // Left inactive so Awake does not run: nothing here opens a slot on disk.
            var host = new GameObject("Save Director Sandbox Default");
            host.SetActive(false);

            try
            {
                VaccineSaveDirector director = host.AddComponent<VaccineSaveDirector>();

                Assert.IsTrue(
                    (bool)typeof(VaccineSaveDirector)
                        .GetField(
                            "useSandboxSaveWhenRealWritesAreOff",
                            BindingFlags.NonPublic | BindingFlags.Instance)
                        .GetValue(director),
                    "with this off and real writes off, 철수 can never be committed");

                // Still not the player's save. D-020's default is untouched.
                Assert.IsFalse(
                    (bool)typeof(VaccineSaveDirector)
                        .GetField(
                            "writeToRealSaveDirectory",
                            BindingFlags.NonPublic | BindingFlags.Instance)
                        .GetValue(director),
                    "a newly added save director would spend a real 복제");

                // Nothing has been prepared, so there is no store and nothing to say yet.
                Assert.AreEqual(SaveCommitReadiness.SwitchedOff, director.Readiness);
                Assert.IsFalse(director.IsSandbox);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        // --- what the player is told ---------------------------------------------------------

        [Test]
        public void ASandboxSessionSaysSoAndIsNotAFailure()
        {
            HudSaveNotice notice = HudSaveNotice.For(SaveCommitReadiness.Sandbox);

            Assert.IsTrue(notice.Shown, "a session written somewhere disposable said nothing");
            Assert.IsFalse(notice.IsFailure, "the sandbox is a setting, not something gone wrong");
            StringAssert.Contains("샌드박스", notice.Text);
        }

        [Test]
        public void TheSandboxDoesNotReadLikeWritesBeingOff()
        {
            // They are different situations: one records the session, one does not, and only
            // one of them can finish a 의뢰.
            Assert.AreNotEqual(
                HudSaveNotice.For(SaveCommitReadiness.SwitchedOff).Text,
                HudSaveNotice.For(SaveCommitReadiness.Sandbox).Text);
        }

        [Test]
        public void TheSandboxDoesNotReadLikeTheRealSaveEither()
        {
            // A session being written to the player's own file says nothing at all; the sandbox
            // must not borrow that silence, or the two become indistinguishable on screen.
            Assert.IsFalse(HudSaveNotice.For(SaveCommitReadiness.Committing).Shown);
            Assert.IsTrue(HudSaveNotice.For(SaveCommitReadiness.Sandbox).Shown);
        }

        [Test]
        public void TheReadinessValuesStayWhereTheyWere()
        {
            // Adding a case must not renumber the others: these values are compared and logged,
            // and a shifted one would turn a sandbox into a halt in an old log.
            Assert.AreEqual(0, (int)SaveCommitReadiness.Committing);
            Assert.AreEqual(1, (int)SaveCommitReadiness.SwitchedOff);
            Assert.AreEqual(2, (int)SaveCommitReadiness.Unavailable);
            Assert.AreEqual(3, (int)SaveCommitReadiness.Halted);
            Assert.AreEqual(4, (int)SaveCommitReadiness.Sandbox);
        }

        // --- why it exists ---------------------------------------------------------------------

        [Test]
        public void WithNoStoreAtAllAnExtractionIsStillRefused()
        {
            // The rule the sandbox works around rather than weakens: everything an extraction
            // produces is permanent, so it cannot happen with nothing to write to.
            MissionCommitResult result = PrototypeMissionCommits.Instance
                .CommitExtraction(1L, 1, 0, new string[0]);

            Assert.IsFalse(result.Committed);
            StringAssert.Contains("저장 커밋 승인 후", result.Detail);
        }

        [Test]
        public void PurificationIsStillPlayableWithNoStore()
        {
            // 의뢰 XP is temporary ([확정] C09), so the area loop stays walkable even with the
            // sandbox switched off as well. Only the permanent step is refused.
            MissionCommitResult result = PrototypeMissionCommits.Instance
                .CommitPurification(1L, "cpu");

            Assert.IsTrue(result.Committed);
        }
    }
}
