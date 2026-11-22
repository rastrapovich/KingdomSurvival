using System;
using System.Collections.Generic;

// Стадия «уступы» (12С): дуга скального обрыва по границе возвышенности.
// Клетки вдоль линии — Cliffs сплошной цепочкой (непроходимы: снизу не зайти,
// сверху не спрыгнуть); концы линии и просвет посередине у длинного уступа —
// проходимы, там обрыв сходит в склон (так же затухает рисунок MapLab,
// MapLabGenerator.ApplyCliffs). Плато — со стороны большей высоты, стенка
// обращена к низу экрана (плато севернее линии, допуск ±60°).
public static partial class WorldMapGenerator
{
    private sealed partial class Builder
    {
        // Клетки у уступов: на них не ставятся озёра (проход не затопить).
        private bool[] ledgeNoLake;

        private void BuildLedges()
        {
            ledgeNoLake = new bool[count];
            if (settings.LedgeCount <= 0)
                return;

            Random random = new Random(StreamSeed(seed, "ledges"));
            double hw = grid.HexWidth;
            WorldMapHexCell start = grid.CellAt(startCell);
            int startBan = settings.StartClearRadiusHexes + settings.LedgeSpacingHexes;
            int edgeBan = edgeBand + 2;

            // Кандидаты: места с заметным уклоном высоты, плато — на севере.
            List<LedgeCandidate> candidates = new List<LedgeCandidate>();
            int tries = Math.Max(160, settings.LedgeCount * 100);
            for (int t = 0; t < tries; t++)
            {
                int cell = random.Next(count);
                if (EdgeDepth(cell) < edgeBan || grid.CellAt(cell).DistanceTo(start) < startBan)
                    continue;
                if (!Gradient(cell, 5, out double gx, out double gy, out double slope))
                    continue;
                if (slope * 3.0 < settings.LedgeMinDrop)
                    continue;
                double length = Math.Sqrt(gx * gx + gy * gy);
                gx /= length;
                gy /= length;
                if (gy > -0.6)
                    continue;
                candidates.Add(new LedgeCandidate
                {
                    Cell = cell, Gx = gx, Gy = gy,
                    Score = slope * (0.7 + 0.6 * random.NextDouble())
                });
            }
            candidates.Sort((a, b) =>
            {
                int c = b.Score.CompareTo(a.Score);
                return c != 0 ? c : a.Cell.CompareTo(b.Cell);
            });

            bool[] blocked = new bool[count];
            int placed = 0;
            foreach (LedgeCandidate candidate in candidates)
            {
                if (placed >= settings.LedgeCount)
                    break;
                if (blocked[candidate.Cell])
                    continue;
                int length = random.Next(settings.LedgeMinLengthHexes, settings.LedgeMaxLengthHexes + 1);
                bool ok = false;
                for (int attempt = 0; attempt < 3 && !ok; attempt++)
                {
                    ok = TryPlaceLedge(candidate, length, random, blocked, startBan, edgeBan);
                    length = Math.Max(settings.LedgeMinLengthHexes, length - 3);
                }
                if (ok)
                    placed++;
            }
        }

        private struct LedgeCandidate
        {
            public int Cell;
            public double Gx, Gy;
            public double Score;
        }

        // Линейная регрессия высоты по клеткам радиуса: направление вверх
        // (пиксели, y вниз) и уклон на одну клетку.
        private bool Gradient(int cell, int radius, out double gx, out double gy, out double slope)
        {
            gx = gy = slope = 0.0;
            List<int> cells = CellsWithin(cell, radius);
            if (cells.Count < 12)
                return false;
            double mx = 0, my = 0, me = 0;
            double[] xs = new double[cells.Count], ys = new double[cells.Count];
            for (int i = 0; i < cells.Count; i++)
            {
                grid.CellCenter(grid.CellAt(cells[i]), out xs[i], out ys[i]);
                mx += xs[i]; my += ys[i]; me += elevation[cells[i]];
            }
            mx /= cells.Count; my /= cells.Count; me /= cells.Count;
            double sxx = 0, syy = 0, sxe = 0, sye = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                double dx = xs[i] - mx, dy = ys[i] - my, de = elevation[cells[i]] - me;
                sxx += dx * dx; syy += dy * dy; sxe += dx * de; sye += dy * de;
            }
            if (sxx < 1e-9 || syy < 1e-9)
                return false;
            gx = sxe / sxx;
            gy = sye / syy;
            double magnitude = Math.Sqrt(gx * gx + gy * gy);
            if (magnitude < 1e-12)
                return false;
            slope = magnitude * grid.HexWidth;
            return true;
        }

        private bool PixelInside(double x, double y) =>
            x >= 0.0 && y >= 0.0 && x <= grid.CanvasWidth && y <= grid.CanvasHeight;

        private float ElevationAtPixel(double x, double y, out bool inside)
        {
            inside = PixelInside(x, y);
            if (!inside)
                return 0f;
            return elevation[grid.IndexOf(grid.CellAtPixel(x, y))];
        }

        private bool TryPlaceLedge(LedgeCandidate candidate, int lengthHexes, Random random, bool[] blocked, int startBan, int edgeBan)
        {
            double hw = grid.HexWidth;
            WorldMapHexCell start = grid.CellAt(startCell);
            grid.CellCenter(grid.CellAt(candidate.Cell), out double cx, out double cy);
            // Плато — слева по ходу: слева от d = (dy, -dx) должно быть (gx, gy).
            double dx = -candidate.Gy, dy = candidate.Gx;
            double half = lengthHexes * hw * 0.5;
            double bulge = (0.04 + 0.08 * random.NextDouble()) * lengthHexes * hw;
            double ax = cx - dx * half, ay = cy - dy * half;
            double bx = cx + dx * half, by = cy + dy * half;
            // Квадратичная кривая: середина смещена к плато на bulge.
            double px = cx + candidate.Gx * 2.0 * bulge, py = cy + candidate.Gy * 2.0 * bulge;

            int steps = Math.Max(8, lengthHexes * 4);
            double[] sx = new double[steps + 1], sy = new double[steps + 1], arc = new double[steps + 1];
            for (int k = 0; k <= steps; k++)
            {
                double u = k / (double)steps, v = 1.0 - u;
                sx[k] = v * v * ax + 2.0 * v * u * px + u * u * bx;
                sy[k] = v * v * ay + 2.0 * v * u * py + u * u * by;
                if (k > 0)
                    arc[k] = arc[k - 1] + Math.Sqrt((sx[k] - sx[k - 1]) * (sx[k] - sx[k - 1]) + (sy[k] - sy[k - 1]) * (sy[k] - sy[k - 1]));
                if (!PixelInside(sx[k], sy[k]))
                    return false;
            }
            double total = arc[steps];

            // Растеризация: цепочка соседних клеток (шаг выборки короче клетки).
            List<int> chain = new List<int>();
            List<double> chainFirst = new List<double>(), chainLast = new List<double>();
            for (int k = 0; k <= steps; k++)
            {
                int cell = grid.IndexOf(grid.CellAtPixel(sx[k], sy[k]));
                if (chain.Count > 0 && chain[chain.Count - 1] == cell)
                {
                    chainLast[chain.Count - 1] = arc[k];
                    continue;
                }
                if (chain.Contains(cell))
                    return false;
                if (chain.Count > 0 && Distance(chain[chain.Count - 1], cell) > 1)
                    return false;
                if (blocked[cell] || protectedStart[cell] || EdgeDepth(cell) < edgeBan
                    || grid.CellAt(cell).DistanceTo(start) < startBan || terrain[cell] == Water)
                    return false;
                chain.Add(cell);
                chainFirst.Add(arc[k]);
                chainLast.Add(arc[k]);
            }

            // Плато выше поперёк всей линии: слева заметно выше, чем справа.
            List<double> dropArc = new List<double>(), dropValue = new List<double>();
            for (int k = 0; k <= steps; k += 2)
            {
                int a = Math.Max(0, k - 1), b = Math.Min(steps, k + 1);
                double tx = sx[b] - sx[a], ty = sy[b] - sy[a];
                double tl = Math.Sqrt(tx * tx + ty * ty);
                if (tl < 1e-9)
                    continue;
                double nx = ty / tl, ny = -tx / tl; // слева по ходу
                float high = ElevationAtPixel(sx[k] + nx * 1.5 * hw, sy[k] + ny * 1.5 * hw, out bool inHigh);
                float low = ElevationAtPixel(sx[k] - nx * 1.5 * hw, sy[k] - ny * 1.5 * hw, out bool inLow);
                if (!inHigh || !inLow)
                    return false;
                dropArc.Add(arc[k]);
                dropValue.Add(high - low);
            }
            if (dropValue.Count == 0)
                return false;

            // Части: проходимые концы и (у длинного) просвет посередине.
            double margin = settings.LedgeEndHexes * hw;
            List<double[]> parts = new List<double[]>(); // [от, до] по дуге
            bool gap = settings.LedgeGapMinLengthHexes > 0 && lengthHexes >= settings.LedgeGapMinLengthHexes
                && total * 0.5 - 2.0 * margin >= 3.0 * hw;
            if (gap)
            {
                parts.Add(new[] { 0.0, total * 0.5 });
                parts.Add(new[] { total * 0.5, total });
            }
            else
                parts.Add(new[] { 0.0, total });

            List<List<int>> partCells = new List<List<int>>();
            foreach (double[] part in parts)
            {
                List<int> cells = new List<int>();
                for (int i = 0; i < chain.Count; i++)
                {
                    double mid = (chainFirst[i] + chainLast[i]) * 0.5;
                    if (mid >= part[0] + margin && mid <= part[1] - margin)
                        cells.Add(chain[i]);
                }
                if (cells.Count < 3)
                    return false;
                // Перепад — по каждой части отдельно.
                double sum = 0.0;
                int n = 0;
                for (int i = 0; i < dropArc.Count; i++)
                {
                    if (dropArc[i] >= part[0] - 1e-6 && dropArc[i] <= part[1] + 1e-6)
                    {
                        sum += dropValue[i];
                        n++;
                    }
                }
                if (n == 0 || sum / n < settings.LedgeMinDrop)
                    return false;
                partCells.Add(cells);
            }

            // Поставить и проверить: вся суша по-прежнему достижима от старта.
            List<(int Cell, byte Old)> changed = new List<(int, byte)>();
            foreach (List<int> cells in partCells)
            {
                foreach (int cell in cells)
                {
                    changed.Add((cell, terrain[cell]));
                    terrain[cell] = Cliffs;
                }
            }
            bool[] reachable = Reachable();
            bool connected = true;
            for (int i = 0; i < count && connected; i++)
            {
                if (terrain[i] != Cliffs && terrain[i] != Water && !reachable[i])
                    connected = false;
            }
            if (!connected)
            {
                foreach ((int cell, byte old) in changed)
                    terrain[cell] = old;
                return false;
            }

            for (int p = 0; p < parts.Count; p++)
            {
                GeneratedMapLedge ledge = new GeneratedMapLedge { UpperSide = 1f, HeightScale = 1f };
                ledge.Cells.AddRange(partCells[p]);
                int from = -1, to = -1;
                for (int k = 0; k <= steps; k++)
                {
                    if (arc[k] >= parts[p][0] - 1e-6 && from < 0)
                        from = k;
                    if (arc[k] <= parts[p][1] + 1e-6)
                        to = k;
                }
                for (int k = from; k <= to; k += 8)
                    AddLedgePoint(ledge, sx[k], sy[k]);
                if ((to - from) % 8 != 0)
                    AddLedgePoint(ledge, sx[to], sy[to]);
                result.Ledges.Add(ledge);
            }

            int spacing = settings.LedgeSpacingHexes;
            foreach (int cell in chain)
            {
                foreach (int near in CellsWithin(cell, spacing))
                    blocked[near] = true;
                foreach (int near in CellsWithin(cell, 2))
                    ledgeNoLake[near] = true;
            }
            return true;
        }

        private void AddLedgePoint(GeneratedMapLedge ledge, double x, double y)
        {
            ledge.Points.Add(new MapPointData(grid.PixelToPercentX(x), grid.PixelToPercentY(y)));
        }
    }
}
