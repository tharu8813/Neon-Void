using System;
using System.Collections.Generic;
using NeonVoid.Core;

namespace NeonVoid.Plugins
{
    /// <summary>How a pick or a reroll ended.</summary>
    public enum RewardVerdict
    {
        /// <summary>Taken.</summary>
        Accepted = 0,

        /// <summary>No screen is open.</summary>
        NoScreenOpen = 1,

        /// <summary>That slot is not on the screen.</summary>
        NoSuchSlot = 2,

        /// <summary>The slot is empty, because the pool had nothing left to offer.</summary>
        EmptySlot = 3,

        /// <summary>No rerolls left. [확정] docs/03 section 3 item 4: 의뢰당 기본 3회.</summary>
        NoRerollsLeft = 4,

        /// <summary>The loadout refused the card, and its reason is carried with this.</summary>
        LoadoutRefused = 5,
    }

    /// <summary>The answer to a pick or a reroll, with the reason when there is one.</summary>
    public readonly struct RewardResult
    {
        private RewardResult(RewardVerdict verdict, string detail, RewardCard card)
        {
            Verdict = verdict;
            Detail = detail ?? string.Empty;
            Card = card;
        }

        public RewardVerdict Verdict { get; }
        public string Detail { get; }

        /// <summary>The card the player took, on an accepted pick.</summary>
        public RewardCard Card { get; }

        public bool Ok => Verdict == RewardVerdict.Accepted;

        internal static RewardResult Accepted(in RewardCard card) =>
            new RewardResult(RewardVerdict.Accepted, string.Empty, card);

        internal static RewardResult Refused(RewardVerdict verdict, string detail) =>
            new RewardResult(verdict, detail, default);

        public override string ToString() => Ok ? $"{Card}" : $"{Verdict}: {Detail}";
    }

    /// <summary>
    /// One mission's reward screens: when one is open, what is on it, and what the three
    /// rerolls have left.
    /// <para>
    /// <b>Nothing here writes to the save.</b> [확정] docs/04 section 1 puts 임시 플러그인 in
    /// 의뢰 상태, and section 3 item 5 says 사망 시 출전 소지분은 소실 - a card taken mid-mission
    /// is not permanent and does not become permanent until 철수 commits it. So picking grants
    /// to the loadout and nothing else; what docs/04 section 7 forbids showing before a commit
    /// is the <i>extraction's</i> result, and that is <see cref="Missions.MissionFlow"/>'s to
    /// refuse.
    /// </para>
    /// <para>
    /// <b>The rerolls belong to the mission.</b> [확정] docs/03 section 3 item 4: 의뢰당 기본
    /// 3회. They refill when a mission begins, not when a screen opens, so a player who
    /// purifies three areas spends the same three across all of them.
    /// </para>
    /// </summary>
    public sealed class RewardSession
    {
        private readonly RewardRoller _roller;
        private readonly Func<PluginLoadout> _loadout;
        private readonly List<string> _owned = new List<string>();

        private RewardScreen _screen;
        private long _runId;
        private int _screenId;
        private int _rerollsSpentOnThisScreen;
        private RewardConditions _conditions;
        private uint _seed;

        /// <summary>
        /// Takes a function rather than a loadout.
        /// <para>
        /// A held reference was wrong twice over: a component built before the one that owns
        /// the loadout captured null and then silently granted nothing - the screen closed, the
        /// player saw the card go, and the loadout never changed - and a new vaccine replaces
        /// the loadout entirely. Asking each time cannot go stale.
        /// </para>
        /// </summary>
        public RewardSession(RewardRoller roller, Func<PluginLoadout> loadout)
        {
            _roller = roller ?? throw new ArgumentNullException(nameof(roller));
            _loadout = loadout;
            RerollsLeft = DesignConstants.InitialRewardRerolls;
        }

        /// <summary>A session over a loadout that will not be replaced. For a test.</summary>
        public RewardSession(RewardRoller roller, PluginLoadout loadout)
            : this(roller, () => loadout)
        {
        }

        /// <summary>The roller the game uses, over a vaccine's loadout.</summary>
        public static RewardSession Approved(Func<PluginLoadout> loadout) =>
            new RewardSession(RewardRoller.Approved(), loadout);

        /// <summary>The screen on show, or null when none is open.</summary>
        public RewardScreen Screen => _screen;

        public bool IsOpen => _screen != null;

        /// <summary>[확정] docs/03 section 3 item 4: 의뢰당 기본 리롤 3회.</summary>
        public int RerollsLeft { get; private set; }

        /// <summary>How many screens this mission has shown.</summary>
        public int ScreensThisMission { get; private set; }

        /// <summary>The last refusal, for a screen that wants to say why.</summary>
        public string LastRefusal { get; private set; } = string.Empty;

        /// <summary>
        /// A mission began: the rerolls refill and nothing is on screen.
        /// <para>
        /// The pity count is deliberately <b>not</b> reset - [확정] docs/03 section 3 item 5
        /// counts 보상 화면 and says nothing about mission boundaries, so a player who ends a
        /// mission in a drought does not have it forgiven for starting another.
        /// </para>
        /// </summary>
        public void BeginMission()
        {
            RerollsLeft = DesignConstants.InitialRewardRerolls;
            ScreensThisMission = 0;
            _screen = null;
            LastRefusal = string.Empty;
        }

        /// <summary>
        /// Opens a screen. Called when an area is purified, which is what docs/01 ties a reward
        /// to. Replaces whatever was open: a player cannot be holding two screens.
        /// </summary>
        public RewardScreen Open(long runId, in RewardConditions conditions, uint sessionSeed)
        {
            _runId = runId;
            _screenId = ScreensThisMission;
            _conditions = conditions;
            _seed = sessionSeed;
            _rerollsSpentOnThisScreen = 0;

            RefreshOwned();
            _screen = _roller.Deal(runId, _screenId, conditions, sessionSeed, _owned);
            ++ScreensThisMission;
            LastRefusal = string.Empty;

            return _screen;
        }

        /// <summary>
        /// Takes one card and closes the screen. [확정] docs/03 section 3: one of the three.
        /// </summary>
        public RewardResult Pick(int slot)
        {
            if (_screen == null)
                return Refuse(RewardVerdict.NoScreenOpen, "열려 있는 보상 화면이 없습니다");

            if (slot < 0 || slot >= _screen.Cards.Count)
                return Refuse(RewardVerdict.NoSuchSlot, $"슬롯 {slot}은 이 화면에 없습니다");

            RewardCard card = _screen.Cards[slot];
            if (card.IsEmpty)
                return Refuse(RewardVerdict.EmptySlot, "이 자리에는 줄 수 있는 플러그인이 없습니다");

            PluginLoadout loadout = Loadout;
            if (loadout == null)
            {
                // Said rather than swallowed. A screen that closed and granted nothing is the
                // worst outcome here: the player watched the reward go and got nothing.
                return Refuse(
                    RewardVerdict.LoadoutRefused,
                    "플러그인을 받을 소지 목록이 없습니다 (PluginProcDirector 미연결)");
            }

            PluginGrantVerdict granted = loadout.TryGrant(card.Definition, out string why);
            if (granted != PluginGrantVerdict.Granted)
                return Refuse(RewardVerdict.LoadoutRefused, why);

            // Only now. A card the loadout refused must not take the screen with it, or the
            // player loses the reward and the other two choices at once.
            _screen = null;
            LastRefusal = string.Empty;
            return RewardResult.Accepted(card);
        }

        /// <summary>
        /// Rerolls one slot. [확정] docs/03 section 3 item 3: 선택한 한 슬롯만, 나머지 2장 유지.
        /// </summary>
        public RewardResult Reroll(int slot)
        {
            if (_screen == null)
                return Refuse(RewardVerdict.NoScreenOpen, "열려 있는 보상 화면이 없습니다");

            if (slot < 0 || slot >= _screen.Cards.Count)
                return Refuse(RewardVerdict.NoSuchSlot, $"슬롯 {slot}은 이 화면에 없습니다");

            if (RerollsLeft <= 0)
                return Refuse(RewardVerdict.NoRerollsLeft, "이번 의뢰의 리롤을 모두 썼습니다");

            ++_rerollsSpentOnThisScreen;
            --RerollsLeft;

            RefreshOwned();
            _roller.Reroll(
                _screen, slot, _runId, _screenId, _rerollsSpentOnThisScreen, _conditions, _seed, _owned);

            LastRefusal = string.Empty;
            return RewardResult.Accepted(_screen.Cards[slot]);
        }

        /// <summary>
        /// Closes the screen without taking anything. Separate from <see cref="Pick"/> because
        /// walking away is a choice a player may want and 보상 화면 닫기 정책 is [미확정] - this
        /// is the behaviour that loses nothing the document has decided.
        /// </summary>
        public void Dismiss()
        {
            _screen = null;
            LastRefusal = string.Empty;
        }

        /// <summary>The loadout right now, or null when nothing owns one.</summary>
        public PluginLoadout Loadout => _loadout?.Invoke();

        private void RefreshOwned()
        {
            _owned.Clear();
            PluginLoadout loadout = Loadout;
            if (loadout == null)
                return;

            IReadOnlyList<PluginDefinition> carried = loadout.Carried;
            for (int i = 0; i < carried.Count; ++i)
                _owned.Add(carried[i].ProposalId);
        }

        private RewardResult Refuse(RewardVerdict verdict, string detail)
        {
            LastRefusal = detail;
            return RewardResult.Refused(verdict, detail);
        }

        public override string ToString() =>
            IsOpen
                ? $"보상 화면 {_screenId} · 리롤 {RerollsLeft} · {_screen}"
                : $"닫힘 · 리롤 {RerollsLeft} · 이번 의뢰 {ScreensThisMission}회";
    }
}
