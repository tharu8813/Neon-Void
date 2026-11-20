using System;
using System.Collections.Generic;
using NeonVoid.Combat;
using UnityEngine;

namespace NeonVoid.Missions
{
    /// <summary>One enemy a room holds, and whether it counts as a 정예.</summary>
    public readonly struct AreaRoomOccupant
    {
        public readonly CombatActor Actor;

        public readonly bool IsElite;

        public AreaRoomOccupant(CombatActor actor, bool isElite)
        {
            Actor = actor;
            IsElite = isElite;
        }
    }

    /// <summary>
    /// Holds a room's enemies out of the simulation until the player walks in, and reports their
    /// deaths.
    /// <para>
    /// <b>Why a room has a gate.</b> A nominal area is budgeted for 48 enemies plus 3 정예 (GDD
    /// v1 section 5.4). Spawning all of them awake means fifty-one AI agents pathing at once, and
    /// - worse than the frame cost - every one of them walking at the player from the moment the
    /// area loads. docs/01 section 7's budget gives an area 5.5분 of 일반 전투 spread across
    /// 2~3목표; one converging mob is not that, and no amount of tuning the enemies would make it
    /// that.
    /// </para>
    /// <para>
    /// Held by deactivating, like <see cref="AreaBossGate"/>: an enemy the player can shoot
    /// through a wall from two rooms away is a worse problem than one that is merely idle, and
    /// an inactive object cannot be targeted, cannot tick, and costs nothing.
    /// </para>
    /// <para>
    /// <b>It never closes again.</b> A room the player has entered stays awake, because closing
    /// it would park a half-dead fight mid-air and reopen it healed - and the enemies the player
    /// already shot would come back as a surprise. Leaving and returning is cheap by comparison.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaRoomGate : MonoBehaviour
    {
        private readonly List<AreaRoomOccupant> _occupants = new List<AreaRoomOccupant>();

        private Vector3 _centre;
        private float _halfWidthX;
        private float _halfWidthZ;
        private bool _subscribed;

        /// <summary>Raised once per enemy, when it dies. The area runtime pays for it.</summary>
        public event Action<AreaRoomGate, bool> EnemyKilled;

        /// <summary>Whether the player has been in and the room is awake.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>Enemies the room was built with.</summary>
        public int HeldCount => _occupants.Count;

        /// <summary>Enemies still standing.</summary>
        public int AliveCount
        {
            get
            {
                int alive = 0;
                for (int i = 0; i < _occupants.Count; ++i)
                {
                    if (_occupants[i].Actor != null && _occupants[i].Actor.IsAlive)
                        ++alive;
                }

                return alive;
            }
        }

        /// <summary>
        /// Names the room's footprint, on the XZ plane, and what it holds.
        /// <para>
        /// A footprint in world units rather than a cell rectangle, so neither this nor
        /// <see cref="HardwareAreaRuntime"/> has to know the layout's cell size or where the area
        /// was built - the builder knows both and does the conversion once.
        /// </para>
        /// Call it while this object is still deactivated.
        /// </summary>
        public void Configure(Vector3 centre, float halfWidthX, float halfWidthZ, IList<AreaRoomOccupant> occupants)
        {
            if (halfWidthX <= 0f || halfWidthZ <= 0f)
                throw new ArgumentOutOfRangeException(nameof(halfWidthX), "A room has a footprint.");

            _centre = centre;
            _halfWidthX = halfWidthX;
            _halfWidthZ = halfWidthZ;

            _occupants.Clear();
            if (occupants == null)
                return;

            for (int i = 0; i < occupants.Count; ++i)
            {
                if (occupants[i].Actor != null)
                    _occupants.Add(occupants[i]);
            }
        }

        private void Awake()
        {
            // Subscribed in Awake, not OnEnable: Close() below deactivates what this holds, and
            // an occupant on a deactivated object would never raise an OnEnable of its own.
            for (int i = 0; i < _occupants.Count; ++i)
                _occupants[i].Actor.Died += OnOccupantDied;

            _subscribed = true;
            Close();
        }

        private void OnDestroy()
        {
            if (!_subscribed)
                return;

            for (int i = 0; i < _occupants.Count; ++i)
            {
                if (_occupants[i].Actor != null)
                    _occupants[i].Actor.Died -= OnOccupantDied;
            }

            _subscribed = false;
        }

        /// <summary>Whether a world position is inside this room, measured on the XZ plane.</summary>
        public bool Contains(Vector3 position) =>
            Mathf.Abs(position.x - _centre.x) <= _halfWidthX
            && Mathf.Abs(position.z - _centre.z) <= _halfWidthZ;

        /// <summary>Wakes the room. Does nothing the second time.</summary>
        public void Open()
        {
            if (IsOpen)
                return;

            IsOpen = true;
            for (int i = 0; i < _occupants.Count; ++i)
            {
                CombatActor actor = _occupants[i].Actor;
                if (actor != null)
                    actor.gameObject.SetActive(true);
            }

            if (_occupants.Count > 0)
                Debug.Log($"{name}: {_occupants.Count} enemy/enemies activated.", this);
        }

        /// <summary>Holds the room out of the simulation. Called once, on build.</summary>
        public void Close()
        {
            IsOpen = false;
            for (int i = 0; i < _occupants.Count; ++i)
            {
                CombatActor actor = _occupants[i].Actor;
                if (actor != null)
                    actor.gameObject.SetActive(false);
            }
        }

        private void OnOccupantDied(CombatActor actor)
        {
            for (int i = 0; i < _occupants.Count; ++i)
            {
                if (_occupants[i].Actor != actor)
                    continue;

                EnemyKilled?.Invoke(this, _occupants[i].IsElite);
                return;
            }
        }

        public override string ToString() =>
            $"{name} {(IsOpen ? "open" : "held")} {AliveCount}/{_occupants.Count} alive";
    }
}
