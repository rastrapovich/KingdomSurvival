using System;
using System.Collections.Generic;

// ПР-09 (ProjectDocs/PR09_ROAD_CAMP_SPEC.md §3–§5): ночлег в пути.
// Лагерь читает место стоянки и состав отряда; до двух действий за ночь;
// «Встать на ночлег» — остановка на 8 игровых часов тем же механизмом, что
// остановки на дороге. Припасы отдельно не тратятся (правка §0.2): только
// обычное суточное списание. Утром: изнеможение снято, выбранные действия
// дали результат, отряд продолжает путь сам.

public enum CampPlaceKind
{
    OpenGround,
    Road,
    AtLocation
}

public enum CampActionKind
{
    // Перевязать раненых — Марта в отряде (с сумкой лекаря — лучше).
    Bandage,
    // Осмотреть окрестности — Агнесса или Остафий; открывает скрытое место рядом.
    Inspect,
    // Выставить дозор — боец в отряде, стоянка у дороги или у воды.
    Watch
}

public readonly struct CampActionOption
{
    public readonly CampActionKind Kind;
    public readonly string Title;
    public readonly string Hint;
    public readonly bool Available;
    public readonly string UnavailableReason;

    public CampActionOption(CampActionKind kind, string title, string hint, bool available, string unavailableReason)
    {
        Kind = kind;
        Title = title;
        Hint = hint;
        Available = available;
        UnavailableReason = unavailableReason;
    }
}

[Serializable]
public sealed class CampNightData
{
    // Выбранные на эту ночь действия (CampActionKind), до двух.
    public List<int> ChosenActions = new List<int>();
    // Контекст «у воды» задаёт глава (например, старый брод) — Watch доступен.
    public bool NearWater;
}

public static class CampRest
{
    public const string RestActivityId = "camp_rest";
    public const double RestHours = 8.0;
    public const int MaxActionsPerNight = 2;

    // Сколько дел можно выбрать этой ночью: 2, с «Полевым ночлегом» в отряде — 3.
    public static int ActionsPerNight(GameState state)
    {
        return MaxActionsPerNight + FeatureEffects.ExtraCampActions(state);
    }
    public const int InspectRadiusCells = 3;
    public const string MartaId = "marta";
    public const string AgnessaId = "agnessa";

    public static CampNightData GetNight(GameState state)
    {
        if (!state.HasActiveExpedition)
            return null;
        if (state.ActiveExpedition.CampNight == null)
            state.ActiveExpedition.CampNight = new CampNightData();
        if (state.ActiveExpedition.CampNight.ChosenActions == null)
            state.ActiveExpedition.CampNight.ChosenActions = new List<int>();
        return state.ActiveExpedition.CampNight;
    }

    public static bool IsResting(GameState state)
    {
        return state.HasActiveExpedition &&
               state.ActiveExpedition.ActiveActivity != null &&
               state.ActiveExpedition.ActiveActivity.Id == RestActivityId;
    }

    // Место стоянки: у достигнутого места, на дороге, в поле.
    public static CampPlaceKind GetPlace(GameState state)
    {
        if (!state.HasActiveExpedition)
            return CampPlaceKind.OpenGround;
        ExpeditionData expedition = state.ActiveExpedition;
        if (expedition.Phase == CommanderState.AtLocation)
        {
            LocationData location = state.FindLocation(expedition.LocationId);
            if (location != null && !location.IsWaypoint)
                return CampPlaceKind.AtLocation;
        }

        WorldMapGameplayTerrainType terrain = WorldMapNavigation.GetTerrainAtPercent(
            expedition.CurrentMapXPercent, expedition.CurrentMapYPercent);
        return terrain == WorldMapGameplayTerrainType.Road || terrain == WorldMapGameplayTerrainType.Trail
            ? CampPlaceKind.Road
            : CampPlaceKind.OpenGround;
    }

    public static string DescribePlace(GameState state)
    {
        switch (GetPlace(state))
        {
            case CampPlaceKind.AtLocation:
                LocationData location = state.FindLocation(state.ActiveExpedition.LocationId);
                return "Стоянка: у места «" + (location != null ? location.Name : "?") + "»";
            case CampPlaceKind.Road:
                return "Стоянка: у дороги";
            default:
                return "Стоянка: в открытом поле";
        }
    }

    // Кто в отряде (герой, бойцы, свита).
    public static List<string> PartyIds(GameState state)
    {
        List<string> party = new List<string>();
        if (!state.HasActiveExpedition)
            return party;
        CommanderData commander = state.GetSelectedCommander();
        if (commander != null)
            party.Add(commander.Id);
        party.AddRange(state.ActiveExpedition.FighterIds);
        if (state.ActiveExpedition.RetinueIds != null)
            party.AddRange(state.ActiveExpedition.RetinueIds);
        return party;
    }

    private static bool InParty(GameState state, string personId)
    {
        ResidentState resident = HomePeopleService.Find(state, personId);
        return resident != null && resident.IsAlive && PartyIds(state).Contains(personId);
    }

    // Можно ли сейчас встать на ночлег (и выбирать действия).
    public static bool CanRest(GameState state, out string reason)
    {
        reason = string.Empty;
        if (!state.HasActiveExpedition || !ContinuousSimulationSystem.HasExpeditionStartedMoving(state))
        {
            reason = "Ночлег — в походе.";
            return false;
        }
        ExpeditionData expedition = state.ActiveExpedition;
        if (state.HasPendingExpeditionDecision)
        {
            reason = "Сначала нужно принять решение.";
            return false;
        }
        // ПР-12К: ночлег — вместе, в лагере, а не порознь.
        if (SettlementCampService.IsCommanderInside(state))
        {
            reason = "Сначала вернитесь к лагерю у поселения.";
            return false;
        }
        if (LocalExplorationService.IsActive(state))
        {
            reason = "Сначала выйдите из места.";
            return false;
        }
        if (expedition.HasTimedActivity)
        {
            reason = IsResting(state) ? "Отряд уже спит." : "Отряд занят: " + expedition.ActiveActivity.DisplayName + ".";
            return false;
        }
        if (expedition.Phase != CommanderState.TravellingToLocation &&
            expedition.Phase != CommanderState.ReturningToCastle &&
            expedition.Phase != CommanderState.AtLocation)
        {
            reason = "Сейчас не встать на ночлег.";
            return false;
        }
        return true;
    }

    public static List<CampActionOption> GetActions(GameState state)
    {
        List<CampActionOption> options = new List<CampActionOption>();
        if (!state.HasActiveExpedition)
            return options;

        // 12Е-9: кто что может в пути — функции присутствия (PresenceFunctions).
        List<string> party = PartyIds(state);
        List<string> fighters = state.ActiveExpedition.FighterIds;
        bool martaHere = PresenceFunctions.Provider(state, PresenceFunctions.Bandage, party, fighters) != null;
        bool anyWounded = false;
        foreach (string id in PartyIds(state))
        {
            ResidentState resident = HomePeopleService.Find(state, id);
            if (resident != null && resident.IsAlive && resident.HasCombatState && resident.CurrentHitPoints < resident.MaxHitPoints)
                anyWounded = true;
        }
        bool bag = martaHere && HasHealerBag(state);
        options.Add(new CampActionOption(CampActionKind.Bandage, "Перевязать раненых",
            bag ? "Марта с сумкой: раненым — половина недостающего к утру." : "Марта без сумки лекаря: четверть недостающего.",
            martaHere && anyWounded,
            !martaHere ? "Некому: Марта не в отряде." : "Раненых нет."));

        bool scout = PresenceFunctions.Provider(state, PresenceFunctions.Inspect, party, fighters) != null;
        options.Add(new CampActionOption(CampActionKind.Inspect, "Осмотреть окрестности",
            "Агнесса или Остафий обходят округу: может найтись место рядом.",
            scout, "Некому: ни Агнессы, ни Остафия в отряде."));

        bool fighter = PresenceFunctions.Provider(state, PresenceFunctions.Watch, party, fighters) != null;
        CampNightData night = GetNight(state);
        bool watchPlace = GetPlace(state) == CampPlaceKind.Road || night.NearWater;
        options.Add(new CampActionOption(CampActionKind.Watch, "Выставить дозор",
            "Кто-то не спит и смотрит на дорогу и воду.",
            fighter && watchPlace,
            !fighter ? "Некому: герой один." : "Здесь дозору нечего стеречь: ни дороги, ни воды."));

        return options;
    }

    private static bool HasHealerBag(GameState state)
    {
        ItemInstanceData special = ItemService.Equipped(state, MartaId, ItemSlot.Special);
        return special != null && special.ItemId == ItemCatalog.HealerBag;
    }

    // Отметить/снять действие на эту ночь (до двух).
    public static bool TryToggleAction(GameState state, CampActionKind kind, out string message)
    {
        if (!CanRest(state, out message))
            return false;

        CampNightData night = GetNight(state);
        int value = (int)kind;
        if (night.ChosenActions.Contains(value))
        {
            night.ChosenActions.Remove(value);
            message = string.Empty;
            return true;
        }

        foreach (CampActionOption option in GetActions(state))
        {
            if (option.Kind != kind)
                continue;
            if (!option.Available)
            {
                message = option.UnavailableReason;
                return false;
            }
        }

        int allowed = ActionsPerNight(state);
        if (night.ChosenActions.Count >= allowed)
        {
            message = allowed > MaxActionsPerNight
                ? "За ночь — не больше " + allowed + " дел, даже с привычкой к полевому ночлегу."
                : "За ночь — не больше двух дел: остальным нужно спать.";
            return false;
        }

        night.ChosenActions.Add(value);
        message = string.Empty;
        return true;
    }

    public static bool IsChosen(GameState state, CampActionKind kind)
    {
        CampNightData night = GetNight(state);
        return night != null && night.ChosenActions.Contains((int)kind);
    }

    // «Встать на ночлег»: остановка на 8 часов, маршрут сохраняется.
    public static bool TryStartRest(GameState state, out string message)
    {
        if (!CanRest(state, out message))
            return false;

        ExpeditionData expedition = state.ActiveExpedition;
        expedition.ActiveActivity = new ExpeditionActivityData
        {
            Id = RestActivityId,
            DisplayName = "НОЧЛЕГ",
            Kind = ExpeditionActivityKind.RoadStop,
            TotalHours = RestHours,
            RemainingHours = RestHours
        };
        message = "Отряд встал на ночлег на " + ContinuousExpeditionCommands.FormatHours(RestHours) + ".";
        return true;
    }

    // Утро: итог ночлега (вызывает владелец времени по окончании остановки).
    public static void CompleteRest(GameState state, List<string> messages)
    {
        CampNightData night = GetNight(state);
        List<string> results = new List<string>();

        foreach (string id in PartyIds(state))
        {
            ResidentState resident = HomePeopleService.Find(state, id);
            if (resident != null && resident.IsAlive && resident.Exhausted)
            {
                resident.Exhausted = false;
                results.Add(resident.DisplayName + " отоспался");
            }
        }

        if (night != null)
        {
            foreach (int action in night.ChosenActions)
            {
                string result = ApplyAction(state, (CampActionKind)action);
                if (!string.IsNullOrEmpty(result))
                    results.Add(result);
            }
            night.ChosenActions.Clear();
        }

        // 12Е-4: ночь прошла — эпоха «до лагеря» сменилась, особенности
        // ночёвки срабатывают.
        // 12Е-7: сработавшие особенности — отдельными строками под итогом.
        List<string> features = FeaturePresentation.Lines(state,
            FeatureDispatcher.Raise(new FeatureEvent { Trigger = FeatureTrigger.CampNight, State = state }));

        string morning = results.Count == 0
            ? "Ночь прошла спокойно. Отряд продолжает путь."
            : "Утро после ночлега: " + string.Join("; ", results) + ".";
        messages?.Add(features.Count == 0 ? morning : morning + "\n" + string.Join("\n", features));
    }

    private static string ApplyAction(GameState state, CampActionKind kind)
    {
        switch (kind)
        {
            case CampActionKind.Bandage:
                if (!InParty(state, MartaId))
                    return null;
                bool bag = HasHealerBag(state);
                List<string> healed = new List<string>();
                foreach (string id in PartyIds(state))
                {
                    ResidentState resident = HomePeopleService.Find(state, id);
                    if (resident == null || !resident.IsAlive || !resident.HasCombatState ||
                        resident.CurrentHitPoints >= resident.MaxHitPoints)
                        continue;
                    int missing = resident.MaxHitPoints - resident.CurrentHitPoints;
                    int gain = Math.Max(1, bag ? missing / 2 : missing / 4);
                    HomePeopleService.SetHitPoints(resident, resident.CurrentHitPoints + gain);
                    healed.Add(resident.DisplayName + " +" + gain + " HP");
                }
                return healed.Count > 0 ? "Марта перевязала: " + string.Join(", ", healed) : null;

            case CampActionKind.Inspect:
                LocationData found = FindHiddenNearby(state);
                if (found != null)
                {
                    found.IsVisibleOnMap = true;
                    found.IsDiscovered = true;
                    return "в округе нашли место «" + found.Name + "» — оно на карте";
                }
                return "округа обойдена: ничего, кроме следов зверья";

            case CampActionKind.Watch:
                return GetPlace(state) == CampPlaceKind.Road
                    ? "дозорные видели ночью огонёк на дороге — кто-то шёл, не останавливаясь"
                    : "дозорные слышали, как вода ночью меняла голос";

            default:
                return null;
        }
    }

    public static LocationData FindHiddenNearby(GameState state)
    {
        if (!state.HasActiveExpedition || state.Locations == null)
            return null;
        ExpeditionData expedition = state.ActiveExpedition;
        // 12И: радиус в клетках пути шестиугольной сетки (+полклетки — как
        // прежний квадрат вокруг клетки героя).
        LocationData best = null;
        double bestDistance = double.MaxValue;
        foreach (LocationData location in state.Locations)
        {
            if (location.IsWaypoint || location.IsVisibleOnMap)
                continue;
            double distance = WorldMapNavigation.DistanceHexes(
                expedition.CurrentMapXPercent, expedition.CurrentMapYPercent,
                location.MapXPercent, location.MapYPercent);
            if (distance <= InspectRadiusCells + 0.5 && distance < bestDistance)
            {
                best = location;
                bestDistance = distance;
            }
        }
        return best;
    }
}
