using System.Collections.Generic;
using System.Linq;

// 12Е-9 (каталог §4.2, Н-97): функции присутствия — что конкретный человек
// даёт отряду в пути, пока он в походе. Базовая способность человека
// существует без особенности; остался дома — возможности нет. Здесь только
// то, что уже работает в игре (дела лагеря, ПР-09); новые функции
// добавляются сюда вместе с их механикой.
public sealed class PresenceFunction
{
    public readonly string Id;
    // «перевязка раненых».
    public readonly string Title;
    // Что даёт в пути.
    public readonly string Effect;
    // Кто даёт: любой из этих людей в отряде; пусто — любой боец отряда.
    public readonly IReadOnlyList<string> PersonIds;

    public PresenceFunction(string id, string title, string effect, params string[] personIds)
    {
        Id = id;
        Title = title;
        Effect = effect;
        PersonIds = personIds ?? new string[0];
    }

    public bool AnyFighter => PersonIds.Count == 0;
}

public static class PresenceFunctions
{
    public const string BandageId = "presence.bandage";
    public const string InspectId = "presence.inspect";
    public const string WatchId = "presence.watch";

    public static readonly PresenceFunction Bandage = new PresenceFunction(BandageId, "перевязка раненых",
        "на ночлеге раненым возвращается часть здоровья", CampRest.MartaId);

    public static readonly PresenceFunction Inspect = new PresenceFunction(InspectId, "осмотр окрестностей",
        "на ночлеге может найтись место рядом", CampRest.AgnessaId, HomePeopleService.OstafiyId);

    public static readonly PresenceFunction Watch = new PresenceFunction(WatchId, "дозор",
        "на ночлеге у дороги или воды кто-то не спит");

    public static readonly IReadOnlyList<PresenceFunction> All = new List<PresenceFunction> { Bandage, Inspect, Watch };

    // Кто в этом составе даёт функцию (первый по порядку функции), или null.
    public static string Provider(GameState state, PresenceFunction function, IEnumerable<string> party, IEnumerable<string> fighters)
    {
        if (state == null || function == null)
            return null;
        if (function.AnyFighter)
        {
            foreach (string fighterId in fighters ?? Enumerable.Empty<string>())
            {
                // У бойца может не быть записи жителя — тогда он просто в отряде.
                ResidentState resident = HomePeopleService.Find(state, fighterId);
                if (resident == null || resident.IsAlive)
                    return fighterId;
            }
            return null;
        }
        foreach (string personId in function.PersonIds)
        {
            if (party != null && party.Contains(personId) && IsAlive(state, personId))
                return personId;
        }
        return null;
    }

    // Есть ли в этом Доме вообще кто-то, кто даёт функцию.
    public static bool ExistsInHome(GameState state, PresenceFunction function)
    {
        if (function.AnyFighter)
            return true;
        foreach (string personId in function.PersonIds)
        {
            if (IsAlive(state, personId))
                return true;
        }
        return false;
    }

    // Личные функции этого человека (для карточки кандидата). То, что может
    // любой боец, на карточке не повторяется.
    public static List<PresenceFunction> ProvidedBy(string personId)
    {
        List<PresenceFunction> functions = new List<PresenceFunction>();
        foreach (PresenceFunction function in All)
        {
            if (!function.AnyFighter && function.PersonIds.Contains(personId))
                functions.Add(function);
        }
        return functions;
    }

    // Строки «В пути» для состава: кто что даёт, а без кого чего не будет.
    public static List<string> DescribeRoad(GameState state, IEnumerable<string> party, IEnumerable<string> fighters)
    {
        List<string> covered = new List<string>();
        List<string> missing = new List<string>();
        foreach (PresenceFunction function in All)
        {
            if (!ExistsInHome(state, function))
                continue;
            string provider = Provider(state, function, party, fighters);
            if (provider != null)
            {
                covered.Add(Capitalize(function.Title) + " — " + (function.AnyFighter ? "бойцы отряда" : Name(state, provider)) +
                            ": " + function.Effect + ".");
            }
            else
            {
                missing.Add("Без " + WhoGenitive(state, function) + " не будет: " + function.Title + ".");
            }
        }
        missing.AddRange(covered);
        return missing;
    }

    // «Марты», «Агнессы или Остафия», «бойца».
    private static string WhoGenitive(GameState state, PresenceFunction function)
    {
        if (function.AnyFighter)
            return "бойца";
        List<string> names = new List<string>();
        foreach (string personId in function.PersonIds)
        {
            if (IsAlive(state, personId))
                names.Add(Genitive(Name(state, personId)));
        }
        return string.Join(" или ", names);
    }

    // Родительный падеж для имён людей Дома: «Марта» → «Марты»,
    // «Агнесса» → «Агнессы», «Остафий» → «Остафия». Правило простое и
    // рассчитано на имена, которые есть в игре.
    public static string Genitive(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        if (name.EndsWith("ий"))
            return name.Substring(0, name.Length - 2) + "ия";
        if (name.EndsWith("а"))
        {
            string stem = name.Substring(0, name.Length - 1);
            char last = stem.Length > 0 ? stem[stem.Length - 1] : ' ';
            return stem + ("гкхжшщчц".IndexOf(last) >= 0 ? "и" : "ы");
        }
        if (name.EndsWith("я"))
            return name.Substring(0, name.Length - 1) + "и";
        if (name.EndsWith("й") || name.EndsWith("ь"))
            return name.Substring(0, name.Length - 1) + "я";
        return name + "а";
    }

    private static string Name(GameState state, string personId)
    {
        ResidentState resident = HomePeopleService.Find(state, personId);
        return resident != null ? resident.DisplayName : personId;
    }

    private static bool IsAlive(GameState state, string personId)
    {
        ResidentState resident = HomePeopleService.Find(state, personId);
        return resident != null && resident.IsAlive;
    }

    private static string Capitalize(string text)
    {
        return string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
    }
}
