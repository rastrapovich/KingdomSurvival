using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.BattleSandbox;
using UnityEngine;
using UnityEngine.Rendering;

namespace KingdomSurvival.LocationRendering
{
    // Сетка гексов места в кадре (игра, тестовый запуск): те же гексы, по
    // которым ходят и воюют (LocalLocationGeometry.BattleGrid), без стен.
    // Одна сетка линий между землёй (с тенями) и предметами: рёбра соседних
    // гексов не дублируются. Строится при первом показе.
    public sealed partial class LocationWorldRenderer
    {
        private GameObject hexGrid;

        public bool HexGridVisible
        {
            get => hexGrid != null && hexGrid.activeSelf;
            set
            {
                if (value && hexGrid == null) BuildHexGrid();
                if (hexGrid != null) hexGrid.SetActive(value);
            }
        }

        private void BuildHexGrid()
        {
            hexGrid = Child("Сетка гексов");
            LocationBattleGrid grid = Geometry.BattleGrid;
            if (grid.IsTooLarge || grid.HexSize <= 0) return;
            HashSet<HexCoord> blocked = Geometry.GridBlockedCells();
            BattlefieldGridLayout layout = grid.Layout;
            float size = grid.HexSize;
            float width = Mathf.Max(1.2f, size * .035f) / LocationVisualGeometry.PixelsPerUnit;
            List<Vector3> vertices = new List<Vector3>();
            List<int> indices = new List<int>();
            HashSet<long> drawn = new HashSet<long>();
            Vector2[] corners = new Vector2[6];
            foreach (HexCoord cell in grid.Cells())
            {
                if (blocked.Contains(cell)) continue;
                Vector2 center = layout.GetCenter(cell.Q, cell.R);
                for (int i = 0; i < 6; i++)
                {
                    float angle = (30 + 60 * i) * Mathf.Deg2Rad;
                    corners[i] = center + new Vector2(Mathf.Cos(angle) * size, Mathf.Sin(angle) * size * BattlefieldFrame.VerticalScale);
                }
                for (int i = 0; i < 6; i++)
                {
                    Vector2 a = corners[i], b = corners[(i + 1) % 6];
                    Vector2 middle = (a + b) / 2;
                    long key = ((long)Mathf.RoundToInt(middle.x * 4) << 32) ^ (uint)Mathf.RoundToInt(middle.y * 4);
                    if (!drawn.Add(key)) continue;
                    Vector2 wa = LocationVisualGeometry.PixelToWorld(Location, a), wb = LocationVisualGeometry.PixelToWorld(Location, b);
                    Vector2 side = Vector2.Perpendicular((wb - wa).normalized) * (width / 2);
                    int start = vertices.Count;
                    vertices.Add(wa - side); vertices.Add(wa + side); vertices.Add(wb + side); vertices.Add(wb - side);
                    indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
                    indices.Add(start); indices.Add(start + 2); indices.Add(start + 3);
                }
            }
            Mesh mesh = Own(new Mesh { name = "Сетка гексов", indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 });
            mesh.SetVertices(vertices);
            Color[] colors = new Color[vertices.Count];
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color(1, .95f, .8f, .28f);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            hexGrid.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer meshRenderer = hexGrid.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = unlit;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            // Над землёй с тенями, под предметами и людьми.
            meshRenderer.sortingOrder = ShadowSortingOrder + 5;
        }
    }
}
