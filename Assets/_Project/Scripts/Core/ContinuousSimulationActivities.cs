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

        if (!runtime.CapitalCrisisChecked &&
            runtime.HourOfDay + Epsilon >= runtime.CapitalCrisisCheckHour)
        {
            runtime.CapitalCrisisChecked = true;
            processed = true;
            int incidentCountBefore = batch.Result.NewExpeditionIncidents.Count;
            CapitalCrisisSystem.ResolveForDay(state, state.Day, batch.Result);

            if (batch.Result.NewExpeditionIncidents.Count > incidentCountBefore)
            {
                ExpeditionIncidentOccurrence crisis =
                    batch.Result.NewExpeditionIncidents[
                        batch.Result.NewExpeditionIncidents.Count - 1];
                batch.MandatoryNotice = new DayModalNotice
                {
                    Title = crisis.Title.ToUpper(),
                    Description = crisis.Description,
                    Consequence = crisis.ConsequenceText
                };
                batch.RequestAutoPause = true;
            }
        }

        if (batch.RequestAutoPause)
            return processed;

        if (!runtime.ExpeditionIncidentChecked &&
            runtime.HourOfDay + Epsilon >= runtime.ExpeditionIncidentCheckHour)
        {
            runtime.ExpeditionIncidentChecked = true;
            processed = true;
            bool hadExpedition = state.HasActiveExpedition;
            ExpeditionReturnSnapshotData returnSnapshot =
                CaptureReturnSnapshot(state);

            ExpeditionIncidentSystem.ResolveForDay(
                state,
                state.Day,
                batch.Result);

            EnsureRouteTracking(state, runtime);

            if (ConsumeLegacyTravelPoints(state, runtime, batch))
                batch.RequestAutoPause = true;

            AddReturnNoticeIfNeeded(
                state,
                hadExpedition,
                returnSnapshot,
                batch);

            // Старое фоновое событие «короткая тропа» умеет мгновенно
            // закончить последний сегмент маршрута. В непрерывной модели
            // такое фактическое прибытие тоже является важным результатом
            // и не должно пройти незаметно без автопаузы.
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
            bool hadPending = state.HasPendingExpeditionDecision;

            ExpeditionDecisionSystem.ResolveForDay(
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

    private static void AdvanceOngoingActivities(
        GameState state,
        RuntimeState runtime,
        double gameHours,
        ContinuousSimulationBatch batch)
    {
        AdvanceResearch(state, runtime, gameHours, batch);

        if (batch.RequestAutoPause)
            return;

        AdvanceExpeditionMovement(state, runtime, gameHours, batch);
    }

    private static void AdvanceResearch(
        GameState state,
        RuntimeState runtime,
        double gameHours,
        ContinuousSimulationBatch batch)
    {
        EnsureResearchTracking(state, runtime);

        if (runtime.ResearchHoursRemaining < 0.0 ||
            !state.HasActiveExpedition ||
            !state.ActiveExpedition.IsExplorationInProgress ||
            state.HasPendingExpeditionDecision)
        {
            return;
        }

        runtime.ResearchHoursRemaining = Math.Max(
            0.0,
            runtime.ResearchHoursRemaining - gameHours);

        state.ActiveExpedition.ExplorationDaysRemaining =
            (int)Math.Ceiling(runtime.ResearchHoursRemaining / 24.0);

        if (runtime.ResearchHoursRemaining > Epsilon)
            return;

        ExpeditionData expedition = state.ActiveExpedition;
        LocationData location = state.FindLocation(expedition.LocationId);

        if (location == null || location.IsWaypoint)
        {
            expedition.IsExplorationInProgress = false;
            ResetResearchTracking(runtime);
            return;
        }

        expedition.IsExplorationInProgress = false;
        expedition.ExplorationDaysRemaining = 0;
        location.IsExplored = true;

        state.ArmyGold += location.RewardArmyGold;
        state.ArmySupply += location.RewardArmySupply;

        List<string> rewards = new List<string>();
        if (location.RewardArmyGold > 0)
            rewards.Add("золото +" + location.RewardArmyGold);
        if (location.RewardArmySupply > 0)
            rewards.Add("снабжение +" + location.RewardArmySupply);

        string rewardText = rewards.Count > 0
            ? string.Join(", ", rewards)
            : "добычи нет";

        batch.Result.Messages.Add(
            "Локация «" + location.Name +
            "» исследована. Добыча отряда: " + rewardText + ".");
        batch.Result.ResearchNotice = new DayModalNotice
        {
            Title = "ИССЛЕДОВАНИЕ ЗАВЕРШЕНО",
            Description = "Локация «" + location.Name + "» полностью исследована.",
            Consequence =
                "Добыча отряда: " + rewardText + ".\n" +
                "Ресурсы остаются у отряда до возвращения в столицу."
        };
        batch.Result.HadNotableOccurrence = true;
        batch.RequestAutoPause = true;
        batch.ReportDay = state.Day;
        ResetResearchTracking(runtime);
    }

    private static void AdvanceExpeditionMovement(
        GameState state,
        RuntimeState runtime,
        double gameHours,
        ContinuousSimulationBatch batch)
    {
        if (!state.HasActiveExpedition ||
            state.HasPendingExpeditionDecision)
        {
            return;
        }

        ExpeditionData expedition = state.ActiveExpedition;

        if (expedition.IsExplorationInProgress ||
            (expedition.Phase != CommanderState.TravellingToLocation &&
             expedition.Phase != CommanderState.ReturningToCastle))
        {
            return;
        }

        EnsureRouteTracking(state, runtime);

        // Старый UI до фактического старта мог оставлять командира в замке.
        // Как только непрерывная симуляция действительно начинает движение,
        // каноническое состояние синхронизируется с фазой экспедиции.
        CommanderData movingCommander = state.FindCommander(expedition.CommanderId);
        if (movingCommander != null)
            movingCommander.State = expedition.Phase;

        double movementHours = ConsumeTravelDelay(
            expedition,
            runtime,
            gameHours);

        if (movementHours <= Epsilon)
        {
            UpdateLegacyRemainingDistance(expedition, runtime);
            return;
        }

        double cellsToMove = movementHours * CellsPerGameHour;

        while (cellsToMove > Epsilon && state.HasActiveExpedition)
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

            double segmentRemaining = 1.0 - runtime.SegmentProgress;
            double usedCells = Math.Min(cellsToMove, segmentRemaining);
            runtime.SegmentProgress += usedCells;
            cellsToMove -= usedCells;

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
            UpdateLegacyRemainingDistance(state.ActiveExpedition, runtime);
    }

    private static double ConsumeTravelDelay(
        ExpeditionData expedition,
        RuntimeState runtime,
        double gameHours)
    {
        if (expedition.TravelDelayDays <= 0)
        {
            runtime.DelayHourProgress = 0.0;
            return gameHours;
        }

        double delayRemaining = Math.Max(
            0.0,
            expedition.TravelDelayDays - runtime.DelayHourProgress);
        double usedDelay = Math.Min(gameHours, delayRemaining);
        runtime.DelayHourProgress += usedDelay;

        while (runtime.DelayHourProgress >= 1.0 - Epsilon &&
               expedition.TravelDelayDays > 0)
        {
            expedition.TravelDelayDays--;
            runtime.DelayHourProgress = Math.Max(
                0.0,
                runtime.DelayHourProgress - 1.0);
        }

        if (expedition.TravelDelayDays <= 0)
            runtime.DelayHourProgress = 0.0;

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
                "». Армия немедленно остановилась у неё.",
            OptionA = new ExpeditionDecisionOptionView
            {
                Id = "investigate_discovered_location",
                Label = "Исследовать",
                ConsequencePreview = location.ExplorationDays > 0
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

    private static bool ConsumeLegacyTravelPoints(
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

        expedition.DaysRemaining = 0;
        expedition.LegTotalDays = 0;
        expedition.TravelDelayDays = 0;
        runtime.DelayHourProgress = 0.0;

        if (expedition.Phase == CommanderState.ReturningToCastle)
        {
            int fighterCount = expedition.FighterIds.Count;
            int deliveredGold = Math.Max(0, state.ArmyGold);
            int deliveredFood = Math.Max(0, state.ArmySupply);
            string commanderName = commander.Name;
            string delivered = state.CompleteExpeditionReturn();

            batch.Result.Messages.Add(
                commanderName + " и " + fighterCount +
                " воинов вернулись в столицу. " + delivered);
            batch.Result.ExpeditionReturnNotice = new DayModalNotice
            {
                Title = "ЭКСПЕДИЦИЯ ВЕРНУЛАСЬ",
                Description =
                    commanderName + " и " + fighterCount +
                    " воинов прибыли в столицу. Отряд не расформирован.",
                Consequence =
                    "В столицу передано: золото +" + deliveredGold +
                    ", пища +" + deliveredFood + ".\n" +
                    "Состояние бойцов: без изменений."
            };
            batch.Result.HadNotableOccurrence = true;
            batch.RequestAutoPause = true;
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
            batch.MandatoryNotice = new DayModalNotice
            {
                Title = "АРМИЯ ПРИБЫЛА",
                Description = arrival,
                Consequence = location.ExplorationDays > 0 && !location.IsExplored
                    ? "Можно начать исследование, изменить маршрут или приказать возвращаться."
                    : "Можно изменить маршрут или приказать возвращаться."
            };
        }
        else
        {
            string arrival = commander.Name + " достиг выбранной точки маршрута.";
            batch.Result.Messages.Add(arrival);
            batch.MandatoryNotice = new DayModalNotice
            {
                Title = "ТОЧКА МАРШРУТА ДОСТИГНУТА",
                Description = arrival,
                Consequence = "Армия остановилась и ждёт нового приказа."
            };
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
            consequence = location.ExplorationDays > 0 && !location.IsExplored
                ? "Можно начать исследование, изменить маршрут или приказать возвращаться."
                : "Можно изменить маршрут или приказать возвращаться.";
        }
        else
        {
            description = commander.Name + " достиг выбранной точки маршрута.";
            consequence = "Армия остановилась и ждёт нового приказа.";
        }

        batch.Result.Messages.Add(description);
        batch.MandatoryNotice = new DayModalNotice
        {
            Title = location != null && !location.IsWaypoint
                ? "АРМИЯ ПРИБЫЛА"
                : "ТОЧКА МАРШРУТА ДОСТИГНУТА",
            Description = description,
            Consequence = consequence
        };
        batch.Result.HadNotableOccurrence = true;
        batch.RequestAutoPause = true;
        batch.ReportDay = state.Day;
    }

}
