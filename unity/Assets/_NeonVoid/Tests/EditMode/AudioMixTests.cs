using NeonVoid.Presentation;
using NeonVoid.Progression.Save;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// 음량 and 음소거, and the two limits on how many sounds may play.
    /// <para>
    /// [초안] docs/05 section 6: BGM·효과음·UI·마스터 볼륨 분리, 음소거 저장/복구, 동시 발음
    /// 제한과 오디오 풀 검증.
    /// </para>
    /// </summary>
    public sealed class AudioMixTests
    {
        // --- the mix -------------------------------------------------------------------------

        [Test]
        public void AMixNobodyConfiguredIsFullVolumeAndNotSilence()
        {
            // A default struct reads as full. The opposite - a game that ships silent because
            // nothing filled in a number - is the failure that is hardest to notice.
            AudioMix untouched = default;

            Assert.AreEqual(AudioMix.FullPercent, untouched.PercentOf(AudioBus.Effects));
            Assert.AreEqual(1f, untouched.EffectiveVolume(AudioBus.Effects), 0.0001f);
            Assert.IsFalse(untouched.IsSilent(AudioBus.Ui));
        }

        [Test]
        public void MasterMultipliesTheOtherBuses()
        {
            AudioMix mix = AudioMix.Default
                .WithPercent(AudioBus.Master, 50)
                .WithPercent(AudioBus.Effects, 50);

            Assert.AreEqual(0.25f, mix.EffectiveVolume(AudioBus.Effects), 0.0001f);
            Assert.AreEqual(0.5f, mix.EffectiveVolume(AudioBus.Master), 0.0001f,
                "마스터 multiplied itself");
        }

        [Test]
        public void MutingMasterSilencesEverything()
        {
            AudioMix mix = AudioMix.Default.WithMuted(AudioBus.Master, true);

            foreach (AudioBus bus in AudioMix.All)
                Assert.IsTrue(mix.IsSilent(bus), bus.ToString());
        }

        [Test]
        public void MutingOneBusLeavesTheOthersAlone()
        {
            AudioMix mix = AudioMix.Default.WithMuted(AudioBus.Music, true);

            Assert.IsTrue(mix.IsSilent(AudioBus.Music));
            Assert.IsFalse(mix.IsSilent(AudioBus.Effects));
            Assert.IsFalse(mix.IsSilent(AudioBus.Ui));
        }

        [Test]
        public void MutingKeepsTheVolumeSoUnmutingGivesItBack()
        {
            // [초안] docs/05 section 6 asks for 음소거 저장/복구 beside the volumes. A mute
            // implemented as "set it to zero" cannot give the player back what they had.
            AudioMix mix = AudioMix.Default.WithPercent(AudioBus.Effects, 35);
            AudioMix muted = mix.WithMuted(AudioBus.Effects, true);

            Assert.AreEqual(35, muted.PercentOf(AudioBus.Effects), "the level was thrown away");
            Assert.AreEqual(0f, muted.EffectiveVolume(AudioBus.Effects), 0.0001f);
            Assert.AreEqual(35, muted.WithMuted(AudioBus.Effects, false).PercentOf(AudioBus.Effects));
        }

        [Test]
        public void AVolumeNeverLeavesItsRange()
        {
            Assert.AreEqual(0, AudioMix.Default.WithPercent(AudioBus.Ui, -40).PercentOf(AudioBus.Ui));
            Assert.AreEqual(
                AudioMix.FullPercent,
                AudioMix.Default.WithPercent(AudioBus.Ui, 400).PercentOf(AudioBus.Ui));
        }

        [Test]
        public void ChangingOneBusLeavesTheRest()
        {
            AudioMix mix = AudioMix.Default
                .WithPercent(AudioBus.Music, 10)
                .WithMuted(AudioBus.Ui, true)
                .WithPercent(AudioBus.Effects, 70);

            Assert.AreEqual(10, mix.PercentOf(AudioBus.Music));
            Assert.AreEqual(70, mix.PercentOf(AudioBus.Effects));
            Assert.IsTrue(mix.UiMuted);
            Assert.AreEqual(AudioMix.FullPercent, mix.PercentOf(AudioBus.Master));
        }

        [Test]
        public void TheFourBusesAreTheOnesTheDocumentNames()
        {
            // [초안] docs/05 section 6: BGM·효과음·UI·마스터.
            Assert.AreEqual(4, AudioMix.All.Length);
            CollectionAssert.AreEquivalent(
                new[] { AudioBus.Master, AudioBus.Music, AudioBus.Effects, AudioBus.Ui }, AudioMix.All);

            foreach (AudioBus bus in AudioMix.All)
                Assert.IsNotEmpty(AudioMix.LabelOf(bus), bus.ToString());
        }

        // --- it survives a round trip through the settings file ------------------------------

        [Test]
        public void TheMixComesBackFromWhatIsWrittenDown()
        {
            AudioMix mix = AudioMix.Default
                .WithPercent(AudioBus.Master, 80)
                .WithPercent(AudioBus.Music, 0)
                .WithMuted(AudioBus.Ui, true);

            var document = GlobalSettingsDocument.Fresh();
            document.SetMix(mix);
            string text = GlobalSettingsStore.Stamp(document);

            GlobalSettingsDocument parsed = GlobalSettingsStore.Parse(text, out string why);
            Assert.IsNotNull(parsed, why);

            AudioMix back = parsed.Mix();
            Assert.AreEqual(80, back.PercentOf(AudioBus.Master));
            Assert.AreEqual(0, back.PercentOf(AudioBus.Music));
            Assert.IsTrue(back.UiMuted);
            Assert.IsFalse(back.EffectsMuted);
        }

        [Test]
        public void AVolumeOutsideItsRangeIsRefusedByTheFileRatherThanClamped()
        {
            // Clamping on read would hide a file written by something that does not know the
            // rule. The store's answer to a file it does not understand is to say so.
            var document = GlobalSettingsDocument.Fresh();
            document.audio.effectsPercent = 500;

            Assert.Throws<System.InvalidOperationException>(() => document.Validate());
        }

        // --- 동시 발음 제한 --------------------------------------------------------------------

        [Test]
        public void TheSameCueWillNotSoundTwiceInTheSameInstant()
        {
            // A hit that happens sixty times a second played sixty times is a buzz, not a
            // loud hit.
            var limiter = new VoiceLimiter(voices: 8, sameCueIntervalTicks: 6);

            Assert.AreEqual(VoiceVerdict.Taken, limiter.Claim("hit", 0L, 6).Verdict);
            Assert.AreEqual(VoiceVerdict.TooSoon, limiter.Claim("hit", 1L, 6).Verdict);
            Assert.AreEqual(VoiceVerdict.TooSoon, limiter.Claim("hit", 5L, 6).Verdict);
            Assert.AreEqual(VoiceVerdict.Taken, limiter.Claim("hit", 6L, 6).Verdict);
        }

        [Test]
        public void TheIntervalIsPerCueAndNotGlobal()
        {
            // Two different things happening at once must both be heard.
            var limiter = new VoiceLimiter(voices: 8, sameCueIntervalTicks: 6);

            Assert.IsTrue(limiter.Claim("hit", 0L, 6).Ok);
            Assert.IsTrue(limiter.Claim("dash", 0L, 6).Ok, "a dash was swallowed by a hit");
        }

        [Test]
        public void TwoCuesNeverShareAVoiceWhileBothAreSounding()
        {
            var limiter = new VoiceLimiter(voices: 4, sameCueIntervalTicks: 0);

            int first = limiter.Claim("a", 0L, 30).Voice;
            int second = limiter.Claim("b", 0L, 30).Voice;

            Assert.AreNotEqual(first, second);
            Assert.AreEqual(2, limiter.Busy(0L));
        }

        [Test]
        public void AVoiceComesBackWhenItsSoundIsOver()
        {
            var limiter = new VoiceLimiter(voices: 1, sameCueIntervalTicks: 0);

            int first = limiter.Claim("a", 0L, 10).Voice;
            Assert.AreEqual(1, limiter.Busy(0L));
            Assert.AreEqual(0, limiter.Busy(10L));

            Assert.AreEqual(VoiceVerdict.Taken, limiter.Claim("b", 10L, 10).Verdict);
            Assert.AreEqual(first, limiter.Claim("c", 20L, 10).Voice, "the pool grew");
        }

        [Test]
        public void AFullPoolStealsTheVoiceClosestToFinishingRatherThanDroppingTheNewSound()
        {
            // A busy moment going quiet is worse than one sound being cut near its end.
            var limiter = new VoiceLimiter(voices: 2, sameCueIntervalTicks: 0);

            int longOne = limiter.Claim("long", 0L, 100).Voice;
            int shortOne = limiter.Claim("short", 0L, 10).Voice;

            VoiceClaim third = limiter.Claim("third", 1L, 50);

            Assert.AreEqual(VoiceVerdict.Stolen, third.Verdict);
            Assert.AreEqual(shortOne, third.Voice, "it cut the sound that had longest to run");
            Assert.AreNotEqual(longOne, third.Voice);
        }

        [Test]
        public void APoolWithNoVoicesRefusesRatherThanThrows()
        {
            var limiter = new VoiceLimiter(voices: 0);

            Assert.AreEqual(VoiceVerdict.NoVoices, limiter.Claim("a", 0L, 10).Verdict);
            Assert.IsFalse(limiter.Claim("a", 0L, 10).Ok);
        }

        [Test]
        public void ClearingForgetsBothLimits()
        {
            var limiter = new VoiceLimiter(voices: 1, sameCueIntervalTicks: 10);
            limiter.Claim("a", 0L, 100);

            limiter.Clear();

            Assert.AreEqual(0, limiter.Busy(0L));
            Assert.AreEqual(VoiceVerdict.Taken, limiter.Claim("a", 0L, 100).Verdict);
        }

        // --- the cues ---------------------------------------------------------------------

        [Test]
        public void UiCuesAreOnTheUiBusAndEverythingElseIsAnEffect()
        {
            Assert.AreEqual(AudioBus.Ui, PrototypeAudioCues.BusOf(AudioCue.UiClick));
            Assert.AreEqual(AudioBus.Ui, PrototypeAudioCues.BusOf(AudioCue.UiConfirm));
            Assert.AreEqual(AudioBus.Ui, PrototypeAudioCues.BusOf(AudioCue.UiRefuse));
            Assert.AreEqual(AudioBus.Effects, PrototypeAudioCues.BusOf(AudioCue.PlayerHurt));
            Assert.AreEqual(AudioBus.Effects, PrototypeAudioCues.BusOf(AudioCue.BossWarning));
        }

        [Test]
        public void EveryCueHasALengthAndAnInterval()
        {
            // A zero length would free its voice instantly and make the pool meaningless.
            foreach (AudioCue cue in System.Enum.GetValues(typeof(AudioCue)))
            {
                Assert.Greater(PrototypeAudioCues.LengthTicksOf(cue), 0, cue.ToString());
                Assert.GreaterOrEqual(PrototypeAudioCues.IntervalTicksOf(cue), 0, cue.ToString());
            }
        }

        [Test]
        public void TheCuesThatRepeatHaveTheShortestIntervals()
        {
            // 명중 happens many times a second; 보스 경고 should not be allowed to stutter.
            Assert.Less(
                PrototypeAudioCues.IntervalTicksOf(AudioCue.Hit),
                PrototypeAudioCues.IntervalTicksOf(AudioCue.BossWarning));
        }
    }
}
