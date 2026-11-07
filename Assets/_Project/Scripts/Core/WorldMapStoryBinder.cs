using System;
using System.Collections.Generic;

// 12У-D, бриф распределения историй (§7, §18): роль истории привязывается к
// месту сгенерированной карты один раз на партию и хранится в сохранении —
// история не переезжает за героем, загрузка не перевыбирает место. Роль
// описывает автор: жёсткие условия (проходимость, достижимость, удалённость
// от деревень, допустимая местность) и мягкие предпочтения (берег реки,
// желаемое время пути от опорной роли). Цена пути считается текущей
// навигацией игры (WorldMapNavigation), а не расстоянием по прямой.
// Восстановление ограничено: сначала ослабляются только мягкие предпочтения;
// если подходящей клетки нет — привязка честно помечается неудачной с
// причиной, без смены seed и без молчаливой подстановки.
[Serializable]
public sealed class StoryRoleBindingData
{
    public string StoryId = string.Empty;
    public string RoleId = string.Empty;
    public bool Bound;
    public float XPercent;
    public float YPercent;
    public int Cell = -1;
    public double TravelHours;
    // Для автора: почему выбрано именно это место или почему не нашлось.
    public string Reason = string.Empty;
}

public sealed class StoryRoleSpec
{
    public string StoryId = string.Empty;
    public string RoleId = string.Empty;
    // Жёсткие условия.
    public List<WorldMapGameplayTerrainType> Terrains = new List<WorldMapGameplayTerrainType>();
    public int MinHexesFromVillages = 2;
    public double MinTravelHours = 1.0;
    public double MaxTravelHours = 12.0;
    // Мягкие предпочтения.
    public bool PreferRiverbank;
    public double TargetTravelHours = 5.0;
    // Сколько кандидатов проверять настоящим поиском пути на одной ступени.
    public int PathBudget = 24;
}

public static class WorldMapStoryBinder
{
    public static StoryRoleBindingData Find(GameState state, string storyId, string roleId)
    {
        List<StoryRoleBindingData> list = state?.Settlements?.StoryBindings;
        if (list == null)
            return null;
        foreach (StoryRoleBindingData binding in list)
        {
            if (binding != null && binding.StoryId == storyId && binding.RoleId == roleId)
                return binding;
        }
        return null;
    }

    // Привязка роли к сгенерированной карте партии относительно опорной точки
    // (например, поселения-источника). Уже существующая привязка возвращается
    // без изменений. Без сгенерированной карты — null (авторский мир).
    public static StoryRoleBindingData Bind(GameState state, StoryRoleSpec spec, float anchorX, float anchorY)
    {
        if (state == null || spec == null)
            return null;
        StoryRoleBindingData existing = Find(state, spec.StoryId, spec.RoleId);
        if (existing != null)
            return existing;
        if (!state.HasGeneratedMap)
            return null;
        StoryRoleBindingData result = Choose(state.GeneratedMap, state.WorldSeed, spec, anchorX, anchorY);
        SettlementService.EnsureState(state).StoryBindings.Add(result);
        return result;
    }

    public static StoryRoleBindingData Choose(GeneratedWorldMap map, int worldSeed, StoryRoleSpec spec, float anchorX, float anchorY)
    {
        StoryRoleBindingData result = new StoryRoleBindingData { StoryId = spec.StoryId, RoleId = spec.RoleId };
        WorldMapHexGrid grid = map.CreateGrid();
        WorldMapTerrainLayer layer = map.CreateTerrainLayer();
        WorldMapMovementRules rules = WorldMapMovementRules.Current ?? WorldMapMovementRules.CreateDefault();
        int count = grid.CellCount;
        HashSet<int> river = new HashSet<int>(map.RiverCells ?? new List<int>());
        List<int> villages = new List<int>();
        if (map.Villages != null)
            foreach (GeneratedMapSite v in map.Villages)
                villages.Add(v.Cell);
        WorldMapHexCell anchor = grid.CellAtPercent(anchorX, anchorY);

        // Грубая граница по прямой из предела времени: дальше заведомо дольше.
        double hoursPerHex = Math.Max(0.05, WorldMapMovementRules.Current?.SafeTravelHoursPerHex ?? WorldMapMovementRules.DefaultTravelHoursPerHex);
        int maxHexes = (int)Math.Ceiling(spec.MaxTravelHours / hoursPerHex * 1.2);
        int minHexes = Math.Max(1, (int)Math.Floor(spec.MinTravelHours / hoursPerHex / 3.0));

        bool NearRiver(WorldMapHexCell cell)
        {
            for (int d = 0; d < 6; d++)
            {
                WorldMapHexCell n = cell.Neighbor(d);
                if (grid.IsInside(n) && river.Contains(grid.IndexOf(n)))
                    return true;
                for (int e = 0; e < 6; e++)
                {
                    WorldMapHexCell m = n.Neighbor(e);
                    if (grid.IsInside(m) && river.Contains(grid.IndexOf(m)))
                        return true;
                }
            }
            return false;
        }

        List<(int Cell, uint Hash, bool River)> candidates = new List<(int, uint, bool)>();
        for (int i = 0; i < count; i++)
        {
            WorldMapHexCell cell = grid.CellAt(i);
            WorldMapGameplayTerrainType t = layer.GetAtIndex(i);
            if (!rules.IsTraversable(t) || t == WorldMapGameplayTerrainType.Road)
                continue;
            if (spec.Terrains.Count > 0 && !spec.Terrains.Contains(t))
                continue;
            int d = cell.DistanceTo(anchor);
            if (d < minHexes || d > maxHexes)
                continue;
            bool tooClose = false;
            foreach (int v in villages)
            {
                if (grid.CellAt(v).DistanceTo(cell) < spec.MinHexesFromVillages)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose)
                continue;
            candidates.Add((i, SettlementService.StableHash(worldSeed + ":" + spec.StoryId + ":" + spec.RoleId + ":" + i), NearRiver(cell)));
        }
        candidates.Sort((a, b) => a.Hash != b.Hash ? a.Hash.CompareTo(b.Hash) : a.Cell.CompareTo(b.Cell));

        // Ступени: 1 — все предпочтения; 2 — без берега реки. Время пути —
        // жёсткое условие, желаемое время — выбор лучшего среди подошедших.
        for (int step = 1; step <= 2; step++)
        {
            bool wantRiver = spec.PreferRiverbank && step == 1;
            if (!spec.PreferRiverbank && step == 2)
                break;
            int checkedPaths = 0;
            int best = -1;
            double bestHours = 0, bestScore = double.MaxValue;
            foreach ((int cell, uint _, bool nearRiver) in candidates)
            {
                if (wantRiver && !nearRiver)
                    continue;
                if (checkedPaths >= spec.PathBudget)
                    break;
                grid.CellCenter(grid.CellAt(cell), out double px, out double py);
                float x = grid.PixelToPercentX(px), y = grid.PixelToPercentY(py);
                List<MapPointData> route = WorldMapNavigation.FindPath(anchorX, anchorY, x, y);
                checkedPaths++;
                if (route.Count < 2)
                    continue;
                MapPointData end = route[route.Count - 1];
                // Цель внутри непроходимого заменяется навигацией ближайшей
                // точкой — такая клетка недостижима как есть.
                if (grid.DistanceHexes(end.XPercent, end.YPercent, x, y) > 0.6)
                    continue;
                double hours = WorldMapNavigation.EstimateTravelHours(route);
                if (hours < spec.MinTravelHours || hours > spec.MaxTravelHours)
                    continue;
                double score = Math.Abs(hours - spec.TargetTravelHours);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = cell;
                    bestHours = hours;
                }
            }
            if (best < 0)
                continue;
            grid.CellCenter(grid.CellAt(best), out double bx, out double by);
            result.Bound = true;
            result.Cell = best;
            result.XPercent = grid.PixelToPercentX(bx);
            result.YPercent = grid.PixelToPercentY(by);
            result.TravelHours = bestHours;
            result.Reason = (wantRiver ? "берег реки" : spec.PreferRiverbank ? "без берега реки (у реки подходящих мест не нашлось)" : "по условиям роли")
                + $", {layer.GetAtIndex(best)}, путь от опорного места {bestHours:0.0} ч (проверено {checkedPaths} путей, ступень {step}).";
            return result;
        }
        result.Reason = $"Нет места: кандидатов по местности и удалённости {candidates.Count}, ни один не прошёл проверку пути {spec.MinTravelHours:0.#}–{spec.MaxTravelHours:0.#} ч в пределах {spec.PathBudget} проверок на ступень.";
        return result;
    }
}
