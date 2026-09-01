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
        if (gameState == null || dayLabel == null || timeToggleButton == null)
            return;

        ContinuousClockSnapshot clock =
            ContinuousSimulationSystem.GetClock(gameState);

        dayLabel.text =
            "День " + gameState.Day + " · " +
            ContinuousSimulationSystem.FormatClock(clock.HourOfDay);

        timeToggleButton.text = clock.IsPaused ? "ПУСК" : "ПАУЗА";
        timeToggleButton.tooltip = HasBlockingModalWork()
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
            if (expedition.HasTimedActivity)
            {
                worldMapArmyMarkerLabel.text =
                    ContinuousExpeditionCommands.FormatHours(
                        expedition.ActiveActivity.RemainingHours);
            }
            else if (expedition.Phase == CommanderState.TravellingToLocation ||
                expedition.Phase == CommanderState.ReturningToCastle)
            {
                worldMapArmyMarkerLabel.text =
                    ContinuousExpeditionCommands.FormatHours(
                        ContinuousSimulationSystem.GetTravelHoursRemaining(gameState));
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
        else if (expedition.IsLocationResearchInProgress)
        {
            stateText = "исследует локацию";
            timingText =
                "До завершения исследования: " +
                ContinuousExpeditionCommands.FormatHours(
                    ContinuousSimulationSystem.GetResearchHoursRemaining(gameState));
        }
        else if (expedition.IsRoadStopInProgress)
        {
            stateText = expedition.ActiveActivity.DisplayName.ToLowerInvariant();
            timingText =
                "До продолжения маршрута: " +
                ContinuousExpeditionCommands.FormatHours(
                    expedition.ActiveActivity.RemainingHours);
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

        if (!isGameOver &&
            !gameState.HasPendingExpeditionDecision &&
            !expedition.IsLocationResearchInProgress)
        {
            if (gameState.CanCancelPreparedExpedition)
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

        if (expedition.IsLocationResearchInProgress)
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

            string researchText = location.ExplorationHours > 0
                ? "Исследование: " +
                  ContinuousExpeditionCommands.FormatHours(location.ExplorationHours)
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

        return text;
    }

    private static string NormalizeContinuousDecisionPreview(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return text;
    }
}
