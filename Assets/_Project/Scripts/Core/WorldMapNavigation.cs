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
    public const int GridWidth = 24;
    public const int GridHeight = 16;
    public const int CellsPerDay = 4;
    public const float CapitalXPercent = 50f;
    public const float CapitalYPercent = 81f;

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

        if (IsBlocked(targetX, targetY))
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
                return BuildPath(parents, current);

            if (closed[current])
                continue;

            closed[current] = true;
            int currentX = current % GridWidth;
            int currentY = current / GridWidth;

            for (int i = 0; i < NeighborOffsets.GetLength(0); i++)
            {
                int nextX = currentX + NeighborOffsets[i, 0];
                int nextY = currentY + NeighborOffsets[i, 1];

                if (!IsInside(nextX, nextY) || IsBlocked(nextX, nextY))
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

    public static int CalculateDays(List<MapPointData> path, int routeIndex = 0)
    {
        if (path == null || path.Count <= 1)
            return 0;

        int remainingCells = Math.Max(0, path.Count - 1 - routeIndex);
        return (remainingCells + CellsPerDay - 1) / CellsPerDay;
    }

    public static void AdvanceRoute(ExpeditionData expedition, int days)
    {
        if (expedition == null || days <= 0)
            return;

        if (expedition.TravelDelayDays > 0)
        {
            int usedDelay = Math.Min(days, expedition.TravelDelayDays);
            expedition.TravelDelayDays -= usedDelay;
            days -= usedDelay;
        }

        if (days > 0 && expedition.Route != null && expedition.Route.Count > 0)
        {
            expedition.RouteIndex = Math.Min(
                expedition.Route.Count - 1,
                expedition.RouteIndex + CellsPerDay * days);
            MapPointData position = expedition.Route[expedition.RouteIndex];
            expedition.CurrentMapXPercent = position.XPercent;
            expedition.CurrentMapYPercent = position.YPercent;
        }

        expedition.DaysRemaining = CalculateDays(
            expedition.Route,
            expedition.RouteIndex) + expedition.TravelDelayDays;
    }

    public static void AddTravelDelay(ExpeditionData expedition, int days)
    {
        if (expedition == null || days <= 0)
            return;
        expedition.TravelDelayDays += days;
        expedition.DaysRemaining += days;
    }

    public static bool IsBlockedPercent(float xPercent, float yPercent)
    {
        return IsBlocked(PercentToGridX(xPercent), PercentToGridY(yPercent));
    }

    public static float ClampMapX(float value) => Math.Max(2f, Math.Min(98f, value));
    public static float ClampMapY(float value) => Math.Max(2f, Math.Min(96f, value));

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

        // Два временных горных массива серого прототипа. Между ними
        // оставлены проходы, чтобы A* всегда мог найти путь по суше.
        bool westernRidge = x >= 7 && x <= 9 && y >= 3 && y <= 10 && y != 7;
        bool easternRidge = x >= 15 && x <= 17 && y >= 1 && y <= 9 && y != 5;
        bool northernLake = x >= 11 && x <= 13 && y >= 1 && y <= 3;
        return westernRidge || easternRidge || northernLake;
    }

    private static bool IsInside(int x, int y) =>
        x >= 0 && x < GridWidth && y >= 0 && y < GridHeight;

    private static int ToIndex(int x, int y) => y * GridWidth + x;
    private static int PercentToGridX(float value) =>
        Math.Max(0, Math.Min(GridWidth - 1, (int)Math.Round(value * (GridWidth - 1) / 100f)));
    private static int PercentToGridY(float value) =>
        Math.Max(0, Math.Min(GridHeight - 1, (int)Math.Round(value * (GridHeight - 1) / 100f)));
    private static float GridToPercentX(int value) => value * 100f / (GridWidth - 1);
    private static float GridToPercentY(int value) => value * 100f / (GridHeight - 1);
    private static float Heuristic(int x, int y, int targetX, int targetY) =>
        Math.Max(Math.Abs(targetX - x), Math.Abs(targetY - y));
}
