using System;
using System.Collections.Generic;
using NeonVoid.Core;
using NeonVoid.Player;
using NUnit.Framework;
using UnityEngine;

namespace NeonVoid.Tests
{
    /// <summary>
    /// Covers docs/02 section 9 items 1 and 6: identical per-tick results at any frame rate,
    /// and a dash that follows the movement vector while ignoring the cursor.
    /// </summary>
    public sealed class PlayerMotionSimulationTests
    {
        private static PlayerMotionConfig Config()
        {
            var config = PlayerMotionConfig.Draft();
            config.Validate();
            return config;
        }

        /// <summary>
        /// Runs the simulation for a span of wall time, paced as a renderer at the given frame
        /// rate would pace it, recording the state after every tick. Input is a function of the
        /// TICK index, never of the frame, which is what lets three frame rates be compared
        /// tick for tick.
        /// </summary>
        private static List<PlayerMotionState> RunAtFrameRate(
            int framesPerSecond,
            double wallClockSeconds,
            Func<long, PlayerInputCommand> inputForTick)
        {
            var config = Config();
            var clock = new SimulationClock(maxTicksPerAdvance: 64);
            var state = PlayerMotionState.Spawn(Vector2.zero, Vector2.up);
            var history = new List<PlayerMotionState>();

            double frameDelta = 1.0 / framesPerSecond;
            int frames = (int)Math.Round(wallClockSeconds * framesPerSecond);

            for (int frame = 0; frame < frames; ++frame)
            {
                int ticks = clock.Advance(frameDelta);
                for (int t = 0; t < ticks; ++t)
                {
                    long tickIndex = history.Count;
                    state = PlayerMotionSimulation.Step(state, inputForTick(tickIndex), config, out _);
                    history.Add(state);
                }
            }

            return history;
        }

        [Test]
        public void SameInputPerTickGivesIdenticalStateAt30And60And144Fps()
        {
            // Walk north-east, dash on tick 10, keep walking. The aim sweeps around so any
            // accidental dependency of movement on the cursor shows up as divergence.
            Func<long, PlayerInputCommand> input = tick => new PlayerInputCommand(
                moveXZ: new Vector2(1f, 1f),
                aimWorldXZ: new Vector2(Mathf.Cos(tick * 0.17f) * 6f, Mathf.Sin(tick * 0.17f) * 6f),
                dashPressed: tick == 10);

            var at30 = RunAtFrameRate(30, 2.0, input);
            var at60 = RunAtFrameRate(60, 2.0, input);
            var at144 = RunAtFrameRate(144, 2.0, input);

            int comparable = Mathf.Min(at30.Count, Mathf.Min(at60.Count, at144.Count));
            Assert.Greater(comparable, 100, "Not enough ticks ran to make the comparison meaningful.");

            for (int tick = 0; tick < comparable; ++tick)
            {
                Assert.AreEqual(at60[tick].PositionXZ, at30[tick].PositionXZ, "position diverged at tick " + tick + " (30 fps)");
                Assert.AreEqual(at60[tick].PositionXZ, at144[tick].PositionXZ, "position diverged at tick " + tick + " (144 fps)");
                Assert.AreEqual(at60[tick].FacingXZ, at30[tick].FacingXZ, "facing diverged at tick " + tick + " (30 fps)");
                Assert.AreEqual(at60[tick].DashTicksRemaining, at144[tick].DashTicksRemaining, "dash timer diverged at tick " + tick);
            }
        }

        [Test]
        public void DashFollowsTheMovementVectorAndNotTheCursor()
        {
            var config = Config();
            var state = PlayerMotionState.Spawn(Vector2.zero, Vector2.up);

            // Moving due north (+Z) while the cursor sits due east (+X).
            var command = new PlayerInputCommand(
                moveXZ: new Vector2(0f, 1f),
                aimWorldXZ: new Vector2(50f, 0f),
                dashPressed: true);

            state = PlayerMotionSimulation.Step(state, command, config, out Vector2 displacement);

            Assert.IsTrue(state.IsDashing);
            Assert.AreEqual(0f, state.DashDirectionXZ.x, 1e-5f, "The dash picked up the cursor direction on X.");
            Assert.AreEqual(1f, state.DashDirectionXZ.y, 1e-5f);
            Assert.Greater(displacement.y, 0f);
            Assert.AreEqual(0f, displacement.x, 1e-5f);
        }

        [Test]
        public void MovingTheCursorMidDashDoesNotChangeTheDashDirection()
        {
            var config = Config();
            var state = PlayerMotionState.Spawn(Vector2.zero, Vector2.up);

            state = PlayerMotionSimulation.Step(
                state,
                new PlayerInputCommand(new Vector2(0f, 1f), new Vector2(0f, 10f), dashPressed: true),
                config,
                out _);

            Vector2 lockedDirection = state.DashDirectionXZ;
            Vector2 travelled = Vector2.zero;

            // Whip the cursor around, and release the movement key, while the dash runs.
            for (int tick = 1; tick < config.DashDurationTicks; ++tick)
            {
                state = PlayerMotionSimulation.Step(
                    state,
                    new PlayerInputCommand(Vector2.zero, new Vector2(Mathf.Cos(tick) * 30f, Mathf.Sin(tick) * 30f)),
                    config,
                    out Vector2 step);
                travelled += step;
                Assert.AreEqual(lockedDirection, state.DashDirectionXZ, "dash direction changed at tick " + tick);
            }

            Assert.AreEqual(0f, travelled.x, 1e-4f, "The dash drifted sideways toward the cursor.");
            Assert.Greater(travelled.y, 0f);
        }

        [Test]
        public void DashDoesNotFireWhileStandingStill()
        {
            // docs/08 D-009: the stationary dash direction is undecided, so the prototype
            // deliberately does nothing rather than inventing a rule.
            var config = Config();
            var state = PlayerMotionState.Spawn(Vector2.zero, Vector2.up);

            state = PlayerMotionSimulation.Step(
                state,
                new PlayerInputCommand(Vector2.zero, new Vector2(5f, 5f), dashPressed: true),
                config,
                out Vector2 displacement);

            Assert.IsFalse(state.IsDashing);
            Assert.AreEqual(0, state.DashCooldownTicksRemaining, "A dash that never happened burned the cooldown.");
            Assert.AreEqual(Vector2.zero, displacement);
        }

        [Test]
        public void DashCooldownBlocksASecondDashAndThenReleasesIt()
        {
            var config = Config();
            var state = PlayerMotionState.Spawn(Vector2.zero, Vector2.up);
            var moveNorthDash = new PlayerInputCommand(new Vector2(0f, 1f), new Vector2(0f, 1f), dashPressed: true);

            // Asserted as the observable contract rather than as an internal counter value:
            // a dash started on tick 0 becomes available again on tick DashCooldownTicks,
            // not one tick later. That off-by-one is exactly what this test caught.
            state = PlayerMotionSimulation.Step(state, moveNorthDash, config, out _);
            Assert.IsTrue(state.IsDashing);

            for (int tick = 1; tick < config.DashCooldownTicks; ++tick)
            {
                state = PlayerMotionSimulation.Step(state, moveNorthDash, config, out _);
                if (tick >= config.DashDurationTicks)
                    Assert.IsFalse(state.IsDashing, "a second dash started during cooldown at tick " + tick);
            }

            state = PlayerMotionSimulation.Step(state, moveNorthDash, config, out _);
            Assert.IsTrue(state.IsDashing,
                "The dash was not available again exactly " + config.DashCooldownTicks + " ticks after it started.");
        }

        [Test]
        public void WalkSpeedCoversTheConfiguredDistanceInOneSecond()
        {
            var config = Config();
            var state = PlayerMotionState.Spawn(Vector2.zero, Vector2.up);
            var east = new PlayerInputCommand(new Vector2(1f, 0f), new Vector2(100f, 0f));

            for (int tick = 0; tick < DesignConstants.SimulationTicksPerSecond; ++tick)
                state = PlayerMotionSimulation.Step(state, east, config, out _);

            Assert.AreEqual(config.MoveSpeedUnitsPerSecond, state.PositionXZ.x, 1e-3f);
            Assert.AreEqual(0f, state.PositionXZ.y, 1e-5f);
        }

        [Test]
        public void DiagonalInputIsNotFasterThanCardinalInput()
        {
            var diagonal = new PlayerInputCommand(new Vector2(1f, 1f), new Vector2(10f, 10f));

            Assert.AreEqual(1f, diagonal.MoveXZ.magnitude, 1e-5f,
                "Holding two direction keys granted 1.41x speed.");
        }

        [Test]
        public void NonFiniteInputIsNeutralisedBeforeItCanPoisonThePosition()
        {
            var config = Config();
            var state = PlayerMotionState.Spawn(Vector2.zero, Vector2.up);

            var broken = new PlayerInputCommand(
                moveXZ: new Vector2(float.NaN, 1f),
                aimWorldXZ: new Vector2(float.PositiveInfinity, 0f));

            state = PlayerMotionSimulation.Step(state, broken, config, out Vector2 displacement);

            Assert.IsFalse(float.IsNaN(state.PositionXZ.x) || float.IsNaN(state.PositionXZ.y));
            Assert.IsFalse(float.IsNaN(displacement.x) || float.IsNaN(displacement.y));
        }

        [Test]
        public void FacingHoldsSteadyWhenTheCursorSitsOnTheCharacter()
        {
            var config = Config();
            var state = PlayerMotionState.Spawn(new Vector2(3f, 4f), Vector2.right);

            state = PlayerMotionSimulation.Step(
                state,
                PlayerInputCommand.Idle(new Vector2(3f, 4f)),
                config,
                out _);

            Assert.AreEqual(Vector2.right, state.FacingXZ, "Facing spun to an arbitrary axis instead of holding.");
        }

        [Test]
        public void ConfigRejectsACooldownShorterThanTheDashItGates()
        {
            var config = PlayerMotionConfig.Draft();
            config.DashCooldownTicks = config.DashDurationTicks - 1;

            Assert.Throws<InvalidOperationException>(() => config.Validate());
        }
    }
}
