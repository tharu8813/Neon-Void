using System.Collections.Generic;
using UnityEngine;

namespace NeonVoid.Combat.Skills
{
    /// <summary>
    /// Every skill the vaccine may equip. [확정] D-009 (2026-11-22): 풀 7종.
    /// <para>
    /// The caster knows its three slots and nothing else, which is right - casting should not
    /// depend on what else exists. The 스킬 장착 터미널 needs the other four, so something has
    /// to hold them, and a component in the scene is where a designer can see and change the
    /// list.
    /// </para>
    /// <para>
    /// <b>Learning is not modelled here.</b> docs/04 section 1 keeps 습득 스킬 in 백신 영구
    /// 상태 and the acquisition path is [미확정], so for now the library is what the vaccine
    /// has. When learning arrives this becomes the catalogue and the save holds the subset.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkillLibrary : MonoBehaviour
    {
        [Tooltip("[확정] D-009: the seven. Filled by the scene builder from Content/Skills.")]
        [SerializeField] private SkillAsset[] skills = new SkillAsset[0];

        /// <summary>The pool, in a fixed order so the terminal lists them the same way twice.</summary>
        public IReadOnlyList<SkillAsset> All => skills ?? (skills = new SkillAsset[0]);

        /// <summary>Fills in what a scene would have serialized.</summary>
        public void Configure(SkillAsset[] pool) => skills = pool ?? new SkillAsset[0];

        /// <summary>The asset for a document key, or null when the pool has no such skill.</summary>
        public SkillAsset Find(string documentKey)
        {
            if (string.IsNullOrEmpty(documentKey) || skills == null)
                return null;

            for (int i = 0; i < skills.Length; ++i)
            {
                if (skills[i] != null && skills[i].DocumentKey == documentKey)
                    return skills[i];
            }

            return null;
        }

        /// <summary>
        /// Complains about a pool that cannot work, once, at startup. A duplicate key would
        /// make two skills one as far as the slots are concerned.
        /// </summary>
        private void Awake()
        {
            if (skills == null || skills.Length == 0)
            {
                Debug.LogWarning($"{name}: the skill pool is empty, so the 장착 터미널 offers nothing.", this);
                return;
            }

            var seen = new HashSet<string>();
            for (int i = 0; i < skills.Length; ++i)
            {
                if (skills[i] == null)
                {
                    Debug.LogError($"{name}: skill {i} is missing, so a row of the terminal is blank.", this);
                    continue;
                }

                if (!seen.Add(skills[i].DocumentKey))
                {
                    Debug.LogError(
                        $"{name}: '{skills[i].DocumentKey}' is in the pool twice; the slots treat " +
                        "them as one skill.", this);
                }
            }
        }
    }
}
