using System;
using System.Collections.Generic;

[Serializable]
public class MapPointData
{
    public float XPercent;
    public float YPercent;

    public MapPointData(float xPercent, float yPercent)
    {
        XPercent = xPercent;
        YPercent = yPercent;
    }
}

// 12И (канон v1.50 §9, §9.9): единственная точка, через которую игра
// спрашивает карту — где клетка, какая там местность, как туда пройти и
// сколько это займёт. Под капотом — шестиугольная сетка активного мира и
// WorldMapPathfinder. Путь экспедиции (ExpeditionData.Route) — сглаженная
// ломаная от фактической позиции героя; игроку она не показывается.
public static class WorldMapNavigation
{
    public const float CapitalXPercent = 50f;
    public const float CapitalYPercent = 81f;

    // Шаг оценки времени и сдвига по пути: местность меняется не чаще клетки.
    private const double SampleStepHexes = 0.25;

    private static WorldMapDefinitionData activeDefinition;
    private static WorldMapTerrainLayer activeLayer;

    // Без авторского мира — сплошная открытая местность, а не скрытый откат
    // к процедурной генерации (канон §9.9).
    public static void ConfigureDefaultTerrain()
    {
        activeDefinition = null;
        activeLayer = WorldMapTerrainLayer.CreateDefault();
    }

    public static void ConfigureFromDefinition(WorldMapDefinitionData definition)
    {
        if (definition == null || !definition.IsValid)
        {
            ConfigureDefaultTerrain();
            return;
        }

        if (activeLayer != null && ReferenceEquals(activeDefinition, definition))
            return;

        activeDefinition = definition;
        activeLayer = definition.CreateTerrainLayer();
    }

    // Тесты и редактор: подставить разметку без авторского мира.
    public static void ConfigureTerrainLayer(WorldMapTerrainLayer layer)
    {
        activeDefinition = null;
        activeLayer = layer ?? WorldMapTerrainLayer.CreateDefault();
    }

    public static bool HasActiveDefinition => activeDefinition != null;

    public static WorldMapDefinitionData ActiveDefinition => activeDefinition;

    public static WorldMapTerrainLayer ActiveLayer
    {
        get
        {
            if (activeLayer == null)
                ConfigureDefaultTerrain();
            return activeLayer;
        }
    }

    public static WorldMapHexGrid Grid => ActiveLayer.Grid;

    // Путь от точной текущей позиции до цели; цель внутри непроходимого
    // места заменяется ближайшей доступной точкой. Всегда хотя бы одна точка —
    // старт; «нечего идти» — одна точка.
    public static List<MapPointData> FindPath(
        float startXPercent,
        float startYPercent,
        float targetXPercent,
        float targetYPercent)
    {
        WorldMapTerrainLayer layer = ActiveLayer;
        WorldMapHexGrid grid = layer.Grid;
        float targetX = ClampMapX(targetXPercent);
        float targetY = ClampMapY(targetYPercent);

        List<MapPointData> route = new List<MapPointData> { new MapPointData(startXPercent, startYPercent) };
        if (grid.DistanceHexes(startXPercent, startYPercent, targetX, targetY) <= 0.0001)
            return route;

        List<WorldMapPathfinder.PathPoint> points = WorldMapPathfinder.FindPath(
            layer,
            WorldMapMovementRules.Current,
            grid.PercentToPixelX(startXPercent),
            grid.PercentToPixelY(startYPercent),
            grid.PercentToPixelX(targetX),
            grid.PercentToPixelY(targetY),
            out _);

        for (int i = 1; i < points.Count; i++)
        {
            route.Add(new MapPointData(
                grid.PixelToPercentX(points[i].X),
                grid.PixelToPercentY(points[i].Y)));
        }

        if (route.Count == 2 &&
            grid.DistanceHexes(route[0].XPercent, route[0].YPercent, route[1].XPercent, route[1].YPercent) <= 0.0001)
        {
            route.RemoveAt(1);
        }
        return route;
    }

    // Длина оставшегося пути в клетках, с округлением вверх.
    public static int CalculateRouteCells(List<MapPointData> path, int routeIndex = 0)
    {
        double hexes = PathLengthHexes(path, routeIndex);
        return hexes <= 0.0001 ? 0 : (int)Math.Ceiling(hexes - 0.0001);
    }

    public static double PathLengthHexes(List<MapPointData> path, int fromIndex = 0)
    {
        if (path == null || path.Count <= 1)
            return 0.0;

        double total = 0.0;
        for (int i = Math.Max(1, fromIndex + 1); i < path.Count; i++)
            total += DistanceHexes(path[i - 1], path[i]);
        return total;
    }

    public static double CalculateGeometricDistanceCells(List<MapPointData> path) => PathLengthHexes(path);

    public static double DistanceHexes(
        float fromXPercent,
        float fromYPercent,
        float toXPercent,
        float toYPercent) =>
        Grid.DistanceHexes(fromXPercent, fromYPercent, toXPercent, toYPercent);

    public static double DistanceHexes(MapPointData from, MapPointData to) =>
        from == null || to == null ? 0.0 : DistanceHexes(from.XPercent, from.YPercent, to.XPercent, to.YPercent);

    public static bool IsWithinHexes(
        float firstXPercent,
        float firstYPercent,
        float secondXPercent,
        float secondYPercent,
        double radiusHexes) =>
        DistanceHexes(firstXPercent, firstYPercent, secondXPercent, secondYPercent) <= radiusHexes + 0.0001;

    public static bool IsWithinDiscoveryRadius(
        float firstXPercent,
        float firstYPercent,
        float secondXPercent,
        float secondYPercent) =>
        IsWithinHexes(
            firstXPercent,
            firstYPercent,
            secondXPercent,
            secondYPercent,
            WorldMapMovementRules.Current.DiscoveryRadiusHexes);

    public static WorldMapGameplayTerrainType GetTerrainAtPercent(float xPercent, float yPercent) =>
        ActiveLayer.GetAtPercent(xPercent, yPercent);

    public static bool IsTraversableAtPercent(float xPercent, float yPercent) =>
        WorldMapMovementRules.Current.IsTraversable(GetTerrainAtPercent(xPercent, yPercent));

    // Игровые часы на оставшийся путь: каждый кусок пути — по местности под ним.
    public static double EstimateTravelHours(
        List<MapPointData> path,
        int routeIndex = 0,
        double segmentProgress = 0.0)
    {
        if (path == null || path.Count <= 1)
            return 0.0;

        WorldMapMovementRules rules = WorldMapMovementRules.Current;
        double hours = 0.0;
        for (int i = Math.Max(0, routeIndex); i < path.Count - 1; i++)
        {
            double startT = i == routeIndex ? Math.Max(0.0, Math.Min(1.0, segmentProgress)) : 0.0;
            hours += SegmentHours(path[i], path[i + 1], startT, rules);
        }
        return hours;
    }

    private static double SegmentHours(MapPointData from, MapPointData to, double startT, WorldMapMovementRules rules)
    {
        double length = DistanceHexes(from, to) * (1.0 - startT);
        if (length <= 0.0)
            return 0.0;

        int samples = Math.Max(1, (int)Math.Ceiling(length / SampleStepHexes));
        double piece = length / samples;
        double hours = 0.0;
        for (int s = 0; s < samples; s++)
        {
            double t = startT + (1.0 - startT) * (s + 0.5) / samples;
            float x = (float)(from.XPercent + (to.XPercent - from.XPercent) * t);
            float y = (float)(from.YPercent + (to.YPercent - from.YPercent) * t);
            hours += piece * rules.HoursPerHex(GetTerrainAtPercent(x, y));
        }
        return hours;
    }

    // Сюжетный или дорожный «короткий путь»: отряд сразу оказывается на
    // cells клеток дальше по своему пути. Пройденные точки попадают в
    // LastTravelPoints, чтобы находки по дороге не терялись.
    public static void AdvanceRouteByCells(ExpeditionData expedition, int cells)
    {
        if (expedition == null || cells <= 0)
            return;

        if (expedition.LastTravelPoints.Count == 0)
        {
            expedition.LastTravelStartedPhase = expedition.Phase;
            expedition.LastTravelTargetLocationId = expedition.LocationId;
            expedition.LastTravelTargetXPercent = expedition.TargetMapXPercent;
            expedition.LastTravelTargetYPercent = expedition.TargetMapYPercent;
        }

        List<MapPointData> route = expedition.Route;
        if (route != null && route.Count > 1)
        {
            float x = expedition.CurrentMapXPercent;
            float y = expedition.CurrentMapYPercent;
            int index = Math.Max(0, Math.Min(route.Count - 1, expedition.RouteIndex));
            double remaining = cells;

            while (remaining > 0.0001 && index < route.Count - 1)
            {
                MapPointData next = route[index + 1];
                double distance = DistanceHexes(x, y, next.XPercent, next.YPercent);
                double step = Math.Min(0.5, Math.Min(remaining, distance));
                if (distance <= 0.0001)
                {
                    index++;
                    continue;
                }

                double t = step / distance;
                x = (float)(x + (next.XPercent - x) * t);
                y = (float)(y + (next.YPercent - y) * t);
                remaining -= step;
                if (step >= distance - 0.0001)
                {
                    x = next.XPercent;
                    y = next.YPercent;
                    index++;
                }
                expedition.LastTravelPoints.Add(new MapPointData(x, y));
            }

            List<MapPointData> rest = new List<MapPointData> { new MapPointData(x, y) };
            for (int i = index + 1; i < route.Count; i++)
                rest.Add(new MapPointData(route[i].XPercent, route[i].YPercent));

            expedition.Route = rest;
            expedition.RouteIndex = 0;
            expedition.CurrentMapXPercent = x;
            expedition.CurrentMapYPercent = y;
        }

        expedition.RemainingRouteCells = CalculateRouteCells(expedition.Route, expedition.RouteIndex);
    }

    public static void AddRouteDelayHours(ExpeditionData expedition, double hours)
    {
        if (expedition == null || hours <= 0.0)
            return;
        expedition.RouteDelayHoursRemaining += hours;
    }

    public static float ClampMapX(float value) =>
        Math.Max(2f, Math.Min(98f, value));

    public static float ClampMapY(float value) =>
        Math.Max(2f, Math.Min(96f, value));
}
