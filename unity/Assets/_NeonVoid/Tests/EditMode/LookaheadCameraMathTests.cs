using NeonVoid.Player;
using NUnit.Framework;
using UnityEngine;

namespace NeonVoid.Tests
{
    /// <summary>
    /// Covers docs/08 R-016 (50 percent cursor look-ahead with a distance cap) and the
    /// docs/02 section 1 rule that aim must not wobble as the camera follows it.
    /// </summary>
    public sealed class LookaheadCameraMathTests
    {
        private static readonly Vector2 WideBoundsMin = new Vector2(-1000f, -1000f);
        private static readonly Vector2 WideBoundsMax = new Vector2(1000f, 1000f);

        [Test]
        public void CameraLeadsExactlyHalfWayTowardTheCursorOffset()
        {
            Vector2 player = new Vector2(10f, -4f);
            Vector2 cursorOffset = new Vector2(6f, 8f); // magnitude 10

            Vector2 focus = LookaheadCameraMath.ComputeFocusXZ(
                player, cursorOffset, lookaheadFraction: 0.5f, maxLookaheadUnits: 100f,
                WideBoundsMin, WideBoundsMax);

            Assert.AreEqual(5f, (focus - player).magnitude, 1e-4f);
            Assert.AreEqual(13f, focus.x, 1e-4f);
            Assert.AreEqual(0f, focus.y, 1e-4f);
        }

        [Test]
        public void LookAheadIsCappedByMaxDistanceButKeepsItsDirection()
        {
            Vector2 player = Vector2.zero;
            Vector2 cursorOffset = new Vector2(300f, 400f); // magnitude 500, half of it is 250

            Vector2 focus = LookaheadCameraMath.ComputeFocusXZ(
                player, cursorOffset, lookaheadFraction: 0.5f, maxLookaheadUnits: 3.2f,
                WideBoundsMin, WideBoundsMax);

            Assert.AreEqual(3.2f, focus.magnitude, 1e-4f, "The cap did not hold.");
            Assert.AreEqual(cursorOffset.normalized.x, focus.normalized.x, 1e-4f, "The cap bent the direction.");
            Assert.AreEqual(cursorOffset.normalized.y, focus.normalized.y, 1e-4f);
        }

        [Test]
        public void FocusIsClampedToTheMapBounds()
        {
            Vector2 focus = LookaheadCameraMath.ComputeFocusXZ(
                new Vector2(15f, 9f), new Vector2(20f, 20f), 0.5f, 100f,
                new Vector2(-16f, -10f), new Vector2(16f, 10f));

            Assert.AreEqual(16f, focus.x, 1e-4f);
            Assert.AreEqual(10f, focus.y, 1e-4f);
        }

        [Test]
        public void InvertedBoundsCollapseToTheMidpointInsteadOfProducingNaN()
        {
            Vector2 focus = LookaheadCameraMath.ComputeFocusXZ(
                Vector2.zero, new Vector2(4f, 4f), 0.5f, 100f,
                boundsMinXZ: new Vector2(10f, 10f),
                boundsMaxXZ: new Vector2(-10f, -10f));

            Assert.IsFalse(float.IsNaN(focus.x) || float.IsNaN(focus.y));
            Assert.AreEqual(0f, focus.x, 1e-4f);
            Assert.AreEqual(0f, focus.y, 1e-4f);
        }

        /// <summary>
        /// The feedback-loop guard. The camera chases its own focus; if the focus depended on
        /// where the camera currently is, iterating would drift or run away. Starting from very
        /// different camera positions must land on one and the same point.
        /// </summary>
        [Test]
        public void FollowingTheFocusConvergesToTheSamePointFromAnyStartingCameraPosition()
        {
            Vector2 player = new Vector2(2f, -3f);
            Vector2 cursorOffset = new Vector2(2.5f, -1.5f);

            Vector2 Settle(Vector2 cameraStart)
            {
                Vector2 camera = cameraStart;
                for (int step = 0; step < 400; ++step)
                {
                    Vector2 focus = LookaheadCameraMath.ComputeFocusXZ(
                        player, cursorOffset, 0.5f, 3.2f, WideBoundsMin, WideBoundsMax);
                    camera = Vector2.Lerp(camera, focus, LookaheadCameraMath.SmoothingAlpha(12f, 1f / 60f));
                }
                return camera;
            }

            Vector2 fromOrigin = Settle(Vector2.zero);
            Vector2 fromFarAway = Settle(new Vector2(500f, -500f));
            Vector2 fromBehind = Settle(new Vector2(-80f, 90f));

            Assert.AreEqual(fromOrigin.x, fromFarAway.x, 1e-3f, "The camera settled somewhere else when it started far away.");
            Assert.AreEqual(fromOrigin.y, fromFarAway.y, 1e-3f);
            Assert.AreEqual(fromOrigin.x, fromBehind.x, 1e-3f);
            Assert.AreEqual(fromOrigin.y, fromBehind.y, 1e-3f);

            Assert.LessOrEqual((fromOrigin - player).magnitude, 3.2f + 1e-3f,
                "The settled camera exceeded the look-ahead cap, which is the run-away signature.");
        }

        [Test]
        public void ViewportOffsetIsIndependentOfWhereTheCameraSits()
        {
            // The conversion takes only the viewport point and the lens, by design: no camera
            // position parameter exists, so no feedback path can be introduced by accident.
            Vector2 topRight = LookaheadCameraMath.ViewportToWorldOffsetXZ(Vector2.one, orthographicSize: 7.5f, aspect: 16f / 9f);

            Assert.AreEqual(7.5f, topRight.y, 1e-4f, "Half the visible height should be the orthographic size.");
            Assert.AreEqual(7.5f * 16f / 9f, topRight.x, 1e-4f);
        }

        [Test]
        public void ScreenCentreProducesNoLookAhead()
        {
            Vector2 centre = LookaheadCameraMath.ViewportToWorldOffsetXZ(new Vector2(0.5f, 0.5f), 7.5f, 21f / 9f);

            Assert.AreEqual(Vector2.zero, centre);
        }

        [Test]
        public void UltrawideLeadsFurtherOnXForTheSameCursorPositionButTheCapStillHolds()
        {
            Vector2 viewport = new Vector2(1f, 0.5f);
            Vector2 wide = LookaheadCameraMath.ViewportToWorldOffsetXZ(viewport, 7.5f, 16f / 9f);
            Vector2 ultrawide = LookaheadCameraMath.ViewportToWorldOffsetXZ(viewport, 7.5f, 21f / 9f);

            Assert.Greater(ultrawide.x, wide.x);

            Vector2 focus = LookaheadCameraMath.ComputeFocusXZ(
                Vector2.zero, ultrawide, 0.5f, 3.2f, WideBoundsMin, WideBoundsMax);

            Assert.LessOrEqual(focus.magnitude, 3.2f + 1e-4f,
                "An ultrawide screen must not let the camera lead further than the cap.");
        }

        [Test]
        public void SmoothingReachesTheSameFractionPerSecondAtAnyFrameRate()
        {
            float Remaining(int framesPerSecond)
            {
                float remaining = 1f;
                for (int frame = 0; frame < framesPerSecond; ++frame)
                    remaining *= 1f - LookaheadCameraMath.SmoothingAlpha(12f, 1f / framesPerSecond);
                return remaining;
            }

            float at30 = Remaining(30);
            float at60 = Remaining(60);
            float at144 = Remaining(144);

            Assert.AreEqual(at60, at30, 1e-4f, "Camera smoothing is frame-rate dependent at 30 fps.");
            Assert.AreEqual(at60, at144, 1e-4f, "Camera smoothing is frame-rate dependent at 144 fps.");
        }

        [Test]
        public void SmoothingIsInertWhenGivenNonsenseInsteadOfJumping()
        {
            Assert.AreEqual(0f, LookaheadCameraMath.SmoothingAlpha(0f, 1f / 60f));
            Assert.AreEqual(0f, LookaheadCameraMath.SmoothingAlpha(12f, 0f));
            Assert.AreEqual(0f, LookaheadCameraMath.SmoothingAlpha(-5f, 1f / 60f));
        }
    }
}
