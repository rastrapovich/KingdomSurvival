using System;
using System.Collections.Generic;
using System.Linq;

// 12У-E, пакет «Фракции на карте» (05.11.2026): минимальный слой без
// политической симуляции. Стороны — данные с устойчивыми строковыми ID (не
// enum), принадлежность групп и контроль поселений различаются, группы мира —
// постоянные личности с домом и назначенным маршрутом по существующим дорогам
// (режим PassiveRoutes: новую задачу группа сама не придумывает). Положение
// группы — функция игрового времени (WorldMapActorMotion), поэтому оно не
// зависит от видимости на экране и не плодит копий при подгрузке.
// Пять сторон, их названия и облик — рабочие предложения пакета, не канон;
// отношения между сторонами не заданы: знак не означает вражду и не
// запускает бой.
[Serializable]
public sealed class WorldMapFactionDefinition
{
    public const string Polity = "polity";
    public const string Organization = "organization";
    public const string Company = "company";

    public string Id = string.Empty;
    public string Name = string.Empty;
    public string Kind = Polity;
    public string ParentId = string.Empty;
    public bool CanControlTerritory;
    public bool Enabled = true;
    // Отображение (не идентичность): эмблема и знамя — записи набора арта,
    // фигура — набор шести ракурсов «<FigureSet>_<Direction>».
    public string Emblem = string.Empty;
    public string Banner = string.Empty;
    public string FigureSet = string.Empty;
    public string PrimaryColor = "#808080";
    public string SecondaryColor = "#D8C9A8";
    public string Status = "рабочее предложение, не канон";

    public WorldMapFactionDefinition Clone() => (WorldMapFactionDefinition)MemberwiseClone();
}

// Группа мира: роль, сторона, облик, сколько в регионе (бюджет мира, не
// лимит на участок карты).
[Serializable]
public sealed class WorldMapGroupRule
{
    public const string Patrol = "patrol";
    public const string Caravan = "caravan";
    public const string Master = "master";
    public const string Band = "band";

    public string Id = string.Empty;
    public string Role = Patrol;
    public string Name = string.Empty;
    public string FactionId = string.Empty;
    public string FigureSet = string.Empty;
    public int Count = 1;
    public bool Enabled = true;
    public string Description = string.Empty;

    public WorldMapGroupRule Clone() => (WorldMapGroupRule)MemberwiseClone();
}

[Serializable]
public sealed class GeneratedMapActor
{
    public string ActorId = string.Empty;
    public string RuleId = string.Empty;
    public string Role = string.Empty;
    public string Name = string.Empty;
    public string FactionId = string.Empty;
    public string FigureSet = string.Empty;
    public string Description = string.Empty;
    // Дом: «village-N», «camp-N» или «feature-N».
    public string HomeId = string.Empty;
    public float HomeXPercent;
    public float HomeYPercent;
    // Маршрут: точки по клеткам пути; ходит туда-обратно (Loop — по кругу).
    public List<MapPointData> Route = new List<MapPointData>();
    public List<int> RouteCells = new List<int>();
    public bool Loop;
    // Остановка у точки маршрута (стоянка в деревне, у заставы), часов.
    public float WaitHours = 2f;
    // Travel — назначенный маршрут; Blocked — маршрут не найден (причина).
    public string State = "Travel";
    public string BlockReason = string.Empty;
    // Сдвиг по времени, чтобы одинаковые группы не шли в ногу.
    public float PhaseHours;
}

public static class WorldMapFactionCatalog
{
    public static List<WorldMapFactionDefinition> DefaultFactions()
    {
        WorldMapFactionDefinition D(string id, string name, string kind, string parent, bool control, string set, string emblem, string banner, string a, string b) =>
            new WorldMapFactionDefinition
            {
                Id = id, Name = name, Kind = kind, ParentId = parent, CanControlTerritory = control,
                FigureSet = set, Emblem = emblem, Banner = banner, PrimaryColor = a, SecondaryColor = b
            };
        return new List<WorldMapFactionDefinition>
        {
            D("draft_court_lands", "Княжий двор", WorldMapFactionDefinition.Polity, "", true, "Faction_Court", "Emblem_Court_A", "Banner_Court_Front", "#627985", "#D8C9A8"),
            D("draft_river_union", "Заречный союз", WorldMapFactionDefinition.Polity, "", true, "Faction_RiverUnion", "Emblem_RiverUnion_A", "Banner_RiverUnion_Front", "#A6654D", "#D8C9A8"),
            D("draft_watch_court_branch", "Заставные", WorldMapFactionDefinition.Organization, "draft_court_lands", false, "Faction_Watch", "Emblem_Watch_A", "Banner_Watch_Front", "#B49B62", "#655744"),
            D("draft_free_circle", "Вольный круг", WorldMapFactionDefinition.Organization, "", false, "Faction_FreeCircle", "Emblem_FreeCircle_A", "Banner_FreeCircle_Front", "#7B8562", "#DED0AE"),
            D("draft_grey_company", "Серые ватаги", WorldMapFactionDefinition.Company, "", false, "Faction_GreyCompany", "Emblem_GreyCompany_A", "Banner_GreyCompany_Front", "#77736A", "#C9C0AC"),
        };
    }

    public static List<WorldMapGroupRule> DefaultGroups()
    {
        WorldMapGroupRule G(string id, string role, string name, string faction, string set, int count, string description) =>
            new WorldMapGroupRule { Id = id, Role = role, Name = name, FactionId = faction, FigureSet = set, Count = count, Description = description };
        return new List<WorldMapGroupRule>
        {
            G("watch-patrol", WorldMapGroupRule.Patrol, "Дозор", "draft_watch_court_branch", "Patrol", 1, "Несколько верховых и пеших с копьями обходят дорогу."),
            G("trade-caravan", WorldMapGroupRule.Caravan, "Обоз", "draft_free_circle", "Caravan", 1, "Телеги с поклажей, возчики идут рядом с лошадьми."),
            G("wandering-master", WorldMapGroupRule.Master, "Странствующий мастер", "draft_free_circle", "Wandering_Master", 1, "Мастер с котомкой инструмента идёт от села к селу."),
            G("grey-band", WorldMapGroupRule.Band, "Ватага", "draft_grey_company", "Bandit", 1, "Вооружённые люди в серых плащах держатся своей тропы."),
        };
    }

    public static WorldMapFactionDefinition Find(IEnumerable<WorldMapFactionDefinition> factions, string id) =>
        factions?.FirstOrDefault(f => f != null && f.Id == id);

    // Верхняя держава по цепочке подчинения (для власти над местом); цикл или
    // слишком глубокая цепочка — null.
    public static WorldMapFactionDefinition TopPolity(IList<WorldMapFactionDefinition> factions, string id)
    {
        HashSet<string> seen = new HashSet<string>();
        WorldMapFactionDefinition f = Find(factions, id);
        while (f != null && seen.Add(f.Id) && seen.Count < 16)
        {
            if (f.Kind == WorldMapFactionDefinition.Polity)
                return f;
            f = string.IsNullOrEmpty(f.ParentId) ? null : Find(factions, f.ParentId);
        }
        return null;
    }

    // Проверка определений: уникальные ID, существующий родитель, нет циклов,
    // контроль территории — только у держав.
    public static List<GeneratedRegionIssue> Validate(IList<WorldMapFactionDefinition> factions, IList<WorldMapGroupRule> groups)
    {
        List<GeneratedRegionIssue> issues = new List<GeneratedRegionIssue>();
        void Add(string code, bool error, string message) => issues.Add(new GeneratedRegionIssue { Code = code, Error = error, Message = message, XPercent = 50f, YPercent = 50f });
        if (factions == null)
            return issues;
        foreach (IGrouping<string, WorldMapFactionDefinition> g in factions.Where(f => f != null).GroupBy(f => f.Id).Where(g => g.Count() > 1))
            Add("F01", true, $"Сторона «{g.Key}» объявлена {g.Count()} раза.");
        foreach (WorldMapFactionDefinition f in factions.Where(f => f != null))
        {
            if (string.IsNullOrWhiteSpace(f.Id))
                Add("F01", true, $"У стороны «{f.Name}» нет ID.");
            if (!string.IsNullOrEmpty(f.ParentId) && Find(factions, f.ParentId) == null)
                Add("F02", true, $"Сторона «{f.Id}»: родитель «{f.ParentId}» не найден.");
            if (f.CanControlTerritory && f.Kind != WorldMapFactionDefinition.Polity)
                Add("F03", false, $"Сторона «{f.Id}» ({f.Kind}) отмечена как владеющая землёй — контроль места разрешён только державе; отметка не используется.");
            HashSet<string> chain = new HashSet<string>();
            WorldMapFactionDefinition p = f;
            while (p != null && !string.IsNullOrEmpty(p.ParentId))
            {
                if (!chain.Add(p.Id))
                {
                    Add("F02", true, $"Сторона «{f.Id}»: цикл подчинения.");
                    break;
                }
                p = Find(factions, p.ParentId);
            }
        }
        if (groups != null)
        {
            foreach (WorldMapGroupRule rule in groups.Where(r => r != null && r.Enabled))
            {
                if (!string.IsNullOrEmpty(rule.FactionId) && Find(factions, rule.FactionId) == null)
                    Add("F04", false, $"Группа «{rule.Id}»: сторона «{rule.FactionId}» не найдена — группа без принадлежности не ставится.");
                if (Array.IndexOf(new[] { WorldMapGroupRule.Patrol, WorldMapGroupRule.Caravan, WorldMapGroupRule.Master, WorldMapGroupRule.Band }, rule.Role) < 0)
                    Add("F05", false, $"Группа «{rule.Id}»: роль «{rule.Role}» не поддержана — нужен код поведения.");
            }
        }
        return issues;
    }
}

// Положение группы на маршруте во времени мира: туда-обратно (или по кругу)
// со стоянками на концах. Скорость — клеток в час (обоз медленнее дозора).
public static class WorldMapActorMotion
{
    public static float SpeedHexesPerHour(string role)
    {
        switch (role)
        {
            case WorldMapGroupRule.Caravan: return 0.18f;
            case WorldMapGroupRule.Patrol: return 0.3f;
            case WorldMapGroupRule.Band: return 0.26f;
            default: return 0.24f;
        }
    }

    // Возвращает точку и направление движения (dx, dy в процентах холста).
    public static MapPointData PositionAt(GeneratedMapActor actor, double worldHour, WorldMapHexGrid grid, out float dirX, out float dirY, out bool waiting)
    {
        dirX = 0f;
        dirY = 1f;
        waiting = true;
        if (actor?.Route == null || actor.Route.Count == 0)
            return new MapPointData(actor?.HomeXPercent ?? 50f, actor?.HomeYPercent ?? 50f);
        if (actor.Route.Count == 1 || actor.State != "Travel")
            return actor.Route[0];
        List<double> cumulative = new List<double> { 0 };
        for (int i = 1; i < actor.Route.Count; i++)
            cumulative.Add(cumulative[i - 1] + grid.DistanceHexes(actor.Route[i - 1].XPercent, actor.Route[i - 1].YPercent, actor.Route[i].XPercent, actor.Route[i].YPercent));
        double length = cumulative[cumulative.Count - 1];
        if (length <= 0.0001)
            return actor.Route[0];
        double travel = length / Math.Max(0.01, SpeedHexesPerHour(actor.Role));
        double wait = Math.Max(0, actor.WaitHours);
        double cycle = actor.Loop ? travel + wait : 2 * (travel + wait);
        double t = (worldHour + actor.PhaseHours) % cycle;
        if (t < 0)
            t += cycle;
        double along;
        bool back = false;
        if (t < wait)
            return actor.Route[0];
        t -= wait;
        if (t < travel)
            along = t / travel * length;
        else if (actor.Loop)
            return actor.Route[actor.Route.Count - 1];
        else
        {
            t -= travel;
            if (t < wait)
                return actor.Route[actor.Route.Count - 1];
            t -= wait;
            along = length - Math.Min(1.0, t / travel) * length;
            back = true;
        }
        waiting = false;
        int k = 1;
        while (k < cumulative.Count - 1 && cumulative[k] < along)
            k++;
        double segment = Math.Max(0.0001, cumulative[k] - cumulative[k - 1]);
        float f = (float)((along - cumulative[k - 1]) / segment);
        MapPointData a = actor.Route[k - 1], b = actor.Route[k];
        dirX = (b.XPercent - a.XPercent) * (back ? -1f : 1f);
        dirY = (b.YPercent - a.YPercent) * (back ? -1f : 1f);
        return new MapPointData(a.XPercent + (b.XPercent - a.XPercent) * f, a.YPercent + (b.YPercent - a.YPercent) * f);
    }

    // Шесть ракурсов набора в порядке MapLabMarkerSet: Front, Front_Right,
    // Back_Right, Back, Back_Left, Front_Left. Направление на холсте: y вниз
    // (к зрителю) — Front. Стороны — стороны персонажа (без зеркала).
    public static readonly string[] Directions = { "Front", "Front_Right", "Back_Right", "Back", "Back_Left", "Front_Left" };

    // То же правило, что у фигурки героя MapLab (MapLabMarkerSet.Sector при
    // азимуте камеры 0): курс 180° (к зрителю) — Front, далее через 60°.
    public static int DirectionIndex(float dirX, float dirY)
    {
        double facing = Math.Atan2(dirX, -dirY) * 180.0 / Math.PI;
        int index = (int)Math.Round((180.0 - facing) / 60.0);
        return ((index % 6) + 6) % 6;
    }
}
