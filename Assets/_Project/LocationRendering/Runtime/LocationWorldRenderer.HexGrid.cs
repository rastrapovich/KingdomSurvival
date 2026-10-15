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

        // ------------------------------------------------------------------
        // Сетка боя: клетки поля (BattlefieldView.GridOnCanvas) в стиле поля —
        // заливка и линии гексов между землёй и предметами, под людьми.
        // Перестраивается, только когда клетки или стиль изменились.
        // ------------------------------------------------------------------

        private GameObject battleGrid;
        private Mesh battleGridMesh;
        private int battleGridKey;

        public bool BattleGridVisible => battleGrid != null && battleGrid.activeSelf;

        // centers — центры гексов в пикселях рисунка места; null или пусто — скрыть.
        public void SetBattleGrid(IReadOnlyList<Vector2> centers, float radius, float verticalScale, Color fill, Color line, float lineWidth)
        {
            bool fills = fill.a > .001f, lines = line.a > .001f && lineWidth > .01f;
            // Не тоньше пикселя кадра: у поля линия сглажена, здесь — нет, и
            // тонкая линия (меньше пикселя кадра) пропадала. Шаг — по приближению камеры.
            if (lines && Target != null && Target.height > 0 && ViewHeight > 0)
            {
                float pixel = ViewHeight / Target.height;
                lineWidth = Mathf.Max(lineWidth, Mathf.Pow(2, Mathf.Ceil(Mathf.Log(pixel * 1.25f, 2) * 4) / 4));
            }
            if (centers == null || centers.Count == 0 || radius <= .01f || (!fills && !lines))
            {
                if (battleGrid != null) battleGrid.SetActive(false);
                return;
            }
            int key = 17;
            unchecked
            {
                foreach (Vector2 center in centers) key = key * 31 + Mathf.RoundToInt(center.x * 4) * 7919 + Mathf.RoundToInt(center.y * 4);
                key = key * 31 + Mathf.RoundToInt(radius * 100);
                key = key * 31 + Mathf.RoundToInt(verticalScale * 1000);
                key = key * 31 + fill.GetHashCode();
                key = key * 31 + line.GetHashCode();
                key = key * 31 + Mathf.RoundToInt(lineWidth * 20);
            }
            if (battleGrid == null)
            {
                battleGrid = Child("Сетка боя");
                battleGridMesh = Own(new Mesh { name = "Сетка боя", indexFormat = IndexFormat.UInt32 });
                battleGrid.AddComponent<MeshFilter>().sharedMesh = battleGridMesh;
                MeshRenderer meshRenderer = battleGrid.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = unlit;
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                // Над землёй с тенями, под предметами и людьми.
                meshRenderer.sortingOrder = ShadowSortingOrder + 5;
                battleGridKey = 0;
            }
            if (!battleGrid.activeSelf) battleGrid.SetActive(true);
            if (key == battleGridKey) return;
            battleGridKey = key;
            List<Vector3> vertices = new List<Vector3>();
            List<Color> colors = new List<Color>();
            List<int> indices = new List<int>();
            Vector2[] corners = new Vector2[6];
            float half = lineWidth / 2 / LocationVisualGeometry.PixelsPerUnit;
            foreach (Vector2 center in centers)
            {
                for (int i = 0; i < 6; i++)
                {
                    float angle = (60 * i - 30) * Mathf.Deg2Rad;
                    corners[i] = LocationVisualGeometry.PixelToWorld(Location,
                        center + new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * verticalScale));
                }
                if (fills)
                {
                    int start = vertices.Count;
                    vertices.Add(LocationVisualGeometry.PixelToWorld(Location, center));
                    colors.Add(fill);
                    for (int i = 0; i < 6; i++) { vertices.Add(corners[i]); colors.Add(fill); }
                    for (int i = 0; i < 6; i++) { indices.Add(start); indices.Add(start + 1 + i); indices.Add(start + 1 + (i + 1) % 6); }
                }
                if (lines)
                    for (int i = 0; i < 6; i++)
                    {
                        Vector2 a = corners[i], b = corners[(i + 1) % 6];
                        Vector2 side = Vector2.Perpendicular((b - a).normalized) * half;
                        int start = vertices.Count;
                        vertices.Add(a - side); vertices.Add(a + side); vertices.Add(b + side); vertices.Add(b - side);
                        for (int k = 0; k < 4; k++) colors.Add(line);
                        indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
                        indices.Add(start); indices.Add(start + 2); indices.Add(start + 3);
                    }
            }
            battleGridMesh.Clear();
            battleGridMesh.SetVertices(vertices);
            battleGridMesh.SetColors(colors);
            battleGridMesh.SetTriangles(indices, 0);
            battleGridMesh.RecalculateBounds();
        }

        // ------------------------------------------------------------------
        // Отметки боя: то, что поле боя рисовало поверх кадра (клетки хода,
        // местность, цель, путь, кольцо ходящего), — в мире над сеткой, под
        // людьми и предметами. Всё в пикселях рисунка места.
        // ------------------------------------------------------------------

        public enum BattleMarkKind { Hex, Circle, Segment }

        public struct BattleMark
        {
            public BattleMarkKind Kind;
            // Центр (гекс, круг) или начало отрезка; B — конец отрезка.
            public Vector2 A, B;
            public float Radius, VerticalScale, LineWidth;
            public Color Fill, Line;
        }

        private GameObject battleMarks;
        private Mesh battleMarksMesh;
        private int battleMarksKey;
        private readonly List<Vector3> markVertices = new List<Vector3>();
        private readonly List<Color> markColors = new List<Color>();
        private readonly List<int> markIndices = new List<int>();

        public bool BattleMarksVisible => battleMarks != null && battleMarks.activeSelf;

        // null или пусто — скрыть.
        public void SetBattleMarks(IReadOnlyList<BattleMark> marks)
        {
            if (marks == null || marks.Count == 0)
            {
                if (battleMarks != null) battleMarks.SetActive(false);
                return;
            }
            // Линия не тоньше пикселя кадра (как у сетки боя).
            float minimum = 0;
            if (Target != null && Target.height > 0 && ViewHeight > 0) minimum = ViewHeight / Target.height * 1.25f;
            int key = 17;
            unchecked
            {
                foreach (BattleMark mark in marks)
                {
                    key = key * 31 + (int)mark.Kind;
                    key = key * 31 + Mathf.RoundToInt(mark.A.x * 4) * 7919 + Mathf.RoundToInt(mark.A.y * 4);
                    key = key * 31 + Mathf.RoundToInt(mark.B.x * 4) * 7919 + Mathf.RoundToInt(mark.B.y * 4);
                    key = key * 31 + Mathf.RoundToInt(mark.Radius * 20) * 97 + Mathf.RoundToInt(mark.VerticalScale * 1000);
                    key = key * 31 + Mathf.RoundToInt(mark.LineWidth * 20);
                    key = key * 31 + mark.Fill.GetHashCode();
                    key = key * 31 + mark.Line.GetHashCode();
                }
                key = key * 31 + Mathf.RoundToInt(minimum * 20);
            }
            if (battleMarks == null)
            {
                battleMarks = Child("Отметки боя");
                battleMarksMesh = Own(new Mesh { name = "Отметки боя", indexFormat = IndexFormat.UInt32 });
                battleMarks.AddComponent<MeshFilter>().sharedMesh = battleMarksMesh;
                MeshRenderer meshRenderer = battleMarks.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = unlit;
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                // Над сеткой боя, под предметами и людьми.
                meshRenderer.sortingOrder = ShadowSortingOrder + 6;
                battleMarksKey = 0;
            }
            if (!battleMarks.activeSelf) battleMarks.SetActive(true);
            if (key == battleMarksKey) return;
            battleMarksKey = key;
            markVertices.Clear();
            markColors.Clear();
            markIndices.Clear();
            foreach (BattleMark mark in marks)
            {
                float width = mark.LineWidth > .01f ? Mathf.Max(mark.LineWidth, minimum) : 0;
                switch (mark.Kind)
                {
                    case BattleMarkKind.Segment:
                        if (mark.Line.a > .001f && width > 0) AddMarkLine(mark.A, mark.B, width, mark.Line);
                        break;
                    default:
                        int sides = mark.Kind == BattleMarkKind.Hex ? 6 : 24;
                        float start = mark.Kind == BattleMarkKind.Hex ? -30 : 0;
                        float vertical = mark.Kind == BattleMarkKind.Hex ? mark.VerticalScale : 1;
                        if (vertical <= 0) vertical = 1;
                        AddMarkShape(mark.A, mark.Radius, vertical, sides, start, mark.Fill, width, mark.Line);
                        break;
                }
            }
            battleMarksMesh.Clear();
            battleMarksMesh.SetVertices(markVertices);
            battleMarksMesh.SetColors(markColors);
            battleMarksMesh.SetTriangles(markIndices, 0);
            battleMarksMesh.RecalculateBounds();
        }

        // Многоугольник (гекс, круг): заливка веером и обводка — линия по
        // контуру внутрь и наружу на половину толщины, как у Painter2D.
        private void AddMarkShape(Vector2 center, float radius, float vertical, int sides, float startDegrees, Color fill, float width, Color line)
        {
            Vector2 Corner(int i, float r)
            {
                float angle = (startDegrees + 360f * i / sides) * Mathf.Deg2Rad;
                return LocationVisualGeometry.PixelToWorld(Location, center + new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r * vertical));
            }
            if (fill.a > .001f)
            {
                int first = markVertices.Count;
                markVertices.Add(LocationVisualGeometry.PixelToWorld(Location, center));
                markColors.Add(fill);
                for (int i = 0; i < sides; i++) { markVertices.Add(Corner(i, radius)); markColors.Add(fill); }
                for (int i = 0; i < sides; i++) { markIndices.Add(first); markIndices.Add(first + 1 + i); markIndices.Add(first + 1 + (i + 1) % sides); }
            }
            if (line.a > .001f && width > 0)
            {
                float inner = Mathf.Max(0, radius - width / 2), outer = radius + width / 2;
                int first = markVertices.Count;
                for (int i = 0; i < sides; i++)
                {
                    markVertices.Add(Corner(i, inner)); markColors.Add(line);
                    markVertices.Add(Corner(i, outer)); markColors.Add(line);
                }
                for (int i = 0; i < sides; i++)
                {
                    int a = first + i * 2, b = first + (i + 1) % sides * 2;
                    markIndices.Add(a); markIndices.Add(a + 1); markIndices.Add(b + 1);
                    markIndices.Add(a); markIndices.Add(b + 1); markIndices.Add(b);
                }
            }
        }

        private void AddMarkLine(Vector2 from, Vector2 to, float width, Color color)
        {
            Vector2 a = LocationVisualGeometry.PixelToWorld(Location, from), b = LocationVisualGeometry.PixelToWorld(Location, to);
            if ((b - a).sqrMagnitude < 1e-10f) return;
            Vector2 side = Vector2.Perpendicular((b - a).normalized) * (width / 2 / LocationVisualGeometry.PixelsPerUnit);
            int first = markVertices.Count;
            markVertices.Add(a - side); markVertices.Add(a + side); markVertices.Add(b + side); markVertices.Add(b - side);
            for (int k = 0; k < 4; k++) markColors.Add(color);
            markIndices.Add(first); markIndices.Add(first + 1); markIndices.Add(first + 2);
            markIndices.Add(first); markIndices.Add(first + 2); markIndices.Add(first + 3);
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
