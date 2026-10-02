using System.Collections.Generic;
using KingdomSurvival.BattleSandbox;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattlefieldDatabase
{
    // Кадр поля боя: фон и гексы раскладываются в одном прямоугольнике 16:9,
    // поэтому их взаимное положение не зависит от размера окна. Окно базы
    // вписывает кадр целиком, бой — заполняет экран, не обрезая арену.
    public static class BattlefieldFrame
    {
        public const float Aspect = 16f / 9f;
        public const float VerticalScale = 0.75f;
        public const int MinActiveCells = 16;

        // Область сетки по умолчанию — доли ширины и высоты кадра.
        public static readonly Rect DefaultGridArea = new Rect(0.11f, 0.11f, 0.78f, 0.80f);

        // Запас вокруг арены, который бой не обрезает при заполнении экрана.
        private const float CoverMargin = 0.03f;
        private static readonly float Sqrt3 = Mathf.Sqrt(3f);

        public static Rect GetGridArea(BattlefieldDefinitionData battlefield)
        {
            float scale = battlefield != null ? battlefield.GridScale : 1f;
            Vector2 offset = battlefield != null ? battlefield.GridOffset : Vector2.zero;
            Vector2 size = DefaultGridArea.size * scale;
            Vector2 center = DefaultGridArea.center + offset;
            return new Rect(center - size * 0.5f, size);
        }

        // Кадр внутри области. cover = false — кадр целиком (окно базы);
        // cover = true — заполнить область, но оставить арену видимой (бой).
        public static Rect FitFrame(Rect area, Rect gridArea, bool cover)
        {
            float height = Mathf.Min(area.width / Aspect, area.height);
            if (cover)
            {
                height = Mathf.Max(area.width / Aspect, area.height);
                Rect arena = GetArenaArea(gridArea);
                float halfX = Mathf.Max(0.5f - arena.xMin, arena.xMax - 0.5f) + CoverMargin;
                float halfY = Mathf.Max(0.5f - arena.yMin, arena.yMax - 0.5f) + CoverMargin;
                height = Mathf.Min(height, area.width * 0.5f / (halfX * Aspect));
                height = Mathf.Min(height, area.height * 0.5f / halfY);
            }

            height = Mathf.Max(1f, height);
            float width = height * Aspect;
            return new Rect(area.center.x - width * 0.5f, area.center.y - height * 0.5f, width, height);
        }

        public static BattlefieldGridLayout ComputeLayout(Rect frameRect, Rect gridArea)
        {
            Rect region = new Rect(
                frameRect.x + gridArea.x * frameRect.width,
                frameRect.y + gridArea.y * frameRect.height,
                gridArea.width * frameRect.width,
                gridArea.height * frameRect.height);
            float widthUnits = Sqrt3 * SandboxArenaShape.Width;
            float heightUnits = (1.5f * (SandboxArenaShape.Height - 1) + 2f) * VerticalScale;
            float size = Mathf.Max(0.01f, Mathf.Min(region.width / widthUnits, region.height / heightUnits));
            Rect arena = new Rect(
                region.center.x - widthUnits * size * 0.5f,
                region.center.y - heightUnits * size * 0.5f,
                widthUnits * size,
                heightUnits * size);
            Vector2 origin = new Vector2(arena.x, arena.y + size * VerticalScale);
            return new BattlefieldGridLayout(size, origin, VerticalScale, arena);
        }

        // Прямоугольник арены в долях кадра.
        public static Rect GetArenaArea(Rect gridArea)
        {
            Rect arena = ComputeLayout(new Rect(0f, 0f, Aspect, 1f), gridArea).ArenaRect;
            return new Rect(arena.x / Aspect, arena.y, arena.width / Aspect, arena.height);
        }

        public static bool IsArenaCell(int q, int r)
        {
            return SandboxArenaShape.Contains(new HexCoord(q, r));
        }

        public static bool IsActiveCell(BattlefieldDefinitionData battlefield, int q, int r)
        {
            return IsArenaCell(q, r) && (battlefield == null || !battlefield.IsCellDisabled(q, r));
        }

        public static IEnumerable<HexCoord> ActiveCells(BattlefieldDefinitionData battlefield)
        {
            foreach (HexCoord cell in SandboxArenaShape.Cells())
            {
                if (battlefield == null || !battlefield.IsCellDisabled(cell.Q, cell.R))
                    yield return cell;
            }
        }

        // Отключённые гексы арены — для SandboxRoster.CreateBattle.
        public static List<HexCoord> DisabledCells(BattlefieldDefinitionData battlefield)
        {
            List<HexCoord> cells = new List<HexCoord>();
            if (battlefield == null)
                return cells;
            foreach (HexCoord cell in SandboxArenaShape.Cells())
            {
                if (battlefield.IsCellDisabled(cell.Q, cell.R))
                    cells.Add(cell);
            }
            return cells;
        }

        public static int CountActiveCells(BattlefieldDefinitionData battlefield)
        {
            int count = 0;
            foreach (HexCoord _ in ActiveCells(battlefield))
                count++;
            return count;
        }

        public static bool AreActiveCellsConnected(BattlefieldDefinitionData battlefield)
        {
            HashSet<HexCoord> active = new HashSet<HexCoord>(ActiveCells(battlefield));
            if (active.Count == 0)
                return false;

            HashSet<HexCoord> visited = new HashSet<HexCoord>();
            Queue<HexCoord> frontier = new Queue<HexCoord>();
            foreach (HexCoord first in active)
            {
                visited.Add(first);
                frontier.Enqueue(first);
                break;
            }

            while (frontier.Count > 0)
            {
                foreach (HexCoord next in frontier.Dequeue().Neighbors())
                {
                    if (active.Contains(next) && visited.Add(next))
                        frontier.Enqueue(next);
                }
            }

            return visited.Count == active.Count;
        }

        // Шесть вершин гекса с острыми вершинами вверх и вниз.
        public static void FillHexPath(Painter2D painter, Vector2 center, float radius, float verticalScale)
        {
            painter.BeginPath();
            for (int i = 0; i < 6; i++)
            {
                float angle = Mathf.Deg2Rad * (60f * i - 30f);
                Vector2 point = center + new Vector2(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius * verticalScale);
                if (i == 0)
                    painter.MoveTo(point);
                else
                    painter.LineTo(point);
            }
            painter.ClosePath();
        }
    }

    public readonly struct BattlefieldGridLayout
    {
        private static readonly float Sqrt3 = Mathf.Sqrt(3f);

        public float Size { get; }
        public Vector2 Origin { get; }
        public float VerticalScale { get; }
        public Rect ArenaRect { get; }

        public BattlefieldGridLayout(float size, Vector2 origin, float verticalScale, Rect arenaRect)
        {
            Size = size;
            Origin = origin;
            VerticalScale = Mathf.Max(0.01f, verticalScale);
            ArenaRect = arenaRect;
        }

        public Vector2 GetCenter(int q, int r)
        {
            float rowOffset = (r & 1) == 0 ? 0f : 0.5f;
            return Origin + new Vector2(Size * Sqrt3 * (q + rowOffset), Size * 1.5f * r * VerticalScale);
        }

        // Прямоугольник, в который вписывается картинка гекса радиуса radius.
        public Vector2 GetHexBox(float radius)
        {
            return new Vector2(Sqrt3 * radius, 2f * radius * VerticalScale);
        }

        // Ближайший гекс арены к точке — или false, если точка вне гексов.
        public bool TryGetCell(Vector2 point, out int q, out int r)
        {
            q = -1;
            r = -1;
            float best = float.MaxValue;
            for (int row = 0; row < SandboxArenaShape.Height; row++)
            {
                for (int column = 0; column < SandboxArenaShape.Width; column++)
                {
                    if (!BattlefieldFrame.IsArenaCell(column, row))
                        continue;
                    Vector2 delta = point - GetCenter(column, row);
                    delta.y /= VerticalScale;
                    float distance = delta.sqrMagnitude;
                    if (distance < best)
                    {
                        best = distance;
                        q = column;
                        r = row;
                    }
                }
            }
            return q >= 0 && best <= Size * Size;
        }
    }
}
