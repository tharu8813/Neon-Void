using System;

namespace NeonVoid.Missions
{
    /// <summary>
    /// What a 소목표 asks the player to do. [초안] - docs/01 section 2 names the objectives but
    /// not their mechanics, and D-015 owns the table.
    /// </summary>
    public enum AreaObjectiveKind
    {
        /// <summary>Destroy a device. The only shape the game had.</summary>
        Destroy = 0,

        /// <summary>Stand at a point and hold it. Reuses <see cref="DwellGate"/>.</summary>
        Hold = 1,

        /// <summary>
        /// Hold a point, but only when it is this node's turn: the area's Sequence nodes make one
        /// chain that has to be walked in order.
        /// </summary>
        Sequence = 2,
    }

    /// <summary>One objective: what to do, what it is called, and where it sits in a chain.</summary>
    public readonly struct AreaObjectiveSpec
    {
        public readonly AreaObjectiveKind Kind;

        /// <summary>[초안] docs/01 section 2's own name for this objective. Display and logs.</summary>
        public readonly string Name;

        /// <summary>Position in the area's chain, or -1 for anything that is not a chain node.</summary>
        public readonly int ChainIndex;

        public AreaObjectiveSpec(AreaObjectiveKind kind, string name, int chainIndex = -1)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("An objective needs a name the HUD can show.", nameof(name));

            if (kind == AreaObjectiveKind.Sequence && chainIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(chainIndex), chainIndex, "A chain node needs its place.");

            if (kind != AreaObjectiveKind.Sequence && chainIndex >= 0)
                throw new ArgumentOutOfRangeException(nameof(chainIndex), chainIndex, "Only a chain node has a place.");

            Kind = kind;
            Name = name;
            ChainIndex = chainIndex;
        }

        /// <summary>Whether this objective is reached by standing at it rather than shooting it.</summary>
        public bool IsDwell => Kind != AreaObjectiveKind.Destroy;

        public override string ToString() =>
            ChainIndex >= 0 ? $"{Name} ({Kind} {ChainIndex + 1})" : $"{Name} ({Kind})";
    }

    /// <summary>
    /// [초안] each hardware's 목표 후보 3종, from docs/01 section 2's table, with the shape each
    /// one actually takes.
    /// <para>
    /// The point of the table is the 핵심 동사 column. CLAUDE.md forbids treating the five areas
    /// as one copy-pasted map, and until now every area's objectives were CPU's 과열 밸브 - three
    /// identical destructibles, renamed nowhere. An area whose objectives are all "shoot the
    /// cube" has no verb of its own no matter what its enemies are called.
    /// </para>
    /// <para>
    /// Only three shapes exist, and each is here because it needs nothing the game has not got:
    /// a destructible, a dwell, and a dwell that cares what order it happens in. The objectives
    /// docs/01 names that are <b>not</b> here are listed by <see cref="Deferred"/> with what each
    /// one is waiting for - a 방어 objective needs enemies that attack a device and a failure
    /// state the flow does not have, and RAM's 진위 구분 needs decoys that pay no XP, which
    /// touches the 소환체 XP 중복 제한 docs/04 leaves open.
    /// </para>
    /// </summary>
    public static class AreaObjectives
    {
        /// <summary>[초안] how close counts as standing at an objective. The console's radius.</summary>
        public const float DraftHoldRadiusUnits = 2f;

        /// <summary>[초안] ticks held at a plain Hold objective. 90 is 1.5 seconds.</summary>
        public const int DraftHoldTicks = 90;

        /// <summary>
        /// [초안] ticks held at HDD's objectives. Longer because HDD's 핵심 동사 is 위치 관리·장기전
        /// and docs/01 asks that a long area not be a big HP bar - holding ground for longer is
        /// the part of 장기전 that is about the player's position rather than the enemy's health.
        /// </summary>
        public const int DraftLongHoldTicks = 150;

        /// <summary>[초안] ticks held at one chain node. Short: the walk between them is the task.</summary>
        public const int DraftChainNodeTicks = 36;

        /// <summary>
        /// [초안] ticks SSD allows for the whole chain once it is started. docs/01 section 2:
        /// 레일을 짧은 시간에 연결. 600 is ten seconds.
        /// </summary>
        public const int DraftSsdChainWindowTicks = 600;

        /// <summary>A chain with no window. GPU's chain is about reading the space, not speed.</summary>
        public const int NoChainWindow = 0;

        /// <summary>
        /// The objectives docs/01 names that are deliberately not built, and why. Text, because
        /// its only consumers are a log line and a test that keeps this list honest.
        /// </summary>
        public static string Deferred =>
            "CPU 연산 코어 방어 / HDD 회전축 방어 as true 방어: enemies that attack a device and an " +
            "objective that can fail, neither of which the flow has. " +
            "RAM 가짜 인스턴스 정리 as 진위 구분: decoys that pay no kill XP, which turns on the " +
            "소환체 XP 중복 제한 docs/04 leaves open. " +
            "GPU 광선 스위치 연결 as 경로 바꿈: rerouting a hazard needs hazards the area does not place.";

        /// <summary>
        /// [초안] the three candidates for a hardware, in the order an area takes them.
        /// <para>
        /// The order matters: an area takes the first <c>count</c> of these, and a chain of one
        /// node is just a Hold with extra words, so every hardware's chain nodes come first.
        /// <see cref="ChainLengthFor"/> and a test hold that.
        /// </para>
        /// </summary>
        public static AreaObjectiveSpec[] CandidatesFor(HardwareId hardware)
        {
            switch (hardware)
            {
                // 수비·타깃 우선순위. The core is held; the rest are priorities to shoot.
                case HardwareId.Cpu:
                    return new[]
                    {
                        new AreaObjectiveSpec(AreaObjectiveKind.Hold, "연산 코어 방어"),
                        new AreaObjectiveSpec(AreaObjectiveKind.Destroy, "프로세스 격리"),
                        new AreaObjectiveSpec(AreaObjectiveKind.Destroy, "과열 밸브 차단"),
                    };

                // 회피·조준·공간 읽기. A chain with no clock: the task is working out the order
                // while the floor belongs to laser sweeps.
                case HardwareId.Gpu:
                    return new[]
                    {
                        new AreaObjectiveSpec(AreaObjectiveKind.Sequence, "렌더 큐 정렬", 0),
                        new AreaObjectiveSpec(AreaObjectiveKind.Sequence, "광선 스위치 연결", 1),
                        new AreaObjectiveSpec(AreaObjectiveKind.Destroy, "셰이더 오염점 제거"),
                    };

                // 진위 구분·적응. Two searches and one kill, until decoys exist.
                case HardwareId.Ram:
                    return new[]
                    {
                        new AreaObjectiveSpec(AreaObjectiveKind.Hold, "캐시 유출 차단"),
                        new AreaObjectiveSpec(AreaObjectiveKind.Hold, "오염 페이지 찾기"),
                        new AreaObjectiveSpec(AreaObjectiveKind.Destroy, "가짜 인스턴스 정리"),
                    };

                // 빠른 이동·구간 연결. The same chain as GPU's, against a clock.
                case HardwareId.Ssd:
                    return new[]
                    {
                        new AreaObjectiveSpec(AreaObjectiveKind.Sequence, "IO 레일 활성화", 0),
                        new AreaObjectiveSpec(AreaObjectiveKind.Sequence, "노드 전달", 1),
                        new AreaObjectiveSpec(AreaObjectiveKind.Destroy, "쓰기 잠금 해제"),
                    };

                // 위치 관리·장기전. Every objective is ground to stand on.
                case HardwareId.Hdd:
                    return new[]
                    {
                        new AreaObjectiveSpec(AreaObjectiveKind.Hold, "손상 섹터 복구"),
                        new AreaObjectiveSpec(AreaObjectiveKind.Hold, "헤드 정렬"),
                        new AreaObjectiveSpec(AreaObjectiveKind.Hold, "회전축 방어"),
                    };

                default:
                    throw new ArgumentOutOfRangeException(nameof(hardware), hardware, "Not a hardware area.");
            }
        }

        /// <summary>
        /// The objectives an area with <paramref name="count"/> of them asks for: the first
        /// <paramref name="count"/> candidates of that hardware.
        /// </summary>
        public static AreaObjectiveSpec[] For(HardwareId hardware, int count)
        {
            AreaObjectiveSpec[] candidates = CandidatesFor(hardware);
            if (count < 1 || count > candidates.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count), count,
                    $"{hardware} has {candidates.Length} candidate objective(s). " +
                    $"docs/01 drafts {HardwareIds.DraftMinObjectivesPerArea}~" +
                    $"{HardwareIds.DraftMaxObjectivesPerArea} per area.");
            }

            var taken = new AreaObjectiveSpec[count];
            Array.Copy(candidates, taken, count);
            return taken;
        }

        /// <summary>How many of an area's objectives are chain nodes.</summary>
        public static int ChainLengthFor(HardwareId hardware, int count)
        {
            AreaObjectiveSpec[] specs = For(hardware, count);
            int length = 0;
            for (int i = 0; i < specs.Length; ++i)
            {
                if (specs[i].Kind == AreaObjectiveKind.Sequence)
                    ++length;
            }

            return length;
        }

        /// <summary>[초안] ticks one Hold objective of this hardware asks for.</summary>
        public static int HoldTicksFor(HardwareId hardware) =>
            hardware == HardwareId.Hdd ? DraftLongHoldTicks : DraftHoldTicks;

        /// <summary>
        /// [초안] ticks the whole chain has once it is started, or <see cref="NoChainWindow"/>.
        /// Only SSD has a clock: docs/01 gives it 짧은 시간에 연결 and gives GPU 공간 읽기.
        /// </summary>
        public static int ChainWindowTicksFor(HardwareId hardware) =>
            hardware == HardwareId.Ssd ? DraftSsdChainWindowTicks : NoChainWindow;

        /// <summary>[초안] ticks held at one node of this hardware's chain.</summary>
        public static int ChainNodeTicksFor(HardwareId hardware) => DraftChainNodeTicks;
    }
}
