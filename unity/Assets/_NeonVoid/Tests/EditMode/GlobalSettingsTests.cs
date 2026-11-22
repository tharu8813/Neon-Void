using System.Collections.Generic;
using NeonVoid.Progression;
using NeonVoid.Progression.Save;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// 전역 설정: that it is written like a save, and that destroying a vaccine cannot reach it.
    /// <para>
    /// [확정] docs/04 section 1: 전역 설정은 백신과 독립이며 백신 최종 파기에서도 보존된다. That
    /// is a claim about where the bytes are, so the test that matters destroys a vaccine for
    /// real and reads the settings back afterwards.
    /// </para>
    /// </summary>
    public sealed class GlobalSettingsTests
    {
        private const string Root = "C:/neonvoid-settings-test";

        private FakeSaveFileSystem _files;
        private GlobalSettingsLayout _layout;

        [SetUp]
        public void SetUp()
        {
            _files = new FakeSaveFileSystem();
            _layout = GlobalSettingsLayout.Under(Root);
        }

        private GlobalSettingsStore Store() => new GlobalSettingsStore(_files, _layout);

        private static KeyValuePair<string, string>[] OneOverride =>
            new[] { new KeyValuePair<string, string>("dash-binding", "<Keyboard>/leftShift") };

        // --- where it lives -------------------------------------------------------------------

        [Test]
        public void TheSettingsFileSitsBesideTheSlotsAndInsideNone()
        {
            // The whole guarantee in one path: a slot is a file in this directory, and so is
            // this, so rewriting one cannot rewrite the other.
            SaveSlotLayout slot = SaveSlotLayout.Under(Root);

            Assert.AreEqual(slot.Directory, _layout.Directory);
            Assert.AreNotEqual(slot.SnapshotPath, _layout.Path);
            StringAssert.EndsWith(GlobalSettingsLayout.FileName, _layout.Path);
        }

        [Test]
        public void TheSettingsFileIsNotOneOfTheFilesTheLegacyGuardProtects()
        {
            // It was, on the first attempt: settings.json is what the previous Python build
            // called its own, and [확정] docs/04 section 4 forbids touching those five names.
            // The guard threw on the first write; this keeps the name from drifting back.
            Assert.IsFalse(LegacyDataGuard.IsProtected(_layout.Path), _layout.Path);
            Assert.IsFalse(LegacyDataGuard.IsProtected(_layout.BackupPath), _layout.BackupPath);
            Assert.IsFalse(LegacyDataGuard.IsProtected(_layout.TempPath), _layout.TempPath);
        }

        [Test]
        public void ARootIsRequiredRatherThanDiscovered()
        {
            // The same guarantee UnitySaveRoot gives the slots: a test that never names a root
            // has no way to reach the player's real one.
            Assert.Throws<System.ArgumentException>(() => GlobalSettingsLayout.Under(null));
            Assert.Throws<System.ArgumentException>(() => GlobalSettingsLayout.Under("  "));
        }

        // --- reading and writing ---------------------------------------------------------------

        [Test]
        public void AFirstRunStartsFromDefaultsAndWritesNothingUntilItIsAsked()
        {
            GlobalSettingsStore store = Store();

            Assert.AreEqual(GlobalSettingsVerdict.Created, store.Open());
            Assert.IsEmpty(store.Document.Overrides());
            Assert.IsFalse(_files.Exists(_layout.Path), "an untouched settings screen wrote a file");
        }

        [Test]
        public void WhatIsSavedComesBackFromASecondStore()
        {
            GlobalSettingsStore store = Store();
            store.Open();
            store.Document.SetOverrides(OneOverride);

            Assert.IsTrue(store.Save(out string detail), detail);

            // A second store, so this is what is on disk and not what the one that wrote it
            // remembers.
            GlobalSettingsStore reopened = Store();
            Assert.AreEqual(GlobalSettingsVerdict.Loaded, reopened.Open());

            IReadOnlyList<KeyValuePair<string, string>> back = reopened.Document.Overrides();
            Assert.AreEqual(1, back.Count);
            Assert.AreEqual("dash-binding", back[0].Key);
            Assert.AreEqual("<Keyboard>/leftShift", back[0].Value);
        }

        [Test]
        public void EachSaveRaisesTheRevision()
        {
            GlobalSettingsStore store = Store();
            store.Open();

            store.Save(out _);
            int first = store.Document.revision;
            store.Save(out _);

            Assert.AreEqual(first + 1, store.Document.revision);
        }

        [Test]
        public void ResettingEverythingWritesAnEmptyList()
        {
            GlobalSettingsStore store = Store();
            store.Open();
            store.Document.SetOverrides(OneOverride);
            store.Save(out _);

            store.Document.SetOverrides(null);
            Assert.IsTrue(store.Save(out string detail), detail);

            GlobalSettingsStore reopened = Store();
            Assert.AreEqual(GlobalSettingsVerdict.Loaded, reopened.Open());
            Assert.IsEmpty(reopened.Document.Overrides());
        }

        // --- a file that is wrong ---------------------------------------------------------------

        [Test]
        public void ACorruptFileIsReportedAndLeftAlone()
        {
            // The bytes are the only copy of whatever the player configured. Rewriting them at
            // startup would destroy it, so the session runs on defaults instead.
            GlobalSettingsStore store = Store();
            store.Open();
            store.Document.SetOverrides(OneOverride);
            store.Save(out _);

            string original = _files.ReadAllText(_layout.Path);
            _files.WriteAllText(_layout.Path, original.Replace("<Keyboard>/leftShift", "<Keyboard>/z"));

            GlobalSettingsStore reopened = Store();
            Assert.AreEqual(GlobalSettingsVerdict.Unreadable, reopened.Open());
            Assert.IsNotEmpty(reopened.Detail);
            Assert.IsEmpty(reopened.Document.Overrides(), "a corrupt file was read as settings");
            StringAssert.Contains(
                "<Keyboard>/z", _files.ReadAllText(_layout.Path), "the corrupt file was overwritten");
        }

        [Test]
        public void AFileFromAnotherProductIsNotRead()
        {
            _files.EnsureDirectory(_layout.Directory);
            _files.WriteAllText(_layout.Path, "{\"schemaVersion\":1,\"product\":\"SomethingElse\"}");

            GlobalSettingsStore store = Store();
            Assert.AreEqual(GlobalSettingsVerdict.Unreadable, store.Open());
            StringAssert.Contains("SomethingElse", store.Detail);
        }

        [Test]
        public void TwoOverridesForOneBindingAreRefusedRatherThanOrderDependent()
        {
            var document = GlobalSettingsDocument.Fresh();
            document.keyBindings.Add(new KeyBindingOverrideRecord { bindingId = "a", path = "<Keyboard>/q" });
            document.keyBindings.Add(new KeyBindingOverrideRecord { bindingId = "a", path = "<Keyboard>/z" });

            Assert.Throws<System.InvalidOperationException>(() => document.Validate());
        }

        [Test]
        public void TheChecksumCoversTheBindings()
        {
            var document = GlobalSettingsDocument.Fresh();
            document.SetOverrides(OneOverride);
            string text = GlobalSettingsStore.Stamp(document);

            Assert.IsNotNull(GlobalSettingsStore.Parse(text, out _));
            Assert.IsNull(
                GlobalSettingsStore.Parse(text.Replace("leftShift", "rightShift"), out string why),
                "a changed key slipped past the checksum");
            Assert.IsNotEmpty(why);
        }

        // --- 백신 최종 파기에서도 보존 ---------------------------------------------------------

        [Test]
        public void DestroyingAVaccineDoesNotTouchTheSettings()
        {
            // [확정] docs/04 section 1 and section 2: the fourth death destroys the vaccine's
            // growth, skills, DB and records - and 전역 설정 is none of those.
            GlobalSettingsStore settings = Store();
            settings.Open();
            settings.Document.SetOverrides(OneOverride);
            Assert.IsTrue(settings.Save(out string detail), detail);

            int revisionBefore = settings.Document.revision;
            string bytesBefore = _files.ReadAllText(_layout.Path);

            var slot = new SaveStore(_files, SaveSlotLayout.Under(Root));
            slot.Open();
            slot.CreateSlot("vaccine-1", "create:vaccine-1");
            slot.Commit(new AllocateRunIdCommand("settings-test"));

            for (int sequence = 1; sequence <= VaccineSaveState.DesignDeathsBeforeFinalDestruction; ++sequence)
            {
                SaveCommitResult commit = slot.Commit(
                    new RegisterDeathCommand(DeathId.For(1L, 1, sequence), DeathSettlement.None));
                Assert.IsTrue(commit.Acknowledged, commit.Detail);
            }

            Assert.IsFalse(slot.Snapshot.vaccine.alive, "the vaccine was not destroyed, so this proves nothing");

            GlobalSettingsStore reopened = Store();
            Assert.AreEqual(GlobalSettingsVerdict.Loaded, reopened.Open());
            Assert.AreEqual(revisionBefore, reopened.Document.revision);
            Assert.AreEqual(1, reopened.Document.Overrides().Count, "최종 파기 took the key bindings with it");
            Assert.AreEqual(bytesBefore, _files.ReadAllText(_layout.Path), "the settings file was rewritten");
        }
    }
}
