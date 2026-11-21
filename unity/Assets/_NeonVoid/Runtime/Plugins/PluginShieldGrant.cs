using System;
using UnityEngine;

namespace NeonVoid.Plugins
{
    /// <summary>
    /// The 보호막 a card hands the carrier. docs/03 G05 주기 차폐막, G07 실행 차폐, R05 돌진 차폐.
    /// <para>
    /// How much and for how long are the card's; <b>what happens when two of them stand at
    /// once is not</b>. That is <see cref="NeonVoid.Combat.ShieldPolicy"/>: the absorption
    /// order is [확정] 신규부터 as of docs/08 D-004a, and the refresh and expiry readings are
    /// still open. A card grants - it does not decide what the stack does with the grant.
    /// </para>
    /// </summary>
    [Serializable]
    public struct PluginShieldGrant
    {
        [Tooltip("[초안] damage the shield can swallow. docs/03 states no amount for any of the three.")]
        public float Amount;

        [Tooltip("[초안] ticks it lasts. 0 for a shield with no timer.")]
        public int LifetimeTicks;

        public bool IsEmpty => Amount <= 0f && LifetimeTicks <= 0;

        public static PluginShieldGrant None => default;

        public void Validate(string proposalId)
        {
            if (float.IsNaN(Amount) || float.IsInfinity(Amount))
                throw new InvalidOperationException($"{proposalId}: a shield amount must be finite.");

            if (Amount <= 0f)
            {
                throw new InvalidOperationException(
                    $"{proposalId}: a 보호막 of {Amount} swallows nothing, which is a card that " +
                    "appears to protect and does not.");
            }

            if (LifetimeTicks < 0)
            {
                throw new InvalidOperationException(
                    $"{proposalId}: a shield cannot last {LifetimeTicks} ticks.");
            }
        }

        public override string ToString() =>
            LifetimeTicks > 0 ? $"{Amount:0.#} for {LifetimeTicks}t" : $"{Amount:0.#}, no timer";
    }
}
