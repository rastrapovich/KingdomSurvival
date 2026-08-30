using System;
using System.Collections.Generic;

public enum CommanderState
{
    InCastle,
    TravellingToLocation,
    AtLocation,
    ReturningToCastle
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
    public int DistanceDays;
    public string Threat;
    public int ExplorationDays;
    public int RewardArmyGold;
    public int RewardArmySupply;
    public bool IsExplored;

    public LocationData(
        string id,
        string name,
        int distanceDays,
        string threat,
        int explorationDays = 0,
        int rewardArmyGold = 0,
        int rewardArmySupply = 0)
    {
        Id = id;
        Name = name;
        DistanceDays = distanceDays;
        Threat = threat;
        ExplorationDays = explorationDays;
        RewardArmyGold = rewardArmyGold;
        RewardArmySupply = rewardArmySupply;
        IsExplored = false;
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
    public int DaysRemaining;
    public int StartedOnDay;

    public bool IsExplorationInProgress;
    public int ExplorationDaysRemaining;

    // Значимое происшествие не меняет канонические состояния командира.
    // Это отдельное состояние самой экспедиции: она ждёт приказа короля.
    public ExpeditionDecisionOccurrence PendingDecision;
    public List<string> UsedDecisionIds = new List<string>();
}

[Serializable]
public class GameState
{
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

    public bool CanCancelExpeditionBeforeDayEnd =>
        HasActiveExpedition &&
        ActiveExpedition.StartedOnDay == Day &&
        ActiveExpedition.Phase == CommanderState.TravellingToLocation;

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
                expedition.IsExplorationInProgress)
            {
                return false;
            }

            LocationData location = FindLocation(expedition.LocationId);

            return location != null &&
                   location.ExplorationDays > 0 &&
                   !location.IsExplored &&
                   ArmySupply >= ExpeditionSupplyConsumption;
        }
    }

    public void CreateNewGame()
    {
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

        Locations = new List<LocationData>
        {
            // Затопленные руины: короткое исследование с большой наградой снабжением.
            new LocationData(
                "ruins",
                "Затопленные руины",
                2,
                "низкая",
                1,
                100,
                200),

            // Старая шахта: длиннее и дороже по снабжению, зато даёт чистое золото.
            new LocationData(
                "mine",
                "Старая шахта",
                3,
                "средняя",
                2,
                300,
                0),

            new LocationData("forest", "Чёрный лес", 5, "высокая")
        };

        ActiveExpedition = null;
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
        if (!CanAdjustArmySupply || Food <= 0)
            return false;

        Food--;
        ArmySupply++;
        return true;
    }

    public bool TryRemoveArmySupply()
    {
        if (!CanAdjustArmySupply || ArmySupply <= 0)
            return false;

        ArmySupply--;
        Food++;
        return true;
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

        if (selectedFighterIds == null || selectedFighterIds.Count == 0)
        {
            resultMessage =
                "Сначала выберите хотя бы одного бойца для экспедиции.";
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

        CommanderData commander = GetSelectedCommander();
        LocationData location = FindLocation(locationId);

        if (commander == null || location == null)
        {
            resultMessage = "Не удалось найти командира или выбранную локацию.";
            return false;
        }

        ExpeditionData expedition = new ExpeditionData
        {
            IsActive = true,
            CommanderId = commander.Id,
            LocationId = location.Id,
            Phase = CommanderState.TravellingToLocation,
            DaysRemaining = location.DistanceDays,
            StartedOnDay = Day,
            IsExplorationInProgress = false,
            ExplorationDaysRemaining = 0,
            PendingDecision = null
        };

        // Сохраняем порядок общей армии, чтобы состав всегда одинаково
        // отображался в интерфейсе и донесениях.
        foreach (FighterData fighter in Fighters)
        {
            if (uniqueSelectedIds.Contains(fighter.Id))
                expedition.FighterIds.Add(fighter.Id);
        }

        ConsecutiveExpeditionSupplyShortageDays = 0;
        ActiveExpedition = expedition;
        commander.State = CommanderState.TravellingToLocation;

        resultMessage =
            commander.Name + " и " + expedition.FighterIds.Count +
            " воинов получили приказ отправиться в локацию «" +
            location.Name + "». В столице осталось бойцов: " +
            GarrisonFighterCount + ", сила гарнизона: " +
            GarrisonDefensePower + "/" + TotalArmyDefensePower +
            ". До завершения текущего дня приказ можно отменить.";

        return true;
    }

    public bool TryCancelExpeditionBeforeDayEnd(out string resultMessage)
    {
        if (!CanCancelExpeditionBeforeDayEnd)
        {
            resultMessage = "Отменить отправку уже нельзя. Используйте приказ о возвращении.";
            return false;
        }

        CommanderData commander = FindCommander(ActiveExpedition.CommanderId);
        LocationData location = FindLocation(ActiveExpedition.LocationId);

        if (commander == null || location == null)
        {
            resultMessage = "Не удалось определить данные экспедиции.";
            return false;
        }

        commander.State = CommanderState.InCastle;
        ActiveExpedition.IsActive = false;
        ActiveExpedition = null;
        ConsecutiveExpeditionSupplyShortageDays = 0;

        resultMessage =
            "Приказ на отправку в локацию «" + location.Name + "» отменён. " +
            commander.Name + " и выбранные бойцы остаются в столице. " +
            "День не завершён.";

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
            expedition.Phase != CommanderState.AtLocation ||
            expedition.IsExplorationInProgress ||
            location.ExplorationDays <= 0 ||
            location.IsExplored)
        {
            return false;
        }

        int requiredSupply = ExpeditionSupplyConsumption;

        if (ArmySupply < requiredSupply)
        {
            resultMessage =
                "Для исследования нужен полный дневной рацион. Требуется снабжения: " +
                requiredSupply + ".";
            return false;
        }

        expedition.IsExplorationInProgress = true;
        expedition.ExplorationDaysRemaining = location.ExplorationDays;

        resultMessage =
            "Отряд начал исследование локации «" + location.Name +
            "». Требуется дней: " + location.ExplorationDays +
            ". До завершения исследования приказ о возвращении недоступен.";

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

        if (ActiveExpedition.IsExplorationInProgress)
        {
            resultMessage =
                "Исследование уже начато. Сначала завершите текущий день исследования.";
            return false;
        }

        if (CanCancelExpeditionBeforeDayEnd)
        {
            resultMessage = "Текущий день ещё не завершён. Сначала можно полностью отменить отправку.";
            return false;
        }

        if (ActiveExpedition.Phase == CommanderState.ReturningToCastle)
        {
            resultMessage = "Экспедиция уже возвращается в столицу.";
            return false;
        }

        LocationData location = FindLocation(ActiveExpedition.LocationId);
        CommanderData commander = FindCommander(ActiveExpedition.CommanderId);

        if (location == null || commander == null)
        {
            resultMessage = "Не удалось определить данные экспедиции.";
            return false;
        }

        int returnDays;

        if (ActiveExpedition.Phase == CommanderState.TravellingToLocation)
        {
            int travelledDays = location.DistanceDays - ActiveExpedition.DaysRemaining;
            returnDays = Math.Max(1, travelledDays);
        }
        else
        {
            returnDays = location.DistanceDays;
        }

        ActiveExpedition.Phase = CommanderState.ReturningToCastle;
        ActiveExpedition.DaysRemaining = returnDays;
        commander.State = CommanderState.ReturningToCastle;

        resultMessage =
            commander.Name + " получил приказ возвращаться. До столицы осталось дней: " +
            returnDays + ". До прибытия бойцы экспедиции не усиливают гарнизон.";

        return true;
    }

    public bool ForceReturnFromSupplyFailure(out string resultMessage)
    {
        resultMessage = "Не удалось начать вынужденное возвращение.";

        if (!HasActiveExpedition)
            return false;

        ExpeditionData expedition = ActiveExpedition;
        LocationData location = FindLocation(expedition.LocationId);
        CommanderData commander = FindCommander(expedition.CommanderId);

        if (location == null || commander == null)
            return false;

        // Голод важнее ожидающего приказа или уже начатого исследования:
        // отряд прекращает другие действия и занимается возвращением.
        expedition.PendingDecision = null;
        expedition.IsExplorationInProgress = false;
        expedition.ExplorationDaysRemaining = 0;

        if (expedition.Phase == CommanderState.ReturningToCastle)
        {
            commander.State = CommanderState.ReturningToCastle;
            resultMessage =
                "Отряд уже возвращается в столицу и продолжает обратный путь.";
            return true;
        }

        int returnDays;

        if (expedition.Phase == CommanderState.TravellingToLocation)
        {
            int travelledDays = location.DistanceDays - expedition.DaysRemaining;
            returnDays = Math.Max(1, travelledDays);
        }
        else
        {
            returnDays = location.DistanceDays;
        }

        expedition.Phase = CommanderState.ReturningToCastle;
        expedition.DaysRemaining = returnDays;
        commander.State = CommanderState.ReturningToCastle;

        resultMessage =
            "Поход сорван: из-за второй подряд нехватки снабжения отряд вынужденно возвращается. " +
            "До столицы осталось дней: " + returnDays + ".";

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

        ActiveExpedition.IsExplorationInProgress = false;
        ActiveExpedition.ExplorationDaysRemaining = 0;
        ActiveExpedition.IsActive = false;

        return
            "В столицу передано: золото +" + deliveredGold +
            ", пища +" + deliveredFood + ".";
    }
}
