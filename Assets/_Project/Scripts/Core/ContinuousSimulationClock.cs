using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

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

// AM-05 (канон v1.33, §9.9 / раздел 15 инструкции по миграции): часть
// непрерывного времени живёт не в GameState, а в приватном RuntimeState
// внутри ConditionalWeakTable<GameState, RuntimeState> — простая
// сериализация GameState это не сохраняет. Явный снимок/восстановление
// (ExportSnapshot/RestoreSnapshot) — единственный контракт, которым
// внешний код (Save/Load) может честно перенести это состояние через save-файл.
[Serializable]
public sealed class ContinuousSimulationSnapshotData
{
    public double HourOfDay;
    public bool IsPaused;
    public int SpeedMultiplier;

    // System.Random нельзя считать восстановленным только по seed после
    // нескольких использований — сохраняем количество уже сделанных "шагов"
    // потока (см. ExportSnapshot/RestoreSnapshot) и на восстановлении
    // прокручиваем свежий Random на столько же шагов вперёд.
    public int RandomDrawCount;

    public int ScheduledDay;
    public double ExpeditionIncidentCheckHour;
    public double ExpeditionDecisionCheckHour;
    public bool ExpeditionIncidentChecked;
    public bool ExpeditionDecisionChecked;

    public int TrackedRouteIndex;
    public double SegmentProgress;
}

public static partial class ContinuousSimulationSystem
{
    // Ускорение ×2 (запрос пользователя, WM-13): было 120.0. Это темп течения
    // МИРОВОГО времени дома и во время дел похода (ночлег, исследование).
    // Пока отряд бежит по карте, часы идут в темпе бега (12И, канон v1.50,
    // см. GetRunningGameHoursPerRealSecond), а не по этой константе.
    public const double RealSecondsPerGameDay = 60.0;
    public const double GameHoursPerRealSecond = 24.0 / RealSecondsPerGameDay;
    public const int NormalSpeedMultiplier = 1;
    public const int FastSpeedMultiplier = 3;
    public const double StartHour = 8.0;

    private const double Epsilon = 0.00001;

    // ПР-07Б (PR07_HOME_SPEC §0.3): длинный шаг проходит кусками не
    // длиннее четверти часа — выход, возвращение или выздоровление внутри
    // шага не засчитывают домашней работы (ловли, ремонта, ухода) больше
    // одного подшага.
    public const double MaxHomeSubstepHours = 0.25;

    private sealed class RuntimeState
    {
        public double HourOfDay;
        public bool IsPaused;
        public int SpeedMultiplier;
        public Random Random;
        public int RandomDrawCount;

        public int ScheduledDay;
        public double ExpeditionIncidentCheckHour;
        public double ExpeditionDecisionCheckHour;
        public bool ExpeditionIncidentChecked;
        public bool ExpeditionDecisionChecked;

        public ExpeditionData TrackedExpedition;
        public List<MapPointData> TrackedRoute;
        public int TrackedRouteIndex;
        public double SegmentProgress;
    }

    private static readonly ConditionalWeakTable<GameState, RuntimeState> RuntimeStates =
        new ConditionalWeakTable<GameState, RuntimeState>();

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

        RuntimeStates.Remove(state);
        RuntimeStates.Add(state, runtime);
        ScheduleDailyChecks(state, runtime, StartHour);
        ResetRouteTracking(runtime, state.ActiveExpedition);
    }

    // Есть ли у кампании уже свой ход времени (новая через Reset, загруженная
    // через RestoreSnapshot, или та же кампания, вернувшаяся в сцену).
    // UI сбрасывает часы только когда его ещё нет — иначе загрузка и смена
    // сцены теряли бы время суток и положение на маршруте.
    public static bool HasRuntimeState(GameState state)
    {
        return state != null && RuntimeStates.TryGetValue(state, out _);
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

    // Единый источник истины "герой физически движется или занят явным
    // времязатратным действием прямо сейчас" — модель "движение = течение
    // времени": стратегическое время должно идти ровно тогда, когда это
    // возвращает true (и не заблокировано модальным окном/решением на
    // уровне UI), и стоять во всех остальных случаях. Не читает и не
    // меняет паузу сама — только сообщает факт, вызывающая сторона решает,
    // что с ним делать (см. PrototypeUIController.ContinuousTime.cs).
    public static bool HasMovementOrActivityInProgress(GameState state)
    {
        if (state == null || !state.HasActiveExpedition)
            return false;

        ExpeditionData expedition = state.ActiveExpedition;
        if (expedition.HasTimedActivity)
            return true;

        return expedition.Phase == CommanderState.TravellingToLocation ||
               expedition.Phase == CommanderState.ReturningToCastle;
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

        if (expedition != null && expedition.Route != null && expedition.Route.Count > 0)
        {
            expedition.Route[0].XPercent = expedition.CurrentMapXPercent;
            expedition.Route[0].YPercent = expedition.CurrentMapYPercent;
        }

        ResetRouteTracking(runtime, expedition);
    }

    // 12И: оставшиеся часы пути — по местности под каждым куском пути.
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

        double movementHours = WorldMapNavigation.EstimateTravelHours(
            expedition.Route, expedition.RouteIndex, runtime.SegmentProgress);
        double delayHours = Math.Max(0.0, expedition.RouteDelayHoursRemaining);
        return movementHours + delayHours;
    }

    // 12И (канон v1.50 §9.6): отряд бежит — путь есть, задержек, дел и
    // обязательного решения нет. Только в этом состоянии часы идут в темпе бега.
    public static bool IsExpeditionRunning(GameState state)
    {
        if (state == null || !state.HasActiveExpedition || state.HasPendingExpeditionDecision)
            return false;

        ExpeditionData expedition = state.ActiveExpedition;
        if (expedition.HasTimedActivity ||
            expedition.RouteDelayHoursRemaining > Epsilon ||
            (expedition.Phase != CommanderState.TravellingToLocation &&
             expedition.Phase != CommanderState.ReturningToCastle))
        {
            return false;
        }

        return expedition.Route != null &&
               expedition.Route.Count > 1 &&
               expedition.RouteIndex < expedition.Route.Count - 1;
    }

    // Игровые часы за реальную секунду бега в местности под героем:
    // скорость бега (клеток/с) × часы на клетку.
    public static double GetRunningGameHoursPerRealSecond(GameState state)
    {
        WorldMapMovementRules rules = WorldMapMovementRules.Current;
        if (state == null || !state.HasActiveExpedition)
            return rules.RunningGameHoursPerRealSecond(WorldMapGameplayTerrainType.OpenGround);

        ExpeditionData expedition = state.ActiveExpedition;
        WorldMapGameplayTerrainType terrain = WorldMapNavigation.GetTerrainAtPercent(
            expedition.CurrentMapXPercent,
            expedition.CurrentMapYPercent);
        if (!rules.IsTraversable(terrain))
            terrain = WorldMapGameplayTerrainType.OpenGround;
        return rules.RunningGameHoursPerRealSecond(terrain);
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

        // 12И: бегущий отряд задаёт темп часов сам — кнопки скорости на бег
        // не действуют; дома и в делах похода часы идут как прежде.
        bool running = IsExpeditionRunning(state);
        double remainingGameHours = running
            ? unscaledRealSeconds * GetRunningGameHoursPerRealSecond(state)
            : unscaledRealSeconds * runtime.SpeedMultiplier * GameHoursPerRealSecond;

        while (remainingGameHours > Epsilon && !runtime.IsPaused)
        {
            // Отряд остановился посреди кадра — остаток кадра стоя не тратится.
            if (running && !IsExpeditionRunning(state))
                break;

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
                Math.Min(remainingGameHours, MaxHomeSubstepHours),
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

            // ПР-06А: домашняя работа и уход идут ровно на прошедшие часы.
            HomeLife.Advance(state, advancedHours, batch.Result.Messages);

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

    // 12И: оценка для ещё не начатой поездки (TravelHoursFromCapital, превью).
    public static double CalculateTravelHours(
        List<MapPointData> route,
        int routeIndex = 0)
    {
        return WorldMapNavigation.EstimateTravelHours(route, routeIndex);
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
            RuntimeStates.TryGetValue(state, out runtime);
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
        runtime.ExpeditionIncidentCheckHour = from + DrawRandom(runtime) * span;
        runtime.ExpeditionDecisionCheckHour = from + DrawRandom(runtime) * span;
        runtime.ExpeditionIncidentChecked = false;
        runtime.ExpeditionDecisionChecked = false;
    }

    // AM-05: единственная точка чтения runtime.Random — считает количество
    // сделанных "шагов" потока, чтобы Save/Load мог честно прокрутить свежий
    // Random на то же место после восстановления по seed (см.
    // ContinuousSimulationSnapshotData.RandomDrawCount).
    private static double DrawRandom(RuntimeState runtime)
    {
        runtime.RandomDrawCount++;
        return runtime.Random.NextDouble();
    }

    private static double HoursUntilNextCheck(RuntimeState runtime)
    {
        double next = double.MaxValue;

        if (!runtime.ExpeditionIncidentChecked)
            next = Math.Min(next, runtime.ExpeditionIncidentCheckHour - runtime.HourOfDay);
        if (!runtime.ExpeditionDecisionChecked)
            next = Math.Min(next, runtime.ExpeditionDecisionCheckHour - runtime.HourOfDay);

        return Math.Max(0.0, next);
    }

    // AM-05: явный снимок/восстановление скрытого RuntimeState — единственный
    // способ для Save/Load честно перенести часы/паузу/скорость/прогресс
    // текущего сегмента маршрута и позицию потока случайности через save-файл.
    public static ContinuousSimulationSnapshotData ExportSnapshot(GameState state)
    {
        RuntimeState runtime = GetRuntime(state);
        return new ContinuousSimulationSnapshotData
        {
            HourOfDay = runtime.HourOfDay,
            IsPaused = runtime.IsPaused,
            SpeedMultiplier = runtime.SpeedMultiplier,
            RandomDrawCount = runtime.RandomDrawCount,
            ScheduledDay = runtime.ScheduledDay,
            ExpeditionIncidentCheckHour = runtime.ExpeditionIncidentCheckHour,
            ExpeditionDecisionCheckHour = runtime.ExpeditionDecisionCheckHour,
            ExpeditionIncidentChecked = runtime.ExpeditionIncidentChecked,
            ExpeditionDecisionChecked = runtime.ExpeditionDecisionChecked,
            TrackedRouteIndex = runtime.TrackedRouteIndex,
            SegmentProgress = runtime.SegmentProgress
        };
    }

    public static void RestoreSnapshot(GameState state, ContinuousSimulationSnapshotData snapshot)
    {
        if (state == null || snapshot == null)
            return;

        RuntimeState runtime = new RuntimeState
        {
            HourOfDay = snapshot.HourOfDay,
            IsPaused = snapshot.IsPaused,
            SpeedMultiplier = snapshot.SpeedMultiplier,
            Random = new Random(state.WorldSeed ^ 0x4B534354),
            ScheduledDay = snapshot.ScheduledDay,
            ExpeditionIncidentCheckHour = snapshot.ExpeditionIncidentCheckHour,
            ExpeditionDecisionCheckHour = snapshot.ExpeditionDecisionCheckHour,
            ExpeditionIncidentChecked = snapshot.ExpeditionIncidentChecked,
            ExpeditionDecisionChecked = snapshot.ExpeditionDecisionChecked
        };

        // Явный seed в конструкторе Random гарантированно использует
        // алгоритм с воспроизводимой последовательностью (совместимый со
        // старым .NET Framework), а не Xoshiro — поэтому "прокрутка" через
        // повторные вызовы NextDouble() детерминированно возвращает
        // генератор в то же состояние, в котором он был на момент сохранения.
        // Прокрутка идёт мимо DrawRandom(), поэтому счётчик выставляется
        // явно — иначе новый RuntimeState считал бы, что ещё не сделал ни
        // одного "шага", и следующий реальный DrawRandom() сбился бы со счёта.
        for (int i = 0; i < snapshot.RandomDrawCount; i++)
            runtime.Random.NextDouble();
        runtime.RandomDrawCount = snapshot.RandomDrawCount;

        RuntimeStates.Remove(state);
        RuntimeStates.Add(state, runtime);

        ResetRouteTracking(runtime, state.ActiveExpedition);
        runtime.TrackedRouteIndex = snapshot.TrackedRouteIndex;
        runtime.SegmentProgress = snapshot.SegmentProgress;
    }
}
