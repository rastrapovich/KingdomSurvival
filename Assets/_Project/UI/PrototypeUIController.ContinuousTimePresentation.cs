using System;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private void RefreshContinuousTimeUi(bool refreshPanels)
    {
        RefreshContinuousClockOnly();
        RefreshContinuousMapMarker();

        if (refreshPanels)
        {
            RefreshContinuousExpeditionTexts();
            RefreshPersistentCommanderNavigationState();
        }
    }

    private void RefreshContinuousClockOnly()
    {
        if (gameState == null || dayLabel == null || endDayButton == null)
            return;

        ContinuousClockSnapshot clock =
            ContinuousSimulationSystem.GetClock(gameState);

        dayLabel.text =
            "День " + gameState.Day + " · " +
            ContinuousSimulationSystem.FormatClock(clock.HourOfDay);

        endDayButton.text = clock.IsPaused ? "ПУСК" : "ПАУЗА";
        endDayButton.tooltip = HasBlockingModalWork()
            ? "Сначала закройте обязательное событие"
            : clock.IsPaused
                ? "Продолжить течение времени"
                : "Поставить время на паузу";

        if (continuousSpeedButton != null)
        {
            bool fast = clock.SpeedMultiplier ==
                ContinuousSimulationSystem.FastSpeedMultiplier;
            continuousSpeedButton.text = fast ? "×3 ✓" : "×3";
            continuousSpeedButton.tooltip = fast
                ? "Ускорение ×3 включено. Нажмите для обычной скорости."
                : "Ускорить течение времени и движение армии в 3 раза.";
            continuousSpeedButton.style.backgroundColor = fast
                ? (Color)new Color32(101, 77, 35, 255)
                : (Color)new Color32(61, 55, 40, 255);
            continuousSpeedButton.SetEnabled(!isGameOver);
        }

        // Старые прототипные события ещё хранят часть эффектов как +/- день
        // пути. В непрерывном runtime они трактуются как краткая задержка или
        // изменение маршрута, поэтому пользовательский preview нормализуем.
        if (openedDecision != null)
        {
            if (decisionOptionAButton != null)
                decisionOptionAButton.text = NormalizeContinuousDecisionPreview(
                    decisionOptionAButton.text);
            if (decisionOptionBButton != null)
                decisionOptionBButton.text = NormalizeContinuousDecisionPreview(
                    decisionOptionBButton.text);
            if (incidentModalConsequence != null)
                incidentModalConsequence.text =
                    "Требуется приказ. Стратегическое время автоматически поставлено на паузу.";
        }

        RefreshContinuousLocationInspectionCard();
    }

    private void RefreshContinuousMapMarker()
    {
        if (gameState == null || worldMapArmyMarker == null)
            return;

        RefreshWorldMapArmyMarker();

        if (gameState.HasActiveExpedition &&
            worldMapArmyMarkerLabel != null)
        {
            ExpeditionData expedition = gameState.ActiveExpedition;
            if (expedition.Phase == CommanderState.TravellingToLocation ||
                expedition.Phase == CommanderState.ReturningToCastle)
            {
                worldMapArmyMarkerLabel.text =
                    ContinuousExpeditionCommands.FormatHours(
                        ContinuousSimulationSystem.GetTravelHoursRemaining(gameState));
            }
            else if (expedition.IsExplorationInProgress)
            {
                worldMapArmyMarkerLabel.text =
                    ContinuousExpeditionCommands.FormatHours(
                        ContinuousSimulationSystem.GetResearchHoursRemaining(gameState));
            }
            else
            {
                worldMapArmyMarkerLabel.text = "на месте";
            }
        }
    }

    private void RefreshContinuousExpeditionTexts()
    {
        if (gameState == null || !gameState.HasActiveExpedition)
            return;

        ExpeditionData expedition = gameState.ActiveExpedition;
        LocationData location = gameState.FindLocation(expedition.LocationId);
        CommanderData commander = gameState.FindCommander(expedition.CommanderId);
        string targetName = location != null
            ? location.TravelTargetName
            : "точка маршрута";

        string stateText;
        string timingText;

        if (gameState.HasPendingExpeditionDecision)
        {
            stateText = "ожидает приказа";
            timingText = "Время остановлено до решения.";
        }
        else if (expedition.IsExplorationInProgress)
        {
            stateText = "исследует локацию";
            timingText =
                "До завершения исследования: " +
                ContinuousExpeditionCommands.FormatHours(
                    ContinuousSimulationSystem.GetResearchHoursRemaining(gameState));
        }
        else if (expedition.Phase == CommanderState.TravellingToLocation)
        {
            stateText = "в пути";
            timingText =
                "Расчётное время до цели: " +
                ContinuousExpeditionCommands.FormatHours(
                    ContinuousSimulationSystem.GetTravelHoursRemaining(gameState));
        }
        else if (expedition.Phase == CommanderState.ReturningToCastle)
        {
            stateText = "возвращается";
            timingText =
                "Расчётное время до столицы: " +
                ContinuousExpeditionCommands.FormatHours(
                    ContinuousSimulationSystem.GetTravelHoursRemaining(gameState));
        }
        else
        {
            stateText = "действует в локации";
            timingText = "Армия находится на месте.";
        }

        expeditionStatusLabel.text =
            "Активная экспедиция: " +
            (commander != null ? commander.Name : "Командир") +
            " · " + targetName + " · " + stateText;

        activeExpeditionDetails.text =
            "Командир: " + (commander != null ? commander.Name : "—") + "\n" +
            "Бойцы: " + GetFighterNames(expedition.FighterIds) + "\n" +
            "Цель: " + targetName + "\n" +
            "Состояние: " + stateText + "\n" +
            timingText;

        bool startedMoving =
            ContinuousSimulationSystem.HasExpeditionStartedMoving(gameState);

        if (!isGameOver &&
            !gameState.HasPendingExpeditionDecision &&
            !expedition.IsExplorationInProgress)
        {
            if (!startedMoving)
            {
                returnExpeditionButton.text = "Отменить отправку";
                returnExpeditionButton.SetEnabled(true);
            }
            else if (expedition.Phase == CommanderState.ReturningToCastle)
            {
                returnExpeditionButton.text = "Возвращение уже приказано";
                returnExpeditionButton.SetEnabled(false);
            }
            else
            {
                returnExpeditionButton.text = "Приказать возвращаться";
                returnExpeditionButton.SetEnabled(true);
            }
        }

        if (expedition.IsExplorationInProgress)
        {
            researchExpeditionButton.text =
                "ИССЛЕДОВАНИЕ · " +
                ContinuousExpeditionCommands.FormatHours(
                    ContinuousSimulationSystem.GetResearchHoursRemaining(gameState));
        }
    }

    private void RefreshContinuousLocationInspectionCard()
    {
        if (worldMapLocationCard == null ||
            worldMapLocationCardTitle == null ||
            worldMapLocationCardDetails == null ||
            gameState == null ||
            worldMapLocationCard.resolvedStyle.display != DisplayStyle.Flex)
        {
            return;
        }

        foreach (LocationData location in gameState.Locations)
        {
            if (location == null || location.IsWaypoint || !location.IsVisibleOnMap)
                continue;

            if (location.Name.ToUpper() != worldMapLocationCardTitle.text)
                continue;

            string researchText = location.ExplorationDays > 0
                ? "Исследование: " +
                  ContinuousExpeditionCommands.FormatHours(location.ExplorationDays * 24.0)
                : "Исследование: пока не реализовано";

            worldMapLocationCardDetails.text =
                "Регион: " + location.RegionName + "\n" +
                "Угроза: " + location.Threat + "\n" +
                GetWorldMapLocationStatus(location) + "\n" +
                researchText + "\n\n" +
                "ЛКМ по маркеру — отдать приказ двигаться сюда.";
            break;
        }
    }

    private static string NormalizeContinuousReportText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return text
            .Replace(
                "До завершения текущего дня приказ можно отменить или изменить.",
                "До начала фактического движения приказ можно отменить или изменить.")
            .Replace(
                "День не завершён.",
                "Течение времени не продвинулось.")
            .Replace(
                "Текущий день ещё не завершён. Нажатие на столицу отменяет отправку.",
                "Армия ещё не начала движение. Нажатие на столицу отменяет отправку.")
            .Replace("Осталось дней пути:", "Осталось клеток маршрута:")
            .Replace("До столицы осталось дней:", "До столицы осталось клеток:")
            .Replace("Оставшийся путь +1 день.", "Маршрут задержан примерно на 1 игровой час.")
            .Replace("Оставшийся путь -1 день.", "Маршрут сокращён примерно на одну клетку.")
            .Replace("путь +1 день", "задержка ~1 игровой час")
            .Replace("путь -1 день", "путь -1 клетка");
    }

    private static string NormalizeContinuousDecisionPreview(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return text
            .Replace("путь +1 день", "задержка ~1 игровой час")
            .Replace("путь -1 день", "путь -1 клетка")
            .Replace("путь +1 день.", "задержка ~1 игровой час.")
            .Replace("путь -1 день.", "путь -1 клетка.");
    }
}
