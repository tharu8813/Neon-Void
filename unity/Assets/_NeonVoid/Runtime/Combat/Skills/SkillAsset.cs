using UnityEngine;

namespace NeonVoid.Combat.Skills
{
    /// <summary>
    /// One skill as data. [확정] CLAUDE.md: skills and balance live in ScriptableObjects or
    /// other verifiable data, never as numbers inside behaviour.
    /// <para>
    /// docs/03 section 6 instructs that two or three of its prototype pool be wired up to prove
    /// learning, equipping and the combat connection, with the numbers replaceable at any time.
    /// Replaceable is the reason this is an asset: changing a cooldown is editing an asset, not
    /// editing and recompiling a component.
    /// </para>
    /// <para>
    /// <see cref="DocumentKey"/> carries the id from the document (`skill.arc_pulse`), so a
    /// skill in the project can be traced back to the line of design that asked for it.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Neon Void/Skill (Prototype)", fileName = "NewSkill")]
    public sealed class SkillAsset : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("The id used in docs/03, e.g. skill.arc_pulse.")]
        [SerializeField] private string documentKey = "skill.unnamed";

        [Tooltip("Shown to the player. '소프트웨어' is never a display name (CLAUDE.md).")]
        [SerializeField] private string displayName = "Unnamed";

        [Header("Cast (all values 초안)")]
        [SerializeField] private SkillDefinition definition;

        [Header("Effect")]
        [SerializeField] private SkillShape shape;

        [SerializeField] private SkillStatusEffect appliedStatus;

        public string DocumentKey => documentKey;
        public string DisplayName => displayName;
        public SkillDefinition Definition => definition;
        public SkillShape Shape => shape;
        public SkillStatusEffect AppliedStatus => appliedStatus;

        /// <summary>
        /// Checks everything that can be checked without a scene. Called when the asset is
        /// equipped, so broken data is reported at the skill it belongs to rather than as a
        /// strange effect later.
        /// </summary>
        public void Validate()
        {
            definition.Validate();
            shape.Validate(definition.IsMovementSkill);
            appliedStatus.Validate();
        }

        /// <summary>Editor-side authoring entry point, used by the prototype scene builder.</summary>
        public void Configure(
            string key,
            string name,
            SkillDefinition skillDefinition,
            SkillShape skillShape,
            SkillStatusEffect status)
        {
            documentKey = key;
            displayName = name;
            definition = skillDefinition;
            shape = skillShape;
            appliedStatus = status;
            Validate();
        }
    }
}
