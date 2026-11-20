namespace NeonVoid.Combat
{
    /// <summary>
    /// Whether an actor is mid-dash, for effects that happen along a dash.
    /// <para>
    /// docs/03 has three cards written against dashing - G16 대시 폭발, G08 이동 버퍼 and R05
    /// 돌진 차폐 - and the dash itself lives in the Player domain. This interface exists so the
    /// Plugins domain never has to reach into it: it asks Combat a one-line question, and the
    /// movement code answers it. The layering rule this project already follows for procs
    /// (Combat must not reference Plugins) stays intact in both directions.
    /// </para>
    /// <para>
    /// Deliberately only a flag. The dash's speed, its remaining ticks and its direction are
    /// the movement simulation's business, and a plugin that could read them would be able to
    /// make decisions the movement code would then contradict. What a dash effect needs is
    /// where the actor is each tick, which it can see in the world like anything else.
    /// </para>
    /// </summary>
    public interface IDashState
    {
        /// <summary>True on every tick the actor is dashing, false the tick it ends.</summary>
        bool IsDashing { get; }
    }
}
