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
        private float _activeScale = 1f;

        public ProcTriggerRouter(PluginLoadout loadout, ProcChainGuard guard, ProcQueue queue)
        {
            _loadout = loadout ?? throw new ArgumentNullException(nameof(loadout));
            _guard = guard ?? throw new ArgumentNullException(nameof(guard));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        }

        /// <summary>Ancestry a hit arriving right now would inherit. Root outside a drain.</summary>
        public ProcChainPath ActivePath => _activePath;

        /// <summary>
        /// Damage fraction a hit arriving right now would inherit. 1 outside a drain.
        /// <para>
        /// <b>It is inherited, not compounded.</b> A 70% burst shot that triggers 전위 연쇄,
        /// whose damage lands and triggers 폭발 각인, gives the explosion 70% and not 49%.
        /// Everything in that chain is an effect of one burst shot, so the document's one
        /// number applies once; compounding would make the damage depend on chain depth, which
        /// docs/03 never mentions and nobody could predict from a card.
        /// </para>
        /// </summary>
        public float ActiveOnHitEffectScale => _activeScale;

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
            if (verdict == PluginGrantVerdict.Granted && definition.ReachesProcQueue)
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

            // Outside a drain _activeScale is 1, so a real shot contributes its own fraction.
            // Inside one it is the resolving event's, and a plugin effect's own hit carries no
            // fraction of its own - so the product is the chain's single inherited scale rather
            // than a power of it.
            float scale = _activeScale * hit.OnHitEffectScale;

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
                    hit.IsCritical,
                    randomStreamId: 0,
                    onHitEffectScale: scale);
            }
        }

        /// <summary>
        /// Offers an effect's own resolution to every plugin that reacts to one. docs/03 R04
        /// 해체 프로토콜 is 적중 효과 발생 때마다, which is this and not a hit.
        /// <para>
        /// Called from inside a drain, so the ancestry it enqueues under is the resolving
        /// event's own child path. That is what makes the cycle rule able to refuse R04
        /// stacking off its own resolution: the queue sees effect 4 asking to happen inside a
        /// chain that already contains effect 4, which is the one thing docs/03 section 2
        /// forbids outright (원형 참조 금지).
        /// </para>
        /// </summary>
        public void OnHitEffectResolved(
            int sourceActorId, int targetActorId, long tick, int runId = 0, int attackId = 0)
        {
            _triggered.Clear();
            _loadout.CollectTriggeredByEffectResolution(_triggered);

            for (int i = 0; i < _triggered.Count; ++i)
            {
                PluginDefinition plugin = _triggered[i];
                _queue.TryEnqueue(
                    plugin.EffectId,
                    sourceActorId,
                    targetActorId,
                    _activePath,
                    tick,
                    runId,
                    attackId,
                    plugin.Damage.Type,
                    plugin.Damage.IsArea,
                    plugin.Damage.IsPeriodic,
                    isCritical: false,
                    randomStreamId: 0,
                    onHitEffectScale: _activeScale);
            }
        }

        /// <summary>
        /// Offers one enemy a dash has touched to every plugin that reacts to a dash. docs/03
        /// G16 대시 폭발 is 대시 경로 폭발 피해, and this is the contact, not a hit.
        /// <para>
        /// Called once per enemy per dash by whatever is sweeping the path; the "once" is that
        /// caller's job, because it is the only thing that knows which dash this is. docs/03
        /// names the hazard directly: 한 대시에서 중복 접촉.
        /// </para>
        /// </summary>
        public void OnDashContact(
            int sourceActorId, int targetActorId, long tick, int runId = 0, int attackId = 0)
        {
            _triggered.Clear();
            _loadout.CollectTriggeredByDashContact(_triggered);

            for (int i = 0; i < _triggered.Count; ++i)
            {
                PluginDefinition plugin = _triggered[i];
                _queue.TryEnqueue(
                    plugin.EffectId,
                    sourceActorId,
                    targetActorId,
                    _activePath,
                    tick,
                    runId,
                    attackId,
                    plugin.Damage.Type,
                    plugin.Damage.IsArea,
                    plugin.Damage.IsPeriodic,
                    isCritical: false,
                    randomStreamId: 0,
                    onHitEffectScale: _activeScale);
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
                float previousScale = _activeScale;
                _activePath = evt.ChildPath;
                _activeScale = evt.OnHitEffectScale;
                try
                {
                    resolve(evt, plugin);

                    // 적중 효과 발생: offered here, after the effect has actually resolved, and
                    // never for a reactor's own resolution. Doing it inside the drain rather
                    // than asking the caller to is what makes the second half structural - a
                    // caller that forgot the gate would let R04 feed itself with no hit
                    // involved, which is the loop docs/03 section 2 forbids outright.
                    if (plugin.Trigger != PluginTriggerKind.OnHitEffectResolved)
                    {
                        OnHitEffectResolved(
                            evt.SourceActorId, evt.TargetActorId, evt.Tick, evt.RunId, evt.AttackId);
                    }
                }
                finally
                {
                    // Restored even if the resolver throws, or one failing effect would leave
                    // every later hit in the tick inheriting a chain - and a damage fraction -
                    // it had nothing to do with.
                    _activePath = previous;
                    _activeScale = previousScale;
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
