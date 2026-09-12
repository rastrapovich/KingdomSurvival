using System.Collections.Generic;
using KingdomSurvival.WorldMapVisual;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    // WM-04: местность рисуется не сеткой одинаковых квадратов, а несколькими
    // крупными спрайтами-"массами" на связный кластер клеток одного типа —
    // так лес/горы читаются как живописное пятно, а не как клетки. Задействуется
    // только когда WorldMapTerrainVisualProfile.MassVariants непусто (см.
    // DrawTerrainForType в PrototypeUIController.WorldMap.cs).
    private const int MinMassesPerCluster = 1;
    private const int MaxMassesPerCluster = 6;
    private const int CellsPerMass = 4;

    private void DrawTerrainMassClusters(
        WorldMapTerrainVisualProfile profile,
        WorldMapTerrainType terrain)
    {
        bool[,] visited = new bool[
            WorldMapNavigation.GridWidth,
            WorldMapNavigation.GridHeight];
        int clusterIndex = 0;

        for (int y = 0; y < WorldMapNavigation.GridHeight; y++)
        {
            for (int x = 0; x < WorldMapNavigation.GridWidth; x++)
            {
                if (visited[x, y] ||
                    WorldMapNavigation.GetTerrainAtGridCell(x, y) != terrain)
                {
                    continue;
                }

                List<Vector2Int> cluster =
                    FloodFillTerrainCluster(x, y, terrain, visited);

                DrawTerrainMassCluster(profile, terrain, cluster, clusterIndex);
                clusterIndex++;
            }
        }
    }

    private static List<Vector2Int> FloodFillTerrainCluster(
        int startX,
        int startY,
        WorldMapTerrainType terrain,
        bool[,] visited)
    {
        List<Vector2Int> cluster = new List<Vector2Int>();
        Queue<Vector2Int> queue = new Queue<Vector2Int>();

        queue.Enqueue(new Vector2Int(startX, startY));
        visited[startX, startY] = true;

        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };

        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            cluster.Add(cell);

            for (int d = 0; d < 4; d++)
            {
                int nx = cell.x + dx[d];
                int ny = cell.y + dy[d];

                if (nx < 0 || ny < 0 ||
                    nx >= WorldMapNavigation.GridWidth ||
                    ny >= WorldMapNavigation.GridHeight)
                {
                    continue;
                }

                if (visited[nx, ny] ||
                    WorldMapNavigation.GetTerrainAtGridCell(nx, ny) != terrain)
                {
                    continue;
                }

                visited[nx, ny] = true;
                queue.Enqueue(new Vector2Int(nx, ny));
            }
        }

        return cluster;
    }

    private void DrawTerrainMassCluster(
        WorldMapTerrainVisualProfile profile,
        WorldMapTerrainType terrain,
        List<Vector2Int> cluster,
        int clusterIndex)
    {
        if (cluster.Count == 0 || worldMapTerrain == null)
            return;

        int minX = int.MaxValue, maxX = int.MinValue;
        int minY = int.MaxValue, maxY = int.MinValue;

        foreach (Vector2Int cell in cluster)
        {
            if (cell.x < minX) minX = cell.x;
            if (cell.x > maxX) maxX = cell.x;
            if (cell.y < minY) minY = cell.y;
            if (cell.y > maxY) maxY = cell.y;
        }

        float minXPercent = GridXToPercent(minX);
        float maxXPercent = GridXToPercent(maxX);
        float minYPercent = GridYToPercent(minY);
        float maxYPercent = GridYToPercent(maxY);

        int massCount = Mathf.Clamp(
            cluster.Count / CellsPerMass,
            MinMassesPerCluster,
            MaxMassesPerCluster);

        // Детерминированный сид: та же партия (WorldSeed) и тот же кластер
        // всегда дают одинаковую расстановку масс — карта не "дрожит" между
        // обновлениями RefreshWorldMapPanel в течение партии.
        int seed = unchecked(
            gameState.WorldSeed * 397 ^
            ((int)terrain + 1) * 131 ^
            clusterIndex);
        System.Random random = new System.Random(seed);

        for (int i = 0; i < massCount; i++)
        {
            Sprite variant = profile.PickVariant(random);

            if (variant == null)
                continue;

            float px = Mathf.Lerp(
                minXPercent, maxXPercent, (float)random.NextDouble());
            float py = Mathf.Lerp(
                minYPercent, maxYPercent, (float)random.NextDouble());
            float scale = Mathf.Lerp(
                0.85f, 1.2f, (float)random.NextDouble());
            float rotationDegrees = Mathf.Lerp(
                -8f, 8f, (float)random.NextDouble());

            Image mass = new Image();
            mass.AddToClassList("world-map-terrain-mass");
            mass.sprite = variant;
            mass.scaleMode = ScaleMode.ScaleToFit;
            mass.pickingMode = PickingMode.Ignore;

            mass.style.left = new Length(px, LengthUnit.Percent);
            mass.style.top = new Length(py, LengthUnit.Percent);
            mass.style.scale = new Scale(new Vector3(scale, scale, 1f));
            mass.style.rotate = new Rotate(new Angle(rotationDegrees));

            worldMapTerrain.Add(mass);
        }
    }
}
