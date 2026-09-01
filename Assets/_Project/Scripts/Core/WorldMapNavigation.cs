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

public static class WorldMapNavigation
{
    // 26 x 16 gives the map a landscape grid close to the available 16:10-ish
    // workspace once the expedition garrison strip is hidden. Grid nodes therefore
    // read as approximately square cells instead of stretched rectangles.
    public const int GridWidth = 26;
    public const int GridHeight = 16;
    public const int DiscoveryRadiusCells = 1;
    public const float CapitalXPercent = 50f;
    public const float CapitalYPercent = 81f;

    private const float ExactPointEpsilon = 0.0001f;

    private static readonly int[,] NeighborOffsets =
    {
        { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 },
        { 1, 1 }, { 1, -1 }, { -1, 1 }, { -1, -1 }
    };

    public static List<MapPointData> FindPath(
        float startXPercent,
        float startYPercent,
        float targetXPercent,
        float targetYPercent)
    {
        int startX = PercentToGridX(startXPercent);
        int startY = PercentToGridY(startYPercent);
        int targetX = PercentToGridX(targetXPercent);
        int targetY = PercentToGridY(targetYPercent);
        bool targetWasBlocked = IsBlocked(targetX, targetY);

        if (targetWasBlocked)
            FindNearestWalkable(ref targetX, ref targetY);

        int nodeCount = GridWidth * GridHeight;
        float[] costs = new float[nodeCount];
        int[] parents = new int[nodeCount];
        bool[] closed = new bool[nodeCount];
        List<int> open = new List<int>();

        for (int i = 0; i < nodeCount; i++)
        {
            costs[i] = float.MaxValue;
            parents[i] = -1;
        }

        int startIndex = ToIndex(startX, startY);
        int targetIndex = ToIndex(targetX, targetY);
        costs[startIndex] = 0f;
        open.Add(startIndex);

        while (open.Count > 0)
        {
            int bestOpenIndex = 0;
            float bestScore = float.MaxValue;

            for (int i = 0; i < open.Count; i++)
            {
                int candidate = open[i];
                int candidateX = candidate % GridWidth;
                int candidateY = candidate / GridWidth;
                float score = costs[candidate] +
                    Heuristic(candidateX, candidateY, targetX, targetY);

                if (score < bestScore)
                {
                    bestScore = score;
                    bestOpenIndex = i;
                }
            }

            int current = open[bestOpenIndex];
            open.RemoveAt(bestOpenIndex);

            if (current == targetIndex)
            {
                return ApplyExactEndpoints(
                    BuildPath(parents, current),
                    startXPercent,
                    startYPercent,
                    targetXPercent,
                    targetYPercent,
                    !targetWasBlocked);
            }

            if (closed[current])
                continue;

            closed[current] = true;
            int currentX = current % GridWidth;
            int currentY = current / GridWidth;

            for (int i = 0; i < NeighborOffsets.GetLength(0); i++)
            {
                int nextX = currentX + NeighborOffsets[i, 0];
                int nextY = currentY + NeighborOffsets[i, 1];

                if (!CanTraverseStep(currentX, currentY, nextX, nextY))
                    continue;

                int next = ToIndex(nextX, nextY);
                if (closed[next])
                    continue;

                bool diagonal =
                    NeighborOffsets[i, 0] != 0 && NeighborOffsets[i, 1] != 0;
                float nextCost = costs[current] + (diagonal ? 1.4142f : 1f);

                if (nextCost >= costs[next])
                    continue;

                costs[next] = nextCost;
                parents[next] = current;

                if (!open.Contains(next))
                    open.Add(next);
            }
        }

        return new List<MapPointData>();
    }

    public static int CalculateRouteCells(List<MapPointData> path, int routeIndex = 0)
    {
        if (path == null || path.Count <= 1)
            return 0;

        return Math.Max(0, path.Count - 1 - routeIndex);
    }

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

    public static bool IsBlockedPercent(float xPercent, float yPercent) =>
        IsBlocked(PercentToGridX(xPercent), PercentToGridY(yPercent));

    public static bool IsBlockedGridCell(int x, int y) => IsBlocked(x, y);

    public static int GridXFromPercent(float value) => PercentToGridX(value);
    public static int GridYFromPercent(float value) => PercentToGridY(value);

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

    private static bool CanTraverseStep(
        int currentX,
        int currentY,
        int nextX,
        int nextY)
    {
        if (!IsInside(nextX, nextY) || IsBlocked(nextX, nextY))
            return false;

        int deltaX = nextX - currentX;
        int deltaY = nextY - currentY;
        bool diagonal = deltaX != 0 && deltaY != 0;

        if (!diagonal)
            return true;

        if (IsBlocked(currentX + deltaX, currentY))
            return false;

        if (IsBlocked(currentX, currentY + deltaY))
            return false;

        return true;
    }

    private static List<MapPointData> ApplyExactEndpoints(
        List<MapPointData> path,
        float startXPercent,
        float startYPercent,
        float targetXPercent,
        float targetYPercent,
        bool preserveExactTarget)
    {
        if (path == null || path.Count == 0)
            return path ?? new List<MapPointData>();

        float resolvedTargetX = preserveExactTarget
            ? targetXPercent
            : path[path.Count - 1].XPercent;
        float resolvedTargetY = preserveExactTarget
            ? targetYPercent
            : path[path.Count - 1].YPercent;

        path[0].XPercent = startXPercent;
        path[0].YPercent = startYPercent;

        if (path.Count == 1)
        {
            float dx = resolvedTargetX - startXPercent;
            float dy = resolvedTargetY - startYPercent;

            if (dx * dx + dy * dy > ExactPointEpsilon * ExactPointEpsilon)
            {
                path.Add(new MapPointData(resolvedTargetX, resolvedTargetY));
            }

            return path;
        }

        path[path.Count - 1].XPercent = resolvedTargetX;
        path[path.Count - 1].YPercent = resolvedTargetY;
        return path;
    }

    private static List<MapPointData> BuildPath(int[] parents, int current)
    {
        List<MapPointData> reversed = new List<MapPointData>();

        while (current >= 0)
        {
            int x = current % GridWidth;
            int y = current / GridWidth;
            reversed.Add(new MapPointData(GridToPercentX(x), GridToPercentY(y)));
            current = parents[current];
        }

        reversed.Reverse();
        return reversed;
    }

    private static void FindNearestWalkable(ref int x, ref int y)
    {
        for (int radius = 1; radius < Math.Max(GridWidth, GridHeight); radius++)
        {
            for (int offsetY = -radius; offsetY <= radius; offsetY++)
            {
                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    int candidateX = x + offsetX;
                    int candidateY = y + offsetY;

                    if (IsInside(candidateX, candidateY) &&
                        !IsBlocked(candidateX, candidateY))
                    {
                        x = candidateX;
                        y = candidateY;
                        return;
                    }
                }
            }
        }
    }

    private static bool IsBlocked(int x, int y)
    {
        if (!IsInside(x, y))
            return true;

        // Same grey-prototype obstacles, rescaled horizontally for the 26-column grid.
        bool westernRidge = x >= 8 && x <= 10 && y >= 3 && y <= 10 && y != 7;
        bool easternRidge = x >= 16 && x <= 19 && y >= 1 && y <= 9 && y != 5;
        bool northernLake = x >= 12 && x <= 14 && y >= 1 && y <= 3;
        return westernRidge || easternRidge || northernLake;
    }

    private static bool IsInside(int x, int y) =>
        x >= 0 && x < GridWidth && y >= 0 && y < GridHeight;

    private static int ToIndex(int x, int y) => y * GridWidth + x;

    private static int PercentToGridX(float value) =>
        Math.Max(0, Math.Min(
            GridWidth - 1,
            (int)Math.Round(value * (GridWidth - 1) / 100f)));

    private static int PercentToGridY(float value) =>
        Math.Max(0, Math.Min(
            GridHeight - 1,
            (int)Math.Round(value * (GridHeight - 1) / 100f)));

    private static float GridToPercentX(int value) =>
        value * 100f / (GridWidth - 1);

    private static float GridToPercentY(int value) =>
        value * 100f / (GridHeight - 1);

    private static float Heuristic(int x, int y, int targetX, int targetY) =>
        Math.Max(Math.Abs(targetX - x), Math.Abs(targetY - y));
}
