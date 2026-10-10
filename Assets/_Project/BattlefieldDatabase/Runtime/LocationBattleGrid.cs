using System.Collections.Generic;
using KingdomSurvival.BattleSandbox;
using UnityEngine;

namespace KingdomSurvival.BattlefieldDatabase
{
    // ПР-12К: гексы боя на всю локацию. Размер клетки — тот же, что у кадра
    // боя места («Ширина кадра боя» + сетка поля), сетка — прямоугольная
    // (нечётные ряды сдвинуты на полклетки, как у арены поля) и покрывает
    // весь рисунок места: первая клетка — в левом верхнем углу. Бой на месте
    // идёт на этой сетке целиком; клетки с центром вне рисунка или на
    // непроходимом — стены. Координаты — пиксели рисунка места (Y вниз).
    public readonly struct LocationBattleGrid
    {
        // Больше клеток не бывает: место слишком велико для такого мелкого
        // гекса — бой не развернуть (проверка места скажет об этом).
        public const int MaxCells = 6000;
        private static readonly float Sqrt3 = Mathf.Sqrt(3f);

        public int Columns { get; }
        public int Rows { get; }
        public float HexSize { get; }
        public Vector2 Canvas { get; }

        public LocationBattleGrid(Vector2 canvas, float hexSize)
        {
            Canvas = new Vector2(Mathf.Max(1f, canvas.x), Mathf.Max(1f, canvas.y));
            HexSize = Mathf.Max(1f, hexSize);
            float vertical = HexSize * BattlefieldFrame.VerticalScale;
            Columns = Mathf.Max(1, Mathf.FloorToInt(Canvas.x / (Sqrt3 * HexSize) + .5f));
            Rows = Mathf.Max(1, Mathf.FloorToInt((Canvas.y / vertical - 1f) / 1.5f) + 1);
        }

        public int CellCount => Columns * Rows;
        public bool IsTooLarge => CellCount > MaxCells;

        public bool Contains(HexCoord cell) => cell.Q >= 0 && cell.R >= 0 && cell.Q < Columns && cell.R < Rows;

        // Сетка в прямоугольнике rect (куда лёг весь рисунок места).
        public BattlefieldGridLayout LayoutIn(Rect rect)
        {
            float k = rect.width / Canvas.x;
            float size = HexSize * k;
            Vector2 origin = rect.position + new Vector2(Sqrt3 * HexSize * .5f, HexSize * BattlefieldFrame.VerticalScale) * k;
            return new BattlefieldGridLayout(size, origin, BattlefieldFrame.VerticalScale, rect);
        }

        // В пикселях рисунка места.
        public BattlefieldGridLayout Layout => LayoutIn(new Rect(Vector2.zero, Canvas));

        public Vector2 CellCenter(HexCoord cell) => Layout.GetCenter(cell.Q, cell.R);

        public IEnumerable<HexCoord> Cells()
        {
            for (int r = 0; r < Rows; r++)
                for (int q = 0; q < Columns; q++)
                    yield return new HexCoord(q, r);
        }

        // Клетка под точкой рисунка: ближайший центр (с учётом сжатия по вертикали).
        public HexCoord CellAt(Vector2 point)
        {
            BattlefieldGridLayout layout = Layout;
            float rowStep = HexSize * 1.5f * BattlefieldFrame.VerticalScale;
            int row = Mathf.Clamp(Mathf.RoundToInt((point.y - layout.Origin.y) / rowStep), 0, Rows - 1);
            HexCoord best = new HexCoord(0, row);
            float bestDistance = float.MaxValue;
            for (int r = Mathf.Max(0, row - 1); r <= Mathf.Min(Rows - 1, row + 1); r++)
            {
                float offset = (r & 1) == 0 ? 0f : .5f;
                int column = Mathf.Clamp(Mathf.RoundToInt((point.x - layout.Origin.x) / (Sqrt3 * HexSize) - offset), 0, Columns - 1);
                for (int q = Mathf.Max(0, column - 1); q <= Mathf.Min(Columns - 1, column + 1); q++)
                {
                    Vector2 delta = point - layout.GetCenter(q, r);
                    delta.y /= BattlefieldFrame.VerticalScale;
                    float distance = delta.sqrMagnitude;
                    if (distance < bestDistance) { bestDistance = distance; best = new HexCoord(q, r); }
                }
            }
            return best;
        }
    }
}
