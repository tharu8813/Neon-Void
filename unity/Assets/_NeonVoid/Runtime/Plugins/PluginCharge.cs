using System;
using UnityEngine;

namespace NeonVoid.Plugins
{
    /// <summary>
    /// A gauge that fills as the carrier fights and, once full, turns a passive on for a while.
    /// docs/03 L03 피버타임 is 적중 또는 처치로 게이지, 충전 완료 후 일정시간 전체 능력치 증가.
    /// <para>
    /// Every number here is [초안]: the document leaves 버프 스택·충전 단위·계수 미확정 and
    /// gives no figure for any of them.
    /// </para>
    /// <para>
    /// A gauge rather than a status effect, for the same reason <see cref="PluginGrowth"/> is
    /// not one: a status needs a duration and a magnitude in the shared catalogue, and docs/02's
    /// catalogue is the document's own two tables rather than a place to add a row whenever a
    /// card wants one. The window here is the card's, and the stat contribution goes through
    /// the 플러그인 stage of docs/02 section 2's pipeline like every other passive.
    /// </para>
    /// </summary>
    [Serializable]
    public struct PluginCharge
    {
        [Tooltip("[초안] gauge gained per landed hit. docs/03 L03: 적중.")]
        public float PerHit;

        [Tooltip("[초안] gauge gained per kill, on top of the hit that caused it. docs/03 L03: 처치.")]
        public float PerKill;

        [Tooltip("[초안] gauge needed to set the buff off.")]
        public float Required;

        [Tooltip("[초안] ticks the buff lasts once it starts. 일정시간 is not a number.")]
        public int ActiveTicks;

        public bool IsEmpty => Required <= 0f && ActiveTicks <= 0;

        public static PluginCharge None => default;

        /// <summary>
        /// Gauge after an event. <paramref name="killed"/> adds the kill's share on top of the
        /// hit's, because a kill is a hit that also killed - docs/03 says 적중 또는 처치 and a
        /// kill that paid only the kill's share would be worth less than a graze whenever
        /// <see cref="PerKill"/> was the smaller number.
        /// </summary>
        public float Add(float charge, bool killed) =>
            charge + PerHit + (killed ? PerKill : 0f);

        /// <summary>Whether this much gauge sets the buff off.</summary>
        public bool IsFull(float charge) => Required > 0f && charge >= Required;

        /// <summary>0 to 1, for a gauge on the HUD. 1 only when it is full.</summary>
        public float Fraction(float charge)
        {
            if (Required <= 0f)
                return 0f;

            float fraction = charge / Required;
            return fraction > 1f ? 1f : fraction;
        }

        public void Validate(string proposalId)
        {
            RequireFinite(PerHit, proposalId, nameof(PerHit));
            RequireFinite(PerKill, proposalId, nameof(PerKill));
            RequireFinite(Required, proposalId, nameof(Required));

            if (PerHit < 0f || PerKill < 0f)
                throw new InvalidOperationException($"{proposalId}: a gauge does not drain on a hit.");

            if (Required <= 0f)
            {
                throw new InvalidOperationException(
                    $"{proposalId}: a gauge that needs {Required} is already full, so the buff would " +
                    "never be off - which is a permanent passive wearing a gauge's name.");
            }

            if (PerHit <= 0f && PerKill <= 0f)
            {
                throw new InvalidOperationException(
                    $"{proposalId}: nothing fills this gauge, so the buff can never start.");
            }

            if (ActiveTicks < 1)
            {
                throw new InvalidOperationException(
                    $"{proposalId}: a buff lasting {ActiveTicks} ticks is a card that charges and " +
                    "then does nothing anybody can see.");
            }
        }

        private static void RequireFinite(float value, string proposalId, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidOperationException($"{proposalId}: {name} must be finite.");
        }

        public override string ToString() =>
            $"{PerHit:0.##}/hit +{PerKill:0.##}/kill to {Required:0.##}, {ActiveTicks}t";
    }
}
