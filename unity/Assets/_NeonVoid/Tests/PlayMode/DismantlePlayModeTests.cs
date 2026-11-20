using System.Collections;
using NeonVoid.Combat;
using NeonVoid.Combat.Status;
using NeonVoid.Plugins;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeonVoid.Tests.PlayModeTests
{
    /// <summary>
    /// R04 해체 프로토콜 in a running scene: a stack is applied by an effect resolving, and the
    /// stack actually removes health over time.
    /// <para>
    /// The second half is the one this project keeps getting wrong. 재생 and 무결성 공격 were
    /// both tracked with a duration, a stack count and a magnitude that nothing read, so
    /// applying either changed no health whatsoever - and every unit test passed. So the
    /// assertion here is health falling with no other damage in flight.
    /// </para>
    /// </summary>
    public sealed class DismantlePlayModeTests
    {
        private PrototypeArena _arena;

        [TearDown]
        public void TearDown()
        {
            _arena?.Dispose();
            _arena = null;
        }

        /// <summary>
        /// Lands one shot and waits for its damage and every effect it sets off to finish, so
        /// what happens afterwards is the damage over time and nothing else. The player's
        /// attacker is disabled by the arena and plain dummies never attack, so once this
        /// returns no other damage is in flight.
        /// </summary>
        /// <summary>
        /// A target with a health pool it cannot die out of. The long waits these tests need -
        /// 전위 연쇄 and 폭발 각인 both carry 대상별 내부 CD 3초 - are long enough for the DoT to
        /// finish the dummy off, and then the next shot lands on a corpse and measures nothing.
        /// </summary>
        private static void MakeUnkillable(CombatActor actor)
        {
            CombatStats tough = PrototypeArena.EnemyStats();
            tough.MaxHealth = 100000f;
            actor.Configure(PrototypeArena.EnemyTeam, tough);
        }

        private IEnumerator LandOneShotOn(CombatActor target)
        {
            _arena.FireAt(target);
            yield return _arena.Tick(45);
        }

        [UnityTest]
        public IEnumerator AnEffectResolvingPutsADismantleStackOnTheTarget()
        {
            _arena = PrototypeArena.Build(new Vector3(0f, 0.7f, 5f), new Vector3(1.6f, 0.7f, 5f));
            CombatActor target = _arena.Enemies[0];

            yield return LandOneShotOn(target);

            Assert.GreaterOrEqual(
                target.Status.StacksOf(StatusEffectId.Dismantle), 1,
                "No 적중 효과 resolved into a 해체 stack.");
        }

        [UnityTest]
        public IEnumerator TheStackActuallyRemovesHealthOverTime()
        {
            // The whole point. Nothing else is damaging anyone by the time this measures.
            _arena = PrototypeArena.Build(new Vector3(0f, 0.7f, 5f), new Vector3(1.6f, 0.7f, 5f));
            CombatActor target = _arena.Enemies[0];

            yield return LandOneShotOn(target);
            Assert.GreaterOrEqual(target.Status.StacksOf(StatusEffectId.Dismantle), 1);

            float before = target.Health.Current;
            yield return _arena.Tick(StatusCatalog.Draft()[StatusEffectId.Dismantle].PeriodicIntervalTicks + 2);

            Assert.Less(
                target.Health.Current, before,
                "해체 was applied and removed no health - the status did nothing.");
        }

        [UnityTest]
        public IEnumerator MoreResolvedEffectsMeanMoreStacksAndMoreDamagePerTick()
        {
            _arena = PrototypeArena.Build(new Vector3(0f, 0.7f, 5f), new Vector3(1.6f, 0.7f, 5f));
            CombatActor target = _arena.Enemies[0];
            int interval = StatusCatalog.Draft()[StatusEffectId.Dismantle].PeriodicIntervalTicks;
            MakeUnkillable(target);

            yield return LandOneShotOn(target);
            int oneStack = target.Status.StacksOf(StatusEffectId.Dismantle);
            float onePerTick = target.Status.TotalMagnitudeOf(StatusEffectId.Dismantle);

            // 대상별 내부 CD 3초 on 전위 연쇄 and 폭발 각인, so the next shot has to wait for
            // them before it can produce effects on the same target again.
            yield return _arena.Tick(200);
            yield return LandOneShotOn(target);

            Assert.Greater(
                target.Status.StacksOf(StatusEffectId.Dismantle), oneStack,
                "A second round of effects added no stack.");
            Assert.Greater(
                target.Status.TotalMagnitudeOf(StatusEffectId.Dismantle), onePerTick,
                "More stacks did not mean more damage, so aggregating lost the stacks.");

            float before = target.Health.Current;
            yield return _arena.Tick(interval + 2);

            Assert.Less(target.Health.Current, before, "The stacked DoT stopped removing health.");
        }

        [UnityTest]
        public IEnumerator HoweverManyStacksItIsStillOneInstance()
        {
            // docs/03 section 7: 무제한 중첩이 하나의 집계 상태를 사용하여 스택 수가 많아져도
            // 선형/안정된 비용. Asserted on the live container rather than only in a unit test,
            // because this is the object the tick loop walks.
            _arena = PrototypeArena.Build(new Vector3(0f, 0.7f, 5f), new Vector3(1.6f, 0.7f, 5f));
            CombatActor target = _arena.Enemies[0];
            MakeUnkillable(target);

            for (int round = 0; round < 3; ++round)
            {
                yield return LandOneShotOn(target);
                yield return _arena.Tick(200);
            }

            int dismantleInstances = 0;
            for (int i = 0; i < target.Status.Instances.Count; ++i)
                if (target.Status.Instances[i].Id == StatusEffectId.Dismantle)
                    dismantleInstances++;

            Assert.AreEqual(1, dismantleInstances, "해체 is not one aggregate state.");
        }

        [UnityTest]
        public IEnumerator TheCardIsCarriedByThePrototypeSetSoNothingHasToGrantItByHand()
        {
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(1);

            Assert.IsTrue(
                _arena.Procs.Loadout.Carries("R04"),
                "해체 프로토콜 exists and nothing in the game hands it out.");
        }

        [UnityTest]
        public IEnumerator TheDocumentsRequiredChainRunsInAScene()
        {
            // docs/03 section 7's first required integration: G10 번개 → G17 폭발 → R04 해체
            // DoT의 정상 연쇄가 차단되지 않음. Two enemies, so the lightning has somewhere to
            // hop and the explosion has something to catch.
            _arena = PrototypeArena.Build(
                new Vector3(0f, 0.7f, 5f), new Vector3(1.6f, 0.7f, 5f), new Vector3(-1.6f, 0.7f, 5f));

            yield return LandOneShotOn(_arena.Enemies[0]);

            int marked = 0;
            for (int i = 0; i < _arena.Enemies.Count; ++i)
                if (_arena.Enemies[i].Status.Has(StatusEffectId.Dismantle))
                    marked++;

            Assert.Greater(marked, 0, "The documented chain produced no 해체 at all.");
            Assert.IsTrue(
                _arena.Procs.LastDrain == null || _arena.Procs.LastDrain.CompletedWithinBudget,
                $"The documented combination hit a resource limit: {_arena.Procs.LastDrain}");
        }
    }
}
