using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual
{
    // Кисть туши: линия (берег, край дороги, тропа) → мазки-многоугольники
    // по профилю WorldMapStrokeStyle. Нажим меняет толщину волной вдоль
    // линии, широкое перо — по направлению, концы сужаются или
    // заканчиваются каплей, кисть местами отрывается, линия может ложиться
    // штрихами внахлёст, гулять в руке и расходиться волосками сухой кисти.
    // Мазки рисует Painter2D (WorldMapVectorShape) — чётко при любом
    // приближении. Используется рисунком карты и предпросмотром в «Редакторе
    // туши».
    public sealed class WorldMapInkBrush
    {
        // Предел точек одного мазка: фигура Painter2D — до 65535 вершин.
        public const int MaxStrokePoints = 2500;

        private readonly WorldMapNoise noise;
        private readonly float hexPx;

        public WorldMapInkBrush(WorldMapNoise noise, float hexPx)
        {
            this.noise = noise;
            this.hexPx = Mathf.Max(1f, hexPx);
        }

        // width — толщина линии до профиля (общая толщина туши или ширина
        // тропы), color — цвет, если у профиля нет своего. wobble — сдвигать
        // ли линию дрожью руки (у края заливки дрожь уже в самом крае).
        public void Stroke(List<WorldMapVectorShape> shapes, IList<Vector2> line, bool closed, WorldMapStrokeStyle style, float width, Color color, int seed, bool wobble = true)
        {
            if (shapes == null || style == null || !style.Enabled || line == null || line.Count < 2)
                return;
            float baseWidth = width * style.Width;
            if (baseWidth <= 0.01f || style.Opacity <= 0f)
                return;
            Color ink = style.OwnColor ? style.Color : color;
            ink.a = Mathf.Clamp01(ink.a * style.Opacity);

            float spacing = Mathf.Clamp(Mathf.Min(baseWidth, hexPx * 0.1f), 0.5f, 6f);
            List<Vector2> points = Resample(line, closed, spacing);
            if (wobble && style.WobbleHexes > 0f)
                points = Wobble(points, closed, style.WobbleHexes * hexPx, style.WobbleWaveHexes * hexPx, seed);
            int count = points.Count;
            if (count < 2)
                return;

            float[] along = Along(points, closed, out float total);
            Vector2[] normals = new Vector2[count];
            float[] widths = new float[count];
            bool[] gap = new bool[count];
            bool anyGap = false;
            Vector2 nib = new Vector2(Mathf.Cos(style.NibAngle * Mathf.Deg2Rad), Mathf.Sin(style.NibAngle * Mathf.Deg2Rad));
            float pressureWave = Mathf.Max(2f, style.PressureWaveHexes * hexPx);
            float gapWave = Mathf.Max(2f, style.GapScaleHexes * hexPx);
            for (int i = 0; i < count; i++)
            {
                normals[i] = Normal(points, i, closed);
                float p = 0.5f + Wave(along[i], pressureWave, total, closed, seed * 13.7, 2, 0.5) * 2f;
                if (style.PressureJitter > 0f)
                    p += style.PressureJitter * Wave(along[i], pressureWave * 0.18f, total, closed, seed * 5.3 + 71.0, 2, 0.5) * 1.5f;
                float pressure = Mathf.Lerp(1f, Mathf.Lerp(style.Thinnest, style.Thickest, Mathf.Clamp01(p)), style.Pressure);
                float across = Mathf.Abs(normals[i].x * nib.x + normals[i].y * nib.y);
                float pen = Mathf.Lerp(1f, Mathf.Lerp(0.3f, 1.35f, across), style.Nib);
                widths[i] = baseWidth * Mathf.Max(0.06f, pressure * pen);
                if (style.Gaps > 0f)
                {
                    gap[i] = 0.5f + Wave(along[i], gapWave, total, closed, seed * 7.9 + 3.0, 2, 0.5) * 1.4f > 1f - style.Gaps;
                    anyGap |= gap[i];
                }
            }

            bool split = style.StrokeLengthHexes > 0f;
            bool dry = style.Bristles > 1 || style.Dryness > 0f;
            if (closed && !anyGap && !split && !dry && count <= MaxStrokePoints)
            {
                // Кольцо: внешний и внутренний край, между ними — тушь.
                Vector2[] outer = new Vector2[count], inner = new Vector2[count];
                for (int i = 0; i < count; i++)
                {
                    outer[i] = points[i] + normals[i] * widths[i] * 0.5f;
                    inner[i] = points[i] - normals[i] * widths[i] * 0.5f;
                }
                WorldMapVectorShape ring = new WorldMapVectorShape { Color = ink, OddEven = true };
                ring.Contours.Add(outer);
                ring.Contours.Add(inner);
                shapes.Add(ring);
                return;
            }

            // Разворачиваем в открытую линию: замкнутая начинается в обрыве
            // (шов не виден) и кончается своей первой точкой.
            int start = 0;
            if (closed && anyGap)
            {
                while (!gap[start])
                    start++;
            }
            int m = closed ? count + 1 : count;
            Unrolled run = new Unrolled(m);
            for (int j = 0; j < m; j++)
            {
                int i = (start + j) % count;
                run.Points[j] = points[i];
                run.Normals[j] = normals[i];
                run.Widths[j] = widths[i];
                run.Gap[j] = gap[i];
                run.Distance[j] = j == 0 ? 0f : run.Distance[j - 1] + Vector2.Distance(run.Points[j - 1], run.Points[j]);
            }
            bool seam = closed && !anyGap;

            System.Random random = new System.Random(seed * 7919 + 17);
            int from = -1;
            for (int j = 0; j <= m; j++)
            {
                bool on = j < m && !run.Gap[j];
                if (on && from < 0)
                    from = j;
                if ((on && j < m) || from < 0)
                    continue;
                int to = j - 1;
                if (split)
                    SplitIntoStrokes(shapes, run, from, to, style, baseWidth, ink, seed, random);
                else
                {
                    for (int chunk = from; chunk < to; chunk += MaxStrokePoints)
                    {
                        int end = Mathf.Min(chunk + MaxStrokePoints, to);
                        Emit(shapes, run, chunk, end, 0f, chunk == from && !seam, end == to && !seam, style, baseWidth, ink, seed);
                    }
                }
                from = -1;
            }
        }

        // Штрихи внахлёст: куски заданной длины, каждый следующий
        // начинается до конца предыдущего и чуть сдвинут в сторону.
        private void SplitIntoStrokes(List<WorldMapVectorShape> shapes, Unrolled run, int from, int to, WorldMapStrokeStyle style, float baseWidth, Color ink, int seed, System.Random random)
        {
            float length = Mathf.Max(1f, style.StrokeLengthHexes * hexPx);
            int s = from;
            int stroke = 0;
            while (s < to)
            {
                float l = length * (1f + style.StrokeLengthJitter * ((float)random.NextDouble() * 2f - 1f));
                int e = s;
                while (e < to && run.Distance[e] - run.Distance[s] < l && e - s < MaxStrokePoints)
                    e++;
                float offset = style.StrokeOffset * baseWidth * ((float)random.NextDouble() * 2f - 1f) * 0.5f;
                Emit(shapes, run, s, e, offset, true, true, style, baseWidth, ink, seed + stroke * 101);
                stroke++;
                if (e >= to)
                    break;
                float back = style.StrokeOverlap * l;
                int next = e;
                while (next > s + 1 && run.Distance[e] - run.Distance[next] < back)
                    next--;
                s = Mathf.Max(next, s + 1);
            }
        }

        // Один мазок from..to: волоски сухой кисти, у каждого — свои
        // сухие пропуски; концы по профилю.
        private void Emit(List<WorldMapVectorShape> shapes, Unrolled run, int from, int to, float offset, bool endStart, bool endEnd, WorldMapStrokeStyle style, float baseWidth, Color ink, int seed)
        {
            if (to - from < 1)
                return;
            int bristles = Mathf.Max(1, style.Bristles);
            float dryWave = Mathf.Max(2f, hexPx * 0.15f);
            for (int b = 0; b < bristles; b++)
            {
                float lateral = bristles > 1 ? (b / (bristles - 1f) - 0.5f) * style.BristleSpread : 0f;
                float scale = bristles > 1 ? Mathf.Max(0.22f, 1.5f / bristles) : 1f;
                int s = -1;
                for (int j = from; j <= to + 1; j++)
                {
                    bool on = j <= to && (style.Dryness <= 0f ||
                        0.5f + (float)noise.Fbm(run.Distance[j] / dryWave + seed * 3.3 + b * 17.1, b * 5.7 + seed * 0.13, 2, 0.5) * 1.5f <= 1f - style.Dryness * 0.75f);
                    if (on && s < 0)
                        s = j;
                    if ((on && j <= to) || s < 0)
                        continue;
                    int e = j - 1;
                    if (e > s)
                    {
                        bool dryStart = s != from, dryEnd = e != to;
                        Polygon(shapes, run, s, e, lateral, scale, offset, endStart || dryStart, endEnd || dryEnd, dryStart, dryEnd, style, baseWidth, ink);
                    }
                    s = -1;
                }
            }
        }

        private void Polygon(List<WorldMapVectorShape> shapes, Unrolled run, int s, int e, float lateral, float scale, float offset,
            bool endStart, bool endEnd, bool shortStart, bool shortEnd, WorldMapStrokeStyle style, float baseWidth, Color ink)
        {
            int n = e - s + 1;
            float length = run.Distance[e] - run.Distance[s];
            float taper = Mathf.Max(0.01f, Mathf.Min(style.TaperLength * baseWidth, length * 0.45f));
            float shortTaper = Mathf.Max(0.01f, Mathf.Min(baseWidth, length * 0.45f));
            bool round = style.Ends != WorldMapStrokeEnd.Pointed;
            List<Vector2> outline = new List<Vector2>(n * 2 + 16);
            Vector2[] centers = new Vector2[n];
            float[] halves = new float[n];
            for (int k = 0; k < n; k++)
            {
                int j = s + k;
                float d0 = run.Distance[j] - run.Distance[s], d1 = run.Distance[e] - run.Distance[j];
                float f = 1f;
                float t0 = shortStart ? shortTaper : taper, t1 = shortEnd ? shortTaper : taper;
                if (style.Ends == WorldMapStrokeEnd.Pointed || shortStart || shortEnd)
                {
                    if (endStart && (style.Ends == WorldMapStrokeEnd.Pointed || shortStart) && d0 < t0)
                        f = Mathf.Min(f, Mathf.Clamp01(d0 / t0) * 0.85f + 0.15f);
                    if (endEnd && (style.Ends == WorldMapStrokeEnd.Pointed || shortEnd) && d1 < t1)
                        f = Mathf.Min(f, Mathf.Clamp01(d1 / t1) * 0.85f + 0.15f);
                }
                if (style.Ends == WorldMapStrokeEnd.Blob)
                {
                    if (endStart && !shortStart && d0 < taper)
                        f *= 1f + 0.7f * (1f - d0 / taper);
                    if (endEnd && !shortEnd && d1 < taper)
                        f *= 1f + 0.7f * (1f - d1 / taper);
                }
                float w = run.Widths[j] * scale * f;
                centers[k] = run.Points[j] + run.Normals[j] * (lateral * run.Widths[j] + offset);
                halves[k] = w * 0.5f;
                outline.Add(centers[k] + run.Normals[j] * halves[k]);
            }
            if (round && endEnd && !shortEnd)
                Cap(outline, centers[n - 1], run.Normals[e], halves[n - 1], true);
            for (int k = n - 1; k >= 0; k--)
                outline.Add(centers[k] - run.Normals[s + k] * halves[k]);
            if (round && endStart && !shortStart)
                Cap(outline, centers[0], run.Normals[s], halves[0], false);
            WorldMapVectorShape shape = new WorldMapVectorShape { Color = ink, OddEven = false };
            shape.Contours.Add(outline.ToArray());
            shapes.Add(shape);
        }

        // Полукруглый конец: от левого края к правому через переднюю точку
        // (forward) или от правого к левому через заднюю.
        private static void Cap(List<Vector2> outline, Vector2 center, Vector2 normal, float radius, bool forward)
        {
            Vector2 tangent = new Vector2(normal.y, -normal.x);
            const int steps = 6;
            for (int i = 1; i < steps; i++)
            {
                float phi = Mathf.PI * i / steps;
                Vector2 direction = forward
                    ? normal * Mathf.Cos(phi) + tangent * Mathf.Sin(phi)
                    : -normal * Mathf.Cos(phi) - tangent * Mathf.Sin(phi);
                outline.Add(center + direction * radius);
            }
        }

        // Дрожь руки: линия гуляет по нормали плавной волной.
        public List<Vector2> Wobble(IList<Vector2> line, bool closed, float amplitudePx, float wavePx, int seed)
        {
            List<Vector2> points = new List<Vector2>(line);
            if (amplitudePx <= 0f || points.Count < 2)
                return points;
            float[] along = Along(points, closed, out float total);
            Vector2[] normals = new Vector2[points.Count];
            for (int i = 0; i < points.Count; i++)
                normals[i] = Normal(points, i, closed);
            float wave = Mathf.Max(2f, wavePx);
            for (int i = 0; i < points.Count; i++)
                points[i] += normals[i] * Wave(along[i], wave, total, closed, seed * 2.9 + 41.0, 2, 0.5) * 2f * amplitudePx;
            return points;
        }

        // Шум вдоль линии; у замкнутой — по кругу, без скачка на шве.
        private float Wave(float along, float wavePx, float total, bool closed, double salt, int octaves, double persistence)
        {
            if (closed && total > wavePx)
            {
                double turns = Math.Max(1.0, Math.Round(total / wavePx));
                double angle = along / total * Math.PI * 2.0;
                double radius = turns / (Math.PI * 2.0);
                return (float)noise.Fbm(Math.Cos(angle) * radius + salt, Math.Sin(angle) * radius - salt * 0.7, octaves, persistence);
            }
            return (float)noise.Fbm(along / wavePx + salt, salt * 0.37 + 0.5, octaves, persistence);
        }

        private static float[] Along(List<Vector2> points, bool closed, out float total)
        {
            float[] along = new float[points.Count];
            for (int i = 1; i < points.Count; i++)
                along[i] = along[i - 1] + Vector2.Distance(points[i - 1], points[i]);
            total = along[points.Count - 1] + (closed ? Vector2.Distance(points[points.Count - 1], points[0]) : 0f);
            return along;
        }

        public static Vector2 Normal(IList<Vector2> points, int i, bool closed)
        {
            int count = points.Count;
            Vector2 previous = i > 0 ? points[i - 1] : closed ? points[count - 1] : points[i];
            Vector2 next = i < count - 1 ? points[i + 1] : closed ? points[0] : points[i];
            Vector2 tangent = next - previous;
            if (tangent.sqrMagnitude < 1e-6f)
                return Vector2.up;
            tangent.Normalize();
            return new Vector2(-tangent.y, tangent.x);
        }

        // Равномерные точки вдоль линии (точки остаются на исходной линии).
        public static List<Vector2> Resample(IList<Vector2> line, bool closed, float spacing)
        {
            List<Vector2> result = new List<Vector2>();
            if (line == null || line.Count == 0)
                return result;
            List<Vector2> source = new List<Vector2>(line);
            if (closed)
                source.Add(line[0]);
            result.Add(source[0]);
            float carry = 0f;
            for (int i = 1; i < source.Count; i++)
            {
                Vector2 a = source[i - 1], b = source[i];
                float segment = Vector2.Distance(a, b);
                float t = spacing - carry;
                while (t <= segment)
                {
                    result.Add(Vector2.Lerp(a, b, t / segment));
                    t += spacing;
                }
                carry = segment - (t - spacing);
            }
            if (!closed && Vector2.Distance(result[result.Count - 1], source[source.Count - 1]) > spacing * 0.25f)
                result.Add(source[source.Count - 1]);
            if (closed && result.Count > 2 && Vector2.Distance(result[result.Count - 1], result[0]) < spacing * 0.5f)
                result.RemoveAt(result.Count - 1);
            return result;
        }

        // Линия, развёрнутая в открытую: точки, нормали, толщина, обрывы и
        // пройденный путь.
        private sealed class Unrolled
        {
            public readonly Vector2[] Points;
            public readonly Vector2[] Normals;
            public readonly float[] Widths;
            public readonly bool[] Gap;
            public readonly float[] Distance;

            public Unrolled(int count)
            {
                Points = new Vector2[count];
                Normals = new Vector2[count];
                Widths = new float[count];
                Gap = new bool[count];
                Distance = new float[count];
            }
        }
    }
}
