using System.Collections.Generic;

namespace KingdomSurvival.BattleSandbox
{
    /// <summary>
    /// Standard compact sandbox arena described from top to bottom as
    /// 7 / 8 / 9 / 8 / 9 / 8 / 7 active pointy-top hexes.
    /// Rows share one visual center: odd rows are shifted by half a hex.
    /// </summary>
    public static class SandboxArenaShape
    {
        public const int Width = 9;
        public const int Height = 7;
        public const int CellCount = 56;

        private static readonly int[] RowStarts = { 1, 0, 0, 0, 0, 0, 1 };
        private static readonly int[] RowLengths = { 7, 8, 9, 8, 9, 8, 7 };

        public static int GetRowStart(int row)
        {
            return row >= 0 && row < Height ? RowStarts[row] : 0;
        }

        public static int GetRowLength(int row)
        {
            return row >= 0 && row < Height ? RowLengths[row] : 0;
        }

        public static bool Contains(HexCoord coord)
        {
            if (coord.R < 0 || coord.R >= Height)
                return false;

            int start = RowStarts[coord.R];
            int length = RowLengths[coord.R];
            return coord.Q >= start && coord.Q < start + length;
        }

        public static IEnumerable<HexCoord> Cells()
        {
            for (int row = 0; row < Height; row++)
            {
                int start = RowStarts[row];
                int end = start + RowLengths[row];
                for (int column = start; column < end; column++)
                    yield return new HexCoord(column, row);
            }
        }
    }
}
