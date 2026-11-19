using System.Collections.Generic;

namespace NeonVoid.Player
{
    /// <summary>One physical control claimed by more than one action.</summary>
    public readonly struct InputBindingConflict
    {
        public readonly string ControlPath;
        public readonly string FirstAction;
        public readonly string SecondAction;

        public InputBindingConflict(string controlPath, string firstAction, string secondAction)
        {
            ControlPath = controlPath;
            FirstAction = firstAction;
            SecondAction = secondAction;
        }

        public override string ToString() =>
            ControlPath + " is bound to both " + FirstAction + " and " + SecondAction;
    }

    /// <summary>
    /// Key conflict detection for the rebinding UI, required by docs/06 section 3.
    /// <para>
    /// Pure and side-effect free so the rule can be tested without a device, an asset or the
    /// Editor. It deliberately reports a conflict rather than resolving one: which binding
    /// wins is the player's choice, not a rule the simulation may make for them.
    /// </para>
    /// </summary>
    public static class InputBindingConflicts
    {
        /// <summary>
        /// Finds controls claimed by two or more different actions. Comparison ignores case,
        /// because Unity control paths are case-insensitive and "space" and "Space" are the
        /// same key to the player.
        /// </summary>
        /// <param name="bindings">(action name, control path) pairs, in display order.</param>
        public static IReadOnlyList<InputBindingConflict> Find(
            IEnumerable<KeyValuePair<string, string>> bindings)
        {
            var conflicts = new List<InputBindingConflict>();
            if (bindings == null)
                return conflicts;

            var claimedBy = new Dictionary<string, string>();

            foreach (var binding in bindings)
            {
                string action = binding.Key;
                string path = binding.Value;

                if (string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(path))
                    continue;

                string key = path.Trim().ToLowerInvariant();

                if (!claimedBy.TryGetValue(key, out string owner))
                {
                    claimedBy.Add(key, action);
                    continue;
                }

                // The same action bound to the same key twice is redundant, not a conflict:
                // it cannot produce ambiguous behaviour, so it must not block the player.
                if (string.Equals(owner, action, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                conflicts.Add(new InputBindingConflict(path, owner, action));
            }

            return conflicts;
        }
    }
}
