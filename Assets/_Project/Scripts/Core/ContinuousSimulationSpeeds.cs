public static partial class ContinuousSimulationSystem
{
    public const int VeryFastSpeedMultiplier = 5;
    public const int MaximumSpeedMultiplier = 10;

    public static bool IsSupportedSpeedMultiplier(int multiplier)
    {
        return multiplier == NormalSpeedMultiplier ||
               multiplier == FastSpeedMultiplier ||
               multiplier == VeryFastSpeedMultiplier ||
               multiplier == MaximumSpeedMultiplier;
    }

    public static bool SetSpeedMultiplier(GameState state, int multiplier)
    {
        if (state == null || !IsSupportedSpeedMultiplier(multiplier))
            return false;

        GetRuntime(state).SpeedMultiplier = multiplier;
        return true;
    }
}
