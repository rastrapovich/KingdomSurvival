using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual
{
    // Векторная часть рисунка карты (слои видеокартой): заливки местности и
    // воды с рваным краем и тушь мазками переменной толщины. Всё — контуры
    // и многоугольники в пикселях исходной карты; их рисует Painter2D со
    // сглаживанием в экранных пикселях, поэтому край чёткий при любом
    // приближении. Узоры местности и рябь лежат в тех же рваных границах
    // (треугольники области вместо растворения в прозрачность).
    public static partial class WorldMapPainter
    {
        private sealed partial class PaintJob
        {
            // Шаг сетки контуров, в пикселях рисунка.
            private float VectorStep => Mathf.Max(2f, hexPx / 10f);

            // Поле «здесь лес» (> 0 внутри) — по нему же рисуется кромка леса.
            private float[] forestField;

            private int GridWidth => Mathf.CeilToInt(width / VectorStep) + 1;
            private int GridHeight => Mathf.CeilToInt(height / VectorStep) + 1;

            // --------------------------------------------------------------
            // Местность: разбиение на типы без щелей и наложений. В каждом
            // узле сетки — доля каждого типа вокруг плюс свой шум рваного
            // края; тип «побеждает» там, где его оценка выше остальных.
            // --------------------------------------------------------------

            private void BuildVectorTerrain()
            {
                float step = VectorStep;
                int gw = GridWidth, gh = GridHeight, nodes = gw * gh;
                const int typeCount = 16;
                float[][] score = new float[typeCount][];
                int radius = Mathf.Max(1, Mathf.RoundToInt(step));
                int sampleStep = Mathf.Max(1, radius / 3);
                int[] counts = new int[typeCount];
                float ragged = style.EdgeRaggedness * 0.45f;
                double raggedFrequency = 1.0 / Mathf.Max(2f, style.RaggedScaleHexes * hexPx);

                for (int j = 0; j < gh; j++)
                {
                    for (int i = 0; i < gw; i++)
                    {
                        Array.Clear(counts, 0, typeCount);
                        int total = 0;
                        int cx = Mathf.RoundToInt(i * step), cy = Mathf.RoundToInt(j * step);
                        for (int y = Mathf.Max(0, cy - radius); y <= Mathf.Min(height - 1, cy + radius); y += sampleStep)
                        {
                            for (int x = Mathf.Max(0, cx - radius); x <= Mathf.Min(width - 1, cx + radius); x += sampleStep)
                            {
                                counts[area[y * width + x]]++;
                                total++;
                            }
                        }
                        int node = j * gw + i;
                        for (int t = 0; t < typeCount; t++)
                        {
                            if (counts[t] == 0 && score[t] == null)
                                continue;
                            score[t] ??= Filled(nodes, -1f);
                            score[t][node] = total > 0 ? counts[t] / (float)total : 0f;
                        }
                    }
                }

                // Шум рваного края — свой у каждого типа.
                for (int t = 0; t < typeCount; t++)
                {
                    if (score[t] == null || ragged <= 0f)
                        continue;
                    for (int j = 0; j < gh; j++)
                    {
                        for (int i = 0; i < gw; i++)
                        {
                            int node = j * gw + i;
                            // Где типа рядом нет, край его не касается.
                            if (score[t][node] <= 0f)
                            {
                                score[t][node] = 0f;
                                continue;
                            }
                            score[t][node] += (float)noise.Fbm(i * step * raggedFrequency + t * 31.7, j * step * raggedFrequency - t * 17.3, 2, 0.6) * ragged;
                        }
                    }
                }

                float[] best = Filled(nodes, float.MinValue), second = Filled(nodes, float.MinValue);
                int[] bestType = new int[nodes];
                for (int t = 0; t < typeCount; t++)
                {
                    if (score[t] == null)
                        continue;
                    for (int n = 0; n < nodes; n++)
                    {
                        float value = score[t][n];
                        if (value > best[n])
                        {
                            second[n] = best[n];
                            best[n] = value;
                            bestType[n] = t;
                        }
                        else if (value > second[n])
                            second[n] = value;
                    }
                }

                float[] Field(int t)
                {
                    float[] field = new float[nodes];
                    bool inside = false;
                    for (int n = 0; n < nodes; n++)
                    {
                        float other = bestType[n] == t ? second[n] : best[n];
                        field[n] = score[t][n] - (other == float.MinValue ? -1f : other);
                        inside |= field[n] > 0f;
                    }
                    return inside ? field : null;
                }

                int forest = (int)WorldMapGameplayTerrainType.Forest;
                if (score[forest] != null)
                    forestField = Field(forest);

                if (!style.DrawTerrain)
                    return;

                foreach (WorldMapGameplayTerrainType type in WorldMapTerrainLabels.All)
                {
                    int t = (int)type;
                    if (score[t] == null || type == WorldMapGameplayTerrainType.Water)
                        continue;
                    WorldMapTerrainArt art = style.FindTerrain(type);
                    if (art == null || !art.Enabled)
                        continue;
                    float[] field = t == forest ? forestField : Field(t);
                    if (field == null)
                        continue;

                    bool fill = art.Opacity > 0f;
                    Image pattern = style.UseTerrainPatterns && art.Pattern != null && art.PatternStrength > 0f ? LoadImage(art.Pattern) : null;
                    if (!fill && pattern == null)
                        continue;
                    float[] triangles = TornTriangles(field, gw, gh);
                    if (fill)
                    {
                        Color color = Grade(art.Color);
                        AddFill("Местность: " + WorldMapTerrainLabels.Name(type), new Color(color.r, color.g, color.b, art.Opacity), triangles, null);
                    }
                    if (pattern != null)
                        AddMasked("Узор: " + WorldMapTerrainLabels.Name(type), GradedCopy(pattern, Color.white), art.PatternTileHexes * hexPx, art.PatternStrength, triangles);
                }
            }

            // --------------------------------------------------------------
            // Вода: заливка ступенями глубины (отмель → мелко → глубже →
            // глубоко) с рваными краями ступеней, рябь в границах воды,
            // тушь берега мазком.
            // --------------------------------------------------------------

            private void BuildVectorWater(float[] toLand)
            {
                float step = VectorStep;
                int gw = GridWidth, gh = GridHeight, nodes = gw * gh;
                float[] shore = new float[nodes];
                float[] depth = new float[nodes];
                int radius = Mathf.Max(1, Mathf.RoundToInt(step * 0.5f));
                double frequency = 1.0 / Mathf.Max(2f, style.RaggedScaleHexes * hexPx);
                float ragged = style.EdgeRaggedness;
                for (int j = 0; j < gh; j++)
                {
                    for (int i = 0; i < gw; i++)
                    {
                        int cx = Mathf.Clamp(Mathf.RoundToInt(i * step), 0, width - 1), cy = Mathf.Clamp(Mathf.RoundToInt(j * step), 0, height - 1);
                        float sum = 0f;
                        int n = 0;
                        for (int y = Mathf.Max(0, cy - radius); y <= Mathf.Min(height - 1, cy + radius); y++)
                        {
                            for (int x = Mathf.Max(0, cx - radius); x <= Mathf.Min(width - 1, cx + radius); x++)
                            {
                                sum += waterCoverage[y * width + x];
                                n++;
                            }
                        }
                        float wobble = (float)noise.Fbm(i * step * frequency + 91.1, j * step * frequency - 44.7, 3, 0.6);
                        shore[j * gw + i] = sum / Mathf.Max(1, n) - 0.5f + wobble * ragged * 0.18f;
                        depth[j * gw + i] = toLand[cy * width + cx] + wobble * ragged * step * 0.8f;
                    }
                }

                // Глубина по расстоянию до берега даёт гребень по оси реки —
                // ступени тянулись бы вдоль него тонкими шипами.
                depth = Blur(Blur(depth, gw, gh), gw, gh);

                Color shallow = style.WaterShallowColor;
                Color deep = style.WaterDeepColor;
                Color shoreColor = new Color(Mathf.Clamp01(shallow.r * 1.25f), Mathf.Clamp01(shallow.g * 1.25f), Mathf.Clamp01(shallow.b * 1.2f), 1f);
                float shorePx = Mathf.Max(1f, style.ShoreBand * hexPx);
                float deepPx = Mathf.Max(2f, hexPx * 0.9f);

                // Ступени — треугольниками по порядку: каждая следующая
                // поверх предыдущей.
                List<float> fills = new List<float>();
                List<Color32> colors = new List<Color32>();
                void Band(float[] field, Color color)
                {
                    float[] triangles = TornTriangles(field, gw, gh);
                    Color graded = Grade(color);
                    Color32 opaque = new Color(graded.r, graded.g, graded.b, 1f);
                    fills.AddRange(triangles);
                    for (int t = 0; t < triangles.Length / 6; t++)
                        colors.Add(opaque);
                }
                float[] Inside(float threshold)
                {
                    float[] field = new float[nodes];
                    for (int n = 0; n < nodes; n++)
                        field[n] = Mathf.Min(shore[n], (depth[n] - threshold) / step);
                    return field;
                }

                Band(shore, style.DrawShoreBand ? shoreColor : shallow);
                if (style.DrawShoreBand)
                    Band(Inside(shorePx), shallow);
                Band(Inside(deepPx * 0.45f), Color.Lerp(shallow, deep, 0.5f));
                Band(Inside(deepPx), deep);
                if (fills.Count == 0)
                    return;
                AddFill("Вода", Color.white, fills.ToArray(), colors.ToArray());

                if (style.UseWaterPattern && style.WaterPattern != null && style.WaterPatternStrength > 0f)
                {
                    Image ripple = LoadImage(style.WaterPattern);
                    if (ripple != null)
                        AddMasked("Рябь", GradedCopy(ripple, Color.white), style.WaterPatternTileHexes * hexPx, style.WaterPatternStrength, TornTriangles(shore, gw, gh));
                }

                if (style.DrawInk && style.DrawShoreInk && inkPx > 0f)
                {
                    List<WorldMapVectorShape> ink = new List<WorldMapVectorShape>();
                    Color color = InkColor(0.92f);
                    int seed = 1;
                    foreach (Vector2[] contour in Contours(shore, gw, gh))
                        AddInk(ink, Smooth(contour), true, inkPx * 1.15f, color, style.InkVariation, style.InkGaps, seed++, true);
                    AddVector("Тушь берега", ink);
                }
            }

            private void BuildVectorForestInk()
            {
                if (forestField == null || !style.DrawInk || !style.DrawForestOutline || inkPx <= 0f || style.ForestOutline <= 0f)
                    return;
                List<WorldMapVectorShape> ink = new List<WorldMapVectorShape>();
                Color color = InkColor(style.ForestOutline);
                int seed = 501;
                foreach (Vector2[] contour in Contours(forestField, GridWidth, GridHeight))
                    AddInk(ink, Smooth(contour), true, inkPx * 0.85f, color, style.InkVariation, style.InkGaps * 1.5f, seed++, true);
                AddVector("Кромка леса", ink);
            }

            // --------------------------------------------------------------
            // Дороги, тропы, мосты — мазками.
            // --------------------------------------------------------------

            private void BuildVectorRoads()
            {
                if (map.Roads == null)
                    return;
                float roadWidth = Mathf.Max(1.6f, style.RoadWidthHexes * hexPx);
                float trailWidth = Mathf.Max(1.2f, style.TrailWidthHexes * hexPx);
                List<WorldMapVectorShape> ink = new List<WorldMapVectorShape>();
                List<WorldMapVectorShape> fill = new List<WorldMapVectorShape>();
                int seed = 1001;
                foreach (GeneratedMapLine line in map.Roads)
                {
                    if (line?.Points == null || line.Points.Count < 2)
                        continue;
                    List<Vector2> points = ToPixels(line.Points);
                    bool trail = line.Kind == GeneratedMapLine.TrailKind;
                    seed++;
                    if (trail)
                    {
                        if (!style.DrawTrails)
                            continue;
                        Color color = Grade(style.TrailColor);
                        float dash = style.TrailDashed ? Mathf.Max(3f, trailWidth * 5f) : 0f;
                        foreach (List<Vector2> part in Dashes(points, dash, dash * 0.8f))
                            AddInk(fill, part, false, trailWidth, new Color(color.r, color.g, color.b, 0.95f), style.InkVariation * 0.5f, 0f, seed, true);
                        continue;
                    }
                    if (!style.DrawRoads)
                        continue;
                    if (style.DrawInk && style.RoadInk > 0f && inkPx > 0f)
                        AddInk(ink, points, false, roadWidth + inkPx * 1.3f, InkColor(style.RoadInk), style.InkVariation * 0.7f, 0f, seed, false);
                    Color road = Grade(style.RoadColor);
                    float roadDash = style.RoadDashed ? roadWidth * 6f : 0f;
                    foreach (List<Vector2> part in Dashes(points, roadDash, roadDash * 0.6f))
                        AddInk(fill, part, false, roadWidth, new Color(road.r, road.g, road.b, 1f), style.InkVariation * 0.2f, 0f, seed, style.RoadDashed);
                }
                ink.AddRange(fill);
                AddVector("Дороги и тропы", ink);
            }

            private void BuildVectorBridges()
            {
                if (!style.DrawBridges || map.Bridges == null || map.Bridges.Count == 0)
                    return;
                float roadHalf = Mathf.Max(0.8f, style.RoadWidthHexes * hexPx * 0.5f);
                float length = Mathf.Max(roadHalf * 4f, hexPx * 0.7f);
                List<WorldMapVectorShape> shapes = new List<WorldMapVectorShape>();
                Color plank = Grade(style.BridgeColor);
                Color ink = InkColor(1f);
                int seed = 3001;
                foreach (MapPointData bridge in map.Bridges)
                {
                    Vector2 center = ToPixel(bridge);
                    Vector2 direction = RoadDirectionAt(center);
                    Sprite sprite = Pick(style.Pool(style.BridgeSprites, style.UseBridgeSprites));
                    if (sprite != null)
                    {
                        DrawSprite(sprite, center + new Vector2(0f, length * 0.25f), length * 0.6f, Color.white, direction.x < 0f);
                        continue;
                    }
                    Vector2 normal = new Vector2(-direction.y, direction.x);
                    float half = roadHalf * 1.5f;
                    Vector2 a = center - direction * length * 0.5f, c = center + direction * length * 0.5f;
                    WorldMapVectorShape deck = new WorldMapVectorShape { Color = new Color(plank.r, plank.g, plank.b, 1f), OddEven = false };
                    deck.Contours.Add(new[] { a + normal * half, c + normal * half, c - normal * half, a - normal * half });
                    shapes.Add(deck);
                    float rail = Mathf.Max(0.8f, inkPx * 0.7f);
                    AddInk(shapes, new List<Vector2> { a + normal * half, c + normal * half }, false, rail, ink, style.InkVariation * 0.5f, 0f, seed++, true);
                    AddInk(shapes, new List<Vector2> { a - normal * half, c - normal * half }, false, rail, ink, style.InkVariation * 0.5f, 0f, seed++, true);
                }
                AddVector("Мосты", shapes);
            }

            // --------------------------------------------------------------
            // Мазок туши: ширина меняется по шуму вдоль линии (то волосок,
            // то нажим), концы сужаются, местами кисть отрывается.
            // --------------------------------------------------------------

            private void AddInk(List<WorldMapVectorShape> shapes, List<Vector2> line, bool closed, float width, Color color, float variation, float gaps, int seed, bool taper)
            {
                List<Vector2> points = Resample(line, closed, Mathf.Max(1f, Mathf.Min(width * 1.5f, hexPx * 0.15f)));
                int count = points.Count;
                if (count < 2)
                    return;
                float[] widths = new float[count];
                bool[] gap = new bool[count];
                float length = 0f;
                double lengthFrequency = 1.0 / Mathf.Max(2f, hexPx * 0.9f);
                double gapFrequency = 1.0 / Mathf.Max(2f, hexPx * 2.2f);
                bool anyGap = false;
                for (int i = 0; i < count; i++)
                {
                    if (i > 0)
                        length += Vector2.Distance(points[i - 1], points[i]);
                    // Нажим: волна вдоль линии — то волосок, то полный мазок.
                    float n = Mathf.Clamp01(0.5f + (float)noise.Fbm(length * lengthFrequency + seed * 13.7, seed * 3.1, 3, 0.55) * 2.2f);
                    // Широкое перо: поперёк пера линия толще, вдоль — тоньше.
                    Vector2 normal = Normal(points, i, closed);
                    float nib = Mathf.Lerp(0.5f, 1.3f, Mathf.Abs(normal.x * NibAxis.x + normal.y * NibAxis.y));
                    widths[i] = width * Mathf.Lerp(1f, Mathf.Max(0.15f, (0.25f + 1.5f * n) * nib), variation);
                    if (gaps > 0f)
                    {
                        float g = 0.5f + (float)noise.Fbm(length * gapFrequency + seed * 7.9, 5.3 + seed, 2, 0.5) * 1.2f;
                        gap[i] = g > 1f - gaps;
                        anyGap |= gap[i];
                    }
                }

                if (closed && !anyGap && count <= MaxStrokePoints)
                {
                    // Кольцо: внешний и внутренний край, между ними — тушь.
                    Vector2[] left = new Vector2[count], right = new Vector2[count];
                    for (int i = 0; i < count; i++)
                    {
                        Vector2 normal = Normal(points, i, true);
                        left[i] = points[i] + normal * widths[i] * 0.5f;
                        right[i] = points[i] - normal * widths[i] * 0.5f;
                    }
                    WorldMapVectorShape ring = new WorldMapVectorShape { Color = color, OddEven = true };
                    ring.Contours.Add(left);
                    ring.Contours.Add(right);
                    shapes.Add(ring);
                    return;
                }

                if (closed && !anyGap)
                {
                    // Длинное кольцо — кусками встык (у фигуры Painter2D
                    // предел вершин), последний кусок замыкается на первую точку.
                    for (int from = 0; from < count; from += MaxStrokePoints)
                        AddStrokeRun(shapes, points, widths, from, Mathf.Min(from + MaxStrokePoints, count), true, false, false, color);
                    return;
                }

                int start = -1;
                for (int i = 0; i <= count; i++)
                {
                    bool on = i < count && !gap[i];
                    if (on && start < 0)
                        start = i;
                    if ((!on || i == count) && start >= 0)
                    {
                        for (int from = start; from < i - 1; from += MaxStrokePoints)
                        {
                            int to = Mathf.Min(from + MaxStrokePoints, i - 1);
                            AddStrokeRun(shapes, points, widths, from, to, false, taper && from == start, taper && to == i - 1, color);
                        }
                        start = -1;
                    }
                }
            }

            // Предел точек одного мазка: фигура Painter2D — до 65535 вершин.
            private const int MaxStrokePoints = 2500;

            // Кусок мазка from..to (индексы по кругу для замкнутой линии).
            private void AddStrokeRun(List<WorldMapVectorShape> shapes, List<Vector2> points, float[] widths, int from, int to, bool closed, bool taperStart, bool taperEnd, Color color)
            {
                int n = to - from + 1;
                if (n < 2)
                    return;
                int count = points.Count;
                Vector2[] outline = new Vector2[n * 2];
                float runLength = 0f;
                for (int i = from + 1; i <= to; i++)
                    runLength += Vector2.Distance(points[(i - 1) % count], points[i % count]);
                float taperLength = Mathf.Min(runLength * 0.3f, Mathf.Max(widths[from % count], 1f) * 3f);
                float walked = 0f;
                for (int k = 0; k < n; k++)
                {
                    int i = (from + k) % count;
                    if (k > 0)
                        walked += Vector2.Distance(points[(from + k - 1) % count], points[i]);
                    float w = widths[i];
                    if (taperLength > 0f)
                    {
                        float distance = Mathf.Min(taperStart ? walked : float.MaxValue, taperEnd ? runLength - walked : float.MaxValue);
                        if (distance < taperLength)
                            w *= Mathf.Clamp01(distance / taperLength * 0.85f + 0.15f);
                    }
                    Vector2 normal = Normal(points, i, closed);
                    outline[k] = points[i] + normal * w * 0.5f;
                    outline[n * 2 - 1 - k] = points[i] - normal * w * 0.5f;
                }
                WorldMapVectorShape shape = new WorldMapVectorShape { Color = color, OddEven = false };
                shape.Contours.Add(outline);
                shapes.Add(shape);
            }

            // Направление нажима пера (по диагонали, как держат кисть).
            private static readonly Vector2 NibAxis = new Vector2(0.6f, 0.8f);

            private static Vector2 Normal(List<Vector2> points, int i, bool closed)
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

            private static List<Vector2> Resample(List<Vector2> line, bool closed, float spacing)
            {
                List<Vector2> result = new List<Vector2>();
                if (line.Count == 0)
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
                if (!closed && result.Count > 0 && Vector2.Distance(result[result.Count - 1], source[source.Count - 1]) > spacing * 0.25f)
                    result.Add(source[source.Count - 1]);
                if (closed && result.Count > 2 && Vector2.Distance(result[result.Count - 1], result[0]) < spacing * 0.5f)
                    result.RemoveAt(result.Count - 1);
                return result;
            }

            // Пунктир: куски линии длиной dash через gap; dash 0 — вся линия.
            private static List<List<Vector2>> Dashes(List<Vector2> line, float dash, float gap)
            {
                List<List<Vector2>> parts = new List<List<Vector2>>();
                if (dash <= 0f)
                {
                    parts.Add(line);
                    return parts;
                }
                List<Vector2> current = new List<Vector2> { line[0] };
                bool drawing = true;
                float left = dash;
                for (int i = 1; i < line.Count; i++)
                {
                    Vector2 a = line[i - 1], b = line[i];
                    float segment = Vector2.Distance(a, b);
                    float position = 0f;
                    while (segment - position > left)
                    {
                        position += left;
                        Vector2 cut = Vector2.Lerp(a, b, position / segment);
                        if (drawing)
                        {
                            current.Add(cut);
                            if (current.Count >= 2)
                                parts.Add(current);
                        }
                        current = new List<Vector2> { cut };
                        drawing = !drawing;
                        left = drawing ? dash : gap;
                    }
                    left -= segment - position;
                    if (drawing)
                        current.Add(b);
                }
                if (drawing && current.Count >= 2)
                    parts.Add(current);
                return parts;
            }

            // --------------------------------------------------------------
            // Марширующие квадраты по полю в узлах сетки (> 0 — внутри).
            // За краем сетки поле отрицательно, поэтому все контуры замкнуты;
            // точки прижимаются к краю рисунка.
            // --------------------------------------------------------------

            private float FieldAt(float[] field, int gw, int gh, int i, int j) =>
                i < 0 || j < 0 || i >= gw || j >= gh ? -1f : field[j * gw + i];

            private Vector2 NodePoint(int i, int j) =>
                new Vector2(Mathf.Clamp(i * VectorStep, 0f, width), Mathf.Clamp(j * VectorStep, 0f, height));

            // Точка на ребре клетки: 0 — верх, 1 — право, 2 — низ, 3 — лево.
            private Vector2 EdgePoint(float[] field, int gw, int gh, int i, int j, int edge)
            {
                int ai = i, aj = j, bi = i, bj = j;
                switch (edge)
                {
                    case 0: bi = i + 1; break;
                    case 1: ai = i + 1; bi = i + 1; bj = j + 1; break;
                    case 2: ai = i + 1; aj = j + 1; bj = j + 1; break;
                    default: aj = j + 1; break;
                }
                float va = FieldAt(field, gw, gh, ai, aj), vb = FieldAt(field, gw, gh, bi, bj);
                float t = Mathf.Abs(va - vb) < 1e-6f ? 0.5f : Mathf.Clamp01(va / (va - vb));
                return Vector2.Lerp(NodePoint(ai, aj), NodePoint(bi, bj), t);
            }

            private long EdgeKey(int gw, int i, int j, int edge)
            {
                // Общее ребро соседних клеток — один ключ.
                switch (edge)
                {
                    case 0: return ((long)(j + 1) * (gw + 2) + (i + 1)) * 2;
                    case 2: return ((long)(j + 2) * (gw + 2) + (i + 1)) * 2;
                    case 3: return ((long)(j + 1) * (gw + 2) + (i + 1)) * 2 + 1;
                    default: return ((long)(j + 1) * (gw + 2) + (i + 2)) * 2 + 1;
                }
            }

            private static readonly int[][] SegmentTable =
            {
                new int[0], new[] { 3, 0 }, new[] { 0, 1 }, new[] { 3, 1 }, new[] { 1, 2 }, null, new[] { 0, 2 }, new[] { 3, 2 },
                new[] { 2, 3 }, new[] { 0, 2 }, null, new[] { 1, 2 }, new[] { 3, 1 }, new[] { 0, 1 }, new[] { 3, 0 }, new int[0]
            };

            private List<Vector2[]> Contours(float[] field, int gw, int gh)
            {
                Dictionary<long, Vector2> points = new Dictionary<long, Vector2>();
                Dictionary<long, List<long>> links = new Dictionary<long, List<long>>();
                void Link(int i, int j, int e0, int e1)
                {
                    long k0 = EdgeKey(gw, i, j, e0), k1 = EdgeKey(gw, i, j, e1);
                    if (!points.ContainsKey(k0)) points[k0] = EdgePoint(field, gw, gh, i, j, e0);
                    if (!points.ContainsKey(k1)) points[k1] = EdgePoint(field, gw, gh, i, j, e1);
                    if (!links.TryGetValue(k0, out List<long> l0)) links[k0] = l0 = new List<long>(2);
                    if (!links.TryGetValue(k1, out List<long> l1)) links[k1] = l1 = new List<long>(2);
                    l0.Add(k1);
                    l1.Add(k0);
                }

                for (int j = -1; j < gh; j++)
                {
                    for (int i = -1; i < gw; i++)
                    {
                        float a = FieldAt(field, gw, gh, i, j), b = FieldAt(field, gw, gh, i + 1, j);
                        float c = FieldAt(field, gw, gh, i + 1, j + 1), d = FieldAt(field, gw, gh, i, j + 1);
                        int index = (a > 0f ? 1 : 0) | (b > 0f ? 2 : 0) | (c > 0f ? 4 : 0) | (d > 0f ? 8 : 0);
                        if (index == 0 || index == 15)
                            continue;
                        if (index == 5 || index == 10)
                        {
                            bool centerInside = (a + b + c + d) * 0.25f > 0f;
                            bool cutAC = (index == 5) == !centerInside;
                            if (cutAC) { Link(i, j, 3, 0); Link(i, j, 1, 2); }
                            else { Link(i, j, 0, 1); Link(i, j, 2, 3); }
                            continue;
                        }
                        int[] segment = SegmentTable[index];
                        Link(i, j, segment[0], segment[1]);
                    }
                }

                List<Vector2[]> contours = new List<Vector2[]>();
                HashSet<long> visited = new HashSet<long>();
                foreach (long startKey in links.Keys)
                {
                    if (visited.Contains(startKey))
                        continue;
                    List<Vector2> contour = new List<Vector2>();
                    long previous = -1, current = startKey;
                    while (current >= 0 && !visited.Contains(current))
                    {
                        visited.Add(current);
                        contour.Add(points[current]);
                        long next = -1;
                        foreach (long candidate in links[current])
                        {
                            if (candidate != previous && !visited.Contains(candidate))
                            {
                                next = candidate;
                                break;
                            }
                        }
                        previous = current;
                        current = next;
                    }
                    Vector2[] simplified = Simplify(contour, VectorStep * 0.2f);
                    if (simplified.Length >= 3)
                        contours.Add(simplified);
                }
                return contours;
            }

            // Треугольники области (поле > 0): многоугольник внутри каждой
            // клетки веером. Для плиток по маске.
            private float[] Triangles(float[] field, int gw, int gh)
            {
                List<float> result = new List<float>();
                List<Vector2> polygon = new List<Vector2>(8);
                for (int j = 0; j < gh - 1; j++)
                {
                    for (int i = 0; i < gw - 1; i++)
                    {
                        float a = field[j * gw + i], b = field[j * gw + i + 1], c = field[(j + 1) * gw + i + 1], d = field[(j + 1) * gw + i];
                        if (a <= 0f && b <= 0f && c <= 0f && d <= 0f)
                            continue;
                        polygon.Clear();
                        float[] values = { a, b, c, d };
                        Vector2[] corners = { NodePoint(i, j), NodePoint(i + 1, j), NodePoint(i + 1, j + 1), NodePoint(i, j + 1) };
                        for (int k = 0; k < 4; k++)
                        {
                            float v0 = values[k], v1 = values[(k + 1) % 4];
                            if (v0 > 0f)
                                polygon.Add(corners[k]);
                            if ((v0 > 0f) != (v1 > 0f))
                                polygon.Add(Vector2.Lerp(corners[k], corners[(k + 1) % 4], Mathf.Clamp01(v0 / (v0 - v1))));
                        }
                        for (int k = 1; k < polygon.Count - 1; k++)
                        {
                            result.Add(polygon[0].x); result.Add(polygon[0].y);
                            result.Add(polygon[k].x); result.Add(polygon[k].y);
                            result.Add(polygon[k + 1].x); result.Add(polygon[k + 1].y);
                        }
                    }
                }
                return result.ToArray();
            }

            // --------------------------------------------------------------
            // Рваный край. Поле читается со сдвигом по шуму (общему для всех
            // заливок, поэтому соседние типы местности по-прежнему сходятся
            // без щелей), а у границы — по мелкой подсетке TornSub × TornSub:
            // зубцы мельче шага сетки. Клетки, до которых граница со сдвигом
            // дотянуться не может, целиком внутри или снаружи.
            // --------------------------------------------------------------

            private const int TornSub = 3;
            private Dictionary<long, Vector2> tornWarp;

            private float TornPx => style.EdgeRaggedness * Mathf.Max(2f, style.RaggedScaleHexes * hexPx) * 0.45f;

            private float[] TornTriangles(float[] field, int gw, int gh)
            {
                if (TornPx < 0.05f)
                    return Triangles(field, gw, gh);
                byte[] state = TornCellStates(field, gw, gh);
                int gwF = (gw - 1) * TornSub + 1, ghF = (gh - 1) * TornSub + 1;
                float[] values = new float[(TornSub + 1) * (TornSub + 1)];
                List<float> result = new List<float>();
                List<Vector2> polygon = new List<Vector2>(8);
                float[] corner = new float[4];
                Vector2[] corners = new Vector2[4];
                for (int j = 0; j < gh - 1; j++)
                {
                    // Клетки целиком внутри идут полосой: одна полоса — два
                    // треугольника.
                    int run = -1;
                    void Flush(int end)
                    {
                        if (run < 0)
                            return;
                        Vector2 p0 = NodePoint(run, j), p1 = NodePoint(end, j), p2 = NodePoint(end, j + 1), p3 = NodePoint(run, j + 1);
                        AddTriangle(result, p0, p1, p2);
                        AddTriangle(result, p0, p2, p3);
                        run = -1;
                    }
                    for (int i = 0; i < gw - 1; i++)
                    {
                        byte cell = state[(j + 1) * (gw + 1) + i + 1];
                        if (cell == 1)
                        {
                            // Граница могла и не пройти через эту клетку.
                            TornValues(field, gw, gh, gwF, ghF, i, j, values);
                            int positive = 0;
                            foreach (float value in values)
                                positive += value > 0f ? 1 : 0;
                            cell = positive == 0 ? (byte)0 : positive == values.Length ? (byte)2 : (byte)1;
                        }
                        if (cell == 2)
                        {
                            if (run < 0)
                                run = i;
                            continue;
                        }
                        Flush(i);
                        if (cell == 0)
                            continue;
                        for (int sj = 0; sj < TornSub; sj++)
                        {
                            for (int si = 0; si < TornSub; si++)
                            {
                                int fi = i * TornSub + si, fj = j * TornSub + sj;
                                corner[0] = values[sj * (TornSub + 1) + si];
                                corner[1] = values[sj * (TornSub + 1) + si + 1];
                                corner[2] = values[(sj + 1) * (TornSub + 1) + si + 1];
                                corner[3] = values[(sj + 1) * (TornSub + 1) + si];
                                if (corner[0] <= 0f && corner[1] <= 0f && corner[2] <= 0f && corner[3] <= 0f)
                                    continue;
                                corners[0] = FineNodePoint(fi, fj);
                                corners[1] = FineNodePoint(fi + 1, fj);
                                corners[2] = FineNodePoint(fi + 1, fj + 1);
                                corners[3] = FineNodePoint(fi, fj + 1);
                                polygon.Clear();
                                for (int k = 0; k < 4; k++)
                                {
                                    float v0 = corner[k], v1 = corner[(k + 1) % 4];
                                    if (v0 > 0f)
                                        polygon.Add(corners[k]);
                                    if ((v0 > 0f) != (v1 > 0f))
                                        polygon.Add(Vector2.Lerp(corners[k], corners[(k + 1) % 4], Mathf.Clamp01(v0 / (v0 - v1))));
                                }
                                for (int k = 1; k < polygon.Count - 1; k++)
                                    AddTriangle(result, polygon[0], polygon[k], polygon[k + 1]);
                            }
                        }
                    }
                    Flush(gw - 1);
                }
                return result.ToArray();
            }

            // Состояние клеток грубой сетки (с внешним кольцом -1..gw-1):
            // 0 — снаружи, 2 — внутри, 1 — здесь может пройти рваный край.
            private byte[] TornCellStates(float[] field, int gw, int gh)
            {
                int reach = Mathf.CeilToInt(TornPx * 1.42f / VectorStep);
                int pad = reach + 1;
                int ew = gw + pad * 2, eh = gh + pad * 2;
                int[] sum = new int[(ew + 1) * (eh + 1)];
                for (int y = 0; y < eh; y++)
                {
                    int row = 0;
                    for (int x = 0; x < ew; x++)
                    {
                        row += FieldAt(field, gw, gh, x - pad, y - pad) > 0f ? 1 : 0;
                        sum[(y + 1) * (ew + 1) + x + 1] = sum[y * (ew + 1) + x + 1] + row;
                    }
                }
                int Count(int x0, int y0, int x1, int y1)
                {
                    x0 += pad; y0 += pad; x1 += pad + 1; y1 += pad + 1;
                    return sum[y1 * (ew + 1) + x1] - sum[y0 * (ew + 1) + x1] - sum[y1 * (ew + 1) + x0] + sum[y0 * (ew + 1) + x0];
                }
                byte[] state = new byte[(gw + 1) * (gh + 1)];
                int window = (reach * 2 + 2) * (reach * 2 + 2);
                for (int j = -1; j < gh; j++)
                {
                    for (int i = -1; i < gw; i++)
                    {
                        int inside = Count(i - reach, j - reach, i + 1 + reach, j + 1 + reach);
                        state[(j + 1) * (gw + 1) + i + 1] = (byte)(inside == 0 ? 0 : inside == window ? 2 : 1);
                    }
                }
                return state;
            }

            // Значения поля в узлах подсетки одной грубой клетки.
            private void TornValues(float[] field, int gw, int gh, int gwF, int ghF, int i, int j, float[] values)
            {
                float sub = VectorStep / TornSub;
                for (int sj = 0; sj <= TornSub; sj++)
                {
                    for (int si = 0; si <= TornSub; si++)
                    {
                        int fi = i * TornSub + si, fj = j * TornSub + sj;
                        float value = -1f;
                        if (fi >= 0 && fj >= 0 && fi < gwF && fj < ghF)
                        {
                            Vector2 warp = TornWarp(fi, fj, gwF);
                            value = SampleField(field, gw, gh, fi * sub + warp.x, fj * sub + warp.y);
                        }
                        values[sj * (TornSub + 1) + si] = value;
                    }
                }
            }

            private Vector2 TornWarp(int fi, int fj, int gwF)
            {
                tornWarp ??= new Dictionary<long, Vector2>();
                long key = (long)fj * (gwF + 1) + fi;
                if (tornWarp.TryGetValue(key, out Vector2 warp))
                    return warp;
                float sub = VectorStep / TornSub;
                double frequency = 1.0 / Mathf.Max(2f, style.RaggedScaleHexes * hexPx);
                double x = fi * sub * frequency, y = fj * sub * frequency;
                float px = TornPx;
                warp = new Vector2(
                    Mathf.Clamp((float)noise.Fbm(x + 13.1, y - 7.7, 3, 0.62) * 2f, -1f, 1f) * px,
                    Mathf.Clamp((float)noise.Fbm(x - 51.3, y + 29.9, 3, 0.62) * 2f, -1f, 1f) * px);
                tornWarp[key] = warp;
                return warp;
            }

            private float SampleField(float[] field, int gw, int gh, float x, float y)
            {
                float fx = Mathf.Clamp(x / VectorStep, 0f, gw - 1.0001f), fy = Mathf.Clamp(y / VectorStep, 0f, gh - 1.0001f);
                int i = (int)fx, j = (int)fy;
                float tx = fx - i, ty = fy - j;
                float top = Mathf.Lerp(field[j * gw + i], field[j * gw + i + 1], tx);
                float bottom = Mathf.Lerp(field[(j + 1) * gw + i], field[(j + 1) * gw + i + 1], tx);
                return Mathf.Lerp(top, bottom, ty);
            }

            private Vector2 FineNodePoint(int fi, int fj)
            {
                float sub = VectorStep / TornSub;
                return new Vector2(Mathf.Clamp(fi * sub, 0f, width), Mathf.Clamp(fj * sub, 0f, height));
            }

            private static void AddTriangle(List<float> result, Vector2 a, Vector2 b, Vector2 c)
            {
                result.Add(a.x); result.Add(a.y);
                result.Add(b.x); result.Add(b.y);
                result.Add(c.x); result.Add(c.y);
            }

            // Сглаживание Чайкина для замкнутой линии туши: без углов сетки.
            private static List<Vector2> Smooth(Vector2[] contour)
            {
                List<Vector2> points = new List<Vector2>(contour);
                for (int pass = 0; pass < 2 && points.Count >= 3; pass++)
                {
                    List<Vector2> next = new List<Vector2>(points.Count * 2);
                    for (int i = 0; i < points.Count; i++)
                    {
                        Vector2 a = points[i], b = points[(i + 1) % points.Count];
                        next.Add(Vector2.Lerp(a, b, 0.25f));
                        next.Add(Vector2.Lerp(a, b, 0.75f));
                    }
                    points = next;
                }
                return points;
            }

            private static float[] Blur(float[] field, int gw, int gh)
            {
                float[] result = new float[field.Length];
                for (int j = 0; j < gh; j++)
                {
                    for (int i = 0; i < gw; i++)
                    {
                        float sum = 0f;
                        int n = 0;
                        for (int y = Mathf.Max(0, j - 1); y <= Mathf.Min(gh - 1, j + 1); y++)
                        {
                            for (int x = Mathf.Max(0, i - 1); x <= Mathf.Min(gw - 1, i + 1); x++)
                            {
                                sum += field[y * gw + x];
                                n++;
                            }
                        }
                        result[j * gw + i] = sum / n;
                    }
                }
                return result;
            }

            // Дуглас — Пекер для замкнутого контура.
            private static Vector2[] Simplify(List<Vector2> contour, float tolerance)
            {
                if (contour.Count < 8)
                    return contour.ToArray();
                bool[] keep = new bool[contour.Count];
                int far = 0;
                float farDistance = 0f;
                for (int i = 1; i < contour.Count; i++)
                {
                    float d = (contour[i] - contour[0]).sqrMagnitude;
                    if (d > farDistance) { farDistance = d; far = i; }
                }
                keep[0] = keep[far] = true;
                Stack<(int, int)> stack = new Stack<(int, int)>();
                stack.Push((0, far));
                stack.Push((far, contour.Count));
                while (stack.Count > 0)
                {
                    (int from, int to) = stack.Pop();
                    Vector2 a = contour[from], b = contour[to % contour.Count];
                    float worst = 0f;
                    int index = -1;
                    for (int i = from + 1; i < to; i++)
                    {
                        float d = DistanceToSegment(contour[i], a, b, out _);
                        if (d > worst) { worst = d; index = i; }
                    }
                    if (index >= 0 && worst > tolerance)
                    {
                        keep[index] = true;
                        stack.Push((from, index));
                        stack.Push((index, to));
                    }
                }
                List<Vector2> result = new List<Vector2>();
                for (int i = 0; i < contour.Count; i++)
                {
                    if (keep[i])
                        result.Add(contour[i]);
                }
                return result.ToArray();
            }

            private static float[] Filled(int count, float value)
            {
                float[] array = new float[count];
                for (int i = 0; i < count; i++)
                    array[i] = value;
                return array;
            }

            private Color InkColor(float alpha)
            {
                Color ink = Grade(style.InkColor);
                return new Color(ink.r, ink.g, ink.b, Mathf.Clamp01(alpha));
            }

            private void AddVector(string name, List<WorldMapVectorShape> shapes)
            {
                if (shapes == null || shapes.Count == 0)
                    return;
                layerList.Add(new WorldMapTextureLayer { Name = name, Shapes = shapes, SourceWidth = width, SourceHeight = height });
            }

            private void AddFill(string name, Color color, float[] triangles, Color32[] colors)
            {
                if (triangles == null || triangles.Length == 0)
                    return;
                layerList.Add(new WorldMapTextureLayer { Name = name, Triangles = triangles, Tint = color, TriangleColors = colors, SourceWidth = width, SourceHeight = height });
            }

            private void AddMasked(string name, Texture2D texture, float tilePx, float alpha, float[] triangles)
            {
                if (triangles == null || triangles.Length == 0)
                {
                    if (Application.isPlaying)
                        UnityEngine.Object.Destroy(texture);
                    else
                        UnityEngine.Object.DestroyImmediate(texture);
                    return;
                }
                float tile = Mathf.Max(2f, tilePx);
                layerList.Add(new WorldMapTextureLayer
                {
                    Name = name,
                    Texture = texture,
                    Tiled = true,
                    TileWidthPercent = tile / width * 100f,
                    TileHeightPercent = tile / height * 100f,
                    UniformAlpha = Mathf.Clamp01(alpha),
                    Triangles = triangles,
                    SourceWidth = width,
                    SourceHeight = height
                });
            }
        }
    }
}
