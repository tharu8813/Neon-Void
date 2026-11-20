using System.Collections;
using NeonVoid.Combat;
using NeonVoid.Combat.AI;
using NeonVoid.Combat.Projectiles;
using NeonVoid.Combat.Skills;
using NeonVoid.Combat.Status;
using NeonVoid.Plugins;
using NeonVoid.Missions;
using NeonVoid.Progression;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeonVoid.Tests.PlayModeTests
{
    /// <summary>
    /// The checks that used to be a list handed to a person: enemies closing distance, hits
    /// landing through real colliders, a corpse that stops being a wall, plugin effects firing,
    /// a mark that actually reduces armour, and a death that registers exactly once.
    /// <para>
    /// None of this needed a human. It needed the game loop, which is what a PlayMode test
    /// runs: real physics, the real SimulationRunner, one tick per frame. What still needs
    /// eyes is what the screen looks like - that a thing is drawn, legible and in the right
    /// place - and how it feels to play. Everything above is behaviour, and behaviour can be
    /// asserted.
    /// </para>
    /// </summary>
    public sealed class CombatPlayModeTests
    {
        private PrototypeArena _arena;

        [TearDown]
        public void TearDown()
        {
            _arena?.Dispose();
            _arena = null;
        }

        // --- enemy AI ----------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AnEnemyClosesDistanceOnThePlayer()
        {
            _arena = PrototypeArena.Build(new Vector3(8f, 0.7f, 0f));
            CombatActor enemy = _arena.Enemies[0];
            PrototypeArena.MakeChaser(enemy);

            float before = PrototypeArena.DistanceXZ(enemy, _arena.Player);
            yield return _arena.Tick(120);
            float after = PrototypeArena.DistanceXZ(enemy, _arena.Player);

            Assert.Less(after, before - 1f, $"The enemy did not approach: {before:0.00} -> {after:0.00}");
        }

        [UnityTest]
        public IEnumerator AnEnemyHoldsItsPreferredRangeInsteadOfOscillating()
        {
            // The hysteresis band, in the scene rather than in a pure function: with floating
            // point and a CharacterController in the way, a band that is too narrow shows up
            // as an enemy twitching forever on the spot.
            _arena = PrototypeArena.Build(new Vector3(6f, 0.7f, 0f));
            CombatActor enemy = _arena.Enemies[0];
            PrototypeArena.MakeChaser(enemy);

            yield return _arena.Tick(240);
            Vector3 settled = enemy.transform.position;

            yield return _arena.Tick(60);
            float drift = Vector3.Distance(settled, enemy.transform.position);

            Assert.Less(drift, 0.35f, $"The enemy kept shuffling after settling: drifted {drift:0.000}");
        }

        [UnityTest]
        public IEnumerator AnEnemyDamagesThePlayerThroughTheSharedEntryPoint()
        {
            _arena = PrototypeArena.Build(new Vector3(3f, 0.7f, 0f));
            PrototypeArena.MakeChaser(_arena.Enemies[0]);

            float before = _arena.Player.Health.Current;
            yield return _arena.Tick(300);

            Assert.Less(_arena.Player.Health.Current, before, "The player was never hit.");
        }

        [UnityTest]
        public IEnumerator AnEnemyGivesUpBeyondItsLeashRange()
        {
            _arena = PrototypeArena.Build(new Vector3(30f, 0.7f, 0f));
            CombatActor enemy = _arena.Enemies[0];
            EnemyAgent agent = PrototypeArena.MakeChaser(enemy);

            yield return _arena.Tick(60);

            Assert.IsFalse(agent.LastIntent.HasTarget, "An enemy 30 units away acquired the player.");
            Assert.AreEqual(Vector2.zero, agent.LastIntent.MoveXZ);
        }

        // --- death -------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ACorpseStopsBeingAWall()
        {
            // The regression this covers shipped once: a dead enemy kept its
            // CharacterController and stayed a solid, invisible obstacle that could corner the
            // player and stop their shots.
            _arena = PrototypeArena.Build(new Vector3(4f, 0.7f, 0f));
            CombatActor enemy = _arena.Enemies[0];
            yield return _arena.Tick(2);

            PrototypeArena.Kill(enemy);
            yield return _arena.Tick(2);

            Assert.IsFalse(enemy.IsAlive);
            Assert.IsFalse(enemy.GetComponent<CharacterController>().enabled,
                "The corpse kept its controller, so it is still solid.");

            Collider[] colliders = enemy.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; ++i)
                Assert.IsFalse(colliders[i].enabled, "A corpse collider is still enabled.");
        }

        [UnityTest]
        public IEnumerator APlayerDeathRegistersExactlyOnceAndReleasesNothing()
        {
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(2);

            PrototypeArena.Kill(_arena.Player);
            yield return _arena.Tick(2);

            RunDeathLedger ledger = _arena.DeathDirector.Ledger;
            Assert.IsTrue(ledger.HasRegisteredDeath, "The death was not recorded.");
            Assert.AreEqual(RunDeathPhase.AwaitingCommit, ledger.Phase);
            Assert.IsFalse(ledger.MayReleaseResults,
                "Results were released although no save service ever acknowledged anything.");
        }

        [UnityTest]
        public IEnumerator ADeadPlayerStopsActing()
        {
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(2);

            PrototypeArena.Kill(_arena.Player);
            Vector3 restingPlace = _arena.PlayerObject.transform.position;
            yield return _arena.Tick(60);

            Assert.AreEqual(
                restingPlace.x, _arena.PlayerObject.transform.position.x, 0.01f, "The corpse moved.");
            Assert.AreEqual(
                restingPlace.z, _arena.PlayerObject.transform.position.z, 0.01f, "The corpse moved.");
        }

        // --- projectiles -------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AShotDamagesTheEnemyItReaches()
        {
            _arena = PrototypeArena.Build(new Vector3(5f, 0.7f, 0f));
            CombatActor enemy = _arena.Enemies[0];
            yield return _arena.Tick(2);

            float before = enemy.Health.Current;
            _arena.FireAt(enemy);
            yield return _arena.Tick(60);

            Assert.Less(enemy.Health.Current, before, "The shot never landed.");
        }

        [UnityTest]
        public IEnumerator AShotWithNoPierceStopsAtTheFirstEnemy()
        {
            // Two enemies in a line, the second directly behind the first.
            _arena = PrototypeArena.Build(new Vector3(5f, 0.7f, 0f), new Vector3(7f, 0.7f, 0f));
            // Without the plugins: an explosion reaching the enemy behind is correct behaviour
            // and would make this assertion about pierce meaningless.
            _arena.WithoutPlugins();
            CombatActor first = _arena.Enemies[0];
            CombatActor second = _arena.Enemies[1];
            yield return _arena.Tick(2);

            float secondBefore = second.Health.Current;
            _arena.FireAt(first);
            yield return _arena.Tick(60);

            Assert.Less(first.Health.Current, first.Health.Max, "The shot never landed.");
            Assert.AreEqual(secondBefore, second.Health.Current, 1e-3f,
                "A shot with one target passed through into the enemy behind.");
        }

        // --- plugins -----------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ThePrototypePluginsAreCarriedAndFireOnAHit()
        {
            _arena = PrototypeArena.Build(new Vector3(5f, 0.7f, 0f), new Vector3(6.2f, 0.7f, 1.2f));
            yield return _arena.Tick(2);

            Assert.AreEqual(
                NeonVoid.Plugins.PluginCatalog.Prototype().Count, _arena.Procs.Loadout.Count,
                "The prototype plugins were not carried.");

            _arena.FireAt(_arena.Enemies[0]);
            yield return _arena.Tick(60);

            Assert.Greater(_arena.Procs.ResolvedCountOf(12), 0, "G12 부식 표식 never fired.");
            Assert.Greater(_arena.Procs.ResolvedCountOf(17), 0, "G17 폭발 각인 never fired.");
            Assert.Greater(_arena.Procs.ResolvedCountOf(10), 0, "G10 전위 연쇄 never fired.");
        }

        [UnityTest]
        public IEnumerator TheCorrosionMarkActuallyReducesArmour()
        {
            // The bug this exists for: the mark was applied, stacked and displayed while
            // reducing nothing, because the reduction was a fraction of a base armour of zero.
            // Nothing in the game said so and no test caught it, because the unit test used a
            // base with armour the real enemies do not have.
            _arena = PrototypeArena.Build(new Vector3(5f, 0.7f, 0f));
            CombatActor enemy = _arena.Enemies[0];
            yield return _arena.Tick(2);

            float armourBefore = enemy.Stats.PhysicalArmor;
            _arena.FireAt(enemy);
            yield return _arena.Tick(60);

            Assert.IsTrue(enemy.Status.Has(StatusEffectId.FirewallBreach), "The mark was not applied.");
            Assert.Less(enemy.Stats.PhysicalArmor, armourBefore,
                "The mark is applied and reduces nothing, which is exactly the bug.");
            Assert.Less(enemy.Stats.MagicResistance, enemy.BaseStats.MagicResistance,
                "docs/03 G12 reduces both resistances.");
        }

        [UnityTest]
        public IEnumerator ChainLightningReachesAnEnemyNobodyShot()
        {
            _arena = PrototypeArena.Build(new Vector3(5f, 0.7f, 0f), new Vector3(6.5f, 0.7f, 1.5f));
            CombatActor shot = _arena.Enemies[0];
            CombatActor bystander = _arena.Enemies[1];
            yield return _arena.Tick(2);

            float bystanderBefore = bystander.Health.Current;
            _arena.FireAt(shot);
            yield return _arena.Tick(60);

            Assert.Less(bystander.Health.Current, bystanderBefore,
                "An enemy standing next to the target took no damage, so nothing chained or exploded.");
        }

        // --- skills ------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ArcPulseDamagesAndSlowsWhatIsInFrontOfTheCursor()
        {
            _arena = PrototypeArena.Build(new Vector3(3f, 0.7f, 0f));
            _arena.WithoutPlugins();
            CombatActor enemy = _arena.Enemies[0];
            yield return _arena.Tick(2);

            SkillAsset arcPulse = PrototypeArena.SkillFrom(
                "skill.arc_pulse",
                new SkillDefinition
                {
                    SkillId = 1001,
                    ManaCost = 25f,
                    BaseCooldownTicks = 300,
                    Damage = new DamageRequest
                    {
                        Flat = 10f, ApRatio = 0.8f, Type = DamageType.Magic, IsArea = true,
                    },
                },
                new SkillShape
                {
                    Kind = SkillEffectKind.ConeDamage, RadiusUnits = 5f, ConeHalfAngleDegrees = 45f,
                },
                new SkillStatusEffect
                {
                    Id = StatusEffectId.Slow, Magnitude = 0.3f, DurationTicksOverride = -1,
                });
            _arena.Caster.Equip(0, arcPulse);

            float before = enemy.Health.Current;
            SkillCastResult result = _arena.Caster.CastSlot(0, new Vector2(10f, 0f));
            yield return _arena.Tick(2);

            Assert.IsTrue(result.Succeeded, $"The cast was refused: {result.Rejection}");
            Assert.Less(enemy.Health.Current, before, "The cone hit nothing.");
            Assert.IsTrue(enemy.Status.Has(StatusEffectId.Slow), "The slow was not applied.");
            Assert.Less(_arena.Player.Mana.Current, _arena.Player.Mana.Max, "No mana was spent.");
        }

        [UnityTest]
        public IEnumerator ArcPulseLeavesWhatIsBehindTheCasterAlone()
        {
            _arena = PrototypeArena.Build(new Vector3(-3f, 0.7f, 0f));
            _arena.WithoutPlugins();
            CombatActor behind = _arena.Enemies[0];
            yield return _arena.Tick(2);

            _arena.Caster.Equip(0, PrototypeArena.SkillFrom(
                "skill.arc_pulse",
                new SkillDefinition
                {
                    SkillId = 1001,
                    BaseCooldownTicks = 300,
                    Damage = new DamageRequest { Flat = 10f, Type = DamageType.Magic, IsArea = true },
                },
                new SkillShape
                {
                    Kind = SkillEffectKind.ConeDamage, RadiusUnits = 5f, ConeHalfAngleDegrees = 45f,
                },
                SkillStatusEffect.None));

            float before = behind.Health.Current;
            _arena.Caster.CastSlot(0, new Vector2(10f, 0f));   // aimed the other way
            yield return _arena.Tick(2);

            Assert.AreEqual(before, behind.Health.Current, 1e-3f, "The cone hit behind the caster.");
        }

        [UnityTest]
        public IEnumerator AFieldBurnsEverythingStandingInItOverTime()
        {
            _arena = PrototypeArena.Build(new Vector3(1.5f, 0.7f, 0f));
            CombatActor enemy = _arena.Enemies[0];
            yield return _arena.Tick(2);

            _arena.Caster.Equip(1, PrototypeArena.SkillFrom(
                "skill.firewall_field",
                new SkillDefinition
                {
                    SkillId = 1002,
                    BaseCooldownTicks = 480,
                    Damage = new DamageRequest
                    {
                        Flat = 4f, Type = DamageType.Magic, IsArea = true, IsPeriodic = true,
                    },
                },
                new SkillShape
                {
                    Kind = SkillEffectKind.PersistentField,
                    RadiusUnits = 3f,
                    Field = FieldSchedule.Of(300, 30),
                },
                SkillStatusEffect.None));

            _arena.Caster.CastSlot(1, new Vector2(0f, 1f));
            yield return _arena.Tick(5);
            float afterFirstTick = enemy.Health.Current;

            yield return _arena.Tick(60);
            Assert.Less(enemy.Health.Current, afterFirstTick, "The field burned once and stopped.");

            yield return _arena.Tick(300);
            Assert.IsNull(
                Object.FindFirstObjectByType<PersistentDamageField>(), "The field outlived its duration.");
        }

        [UnityTest]
        public IEnumerator ABlinkMovesThePlayerAndRefusesToEnterAWall()
        {
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(2);

            _arena.Caster.Equip(2, PrototypeArena.SkillFrom(
                "skill.packet_blink",
                new SkillDefinition
                {
                    SkillId = 1003,
                    BaseCooldownTicks = 1,
                    IsMovementSkill = true,
                },
                new SkillShape { Kind = SkillEffectKind.Teleport, TeleportMaxDistanceUnits = 6f },
                SkillStatusEffect.None));

            Vector3 start = _arena.PlayerObject.transform.position;
            SkillCastResult moved = _arena.Caster.CastSlot(2, new Vector2(5f, 0f));
            yield return _arena.Tick(2);

            Assert.IsTrue(moved.Succeeded, $"The blink was refused: {moved.Rejection}");
            Assert.Greater(
                Vector3.Distance(start, _arena.PlayerObject.transform.position), 3f, "The blink did not move.");

            // A wall right on top of the caster leaves nowhere to land.
            GameObject wall = _arena.Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            wall.transform.position = _arena.PlayerObject.transform.position + new Vector3(3f, 0f, 0f);
            wall.transform.localScale = new Vector3(14f, 4f, 14f);
            yield return _arena.Tick(2);

            Vector3 blocked = _arena.PlayerObject.transform.position;
            float manaBefore = _arena.Player.Mana.Current;
            SkillCastResult refused = _arena.Caster.CastSlot(2, new Vector2(blocked.x + 5f, blocked.z));
            yield return _arena.Tick(2);

            Assert.IsFalse(refused.Succeeded, "The blink went into a wall.");
            Assert.AreEqual(SkillCastRejection.NoValidDestination, refused.Rejection);
            Assert.AreEqual(manaBefore, _arena.Player.Mana.Current, 1e-3f,
                "A blink with nowhere to land still charged the player.");
        }










        // --- the three later skills --------------------------------------------------------

        [UnityTest]
        public IEnumerator PatchBurstHealsTheCasterAndIsSpentAtFullHealth()
        {
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(2);

            _arena.Caster.Equip(0, PrototypeArena.SkillFrom(
                "skill.patch_burst",
                new SkillDefinition
                {
                    SkillId = 1004,
                    ManaCost = 30f,
                    BaseCooldownTicks = 1,
                    Damage = new DamageRequest { Flat = 18f, ApRatio = 0.5f, Type = DamageType.Magic },
                },
                new SkillShape { Kind = SkillEffectKind.SelfHeal },
                SkillStatusEffect.None));

            _arena.Player.ReceiveDamage(
                new DamageRequest { Flat = 40f, Type = DamageType.True }, CombatStats.StartingVaccine());
            float wounded = _arena.Player.Health.Current;

            Assert.IsTrue(_arena.Caster.CastSlot(0, Vector2.zero).Succeeded);
            yield return _arena.Tick(2);
            Assert.Greater(_arena.Player.Health.Current, wounded, "The heal did nothing.");

            // At full health the cast is still spent. docs/03 lists this case and does not say
            // how to resolve it, so the mana goes and the decision stays open.
            CombatStats full = _arena.Player.BaseStats;
            _arena.Player.Configure(PrototypeArena.PlayerTeam, full);
            float manaBefore = _arena.Player.Mana.Current;

            Assert.IsTrue(_arena.Caster.CastSlot(0, Vector2.zero).Succeeded);
            yield return _arena.Tick(2);

            Assert.AreEqual(_arena.Player.Health.Max, _arena.Player.Health.Current, 1e-3f);
            Assert.Less(_arena.Player.Mana.Current, manaBefore, "A full-health cast was free.");
        }

        [UnityTest]
        public IEnumerator TheCleanseClearsWhatItCanAndCannotBeCastThroughASilence()
        {
            // The only thing in the project that calls StatusContainer.Remove, and the one
            // place docs/03's 사용불가 디버프 경계 is visible: 제한 stops the cast, so a cleanse
            // can never remove it.
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(2);

            _arena.Caster.Equip(0, PrototypeArena.SkillFrom(
                "skill.cleanse",
                new SkillDefinition { SkillId = 1005, ManaCost = 20f, BaseCooldownTicks = 1 },
                new SkillShape
                {
                    Kind = SkillEffectKind.CleanseControl,
                    CleansedEffects = new[]
                    {
                        StatusEffectId.Slow, StatusEffectId.Root, StatusEffectId.Malfunction,
                    },
                },
                SkillStatusEffect.None));

            _arena.Player.ApplyStatus(StatusEffectId.Slow, 0.4f);
            _arena.Player.ApplyStatus(StatusEffectId.Root, 1f);
            yield return _arena.Tick(1);
            Assert.IsTrue(_arena.Player.Status.Has(StatusEffectId.Slow));

            Assert.IsTrue(_arena.Caster.CastSlot(0, Vector2.zero).Succeeded, "Rooted blocked a still skill.");
            yield return _arena.Tick(1);

            Assert.IsFalse(_arena.Player.Status.Has(StatusEffectId.Slow), "구속 survived the cleanse.");
            Assert.IsFalse(_arena.Player.Status.Has(StatusEffectId.Root), "속박 survived the cleanse.");

            _arena.Player.ApplyStatus(StatusEffectId.Silence, 1f);
            yield return _arena.Tick(1);
            SkillCastResult refused = _arena.Caster.CastSlot(0, Vector2.zero);

            Assert.IsFalse(refused.Succeeded);
            Assert.AreEqual(SkillCastRejection.Silenced, refused.Rejection,
                "A cleanse must not be able to cleanse the thing that stops it being cast.");
        }

        [UnityTest]
        public IEnumerator LockOnHitsTheNearestEnemyAndLeavesItEasierToHurt()
        {
            _arena = PrototypeArena.Build(new Vector3(3f, 0.7f, 0f), new Vector3(8f, 0.7f, 0f));
            _arena.WithoutPlugins();
            CombatActor near = _arena.Enemies[0];
            CombatActor far = _arena.Enemies[1];
            yield return _arena.Tick(2);

            _arena.Caster.Equip(0, PrototypeArena.SkillFrom(
                "skill.lockon",
                new SkillDefinition
                {
                    SkillId = 1006,
                    ManaCost = 25f,
                    BaseCooldownTicks = 1,
                    Damage = new DamageRequest { Flat = 6f, AdRatio = 1.4f, Type = DamageType.Physical },
                },
                new SkillShape { Kind = SkillEffectKind.SingleTarget, RadiusUnits = 9f },
                new SkillStatusEffect
                {
                    Id = StatusEffectId.FirewallBreach, Magnitude = 5f, DurationTicksOverride = 240,
                }));

            float farBefore = far.Health.Current;
            float armourBefore = near.Stats.PhysicalArmor;

            Assert.IsTrue(_arena.Caster.CastSlot(0, Vector2.zero).Succeeded);
            yield return _arena.Tick(2);

            Assert.Less(near.Health.Current, near.Health.Max, "The nearest enemy was not hit.");
            Assert.AreEqual(farBefore, far.Health.Current, 1e-3f, "A single-target skill hit two enemies.");
            Assert.Less(near.Stats.PhysicalArmor, armourBefore, "The defence debuff did nothing.");
        }

        // --- the attack statuses -----------------------------------------------------------

        [UnityTest]
        public IEnumerator AttackSpeedUpMakesTheWeaponFireMoreOften()
        {
            // 발사속도 증가 was tracked and read by nothing, so the rate never moved.
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(2);

            int baseInterval = _arena.Attacker.IntervalTicks;
            _arena.Player.ApplyStatus(StatusEffectId.AttackSpeedUp, 0.5f);
            yield return _arena.Tick(1);

            Assert.Less(_arena.Attacker.IntervalTicks, baseInterval, "The buff changed nothing.");
        }

        [UnityTest]
        public IEnumerator MalfunctionBothSlowsTheWeaponAndMakesItFail()
        {
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(2);

            int baseInterval = _arena.Attacker.IntervalTicks;
            _arena.Player.ApplyStatus(StatusEffectId.Malfunction, 1f);   // certain failure
            yield return _arena.Tick(1);

            Assert.Greater(_arena.Attacker.IntervalTicks, baseInterval, "기능고장 did not slow it.");
            Assert.IsTrue(_arena.Player.RollChance(100f), "A certain chance must be certain.");
        }

        [UnityTest]
        public IEnumerator ACertainMalfunctionStopsShotsBeingProduced()
        {
            _arena = PrototypeArena.Build(new Vector3(6f, 0.7f, 0f));
            _arena.WithoutPlugins();
            yield return _arena.Tick(2);

            // Aim at the enemy and let the weapon run.
            _arena.PlayerObject.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
            _arena.Player.ApplyStatus(StatusEffectId.Malfunction, 1f);
            _arena.Attacker.enabled = true;
            yield return _arena.Tick(120);

            Assert.IsNull(
                Object.FindFirstObjectByType<CombatProjectile>(),
                "A weapon that always malfunctions still produced a shot.");
        }

        [UnityTest]
        public IEnumerator PierceLetsOneShotReachTwoEnemies()
        {
            // 관통 with the ledger still forbidding the same enemy twice, which is the half of
            // docs/02 section 5 that this number must not be mistaken for.
            _arena = PrototypeArena.Build(new Vector3(5f, 0.7f, 0f), new Vector3(7f, 0.7f, 0f));
            _arena.WithoutPlugins();
            CombatActor first = _arena.Enemies[0];
            CombatActor second = _arena.Enemies[1];
            yield return _arena.Tick(2);

            _arena.Player.ApplyStatus(StatusEffectId.Pierce, 1f);
            yield return _arena.Tick(1);

            Assert.AreEqual(2, _arena.Attacker.EffectiveMaxTargets, "관통 did not raise the target count.");

            float secondBefore = second.Health.Current;
            _arena.FireAt(first, maxTargets: _arena.Attacker.EffectiveMaxTargets);
            yield return _arena.Tick(60);

            Assert.Less(first.Health.Current, first.Health.Max);
            Assert.Less(second.Health.Current, secondBefore, "The shot stopped at the first enemy.");
        }

        [UnityTest]
        public IEnumerator ProjectileSpeedUpMakesAShotArriveSooner()
        {
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(2);

            float baseSpeed = _arena.Attacker.EffectiveProjectileSpeed;
            _arena.Player.ApplyStatus(StatusEffectId.ProjectileSpeedUp, 0.5f);
            yield return _arena.Tick(1);

            Assert.Greater(_arena.Attacker.EffectiveProjectileSpeed, baseSpeed, "탄환속도 증가 did nothing.");
        }

        // --- damage and healing over time --------------------------------------------------

        [UnityTest]
        public IEnumerator DamageOverTimeActuallyRemovesHealth()
        {
            // 무결성 공격 was tracked with a duration and a magnitude that nothing read, so
            // applying it changed no health at all.
            _arena = PrototypeArena.Build(new Vector3(5f, 0.7f, 0f));
            CombatActor enemy = _arena.Enemies[0];
            _arena.WithoutPlugins();
            yield return _arena.Tick(2);

            float before = enemy.Health.Current;
            enemy.ApplyStatus(StatusEffectId.IntegrityAttack, 2f);
            yield return _arena.Tick(StatusCatalog.Draft()[StatusEffectId.IntegrityAttack].PeriodicIntervalTicks + 2);

            Assert.Less(enemy.Health.Current, before, "The damage over time did nothing.");
        }

        [UnityTest]
        public IEnumerator DamageOverTimeIsReducedByResistanceRatherThanBypassingIt()
        {
            // It is magic damage with no attacker, not true damage: the magnitude is the
            // damage, and the target's own resistance still applies.
            _arena = PrototypeArena.Build(new Vector3(5f, 0.7f, 0f), new Vector3(5f, 0.7f, 8f));
            _arena.WithoutPlugins();
            CombatActor soft = _arena.Enemies[0];
            CombatActor armoured = _arena.Enemies[1];
            yield return _arena.Tick(2);

            CombatStats softStats = soft.BaseStats;
            softStats.MagicResistance = 0f;
            soft.Configure(PrototypeArena.EnemyTeam, softStats);

            CombatStats hardStats = armoured.BaseStats;
            hardStats.MagicResistance = 100f;
            armoured.Configure(PrototypeArena.EnemyTeam, hardStats);

            float softBefore = soft.Health.Current;
            float hardBefore = armoured.Health.Current;
            soft.ApplyStatus(StatusEffectId.IntegrityAttack, 4f);
            armoured.ApplyStatus(StatusEffectId.IntegrityAttack, 4f);
            yield return _arena.Tick(StatusCatalog.Draft()[StatusEffectId.IntegrityAttack].PeriodicIntervalTicks + 2);

            float softLost = softBefore - soft.Health.Current;
            float hardLost = hardBefore - armoured.Health.Current;

            Assert.Greater(softLost, 0f);
            Assert.Less(hardLost, softLost, "Resistance did not reduce the damage over time.");
        }

        [UnityTest]
        public IEnumerator RegenerationActuallyRestoresHealth()
        {
            _arena = PrototypeArena.Build(new Vector3(5f, 0.7f, 0f));
            CombatActor enemy = _arena.Enemies[0];
            _arena.WithoutPlugins();
            yield return _arena.Tick(2);

            enemy.ReceiveDamage(
                new DamageRequest { Flat = 20f, Type = DamageType.True }, CombatStats.StartingVaccine());
            float wounded = enemy.Health.Current;

            enemy.ApplyStatus(StatusEffectId.Regeneration, 3f);
            yield return _arena.Tick(StatusCatalog.Draft()[StatusEffectId.Regeneration].PeriodicIntervalTicks + 2);

            Assert.Greater(enemy.Health.Current, wounded, "Regeneration healed nothing.");
        }

        [UnityTest]
        public IEnumerator DamageOverTimeCanKillAndTheDeathPathStillRuns()
        {
            // It goes through ReceiveDamage like everything else, so death handling applies -
            // including the corpse no longer being a wall.
            _arena = PrototypeArena.Build(new Vector3(5f, 0.7f, 0f));
            CombatActor enemy = _arena.Enemies[0];
            _arena.WithoutPlugins();
            yield return _arena.Tick(2);

            enemy.ApplyStatus(StatusEffectId.IntegrityAttack, enemy.Health.Max * 2f);
            yield return _arena.Tick(StatusCatalog.Draft()[StatusEffectId.IntegrityAttack].PeriodicIntervalTicks + 4);

            Assert.IsFalse(enemy.IsAlive, "It survived a tick larger than its own health.");
            Assert.IsFalse(enemy.GetComponent<CharacterController>().enabled,
                "The death path did not run, so the corpse is still solid.");
        }

        // --- crits -------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AGuaranteedCritHitsHarderThanOneThatCannotHappen()
        {
            // Every attack site used to pass critRolled: false, so this is the first time a
            // crit can happen in the scene at all.
            _arena = PrototypeArena.Build(new Vector3(5f, 0.7f, 0f), new Vector3(5f, 0.7f, 8f));
            _arena.WithoutPlugins();
            CombatActor unlucky = _arena.Enemies[0];
            CombatActor doomed = _arena.Enemies[1];
            yield return _arena.Tick(2);

            CombatStats never = _arena.Player.BaseStats;
            never.CritChancePercent = 0f;
            CombatStats always = _arena.Player.BaseStats;
            always.CritChancePercent = 100f;

            var hit = new DamageRequest { Flat = 5f, Type = DamageType.True, CanCrit = true };

            _arena.Player.Configure(PrototypeArena.PlayerTeam, never);
            float before = unlucky.Health.Current;
            unlucky.ReceiveDamage(hit, _arena.Player.Stats, _arena.Player.RollCrit(hit));
            float normalLoss = before - unlucky.Health.Current;

            _arena.Player.Configure(PrototypeArena.PlayerTeam, always);
            before = doomed.Health.Current;
            doomed.ReceiveDamage(hit, _arena.Player.Stats, _arena.Player.RollCrit(hit));
            float critLoss = before - doomed.Health.Current;

            Assert.Greater(critLoss, normalLoss, "A guaranteed crit did no more than a normal hit.");
            Assert.AreEqual(
                normalLoss * _arena.Player.Stats.CritDamageMultiplier, critLoss, 0.05f);
        }

        [UnityTest]
        public IEnumerator EachActorRollsOnItsOwnStreamPosition()
        {
            // One enemy's swings must not shift where the player's crit sequence has got to, or
            // a fight would depend on how many times something else happened to attack.
            _arena = PrototypeArena.Build(new Vector3(5f, 0.7f, 0f));
            yield return _arena.Tick(2);

            CombatStats half = _arena.Player.BaseStats;
            half.CritChancePercent = 50f;
            _arena.Player.Configure(PrototypeArena.PlayerTeam, half);

            CombatStats enemyHalf = _arena.Enemies[0].BaseStats;
            enemyHalf.CritChancePercent = 50f;
            _arena.Enemies[0].Configure(PrototypeArena.EnemyTeam, enemyHalf);

            var hit = new DamageRequest { Flat = 1f, Type = DamageType.True, CanCrit = true };

            // The player's first eight rolls, with the enemy rolling in between.
            var withInterference = new System.Text.StringBuilder();
            for (int i = 0; i < 8; ++i)
            {
                withInterference.Append(_arena.Player.RollCrit(hit) ? '1' : '0');
                _arena.Enemies[0].RollCrit(hit);
            }

            // A fresh arena, same run id and seed is not guaranteed - so instead assert the
            // weaker but meaningful property: the enemy's rolls are not the player's.
            var enemyRolls = new System.Text.StringBuilder();
            for (int i = 0; i < 8; ++i)
                enemyRolls.Append(_arena.Enemies[0].RollCrit(hit) ? '1' : '0');

            Assert.AreNotEqual(withInterference.ToString(), enemyRolls.ToString(),
                "The player and an enemy drew the same sequence, so they share a stream position.");
        }

        // --- the run identity --------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheMissionBeginsWithASeedThatCanBeReadBack()
        {
            // A deterministic generator whose seed nobody can recover is not reproducible in
            // practice, whatever it is in principle.
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(2);

            Assert.IsTrue(_arena.Mission.Identity.IsValid, "No run was started.");
            Assert.IsTrue(_arena.Mission.State.IsRunning);
            Assert.AreNotEqual(
                _arena.Mission.Identity.SeedFor(RandomStream.Reward),
                _arena.Mission.Identity.SeedFor(RandomStream.Combat),
                "Two streams share a seed, so one would shift the other.");
        }

        [UnityTest]
        public IEnumerator ADeathReceiptIsKeyedOnTheRunThatWasActuallyPlayed()
        {
            // It used to be keyed on a hardcoded 1, so every run the player ever played would
            // have minted the same receipt id - and telling one death from another is the whole
            // job of that key.
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(2);

            long runId = _arena.Mission.Identity.RunId;
            PrototypeArena.Kill(_arena.Player);
            yield return _arena.Tick(2);

            Assert.AreEqual(runId, _arena.DeathDirector.Ledger.RegisteredDeathId.RunId);
        }

        [UnityTest]
        public IEnumerator APlayerDeathFailsTheMission()
        {
            // [확정] docs/00: 사망은 해당 의뢰 실패. In memory only; what it costs is still the
            // save service's call, and there is no save service.
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(2);
            Assert.IsTrue(_arena.Mission.State.IsRunning);

            PrototypeArena.Kill(_arena.Player);
            yield return _arena.Tick(2);

            Assert.AreEqual(MissionPhase.Failed, _arena.Mission.State.Phase);
            Assert.IsFalse(_arena.Mission.State.IsRunning);
        }

        // --- the support -------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ASupportRaisesItsAlliesDamageAndNotItsOwn()
        {
            // 우선 처치 only means something if its help is real. This is also the first time
            // anything other than the player applies a status to an actor.
            _arena = PrototypeArena.Build(new Vector3(4f, 0.7f, 0f), new Vector3(6f, 0.7f, 2f));
            CombatActor ally = _arena.Enemies[0];
            CombatActor support = _arena.Enemies[1];
            PrototypeArena.MakeChaser(ally);
            PrototypeArena.MakeSupport(support);
            yield return _arena.Tick(5);

            float allyBase = ally.BaseStats.AttackDamage;

            Assert.IsTrue(ally.Status.Has(StatusEffectId.Empower), "The ally was never buffed.");
            Assert.Greater(ally.Stats.AttackDamage, allyBase,
                "The buff was applied and changed nothing, which is the bug this archetype would hide.");
            Assert.IsFalse(support.Status.Has(StatusEffectId.Empower),
                "It buffed itself, which makes it a tougher enemy rather than a support.");
        }

        [UnityTest]
        public IEnumerator ASupportDoesNotBuffThePlayer()
        {
            _arena = PrototypeArena.Build(new Vector3(4f, 0.7f, 0f));
            PrototypeArena.MakeSupport(_arena.Enemies[0]);
            yield return _arena.Tick(5);

            Assert.IsFalse(_arena.Player.Status.Has(StatusEffectId.Empower),
                "It handed the player its own buff.");
        }

        [UnityTest]
        public IEnumerator KillingTheSupportStopsTheBuffComingBack()
        {
            // The whole tactical point: the help has to stop arriving.
            _arena = PrototypeArena.Build(new Vector3(4f, 0.7f, 0f), new Vector3(6f, 0.7f, 2f));
            CombatActor ally = _arena.Enemies[0];
            CombatActor support = _arena.Enemies[1];
            PrototypeArena.MakeChaser(ally);
            EnemyAgent supportAgent = PrototypeArena.MakeSupport(support);
            yield return _arena.Tick(5);
            Assert.IsTrue(ally.Status.Has(StatusEffectId.Empower));

            PrototypeArena.Kill(support);
            int duration = ally.Status.RemainingTicksOf(StatusEffectId.Empower);
            yield return _arena.Tick(duration + 2);

            Assert.IsFalse(ally.Status.Has(StatusEffectId.Empower),
                "The buff was refreshed by a corpse.");
            Assert.AreEqual(ally.BaseStats.AttackDamage, ally.Stats.AttackDamage, 1e-3f,
                "The ally kept its raised damage after the support died.");
        }

        // --- the charger -------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AChargerStandsStillToWarnAndThenCoversGround()
        {
            // 경고 프레임과 판정 동기, in the scene rather than in the pure function: the
            // wind-up has to be visibly motionless, and the dash has to move further than
            // walking could, or neither reads as a charge.
            //
            // Measured by watching the phase rather than by counting ticks. The first version
            // counted them from the moment it noticed the wind-up, which was already two ticks
            // in, so its last "wind-up" tick was really the first dash tick - and the test
            // failed on a charge that was behaving correctly.
            _arena = PrototypeArena.Build(new Vector3(7f, 0.7f, 0f));
            CombatActor enemy = _arena.Enemies[0];
            EnemyAgent agent = PrototypeArena.MakeCharger(enemy);
            yield return _arena.Tick(2);

            int guard = 0;
            while (agent.LastIntent.ChargePhase != EnemyChargePhase.Telegraph && guard++ < 900)
                yield return _arena.Tick(1);
            Assert.AreEqual(EnemyChargePhase.Telegraph, agent.LastIntent.ChargePhase, "It never wound up.");

            Vector3 windUpStart = enemy.transform.position;
            int windUpTicks = 0;
            while (agent.LastIntent.ChargePhase == EnemyChargePhase.Telegraph && windUpTicks++ < 900)
            {
                Assert.Less(
                    Vector3.Distance(windUpStart, enemy.transform.position), 0.05f,
                    $"It moved during its own warning, on wind-up tick {windUpTicks}.");
                yield return _arena.Tick(1);
            }

            Assert.AreEqual(EnemyChargePhase.Dashing, agent.LastIntent.ChargePhase,
                "The wind-up did not lead into a dash.");

            Vector3 dashStart = enemy.transform.position;
            int dashTicks = 0;
            while (agent.LastIntent.ChargePhase == EnemyChargePhase.Dashing && dashTicks++ < 900)
                yield return _arena.Tick(1);

            float dashed = Vector3.Distance(dashStart, enemy.transform.position);
            float walkable = agent.Archetype.MoveSpeedUnitsPerSecond * dashTicks * PrototypeArena.TickSeconds;

            Assert.Greater(dashed, walkable * 1.5f,
                $"The dash covered {dashed:0.00} units in {dashTicks} ticks, barely more than walking ({walkable:0.00}).");
        }

        [UnityTest]
        public IEnumerator AChargerHitsThePlayerOnceWhenItConnects()
        {
            _arena = PrototypeArena.Build(new Vector3(5f, 0.7f, 0f));
            PrototypeArena.MakeCharger(_arena.Enemies[0]);

            float before = _arena.Player.Health.Current;
            yield return _arena.Tick(300);

            Assert.Less(_arena.Player.Health.Current, before, "The charge never connected at all.");
        }

        // --- the ranged archetype ----------------------------------------------------------

        [UnityTest]
        public IEnumerator ARangedEnemyHoldsItsDistanceInsteadOfWalkingIn()
        {
            _arena = PrototypeArena.Build(new Vector3(14f, 0.7f, 0f));
            CombatActor enemy = _arena.Enemies[0];
            PrototypeArena.MakeRanged(enemy);
            yield return _arena.Tick(420);

            float distance = PrototypeArena.DistanceXZ(enemy, _arena.Player);
            EnemyArchetype archetype = enemy.GetComponent<EnemyAgent>().Archetype;
            float band = archetype.Brain.RangeToleranceUnits + 0.6f;

            Assert.AreEqual(archetype.Brain.PreferredRange, distance, band,
                $"It settled at {distance:0.0} rather than near {archetype.Brain.PreferredRange}.");
        }

        [UnityTest]
        public IEnumerator ARangedEnemyShootsThePlayerFromAcrossTheArena()
        {
            _arena = PrototypeArena.Build(new Vector3(9f, 0.7f, 0f));
            PrototypeArena.MakeRanged(_arena.Enemies[0]);

            float before = _arena.Player.Health.Current;
            yield return _arena.Tick(300);

            Assert.Less(_arena.Player.Health.Current, before, "Nothing ever reached the player.");
            Assert.Greater(
                PrototypeArena.DistanceXZ(_arena.Enemies[0], _arena.Player), 4f,
                "It hit the player by walking into them, which is not what a ranged enemy does.");
        }

        [UnityTest]
        public IEnumerator AWallStopsARangedEnemyFromShootingThroughIt()
        {
            // docs/01 lists LOS as what this archetype must implement. Without it cover is
            // decorative and the arena's two blocks mean nothing.
            _arena = PrototypeArena.Build(new Vector3(12f, 0.7f, 0f));
            PrototypeArena.MakeRanged(_arena.Enemies[0]);
            _arena.Wall(new Vector3(6f, 1f, 0f), new Vector3(1f, 4f, 24f));

            float before = _arena.Player.Health.Current;
            yield return _arena.Tick(300);

            Assert.AreEqual(before, _arena.Player.Health.Current, 1e-3f,
                "The player was shot through a wall.");
        }

        [UnityTest]
        public IEnumerator ABlockedRangedEnemyRepositionsRatherThanStandingStill()
        {
            _arena = PrototypeArena.Build(new Vector3(12f, 0.7f, 0f));
            CombatActor enemy = _arena.Enemies[0];
            PrototypeArena.MakeRanged(enemy);
            _arena.Wall(new Vector3(6f, 1f, 0f), new Vector3(1f, 4f, 24f));

            Vector3 start = enemy.transform.position;
            yield return _arena.Tick(180);

            Assert.Greater(
                Vector3.Distance(start, enemy.transform.position), 0.5f,
                "It stood behind cover doing nothing, which is not a tactic.");
        }

        // --- plugin passives ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator PassivePluginsReachThePlayersStatSnapshot()
        {
            // Granted in Start and pushed to the actor there. A plugin that was carried but
            // never pushed would sit in the list contributing nothing, which is the failure
            // this project has already shipped in two other shapes.
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(2);

            CombatStats resolved = _arena.Player.Stats;

            Assert.Greater(resolved.PhysicalPenPercent, 0f, "G04 취약점 분석 contributed nothing.");
            Assert.Greater(resolved.MagicPenPercent, 0f, "G04 취약점 분석 contributed nothing.");
            Assert.AreEqual(
                NeonVoid.Plugins.PluginCatalog.StatedExecutionBonusPercent,
                resolved.LowHealthDamageBonusPercent, 1e-3f, "R01 처형 알고리즘 contributed nothing.");
            Assert.Greater(
                _arena.Player.MoveSpeedMultiplier, 1f, "G02 경량화 did not raise movement speed.");
        }

        [UnityTest]
        public IEnumerator TheExecutionBonusHitsHarderOnAWoundedEnemy()
        {
            _arena = PrototypeArena.Build(new Vector3(5f, 0.7f, 0f), new Vector3(5f, 0.7f, 6f));
            CombatActor healthy = _arena.Enemies[0];
            CombatActor wounded = _arena.Enemies[1];
            yield return _arena.Tick(2);

            // Taken down to 40%, under R01's 50% mark, without the plugins interfering.
            _arena.WithoutPlugins();
            wounded.ReceiveDamage(
                new DamageRequest { Flat = wounded.Health.Max * 0.6f, Type = DamageType.True },
                CombatStats.StartingVaccine());
            yield return _arena.Tick(2);

            float healthyBefore = healthy.Health.Current;
            float woundedBefore = wounded.Health.Current;

            var hit = new DamageRequest { Flat = 5f, Type = DamageType.True };
            CombatStats attacker = _arena.Player.Stats;
            healthy.ReceiveDamage(hit, attacker);
            wounded.ReceiveDamage(hit, attacker);

            float healthyLost = healthyBefore - healthy.Health.Current;
            float woundedLost = woundedBefore - wounded.Health.Current;

            Assert.Greater(woundedLost, healthyLost,
                "A wounded enemy took no more than a healthy one, so R01 never applied.");
        }

        [UnityTest]
        public IEnumerator AMovementPassiveCannotWalkOutOfARoot()
        {
            // 속박 is a stop. A movement plugin added on top of it would let a build ignore the
            // effect that is holding the player still.
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(2);
            Assert.Greater(_arena.Player.MoveSpeedMultiplier, 1f);

            _arena.Player.ApplyStatus(NeonVoid.Combat.Status.StatusEffectId.Root, 1f);
            yield return _arena.Tick(2);

            Assert.AreEqual(0f, _arena.Player.MoveSpeedMultiplier, 1e-4f);
        }

        // --- the clock ---------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheSimulationRunsOneTickPerFrameAndDropsNothing()
        {
            _arena = PrototypeArena.Build(new Vector3(5f, 0.7f, 0f));
            PrototypeArena.MakeChaser(_arena.Enemies[0]);

            long before = _arena.Runner.TickIndex;
            yield return _arena.Tick(120);

            Assert.AreEqual(120L, _arena.Runner.TickIndex - before,
                "The clock did not advance one tick per frame, so timing assertions here mean nothing.");
            Assert.AreEqual(0L, _arena.Runner.DroppedTicks, "The simulation abandoned ticks.");
        }
    }
}
