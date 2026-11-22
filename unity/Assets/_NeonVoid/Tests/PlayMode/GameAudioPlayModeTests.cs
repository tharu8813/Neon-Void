using System;
using System.Collections;
using System.IO;
using NeonVoid.Combat;
using NeonVoid.Player;
using NeonVoid.Presentation;
using NeonVoid.Progression.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace NeonVoid.Tests.PlayModeTests
{
    /// <summary>
    /// Sound in a running scene: the pool, the volumes, and the one cue that must always land.
    /// <para>
    /// [초안] docs/05 section 6 asks for 동시 발음 제한과 오디오 풀 검증 and for the four
    /// volumes to be separate and remembered. The rules are tested without Unity in
    /// <c>AudioMixTests</c>; what needs a scene is that they reach real AudioSources and a real
    /// settings file.
    /// </para>
    /// </summary>
    public sealed class GameAudioPlayModeTests
    {
        private string _root;
        private PrototypeArena _arena;
        private GameObject _audioObject;
        private GameObject _bindingObject;
        private InputActionAsset _asset;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "neonvoid-audio-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (_audioObject != null)
                UnityEngine.Object.Destroy(_audioObject);
            if (_bindingObject != null)
                UnityEngine.Object.Destroy(_bindingObject);
            if (_asset != null)
                UnityEngine.Object.Destroy(_asset);

            _audioObject = null;
            _bindingObject = null;
            _asset = null;

            _arena?.Dispose();
            _arena = null;

            if (_root != null && Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        /// <summary>
        /// A binding director over a throwaway settings file, because that is where 음량 is
        /// kept. docs/04 section 4 fails a test that reaches the player's own.
        /// </summary>
        private InputBindingDirector Settings()
        {
            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap map = _asset.AddActionMap(KeyBindings.MapName);
            map.AddAction("Dash", InputActionType.Button, "<Keyboard>/space");

            var store = new GlobalSettingsStore(
                new DiskSaveFileSystem(), GlobalSettingsLayout.Under(_root));
            store.Open();

            _bindingObject = new GameObject("Input Binding Director");
            _bindingObject.SetActive(false);
            var director = _bindingObject.AddComponent<InputBindingDirector>();
            director.Configure(_asset);
            director.UseSettings(store);
            _bindingObject.SetActive(true);
            return director;
        }

        private GameAudio Audio(InputBindingDirector settings, CombatActor vaccine)
        {
            _audioObject = new GameObject("Game Audio");
            _audioObject.SetActive(false);
            var audio = _audioObject.AddComponent<GameAudio>();
            audio.Configure(settings, vaccine);
            _audioObject.SetActive(true);
            return audio;
        }

        // --- the pool -----------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ThePoolIsBuiltOnceAndDoesNotGrow()
        {
            GameAudio audio = Audio(Settings(), null);
            int voices = audio.Voices;

            Assert.Greater(voices, 0);

            for (int i = 0; i < 200; ++i)
                audio.Play(AudioCue.UiClick);

            yield return null;

            Assert.AreEqual(voices, audio.Voices, "the pool grew under load");
            Assert.AreEqual(voices, _audioObject.transform.childCount, "a source escaped the pool");
        }

        [UnityTest]
        public IEnumerator OneCueFiredEveryFrameIsNotPlayedEveryFrame()
        {
            // 동시 발음 제한. A hundred copies of one click is a buzz, not a loud click.
            GameAudio audio = Audio(Settings(), null);

            for (int i = 0; i < 50; ++i)
                audio.Play(AudioCue.Hit);

            yield return null;

            Assert.AreEqual(50, audio.Requested);
            Assert.AreEqual(1, audio.Played, "the interval let a cue through more than once in a tick");
        }

        // --- the volumes --------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AMutedBusClaimsNoVoiceAtAll()
        {
            // Silent has to mean "did not play", not "played at zero": a muted bus that still
            // took voices would starve one that is audible.
            GameAudio audio = Audio(Settings(), null);
            audio.ApplyMix(audio.Mix.WithMuted(AudioBus.Ui, true));

            audio.Play(AudioCue.UiClick);
            yield return null;

            Assert.AreEqual(0, audio.Played);
            Assert.IsFalse(audio.LastClaim.Ok);

            // And the bus that is not muted still sounds.
            Assert.IsTrue(audio.Play(AudioCue.PlayerHurt).Ok);
        }

        [UnityTest]
        public IEnumerator MutingMasterSilencesEveryBus()
        {
            GameAudio audio = Audio(Settings(), null);
            audio.ApplyMix(audio.Mix.WithMuted(AudioBus.Master, true));

            audio.Play(AudioCue.UiClick);
            audio.Play(AudioCue.PlayerHurt);
            audio.Play(AudioCue.BossWarning);
            yield return null;

            Assert.AreEqual(0, audio.Played);
        }

        [UnityTest]
        public IEnumerator AVolumeIsWrittenDownAndComesBack()
        {
            // [초안] docs/05 section 6: 음소거 저장/복구, and the volumes with it.
            InputBindingDirector settings = Settings();
            GameAudio audio = Audio(settings, null);

            Assert.IsTrue(
                audio.SetMix(
                    audio.Mix.WithPercent(AudioBus.Effects, 20).WithMuted(AudioBus.Music, true),
                    out string detail),
                detail);
            yield return null;

            var reopened = new GlobalSettingsStore(
                new DiskSaveFileSystem(), GlobalSettingsLayout.Under(_root));
            Assert.AreEqual(GlobalSettingsVerdict.Loaded, reopened.Open());

            AudioMix back = reopened.Document.Mix();
            Assert.AreEqual(20, back.PercentOf(AudioBus.Effects));
            Assert.IsTrue(back.MusicMuted);
            Assert.IsFalse(back.EffectsMuted);
        }

        [UnityTest]
        public IEnumerator DraggingASliderDoesNotWriteAFilePerStep()
        {
            // ApplyMix is what a slider sends; a file transaction per step would be a hundred
            // for one drag.
            InputBindingDirector settings = Settings();
            GameAudio audio = Audio(settings, null);

            Assert.IsTrue(audio.SetMix(audio.Mix.WithPercent(AudioBus.Ui, 50), out string detail), detail);
            int revision = settings.Settings.Document.revision;

            for (int percent = 50; percent >= 0; --percent)
                audio.ApplyMix(audio.Mix.WithPercent(AudioBus.Ui, percent));

            yield return null;
            Assert.AreEqual(revision, settings.Settings.Document.revision, "a drag wrote the file");

            Assert.IsTrue(audio.PersistMix(out detail), detail);
            Assert.AreEqual(revision + 1, settings.Settings.Document.revision);
            Assert.AreEqual(0, settings.Settings.Document.Mix().PercentOf(AudioBus.Ui));
        }

        [UnityTest]
        public IEnumerator PersistingAnUnchangedMixWritesNothing()
        {
            InputBindingDirector settings = Settings();
            GameAudio audio = Audio(settings, null);

            int revision = settings.Settings.Document.revision;
            Assert.IsTrue(audio.PersistMix(out string detail), detail);

            Assert.AreEqual(revision, settings.Settings.Document.revision, "closing 설정 bumped the file");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheVolumeFromTheFileIsInForceBeforeAnythingPlays()
        {
            InputBindingDirector settings = Settings();
            settings.Settings.Document.SetMix(AudioMix.Default.WithPercent(AudioBus.Effects, 7));
            settings.Settings.Save(out _);

            GameAudio audio = Audio(settings, null);
            yield return null;

            Assert.AreEqual(7, audio.Mix.PercentOf(AudioBus.Effects));
        }

        // --- it reads results, it does not make them -----------------------------------------

        [UnityTest]
        public IEnumerator BeingHitSoundsAndTheSoundChangesNothing()
        {
            // docs/05 section 5 forbids the effects layer from touching HP or rewards. The
            // audio subscribes to a resolved outcome and plays it.
            _arena = PrototypeArena.Build();
            GameAudio audio = Audio(Settings(), _arena.Player);
            yield return _arena.Tick(1);

            float healthBefore = _arena.Player.Health.Current;
            int playedBefore = audio.Played;

            _arena.Player.ReceiveDamage(
                new DamageRequest { Flat = 10f, Type = DamageType.True }, CombatStats.StartingVaccine());
            yield return _arena.Tick(1);

            Assert.AreEqual(playedBefore + 1, audio.Played, "being hit made no sound");
            Assert.AreEqual(
                healthBefore - 10f, _arena.Player.Health.Current, 0.001f,
                "the audio layer changed the damage");
        }

        [UnityTest]
        public IEnumerator AHitThatRemovedNoHealthMakesNoSound()
        {
            // A fully absorbed hit is not the 피격 sound; the shield has its own story and
            // playing the hurt cue would tell the player they lost health they still have.
            _arena = PrototypeArena.Build();
            GameAudio audio = Audio(Settings(), _arena.Player);
            yield return _arena.Tick(1);

            int playedBefore = audio.Played;

            _arena.Player.ReceiveDamage(
                new DamageRequest { Flat = 0f, Type = DamageType.True }, CombatStats.StartingVaccine());
            yield return _arena.Tick(1);

            Assert.AreEqual(playedBefore, audio.Played);
        }
    
        // --- the cues the game fires ---------------------------------------------------------

        [UnityTest]
        public IEnumerator ADashSoundsOnceWhenItStartsAndNotWhileItRuns()
        {
            // The rising edge. A dash lasts several ticks, and a sound per tick of it would be
            // the buzz the per-cue interval exists to stop - but the interval should not be
            // what is doing the work here.
            _arena = PrototypeArena.Build();
            GameAudio audio = Audio(Settings(), _arena.Player);
            yield return _arena.Tick(1);

            int dashes = 0;
            _arena.Driver.DashStarted += _ => ++dashes;

            int playedBefore = audio.Played;
            _arena.Driver.RequestDash(new Vector2(1f, 0f));
            yield return _arena.Tick(20);

            Assert.AreEqual(1, dashes, "the dash was reported more than once");
            Assert.AreEqual(playedBefore + 1, audio.Played, "the dash made no sound, or too many");
        }

        [UnityTest]
        public IEnumerator ABossWarningSoundsAndTheTelegraphDoesNotDependOnIt()
        {
            // [초안] docs/05 section 6: 위험 공격은 음향 없이도 피할 수 있어야 한다. The sound
            // is a second channel; the telegraph is the one that must always be there.
            _arena = PrototypeArena.Build();

            CombatStats playerStats = PrototypeArena.PlayerStats();
            playerStats.MaxHealth = 1_000_000f;
            _arena.Player.Configure(PrototypeArena.PlayerTeam, playerStats);

            GameObject bossObject = _arena.Track(GameObject.CreatePrimitive(PrimitiveType.Capsule));
            bossObject.transform.position = _arena.PlayerObject.transform.position + new Vector3(0f, 0f, 4f);

            CombatStats bossStats = CombatStats.StartingVaccine();
            bossStats.MaxHealth = 500_000f;
            var boss = bossObject.AddComponent<CombatActor>();
            boss.Configure(PrototypeArena.EnemyTeam, bossStats);

            var brain = bossObject.AddComponent<NeonVoid.Combat.AI.AreaBossBrain>();
            brain.Configure(boss, _arena.Player);
            brain.UseCycle(NeonVoid.Combat.AI.BossCatalog.CoreWatcher());
            brain.RefreshHostiles();

            GameAudio audio = Audio(Settings(), _arena.Player);

            // 효과음 off before the boss ever acts: the telegraph still has to appear.
            audio.ApplyMix(audio.Mix.WithMuted(AudioBus.Effects, true));

            var warning = bossObject.AddComponent<NeonVoid.Presentation.BossWarningSound>();
            warning.Configure(audio);

            int playedBefore = audio.Played;
            bool telegraphed = false;
            for (int i = 0; i < 400 && !telegraphed; ++i)
            {
                yield return _arena.Tick(1);
                telegraphed = brain.TryGetTelegraph(out _);
            }

            Assert.IsTrue(telegraphed, "the boss never telegraphed, so this proves nothing");
            Assert.GreaterOrEqual(warning.Warnings, 1, "the warning never reached the sound");
            Assert.AreEqual(playedBefore, audio.Played, "a muted 효과음 still played the warning");

            // Unmuted, the same warning sounds.
            audio.ApplyMix(audio.Mix.WithMuted(AudioBus.Effects, false));
            Assert.IsTrue(audio.Play(AudioCue.BossWarning).Ok);
        }

        [UnityTest]
        public IEnumerator AMissingAudioDoesNotStopTheBossFromWarning()
        {
            // The component must survive having nothing to play into: a scene with no GameAudio
            // is every test fixture in this project.
            _arena = PrototypeArena.Build();

            GameObject bossObject = _arena.Track(GameObject.CreatePrimitive(PrimitiveType.Capsule));
            bossObject.transform.position = _arena.PlayerObject.transform.position + new Vector3(0f, 0f, 4f);

            CombatStats bossStats = CombatStats.StartingVaccine();
            bossStats.MaxHealth = 500_000f;
            var boss = bossObject.AddComponent<CombatActor>();
            boss.Configure(PrototypeArena.EnemyTeam, bossStats);

            var brain = bossObject.AddComponent<NeonVoid.Combat.AI.AreaBossBrain>();
            brain.Configure(boss, _arena.Player);
            brain.UseCycle(NeonVoid.Combat.AI.BossCatalog.CoreWatcher());
            brain.RefreshHostiles();

            var warning = bossObject.AddComponent<NeonVoid.Presentation.BossWarningSound>();
            warning.Configure(null);

            bool telegraphed = false;
            for (int i = 0; i < 400 && !telegraphed; ++i)
            {
                yield return _arena.Tick(1);
                telegraphed = brain.TryGetTelegraph(out _);
            }

            Assert.IsTrue(telegraphed);
            Assert.GreaterOrEqual(warning.Warnings, 1);
        }
    }
}
