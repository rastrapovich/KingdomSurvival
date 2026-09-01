using System;
using System.Collections.Generic;

[Serializable]
public class ExpeditionDecisionOptionView
{
    public string Id;
    public string Label;
    public string ConsequencePreview;
}

[Serializable]
public class ExpeditionDecisionOccurrence
{
    public int Id;
    public int Day;
    public string DefinitionId;
    public string Title;
    public string Description;
    public ExpeditionDecisionOptionView OptionA;
    public ExpeditionDecisionOptionView OptionB;
}

public static class ExpeditionDecisionSystem
{
    private const string LocationDiscoveryDefinitionId = "location_discovered";
    private const string InvestigateDiscoveredLocationOptionId =
        "investigate_discovered_location";
    private const string ContinueInterruptedRouteOptionId =
        "continue_interrupted_route";

    private class DecisionOptionDefinition
    {
        public string Id;
        public string Label;
        public int SupplyDelta;
        public double ActivityHours;
        public int RouteShortcutCells;
        public int RequiredSupply;
        public string ActivityName;

        public DecisionOptionDefinition(
            string id,
            string label,
            int supplyDelta,
            double activityHours,
            int routeShortcutCells,
            int requiredSupply,
            string activityName = null)
        {
            Id = id;
            Label = label;
            SupplyDelta = supplyDelta;
            ActivityHours = activityHours;
            RouteShortcutCells = routeShortcutCells;
            RequiredSupply = requiredSupply;
            ActivityName = activityName;
        }
    }

    private class DecisionDefinition
    {
        public string Id;
        public string Title;
        public string Description;
        public DecisionOptionDefinition OptionA;
        public DecisionOptionDefinition OptionB;

        public DecisionDefinition(
            string id,
            string title,
            string description,
            DecisionOptionDefinition optionA,
            DecisionOptionDefinition optionB)
        {
            Id = id;
            Title = title;
            Description = description;
            OptionA = optionA;
            OptionB = optionB;
        }
    }

    private static readonly Random Random = new Random();
    private static int nextOccurrenceId = 1;

    // Временная вероятность для тестового прототипа.
    private const double DecisionChancePerScheduledCheck = 0.5;

    private static readonly List<DecisionDefinition> Definitions =
        new List<DecisionDefinition>
        {
            new DecisionDefinition(
                "unmapped_fork",
                "Развилка без карты",
                "Старая дорога раздваивается. Один путь уходит в длинный безопасный обход, второй режет путь через овраг. Разведчик уверен, что пройти можно, но быстрое продвижение потребует дополнительных припасов.",
                new DecisionOptionDefinition(
                    "shortcut",
                    "Срезать через овраг",
                    -2,
                    0.0,
                    1,
                    2),
                new DecisionOptionDefinition(
                    "safe_road",
                    "Идти безопасной дорогой",
                    0,
                    1.0,
                    0,
                    0,
                    "БЕЗОПАСНЫЙ ОБХОД")),

            new DecisionDefinition(
                "berry_bushes",
                "Ягодные заросли",
                "У дороги обнаружились густые заросли спелых ягод. Их достаточно, чтобы пополнить походный запас, но сбор займёт время и задержит отряд.",
                new DecisionOptionDefinition(
                    "gather_berries",
                    "Остановиться и собрать ягоды",
                    3,
                    3.0,
                    0,
                    0,
                    "СБОР ЯГОД"),
                new DecisionOptionDefinition(
                    "keep_moving",
                    "Не задерживаться",
                    0,
                    0.0,
                    0,
                    0)),

            new DecisionDefinition(
                "hungry_travelers",
                "Голодные путники",
                "На дороге отряд встретил измождённых путников. Они просят еды и обещают взамен показать старую тропу, которой нет на картах. Командир ждёт приказа короля.",
                new DecisionOptionDefinition(
                    "share",
                    "Поделиться припасами",
                    -4,
                    0.0,
                    1,
                    4),
                new DecisionOptionDefinition(
                    "refuse",
                    "Отказать и продолжить путь",
                    0,
                    0.0,
                    0,
                    0))
        };

    public static void ResolveAtScheduledCheck(
        GameState state,
        int finishedDay,
        StrategicSimulationResult result)
    {
        if (TryCreateLocationDiscoveryDecision(
                state,
                finishedDay,
                result))
        {
            return;
        }

        if (!CanGenerateDecision(state))
            return;

        if (Random.NextDouble() >= DecisionChancePerScheduledCheck)
            return;

        List<DecisionDefinition> eligible = GetEligibleDefinitions(state);

        if (eligible.Count == 0)
            return;

        DecisionDefinition definition =
            eligible[Random.Next(0, eligible.Count)];

        ExpeditionDecisionOccurrence occurrence =
            new ExpeditionDecisionOccurrence
            {
                Id = nextOccurrenceId++,
                Day = finishedDay,
                DefinitionId = definition.Id,
                Title = definition.Title,
                Description = definition.Description,
                OptionA = CreateOptionView(definition.OptionA),
                OptionB = CreateOptionView(definition.OptionB)
            };

        state.ActiveExpedition.PendingDecision = occurrence;
        state.ActiveExpedition.UsedDecisionIds.Add(definition.Id);

        result.Messages.Add(
            "<color=#E5BD63>Требуется приказ: «" +
            occurrence.Title +
            "». Экспедиция остановилась и ждёт решения.</color>");
    }

    public static bool CanChooseOption(
        GameState state,
        string optionId)
    {
        if (IsLocationDiscoveryDecision(state))
        {
            if (optionId == ContinueInterruptedRouteOptionId)
                return true;

            if (optionId == InvestigateDiscoveredLocationOptionId)
            {
                LocationData location =
                    state.FindLocation(state.ActiveExpedition.LocationId);

                if (location == null || location.IsWaypoint)
                    return false;

                if (location.ExplorationHours <= 0 || location.IsExplored)
                    return true;

                return
                    state.ArmySupply >= state.ExpeditionSupplyConsumption;
            }

            return false;
        }

        DecisionOptionDefinition option =
            FindPendingOption(state, optionId);

        if (option == null)
            return false;

        return state.ArmySupply >= option.RequiredSupply;
    }

    public static bool TryApplyChoice(
        GameState state,
        string optionId,
        out string resultMessage)
    {
        resultMessage = "Не удалось выполнить приказ.";

        if (!state.HasPendingExpeditionDecision)
            return false;

        if (IsLocationDiscoveryDecision(state))
        {
            return TryApplyLocationDiscoveryChoice(
                state,
                optionId,
                out resultMessage);
        }

        ExpeditionDecisionOccurrence occurrence =
            state.ActiveExpedition.PendingDecision;

        DecisionDefinition definition =
            FindDefinition(occurrence.DefinitionId);

        if (definition == null)
            return false;

        DecisionOptionDefinition option =
            definition.OptionA.Id == optionId
                ? definition.OptionA
                : definition.OptionB.Id == optionId
                    ? definition.OptionB
                    : null;

        if (option == null)
            return false;

        if (state.ArmySupply < option.RequiredSupply)
        {
            resultMessage =
                "Для этого приказа не хватает снабжения. Требуется: " +
                option.RequiredSupply + ".";
            return false;
        }

        if (option.ActivityHours > 0.0)
        {
            state.ActiveExpedition.PendingDecision = null;
            string activityMessage;
            bool started = state.TryStartRoadActivity(
                "decision:" + definition.Id + ":" + option.Id,
                GetActivityDisplayName(option),
                option.ActivityHours,
                0,
                Math.Max(0, option.SupplyDelta),
                out activityMessage);

            if (!started)
            {
                state.ActiveExpedition.PendingDecision = occurrence;
                resultMessage = activityMessage;
                return false;
            }

            resultMessage =
                "Приказ по событию «" + occurrence.Title + "»: " +
                option.Label + ". " + activityMessage;
            return true;
        }

        List<string> consequences = new List<string>();

        int actualSupplyDelta =
            ApplySupplyDelta(state, option.SupplyDelta);
        string arrivalText;
        int actualShortcutCells =
            ApplyRouteShortcut(
                state,
                option.RouteShortcutCells,
                out arrivalText);

        if (option.SupplyDelta != 0)
            consequences.Add(
                FormatSupplyConsequence(actualSupplyDelta));

        if (option.RouteShortcutCells > 0)
            consequences.Add(
                FormatRouteShortcutConsequence(actualShortcutCells));

        if (!string.IsNullOrWhiteSpace(arrivalText))
            consequences.Add(arrivalText);

        if (consequences.Count == 0)
            consequences.Add("Механических изменений нет.");

        // ActiveExpedition остаётся доступной даже после фактического возвращения,
        // поэтому pending можно безопасно очистить и после CompleteExpeditionReturn().
        state.ActiveExpedition.PendingDecision = null;

        resultMessage =
            "Приказ по событию «" + occurrence.Title + "»: " +
            option.Label + ". " +
            string.Join(" ", consequences);

        return true;
    }

    private static bool TryCreateLocationDiscoveryDecision(
        GameState state,
        int finishedDay,
        StrategicSimulationResult result)
    {
        if (!state.HasActiveExpedition ||
            state.HasPendingExpeditionDecision ||
            state.ActiveExpedition.HasTimedActivity)
        {
            return false;
        }

        ExpeditionData expedition = state.ActiveExpedition;
        bool arrivedAtWaypoint =
            expedition.Phase == CommanderState.AtLocation &&
            state.FindLocation(expedition.LocationId) != null &&
            state.FindLocation(expedition.LocationId).IsWaypoint;

        if (arrivedAtWaypoint)
        {
            result.Messages.RemoveAll(
                message => message.Contains("Точка маршрута"));
        }

        if (expedition.LastTravelPoints == null ||
            expedition.LastTravelPoints.Count == 0)
        {
            return false;
        }

        LocationData location =
            state.FindFirstHiddenLocationAlongLastTravel();

        if (location == null)
        {
            expedition.LastTravelPoints.Clear();

            if (arrivedAtWaypoint)
            {
                result.Messages.Add(
                    "Армия достигла выбранной точки маршрута. " +
                    "Новый приказ можно отдать кликом по карте.");
            }

            return false;
        }

        string stopMessage;
        if (!state.StopAtDiscoveredLocation(
                location,
                out stopMessage))
        {
            expedition.LastTravelPoints.Clear();
            return false;
        }

        ExpeditionDecisionOccurrence occurrence =
            new ExpeditionDecisionOccurrence
            {
                Id = nextOccurrenceId++,
                Day = finishedDay,
                DefinitionId = LocationDiscoveryDefinitionId,
                Title = "Обнаружена локация «" + location.Name + "»",
                Description =
                    "Вы обнаружили локацию «" + location.Name +
                    "». Армия немедленно остановилась у неё.",
                OptionA = new ExpeditionDecisionOptionView
                {
                    Id = InvestigateDiscoveredLocationOptionId,
                    Label = "Исследовать",
                    ConsequencePreview =
                        location.ExplorationHours > 0
                            ? "Начать исследование локации"
                            : "Осмотреть найденное место"
                },
                OptionB = new ExpeditionDecisionOptionView
                {
                    Id = ContinueInterruptedRouteOptionId,
                    Label = "Продолжить маршрут",
                    ConsequencePreview =
                        "Вернуться к прерванной цели"
                }
            };

        state.ActiveExpedition.PendingDecision = occurrence;

        result.Messages.Add(
            "<color=#E5BD63>" + stopMessage +
            " Требуется приказ: исследовать находку или продолжить прежний маршрут.</color>");
        result.HadNotableOccurrence = true;
        return true;
    }

    private static bool TryApplyLocationDiscoveryChoice(
        GameState state,
        string optionId,
        out string resultMessage)
    {
        resultMessage = "Не удалось выполнить приказ по обнаруженной локации.";

        ExpeditionData expedition = state.ActiveExpedition;
        LocationData location =
            state.FindLocation(expedition.LocationId);

        if (location == null || location.IsWaypoint)
        {
            resultMessage =
                "Данные обнаруженной локации потеряны.";
            return false;
        }

        if (optionId == ContinueInterruptedRouteOptionId)
        {
            expedition.PendingDecision = null;

            if (state.TryResumeInterruptedRoute(out resultMessage))
                return true;

            // Если возобновить уже нечего, оставляем отряд у найденной локации,
            // но обязательное решение считаем обработанным.
            expedition.Phase = CommanderState.AtLocation;
            CommanderData commander =
                state.FindCommander(expedition.CommanderId);
            if (commander != null)
                commander.State = CommanderState.AtLocation;

            resultMessage =
                "Прежняя цель уже недоступна. Армия остаётся у локации «" +
                location.Name + "».";
            return true;
        }

        if (optionId != InvestigateDiscoveredLocationOptionId)
            return false;

        if (location.ExplorationHours > 0 &&
            !location.IsExplored &&
            state.ArmySupply < state.ExpeditionSupplyConsumption)
        {
            resultMessage =
                "Для исследования нужен достаточный запас снабжения. Требуется: " +
                state.ExpeditionSupplyConsumption + ".";
            return false;
        }

        expedition.PendingDecision = null;
        expedition.HasInterruptedRoute = false;

        if (location.ExplorationHours > 0 && !location.IsExplored)
        {
            string researchMessage;

            if (state.TryStartLocationResearch(out researchMessage))
            {
                resultMessage =
                    "Армия остаётся у обнаруженной локации. " +
                    researchMessage;
                return true;
            }

            resultMessage = researchMessage;
            return false;
        }

        resultMessage = location.IsExplored
            ? "Локация «" + location.Name +
              "» уже исследована. Армия остаётся на месте."
            : "Армия остановилась у локации «" + location.Name +
              "». Полноценное исследование этой локации пока не реализовано.";

        return true;
    }

    private static bool IsLocationDiscoveryDecision(GameState state)
    {
        return
            state.HasPendingExpeditionDecision &&
            state.ActiveExpedition.PendingDecision.DefinitionId ==
            LocationDiscoveryDefinitionId;
    }

    private static bool CanGenerateDecision(GameState state)
    {
        if (!state.HasActiveExpedition ||
            state.HasPendingExpeditionDecision ||
            state.ActiveExpedition.HasTimedActivity)
        {
            return false;
        }

        ExpeditionData expedition = state.ActiveExpedition;

        if (expedition.RemainingRouteCells <= 0)
            return false;

        return
            expedition.Phase == CommanderState.TravellingToLocation ||
            expedition.Phase == CommanderState.ReturningToCastle;
    }

    private static List<DecisionDefinition> GetEligibleDefinitions(
        GameState state)
    {
        List<DecisionDefinition> eligible =
            new List<DecisionDefinition>();
        ExpeditionData expedition = state.ActiveExpedition;

        foreach (DecisionDefinition definition in Definitions)
        {
            if (!expedition.UsedDecisionIds.Contains(definition.Id))
                eligible.Add(definition);
        }

        return eligible;
    }

    private static ExpeditionDecisionOptionView CreateOptionView(
        DecisionOptionDefinition option)
    {
        return new ExpeditionDecisionOptionView
        {
            Id = option.Id,
            Label = option.Label,
            ConsequencePreview =
                BuildConsequencePreview(option)
        };
    }

    private static string BuildConsequencePreview(
        DecisionOptionDefinition option)
    {
        List<string> parts = new List<string>();

        if (option.SupplyDelta > 0)
            parts.Add("снабжение +" + option.SupplyDelta);
        else if (option.SupplyDelta < 0)
            parts.Add("снабжение " + option.SupplyDelta);

        if (option.RouteShortcutCells > 0)
            parts.Add("маршрут -" + option.RouteShortcutCells + " клетка");

        if (option.ActivityHours > 0.0)
            parts.Add(
                "время: " +
                ContinuousExpeditionCommands.FormatHours(option.ActivityHours));

        return parts.Count > 0
            ? string.Join(", ", parts)
            : "без механических изменений";
    }

    private static DecisionDefinition FindDefinition(
        string definitionId)
    {
        foreach (DecisionDefinition definition in Definitions)
        {
            if (definition.Id == definitionId)
                return definition;
        }

        return null;
    }

    private static DecisionOptionDefinition FindPendingOption(
        GameState state,
        string optionId)
    {
        if (!state.HasPendingExpeditionDecision)
            return null;

        DecisionDefinition definition =
            FindDefinition(
                state.ActiveExpedition.PendingDecision.DefinitionId);

        if (definition == null)
            return null;

        if (definition.OptionA.Id == optionId)
            return definition.OptionA;

        if (definition.OptionB.Id == optionId)
            return definition.OptionB;

        return null;
    }

    private static int ApplySupplyDelta(
        GameState state,
        int requestedDelta)
    {
        if (requestedDelta >= 0)
        {
            state.ArmySupply += requestedDelta;
            return requestedDelta;
        }

        int requestedLoss = -requestedDelta;
        int actualLoss =
            Math.Min(state.ArmySupply, requestedLoss);
        state.ArmySupply -= actualLoss;
        return -actualLoss;
    }

    private static int ApplyRouteShortcut(
        GameState state,
        int requestedCells,
        out string arrivalText)
    {
        arrivalText = string.Empty;
        ExpeditionData expedition =
            state.ActiveExpedition;

        if (expedition == null ||
            expedition.RemainingRouteCells <= 0)
        {
            return 0;
        }

        int reduction =
            Math.Min(
                expedition.RemainingRouteCells,
                Math.Max(0, requestedCells));

        WorldMapNavigation.AdvanceRouteByCells(
            expedition,
            reduction);

        if (reduction > 0 &&
            expedition.RemainingRouteCells == 0)
        {
            arrivalText =
                ResolveArrival(state, expedition);
        }

        return reduction;
    }

    private static string ResolveArrival(
        GameState state,
        ExpeditionData expedition)
    {
        CommanderData commander =
            state.FindCommander(expedition.CommanderId);

        if (commander == null)
            return string.Empty;

        if (expedition.Phase ==
            CommanderState.TravellingToLocation)
        {
            expedition.Phase =
                CommanderState.AtLocation;
            commander.State =
                CommanderState.AtLocation;
            return "Экспедиция достигла цели.";
        }

        if (expedition.Phase ==
            CommanderState.ReturningToCastle)
        {
            string deliveredResources =
                state.CompleteExpeditionReturn();
            return
                "Армия вернулась в столицу. " +
                deliveredResources;
        }

        return string.Empty;
    }

    private static string FormatSupplyConsequence(int delta)
    {
        if (delta > 0)
            return "Снабжение +" + delta + ".";

        if (delta < 0)
            return "Снабжение " + delta + ".";

        return "Снабжение не изменилось.";
    }

    private static string GetActivityDisplayName(
        DecisionOptionDefinition option)
    {
        return string.IsNullOrWhiteSpace(option.ActivityName)
            ? option.Label.ToUpperInvariant()
            : option.ActivityName;
    }

    private static string FormatRouteShortcutConsequence(int cells)
    {
        if (cells > 0)
            return "Маршрут сокращён на " + cells + " клетку.";

        return "Длина пути не изменилась.";
    }
}
