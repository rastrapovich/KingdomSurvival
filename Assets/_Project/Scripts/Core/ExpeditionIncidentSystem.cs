using System;
using System.Collections.Generic;

public enum ExpeditionIncidentTone
{
    Positive,
    Negative,
    Mixed
}

[Serializable]
public class ExpeditionIncidentOccurrence
{
    public int Id;
    public int Day;
    public string Title;
    public string Description;
    public string ConsequenceText;
    public ExpeditionIncidentTone Tone;
}

public static class ExpeditionIncidentSystem
{
    private enum IncidentEffectKind
    {
        Supply,
        Travel,
        SupplyAndTravel
    }

    private class IncidentDefinition
    {
        public string Id;
        public string Title;
        public string Description;
        public ExpeditionIncidentTone Tone;
        public IncidentEffectKind EffectKind;
        public int SupplyDelta;
        public int TravelDelta;

        public IncidentDefinition(
            string id,
            string title,
            string description,
            ExpeditionIncidentTone tone,
            IncidentEffectKind effectKind,
            int supplyDelta,
            int travelDelta)
        {
            Id = id;
            Title = title;
            Description = description;
            Tone = tone;
            EffectKind = effectKind;
            SupplyDelta = supplyDelta;
            TravelDelta = travelDelta;
        }
    }

    private static readonly Random Random = new Random();
    private static int nextOccurrenceId = 1;

    private static readonly List<IncidentDefinition> Definitions =
        new List<IncidentDefinition>
        {
            new IncidentDefinition(
                "rats",
                "Крысы в припасах",
                "Ночью часовой слышал возню возле мешков. Он решил, что это ветер. Ветер оказался очень хорошо откормленным.",
                ExpeditionIncidentTone.Negative,
                IncidentEffectKind.Supply,
                -2,
                0),

            new IncidentDefinition(
                "hunt",
                "Удачная охота",
                "Охотники вернулись в лагерь не с рассказом о добыче, а с самой добычей. Сегодня это редкая роскошь.",
                ExpeditionIncidentTone.Positive,
                IncidentEffectKind.Supply,
                3,
                0),

            new IncidentDefinition(
                "bad_water",
                "Испорченная вода",
                "Несколько бурдюков пахли так, будто внутри уже успела возникнуть и погибнуть отдельная цивилизация. Воду пришлось вылить.",
                ExpeditionIncidentTone.Negative,
                IncidentEffectKind.Supply,
                -2,
                0),

            new IncidentDefinition(
                "cache",
                "Заброшенный схрон",
                "Под старым навесом нашли хорошо укрытый запас еды. Хозяин так и не вернулся за ним, и отряд решил не спорить с судьбой.",
                ExpeditionIncidentTone.Positive,
                IncidentEffectKind.Supply,
                4,
                0),

            new IncidentDefinition(
                "washed_road",
                "Размытая дорога",
                "Дорога впереди превратилась в вязкое месиво. Пришлось искать обход и часть дневного продвижения оказалась потеряна.",
                ExpeditionIncidentTone.Negative,
                IncidentEffectKind.Travel,
                0,
                1),

            new IncidentDefinition(
                "short_path",
                "Короткая тропа",
                "Разведчик заметил старую тропу между холмами. Она выглядит неприятно, зато действительно сокращает путь.",
                ExpeditionIncidentTone.Positive,
                IncidentEffectKind.Travel,
                0,
                -1),

            new IncidentDefinition(
                "cold_rain",
                "Холодный ливень",
                "Ливень промочил часть запасов и заставил отряд искать более длинный, но безопасный проход.",
                ExpeditionIncidentTone.Mixed,
                IncidentEffectKind.SupplyAndTravel,
                -1,
                1),

            new IncidentDefinition(
                "torn_bags",
                "Порванные мешки",
                "Когда пропажу заметили, дорожка из крупы уже тянулась далеко назад. Возвращаться за ней никто не предложил.",
                ExpeditionIncidentTone.Negative,
                IncidentEffectKind.Supply,
                -3,
                0),

            new IncidentDefinition(
                "fishing",
                "Рыбное место",
                "Стоянка у реки неожиданно оказалась полезнее запланированного. Вечером котлы были полнее обычного.",
                ExpeditionIncidentTone.Positive,
                IncidentEffectKind.Supply,
                2,
                0),

            new IncidentDefinition(
                "good_crossing",
                "Удачный переход",
                "Местность впереди оказалась проще, чем обещали старые карты. Отряд прошёл заметно дальше обычного.",
                ExpeditionIncidentTone.Positive,
                IncidentEffectKind.Travel,
                0,
                -1)
        };

    public static void ResolveForDay(
        GameState state,
        int finishedDay,
        DayResolutionResult result)
    {
        // Если армия в этот день уже вернулась в столицу,
        // новых походных происшествий после возвращения нет.
        if (!state.HasActiveExpedition)
            return;

        int requestedIncidentCount = Random.Next(0, 3);

        if (requestedIncidentCount == 0)
            return;

        List<string> usedDefinitionIds = new List<string>();
        List<ExpeditionIncidentOccurrence> created =
            new List<ExpeditionIncidentOccurrence>();

        for (int i = 0; i < requestedIncidentCount; i++)
        {
            List<IncidentDefinition> eligible =
                GetEligibleDefinitions(state, usedDefinitionIds);

            if (eligible.Count == 0)
                break;

            IncidentDefinition definition =
                eligible[Random.Next(0, eligible.Count)];

            usedDefinitionIds.Add(definition.Id);

            ExpeditionIncidentOccurrence occurrence =
                ApplyIncident(state, definition, finishedDay);

            created.Add(occurrence);
            result.NewExpeditionIncidents.Add(occurrence);
        }

        if (created.Count == 0)
            return;

        List<string> summaryLines = new List<string>();

        foreach (ExpeditionIncidentOccurrence occurrence in created)
        {
            summaryLines.Add(
                "• " + occurrence.Title + " — " + occurrence.ConsequenceText);
        }

        result.Messages.Add(
            "Экспедиция: " + created.Count +
            GetIncidentWord(created.Count) +
            "\n" +
            string.Join("\n", summaryLines));
    }

    private static List<IncidentDefinition> GetEligibleDefinitions(
        GameState state,
        List<string> usedDefinitionIds)
    {
        List<IncidentDefinition> eligible = new List<IncidentDefinition>();

        if (!state.HasActiveExpedition)
            return eligible;

        ExpeditionData expedition = state.ActiveExpedition;

        bool canChangeTravel =
            expedition != null &&
            expedition.DaysRemaining > 0 &&
            (expedition.Phase == CommanderState.TravellingToLocation ||
             expedition.Phase == CommanderState.ReturningToCastle);

        foreach (IncidentDefinition definition in Definitions)
        {
            if (usedDefinitionIds.Contains(definition.Id))
                continue;

            bool needsTravel =
                definition.EffectKind == IncidentEffectKind.Travel ||
                definition.EffectKind == IncidentEffectKind.SupplyAndTravel;

            if (needsTravel && !canChangeTravel)
                continue;

            eligible.Add(definition);
        }

        return eligible;
    }

    private static ExpeditionIncidentOccurrence ApplyIncident(
        GameState state,
        IncidentDefinition definition,
        int finishedDay)
    {
        List<string> consequences = new List<string>();

        if (definition.EffectKind == IncidentEffectKind.Supply ||
            definition.EffectKind == IncidentEffectKind.SupplyAndTravel)
        {
            int actualSupplyDelta = ApplySupplyDelta(state, definition.SupplyDelta);
            consequences.Add(FormatSupplyConsequence(actualSupplyDelta));
        }

        if (definition.EffectKind == IncidentEffectKind.Travel ||
            definition.EffectKind == IncidentEffectKind.SupplyAndTravel)
        {
            int actualTravelDelta = ApplyTravelDelta(state, definition.TravelDelta);
            string travelConsequence = FormatTravelConsequence(actualTravelDelta);
            string arrivalText = GetArrivalText(state, actualTravelDelta);

            consequences.Add(travelConsequence + arrivalText);
        }

        ExpeditionIncidentOccurrence occurrence =
            new ExpeditionIncidentOccurrence
            {
                Id = nextOccurrenceId++,
                Day = finishedDay,
                Title = definition.Title,
                Description = definition.Description,
                ConsequenceText = string.Join(" ", consequences),
                Tone = definition.Tone
            };

        return occurrence;
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

        int requestedReduction = -requestedDelta;
        int actualReduction = Math.Min(
            expedition.DaysRemaining,
            requestedReduction);

        expedition.DaysRemaining -= actualReduction;

        if (actualReduction > 0 && expedition.DaysRemaining == 0)
            ResolveArrivalAfterShortcut(state, expedition);

        return -actualReduction;
    }

    private static void ResolveArrivalAfterShortcut(
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
            return " Экспедиция достигла цели.";

        if (!expedition.IsActive &&
            expedition.Phase == CommanderState.ReturningToCastle)
        {
            return " Армия вернулась в столицу.";
        }

        return string.Empty;
    }

    private static string FormatSupplyConsequence(int delta)
    {
        if (delta > 0)
            return "Снабжение +" + delta + ".";

        if (delta < 0)
            return "Снабжение " + delta + ".";

        return "Снабжение не изменилось: запас уже пуст.";
    }

    private static string FormatTravelConsequence(int delta)
    {
        if (delta > 0)
            return "Оставшийся путь +" + delta + " день.";

        if (delta < 0)
            return "Оставшийся путь " + delta + " день.";

        return "Длина пути не изменилась.";
    }

    private static string GetIncidentWord(int count)
    {
        return count == 1 ? " происшествие" : " происшествия";
    }
}
