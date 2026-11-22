using NeonVoid.UI;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// The 설정 screen while it waits for a key, and what a pressed control is stored as.
    /// <para>
    /// docs/05 section 7 asks for 리바인딩·키 중복 확인 as something a person does. The two
    /// rules worth a test are not about input: Escape has to mean cancel, and the path written
    /// down has to be the layout's and not the device instance's.
    /// </para>
    /// </summary>
    public sealed class KeyCaptureTests
    {
        // --- the state ---------------------------------------------------------------------

        [Test]
        public void IdleWaitsForNothingAndSaysNothing()
        {
            Assert.IsFalse(KeyCapture.Idle.IsListening);
            Assert.IsFalse(KeyCapture.Idle.HasMessage);
        }

        [Test]
        public void ListeningNamesWhatItIsWaitingForAndHowToGetOut()
        {
            KeyCapture waiting = KeyCapture.Listening("dash-0", "대시");

            Assert.IsTrue(waiting.IsListening);
            Assert.AreEqual("dash-0", waiting.BindingId);
            StringAssert.Contains("대시", waiting.Message);
            StringAssert.Contains("Esc", waiting.Message, "a prompt with no way out of it");
            Assert.IsFalse(waiting.IsFailure);
        }

        [Test]
        public void AResultStopsWaiting()
        {
            Assert.IsFalse(KeyCapture.Took("대시", "L-Shift").IsListening);
            Assert.IsFalse(KeyCapture.Refused("이미 쓰고 있습니다").IsListening);
            Assert.IsFalse(KeyCapture.Cancelled().IsListening);
        }

        [Test]
        public void ARefusalReadsAsOneAndATakenKeyDoesNot()
        {
            Assert.IsTrue(KeyCapture.Refused("이미 쓰고 있습니다").IsFailure);
            Assert.IsFalse(KeyCapture.Took("대시", "L-Shift").IsFailure);
            Assert.IsFalse(KeyCapture.Cancelled().IsFailure);
        }

        [Test]
        public void ATakenKeySaysWhatItBecame()
        {
            StringAssert.Contains("대시", KeyCapture.Took("대시", "L-Shift").Message);
            StringAssert.Contains("L-Shift", KeyCapture.Took("대시", "L-Shift").Message);
        }

        // --- escape ----------------------------------------------------------------------

        [Test]
        public void EscapeCancelsRatherThanBinds()
        {
            // Without this, a player who opened the prompt by mistake has to bind something to
            // get out of it.
            Assert.IsTrue(KeyCapturePath.IsCancel("<Keyboard>/escape"));
            Assert.IsTrue(KeyCapturePath.IsCancel("<Keyboard>/Escape"));
            Assert.IsFalse(KeyCapturePath.IsCancel("<Keyboard>/e"));
        }

        // --- what gets written down ---------------------------------------------------------

        [Test]
        public void APressedControlIsStoredByItsLayoutAndNotByTheDeviceItCameFrom()
        {
            // /Keyboard1/q names that keyboard. Stored like that, the binding stops working the
            // moment the player plugs in a second one.
            Assert.AreEqual(
                "<Keyboard>/q",
                KeyCapturePath.LayoutPath("Keyboard", "/Keyboard", "/Keyboard/q"));

            Assert.AreEqual(
                "<Keyboard>/q",
                KeyCapturePath.LayoutPath("Keyboard", "/Keyboard1", "/Keyboard1/q"));
        }

        [Test]
        public void ANestedControlKeepsItsWholePath()
        {
            Assert.AreEqual(
                "<Gamepad>/leftStick/up",
                KeyCapturePath.LayoutPath("Gamepad", "/Gamepad", "/Gamepad/leftStick/up"));
        }

        [Test]
        public void SomethingThatDoesNotDescribeAControlComesBackEmpty()
        {
            // Empty rather than a half-built path: a stored "<Keyboard>/" would be accepted by
            // the file and then silently bind nothing.
            Assert.IsEmpty(KeyCapturePath.LayoutPath("Keyboard", "/Keyboard", "/Keyboard"));
            Assert.IsEmpty(KeyCapturePath.LayoutPath("Keyboard", "/Keyboard", "/Mouse/leftButton"));
            Assert.IsEmpty(KeyCapturePath.LayoutPath(null, "/Keyboard", "/Keyboard/q"));
            Assert.IsEmpty(KeyCapturePath.LayoutPath("Keyboard", null, "/Keyboard/q"));
            Assert.IsEmpty(KeyCapturePath.LayoutPath("Keyboard", "/Keyboard", null));
        }

        [Test]
        public void WhatIsCapturedIsSomethingTheBindingTableWillAccept()
        {
            // The two halves have to agree: a path this produces and the table refuses would
            // make every rebind fail with "not a control path".
            string path = KeyCapturePath.LayoutPath("Keyboard", "/Keyboard", "/Keyboard/leftShift");

            Assert.IsTrue(NeonVoid.Player.KeyBindings.IsControlPath(path));
            Assert.AreEqual("L-Shift", NeonVoid.Player.KeyBindings.DisplayKey(path));
        }
    }
}
