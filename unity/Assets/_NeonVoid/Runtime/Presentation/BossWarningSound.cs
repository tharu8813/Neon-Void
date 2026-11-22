using NeonVoid.Combat.AI;
using UnityEngine;

namespace NeonVoid.Presentation
{
    /// <summary>
    /// Sounds the boss's 위험 경고 when a telegraph begins.
    /// <para>
    /// [초안] docs/05 section 6 wants 보스 경고 to be one of the sounds with a face of its own.
    /// The same section also says <i>위험 공격은 음향 없이도 피할 수 있어야 한다</i>, so this is
    /// deliberately only a second channel: the telegraph is drawn whether or not this component
    /// exists, whether or not 효과음 is muted, and whether or not the player can hear.
    /// </para>
    /// <para>
    /// It sits beside <see cref="AreaBossBrain"/> the way <c>ActorDeathReaction</c> sits beside
    /// an actor - a presentation component on a runtime-built object - so the brain never has
    /// to know a sound exists.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AreaBossBrain))]
    public sealed class BossWarningSound : MonoBehaviour
    {
        [Tooltip("Found in the scene when left empty.")]
        [SerializeField] private GameAudio gameAudio;

        private AreaBossBrain _brain;

        /// <summary>How many warnings this boss has sounded. For a test.</summary>
        public int Warnings { get; private set; }

        /// <summary>Fills in what a scene would have serialized.</summary>
        public void Configure(GameAudio audio) => gameAudio = audio;

        private void OnEnable()
        {
            _brain = GetComponent<AreaBossBrain>();

            if (gameAudio == null)
                gameAudio = FindFirstObjectByType<GameAudio>();

            if (_brain != null)
                _brain.TelegraphBegan += OnTelegraphBegan;
        }

        private void OnDisable()
        {
            if (_brain != null)
                _brain.TelegraphBegan -= OnTelegraphBegan;
        }

        private void OnTelegraphBegan(BossTelegraph telegraph)
        {
            ++Warnings;

            // Reads a telegraph the brain already committed to. Nothing here can change what
            // the attack covers or when it lands.
            if (gameAudio != null)
                gameAudio.Play(AudioCue.BossWarning);
        }
    }
}
