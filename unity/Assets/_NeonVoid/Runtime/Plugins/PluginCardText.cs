using System;
using System.Collections.Generic;

namespace NeonVoid.Plugins
{
    /// <summary>What one card says on a 보상 화면.</summary>
    public readonly struct PluginCardText
    {
        internal PluginCardText(string name, string effect)
        {
            Name = name;
            Effect = effect;
        }

        /// <summary>[초안] docs/03's 임시 이름. The document calls them provisional itself.</summary>
        public string Name { get; }

        /// <summary>One line of what it does, in the document's own words.</summary>
        public string Effect { get; }

        public bool IsKnown => !string.IsNullOrEmpty(Name);

        public override string ToString() => IsKnown ? $"{Name} — {Effect}" : "(이름 없음)";
    }

    /// <summary>
    /// The names and effects the player reads, transcribed from docs/03 sections 2's tables.
    /// <para>
    /// <b>Why a table and not a field on the card.</b> <see cref="PluginDefinition"/> is what
    /// combat resolves against - rarity, trigger, numbers - and it is built in code by
    /// <see cref="PluginCatalog"/>. Text is what a screen reads, it changes for reasons that
    /// have nothing to do with combat, and [초안] docs/03 says the names are provisional. A
    /// string beside the damage numbers would make every rename a change to a combat type.
    /// </para>
    /// <para>
    /// <b>An unknown id reads as its id rather than as nothing.</b> A card with no entry here
    /// still has to be pickable; showing a blank where a name goes is how a reward screen
    /// looks broken.
    /// </para>
    /// </summary>
    public static class PluginCardTexts
    {
        private static readonly Dictionary<string, PluginCardText> Texts =
            new Dictionary<string, PluginCardText>(StringComparer.Ordinal)
            {
                // --- 일반 (17) ----------------------------------------------------------------
                { "G01", new PluginCardText("랜덤 패치", "무작위 능력치 3개 증가") },
                { "G02", new PluginCardText("경량화", "이동속도 증가") },
                { "G03", new PluginCardText("확장 탄두", "탄환 크기 증가") },
                { "G04", new PluginCardText("취약점 분석", "물리·마법 비율 관통 증가") },
                { "G05", new PluginCardText("주기 차폐막", "30초마다 보호막 생성") },
                { "G06", new PluginCardText("스로틀 증폭", "공격속도 + 탄환속도 증가") },
                { "G07", new PluginCardText("실행 차폐", "스킬 사용 시 보호막 생성") },
                { "G08", new PluginCardText("이동 버퍼", "돌진·점멸 뒤 2초 이동속도 증가") },
                { "G09", new PluginCardText("비상 복구", "체력 30% 이하에서 최대 체력 50% 회복 + 이동속도 (CD 60초)") },
                { "G10", new PluginCardText("전위 연쇄", "탄환 적중 시 근처 적에게 번개 (대상별 CD 3초)") },
                { "G11", new PluginCardText("무피격 가속", "이동속도 100% 증가, 피해를 받으면 비활성화") },
                { "G12", new PluginCardText("부식 표식", "타격 시 물리·마법 방어 감소 중첩 4초") },
                { "G13", new PluginCardText("4중 캐시", "적중 효과 4회 후 탄환 4발 추가 연사 (추가탄 70%)") },
                { "G14", new PluginCardText("추적 응답", "스킬 피해 적중 시 자동발사 탄환 1개") },
                { "G15", new PluginCardText("희귀 획득", "무작위 레어 플러그인 1개를 즉시 획득") },
                { "G16", new PluginCardText("대시 폭발", "대시 경로 폭발 피해") },
                { "G17", new PluginCardText("폭발 각인", "탄환 적중 시 대상 폭발 (대상별 CD 3초)") },

                // --- 레어 (7) -----------------------------------------------------------------
                { "R01", new PluginCardText("처형 알고리즘", "체력 50% 미만 적에게 피해 15% 증가") },
                { "R02", new PluginCardText("킬 스택", "적 처치 시 AD 증가, 최대 20스택 (의뢰 종료 시 소멸)") },
                { "R03", new PluginCardText("위기 반발", "HP 30% 이하일 때 주변 적 넉백") },
                { "R04", new PluginCardText("해체 프로토콜", "적중 효과마다 상한 없는 스택의 지속 마법 피해") },
                { "R05", new PluginCardText("돌진 차폐", "돌진 또는 점멸 뒤 보호막 생성") },
                { "R06", new PluginCardText("보조 탄환", "탄환 1개 추가 (추가탄 적중 효과 80%)") },
                { "R07", new PluginCardText("자동 추적탄", "유도탄 발사") },

                // --- 전설 (3) -----------------------------------------------------------------
                { "L01", new PluginCardText("레이저 코어", "탄환을 레이저로 변환, 적중 효과 70%") },
                { "L02", new PluginCardText(
                    "부메랑 프로토콜", "부메랑 범위 피해 + 귀환 (귀환 전 추가 기본공격 불가)") },
                { "L03", new PluginCardText("피버타임", "게이지 충전 후 일정시간 전체 능력치 증가") },
            };

        /// <summary>The text for a card, or its id when nothing has been written for it.</summary>
        public static PluginCardText For(string proposalId)
        {
            if (string.IsNullOrEmpty(proposalId))
                return default;

            return Texts.TryGetValue(proposalId, out PluginCardText text)
                ? text
                : new PluginCardText(proposalId, string.Empty);
        }

        /// <summary>What the 보상 화면 calls each grade.</summary>
        public static string LabelOf(PluginRarity rarity)
        {
            switch (rarity)
            {
                case PluginRarity.Common: return "일반";
                case PluginRarity.Rare: return "레어";
                case PluginRarity.Legendary: return "전설";
                default: return "신화";
            }
        }

        /// <summary>How many cards this table has words for. Held by a test against docs/03.</summary>
        public static int Count => Texts.Count;

        /// <summary>Whether a card has words of its own rather than falling back to its id.</summary>
        public static bool Knows(string proposalId) =>
            !string.IsNullOrEmpty(proposalId) && Texts.ContainsKey(proposalId);
    }
}
