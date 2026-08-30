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
    private class DecisionOptionDefinition
    {
        public string Id;
        public string Label;
        public int SupplyDelta;
        public int TravelDelta;
        public int RequiredSupply;

        public DecisionOptionDefinition(
            string id,
            string label,
            int supplyDelta,
            int travelDelta,
            int requiredSupply)
        {
            Id = id;
            Label = label;
            SupplyDelta = supplyDelta;
            TravelDelta = travelDelta;
            RequiredSupply = requiredSupply;
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
    private const double DecisionChancePerTravelDay = 0.5;

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
                    -1,
                    2),
                new DecisionOptionDefinition(
                    "safe_road",
                    "Идти безопасной дорогой",
                    0,
                    1,
                    0)),

            new DecisionDefinition(
                "berry_bushes",
                "Ягодные заросли",
                "У дороги обнаружились густые заросли спелых ягод. Их достаточно, чтобы пополнить походный запас, но сбор займёт время и задержит отряд.",
                new DecisionOptionDefinition(
                    "gather_berries",
                    "Остановиться и собрать ягоды",
                    3,
                    1,
                    0),
                new DecisionOptionDefinition(
                    "keep_moving",
                    "Не задерживаться",
                    0,
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
                    -1,
                    4),
                new DecisionOptionDefinition(
                    "refuse",
                    "Отказать и продолжить путь",
                    0,
                    0,
                    0))
        };

    public static void ResolveForDay(
        GameState state,
        int finishedDay,
        DayResolutionResult result)
    {
        if (!CanGenerateDecision(state))
            return;

        if (Random.NextDouble() >= DecisionChancePerTravelDay)
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
        DecisionOptionDefinition option = FindPendingOption(state, optionId);

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

        ExpeditionDecisionOccurrence occurrence =
            state.ActiveExpedition.PendingDecision;

        DecisionDefinition definition = FindDefinition(occurrence.DefinitionId);

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

        List<string> consequences = new List<string>();

        int actualSupplyDelta = ApplySupplyDelta(state, option.SupplyDelta);
        int actualTravelDelta = ApplyTravelDelta(state, option.TravelDelta);

        if (option.SupplyDelta != 0)
            consequences.Add(FormatSupplyConsequence(actualSupplyDelta));

        if (option.TravelDelta != 0)
            consequences.Add(FormatTravelConsequence(actualTravelDelta));

        string arrivalText = GetArrivalText(state, actualTravelDelta);

        if (!string.IsNullOrWhiteSpace(arrivalText))
            consequences.Add(arrivalText);

        if (consequences.Count == 0)
            consequences.Add("Механических изменений нет.");

        state.ActiveExpedition.PendingDecision = null;

        resultMessage =
            "Приказ по событию «" + occurrence.Title + "»: " +
            option.Label + ". " +
            string.Join(" ", consequences);

        return true;
    }

    private static bool CanGenerateDecision(GameState state)
    {
        if (!state.HasActiveExpedition || state.HasPendingExpeditionDecision)
            return false;

        ExpeditionData expedition = state.ActiveExpedition;

        if (expedition.DaysRemaining <= 0)
            return false;

        return
            expedition.Phase == CommanderState.TravellingToLocation ||
            expedition.Phase == CommanderState.ReturningToCastle;
    }

    private static List<DecisionDefinition> GetEligibleDefinitions(GameState state)
    {
        List<DecisionDefinition> eligible = new List<DecisionDefinition>();
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
            ConsequencePreview = BuildConsequencePreview(option)
        };
    }

    private static string BuildConsequencePreview(DecisionOptionDefinition option)
    {
        List<string> parts = new List<string>();

        if (option.SupplyDelta > 0)
            parts.Add("снабжение +" + option.SupplyDelta);
        else if (option.SupplyDelta < 0)
            parts.Add("снабжение " + option.SupplyDelta);

        if (option.TravelDelta > 0)
            parts.Add("путь +" + option.TravelDelta + " день");
        else if (option.TravelDelta < 0)
            parts.Add("путь " + option.TravelDelta + " день");

        return parts.Count > 0
            ? string.Join(", ", parts)
            : "без механических изменений";
    }

    private static DecisionDefinition FindDefinition(string definitionId)
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
            FindDefinition(state.ActiveExpedition.PendingDecision.DefinitionId);

        if (definition == null)
            return null;

        if (definition.OptionA.Id == optionId)
            return definition.OptionA;

        if (definition.OptionB.Id == optionId)
            return definition.OptionB;

        return null;
    }

    private static int ApplySupplyDelta(GameState state, int requestedDelta)
    {
        if (requestedDelta >= 0)
        {
            state.ArmySupply += requestedDelta;
            return requestedDelta;
        }

        int requestedLoss = -requestedDelta;
        int actualLoss = Math.Min(state.ArmySupply, requestedLoss);
        state.ArmySupply -= actualLoss;
        return -actualLoss;
    }

    private static int ApplyTravelDelta(GameState state, int requestedDelta)
    {
        ExpeditionData expedition = state.ActiveExpedition;

        if (expedition == null || expedition.DaysRemaining <= 0)
            return 0;

        if (requestedDelta >= 0)
        {
            expedition.DaysRemaining += requestedDelta;
            return requestedDelta;
        }

        int reduction = Math.Min(expedition.DaysRemaining, -requestedDelta);
        expedition.DaysRemaining -= reduction;

        if (reduction > 0 && expedition.DaysRemaining == 0)
            ResolveArrival(state, expedition);

        return -reduction;
    }

    private static void ResolveArrival(
        GameState state,
        ExpeditionData expedition)
    {
        CommanderData commander = state.FindCommander(expedition.CommanderId);

        if (commander == null)
            return;

        if (expedition.Phase == CommanderState.TravellingToLocation)
        {
            expedition.Phase = CommanderState.AtLocation;
            commander.State = CommanderState.AtLocation;
            return;
        }

        if (expedition.Phase == CommanderState.ReturningToCastle)
        {
            commander.State = CommanderState.InCastle;
            expedition.IsActive = false;
        }
    }

    private static string GetArrivalText(GameState state, int travelDelta)
    {
        if (travelDelta >= 0 || state.ActiveExpedition == null)
            return string.Empty;

        ExpeditionData expedition = state.ActiveExpedition;

        if (expedition.DaysRemaining > 0)
            return string.Empty;

        if (expedition.Phase == CommanderState.AtLocation)
            return "Экспедиция достигла цели.";

        if (!expedition.IsActive &&
            expedition.Phase == CommanderState.ReturningToCastle)
        {
            return "Армия вернулась в столицу.";
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

    private static string FormatTravelConsequence(int delta)
    {
        if (delta > 0)
            return "Оставшийся путь +" + delta + " день.";

        if (delta < 0)
            return "Оставшийся путь " + delta + " день.";

        return "Длина пути не изменилась.";
    }
}
