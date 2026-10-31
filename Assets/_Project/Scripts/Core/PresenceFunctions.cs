using System.Collections.Generic;
using System.Linq;

// 12Е-9 (каталог §4.2, Н-97): функции присутствия — что конкретный человек
// даёт отряду в пути, пока он в походе. Базовая способность человека
// существует без особенности; нет его в отряде — возможности нет. Здесь только
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
        "на ночлеге может найтись место рядом", CampRest.AgnessaId, CampRest.OstafiyId);

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
                ResidentState resident = PeopleService.Find(state, fighterId);
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

    private static bool IsAlive(GameState state, string personId)
    {
        ResidentState resident = PeopleService.Find(state, personId);
        return resident != null && resident.IsAlive;
    }
}
