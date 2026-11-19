using System;
using UnityEngine;

namespace NeonVoid.Combat.AI
{
    /// <summary>
    /// One candidate the enemy can see. Carried by value so a brain cannot reach through it
    /// into the scene and change anything.
    /// </summary>
    public readonly struct PerceivedActor
    {
        public readonly int ActorId;
        public readonly Vector2 PositionXZ;
        public readonly bool IsAlive;

        public PerceivedActor(int actorId, Vector2 positionXZ, bool isAlive)
        {
            ActorId = actorId;
            PositionXZ = positionXZ;
            IsAlive = isAlive;
        }
    }

    /// <summary>
    /// What an enemy knows at the start of a tick: where it is, what it can see, and which of
    /// its own actions are currently denied.
    /// <para>
    /// The control flags are read from the enemy's own status container, so enemies obey the
    /// same 정지 / 속박 / 구속 rules as the player. docs/02 section 5 separates them
    /// deliberately: Stun denies every action, Root denies movement only, and Slow reduces
    /// speed without denying anything, so an enemy that is rooted must still be able to shoot.
    /// </para>
    /// </summary>
    public readonly struct EnemyPerception
    {
        public readonly Vector2 SelfPositionXZ;

        /// <summary>Visible candidates. Order must be stable for the decision to be deterministic.</summary>
        public readonly PerceivedActor[] Candidates;

        /// <summary>정지 - every action denied.</summary>
        public readonly bool Stunned;

        /// <summary>속박 - movement denied, attacks still allowed.</summary>
        public readonly bool Rooted;

        /// <summary>구속 - fraction of base speed left, 1 when unimpeded.</summary>
        public readonly float SpeedMultiplier;

        public EnemyPerception(
            Vector2 selfPositionXZ,
            PerceivedActor[] candidates,
            bool stunned = false,
            bool rooted = false,
            float speedMultiplier = 1f)
        {
            SelfPositionXZ = selfPositionXZ;
            Candidates = candidates ?? Array.Empty<PerceivedActor>();
            Stunned = stunned;
            Rooted = rooted;
            SpeedMultiplier = float.IsNaN(speedMultiplier) ? 1f : Mathf.Clamp(speedMultiplier, 0f, 1f);
        }

        /// <summary>Finds a candidate by id, so a brain can keep tracking the target it chose.</summary>
        public bool TryGet(int actorId, out PerceivedActor actor)
        {
            for (int i = 0; i < Candidates.Length; ++i)
            {
                if (Candidates[i].ActorId != actorId)
                    continue;
                actor = Candidates[i];
                return true;
            }
            actor = default;
            return false;
        }
    }
}
