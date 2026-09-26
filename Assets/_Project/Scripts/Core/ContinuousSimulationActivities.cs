using System;
using System.Collections.Generic;

public static partial class ContinuousSimulationSystem
{
    private static bool ProcessDueRandomChecks(
        GameState state,
        RuntimeState runtime,
        ContinuousSimulationBatch batch)
    {
        bool processed = false;
        batch.ReportDay = state.Day;
        batch.EventHour = runtime.HourOfDay;

        if (!runtime.ExpeditionIncidentChecked &&
            runtime.HourOfDay + Epsilon >= runtime.ExpeditionIncidentCheckHour)
        {
            runtime.ExpeditionIncidentChecked = true;
            processed = true;
            bool expeditionIsBusy =
                state.HasActiveExpedition &&
                state.ActiveExpedition.HasTimedActivity;

            if (expeditionIsBusy)
                return processed;

            bool hadExpedition = state.HasActiveExpedition;
            ExpeditionIncidentSystem.ResolveAtScheduledCheck(
                state,
                state.Day,
                batch.Result);

            EnsureRouteTracking(state, runtime);

            if (ConsumeQueuedTravelPoints(state, runtime, batch))
                batch.RequestAutoPause = true;

            if (!batch.RequestAutoPause &&
                hadExpedition &&
                state.HasActiveExpedition &&
                state.ActiveExpedition.Phase == CommanderState.AtLocation)
            {
                AddContinuousArrivalNotice(state, batch);
            }
        }

        if (batch.RequestAutoPause)
            return processed;

        if (!runtime.ExpeditionDecisionChecked &&
            runtime.HourOfDay + Epsilon >= runtime.ExpeditionDecisionCheckHour)
        {
            runtime.ExpeditionDecisionChecked = true;
            processed = true;
            bool expeditionIsBusy =
                state.HasActiveExpedition &&
                state.ActiveExpedition.HasTimedActivity;

            if (expeditionIsBusy)
                return processed;

            bool hadPending = state.HasPendingExpeditionDecision;

            ExpeditionDecisionSystem.ResolveAtScheduledCheck(
                state,
                state.Day,
                batch.Result);

            if (!hadPending && state.HasPendingExpeditionDecision)
            {
                batch.RequestAutoPause = true;
                batch.Result.HadNotableOccurrence = true;
            }
        }

        return processed;
    }

    private static double AdvanceOngoingActivities(
        GameState state,
        RuntimeState runtime,
        double gameHours,
        ContinuousSimulationBatch batch)
    {
        double elapsedHours;
        double movementHours = AdvanceTimedActivity(
            state,
            gameHours,
            batch,
            out elapsedHours);

        if (batch.RequestAutoPause)
            return elapsedHours;

        double nonMovementHours = Math.Max(0.0, gameHours - movementHours);
        double movementElapsedHours = AdvanceExpeditionMovement(
            state,
            runtime,
            movementHours,
            batch);

        if (batch.RequestAutoPause)
            return Math.Min(gameHours, nonMovementHours + movementElapsedHours);

        return gameHours;
    }

    private static double AdvanceTimedActivity(
        GameState state,
        double gameHours,
        ContinuousSimulationBatch batch,
        out double elapsedHours)
    {
        elapsedHours = gameHours;

        if (!state.HasActiveExpedition ||
            !state.ActiveExpedition.HasTimedActivity)
        {
            return gameHours;
        }

        ExpeditionData expedition = state.ActiveExpedition;
        ExpeditionActivityData activity = expedition.ActiveActivity;

        if (state.HasPendingExpeditionDecision)
            return 0.0;

        double usedHours = Math.Min(
            gameHours,
            Math.Max(0.0, activity.RemainingHours));
        activity.RemainingHours = Math.Max(
            0.0,
            activity.RemainingHours - usedHours);

        if (activity.RemainingHours > Epsilon)
            return 0.0;

        expedition.ActiveActivity = null;

        // ПР-09: утро после ночлега — итог стоянки вместо отчёта о добыче.
        if (activity.Id == CampRest.RestActivityId)
        {
            CampRest.CompleteRest(state, batch.Result.Messages);
            batch.Result.HadNotableOccurrence = true;
            batch.ReportDay = state.Day;
            return Math.Max(0.0, gameHours - usedHours);
        }

        if (activity.Kind == ExpeditionActivityKind.RoadStop)
        {
            state.ArmyGold += Math.Max(0, activity.RewardArmyGold);
            state.ArmySupply += Math.Max(0, activity.RewardArmySupply);

            List<string> roadRewards = BuildActivityRewards(activity);
            batch.Result.Messages.Add(
                "Действие «" + activity.DisplayName + "» завершено. " +
                (roadRewards.Count > 0
                    ? "Добыча отряда: " + string.Join(", ", roadRewards) + "."
                    : "Отряд продолжил маршрут."));
            batch.Result.HadNotableOccurrence = true;
            batch.ReportDay = state.Day;
            return Math.Max(0.0, gameHours - usedHours);
        }

        LocationData location = state.FindLocation(activity.LocationId);
        if (location == null || location.IsWaypoint)
            return 0.0;

        location.IsExplored = true;

        state.ArmyGold += Math.Max(0, activity.RewardArmyGold);
        state.ArmySupply += Math.Max(0, activity.RewardArmySupply);

        List<string> rewards = BuildActivityRewards(activity);

        string rewardText = rewards.Count > 0
            ? string.Join(", ", rewards)
            : "добычи нет";

        batch.Result.Messages.Add(
            "Локация «" + location.Name +
            "» исследована. Добыча отряда: " + rewardText + ".");

        // Канон v1.48 §27.4: впервые исследованное место — пережитое заново.
        string experience = CharacterProgressionService.DescribeShared(
            "Опыт исследования",
            CharacterProgressionService.AwardShared(state, "explore." + location.Id,
                CharacterProgression.ExplorationExperience, CharacterProgressionService.PartyPersonIds(state)));
        if (experience.Length > 0)
            batch.Result.Messages.Add(experience);
        // ПР-09 (ТЗ §6): карточка итога — что увидели и что получили.
        batch.Result.ResearchNotice = new StrategicModalNotice
        {
            Title = "ИССЛЕДОВАНИЕ ЗАВЕРШЕНО",
            Description = string.IsNullOrWhiteSpace(location.ResearchResultText)
                ? "Место «" + location.Name + "» осмотрено."
                : location.ResearchResultText,
            Consequence = "Получено: " + rewardText + ". Добытое остаётся у отряда."
        };
        batch.Result.HadNotableOccurrence = true;
        batch.RequestAutoPause = true;
        batch.ReportDay = state.Day;
        elapsedHours = usedHours;
        return 0.0;
    }

    private static List<string> BuildActivityRewards(
        ExpeditionActivityData activity)
    {
        List<string> rewards = new List<string>();

        if (activity.RewardArmyGold > 0)
            rewards.Add("золото +" + activity.RewardArmyGold);
        if (activity.RewardArmySupply > 0)
            rewards.Add("припасы +" + activity.RewardArmySupply);

        return rewards;
    }

    private static double AdvanceExpeditionMovement(
        GameState state,
        RuntimeState runtime,
        double gameHours,
        ContinuousSimulationBatch batch)
    {
        if (!state.HasActiveExpedition ||
            state.HasPendingExpeditionDecision)
        {
            return gameHours;
        }

        ExpeditionData expedition = state.ActiveExpedition;

        if (expedition.HasTimedActivity ||
            (expedition.Phase != CommanderState.TravellingToLocation &&
             expedition.Phase != CommanderState.ReturningToCastle))
        {
            return gameHours;
        }

        EnsureRouteTracking(state, runtime);

        // До фактического старта подготовленный командир остаётся дома.
        // При начале движения состояние синхронизируется с фазой экспедиции.
        CommanderData movingCommander = state.FindCommander(expedition.CommanderId);
        if (movingCommander != null)
            movingCommander.State = expedition.Phase;

        double movementHours = ConsumeTravelDelay(
            expedition,
            runtime,
            gameHours);
        double delayHoursUsed = Math.Max(0.0, gameHours - movementHours);

        if (movementHours <= Epsilon)
        {
            UpdateRemainingRouteCells(expedition, runtime);
            return gameHours;
        }

        // WM-T04 (задача "gameplay-география дорог"): бюджет ведётся в
        // игровых часах, а не в клетках, как раньше — скорость (клеток на
        // час) больше не постоянна на весь вызов, а зависит от того, где
        // герой находится ПРЯМО СЕЙЧАС (живой запрос, не связан с плотностью
        // маршрута, посчитанной один раз в WorldMapNavigation.FindPath для
        // Hills/Mountains — тот механизм не тронут). При множителе 1.0
        // (везде, где нет дороги) арифметика идентична прежней константе
        // CellsPerGameHour.
        double hoursRemaining = movementHours;
        double hoursUsedByMovement = 0.0;

        while (hoursRemaining > Epsilon && state.HasActiveExpedition)
        {
            EnsureRouteTracking(state, runtime);

            if (expedition.Route == null ||
                expedition.Route.Count == 0 ||
                expedition.RouteIndex >= expedition.Route.Count - 1)
            {
                ResolveRouteArrival(state, runtime, batch);
                break;
            }

            if (expedition.LastTravelPoints.Count == 0)
            {
                expedition.LastTravelStartedPhase = expedition.Phase;
                expedition.LastTravelTargetLocationId = expedition.LocationId;
                expedition.LastTravelTargetXPercent = expedition.TargetMapXPercent;
                expedition.LastTravelTargetYPercent = expedition.TargetMapYPercent;
            }

            float speedMultiplier = GetLiveMovementSpeedMultiplier(
                expedition.CurrentMapXPercent,
                expedition.CurrentMapYPercent);
            double effectiveCellsPerGameHour =
                CellsPerGameHour * Math.Max(0.0001, speedMultiplier);

            double segmentRemaining = 1.0 - runtime.SegmentProgress;
            double maxCellsThisStep = hoursRemaining * effectiveCellsPerGameHour;
            double usedCells = Math.Min(maxCellsThisStep, segmentRemaining);
            double usedHours = usedCells / effectiveCellsPerGameHour;

            runtime.SegmentProgress += usedCells;
            hoursRemaining -= usedHours;
            hoursUsedByMovement += usedHours;

            MapPointData from = expedition.Route[expedition.RouteIndex];
            MapPointData to = expedition.Route[expedition.RouteIndex + 1];
            float t = (float)Math.Max(0.0, Math.Min(1.0, runtime.SegmentProgress));
            expedition.CurrentMapXPercent = Lerp(from.XPercent, to.XPercent, t);
            expedition.CurrentMapYPercent = Lerp(from.YPercent, to.YPercent, t);

            if (runtime.SegmentProgress < 1.0 - Epsilon)
                break;

            expedition.RouteIndex++;
            runtime.TrackedRouteIndex = expedition.RouteIndex;
            runtime.SegmentProgress = 0.0;
            expedition.CurrentMapXPercent = to.XPercent;
            expedition.CurrentMapYPercent = to.YPercent;
            expedition.LastTravelPoints.Add(
                new MapPointData(to.XPercent, to.YPercent));

            if (TryResolveDiscovery(state, runtime, batch))
                break;

            expedition.LastTravelPoints.Clear();

            if (expedition.RouteIndex >= expedition.Route.Count - 1)
            {
                ResolveRouteArrival(state, runtime, batch);
                break;
            }
        }

        if (state.HasActiveExpedition)
            UpdateRemainingRouteCells(state.ActiveExpedition, runtime);

        if (batch.RequestAutoPause)
            return Math.Min(gameHours, delayHoursUsed + hoursUsedByMovement);

        return gameHours;
    }

    // WM-T04: единственное место, где живой запрос местности превращается в
    // множитель скорости для движения экспедиции. Не размазано по UI/другим
    // системам — раздел 11 задачи.
    private static float GetLiveMovementSpeedMultiplier(
        float xPercent,
        float yPercent)
    {
        return WorldMapGameplayTerrainQuery.GetMovementMultiplier(
            WorldMapNavigation.ActiveDefinition,
            xPercent,
            yPercent);
    }

    private static double ConsumeTravelDelay(
        ExpeditionData expedition,
        RuntimeState runtime,
        double gameHours)
    {
        if (expedition.RouteDelayHoursRemaining <= Epsilon)
            return gameHours;

        double usedDelay = Math.Min(
            gameHours,
            expedition.RouteDelayHoursRemaining);
        expedition.RouteDelayHoursRemaining = Math.Max(
            0.0,
            expedition.RouteDelayHoursRemaining - usedDelay);

        return Math.Max(0.0, gameHours - usedDelay);
    }

    private static bool TryResolveDiscovery(
        GameState state,
        RuntimeState runtime,
        ContinuousSimulationBatch batch)
    {
        LocationData location = state.FindFirstHiddenLocationAlongLastTravel();

        if (location == null)
            return false;

        string stopMessage;
        if (!state.StopAtDiscoveredLocation(location, out stopMessage))
            return false;

        ExpeditionData expedition = state.ActiveExpedition;
        expedition.PendingDecision = new ExpeditionDecisionOccurrence
        {
            Id = nextContinuousDecisionId++,
            Day = state.Day,
            DefinitionId = "location_discovered",
            Title = "Обнаружена локация «" + location.Name + "»",
            Description =
                "Вы обнаружили локацию «" + location.Name +
                "». Отряд немедленно остановился у неё.",
            OptionA = new ExpeditionDecisionOptionView
            {
                Id = "investigate_discovered_location",
                Label = "Исследовать",
                ConsequencePreview = location.ExplorationHours > 0
                    ? "Начать исследование локации"
                    : "Осмотреть найденное место"
            },
            OptionB = new ExpeditionDecisionOptionView
            {
                Id = "continue_interrupted_route",
                Label = "Продолжить маршрут",
                ConsequencePreview = "Вернуться к прерванной цели"
            }
        };

        batch.Result.Messages.Add(
            stopMessage + " Требуется приказ: исследовать находку " +
            "или продолжить прежний маршрут.");
        batch.Result.HadNotableOccurrence = true;
        batch.RequestAutoPause = true;
        batch.ReportDay = state.Day;
        ResetRouteTracking(runtime, expedition);
        return true;
    }

    private static bool ConsumeQueuedTravelPoints(
        GameState state,
        RuntimeState runtime,
        ContinuousSimulationBatch batch)
    {
        if (!state.HasActiveExpedition ||
            state.ActiveExpedition.LastTravelPoints == null ||
            state.ActiveExpedition.LastTravelPoints.Count == 0)
        {
            return false;
        }

        if (TryResolveDiscovery(state, runtime, batch))
            return true;

        state.ActiveExpedition.LastTravelPoints.Clear();
        EnsureRouteTracking(state, runtime);
        return false;
    }

    private static void ResolveRouteArrival(
        GameState state,
        RuntimeState runtime,
        ContinuousSimulationBatch batch)
    {
        if (!state.HasActiveExpedition)
            return;

        ExpeditionData expedition = state.ActiveExpedition;
        CommanderData commander = state.FindCommander(expedition.CommanderId);

        if (commander == null)
            return;

        expedition.RemainingRouteCells = 0;
        expedition.RouteLengthCells = 0;
        expedition.RouteDelayHoursRemaining = 0.0;

        if (expedition.Phase == CommanderState.ReturningToCastle)
        {
            int fighterCount = expedition.FighterIds.Count;
            string commanderName = commander.Name;
            string delivered = state.CompleteExpeditionReturn();

            // ПР-08: отдельной сводки «Экспедиция вернулась» больше нет —
            // только строка в донесениях.
            batch.Result.Messages.Add(
                commanderName + " и " + fighterCount +
                " бойцов вернулись в Дом. " + delivered);
            batch.Result.HadNotableOccurrence = true;
            ResetRouteTracking(runtime, null);
            return;
        }

        expedition.Phase = CommanderState.AtLocation;
        commander.State = CommanderState.AtLocation;
        LocationData location = state.FindLocation(expedition.LocationId);

        if (location != null && !location.IsWaypoint)
        {
            location.IsVisibleOnMap = true;
            location.IsDiscovered = true;
            string arrival =
                commander.Name + " прибыл в локацию «" + location.Name + "».";
            batch.Result.Messages.Add(arrival);
            batch.MandatoryNotice = new StrategicModalNotice
            {
                Title = "ОТРЯД ПРИБЫЛ",
                Description = arrival,
                Consequence = location.ExplorationHours > 0 && !location.IsExplored
                    ? "Можно начать исследование, изменить маршрут или приказать возвращаться."
                    : "Можно изменить маршрут или приказать возвращаться."
            };
        }
        else
        {
            string arrival = commander.Name + " достиг выбранной точки и остановился.";
            batch.Result.Messages.Add(arrival);
            ResetRouteTracking(runtime, expedition);
            return;
        }

        batch.Result.HadNotableOccurrence = true;
        batch.RequestAutoPause = true;
        ResetRouteTracking(runtime, expedition);
    }

    private static void AddContinuousArrivalNotice(
        GameState state,
        ContinuousSimulationBatch batch)
    {
        if (state == null || !state.HasActiveExpedition)
            return;

        ExpeditionData expedition = state.ActiveExpedition;
        CommanderData commander = state.FindCommander(expedition.CommanderId);
        LocationData location = state.FindLocation(expedition.LocationId);

        if (commander == null)
            return;

        string description;
        string consequence;

        if (location != null && !location.IsWaypoint)
        {
            location.IsVisibleOnMap = true;
            location.IsDiscovered = true;
            description = commander.Name + " прибыл в локацию «" + location.Name + "».";
            consequence = location.ExplorationHours > 0 && !location.IsExplored
                ? "Можно начать исследование, изменить маршрут или приказать возвращаться."
                : "Можно изменить маршрут или приказать возвращаться.";
        }
        else
        {
            batch.Result.Messages.Add(
                commander.Name + " достиг выбранной точки и остановился.");
            batch.ReportDay = state.Day;
            return;
        }

        batch.Result.Messages.Add(description);
        batch.MandatoryNotice = new StrategicModalNotice
        {
            Title = location != null && !location.IsWaypoint
                ? "ОТРЯД ПРИБЫЛ"
                : "ТОЧКА МАРШРУТА ДОСТИГНУТА",
            Description = description,
            Consequence = consequence
        };
        batch.Result.HadNotableOccurrence = true;
        batch.RequestAutoPause = true;
        batch.ReportDay = state.Day;
    }
}
