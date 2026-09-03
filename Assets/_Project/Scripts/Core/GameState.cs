using System;
using System.Collections.Generic;

public enum CommanderState
{
    InCastle,
    TravellingToLocation,
    AtLocation,
    ReturningToCastle
}

public enum ExpeditionActivityKind
{
    LocationResearch,
    RoadStop
}

[Serializable]
public class ExpeditionActivityData
{
    public string Id;
    public string DisplayName;
    public ExpeditionActivityKind Kind;
    public string LocationId;
    public double TotalHours;
    public double RemainingHours;
    public int RewardArmyGold;
    public int RewardArmySupply;

    public double Progress01 =>
        TotalHours <= 0.0
            ? 1.0
            : Math.Max(0.0, Math.Min(1.0, 1.0 - RemainingHours / TotalHours));
}

[Serializable]
public class FighterData
{
    public string Id;
    public string Name;
    public string Role;
    public int Level;
    public int DefensePower;

    public FighterData(
        string id,
        string name,
        string role,
        int level,
        int defensePower)
    {
        Id = id;
        Name = name;
        Role = role;
        Level = level;
        DefensePower = defensePower;
    }
}

[Serializable]
public class CommanderData
{
    public string Id;
    public string Name;
    public CommanderState State;

    public CommanderData(string id, string name)
    {
        Id = id;
        Name = name;
        State = CommanderState.InCastle;
    }
}

[Serializable]
public class LocationData
{
    public string Id;
    public string Name;
    public string RegionId;
    public string RegionName;
    public int MapSlotIndex;
    public float MapXPercent;
    public float MapYPercent;
    public double TravelHoursFromCapital;
    public string Threat;
    public double ExplorationHours;
    public int RewardArmyGold;
    public int RewardArmySupply;
    public bool IsDiscovered;
    public bool IsExplored;
    public bool IsVisibleOnMap;
    public bool IsWaypoint;

    public string TravelTargetName =>
        IsWaypoint ? "точка маршрута" :
        IsDiscovered ? Name : RegionName;

    public LocationData(
        string id,
        string name,
        double travelHoursFromCapital,
        string threat,
        double explorationHours = 0.0,
        int rewardArmyGold = 0,
        int rewardArmySupply = 0)
    {
        Id = id;
        Name = name;
        TravelHoursFromCapital = travelHoursFromCapital;
        Threat = threat;
        ExplorationHours = explorationHours;
        RewardArmyGold = rewardArmyGold;
        RewardArmySupply = rewardArmySupply;
        IsDiscovered = true;
        IsExplored = false;
        IsVisibleOnMap = true;
        IsWaypoint = false;
    }

    public void AssignToRegion(
        string regionId,
        string regionName,
        int mapSlotIndex,
        float mapXPercent,
        float mapYPercent,
        double travelHoursFromCapital)
    {
        RegionId = regionId;
        RegionName = regionName;
        MapSlotIndex = mapSlotIndex;
        MapXPercent = mapXPercent;
        MapYPercent = mapYPercent;
        TravelHoursFromCapital = travelHoursFromCapital;
        IsDiscovered = false;
        IsVisibleOnMap = false;
        IsWaypoint = false;
    }
}

[Serializable]
public class ExpeditionData
{
    public bool IsActive;
    public string CommanderId;
    public string LocationId;
    public List<string> FighterIds = new List<string>();
    public CommanderState Phase;
    public int RemainingRouteCells;
    public int RouteLengthCells;
    public float CurrentMapXPercent;
    public float CurrentMapYPercent;
    public float TargetMapXPercent;
    public float TargetMapYPercent;
    public bool IsScoutingTarget;
    public int RouteIndex;
    public double RouteDelayHoursRemaining;
    public List<MapPointData> Route = new List<MapPointData>();
    public ExpeditionActivityData ActiveActivity;

    public bool HasTimedActivity => ActiveActivity != null;
    public bool IsLocationResearchInProgress =>
        ActiveActivity != null &&
        ActiveActivity.Kind == ExpeditionActivityKind.LocationResearch;
    public bool IsRoadStopInProgress =>
        ActiveActivity != null &&
        ActiveActivity.Kind == ExpeditionActivityKind.RoadStop;

    // Значимое происшествие не меняет канонические состояния командира.
    // Это отдельное состояние самой экспедиции: она ждёт приказа короля.
    public ExpeditionDecisionOccurrence PendingDecision;
    public List<string> UsedDecisionIds = new List<string>();

    // Каждый расчёт пути запоминает реально пройденные клетки этого перемещения.
    // По ним система разведки проверяет скрытые локации вдоль всего маршрута.
    public List<MapPointData> LastTravelPoints = new List<MapPointData>();
    public CommanderState LastTravelStartedPhase;
    public string LastTravelTargetLocationId;
    public float LastTravelTargetXPercent;
    public float LastTravelTargetYPercent;

    // Если движение прервано обнаруженной локацией, сохраняем прежнюю цель.
    public bool HasInterruptedRoute;
    public CommanderState InterruptedPhase;
    public string InterruptedTargetLocationId;
    public float InterruptedTargetXPercent;
    public float InterruptedTargetYPercent;
}

[Serializable]
public class GameState
{
    private const string RouteWaypointId = "__route_waypoint";

    public int WorldSeed;
    public int Day;
    public int Gold;
    public int Food;
    public int Population;
    public int Mood;
    public int ConsecutiveFoodShortageDays;
    public int ConsecutiveExpeditionSupplyShortageDays;

    // Походные ресурсы принадлежат единственному отряду.
    public int ArmySupply;
    public int ArmyGold;

    public string SelectedCommanderId;
    public List<CommanderData> Commanders;
    public List<FighterData> Fighters;
    public List<LocationData> Locations;
    public ExpeditionData ActiveExpedition;

    // Временные базовые доходы прототипа. Источники дохода добавим позже.
    public int DailyGoldIncome => 3;
    public int DailyFoodIncome => 7;

    public int DailyFoodConsumption => Population;

    // В походе по 1 единице снабжения потребляют командир и каждый боец.
    public int ExpeditionSupplyConsumption
    {
        get
        {
            if (HasActiveExpedition)
                return ActiveExpedition.FighterIds.Count + 1;

            return Fighters.Count + 1;
        }
    }

    public int FullSupplyDays
    {
        get
        {
            int dailyConsumption = ExpeditionSupplyConsumption;
            return dailyConsumption > 0 ? ArmySupply / dailyConsumption : 0;
        }
    }

    public bool HasActiveExpedition =>
        ActiveExpedition != null && ActiveExpedition.IsActive;

    public bool HasPendingExpeditionDecision =>
        HasActiveExpedition &&
        ActiveExpedition.PendingDecision != null;

    public int TotalArmyDefensePower
    {
        get
        {
            int total = 0;

            foreach (FighterData fighter in Fighters)
                total += Math.Max(0, fighter.DefensePower);

            return total;
        }
    }

    public int GarrisonFighterCount
    {
        get
        {
            if (!HasActiveExpedition)
                return Fighters.Count;

            int count = 0;

            foreach (FighterData fighter in Fighters)
            {
                if (!IsFighterInActiveExpedition(fighter.Id))
                    count++;
            }

            return count;
        }
    }

    public int GarrisonDefensePower
    {
        get
        {
            int total = 0;

            foreach (FighterData fighter in Fighters)
            {
                if (!IsFighterInActiveExpedition(fighter.Id))
                    total += Math.Max(0, fighter.DefensePower);
            }

            return total;
        }
    }

    public int ExpeditionDefensePower
    {
        get
        {
            if (!HasActiveExpedition)
                return 0;

            return CalculateDefensePower(ActiveExpedition.FighterIds);
        }
    }

    public bool CanCancelPreparedExpedition =>
        HasActiveExpedition &&
        ActiveExpedition.Phase == CommanderState.TravellingToLocation &&
        !HasPendingExpeditionDecision &&
        !ActiveExpedition.HasTimedActivity &&
        !ContinuousSimulationSystem.HasExpeditionStartedMoving(this);

    public bool CanAdjustArmySupply
    {
        get
        {
            CommanderData commander = GetSelectedCommander();

            return commander != null &&
                   commander.State == CommanderState.InCastle &&
                   !HasActiveExpedition;
        }
    }

    public bool CanResearchActiveLocation
    {
        get
        {
            if (!HasActiveExpedition || HasPendingExpeditionDecision)
                return false;

            ExpeditionData expedition = ActiveExpedition;

            if (expedition.Phase != CommanderState.AtLocation ||
                expedition.HasTimedActivity)
            {
                return false;
            }

            LocationData location = FindLocation(expedition.LocationId);

            return location != null &&
                   !location.IsWaypoint &&
                   location.ExplorationHours > 0.0 &&
                   !location.IsExplored &&
                   ArmySupply >= ExpeditionSupplyConsumption;
        }
    }

    public void CreateNewGame(int? worldSeed = null)
    {
        WorldSeed = worldSeed ?? Guid.NewGuid().GetHashCode();
        WorldMapNavigation.ConfigureTerrain(WorldSeed);
        Day = 1;
        Gold = 120;
        Food = 72;
        Population = 24;
        Mood = 64;
        ConsecutiveFoodShortageDays = 0;
        ConsecutiveExpeditionSupplyShortageDays = 0;
        ArmySupply = 0;
        ArmyGold = 0;

        Commanders = new List<CommanderData>
        {
            new CommanderData("alric", "Сэр Альрик"),
            new CommanderData("mirena", "Леди Мирена"),
            new CommanderData("bran", "Бран Каменная Рука")
        };

        SelectedCommanderId = "alric";

        Fighters = new List<FighterData>
        {
            // Сила обороны — временные числа серого прототипа, не финальный баланс.
            new FighterData("garrick", "Гаррик", "Гвардеец", 1, 3),
            new FighterData("edric", "Эдрик", "Лучник", 1, 2),
            new FighterData("marta", "Марта", "Лекарь", 1, 1),
            new FighterData("torvin", "Торвин", "Копейщик", 1, 3),
            new FighterData("agnessa", "Агнесса", "Разведчик", 1, 2)
        };

        List<LocationData> locationPool = new List<LocationData>
        {
            // Низкая угроза: короткое двухчасовое исследование.
            new LocationData(
                "ruins",
                "Затопленные руины",
                2,
                "низкая",
                2.0,
                100,
                200),

            // Средняя угроза: полное пятичасовое исследование.
            new LocationData(
                "mine",
                "Старая шахта",
                3,
                "средняя",
                5.0,
                300,
                0),

            new LocationData("forest", "Чёрный лес", 5, "высокая")
        };

        Random worldRandom = new Random(WorldSeed);
        ShuffleLocations(locationPool, worldRandom);

        // Локации существуют в мире заранее и больше не переносятся в точку клика.
        float[,] candidatePositions =
        {
            { 13f, 20f }, { 48f, 12f }, { 80f, 25f }
        };

        for (int i = 0; i < locationPool.Count; i++)
        {
            float x = candidatePositions[i, 0] + worldRandom.Next(-4, 5);
            float y = candidatePositions[i, 1] + worldRandom.Next(-3, 4);
            List<MapPointData> candidateRoute = WorldMapNavigation.FindPath(
                WorldMapNavigation.CapitalXPercent,
                WorldMapNavigation.CapitalYPercent,
                x,
                y);

            if (candidateRoute.Count > 0)
            {
                x = candidateRoute[candidateRoute.Count - 1].XPercent;
                y = candidateRoute[candidateRoute.Count - 1].YPercent;
            }

            locationPool[i].AssignToRegion(
                "sector-" + i,
                GetRegionName(x, y),
                i,
                x,
                y,
                ContinuousSimulationSystem.CalculateTravelHours(candidateRoute));
        }

        Locations = locationPool;
        ActiveExpedition = null;
    }

    private static void ShuffleLocations(
        List<LocationData> locations,
        Random random)
    {
        for (int i = locations.Count - 1; i > 0; i--)
        {
            int swapIndex = random.Next(i + 1);
            LocationData temporary = locations[i];
            locations[i] = locations[swapIndex];
            locations[swapIndex] = temporary;
        }
    }

    public CommanderData GetSelectedCommander()
    {
        return FindCommander(SelectedCommanderId);
    }

    public CommanderData FindCommander(string commanderId)
    {
        foreach (CommanderData commander in Commanders)
        {
            if (commander.Id == commanderId)
                return commander;
        }

        return null;
    }

    public LocationData FindLocation(string locationId)
    {
        if (string.IsNullOrEmpty(locationId) || Locations == null)
            return null;

        foreach (LocationData location in Locations)
        {
            if (location.Id == locationId)
                return location;
        }

        return null;
    }

    public FighterData FindFighter(string fighterId)
    {
        foreach (FighterData fighter in Fighters)
        {
            if (fighter.Id == fighterId)
                return fighter;
        }

        return null;
    }

    public bool IsFighterInActiveExpedition(string fighterId)
    {
        return HasActiveExpedition &&
               ActiveExpedition.FighterIds.Contains(fighterId);
    }

    public int CalculateDefensePower(IEnumerable<string> fighterIds)
    {
        if (fighterIds == null)
            return 0;

        int total = 0;
        HashSet<string> countedIds = new HashSet<string>();

        foreach (string fighterId in fighterIds)
        {
            if (!countedIds.Add(fighterId))
                continue;

            FighterData fighter = FindFighter(fighterId);

            if (fighter != null)
                total += Math.Max(0, fighter.DefensePower);
        }

        return total;
    }

    public List<string> GetCommanderNames()
    {
        List<string> names = new List<string>();

        foreach (CommanderData commander in Commanders)
            names.Add(commander.Name);

        return names;
    }

    public bool SelectCommanderByName(string commanderName)
    {
        if (HasActiveExpedition)
            return false;

        foreach (CommanderData commander in Commanders)
        {
            if (commander.Name == commanderName)
            {
                SelectedCommanderId = commander.Id;
                return true;
            }
        }

        return false;
    }

    public bool TryAddArmySupply()
    {
        return AdjustArmySupply(1) > 0;
    }

    public bool TryRemoveArmySupply()
    {
        return AdjustArmySupply(-1) < 0;
    }

    public int AdjustArmySupply(int requestedDelta)
    {
        if (!CanAdjustArmySupply || requestedDelta == 0)
            return 0;

        if (requestedDelta > 0)
        {
            int transferred = Math.Min(Food, requestedDelta);
            Food -= transferred;
            ArmySupply += transferred;
            return transferred;
        }

        int returned = Math.Min(ArmySupply, -requestedDelta);
        ArmySupply -= returned;
        Food += returned;
        return -returned;
    }

    public bool TryStartExpedition(
        string locationId,
        List<string> selectedFighterIds,
        out string resultMessage)
    {
        if (HasActiveExpedition)
        {
            resultMessage = "Нельзя отправить вторую экспедицию: один поход уже активен.";
            return false;
        }

        if (Fighters.Count < 4 || Fighters.Count > 6)
        {
            resultMessage = "Армия должна состоять из 4–6 отдельных воинов.";
            return false;
        }

        LocationData location = FindLocation(locationId);

        if (location == null || location.IsWaypoint)
        {
            resultMessage = "Не удалось найти выбранную локацию.";
            return false;
        }

        return TryStartExpeditionToMapPoint(
            location.MapXPercent,
            location.MapYPercent,
            location.Id,
            false,
            selectedFighterIds,
            out resultMessage);
    }

    public bool TryStartExpeditionToMapPoint(
        float targetXPercent,
        float targetYPercent,
        string locationId,
        bool isScoutingTarget,
        List<string> selectedFighterIds,
        out string resultMessage)
    {
        if (HasActiveExpedition)
        {
            resultMessage = "Нельзя отправить вторую экспедицию: один поход уже активен.";
            return false;
        }

        if (!ValidateExpeditionFighters(selectedFighterIds, out resultMessage))
            return false;

        CommanderData commander = GetSelectedCommander();

        if (commander == null)
        {
            resultMessage = "Не удалось найти выбранного командира.";
            return false;
        }

        LocationData location = FindLocation(locationId);

        if (location == null)
        {
            location = GetOrCreateRouteWaypoint(targetXPercent, targetYPercent);
            isScoutingTarget = false;
        }

        List<MapPointData> route = WorldMapNavigation.FindPath(
            WorldMapNavigation.CapitalXPercent,
            WorldMapNavigation.CapitalYPercent,
            targetXPercent,
            targetYPercent);
        int routeCells = WorldMapNavigation.CalculateRouteCells(route);

        if (route.Count < 2 || routeCells <= 0)
        {
            resultMessage = "До выбранной точки нельзя построить доступный маршрут.";
            return false;
        }

        ExpeditionData expedition = new ExpeditionData
        {
            IsActive = true,
            CommanderId = commander.Id,
            LocationId = location.Id,
            Phase = CommanderState.TravellingToLocation,
            RemainingRouteCells = routeCells,
            RouteLengthCells = routeCells,
            CurrentMapXPercent = route[0].XPercent,
            CurrentMapYPercent = route[0].YPercent,
            TargetMapXPercent = route[route.Count - 1].XPercent,
            TargetMapYPercent = route[route.Count - 1].YPercent,
            IsScoutingTarget = isScoutingTarget,
            RouteIndex = 0,
            RouteDelayHoursRemaining = 0.0,
            Route = route,
            ActiveActivity = null,
            PendingDecision = null,
            HasInterruptedRoute = false
        };

        HashSet<string> selectedIds = new HashSet<string>(selectedFighterIds);

        // Сохраняем порядок общей армии, чтобы состав всегда одинаково
        // отображался в интерфейсе и донесениях.
        foreach (FighterData fighter in Fighters)
        {
            if (selectedIds.Contains(fighter.Id))
                expedition.FighterIds.Add(fighter.Id);
        }

        ConsecutiveExpeditionSupplyShortageDays = 0;
        ActiveExpedition = expedition;
        commander.State = CommanderState.TravellingToLocation;

        string destinationText = location.IsWaypoint
            ? "выбранную точку"
            : "локацию «" + location.Name + "»";

        resultMessage =
            commander.Name + " и " + expedition.FighterIds.Count +
            " воинов получили приказ двигаться в " +
            destinationText + ". В столице осталось бойцов: " +
            GarrisonFighterCount + ", сила гарнизона: " +
            GarrisonDefensePower + "/" + TotalArmyDefensePower +
            ". До начала фактического движения приказ можно отменить или изменить.";

        return true;
    }

    public bool TryChangeExpeditionRoute(
        float targetXPercent,
        float targetYPercent,
        string locationId,
        out string resultMessage)
    {
        resultMessage = "Не удалось изменить маршрут.";

        if (!HasActiveExpedition)
        {
            resultMessage = "Сейчас нет активной экспедиции.";
            return false;
        }

        if (HasPendingExpeditionDecision)
        {
            resultMessage = "Сначала требуется принять обязательное решение.";
            return false;
        }

        if (ActiveExpedition.IsLocationResearchInProgress)
        {
            resultMessage = "Нельзя менять маршрут во время начатого исследования.";
            return false;
        }

        CommanderData commander = FindCommander(ActiveExpedition.CommanderId);

        if (commander == null)
        {
            resultMessage = "Не удалось определить командира экспедиции.";
            return false;
        }

        LocationData location = FindLocation(locationId);
        if (location == null)
            location = GetOrCreateRouteWaypoint(targetXPercent, targetYPercent);

        List<MapPointData> route = WorldMapNavigation.FindPath(
            ActiveExpedition.CurrentMapXPercent,
            ActiveExpedition.CurrentMapYPercent,
            targetXPercent,
            targetYPercent);
        int routeCells = WorldMapNavigation.CalculateRouteCells(route);

        if (route.Count < 2 || routeCells <= 0)
        {
            resultMessage = "Новая цель совпадает с текущей позицией или недоступна.";
            return false;
        }

        ActiveExpedition.LocationId = location.Id;
        ActiveExpedition.Phase = CommanderState.TravellingToLocation;
        CancelRoadActivity(ActiveExpedition);
        ActiveExpedition.RemainingRouteCells = routeCells;
        ActiveExpedition.RouteLengthCells = routeCells;
        ActiveExpedition.Route = route;
        ActiveExpedition.RouteIndex = 0;
        ActiveExpedition.RouteDelayHoursRemaining = 0.0;
        ActiveExpedition.TargetMapXPercent = route[route.Count - 1].XPercent;
        ActiveExpedition.TargetMapYPercent = route[route.Count - 1].YPercent;
        ActiveExpedition.IsScoutingTarget = false;
        ActiveExpedition.HasInterruptedRoute = false;
        ActiveExpedition.LastTravelPoints.Clear();

        commander.State = CommanderState.TravellingToLocation;

        resultMessage = location.IsWaypoint
            ? "Маршрут изменён. Армия направляется к новой точке от своей текущей позиции."
            : "Маршрут изменён. Армия направляется к локации «" + location.Name +
              "» от своей текущей позиции.";

        return true;
    }

    public bool TryCancelPreparedExpedition(out string resultMessage)
    {
        if (!CanCancelPreparedExpedition)
        {
            resultMessage = "Отменить отправку уже нельзя. Используйте приказ о возвращении.";
            return false;
        }

        CommanderData commander = FindCommander(ActiveExpedition.CommanderId);
        if (commander == null)
        {
            resultMessage = "Не удалось определить данные экспедиции.";
            return false;
        }

        commander.State = CommanderState.InCastle;
        ActiveExpedition.IsActive = false;
        ActiveExpedition = null;
        ConsecutiveExpeditionSupplyShortageDays = 0;

        resultMessage =
            "Приказ на отправку отменён. " +
            commander.Name + " и выбранные бойцы остаются в столице. " +
            "Стратегическое время не продвинулось.";

        return true;
    }

    public bool TryStartLocationResearch(out string resultMessage)
    {
        resultMessage = "Исследование сейчас недоступно.";

        if (!HasActiveExpedition || HasPendingExpeditionDecision)
            return false;

        ExpeditionData expedition = ActiveExpedition;
        LocationData location = FindLocation(expedition.LocationId);

        if (location == null ||
            location.IsWaypoint ||
            expedition.Phase != CommanderState.AtLocation ||
            expedition.HasTimedActivity ||
            location.ExplorationHours <= 0.0 ||
            location.IsExplored)
        {
            return false;
        }

        int requiredSupply = ExpeditionSupplyConsumption;

        if (ArmySupply < requiredSupply)
        {
            resultMessage =
                "Для исследования нужен достаточный запас снабжения. Требуется: " +
                requiredSupply + ".";
            return false;
        }

        expedition.ActiveActivity = new ExpeditionActivityData
        {
            Id = "research:" + location.Id,
            DisplayName = "ИССЛЕДОВАНИЕ",
            Kind = ExpeditionActivityKind.LocationResearch,
            LocationId = location.Id,
            TotalHours = location.ExplorationHours,
            RemainingHours = location.ExplorationHours,
            RewardArmyGold = location.RewardArmyGold,
            RewardArmySupply = location.RewardArmySupply
        };

        resultMessage =
            "Отряд начал исследование локации «" + location.Name +
            "». Требуется времени: " +
            ContinuousExpeditionCommands.FormatHours(location.ExplorationHours) +
            ". До завершения исследования приказ о возвращении недоступен.";

        return true;
    }

    public bool TryStartRoadActivity(
        string activityId,
        string displayName,
        double durationHours,
        int rewardArmyGold,
        int rewardArmySupply,
        out string resultMessage)
    {
        resultMessage = "Походное действие сейчас недоступно.";

        if (!HasActiveExpedition ||
            HasPendingExpeditionDecision ||
            durationHours <= 0.0 ||
            ActiveExpedition.HasTimedActivity)
        {
            return false;
        }

        ExpeditionData expedition = ActiveExpedition;
        if (expedition.Phase != CommanderState.TravellingToLocation &&
            expedition.Phase != CommanderState.ReturningToCastle)
        {
            return false;
        }

        expedition.ActiveActivity = new ExpeditionActivityData
        {
            Id = activityId,
            DisplayName = displayName,
            Kind = ExpeditionActivityKind.RoadStop,
            TotalHours = durationHours,
            RemainingHours = durationHours,
            RewardArmyGold = rewardArmyGold,
            RewardArmySupply = rewardArmySupply
        };

        resultMessage =
            displayName + " начат. Отряд остановится на " +
            ContinuousExpeditionCommands.FormatHours(durationHours) +
            " и затем автоматически продолжит маршрут.";
        return true;
    }

    public bool TryOrderReturn(out string resultMessage)
    {
        if (!HasActiveExpedition)
        {
            resultMessage = "Сейчас нет активной экспедиции.";
            return false;
        }

        if (HasPendingExpeditionDecision)
        {
            resultMessage =
                "Экспедиция ждёт приказа по значимому происшествию. Сначала выберите решение.";
            return false;
        }

        if (ActiveExpedition.IsLocationResearchInProgress)
        {
            resultMessage =
                "Исследование уже начато. Сначала дождитесь его завершения.";
            return false;
        }

        if (CanCancelPreparedExpedition)
        {
            resultMessage =
                "Армия ещё не начала движение. Нажатие на столицу отменяет отправку.";
            return false;
        }

        if (ActiveExpedition.Phase == CommanderState.ReturningToCastle)
        {
            resultMessage = "Экспедиция уже возвращается в столицу.";
            return false;
        }

        CommanderData commander = FindCommander(ActiveExpedition.CommanderId);

        if (commander == null)
        {
            resultMessage = "Не удалось определить данные экспедиции.";
            return false;
        }

        List<MapPointData> returnRoute = WorldMapNavigation.FindPath(
            ActiveExpedition.CurrentMapXPercent,
            ActiveExpedition.CurrentMapYPercent,
            WorldMapNavigation.CapitalXPercent,
            WorldMapNavigation.CapitalYPercent);
        int returnCells = Math.Max(
            1,
            WorldMapNavigation.CalculateRouteCells(returnRoute));

        ActiveExpedition.Phase = CommanderState.ReturningToCastle;
        CancelRoadActivity(ActiveExpedition);
        ActiveExpedition.RemainingRouteCells = returnCells;
        ActiveExpedition.RouteLengthCells = returnCells;
        ActiveExpedition.Route = returnRoute;
        ActiveExpedition.RouteIndex = 0;
        ActiveExpedition.RouteDelayHoursRemaining = 0.0;
        ActiveExpedition.TargetMapXPercent = WorldMapNavigation.CapitalXPercent;
        ActiveExpedition.TargetMapYPercent = WorldMapNavigation.CapitalYPercent;
        ActiveExpedition.HasInterruptedRoute = false;
        ActiveExpedition.LastTravelPoints.Clear();
        commander.State = CommanderState.ReturningToCastle;

        resultMessage =
            commander.Name + " получил приказ возвращаться. Расчётное время пути: " +
            ContinuousSimulationSystem.FormatTravelTime(returnRoute) +
            ". До прибытия бойцы экспедиции не усиливают гарнизон.";

        return true;
    }

    public bool ForceReturnFromSupplyFailure(out string resultMessage)
    {
        resultMessage = "Не удалось начать вынужденное возвращение.";

        if (!HasActiveExpedition)
            return false;

        ExpeditionData expedition = ActiveExpedition;
        CommanderData commander = FindCommander(expedition.CommanderId);

        if (commander == null)
            return false;

        // Голод важнее ожидающего приказа или уже начатого исследования:
        // отряд прекращает другие действия и занимается возвращением.
        expedition.PendingDecision = null;
        expedition.ActiveActivity = null;
        expedition.HasInterruptedRoute = false;

        if (expedition.Phase == CommanderState.ReturningToCastle)
        {
            commander.State = CommanderState.ReturningToCastle;
            resultMessage =
                "Отряд уже возвращается в столицу и продолжает обратный путь.";
            return true;
        }

        List<MapPointData> returnRoute = WorldMapNavigation.FindPath(
            expedition.CurrentMapXPercent,
            expedition.CurrentMapYPercent,
            WorldMapNavigation.CapitalXPercent,
            WorldMapNavigation.CapitalYPercent);
        int returnCells = Math.Max(
            1,
            WorldMapNavigation.CalculateRouteCells(returnRoute));

        expedition.Phase = CommanderState.ReturningToCastle;
        expedition.RemainingRouteCells = returnCells;
        expedition.RouteLengthCells = returnCells;
        expedition.Route = returnRoute;
        expedition.RouteIndex = 0;
        expedition.RouteDelayHoursRemaining = 0.0;
        expedition.TargetMapXPercent = WorldMapNavigation.CapitalXPercent;
        expedition.TargetMapYPercent = WorldMapNavigation.CapitalYPercent;
        expedition.LastTravelPoints.Clear();
        commander.State = CommanderState.ReturningToCastle;

        resultMessage =
            "Поход сорван: из-за второй подряд нехватки снабжения отряд вынужденно возвращается. " +
            "Расчётное время пути: " +
            ContinuousSimulationSystem.FormatTravelTime(returnRoute) + ".";

        return true;
    }

    public string CompleteExpeditionReturn()
    {
        if (ActiveExpedition == null)
            return "Походные запасы не переданы: данные экспедиции отсутствуют.";

        CommanderData commander = FindCommander(ActiveExpedition.CommanderId);
        int deliveredGold = Math.Max(0, ArmyGold);
        int deliveredFood = Math.Max(0, ArmySupply);

        Gold += deliveredGold;
        Food += deliveredFood;
        ArmyGold = 0;
        ArmySupply = 0;
        ConsecutiveExpeditionSupplyShortageDays = 0;

        if (commander != null)
            commander.State = CommanderState.InCastle;

        ActiveExpedition.ActiveActivity = null;
        ActiveExpedition.PendingDecision = null;
        ActiveExpedition.HasInterruptedRoute = false;
        ActiveExpedition.IsActive = false;

        return
            "В столицу передано: золото +" + deliveredGold +
            ", пища +" + deliveredFood + ".";
    }

    public LocationData FindFirstHiddenLocationAlongLastTravel()
    {
        if (!HasActiveExpedition ||
            ActiveExpedition.LastTravelPoints == null ||
            ActiveExpedition.LastTravelPoints.Count == 0)
        {
            return null;
        }

        foreach (MapPointData point in ActiveExpedition.LastTravelPoints)
        {
            foreach (LocationData location in Locations)
            {
                if (location.IsWaypoint || location.IsVisibleOnMap)
                    continue;

                if (WorldMapNavigation.IsWithinDiscoveryRadius(
                        point.XPercent,
                        point.YPercent,
                        location.MapXPercent,
                        location.MapYPercent))
                {
                    return location;
                }
            }
        }

        return null;
    }

    public bool StopAtDiscoveredLocation(
        LocationData location,
        out string resultMessage)
    {
        resultMessage = "Не удалось остановиться у обнаруженной локации.";

        if (!HasActiveExpedition || location == null || location.IsWaypoint)
            return false;

        ExpeditionData expedition = ActiveExpedition;
        CommanderData commander = FindCommander(expedition.CommanderId);

        if (commander == null)
            return false;

        expedition.HasInterruptedRoute = true;
        expedition.InterruptedPhase = expedition.LastTravelStartedPhase;
        expedition.InterruptedTargetLocationId =
            expedition.LastTravelTargetLocationId;
        expedition.InterruptedTargetXPercent =
            expedition.LastTravelTargetXPercent;
        expedition.InterruptedTargetYPercent =
            expedition.LastTravelTargetYPercent;

        location.IsVisibleOnMap = true;
        location.IsDiscovered = true;
        location.RegionName = GetRegionName(
            location.MapXPercent,
            location.MapYPercent);
        location.TravelHoursFromCapital =
            ContinuousSimulationSystem.CalculateTravelHours(
                WorldMapNavigation.FindPath(
                WorldMapNavigation.CapitalXPercent,
                WorldMapNavigation.CapitalYPercent,
                location.MapXPercent,
                location.MapYPercent));

        expedition.LocationId = location.Id;
        expedition.Phase = CommanderState.AtLocation;
        expedition.CurrentMapXPercent = location.MapXPercent;
        expedition.CurrentMapYPercent = location.MapYPercent;
        expedition.TargetMapXPercent = location.MapXPercent;
        expedition.TargetMapYPercent = location.MapYPercent;
        expedition.RemainingRouteCells = 0;
        expedition.RouteLengthCells = 0;
        expedition.RouteIndex = 0;
        expedition.RouteDelayHoursRemaining = 0.0;
        expedition.ActiveActivity = null;
        expedition.Route = new List<MapPointData>
        {
            new MapPointData(location.MapXPercent, location.MapYPercent)
        };
        expedition.LastTravelPoints.Clear();
        commander.State = CommanderState.AtLocation;

        resultMessage =
            "Вы обнаружили локацию «" + location.Name +
            "». Армия остановилась у неё.";

        return true;
    }

    public bool TryResumeInterruptedRoute(out string resultMessage)
    {
        resultMessage = "Прерванного маршрута больше нет.";

        if (!HasActiveExpedition || !ActiveExpedition.HasInterruptedRoute)
            return false;

        ExpeditionData expedition = ActiveExpedition;
        CommanderData commander = FindCommander(expedition.CommanderId);

        if (commander == null)
            return false;

        CommanderState resumePhase = expedition.InterruptedPhase;
        string targetLocationId = expedition.InterruptedTargetLocationId;
        float targetX = expedition.InterruptedTargetXPercent;
        float targetY = expedition.InterruptedTargetYPercent;

        List<MapPointData> route = WorldMapNavigation.FindPath(
            expedition.CurrentMapXPercent,
            expedition.CurrentMapYPercent,
            targetX,
            targetY);
        int routeCells = WorldMapNavigation.CalculateRouteCells(route);

        expedition.HasInterruptedRoute = false;
        expedition.PendingDecision = null;

        if (route.Count < 2 || routeCells <= 0)
        {
            if (resumePhase == CommanderState.ReturningToCastle)
            {
                string delivered = CompleteExpeditionReturn();
                resultMessage = "Армия уже у столицы. " + delivered;
                return true;
            }

            expedition.LocationId = targetLocationId;
            expedition.Phase = CommanderState.AtLocation;
            commander.State = CommanderState.AtLocation;
            resultMessage = "Армия уже достигла прежней цели.";
            return true;
        }

        expedition.LocationId = targetLocationId;
        expedition.Phase = resumePhase == CommanderState.ReturningToCastle
            ? CommanderState.ReturningToCastle
            : CommanderState.TravellingToLocation;
        expedition.RemainingRouteCells = routeCells;
        expedition.RouteLengthCells = routeCells;
        expedition.Route = route;
        expedition.RouteIndex = 0;
        expedition.RouteDelayHoursRemaining = 0.0;
        expedition.ActiveActivity = null;
        expedition.TargetMapXPercent = route[route.Count - 1].XPercent;
        expedition.TargetMapYPercent = route[route.Count - 1].YPercent;
        expedition.LastTravelPoints.Clear();

        commander.State = expedition.Phase;

        resultMessage = expedition.Phase == CommanderState.ReturningToCastle
            ? "Армия продолжила прерванное возвращение в столицу."
            : "Армия продолжила прежний маршрут от обнаруженной локации.";

        return true;
    }

    public LocationData RevealLocationNear(float xPercent, float yPercent)
    {
        LocationData nearest = null;
        float nearestDistanceSquared = float.MaxValue;

        foreach (LocationData location in Locations)
        {
            if (location.IsWaypoint || location.IsVisibleOnMap)
                continue;

            if (!WorldMapNavigation.IsWithinDiscoveryRadius(
                    xPercent,
                    yPercent,
                    location.MapXPercent,
                    location.MapYPercent))
            {
                continue;
            }

            float deltaX = location.MapXPercent - xPercent;
            float deltaY = location.MapYPercent - yPercent;
            float distanceSquared = deltaX * deltaX + deltaY * deltaY;

            if (distanceSquared < nearestDistanceSquared)
            {
                nearest = location;
                nearestDistanceSquared = distanceSquared;
            }
        }

        if (nearest != null)
        {
            nearest.RegionName = GetRegionName(
                nearest.MapXPercent,
                nearest.MapYPercent);
            nearest.IsVisibleOnMap = true;
            nearest.IsDiscovered = true;
            nearest.TravelHoursFromCapital =
                ContinuousSimulationSystem.CalculateTravelHours(
                    WorldMapNavigation.FindPath(
                        WorldMapNavigation.CapitalXPercent,
                        WorldMapNavigation.CapitalYPercent,
                        nearest.MapXPercent,
                        nearest.MapYPercent));
        }

        return nearest;
    }

    public static string GetRegionName(float xPercent, float yPercent)
    {
        // Сначала делим карту по горизонтали. Так северо-запад не превращается
        // автоматически в "Север", а три тестовые зоны действительно дают
        // Запад / Север / Восток.
        if (xPercent < 34f)
            return "Западные земли";

        if (xPercent > 66f)
            return "Восточные земли";

        if (yPercent < 40f)
            return "Северные земли";

        return "Центральные земли";
    }

    private bool ValidateExpeditionFighters(
        List<string> selectedFighterIds,
        out string resultMessage)
    {
        if (selectedFighterIds == null || selectedFighterIds.Count == 0)
        {
            resultMessage = "Сначала выберите хотя бы одного бойца для экспедиции.";
            return false;
        }

        HashSet<string> uniqueSelectedIds =
            new HashSet<string>(selectedFighterIds);

        if (uniqueSelectedIds.Count != selectedFighterIds.Count)
        {
            resultMessage = "В составе экспедиции один боец указан несколько раз.";
            return false;
        }

        foreach (string fighterId in uniqueSelectedIds)
        {
            if (FindFighter(fighterId) == null)
            {
                resultMessage =
                    "В составе экспедиции найден неизвестный боец: " +
                    fighterId + ".";
                return false;
            }
        }

        resultMessage = string.Empty;
        return true;
    }

    private LocationData GetOrCreateRouteWaypoint(
        float xPercent,
        float yPercent)
    {
        LocationData waypoint = FindLocation(RouteWaypointId);

        if (waypoint == null)
        {
            waypoint = new LocationData(
                RouteWaypointId,
                "Точка маршрута",
                0,
                "—");
            waypoint.IsWaypoint = true;
            Locations.Add(waypoint);
        }

        waypoint.MapXPercent = WorldMapNavigation.ClampMapX(xPercent);
        waypoint.MapYPercent = WorldMapNavigation.ClampMapY(yPercent);
        waypoint.RegionId = "waypoint";
        waypoint.RegionName = GetRegionName(
            waypoint.MapXPercent,
            waypoint.MapYPercent);
        waypoint.TravelHoursFromCapital =
            ContinuousSimulationSystem.CalculateTravelHours(
                WorldMapNavigation.FindPath(
                    WorldMapNavigation.CapitalXPercent,
                    WorldMapNavigation.CapitalYPercent,
                    waypoint.MapXPercent,
                    waypoint.MapYPercent));
        waypoint.IsVisibleOnMap = false;
        waypoint.IsDiscovered = true;
        waypoint.IsExplored = false;
        waypoint.IsWaypoint = true;
        return waypoint;
    }

    private static void CancelRoadActivity(ExpeditionData expedition)
    {
        if (expedition != null && expedition.IsRoadStopInProgress)
            expedition.ActiveActivity = null;
    }
}
