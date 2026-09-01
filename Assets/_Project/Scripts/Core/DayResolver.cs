using System;

// Compatibility shell kept only so old prototype callback methods still compile.
// The former discrete day simulation has been removed. Runtime progression is
// owned exclusively by ContinuousSimulationSystem.
public static class DayResolver
{
    [Obsolete(
        "Discrete day resolution is removed. Use ContinuousSimulationSystem.",
        false)]
    public static DayResolutionResult ResolveDay(GameState state)
    {
        // Intentionally inert. Even if an obsolete callback is accidentally
        // invoked, it cannot advance the calendar, economy, expedition or events.
        return new DayResolutionResult();
    }
}
