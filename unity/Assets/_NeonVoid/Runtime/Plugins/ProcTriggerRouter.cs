using System;
using System.Collections.Generic;
using NeonVoid.Combat;
using NeonVoid.Combat.Proc;
using NeonVoid.Core;

namespace NeonVoid.Plugins
{
    /// <summary>
    /// Connects landed hits to the proc queue, and keeps causal ancestry attached while
    /// triggered effects resolve.
    /// <para>
    /// <b>The part that matters.</b> A plugin effect deals damage, that damage lands, and the
    /// landing is itself a hit that can trigger plugins again. If such a hit were enqueued from
    /// <see cref="ProcChainPath.Root"/>, like a hit from a basic attack, the chain guard would
    /// see an unrelated first-generation event every time and its cycle rule would never fire:
    /// chain lightning would feed itself indefinitely, bounded only by an internal cooldown and
    /// the drain budget. docs/03 section 2 names this hazard directly - 효과 재진입 경로 추적,
    /// 원형 참조 금지 - and docs/02 section 6 forbids the silent rule loss it produces.
    /// </para>
    /// <para>
    /// So while an event resolves, this router treats that event's child path as the current
    /// ancestry, and any hit arriving in that window inherits it. The guard then judges the
    /// chain that actually happened.
    /// </para>
    /// <para>
    /// Pure: no Unity types and no scene. What an effect does to the world is the caller's job;
    /// deciding whether it may happen at all is this one's.
    /// </para>
    /// </summary>
    public sealed class ProcTriggerRouter
    {
        private readonly PluginLoadout _loadout;
        private readonly ProcChainGuard _guard;
        private readonly ProcQueue _queue;
        private readonly List<PluginDefinition> _triggered = new List<PluginDefinition>();

        private ProcChainPath _activePath = ProcChainPath.Root;

        public ProcTriggerRouter(PluginLoadout loadout, ProcChainGuard guard, ProcQueue queue)
        {
            _loadout = loadout ?? throw new ArgumentNullException(nameof(loadout));
            _guard = guard ?? throw new ArgumentNullException(nameof(guard));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        }

        /// <summary>Ancestry a hit arriving right now would inherit. Root outside a drain.</summary>
        public ProcChainPath ActivePath => _activePath;

        public PluginLoadout Loadout => _loadout;

        public int PendingCount => _queue.PendingCount;

        /// <summary>
        /// Takes a plugin into the loadout and registers its chain rules with the guard. The
        /// two happen together because a plugin whose rule never reached the guard would be
        /// judged by the guard's default instead of by its own data.
        /// </summary>
        public PluginGrantVerdict TryGrant(in PluginDefinition definition, out string reason) =>
            TryGrant(definition, null, out reason);

        /// <summary>
        /// As above, drawing anything the plugin leaves to chance from the reward stream.
        /// <para>
        /// A passive registers no chain rule: it never reaches the queue, so giving the guard
        /// an entry for it would only invite a future effect id collision.
        /// </para>
        /// </summary>
        public PluginGrantVerdict TryGrant(
            in PluginDefinition definition, DeterministicRng rewardRng, out string reason)
        {
            PluginGrantVerdict verdict = _loadout.TryGrant(definition, rewardRng, out reason);
            if (verdict == PluginGrantVerdict.Granted && definition.Effect != PluginEffectKind.Passive)
                _guard.Register(definition.Rule);

            return verdict;
        }

        /// <summary>
        /// Offers a landed hit to every plugin it triggers. Decisions are the queue's; this
        /// only supplies the ancestry and the order.
        /// </summary>
        public void OnHitLanded(in HitReport hit, long tick, int runId = 0, int attackId = 0)
        {
            _triggered.Clear();
            _loadout.CollectTriggered(hit, _triggered);

            for (int i = 0; i < _triggered.Count; ++i)
            {
                PluginDefinition plugin = _triggered[i];
                _queue.TryEnqueue(
                    plugin.EffectId,
                    hit.SourceActorId,
                    hit.TargetActorId,
                    _activePath,
                    tick,
                    runId,
                    attackId,
                    plugin.Damage.Type,
                    plugin.Damage.IsArea,
                    plugin.Damage.IsPeriodic,
                    hit.IsCritical);
            }
        }

        /// <summary>
        /// Resolves everything queued, handing each event the plugin that produced it. The
        /// resolver may deal damage, which may land, which may trigger more plugins - and those
        /// arrive with this event's ancestry attached rather than as fresh chains.
        /// </summary>
        public ProcDrainReport Drain(Action<ProcEvent, PluginDefinition> resolve)
        {
            if (resolve == null)
                throw new ArgumentNullException(nameof(resolve));

            return _queue.Drain((evt, _) =>
            {
                if (!TryFindByEffectId(evt.EffectId, out PluginDefinition plugin))
                    return;

                ProcChainPath previous = _activePath;
                _activePath = evt.ChildPath;
                try
                {
                    resolve(evt, plugin);
                }
                finally
                {
                    // Restored even if the resolver throws, or one failing effect would leave
                    // every later hit in the tick inheriting a chain it had nothing to do with.
                    _activePath = previous;
                }
            });
        }

        private bool TryFindByEffectId(int effectId, out PluginDefinition definition)
        {
            for (int i = 0; i < _loadout.Count; ++i)
            {
                if (_loadout.At(i).EffectId != effectId)
                    continue;

                definition = _loadout.At(i);
                return true;
            }

            definition = default;
            return false;
        }
    }
}
