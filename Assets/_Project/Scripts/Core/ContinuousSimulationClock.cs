using System;
using System.Collections.Generic;

public sealed class ContinuousSimulationBatch
{
    public readonly StrategicSimulationResult Result = new StrategicSimulationResult();
    public StrategicModalNotice MandatoryNotice;
    public bool RequestAutoPause;
    public bool StateChanged;
    public int ReportDay;
    public double EventHour;

    public bool HasReportableContent =>
        Result.Messages.Count > 0 ||
        Result.NewExpeditionIncidents.Count > 0 ||
        Result.ResearchNotice != null ||
        Result.ExpeditionReturnNotice != null ||
        MandatoryNotice != null ||
        (Result.HadNotableOccurrence && RequestAutoPause);
}

public struct ContinuousClockSnapshot
{
    public int Day;
    public double HourOfDay;
    public bool IsPaused;
    public int SpeedMultiplier;
}

public static partial class ContinuousSimulationSystem
{
    public const double RealSecondsPerGameDay = 120.0;
    public const double GameHoursPerRealSecond = 24.0 / RealSecondsPerGameDay;
    public const double ArmyCellsPerRealSecond = 0.5;
    public const int NormalSpeedMultiplier = 1;
    public const int FastSpeedMultiplier = 3;
    public const double StartHour = 8.0;

    private const double Epsilon = 0.00001;
    private const int MoodLossPerShortageDay = 1;
    private const int PopulationLossPerStarvationDay = 1;
    private const int MoodOnlyShortageDays = 3;

    private sealed class RuntimeState
    {
        public double HourOfDay;
        public bool IsPaused;
        public int SpeedMultiplier;
        public Random Random;

        public int ScheduledDay;
        public double CapitalCrisisCheckHour;
        public double ExpeditionIncidentCheckHour;
        public double ExpeditionDecisionCheckHour;
        public bool CapitalCrisisChecked;
        public bool ExpeditionIncidentChecked;
        public bool ExpeditionDecisionChecked;

        public ExpeditionData TrackedExpedition;
        public List<MapPointData> TrackedRoute;
        public int TrackedRouteIndex;
        public double SegmentProgress;
    }

    private static readonly Dictionary<GameState, RuntimeState> RuntimeStates =
        new Dictionary<GameState, RuntimeState>();

    private static int nextContinuousDecisionId = 100000;

    public static void Reset(GameState state)
    {
        if (state == null)
            return;

        RuntimeState runtime = new RuntimeState
        {
            HourOfDay = StartHour,
            IsPaused = true,
            SpeedMultiplier = NormalSpeedMultiplier,
            Random = new Random(state.WorldSeed ^ 0x4B534354)
        };

        RuntimeStates[state] = runtime;
        ScheduleDailyChecks(state, runtime, StartHour);
        ResetRouteTracking(runtime, state.ActiveExpedition);
    }

    public static ContinuousClockSnapshot GetClock(GameState state)
    {
        RuntimeState runtime = GetRuntime(state);
        return new ContinuousClockSnapshot
        {
            Day = state != null ? state.Day : 0,
            HourOfDay = runtime.HourOfDay,
            IsPaused = runtime.IsPaused,
            SpeedMultiplier = runtime.SpeedMultiplier
        };
    }

    public static bool IsPaused(GameState state) => GetRuntime(state).IsPaused;

    public static void SetPaused(GameState state, bool paused)
    {
        GetRuntime(state).IsPaused = paused;
    }

    public static void TogglePause(GameState state)
    {
        RuntimeState runtime = GetRuntime(state);
        runtime.IsPaused = !runtime.IsPaused;
    }

    public static int GetSpeedMultiplier(GameState state) =>
        GetRuntime(state).SpeedMultiplier;

    public static void ToggleSpeed(GameState state)
    {
        RuntimeState runtime = GetRuntime(state);
        runtime.SpeedMultiplier =
            runtime.SpeedMultiplier == FastSpeedMultiplier
                ? NormalSpeedMultiplier
                : FastSpeedMultiplier;
    }

    public static bool HasExpeditionStartedMoving(GameState state)
    {
        if (state == null || !state.HasActiveExpedition)
            return false;

        ExpeditionData expedition = state.ActiveExpedition;

        if (expedition.RouteIndex > 0)
            return true;

        double dx = expedition.CurrentMapXPercent - WorldMapNavigation.CapitalXPercent;
        double dy = expedition.CurrentMapYPercent - WorldMapNavigation.CapitalYPercent;
        return dx * dx + dy * dy > 0.0001;
    }

    public static void NotifyRouteChanged(GameState state)
    {
        RuntimeState runtime = GetRuntime(state);
        ExpeditionData expedition = state != null ? state.ActiveExpedition : null;

        // Новый A* всё ещё начинается с ближайшего узла сетки. При смене
        // приказа посреди клетки сохраняем фактическую позицию армии как
        // первый визуальный сегмент нового маршрута, чтобы маркер не прыгал
        // обратно к центру ближайшей клетки.
        if (expedition != null && expedition.Route != null && expedition.Route.Count > 0)
        {
            expedition.Route[0].XPercent = expedition.CurrentMapXPercent;
            expedition.Route[0].YPercent = expedition.CurrentMapYPercent;
        }

        ResetRouteTracking(runtime, expedition);
    }

    public static double GetTravelHoursRemaining(GameState state)
    {
        if (state == null || !state.HasActiveExpedition)
            return 0.0;

        RuntimeState runtime = GetRuntime(state);
        EnsureRouteTracking(state, runtime);
        ExpeditionData expedition = state.ActiveExpedition;

        if (expedition.Phase != CommanderState.TravellingToLocation &&
            expedition.Phase != CommanderState.ReturningToCastle)
        {
            return 0.0;
        }

        double remainingCells = GetRemainingCells(expedition, runtime);
        double movementHours = remainingCells / CellsPerGameHour;
        double delayHours = Math.Max(0.0, expedition.RouteDelayHoursRemaining);
        return movementHours + delayHours;
    }

    public static double GetResearchHoursRemaining(GameState state)
    {
        if (state == null ||
            !state.HasActiveExpedition ||
            !state.ActiveExpedition.IsLocationResearchInProgress)
        {
            return 0.0;
        }

        return Math.Max(
            0.0,
            state.ActiveExpedition.ActiveActivity.RemainingHours);
    }

    public static ContinuousSimulationBatch Advance(
        GameState state,
        float unscaledRealSeconds,
        bool processRandomEvents = true)
    {
        ContinuousSimulationBatch batch = new ContinuousSimulationBatch
        {
            ReportDay = state != null ? state.Day : 0
        };

        if (state == null || unscaledRealSeconds <= 0f)
            return batch;

        RuntimeState runtime = GetRuntime(state);

        if (runtime.IsPaused)
            return batch;

        EnsureRouteTracking(state, runtime);

        if (ConsumeQueuedTravelPoints(state, runtime, batch))
        {
            PauseIfRequested(runtime, batch);
            return batch;
        }

        double scaledRealSeconds =
            unscaledRealSeconds * runtime.SpeedMultiplier;
        double remainingGameHours =
            scaledRealSeconds * GameHoursPerRealSecond;

        while (remainingGameHours > Epsilon && !runtime.IsPaused)
        {
            if (processRandomEvents &&
                ProcessDueRandomChecks(state, runtime, batch))
            {
                PauseIfRequested(runtime, batch);
                if (runtime.IsPaused)
                    break;
            }

            double hoursToMidnight = 24.0 - runtime.HourOfDay;
            double hoursToCheck = processRandomEvents
                ? HoursUntilNextCheck(runtime)
                : double.MaxValue;
            double stepHours = Math.Min(
                remainingGameHours,
                Math.Min(hoursToMidnight, hoursToCheck));

            if (stepHours <= Epsilon)
            {
                if (hoursToMidnight <= Epsilon)
                {
                    ResolveMidnight(state, runtime, batch);
                    if (batch.RequestAutoPause)
                        PauseIfRequested(runtime, batch);
                    continue;
                }

                if (processRandomEvents)
                {
                    ProcessDueRandomChecks(state, runtime, batch);
                    PauseIfRequested(runtime, batch);
                    continue;
                }

                break;
            }

            double advancedHours = AdvanceOngoingActivities(
                state,
                runtime,
                stepHours,
                batch);

            // Восстановление использует ровно то игровое время, которое реально
            // продвинулось в этом шаге. Бойцы активной экспедиции внутри
            // BattleSystem отфильтровываются и пассивно не лечатся.
            BattleSystem.AdvanceCapitalRecovery(state, advancedHours);

            runtime.HourOfDay += advancedHours;
            remainingGameHours -= advancedHours;
            batch.StateChanged = true;
            batch.EventHour = runtime.HourOfDay;

            if (batch.RequestAutoPause)
            {
                PauseIfRequested(runtime, batch);
                break;
            }

            if (runtime.HourOfDay >= 24.0 - Epsilon)
            {
                ResolveMidnight(state, runtime, batch);
                PauseIfRequested(runtime, batch);
            }
        }

        return batch;
    }

    public static string FormatClock(double hourOfDay)
    {
        double normalized = hourOfDay % 24.0;
        if (normalized < 0.0)
            normalized += 24.0;

        int totalMinutes = (int)Math.Floor(normalized * 60.0 + 0.0001);
        int hours = (totalMinutes / 60) % 24;
        int minutes = totalMinutes % 60;
        return hours.ToString("00") + ":" + minutes.ToString("00");
    }

    public static double CellsPerGameHour =>
        ArmyCellsPerRealSecond / GameHoursPerRealSecond;

    public static double CalculateTravelHours(
        List<MapPointData> route,
        int routeIndex = 0)
    {
        int cells = WorldMapNavigation.CalculateRouteCells(route, routeIndex);
        return cells / CellsPerGameHour;
    }

    public static string FormatTravelTime(
        List<MapPointData> route,
        int routeIndex = 0)
    {
        return ContinuousExpeditionCommands.FormatHours(
            CalculateTravelHours(route, routeIndex));
    }

    private static RuntimeState GetRuntime(GameState state)
    {
        if (state == null)
            return new RuntimeState
            {
                HourOfDay = StartHour,
                IsPaused = true,
                SpeedMultiplier = NormalSpeedMultiplier,
                Random = new Random(1)
            };

        RuntimeState runtime;
        if (!RuntimeStates.TryGetValue(state, out runtime))
        {
            Reset(state);
            runtime = RuntimeStates[state];
        }

        return runtime;
    }

    private static void ScheduleDailyChecks(
        GameState state,
        RuntimeState runtime,
        double earliestHour)
    {
        runtime.ScheduledDay = state.Day;
        double from = Math.Max(0.0, Math.Min(23.95, earliestHour));
        double span = Math.Max(0.04, 23.95 - from);
        runtime.CapitalCrisisCheckHour = from + runtime.Random.NextDouble() * span;
        runtime.ExpeditionIncidentCheckHour = from + runtime.Random.NextDouble() * span;
        runtime.ExpeditionDecisionCheckHour = from + runtime.Random.NextDouble() * span;
        runtime.CapitalCrisisChecked = false;
        runtime.ExpeditionIncidentChecked = false;
        runtime.ExpeditionDecisionChecked = false;
    }

    private static double HoursUntilNextCheck(RuntimeState runtime)
    {
        double next = double.MaxValue;

        if (!runtime.CapitalCrisisChecked)
            next = Math.Min(next, runtime.CapitalCrisisCheckHour - runtime.HourOfDay);
        if (!runtime.ExpeditionIncidentChecked)
            next = Math.Min(next, runtime.ExpeditionIncidentCheckHour - runtime.HourOfDay);
        if (!runtime.ExpeditionDecisionChecked)
            next = Math.Min(next, runtime.ExpeditionDecisionCheckHour - runtime.HourOfDay);

        return Math.Max(0.0, next);
    }
}
