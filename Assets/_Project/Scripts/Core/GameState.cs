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

    public FighterData(string id, string name, string role, int level)
    {
        Id = id;
        Name = name;
        Role = role;
        Level = level;
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

    public LocationData(string id, string name, int distanceDays, string threat)
    {
        Id = id;
        Name = name;
        DistanceDays = distanceDays;
        Threat = threat;
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

    // Снабжение принадлежит единственной армии, а не конкретному командиру.
    public int ArmySupply;

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

    public void CreateNewGame()
    {
        Day = 1;
        Gold = 120;
        Food = 72;
        Population = 24;
        Mood = 64;
        ConsecutiveFoodShortageDays = 0;
        ArmySupply = 0;

        Commanders = new List<CommanderData>
        {
            new CommanderData("alric", "Сэр Альрик"),
            new CommanderData("mirena", "Леди Мирена"),
            new CommanderData("bran", "Бран Каменная Рука")
        };

        SelectedCommanderId = "alric";

        Fighters = new List<FighterData>
        {
            new FighterData("garrick", "Гаррик", "Гвардеец", 1),
            new FighterData("edric", "Эдрик", "Лучник", 1),
            new FighterData("marta", "Марта", "Лекарь", 1),
            new FighterData("torvin", "Торвин", "Копейщик", 1),
            new FighterData("agnessa", "Агнесса", "Разведчик", 1)
        };

        Locations = new List<LocationData>
        {
            new LocationData("ruins", "Затопленные руины", 2, "низкая"),
            new LocationData("mine", "Старая шахта", 3, "средняя"),
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

    public bool TryStartExpedition(string locationId, out string resultMessage)
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
            PendingDecision = null
        };

        foreach (FighterData fighter in Fighters)
            expedition.FighterIds.Add(fighter.Id);

        ActiveExpedition = expedition;
        commander.State = CommanderState.TravellingToLocation;

        resultMessage =
            commander.Name + " и " + Fighters.Count +
            " воинов получили приказ отправиться в локацию «" +
            location.Name + "». До завершения текущего дня приказ можно отменить.";

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

        resultMessage =
            "Приказ на отправку в локацию «" + location.Name + "» отменён. " +
            commander.Name + " и армия остаются в столице. День не завершён.";

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
            returnDays + ". До прибытия армия не защищает город.";

        return true;
    }
}
