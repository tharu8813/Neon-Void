using System;
using System.Collections.Generic;
using NeonVoid.Core;
using NeonVoid.Plugins;

namespace NeonVoid.Missions
{
    /// <summary>One card the player may keep on a successful 철수.</summary>
    public readonly struct LearnCandidate
    {
        public LearnCandidate(string proposalId, string documentKey, PluginRarity rarity, int level = 1)
        {
            ProposalId = proposalId ?? string.Empty;
            DocumentKey = documentKey ?? string.Empty;
            Rarity = rarity;
            Level = level < PluginLevels.FirstLevel ? PluginLevels.FirstLevel : level;
        }

        /// <summary>
        /// The level it is carried at. [확정] docs/04 section 3.1: 학습할 때 레벨을 유지한다 -
        /// a card raised to Lv.3 in the run is archived at Lv.3, not reset.
        /// </summary>
        public int Level { get; }

        /// <summary>docs/03's development id, for text and for a log.</summary>
        public string ProposalId { get; }

        /// <summary>The stable key an archived instance is written under.</summary>
        public string DocumentKey { get; }

        public PluginRarity Rarity { get; }

        public bool IsEmpty => DocumentKey.Length == 0;

        public override string ToString() => $"{ProposalId} ({Rarity}) {DocumentKey}";
    }

    /// <summary>
    /// What a 철수 may keep, and which of it the player has chosen.
    /// <para>
    /// [확정] rules this applies literally:
    /// </para>
    /// <list type="bullet">
    /// <item>일반·레어 플러그인을 성공 철수 후 <b>0~2개</b> 새로 학습 (docs/04 section 3.1, R-015).
    /// Zero is a complete answer, not a failure.</item>
    /// <item>전설·신화는 <b>학습 불가</b>, 그리고 <i>성공 철수 학습 카드에 절대 노출되지 않음</i>
    /// (docs/03 section 4, docs/04 section 3.2). They are filtered out of the candidates rather
    /// than offered and refused, because docs/03 says not to show them at all.</item>
    /// <item>학습할 때 레벨을 유지한다 (section 3.1). Plugin levels do not exist yet (docs/03
    /// section 3 item 6), so there is no level to keep and nothing is invented for one.</item>
    /// </list>
    /// <para>
    /// <b>What it deliberately does not decide.</b> [미확정] D-002: whether a 학습 플러그인 the
    /// vaccine carried INTO the mission may be re-archived, and how that interacts with the limit
    /// of two. Only cards picked up in this run are offered, so that question is not answered
    /// here by accident - and <see cref="CarriedInFromBefore"/> counts what is being left out,
    /// so a screen can say so rather than silently showing a shorter list.
    /// </para>
    /// <para>
    /// Pure C#: the whole rule is testable without a scene, which is the point. The selection
    /// decides what a permanent archive write contains.
    /// </para>
    /// </summary>
    public sealed class ExtractionLearning
    {
        /// <summary>[확정] R-015 / docs/04 section 3.1.</summary>
        public const int MaxLearned = DesignConstants.LearnablePluginsPerSuccessfulRun;

        private readonly List<LearnCandidate> _candidates = new List<LearnCandidate>();
        private readonly List<string> _chosenKeys = new List<string>();
        private readonly Func<string> _unique;

        /// <param name="unique">
        /// Where the unique half of an instance id comes from. Injected so a test is
        /// deterministic; the game passes a GUID.
        /// </param>
        public ExtractionLearning(Func<string> unique = null)
        {
            _unique = unique ?? (() => Guid.NewGuid().ToString("N"));
        }

        /// <summary>What may be kept, in the order it was picked up.</summary>
        public IReadOnlyList<LearnCandidate> Candidates => _candidates;

        /// <summary>How many are chosen right now.</summary>
        public int ChosenCount => _chosenKeys.Count;

        /// <summary>Whether another can still be chosen.</summary>
        public bool HasRoom => _chosenKeys.Count < MaxLearned;

        /// <summary>
        /// 전설·신화 the vaccine is carrying, which are never offered. Counted so a screen can
        /// say <i>전설은 학습할 수 없습니다</i> instead of just not listing them.
        /// </summary>
        public int UnlearnableCarried { get; private set; }

        /// <summary>
        /// Cards that were already learned before this 의뢰 and so are not new. [미확정] D-002
        /// covers what may be done with these; nothing here does anything with them.
        /// </summary>
        public int CarriedInFromBefore { get; private set; }

        /// <summary>Why the last change was refused, or empty.</summary>
        public string LastRefusal { get; private set; } = string.Empty;

        /// <summary>
        /// Reads the candidates off what the vaccine is carrying. Clears any previous selection:
        /// a choice made against a different list is not a choice.
        /// </summary>
        /// <param name="learnedBefore">
        /// Document keys the vaccine already had learned when the 의뢰 began. Those cards are not
        /// new, so they are not offered.
        /// </param>
        public void Offer(PluginLoadout loadout, IReadOnlyCollection<string> learnedBefore = null)
        {
            _candidates.Clear();
            _chosenKeys.Clear();
            UnlearnableCarried = 0;
            CarriedInFromBefore = 0;
            LastRefusal = string.Empty;

            if (loadout == null)
                return;

            IReadOnlyList<PluginDefinition> carried = loadout.Carried;
            for (int i = 0; i < carried.Count; ++i)
            {
                PluginDefinition plugin = carried[i];

                if (!IsLearnable(plugin.Rarity))
                {
                    UnlearnableCarried++;
                    continue;
                }

                string key = PluginDocumentKeys.KeyFor(plugin.ProposalId);
                if (key.Length == 0)
                {
                    // A card with no stable key cannot be written into an archive at all. It is
                    // left out rather than offered: a candidate that could not be kept would be
                    // a choice that quietly does nothing.
                    continue;
                }

                if (Holds(learnedBefore, key))
                {
                    CarriedInFromBefore++;
                    continue;
                }

                _candidates.Add(new LearnCandidate(
                    plugin.ProposalId, key, plugin.Rarity,
                    PluginLevels.Clamp(plugin.ProposalId, plugin.Level)));
            }
        }

        /// <summary>
        /// [확정] docs/04 section 3.2 and docs/03 section 4: only 일반 and 레어 may ever be
        /// learned. Written once, here, so no screen has its own copy of the rule.
        /// </summary>
        public static bool IsLearnable(PluginRarity rarity) =>
            rarity == PluginRarity.Common || rarity == PluginRarity.Rare;

        /// <summary>Whether that card is chosen.</summary>
        public bool IsChosen(string documentKey) =>
            !string.IsNullOrEmpty(documentKey) && _chosenKeys.Contains(documentKey);

        /// <summary>
        /// Chooses or unchooses a card. Returns whether the selection now holds it.
        /// <para>
        /// A full selection refuses rather than dropping the oldest choice: trimming would pick
        /// which of the player's cards to lose, silently - the same reason
        /// <see cref="Progression.Save.ExtractRunCommand"/> refuses more than two instead of
        /// taking the first two.
        /// </para>
        /// </summary>
        public bool Toggle(string documentKey)
        {
            LastRefusal = string.Empty;

            if (string.IsNullOrEmpty(documentKey))
                return false;

            if (_chosenKeys.Remove(documentKey))
                return false;

            if (!Offers(documentKey))
            {
                LastRefusal = "이 의뢰에서 얻은 카드가 아닙니다";
                return false;
            }

            if (!HasRoom)
            {
                LastRefusal = $"학습은 최대 {MaxLearned}개입니다";
                return false;
            }

            _chosenKeys.Add(documentKey);
            return true;
        }

        /// <summary>Unchooses everything. 0개 학습 is a complete, allowed answer.</summary>
        public void ChooseNothing()
        {
            _chosenKeys.Clear();
            LastRefusal = string.Empty;
        }

        /// <summary>
        /// The instance ids to hand to 철수, minted now.
        /// <para>
        /// Minted at commit rather than when a card is ticked, so a card ticked, unticked and
        /// ticked again is one instance and not three abandoned ids. Each carries its document
        /// key, so the archive can say what it holds.
        /// </para>
        /// </summary>
        public string[] MintChosenInstanceIds()
        {
            var ids = new string[_chosenKeys.Count];
            for (int i = 0; i < _chosenKeys.Count; ++i)
                ids[i] = PluginInstanceId.Mint(_chosenKeys[i], _unique(), LevelOf(_chosenKeys[i])).Value;

            return ids;
        }

        /// <summary>The chosen cards' keys, in the order they were ticked.</summary>
        public IReadOnlyList<string> ChosenKeys => _chosenKeys;

        /// <summary>
        /// Written out rather than using Contains: the parameter is a read-only collection so a
        /// caller can pass whatever it already has, and that interface has no Contains of its own.
        /// </summary>
        private static bool Holds(IReadOnlyCollection<string> keys, string key)
        {
            if (keys == null)
                return false;

            foreach (string held in keys)
            {
                if (string.Equals(held, key, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>The level the chosen card is carried at, so the archive keeps it.</summary>
        private int LevelOf(string documentKey)
        {
            for (int i = 0; i < _candidates.Count; ++i)
            {
                if (_candidates[i].DocumentKey == documentKey)
                    return _candidates[i].Level;
            }

            return PluginLevels.FirstLevel;
        }

        private bool Offers(string documentKey)
        {
            for (int i = 0; i < _candidates.Count; ++i)
            {
                if (_candidates[i].DocumentKey == documentKey)
                    return true;
            }

            return false;
        }
    }
}
