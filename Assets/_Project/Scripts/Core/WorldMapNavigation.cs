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

public enum WorldMapTerrainType
{
    Plains,
    Hills,
    Mountains
}

public static class WorldMapNavigation
{
    public const int GridWidth = 26;
    public const int GridHeight = 16;
    public const int DiscoveryRadiusCells = 1;
    public const float CapitalXPercent = 50f;
    public const float CapitalYPercent = 81f;

    private const int ProtectedCapitalRadiusCells = 2;
    private const int HillClusterCount = 8;
    private const int MountainClusterCount = 5;

    private static int configuredTerrainSeed;
    private static bool terrainConfigured;
    private static WorldMapTerrainType[,] terrainGrid;

    public static void ConfigureTerrain(int worldSeed)
    {
        if (terrainConfigured && configuredTerrainSeed == worldSeed && terrainGrid != null)
            return;

        configuredTerrainSeed = worldSeed;
        terrainConfigured = true;
        terrainGrid = GenerateTerrain(worldSeed);
    }

    public static List<MapPointData> FindPath(
        float startXPercent,
        float startYPercent,
        float targetXPercent,
        float targetYPercent)
    {
        EnsureTerrainConfigured();

        float startX = ClampMapX(startXPercent);
        float startY = ClampMapY(startYPercent);
        float targetX = ClampMapX(targetXPercent);
        float targetY = ClampMapY(targetYPercent);

        double dxCells = (targetX - startX) * (GridWidth - 1) / 100.0;
        double dyCells = (targetY - startY) * (GridHeight - 1) / 100.0;
        double distanceCells = Math.Sqrt(dxCells * dxCells + dyCells * dyCells);

        List<MapPointData> route = new List<MapPointData>
        {
            new MapPointData(startX, startY)
        };

        if (distanceCells <= 0.0001)
            return route;

        int baseSegments = Math.Max(1, (int)Math.Ceiling(distanceCells));

        for (int segment = 1; segment <= baseSegments; segment++)
        {
            float fromT = (segment - 1f) / baseSegments;
            float toT = segment / (float)baseSegments;
            float midpointT = (fromT + toT) * 0.5f;
            float midpointX = Lerp(startX, targetX, midpointT);
            float midpointY = Lerp(startY, targetY, midpointT);
            int terrainCost = GetTerrainTravelCost(
                GetTerrainAtPercent(midpointX, midpointY));

            // Точки остаются на прямой. Дополнительные подточки лишь растягивают
            // время прохождения трудной местности для существующей симуляции.
            for (int part = 1; part <= terrainCost; part++)
            {
                float localT = part / (float)terrainCost;
                float t = fromT + (toT - fromT) * localT;
                route.Add(new MapPointData(
                    Lerp(startX, targetX, t),
                    Lerp(startY, targetY, t)));
            }
        }

        route[0].XPercent = startXPercent;
        route[0].YPercent = startYPercent;
        route[route.Count - 1].XPercent = targetXPercent;
        route[route.Count - 1].YPercent = targetYPercent;
        return route;
    }

    // В существующей симуляции один сегмент маршрута занимает одну базовую
    // единицу движения. На холмах/горах FindPath добавляет 2/3 под-сегмента.
    public static int CalculateRouteCells(List<MapPointData> path, int routeIndex = 0)
    {
        if (path == null || path.Count <= 1)
            return 0;

        return Math.Max(0, path.Count - 1 - routeIndex);
    }

    public static double CalculateGeometricDistanceCells(List<MapPointData> path)
    {
        if (path == null || path.Count <= 1)
            return 0.0;

        double total = 0.0;
        for (int i = 1; i < path.Count; i++)
        {
            double dx = (path[i].XPercent - path[i - 1].XPercent) *
                (GridWidth - 1) / 100.0;
            double dy = (path[i].YPercent - path[i - 1].YPercent) *
                (GridHeight - 1) / 100.0;
            total += Math.Sqrt(dx * dx + dy * dy);
        }

        return total;
    }

    public static int GetTerrainTravelCost(WorldMapTerrainType terrain)
    {
        switch (terrain)
        {
            case WorldMapTerrainType.Hills:
                return 2;
            case WorldMapTerrainType.Mountains:
                return 3;
            default:
                return 1;
        }
    }

    public static float GetTerrainSpeedMultiplier(WorldMapTerrainType terrain)
    {
        switch (terrain)
        {
            case WorldMapTerrainType.Hills:
                return 0.5f;
            case WorldMapTerrainType.Mountains:
                return 1f / 3f;
            default:
                return 1f;
        }
    }

    public static WorldMapTerrainType GetTerrainAtPercent(
        float xPercent,
        float yPercent)
    {
        EnsureTerrainConfigured();
        return GetTerrainAtGridCell(
            PercentToGridX(xPercent),
            PercentToGridY(yPercent));
    }

    public static WorldMapTerrainType GetTerrainAtGridCell(int x, int y)
    {
        EnsureTerrainConfigured();
        if (!IsInside(x, y))
            return WorldMapTerrainType.Plains;
        return terrainGrid[x, y];
    }

    // Оставлены для совместимости со старым UI/тестами. Непроходимых клеток
    // больше нет: холмы и горы замедляют, но не блокируют движение.
    public static bool IsBlockedPercent(float xPercent, float yPercent) => false;
    public static bool IsBlockedGridCell(int x, int y) => false;

    public static int GridXFromPercent(float value) => PercentToGridX(value);
    public static int GridYFromPercent(float value) => PercentToGridY(value);

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

        if (expedition.Route != null && expedition.Route.Count > 0)
        {
            for (int i = 0; i < cells; i++)
            {
                if (expedition.RouteIndex >= expedition.Route.Count - 1)
                    break;

                expedition.RouteIndex++;
                MapPointData position = expedition.Route[expedition.RouteIndex];
                expedition.CurrentMapXPercent = position.XPercent;
                expedition.CurrentMapYPercent = position.YPercent;
                expedition.LastTravelPoints.Add(
                    new MapPointData(position.XPercent, position.YPercent));
            }
        }

        expedition.RemainingRouteCells =
            CalculateRouteCells(expedition.Route, expedition.RouteIndex);
    }

    public static void AddRouteDelayHours(ExpeditionData expedition, double hours)
    {
        if (expedition == null || hours <= 0.0)
            return;
        expedition.RouteDelayHoursRemaining += hours;
    }

    public static bool IsWithinDiscoveryRadius(
        float firstXPercent,
        float firstYPercent,
        float secondXPercent,
        float secondYPercent)
    {
        int firstX = PercentToGridX(firstXPercent);
        int firstY = PercentToGridY(firstYPercent);
        int secondX = PercentToGridX(secondXPercent);
        int secondY = PercentToGridY(secondYPercent);

        int gridDistance = Math.Max(
            Math.Abs(firstX - secondX),
            Math.Abs(firstY - secondY));
        return gridDistance <= DiscoveryRadiusCells;
    }

    public static float ClampMapX(float value) =>
        Math.Max(2f, Math.Min(98f, value));

    public static float ClampMapY(float value) =>
        Math.Max(2f, Math.Min(96f, value));

    private static void EnsureTerrainConfigured()
    {
        if (!terrainConfigured || terrainGrid == null)
            ConfigureTerrain(0);
    }

    private static WorldMapTerrainType[,] GenerateTerrain(int worldSeed)
    {
        WorldMapTerrainType[,] result =
            new WorldMapTerrainType[GridWidth, GridHeight];
        Random random = new Random(unchecked(worldSeed ^ 0x4B53544E));

        PaintClusters(
            result,
            random,
            WorldMapTerrainType.Hills,
            HillClusterCount,
            4,
            8,
            true);
        PaintClusters(
            result,
            random,
            WorldMapTerrainType.Mountains,
            MountainClusterCount,
            3,
            6,
            false);

        int capitalX = PercentToGridX(CapitalXPercent);
        int capitalY = PercentToGridY(CapitalYPercent);
        for (int y = 0; y < GridHeight; y++)
        {
            for (int x = 0; x < GridWidth; x++)
            {
                if (Math.Max(Math.Abs(x - capitalX), Math.Abs(y - capitalY)) <=
                    ProtectedCapitalRadiusCells)
                {
                    result[x, y] = WorldMapTerrainType.Plains;
                }
            }
        }

        return result;
    }

    private static void PaintClusters(
        WorldMapTerrainType[,] grid,
        Random random,
        WorldMapTerrainType terrain,
        int clusterCount,
        int minLength,
        int maxLength,
        bool broad)
    {
        for (int cluster = 0; cluster < clusterCount; cluster++)
        {
            int x = random.Next(1, GridWidth - 1);
            int y = random.Next(1, GridHeight - 1);
            int length = random.Next(minLength, maxLength + 1);
            int directionX = random.Next(-1, 2);
            int directionY = random.Next(-1, 2);
            if (directionX == 0 && directionY == 0)
                directionX = 1;

            for (int step = 0; step < length; step++)
            {
                PaintCell(grid, x, y, terrain);

                if (broad)
                {
                    if (random.NextDouble() < 0.75)
                        PaintCell(grid, x + 1, y, terrain);
                    if (random.NextDouble() < 0.75)
                        PaintCell(grid, x - 1, y, terrain);
                    if (random.NextDouble() < 0.60)
                        PaintCell(grid, x, y + 1, terrain);
                    if (random.NextDouble() < 0.60)
                        PaintCell(grid, x, y - 1, terrain);
                }
                else if (random.NextDouble() < 0.45)
                {
                    PaintCell(grid, x + directionY, y - directionX, terrain);
                }

                if (random.NextDouble() < 0.35)
                {
                    directionX = Math.Max(-1, Math.Min(1, directionX + random.Next(-1, 2)));
                    directionY = Math.Max(-1, Math.Min(1, directionY + random.Next(-1, 2)));
                    if (directionX == 0 && directionY == 0)
                        directionX = random.Next(0, 2) == 0 ? -1 : 1;
                }

                x = Math.Max(1, Math.Min(GridWidth - 2, x + directionX));
                y = Math.Max(1, Math.Min(GridHeight - 2, y + directionY));
            }
        }
    }

    private static void PaintCell(
        WorldMapTerrainType[,] grid,
        int x,
        int y,
        WorldMapTerrainType terrain)
    {
        if (!IsInside(x, y) || IsProtectedCapitalCell(x, y))
            return;

        if (terrain == WorldMapTerrainType.Mountains ||
            grid[x, y] == WorldMapTerrainType.Plains)
        {
            grid[x, y] = terrain;
        }
    }

    private static bool IsProtectedCapitalCell(int x, int y)
    {
        int capitalX = PercentToGridX(CapitalXPercent);
        int capitalY = PercentToGridY(CapitalYPercent);
        return Math.Max(Math.Abs(x - capitalX), Math.Abs(y - capitalY)) <=
            ProtectedCapitalRadiusCells;
    }

    private static bool IsInside(int x, int y) =>
        x >= 0 && x < GridWidth && y >= 0 && y < GridHeight;

    private static int PercentToGridX(float value) =>
        Math.Max(0, Math.Min(
            GridWidth - 1,
            (int)Math.Round(value * (GridWidth - 1) / 100f)));

    private static int PercentToGridY(float value) =>
        Math.Max(0, Math.Min(
            GridHeight - 1,
            (int)Math.Round(value * (GridHeight - 1) / 100f)));

    private static float Lerp(float a, float b, float t) =>
        a + (b - a) * t;
}
