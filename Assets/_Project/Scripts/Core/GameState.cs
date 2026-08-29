using System;
using System.Collections.Generic;

// ============================================================
// СОСТОЯНИЕ КОМАНДИРА
// ============================================================

public enum CommanderState
{
    InCastle,             // Находится в столице
    TravellingToLocation, // Едет к найденной локации
    AtLocation,           // Действует внутри локации
    ReturningToCastle     // Возвращается в столицу
}

// ============================================================
// ОТДЕЛЬНЫЙ ПОСТОЯННЫЙ ВОИН
// Никаких отрядов и вложенных групп здесь нет.
// ============================================================

[Serializable]
public class FighterData
{
    public string Id;
    public string Name;
    public int Level;

    public FighterData(string id, string name, int level)
    {
        Id = id;
        Name = name;
        Level = level;
    }
}

// ============================================================
// КОМАНДИР
// Выбранный командир управляет одной армией из 4–6 воинов.
// ============================================================

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

// ============================================================
// НАЙДЕННАЯ ЛОКАЦИЯ
// Предполагаемая награда намеренно отсутствует.
// ============================================================

[Serializable]
public class LocationData
{
    public string Id;
    public string Name;
    public int DistanceDays;
    public string Threat;

    public LocationData(
        string id,
        string name,
        int distanceDays,
        string threat)
    {
        Id = id;
        Name = name;
        DistanceDays = distanceDays;
        Threat = threat;
    }
}

// ============================================================
// АКТИВНАЯ ЭКСПЕДИЦИЯ
// Одновременно может существовать не более одной экспедиции.
// ============================================================

[Serializable]
public class ExpeditionData
{
    public bool IsActive;
    public string CommanderId;
    public string LocationId;

    // Здесь находятся идентификаторы отдельных воинов.
    public List<string> FighterIds = new List<string>();

    public CommanderState Phase;
    public int DaysRemaining;
}

// ============================================================
// ОБЩЕЕ СОСТОЯНИЕ ИГРЫ
// Этот класс хранит игровые данные, но не управляет интерфейсом.
// ============================================================

[Serializable]
public class GameState
{
    public int Day;
    public int Gold;
    public int Food;
    public int Population;
    public int Mood;

    public string SelectedCommanderId;

    public List<CommanderData> Commanders;
    public List<FighterData> Fighters;
    public List<LocationData> Locations;

    public ExpeditionData ActiveExpedition;

    // Каждый житель потребляет ровно 1 пищу в день.
    public int DailyFoodConsumption
    {
        get { return Population; }
    }

    public bool HasActiveExpedition
    {
        get
        {
            return ActiveExpedition != null &&
                   ActiveExpedition.IsActive;
        }
    }

    // --------------------------------------------------------
    // СОЗДАНИЕ НОВОЙ ИГРЫ
    // --------------------------------------------------------

    public void CreateNewGame()
    {
        Day = 1;
        Gold = 120;
        Food = 72;
        Population = 24;
        Mood = 64;

        Commanders = new List<CommanderData>
        {
            new CommanderData("alric", "Сэр Альрик"),
            new CommanderData("mirena", "Леди Мирена"),
            new CommanderData("bran", "Бран Каменная Рука")
        };

        SelectedCommanderId = "alric";

        // Сейчас у армии пять отдельных постоянных воинов.
        // Это укладывается в утверждённый диапазон 4–6.
        Fighters = new List<FighterData>
        {
            new FighterData("garrick", "Гаррик", 1),
            new FighterData("edric", "Эдрик", 1),
            new FighterData("marta", "Марта", 1),
            new FighterData("torvin", "Торвин", 1),
            new FighterData("agnessa", "Агнесса", 1)
        };

        Locations = new List<LocationData>
        {
            new LocationData(
                "ruins",
                "Затопленные руины",
                2,
                "низкая"),

            new LocationData(
                "mine",
                "Старая шахта",
                3,
                "средняя"),

            new LocationData(
                "forest",
                "Чёрный лес",
                5,
                "высокая")
        };

        ActiveExpedition = null;
    }

    // --------------------------------------------------------
    // ПОИСК ИГРОВЫХ ОБЪЕКТОВ
    // --------------------------------------------------------

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

    // --------------------------------------------------------
    // ВЫБОР КОМАНДИРА
    // --------------------------------------------------------

    public bool SelectCommanderByName(string commanderName)
    {
        // Во время экспедиции заменить командира нельзя.
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

    // --------------------------------------------------------
    // ОТПРАВКА ЭКСПЕДИЦИИ
    // --------------------------------------------------------

    public bool TryStartExpedition(
        string locationId,
        out string resultMessage)
    {
        if (HasActiveExpedition)
        {
            resultMessage =
                "Нельзя отправить вторую экспедицию: один поход уже активен.";

            return false;
        }

        if (Fighters.Count < 4 || Fighters.Count > 6)
        {
            resultMessage =
                "Армия должна состоять из 4–6 отдельных воинов.";

            return false;
        }

        CommanderData commander = GetSelectedCommander();
        LocationData location = FindLocation(locationId);

        if (commander == null || location == null)
        {
            resultMessage =
                "Не удалось найти командира или выбранную локацию.";

            return false;
        }

        ExpeditionData expedition = new ExpeditionData();

        expedition.IsActive = true;
        expedition.CommanderId = commander.Id;
        expedition.LocationId = location.Id;
        expedition.Phase = CommanderState.TravellingToLocation;
        expedition.DaysRemaining = location.DistanceDays;

        // В экспедицию отправляются именно отдельные воины.
        foreach (FighterData fighter in Fighters)
            expedition.FighterIds.Add(fighter.Id);

        ActiveExpedition = expedition;
        commander.State = CommanderState.TravellingToLocation;

        resultMessage =
            commander.Name +
            " и " +
            Fighters.Count +
            " воинов отправлены в локацию «" +
            location.Name +
            "». Столица осталась без этой армии.";

        return true;
    }    
    // --------------------------------------------------------
    // ДОСРОЧНОЕ ВОЗВРАЩЕНИЕ ЭКСПЕДИЦИИ
    // --------------------------------------------------------

    public bool TryOrderReturn(out string resultMessage)
    {
        if (!HasActiveExpedition)
        {
            resultMessage =
                "Сейчас нет активной экспедиции.";

            return false;
        }

        if (ActiveExpedition.Phase ==
            CommanderState.ReturningToCastle)
        {
            resultMessage =
                "Экспедиция уже возвращается в столицу.";

            return false;
        }

        LocationData location =
            FindLocation(ActiveExpedition.LocationId);

        CommanderData commander =
            FindCommander(ActiveExpedition.CommanderId);

        if (location == null || commander == null)
        {
            resultMessage =
                "Не удалось определить данные экспедиции.";

            return false;
        }

        int returnDays;

        if (ActiveExpedition.Phase ==
            CommanderState.TravellingToLocation)
        {
            // Считаем, сколько дней армия уже прошла от столицы.
            int travelledDays =
                location.DistanceDays -
                ActiveExpedition.DaysRemaining;

            // Минимум один день нужен, чтобы приказ
            // не телепортировал армию обратно мгновенно.
            returnDays = Math.Max(1, travelledDays);
        }
        else
        {
            // Если армия уже достигла локации,
            // возвращение занимает полное расстояние.
            returnDays = location.DistanceDays;
        }

        ActiveExpedition.Phase =
            CommanderState.ReturningToCastle;

        ActiveExpedition.DaysRemaining = returnDays;

        commander.State =
            CommanderState.ReturningToCastle;

        resultMessage =
            commander.Name +
            " получил приказ возвращаться. " +
            "До столицы осталось дней: " +
            returnDays +
            ". До прибытия армия не защищает город.";

        return true;
    }
}
