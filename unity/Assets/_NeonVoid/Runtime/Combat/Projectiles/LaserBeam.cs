using NeonVoid.Core;
using UnityEngine;

namespace NeonVoid.Combat.Projectiles
{
    /// <summary>
    /// L01 레이저 코어's shot: a beam resolved on the tick it is fired, and a short-lived visual
    /// that shows where it went.
    /// <para>
    /// <b>Hitscan, not a fast projectile.</b> docs/03 says 탄환을 레이저로 변환 - a 레이저 arrives
    /// where it is pointing, it does not travel there. Faking it with a very fast
    /// <see cref="CombatProjectile"/> would put the damage one or more ticks after the shot and
    /// make the arrival time depend on the distance, which is the thing a laser does not do.
    /// </para>
    /// <para>
    /// <b>Damage is resolved synchronously, inside the attacker's tick.</b> The beam is fired
    /// from <see cref="AutoAttacker"/> while the runner is already stepping it, so resolving on
    /// the spot keeps the whole shot inside one tick. This component is then only the visual and
    /// counts its own ticks down; it never damages anything.
    /// </para>
    /// <para>
    /// <b>관통 is the attacker's, not the beam's.</b> [미확정] D-005 names L01·관통 동시 장착 as
    /// undecided, so how many enemies one beam may damage arrives from the caller
    /// (<see cref="MainShotSpec.PiercesEverything"/>) and is decided in Plugins. A beam that
    /// made its own rule would be a second definition of 관통 to drift from the first.
    /// </para>
    /// </summary>
    public sealed class LaserBeam : MonoBehaviour, ISimulationTickReceiver
    {
        /// <summary>[초안] how far the beam reaches. docs/03 L01 gives no range.</summary>
        public const float DraftRangeUnits = 22f;

        /// <summary>[초안] the beam's sweep radius - its 유효 충돌크기, not how wide it looks.</summary>
        public const float DraftRadiusUnits = 0.22f;

        /// <summary>[초안] ticks the visual stays on screen. The damage is already done.</summary>
        public const int DraftVisibleTicks = 6;

        private SimulationRunner _runner;
        private int _ticksRemaining;

        int ISimulationTickReceiver.TickOrder => SimulationRunner.Order.Projectiles;

        /// <summary>
        /// Fires one beam: resolves every hit along it and spawns the visual.
        /// </summary>
        /// <returns>How many actors it damaged, so a test can read the shot without a collider.</returns>
        public static int Fire(
            CombatActor attacker,
            in DamageRequest damage,
            Vector3 origin,
            Vector3 direction,
            float rangeUnits,
            float radiusUnits,
            int maxTargets,
            float onHitEffectScale,
            Material material,
            SimulationRunner runner)
        {
            Vector3 aim = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
            float range = rangeUnits > 0f && !float.IsNaN(rangeUnits) && !float.IsInfinity(rangeUnits)
                ? rangeUnits
                : DraftRangeUnits;
            float radius = radiusUnits > 0f && !float.IsNaN(radiusUnits) && !float.IsInfinity(radiusUnits)
                ? radiusUnits
                : DraftRadiusUnits;

            CombatStats attackerStats =
                attacker != null ? attacker.Stats : CombatStats.StartingVaccine();
            var ledger = new ProjectileHitLedger(Mathf.Max(1, maxTargets));
            float reached = range;
            int damaged = 0;

            // Sorted by distance, because a wall must stop the beam before the enemies behind
            // it are considered. SphereCastAll's order is not specified.
            RaycastHit[] hits = Physics.SphereCastAll(
                origin, radius, aim, range, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; ++i)
            {
                var actor = hits[i].collider.GetComponentInParent<CombatActor>();
                if (actor == null)
                {
                    // A collider already overlapping the muzzle is not something the beam ran
                    // into: SphereCastAll reports an overlap at distance 0, and treating that
                    // as a wall would make the shooter's own body stop its own shot.
                    if (hits[i].distance <= 0.001f)
                        continue;

                    // Geometry: the beam stops here. A laser does not pass through a wall any
                    // more than a 탄환 does, and CombatProjectile stops on the same test.
                    reached = hits[i].distance;
                    break;
                }

                if (!actor.IsAlive || attacker == null || !attacker.IsHostileTo(actor))
                    continue;

                if (ledger.TryRegisterHit(actor.ActorId) != ProjectileHitVerdict.Allowed)
                    continue;

                bool crit = attacker.RollCrit(damage);
                DamageOutcome outcome = actor.ReceiveDamage(damage, attackerStats, crit);
                if (outcome.Lifesteal.Total > 0f)
                    attacker.Heal(outcome.Lifesteal.Total);

                // Still 탄환 적중 as far as the cards are concerned: L01 converts what the
                // basic attack looks like, not what triggers off it. G10 전위 연쇄 and G17
                // 폭발 각인 are written against the basic attack landing, and a card that
                // silently stopped triggering them would be a nerf nobody wrote down.
                attacker.NotifyHitLanded(
                    actor, damage, outcome, HitOrigin.Projectile, onHitEffectScale);

                damaged++;
                reached = hits[i].distance;

                if (ledger.IsSpent)
                    break;
            }

            Show(origin, aim, reached, radius, material, runner);
            return damaged;
        }

        /// <summary>The visual only. Nothing here damages anything.</summary>
        private static void Show(
            Vector3 origin, Vector3 aim, float length, float radius, Material material,
            SimulationRunner runner)
        {
            float drawn = Mathf.Max(0.5f, length);
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beam.name = "NV Laser";
            beam.transform.position = origin + aim * (drawn * 0.5f);
            beam.transform.rotation = Quaternion.LookRotation(aim, Vector3.up);

            // [초안] docs/03 asks for 유효 충돌크기·VFX 크기 분리 by name: the beam is drawn
            // wider than it sweeps, deliberately, and the width here reaches nothing.
            beam.transform.localScale = new Vector3(radius * 1.6f, radius * 1.6f, drawn);

            Object.Destroy(beam.GetComponent<Collider>());

            if (material != null)
            {
                var renderer = beam.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.sharedMaterial = material;
            }

            var fade = beam.AddComponent<LaserBeam>();
            fade._ticksRemaining = DraftVisibleTicks;
            fade._runner = runner != null ? runner : FindFirstObjectByType<SimulationRunner>();

            if (fade._runner != null)
                fade._runner.Register(fade);
            else
                Object.Destroy(beam);
        }

        void ISimulationTickReceiver.OnSimulationTick(long tickIndex)
        {
            if (--_ticksRemaining > 0)
                return;

            if (_runner != null)
                _runner.Unregister(this);
            Destroy(gameObject);
        }

        private void OnDisable()
        {
            if (_runner != null)
                _runner.Unregister(this);
        }
    }
}
