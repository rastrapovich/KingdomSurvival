using System;
using System.Collections.Generic;
using System.Linq;

// Временные необязательные встречи на глобальной карте (просьба пользователя
// 19.11.2026), [РАБОЧЕЕ — ПРОТОТИП]. Помимо постоянных групп: проходящая
// ватага, охотящаяся стая, зверь у дороги, чужая стычка, падальщики у
// недавней находки отряда.
//   • Появление по контексту: местность места, время суток, недавние
//     встречи (тот же вид не повторяется сразу), передышка после боя, общий
//     бюджет угроз рядом, зачищенный участок не наполняется. Место — вне
//     обзора отряда; проходящая группа входит в обзор сама, с края.
//   • Своё занятие и срок по игровому времени (пауза его останавливает).
//   • Срок вышел — группа уходит; запись исчезает только вне обзора.
//   • Пока идёт разговор, бой, погоня или схватка — по таймеру не уходит.
//   • Необязательна: пройти мимо можно без штрафа; сюжетных сведений нет.
//   • Память: ID по счётчику партии, состав, занятие и срок — в сохранении;
//     загрузка, камера и вид карты встречу не пересоздают.
public static partial class WorldPopulationService
{
    public static GeneratedMapActor DefinitionOf(GameState state, PopulationGroupState group)
    {
        if (group == null)
            return null;
        if (group.Temporary)
            return group.TempDefinition;
        return Definition(state, group.DefinitionId);
    }

    private static void TemporaryStep(TickContext ctx, double t)
    {
        WorldPopulationStateData data = ctx.Data;
        WorldPopulationSettings s = ctx.Settings;
        TrackFinds(ctx, t);
        double sight = WorldMapMovementRules.Current.FogRevealRadiusHexes + SightBonusHexes;

        // Срок и уход.
        foreach (PopulationGroupState g in data.Groups)
        {
            if (g == null || !g.Temporary || !g.IsActive)
                continue;
            if (IsEngaged(ctx, g))
                continue;
            if (g.Activity != PopulationGroupState.Leave && t >= g.ExpireHour)
                BeginLeave(ctx, g, sight);
            if (g.Activity == PopulationGroupState.Leave)
            {
                bool seen = ctx.HeroOnMap && ctx.Grid.DistanceHexes(ctx.HeroX, ctx.HeroY, g.X, g.Y) <= sight + 0.5;
                if (!seen)
                {
                    g.Status = PopulationGroupState.Gone;
                    g.Outcome = string.IsNullOrEmpty(g.Outcome) ? "left" : g.Outcome;
                }
                else if (ctx.Grid.DistanceHexes(g.X, g.Y, g.ExitX, g.ExitY) < 0.3)
                    BeginLeave(ctx, g, sight);
            }
        }
        PruneGone(data);

        // Появление.
        if (t < data.NextTemporaryCheckHour)
            return;
        data.NextTemporaryCheckHour = t + s.TemporaryCheckHours;
        if (!s.TemporaryEnabled || !ctx.HeroOnMap || PendingContact(ctx.State) != null || t < data.RespiteUntilHour)
            return;
        int checkIndex = (int)Math.Floor(t / s.TemporaryCheckHours);
        Random random = new Random(WorldMapGenerator.StreamSeed(ctx.State.GeneratedMap.Seed, "population.temp.check." + checkIndex));
        if (random.NextDouble() >= s.TemporaryChance)
            return;
        if (data.Groups.Count(g => g.Temporary && g.IsActive) >= s.TemporaryMaxActive)
            return;
        int nearby = data.Groups.Count(g => g.IsActive && WorldPopulationBehavior.IsHostileCapable(g.Behavior) &&
                                            ctx.Grid.DistanceHexes(ctx.HeroX, ctx.HeroY, g.X, g.Y) <= 10.0);
        if (nearby >= s.TemporaryNearbyThreatLimit)
            return;
        TrySpawnTemporary(ctx, t, random, sight);
    }

    private static bool IsEngaged(TickContext ctx, PopulationGroupState g)
    {
        if (!string.IsNullOrEmpty(g.ConflictId) || g.Activity == PopulationGroupState.Conflict)
            return true;
        if ((g.Activity == PopulationGroupState.Chase || g.Activity == PopulationGroupState.Observe) && g.TargetId == HeroTarget)
            return true;
        return ctx.Data.Contacts.Any(c => c != null && c.GroupId == g.GroupId && c.Status != PopulationContactState.Resolved);
    }

    // Уход: проходящая — к концу своего пути, остальные — прочь от отряда
    // за пределы обзора.
    private static void BeginLeave(TickContext ctx, PopulationGroupState g, double sight)
    {
        WorldMapHexGrid grid = ctx.Grid;
        float fromX = ctx.HeroOnMap ? ctx.HeroX : g.X, fromY = ctx.HeroOnMap ? ctx.HeroY : g.Y;
        double dx = grid.PercentToPixelX(g.X) - grid.PercentToPixelX(fromX);
        double dy = grid.PercentToPixelY(g.Y) - grid.PercentToPixelY(fromY);
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-3)
        {
            dx = 1;
            dy = 0;
            length = 1;
        }
        double reach = (sight + 3.0) * grid.HexWidth;
        double ex = grid.PercentToPixelX(g.X) + dx / length * reach;
        double ey = grid.PercentToPixelY(g.Y) + dy / length * reach;
        g.ExitX = Math.Max(0.5f, Math.Min(99.5f, grid.PixelToPercentX(ex)));
        g.ExitY = Math.Max(0.5f, Math.Min(99.5f, grid.PixelToPercentY(ey)));
        if (g.Activity != PopulationGroupState.Leave)
        {
            StartActivity(g, PopulationGroupState.Leave, string.Empty, 0, 0);
            g.ActivityUntilHour = 0;
        }
    }

    private static void PruneGone(WorldPopulationStateData data)
    {
        List<PopulationGroupState> gone = data.Groups.Where(g => g.Temporary && g.Status == PopulationGroupState.Gone).ToList();
        if (gone.Count <= 30)
            return;
        foreach (PopulationGroupState g in gone.OrderBy(g => g.ExpireHour).Take(gone.Count - 30))
            data.Groups.Remove(g);
    }

    // Недавние находки: место, впервые исследованное после начала учёта.
    private static void TrackFinds(TickContext ctx, double t)
    {
        WorldPopulationStateData data = ctx.Data;
        List<LocationData> locations = ctx.State.Locations;
        if (locations == null)
            return;
        bool first = data.NextTemporaryCheckHour <= 0;
        foreach (LocationData location in locations)
        {
            if (location == null || location.IsWaypoint || !location.IsExplored || string.IsNullOrEmpty(location.Id) ||
                data.KnownExploredIds.Contains(location.Id))
                continue;
            data.KnownExploredIds.Add(location.Id);
            if (!first)
                data.RecentFinds.Add(new PopulationTimedMark { Key = location.Id, Hour = t, X = location.MapXPercent, Y = location.MapYPercent });
        }
        data.RecentFinds.RemoveAll(f => t - f.Hour > ctx.Settings.TemporaryFindHours);
        if (first)
            data.NextTemporaryCheckHour = t + ctx.Settings.TemporaryCheckHours;
    }

    private static void TrySpawnTemporary(TickContext ctx, double t, Random random, double sight)
    {
        WorldPopulationStateData data = ctx.Data;
        WorldPopulationSettings s = ctx.Settings;
        double hourOfDay = ((t % 24.0) + 24.0) % 24.0;
        bool night = hourOfDay >= 20.0 || hourOfDay < 5.0;
        List<(WorldTemporaryEncounterKind Kind, double Weight)> options = new List<(WorldTemporaryEncounterKind, double)>();
        foreach (WorldTemporaryEncounterKind kind in s.TemporaryKinds.Where(k => k.Enabled).OrderBy(k => k.Id, StringComparer.Ordinal))
        {
            WorldPopulationPreset preset = s.Find(kind.BasePresetId);
            if (preset == null || !preset.Enabled || !WorldPopulationCatalog.CheckUnits(preset, out _))
                continue;
            PopulationTimedMark memory = data.TemporaryKindMemory.FirstOrDefault(m => m.Key == kind.Id);
            if (memory != null && t - memory.Hour < s.TemporaryRepeatKindHours)
                continue;
            if (kind.Kind == WorldTemporaryEncounterKind.AtFind && !data.RecentFinds.Any(f =>
                    ctx.Grid.DistanceHexes(ctx.HeroX, ctx.HeroY, f.X, f.Y) > sight + 0.5 &&
                    ctx.Grid.DistanceHexes(ctx.HeroX, ctx.HeroY, f.X, f.Y) <= sight + s.TemporarySpawnMaxExtraHexes + 3))
                continue;
            double weight = kind.Weight * (night ? kind.NightWeight : 1.0);
            if (weight > 0)
                options.Add((kind, weight));
        }
        if (options.Count == 0)
            return;
        double roll = random.NextDouble() * options.Sum(o => o.Weight);
        WorldTemporaryEncounterKind chosen = options[options.Count - 1].Kind;
        foreach ((WorldTemporaryEncounterKind kind, double weight) in options)
        {
            roll -= weight;
            if (roll <= 0)
            {
                chosen = kind;
                break;
            }
        }
        if (!FindSpawnPoint(ctx, chosen, random, sight, t, out float x, out float y))
            return;
        SpawnTemporary(ctx, chosen, random, t, x, y, sight);
    }

    private static bool PointAllowed(TickContext ctx, WorldTemporaryEncounterKind kind, float x, float y, double t)
    {
        WorldMapHexGrid grid = ctx.Grid;
        WorldPopulationSettings s = ctx.Settings;
        if (x < 1f || y < 1f || x > 99f || y > 99f)
            return false;
        WorldMapGameplayTerrainType terrain = WorldMapNavigation.GetTerrainAtPercent(x, y);
        if (!WorldMapMovementRules.Current.IsTraversable(terrain) || terrain == WorldMapGameplayTerrainType.Water ||
            terrain == WorldMapGameplayTerrainType.Cliffs || terrain == WorldMapGameplayTerrainType.Mountains)
            return false;
        if (kind.Terrains != null && kind.Terrains.Count > 0 && !kind.Terrains.Contains(terrain))
            return false;
        GeneratedWorldMap map = ctx.State.GeneratedMap;
        if (grid.DistanceHexes(x, y, map.StartXPercent, map.StartYPercent) < s.StartSafeRadiusHexes + 1)
            return false;
        if (map.Villages.Any(v => grid.DistanceHexes(x, y, v.XPercent, v.YPercent) < s.ServiceSafeRadiusHexes + 1))
            return false;
        // Зачищенный участок: недавно разбитая группа или сожжённый лагерь рядом.
        if (ctx.Data.Groups.Any(g => g.Status == PopulationGroupState.Destroyed && t - g.DestroyedHour < s.TemporaryClearedHours &&
                                     grid.DistanceHexes(x, y, g.X, g.Y) < s.TemporaryClearedHexes))
            return false;
        if (ctx.Data.Sources.Any(src => (src.Status == PopulationSourceState.Eliminated || src.Status == PopulationSourceState.Weakened) &&
                                        (src.Status != PopulationSourceState.Eliminated || t - src.EliminatedHour < s.TemporaryClearedHours) &&
                                        grid.DistanceHexes(x, y, src.X, src.Y) < s.TemporaryClearedHexes))
            return false;
        return true;
    }

    private static bool FindSpawnPoint(TickContext ctx, WorldTemporaryEncounterKind kind, Random random, double sight, double t, out float x, out float y)
    {
        WorldMapHexGrid grid = ctx.Grid;
        WorldPopulationSettings s = ctx.Settings;
        x = y = 0f;
        if (kind.Kind == WorldTemporaryEncounterKind.AtFind)
        {
            foreach (PopulationTimedMark find in ctx.Data.RecentFinds.OrderByDescending(f => f.Hour))
            {
                double d = grid.DistanceHexes(ctx.HeroX, ctx.HeroY, find.X, find.Y);
                if (d <= sight + 0.5 || d > sight + s.TemporarySpawnMaxExtraHexes + 3)
                    continue;
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    double a = random.NextDouble() * Math.PI * 2, r = 0.6 + random.NextDouble();
                    float px = grid.PixelToPercentX(grid.PercentToPixelX(find.X) + Math.Cos(a) * r * grid.HexWidth);
                    float py = grid.PixelToPercentY(grid.PercentToPixelY(find.Y) + Math.Sin(a) * r * grid.HexWidth);
                    if (grid.DistanceHexes(ctx.HeroX, ctx.HeroY, px, py) > sight + 0.5 && PointAllowed(ctx, kind, px, py, t))
                    {
                        x = px;
                        y = py;
                        return true;
                    }
                }
            }
            return false;
        }
        for (int attempt = 0; attempt < 60; attempt++)
        {
            double a = random.NextDouble() * Math.PI * 2;
            double r = sight + s.TemporarySpawnMinExtraHexes + random.NextDouble() * (s.TemporarySpawnMaxExtraHexes - s.TemporarySpawnMinExtraHexes);
            float px = grid.PixelToPercentX(grid.PercentToPixelX(ctx.HeroX) + Math.Cos(a) * r * grid.HexWidth);
            float py = grid.PixelToPercentY(grid.PercentToPixelY(ctx.HeroY) + Math.Sin(a) * r * grid.HexWidth);
            if (PointAllowed(ctx, kind, px, py, t))
            {
                x = px;
                y = py;
                return true;
            }
        }
        return false;
    }

    private static string FigureFor(string role)
    {
        switch (role)
        {
            case WorldMapGroupRule.Band: return "Bandit";
            case WorldMapGroupRule.Caravan: return "Caravan";
            case WorldMapGroupRule.Patrol: return "Patrol";
            default: return string.Empty;
        }
    }

    private static PopulationGroupState CreateTemporaryGroup(TickContext ctx, WorldTemporaryEncounterKind kind, WorldPopulationPreset preset, string name,
        double t, float x, float y, List<MapPointData> route, bool loop, float waitHours)
    {
        WorldPopulationStateData data = ctx.Data;
        string id = "temp-" + (++data.NextTemporaryNumber) + "-" + preset.Id;
        Random comp = new Random(WorldMapGenerator.StreamSeed(ctx.State.GeneratedMap.Seed, "population.temp.comp." + id));
        List<GeneratedActorMember> members = new List<GeneratedActorMember>();
        foreach (WorldPopulationMember m in preset.Members)
        {
            int n = m.Min + comp.Next(Math.Max(1, m.Max - m.Min + 1));
            for (int k = 1; k <= n && members.Count < 8; k++)
                members.Add(new GeneratedActorMember { MemberId = id + ":" + m.UnitTypeId + ":" + k, UnitTypeId = m.UnitTypeId });
        }
        GeneratedMapActor def = new GeneratedMapActor
        {
            ActorId = id,
            RuleId = kind.Id,
            Role = preset.Role,
            Name = name,
            FigureSet = string.IsNullOrEmpty(preset.FigureSet) ? FigureFor(preset.Role) : preset.FigureSet,
            Description = preset.Description,
            HomeId = string.Empty,
            HomeXPercent = x,
            HomeYPercent = y,
            Route = route,
            Loop = loop,
            WaitHours = waitHours,
            PresetId = preset.Id,
            Behavior = preset.Behavior,
            Members = members,
            TerritoryXPercent = x,
            TerritoryYPercent = y,
            TerritoryRadiusHexes = preset.TerritoryRadiusHexes,
            UnitArtId = members.Count > 0 ? members[0].UnitTypeId : string.Empty,
        };
        // Фаза — так, чтобы в момент появления группа была в начале пути.
        double length = 0;
        for (int i = 1; i < route.Count; i++)
            length += ctx.Grid.DistanceHexes(route[i - 1].XPercent, route[i - 1].YPercent, route[i].XPercent, route[i].YPercent);
        double travel = length / Math.Max(0.01, WorldMapMovementRules.Current.GroupHexesPerGameHour(WorldMapActorMotion.SpeedHexesPerHour(def.Role)));
        double cycle = Math.Max(0.01, loop ? travel + waitHours : 2 * (travel + waitHours));
        def.PhaseHours = (float)(((-t % cycle) + cycle) % cycle);

        PopulationGroupState group = NewGroup(def, id, members, t);
        group.Temporary = true;
        group.TempDefinition = def;
        group.TempKind = kind.Kind;
        group.TempReason = kind.Reason ?? string.Empty;
        group.Name = name;
        SetPosition(group, x, y, 0f, 1f);
        data.Groups.Add(group);
        return group;
    }

    private static void SpawnTemporary(TickContext ctx, WorldTemporaryEncounterKind kind, Random random, double t, float x, float y, double sight)
    {
        WorldPopulationSettings s = ctx.Settings;
        WorldMapHexGrid grid = ctx.Grid;
        WorldPopulationPreset preset = s.Find(kind.BasePresetId);
        double life = s.TemporaryMinHours + random.NextDouble() * (s.TemporaryMaxHours - s.TemporaryMinHours);
        string name = string.IsNullOrEmpty(kind.Name) ? preset.Name : kind.Name;
        PopulationGroupState group;
        switch (kind.Kind)
        {
            case WorldTemporaryEncounterKind.Passing:
            {
                // Проходит через округу: с края обзора на противоположный край.
                double dx = grid.PercentToPixelX(ctx.HeroX) - grid.PercentToPixelX(x);
                double dy = grid.PercentToPixelY(ctx.HeroY) - grid.PercentToPixelY(y);
                double len = Math.Max(1e-3, Math.Sqrt(dx * dx + dy * dy));
                // Путь проходит сбоку от отряда (не в упор) и обязан войти в
                // обзор: пробуются боковые смещения, путь не идёт сквозь воду.
                List<MapPointData> route = null;
                foreach (double offset in new[] { 1.5, -1.5, 2.2, -2.2, 0.9, -0.9 })
                {
                    double side = offset * grid.HexWidth;
                    double ex = grid.PercentToPixelX(ctx.HeroX) + dx - dy / len * side;
                    double ey = grid.PercentToPixelY(ctx.HeroY) + dy + dx / len * side;
                    List<MapPointData> candidate = WalkableLine(ctx, x, y, grid.PixelToPercentX(ex), grid.PixelToPercentY(ey));
                    if (candidate.Count >= 2 && candidate.Any(p => grid.DistanceHexes(ctx.HeroX, ctx.HeroY, p.XPercent, p.YPercent) <= sight - 0.5))
                    {
                        route = candidate;
                        break;
                    }
                }
                if (route == null)
                    return;
                group = CreateTemporaryGroup(ctx, kind, preset, name, t, x, y, route, false, 0f);
                double length = 0;
                for (int i = 1; i < route.Count; i++)
                    length += grid.DistanceHexes(route[i - 1].XPercent, route[i - 1].YPercent, route[i].XPercent, route[i].YPercent);
                double travel = length / Math.Max(0.01, WorldMapMovementRules.Current.GroupHexesPerGameHour(WorldMapActorMotion.SpeedHexesPerHour(preset.Role)));
                group.ExpireHour = t + Math.Max(0.5, Math.Min(travel, s.TemporaryMaxHours));
                MapPointData end = route[route.Count - 1];
                group.ExitX = end.XPercent;
                group.ExitY = end.YPercent;
                break;
            }
            case WorldTemporaryEncounterKind.Skirmish:
            {
                List<MapPointData> here = new List<MapPointData> { new MapPointData(x, y) };
                group = CreateTemporaryGroup(ctx, kind, preset, name, t, x, y, here, true, 1f);
                WorldPopulationPreset second = s.Find(kind.SecondPresetId) ?? WorldPopulationCatalog.DefaultForRole(s, WorldMapGroupRule.Caravan);
                if (second != null)
                {
                    float ox = grid.PixelToPercentX(grid.PercentToPixelX(x) + 0.3 * grid.HexWidth);
                    PopulationGroupState other = CreateTemporaryGroup(ctx, kind, second, string.IsNullOrEmpty(kind.SecondName) ? second.Name : kind.SecondName,
                        t, ox, y, new List<MapPointData> { new MapPointData(ox, y) }, true, 1f);
                    other.ExpireHour = t + life;
                    other.TempReason = "отбиваются от ватаги";
                    StartConflict(ctx, group, other, t);
                    // Окно, за которое отряд успевает подойти.
                    PopulationConflictState conflict = ctx.Data.Conflicts[ctx.Data.Conflicts.Count - 1];
                    conflict.ResolveHour = t + Math.Max(s.ConflictWindowHours, 2.5);
                }
                group.ExpireHour = t + life;
                break;
            }
            default:
            {
                // Охота / у дороги / у находки — петля вокруг места появления.
                List<MapPointData> route = LocalLoop(ctx, x, y, kind.Kind == WorldTemporaryEncounterKind.Hunting ? 2.5 : 1.2, random);
                float wait = kind.Kind == WorldTemporaryEncounterKind.Hunting ? 0.5f : 2.5f;
                group = CreateTemporaryGroup(ctx, kind, preset, name, t, x, y, route, true, wait);
                group.ExpireHour = t + life;
                break;
            }
        }
        PopulationTimedMark memory = ctx.Data.TemporaryKindMemory.FirstOrDefault(m => m.Key == kind.Id);
        if (memory == null)
            ctx.Data.TemporaryKindMemory.Add(memory = new PopulationTimedMark { Key = kind.Id });
        memory.Hour = t;
        memory.X = x;
        memory.Y = y;
        // Признак — если отряд достаточно близко, чтобы услышать или заметить.
        if (!string.IsNullOrEmpty(kind.Hint) && grid.DistanceHexes(ctx.HeroX, ctx.HeroY, x, y) <= sight + 3.0)
            ctx.Messages?.Add(kind.Hint);
    }

    // Путь по проходимой суше прямой линией с шагом в полклетки; где прямая
    // упирается в воду или скалы — путь короче (не сквозь реку).
    private static List<MapPointData> WalkableLine(TickContext ctx, float fromX, float fromY, float toX, float toY)
    {
        WorldMapHexGrid grid = ctx.Grid;
        List<MapPointData> route = new List<MapPointData> { new MapPointData(fromX, fromY) };
        double total = grid.DistanceHexes(fromX, fromY, toX, toY);
        int steps = Math.Max(1, (int)Math.Ceiling(total * 2));
        for (int i = 1; i <= steps; i++)
        {
            float px = fromX + (toX - fromX) * i / steps, py = fromY + (toY - fromY) * i / steps;
            if (!Passable(ctx.State, px, py) || px < 0.5f || py < 0.5f || px > 99.5f || py > 99.5f)
                break;
            route.Add(new MapPointData(px, py));
        }
        return route;
    }

    private static List<MapPointData> LocalLoop(TickContext ctx, float x, float y, double radius, Random random)
    {
        WorldMapHexGrid grid = ctx.Grid;
        List<MapPointData> route = new List<MapPointData> { new MapPointData(x, y) };
        for (int leg = 0; leg < 2; leg++)
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                double a = random.NextDouble() * Math.PI * 2, r = radius * (0.5 + random.NextDouble() * 0.5);
                float px = grid.PixelToPercentX(grid.PercentToPixelX(x) + Math.Cos(a) * r * grid.HexWidth);
                float py = grid.PixelToPercentY(grid.PercentToPixelY(y) + Math.Sin(a) * r * grid.HexWidth);
                List<MapPointData> outward = WalkableLine(ctx, x, y, px, py);
                if (outward.Count < 2)
                    continue;
                route.AddRange(outward.Skip(1));
                outward.Reverse();
                route.AddRange(outward.Skip(1));
                break;
            }
        }
        return route;
    }
}
