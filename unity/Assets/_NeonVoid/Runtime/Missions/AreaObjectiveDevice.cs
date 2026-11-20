using System;
using NeonVoid.Combat;
using UnityEngine;

namespace NeonVoid.Missions
{
    /// <summary>
    /// A 소목표 in the world: a device the player destroys.
    /// <para>
    /// docs/01's CPU objectives are 연산 코어 방어 / 프로세스 격리 / 과열 밸브 차단, all [초안] and
    /// all owned by D-015. This is the one of those three shapes that needs nothing the game does
    /// not already have - a destructible thing - so it is the first. Defending a core for a
    /// duration and isolating named processes are different components, not different settings on
    /// this one.
    /// </para>
    /// <para>
    /// <b>It completes exactly once.</b> docs/01 section 6.6: 완료 보상은 반복 호출에도 정확히
    /// 1회. The flag is here and not only in the flow, because the flow counts objectives without
    /// knowing which device each one came from - so two reports from one device would be two
    /// objectives, and an area with three devices could be cleared by destroying one of them
    /// three times if anything ever revived it.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaObjectiveDevice : MonoBehaviour
    {
        [Header("What is destroyed")]
        [Tooltip("The device's actor. Found on this object when left empty.")]
        [SerializeField] private CombatActor device;

        [Header("Identity")]
        [Tooltip("[초안] which of docs/01's objective candidates this is. Display and logs only.")]
        [SerializeField] private string objectiveName = "과열 밸브 차단";

        private bool _completed;
        private bool _subscribed;

        /// <summary>Raised once, when the device is destroyed. The area runtime listens.</summary>
        public event Action<AreaObjectiveDevice> Completed;

        /// <summary>Whether this objective has been completed. Never goes back to false.</summary>
        public bool IsCompleted => _completed;

        /// <summary>[초안] display name, for the HUD and the log.</summary>
        public string ObjectiveName => objectiveName;

        /// <summary>The actor that has to be destroyed, or null when none was found.</summary>
        public CombatActor Device => device;

        /// <summary>
        /// Names the actor to destroy. The injection seam for a test and for anything that builds
        /// an area at runtime; call it while this object is still deactivated, so
        /// <see cref="OnEnable"/> subscribes to the actor rather than to nothing.
        /// </summary>
        public void Configure(CombatActor deviceActor, string displayName = null)
        {
            device = deviceActor;
            if (!string.IsNullOrWhiteSpace(displayName))
                objectiveName = displayName;
        }

        private void Awake()
        {
            if (device == null)
                device = GetComponent<CombatActor>();

            if (device == null)
            {
                Debug.LogError(
                    $"{name}: an objective device needs a CombatActor to be destroyed, so this " +
                    "objective can never be completed.", this);
            }
        }

        private void OnEnable()
        {
            if (device == null || _subscribed)
                return;

            device.Died += OnDeviceDestroyed;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (device == null || !_subscribed)
                return;

            device.Died -= OnDeviceDestroyed;
            _subscribed = false;
        }

        private void OnDeviceDestroyed(CombatActor actor)
        {
            if (_completed)
            {
                Debug.LogWarning(
                    $"{name}: {objectiveName} reported a second destruction; ignored. " +
                    "완료 보상은 정확히 1회 (docs/01 section 6.6).", this);
                return;
            }

            _completed = true;
            Debug.Log($"Objective complete: {objectiveName}.", this);
            Completed?.Invoke(this);
        }

        public override string ToString() => $"{objectiveName} {(_completed ? "done" : "outstanding")}";
    }
}
