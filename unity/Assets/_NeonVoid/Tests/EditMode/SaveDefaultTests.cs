using System.IO;
using NeonVoid.Progression.Save;
using NeonVoid.UI;
using NUnit.Framework;
using UnityEngine;

namespace NeonVoid.Tests
{
    /// <summary>
    /// That nothing writes to the player's save until someone turns it on, and that the player
    /// is told when it is off.
    /// <para>
    /// This exists because the opposite cost two clones. The scenes shipped with permanent
    /// writes on, the in-memory guard a session applied was never saved back to the scene, and
    /// so every fresh Editor had them on again - while [확정] docs/04 section 2 makes 복제 회복
    /// impossible by any means. A session played with writes off loses only that session.
    /// </para>
    /// </summary>
    public sealed class SaveDefaultTests
    {
        private static string ScenesDirectory =>
            Path.Combine(Application.dataPath, "_NeonVoid", "Scenes");

        // --- the component's own default ------------------------------------------------------

        [Test]
        public void AFreshSaveDirectorWritesNothingUntilItIsTurnedOn()
        {
            // The scene builders never assign the flag, so this default is what a regenerated
            // scene gets. Left inactive so Awake does not run and nothing opens a slot.
            var host = new GameObject("Save Director Default");
            host.SetActive(false);

            try
            {
                VaccineSaveDirector director = host.AddComponent<VaccineSaveDirector>();

                Assert.AreEqual(
                    SaveCommitReadiness.SwitchedOff, director.Readiness,
                    "a newly added save director would write to the player's real save");
                Assert.IsFalse(director.CanCommit);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        // --- the shipped scenes ---------------------------------------------------------------

        [TestCase("Prototype_Loop.unity")]
        [TestCase("Prototype_Bootstrap.unity")]
        public void AShippedSceneHasPermanentWritesOff(string sceneName)
        {
            string path = Path.Combine(ScenesDirectory, sceneName);
            Assert.IsTrue(File.Exists(path), $"{sceneName} is not where this test looks for it");

            string text = File.ReadAllText(path);
            Assume.That(
                text, Does.Contain("writeToRealSaveDirectory:"),
                $"{sceneName} has no save director, so there is nothing to check");

            StringAssert.DoesNotContain(
                "writeToRealSaveDirectory: 1", text,
                $"{sceneName} would spend a real clone the moment anyone enters Play mode");
        }

        // --- what the player is told -----------------------------------------------------------

        [Test]
        public void ASessionThatIsBeingWrittenDownSaysNothing()
        {
            // The notice is for the exceptional case. A line that was always on screen would be
            // read as decoration and stop carrying information.
            Assert.IsFalse(HudSaveNotice.For(SaveCommitReadiness.Committing).Shown);
        }

        [Test]
        public void WritesSwitchedOffAreStatedPlainlyAndNotAsAFailure()
        {
            HudSaveNotice notice = HudSaveNotice.For(SaveCommitReadiness.SwitchedOff);

            Assert.IsTrue(notice.Shown, "the default now loses the session's progress silently");
            Assert.IsFalse(notice.IsFailure, "a deliberate setting read as something gone wrong");
            Assert.IsNotEmpty(notice.Text);
        }

        [TestCase(SaveCommitReadiness.Unavailable)]
        [TestCase(SaveCommitReadiness.Halted)]
        public void ASlotThatCannotBeWrittenReadsAsAFailure(SaveCommitReadiness readiness)
        {
            // These two are the dangerous ones: writes were asked for, so the player believes
            // the run is being kept, and it is not.
            HudSaveNotice notice = HudSaveNotice.For(readiness);

            Assert.IsTrue(notice.Shown);
            Assert.IsTrue(notice.IsFailure);
            Assert.AreNotEqual(
                HudSaveNotice.For(SaveCommitReadiness.SwitchedOff).Text, notice.Text,
                "a broken save reads exactly like the one that was switched off on purpose");
        }
    }
}
