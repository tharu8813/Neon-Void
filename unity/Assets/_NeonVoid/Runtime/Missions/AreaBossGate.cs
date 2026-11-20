using System;
using NeonVoid.Combat;
using UnityEngine;

namespace NeonVoid.Missions
{
    /// <summary>
    /// Keeps the area's boss out of the fight until the objectives are done, and reports its
    /// death once.
    /// <para>
    /// [확정] docs/01 section 6.6: 목표가 파괴/완료되기 전에 보스·철수가 활성화되지 않음.
    /// <see cref="MissionFlow"/> refuses a <c>TryDefeatBoss</c> before then, but a refusal is not
    /// enough on its own: a boss standing in the room is a boss the player can shoot, and one
    /// killed early would be a boss the flow refused to count and the world has no more of. So
    /// the gate holds the object inactive, which makes it unshootable and untargetable rather
    /// than merely uncounted.
    /// </para>
    /// <para>
    /// <b>This is not the boss fight.</b> docs/01 section 3 asks each boss for 최소 3개의 형태가
    /// 다른 패턴 + 1개 지역 기믹 + 1개 페이즈 변화, with 0.8~1.5초 텔레그래프 and 전조 표시와 실제
    /// 충돌 볼륨의 일치. All of that is content behind D-015 and none of it is here. This is the
    /// gate the fight will be put behind.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaBossGate : MonoBehaviour
    {
        [Header("The boss")]
        [Tooltip("The boss actor. Found on this object when left empty.")]
        [SerializeField] private CombatActor boss;

        [Tooltip(
            "The object held inactive until the boss opens. Defaults to the boss actor's own " +
            "object, so it cannot be shot before its phase.")]
        [SerializeField] private GameObject heldObject;

        [Tooltip("[초안] 보스 컨셉 from docs/01 section 2. Display and logs only.")]
        [SerializeField] private string bossName = "코어 워처";

        private bool _open;
        private bool _defeated;
        private bool _subscribed;

        /// <summary>Raised once, when the boss dies after having been opened.</summary>
        public event Action<AreaBossGate> Defeated;

        /// <summary>Whether the boss has been let into the fight.</summary>
        public bool IsOpen => _open;

        /// <summary>Whether the boss is down. Never goes back to false.</summary>
        public bool IsDefeated => _defeated;

        /// <summary>[초안] display name.</summary>
        public string BossName => bossName;

        public CombatActor Boss => boss;

        /// <summary>
        /// Names the boss and the object to hold. The injection seam for a test and for anything
        /// that builds an area at runtime; call it while this object is still deactivated, so
        /// <see cref="Awake"/> sees the references rather than searching for them.
        /// </summary>
        public void Configure(CombatActor bossActor, GameObject objectToHold = null, string displayName = null)
        {
            boss = bossActor;
            heldObject = objectToHold != null ? objectToHold : (bossActor != null ? bossActor.gameObject : null);
            if (!string.IsNullOrWhiteSpace(displayName))
                bossName = displayName;
        }

        private void Awake()
        {
            if (boss == null)
                boss = GetComponent<CombatActor>();
            if (heldObject == null && boss != null)
                heldObject = boss.gameObject;

            if (boss == null)
            {
                Debug.LogError(
                    $"{name}: a boss gate needs a CombatActor, so this area's boss can never be " +
                    "defeated and the area can never be purified.", this);
                return;
            }

            // Subscribed in Awake rather than OnEnable, because the held object is deactivated
            // immediately below: an OnEnable subscription on the boss's own object would be
            // undone by the very deactivation that hides it.
            boss.Died += OnBossDied;
            _subscribed = true;

            Close();
        }

        private void OnDestroy()
        {
            if (boss != null && _subscribed)
                boss.Died -= OnBossDied;
            _subscribed = false;
        }

        /// <summary>
        /// Lets the boss in. Called by <see cref="HardwareAreaRuntime"/> when the flow reaches
        /// <see cref="AreaProgressPhase.Boss"/>.
        /// </summary>
        public void Open()
        {
            if (_open || _defeated)
                return;

            _open = true;
            if (heldObject != null)
                heldObject.SetActive(true);

            Debug.Log($"Boss active: {bossName}.", this);
        }

        /// <summary>Holds the boss out of the fight. Called on entry, before any objective is done.</summary>
        public void Close()
        {
            if (_defeated)
                return;

            _open = false;
            if (heldObject != null)
                heldObject.SetActive(false);
        }

        private void OnBossDied(CombatActor actor)
        {
            if (_defeated)
                return;

            if (!_open)
            {
                // Nothing in the game can do this while the object is inactive. If something
                // does, counting it would hand the player a purification for a fight that never
                // happened, and the flow would accept it because the flow cannot see the room.
                Debug.LogError(
                    $"{name}: {bossName} died before its phase opened; not counted. The area " +
                    "cannot be purified, which is the safe failure.", this);
                return;
            }

            _defeated = true;
            Debug.Log($"Boss defeated: {bossName}. 정화 is now available.", this);
            Defeated?.Invoke(this);
        }

        public override string ToString() =>
            $"{bossName} {(_defeated ? "defeated" : _open ? "fighting" : "held")}";
    }
}
