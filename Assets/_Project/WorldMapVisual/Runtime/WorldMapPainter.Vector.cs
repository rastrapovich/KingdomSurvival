using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual
{
    // Векторная часть рисунка карты (слои видеокартой): заливки местности и
    // воды и тушь мазками кисти. Всё — контуры и треугольники в пикселях
    // исходной карты; видеокарта рисует их чётко при любом приближении.
    // Область заливки — поле (> 0 внутри), прочитанное со сдвигом по шуму:
    // зубцы рваного края или дрожь руки. Где край обведён тушью, заливка и
    // линия строятся по одному и тому же краю — заливка обрывается ровно
    // по линии туши. Между остальными заливками — переход по выбору
    // (встык, языками, пятнами, штрихами, размывкой).
    public static partial class WorldMapPainter
    {
        private sealed partial class PaintJob
        {
            // Шаг сетки контуров, в пикселях рисунка.
            private float VectorStep => Mathf.Max(2f, hexPx / 10f);

            // Поле «здесь лес» (> 0 внутри) — по нему же рисуется кромка леса.
            private float[] forestField;
            private WorldMapInkBrush inkBrush;
            private WorldMapInkBrush Brush => inkBrush ??= new WorldMapInkBrush(noise, hexPx);
            private WorldMapInkStyle Ink => style.Ink;

            private int GridWidth => Mathf.CeilToInt(width / VectorStep) + 1;
            private int GridHeight => Mathf.CeilToInt(height / VectorStep) + 1;

            private bool ForestInked => style.DrawInk && style.DrawForestOutline && inkPx > 0f && Ink.ForestEdge.Enabled && Ink.ForestEdge.Opacity > 0f;
            private bool ShoreInked => style.DrawInk && style.DrawShoreInk && inkPx > 0f && Ink.Shore.Enabled && Ink.Shore.Opacity > 0f;

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

                int forest = (int)WorldMapGameplayTerrainType.Forest;
                // Лучшая и вторая оценка в узле; без исключённого типа —
                // разбиение остальных типов, будто его нет.
                float[] best = null, second = null;
                int[] bestType = null;
                void Rank(int excluded)
                {
                    best = Filled(nodes, float.MinValue);
                    second = Filled(nodes, float.MinValue);
                    bestType = new int[nodes];
                    for (int t = 0; t < typeCount; t++)
                    {
                        if (score[t] == null || t == excluded)
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

                Rank(-1);
                if (score[forest] != null)
                    forestField = Field(forest);

                if (!style.DrawTerrain)
                    return;

                // Лес, обведённый тушью: его край — линия туши (со своей
                // дрожью руки), остальные заливки обрываются по ней же.
                EdgeWarp forestWarp = null;
                bool cutByForest = forestField != null && ForestInked && Ink.FillsFollowInk;
                if (cutByForest)
                {
                    forestWarp = HandWarp(Ink.ForestEdge, 501.0);
                    Rank(forest);
                }
                EdgeWarp torn = TornWarp();

                foreach (WorldMapGameplayTerrainType type in WorldMapTerrainLabels.All)
                {
                    int t = (int)type;
                    if (score[t] == null || type == WorldMapGameplayTerrainType.Water)
                        continue;
                    WorldMapTerrainArt art = style.FindTerrain(type);
                    if (art == null || !art.Enabled)
                        continue;
                    bool fill = art.Opacity > 0f;
                    Image pattern = style.UseTerrainPatterns && art.Pattern != null && art.PatternStrength > 0f ? LoadImage(art.Pattern) : null;
                    if (!fill && pattern == null)
                        continue;

                    EdgeTerm[] terms;
                    float[] alpha = null;
                    if (cutByForest && t == forest)
                        terms = new[] { new EdgeTerm(forestField, forestWarp, false) };
                    else
                    {
                        float[] field = t == forest ? forestField : Field(t);
                        if (field == null)
                            continue;
                        field = BlendField(field, t, out alpha);
                        terms = cutByForest
                            ? new[] { new EdgeTerm(field, torn, false), new EdgeTerm(forestField, forestWarp, true) }
                            : new[] { new EdgeTerm(field, torn, false) };
                    }

                    float[] triangles = RegionTriangles(terms, alpha, out float[] vertexAlpha);
                    if (fill)
                    {
                        Color color = Grade(art.Color);
                        AddFill("Местность: " + WorldMapTerrainLabels.Name(type), new Color(color.r, color.g, color.b, art.Opacity), triangles, null, vertexAlpha);
                    }
                    if (pattern != null)
                        AddMasked("Узор: " + WorldMapTerrainLabels.Name(type), GradedCopy(pattern, Color.white), art.PatternTileHexes * hexPx, art.PatternStrength, triangles, vertexAlpha);
                }
            }

            // --------------------------------------------------------------
            // Переход между заливками. Заливка заходит на соседние на
            // ширину перехода там, где разрешает узор (языки, пятна,
            // штрихи); при мягкости — тает к краю захода. Сама область
            // типа остаётся целиком — щелей нет, заходы ложатся поверх.
            // --------------------------------------------------------------

            private float[] BlendField(float[] field, int salt, out float[] alpha)
            {
                alpha = null;
                float reachPx = style.EdgeBlendWidthHexes * hexPx;
                if (style.EdgeBlend == WorldMapEdgeBlend.Torn || reachPx <= 0.5f)
                    return field;
                float step = VectorStep;
                int gw = GridWidth, gh = GridHeight, nodes = gw * gh;
                float[] outside = DistanceOutside(field, gw, gh);
                double frequency = 1.0 / Mathf.Max(2f, style.EdgeBlendScaleHexes * hexPx);
                float density = style.EdgeBlendDensity;
                float threshold = 0.22f - density * 0.5f;
                float angle = style.EdgeBlendAngle * Mathf.Deg2Rad;
                float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                float softness = style.EdgeBlend == WorldMapEdgeBlend.Wash ? Mathf.Max(0.85f, style.EdgeBlendSoftness) : style.EdgeBlendSoftness;
                float[] result = new float[nodes];
                if (softness > 0f)
                    alpha = new float[nodes];
                double sx = salt * 13.1, sy = -salt * 7.3;
                for (int j = 0; j < gh; j++)
                {
                    for (int i = 0; i < gw; i++)
                    {
                        int n = j * gw + i;
                        float inside = field[n] * step;
                        float reach = 0f;
                        if (outside[n] < reachPx * 1.05f)
                        {
                            double x = i * step, y = j * step;
                            switch (style.EdgeBlend)
                            {
                                case WorldMapEdgeBlend.Tongues:
                                    reach = Mathf.Clamp01((float)noise.Fbm(x * frequency + sx, y * frequency + sy, 3, 0.55) * 2.2f + density - 0.3f);
                                    break;
                                case WorldMapEdgeBlend.Spots:
                                    reach = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(threshold - 0.05f, threshold + 0.05f, (float)noise.Fbm(x * frequency * 1.4 + sx, y * frequency * 1.4 + sy, 3, 0.5)));
                                    break;
                                case WorldMapEdgeBlend.Streaks:
                                    double u = x * cos + y * sin, v = -x * sin + y * cos;
                                    reach = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(threshold - 0.05f, threshold + 0.05f, (float)noise.Fbm(u * frequency * 0.22 + sx, v * frequency * 2.4 + sy, 3, 0.5)));
                                    break;
                                default:
                                    reach = 0.55f + 0.45f * Mathf.Clamp((float)noise.Fbm(x * frequency + sx, y * frequency + sy, 3, 0.55) * 2f, -1f, 1f);
                                    break;
                            }
                            reach *= reachPx;
                        }
                        result[n] = Mathf.Max(inside, reach - outside[n]);
                        if (alpha != null)
                            alpha[n] = field[n] > 0f ? 1f : reach <= 0f ? 0f : Mathf.Clamp01((reach - outside[n]) / (softness * reach));
                    }
                }
                return result;
            }

            // Расстояние от узла до области типа, в пикселях (0 внутри):
            // два прохода по сетке (фаска 1 и √2 шага).
            private float[] DistanceOutside(float[] field, int gw, int gh)
            {
                float step = VectorStep, diagonal = step * 1.4142f;
                float[] d = new float[gw * gh];
                for (int n = 0; n < d.Length; n++)
                    d[n] = field[n] > 0f ? 0f : float.MaxValue / 4f;
                for (int j = 0; j < gh; j++)
                {
                    for (int i = 0; i < gw; i++)
                    {
                        int n = j * gw + i;
                        float v = d[n];
                        if (i > 0) v = Mathf.Min(v, d[n - 1] + step);
                        if (j > 0)
                        {
                            v = Mathf.Min(v, d[n - gw] + step);
                            if (i > 0) v = Mathf.Min(v, d[n - gw - 1] + diagonal);
                            if (i < gw - 1) v = Mathf.Min(v, d[n - gw + 1] + diagonal);
                        }
                        d[n] = v;
                    }
                }
                for (int j = gh - 1; j >= 0; j--)
                {
                    for (int i = gw - 1; i >= 0; i--)
                    {
                        int n = j * gw + i;
                        float v = d[n];
                        if (i < gw - 1) v = Mathf.Min(v, d[n + 1] + step);
                        if (j < gh - 1)
                        {
                            v = Mathf.Min(v, d[n + gw] + step);
                            if (i < gw - 1) v = Mathf.Min(v, d[n + gw + 1] + diagonal);
                            if (i > 0) v = Mathf.Min(v, d[n + gw - 1] + diagonal);
                        }
                        d[n] = v;
                    }
                }
                return d;
            }

            // --------------------------------------------------------------
            // Вода: заливка ступенями глубины (отмель → мелко → глубже →
            // глубоко), рябь в границах воды, тушь берега и линии воды
            // вдоль берега.
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
                float[] Inside(float threshold)
                {
                    float[] field = new float[nodes];
                    for (int n = 0; n < nodes; n++)
                        field[n] = (depth[n] - threshold) / step;
                    return field;
                }

                // Берег, обведённый тушью: край воды и линия — один край.
                bool follow = ShoreInked && Ink.FillsFollowInk;
                EdgeWarp torn = TornWarp();
                EdgeTerm water = new EdgeTerm(shore, follow ? HandWarp(Ink.Shore, 1.0) : torn, false);

                Color shallow = style.WaterShallowColor;
                Color deep = style.WaterDeepColor;
                Color shoreColor = new Color(Mathf.Clamp01(shallow.r * 1.25f), Mathf.Clamp01(shallow.g * 1.25f), Mathf.Clamp01(shallow.b * 1.2f), 1f);
                float shorePx = Mathf.Max(1f, style.ShoreBand * hexPx);
                float deepPx = Mathf.Max(2f, hexPx * 0.9f);

                // Ступени — треугольниками по порядку: каждая следующая
                // поверх предыдущей и не выходит за край воды.
                List<float> fills = new List<float>();
                List<Color32> colors = new List<Color32>();
                float[] Band(EdgeTerm[] terms, Color color)
                {
                    float[] triangles = RegionTriangles(terms, null, out _);
                    Color graded = Grade(color);
                    Color32 opaque = new Color(graded.r, graded.g, graded.b, 1f);
                    fills.AddRange(triangles);
                    for (int t = 0; t < triangles.Length / 6; t++)
                        colors.Add(opaque);
                    return triangles;
                }

                float[] surface = Band(new[] { water }, style.DrawShoreBand ? shoreColor : shallow);
                if (style.DrawShoreBand)
                    Band(new[] { water, new EdgeTerm(Inside(shorePx), torn, false) }, shallow);
                Band(new[] { water, new EdgeTerm(Inside(deepPx * 0.45f), torn, false) }, Color.Lerp(shallow, deep, 0.5f));
                Band(new[] { water, new EdgeTerm(Inside(deepPx), torn, false) }, deep);
                if (fills.Count == 0)
                    return;
                AddFill("Вода", Color.white, fills.ToArray(), colors.ToArray(), null);

                if (style.UseWaterPattern && style.WaterPattern != null && style.WaterPatternStrength > 0f)
                {
                    Image ripple = LoadImage(style.WaterPattern);
                    if (ripple != null)
                        AddMasked("Рябь", GradedCopy(ripple, Color.white), style.WaterPatternTileHexes * hexPx, style.WaterPatternStrength, surface, null);
                }

                shoreField = shore;
                depthField = depth;
                shoreWarp = water.Warp;
                BuildShoreInk();
            }

            // Поля воды для туши берега и линий воды — хранятся, чтобы
            // правка туши перерисовывала только линии (RebuildInk).
            private float[] shoreField;
            private float[] depthField;
            private EdgeWarp shoreWarp;

            private float[] DepthInside(float threshold)
            {
                float step = VectorStep;
                float[] field = new float[depthField.Length];
                for (int n = 0; n < field.Length; n++)
                    field[n] = (depthField[n] - threshold) / step;
                return field;
            }

            private void BuildShoreInk()
            {
                if (shoreField == null)
                    return;
                EdgeTerm water = new EdgeTerm(shoreField, shoreWarp, false);
                if (style.DrawInk && style.DrawShoreInk && inkPx > 0f && Ink.ShoreEchoCount > 0 && Ink.ShoreEcho.Enabled)
                {
                    // Линии воды вдоль берега, как на старых картах.
                    List<WorldMapVectorShape> echoes = new List<WorldMapVectorShape>();
                    EdgeWarp still = new EdgeWarp();
                    int seed = 201;
                    for (int k = 1; k <= Ink.ShoreEchoCount; k++)
                    {
                        EdgeTerm[] line = { water, new EdgeTerm(DepthInside(k * Ink.ShoreEchoSpacingHexes * hexPx), still, false) };
                        foreach (Vector2[] contour in RegionContours(line))
                            Brush.Stroke(echoes, contour, true, Ink.ShoreEcho, inkPx, InkColor(1f), seed++);
                    }
                    AddVector("Линии воды у берега", echoes);
                }

                if (ShoreInked)
                {
                    List<WorldMapVectorShape> ink = new List<WorldMapVectorShape>();
                    int seed = 1;
                    if (Ink.FillsFollowInk)
                    {
                        foreach (Vector2[] contour in RegionContours(new[] { water }))
                            Brush.Stroke(ink, contour, true, Ink.Shore, inkPx, InkColor(1f), seed++, false);
                    }
                    else
                    {
                        foreach (Vector2[] contour in Contours(shoreField, GridWidth, GridHeight))
                            Brush.Stroke(ink, Smooth(contour), true, Ink.Shore, inkPx, InkColor(1f), seed++);
                    }
                    AddVector("Тушь берега", ink);
                }
            }

            private void BuildVectorForestInk()
            {
                if (forestField == null || !ForestInked)
                    return;
                List<WorldMapVectorShape> ink = new List<WorldMapVectorShape>();
                int seed = 501;
                if (Ink.FillsFollowInk)
                {
                    foreach (Vector2[] contour in RegionContours(new[] { new EdgeTerm(forestField, HandWarp(Ink.ForestEdge, 501.0), false) }))
                        Brush.Stroke(ink, contour, true, Ink.ForestEdge, inkPx, InkColor(1f), seed++, false);
                }
                else
                {
                    foreach (Vector2[] contour in Contours(forestField, GridWidth, GridHeight))
                        Brush.Stroke(ink, Smooth(contour), true, Ink.ForestEdge, inkPx, InkColor(1f), seed++);
                }
                AddVector("Кромка леса", ink);
            }

            // --------------------------------------------------------------
            // Дороги, тропы, мосты. Дорога — полоса цвета дороги, её края
            // обведены мазками по самой кромке полосы: полоса ложится поверх
            // внутренней половины мазка, так что край дороги — линия туши,
            // а на перекрёстках полосы закрывают чужую обводку.
            // --------------------------------------------------------------

            private void BuildVectorRoads()
            {
                if (map.Roads == null)
                    return;
                float roadWidth = Mathf.Max(1.6f, style.RoadWidthHexes * hexPx);
                float trailWidth = Mathf.Max(1.2f, style.TrailWidthHexes * hexPx);
                WorldMapStrokeStyle edge = Ink.RoadEdge;
                bool edges = style.DrawInk && inkPx > 0f && edge.Enabled && edge.Opacity > 0f;
                List<WorldMapVectorShape> trails = new List<WorldMapVectorShape>();
                List<WorldMapVectorShape> ink = new List<WorldMapVectorShape>();
                List<WorldMapVectorShape> fill = new List<WorldMapVectorShape>();
                Color road = Grade(style.RoadColor);
                road.a = 1f;
                Color trailColor = Grade(style.TrailColor);
                trailColor.a = 1f;
                int seed = 1001;
                // Бриф правок п. 13: в русле дорогу не рисуем — её несёт мост.
                foreach ((string kind, List<Vector2> points) in RoadPartsOnLand())
                {
                    seed++;
                    if (kind == GeneratedMapLine.TrailKind)
                    {
                        if (!style.DrawTrails)
                            continue;
                        float dash = style.TrailDashed ? Mathf.Max(3f, trailWidth * 5f) : 0f;
                        foreach (List<Vector2> part in Dashes(points, dash, dash * 0.8f))
                            Brush.Stroke(trails, part, false, Ink.Trail, trailWidth, trailColor, seed);
                        continue;
                    }
                    if (!style.DrawRoads)
                        continue;

                    List<Vector2> center = WorldMapInkBrush.Resample(points, false, Mathf.Max(1f, hexPx * 0.1f));
                    if (edge.Enabled && edge.WobbleHexes > 0f)
                        center = Brush.Wobble(center, false, edge.WobbleHexes * hexPx, edge.WobbleWaveHexes * hexPx, seed);
                    if (center.Count < 2)
                        continue;
                    List<Vector2> left = new List<Vector2>(center.Count), right = new List<Vector2>(center.Count);
                    for (int i = 0; i < center.Count; i++)
                    {
                        Vector2 normal = WorldMapInkBrush.Normal(center, i, false);
                        left.Add(center[i] + normal * roadWidth * 0.5f);
                        right.Add(center[i] - normal * roadWidth * 0.5f);
                    }
                    if (edges)
                    {
                        Brush.Stroke(ink, left, false, edge, inkPx, InkColor(1f), seed * 2, false);
                        Brush.Stroke(ink, right, false, edge, inkPx, InkColor(1f), seed * 2 + 1, false);
                    }
                    float roadDash = style.RoadDashed ? roadWidth * 6f : 0f;
                    foreach (List<Vector2> part in Dashes(center, roadDash, roadDash * 0.6f))
                        AddRibbon(fill, part, roadWidth, road);
                }
                trails.AddRange(ink);
                trails.AddRange(fill);
                AddVector("Дороги и тропы", trails);
            }

            // Полоса постоянной ширины вдоль линии.
            private static void AddRibbon(List<WorldMapVectorShape> shapes, List<Vector2> line, float width, Color color)
            {
                if (line.Count < 2)
                    return;
                for (int from = 0; from < line.Count - 1; from += WorldMapInkBrush.MaxStrokePoints)
                {
                    int to = Mathf.Min(from + WorldMapInkBrush.MaxStrokePoints, line.Count - 1);
                    Vector2[] outline = new Vector2[(to - from + 1) * 2];
                    int n = to - from + 1;
                    for (int k = 0; k < n; k++)
                    {
                        Vector2 normal = WorldMapInkBrush.Normal(line, from + k, false);
                        outline[k] = line[from + k] + normal * width * 0.5f;
                        outline[n * 2 - 1 - k] = line[from + k] - normal * width * 0.5f;
                    }
                    WorldMapVectorShape shape = new WorldMapVectorShape { Color = color, OddEven = false };
                    shape.Contours.Add(outline);
                    shapes.Add(shape);
                }
            }

            private void BuildVectorBridges()
            {
                if (!style.DrawBridges || map.Bridges == null || map.Bridges.Count == 0)
                    return;
                float roadHalf = Mathf.Max(0.8f, style.RoadWidthHexes * hexPx * 0.5f);
                float length = Mathf.Max(roadHalf * 4f, hexPx * 0.7f);
                List<WorldMapVectorShape> shapes = new List<WorldMapVectorShape>();
                Color plank = Grade(style.BridgeColor);
                int seed = 3001;
                // Бриф правок п. 13: пролёт — от берега до берега поперёк
                // воды; концы совпадают с концами дороги на суше.
                if (map.BridgeSpans != null && map.BridgeSpans.Count >= 2)
                {
                    foreach ((Vector2 a, Vector2 c) in BridgeSpanPixels())
                    {
                        Vector2 along = (c - a).normalized;
                        Vector2 normal = new Vector2(-along.y, along.x);
                        float half = roadHalf * 1.4f;
                        WorldMapVectorShape deck = new WorldMapVectorShape { Color = new Color(plank.r, plank.g, plank.b, 1f), OddEven = false };
                        deck.Contours.Add(new[] { a + normal * half, c + normal * half, c - normal * half, a - normal * half });
                        shapes.Add(deck);
                        if (!style.DrawInk || inkPx <= 0f)
                            continue;
                        Brush.Stroke(shapes, new List<Vector2> { a + normal * half, c + normal * half }, false, Ink.Bridge, inkPx, InkColor(1f), seed++);
                        Brush.Stroke(shapes, new List<Vector2> { a - normal * half, c - normal * half }, false, Ink.Bridge, inkPx, InkColor(1f), seed++);
                    }
                    AddVector("Мосты", shapes);
                    return;
                }
                foreach (MapPointData bridge in map.Bridges)
                {
                    Vector2 center = ToPixel(bridge);
                    Vector2 direction = RoadDirectionAt(center);
                    Sprite sprite = Pick(style.Pool(style.BridgeSprites, style.UseBridgeSprites));
                    if (sprite != null)
                    {
                        if (inkTarget == null)
                            DrawSprite(sprite, center + new Vector2(0f, length * 0.25f), length * 0.6f, Color.white, direction.x < 0f);
                        continue;
                    }
                    Vector2 normal = new Vector2(-direction.y, direction.x);
                    float half = roadHalf * 1.5f;
                    Vector2 a = center - direction * length * 0.5f, c = center + direction * length * 0.5f;
                    WorldMapVectorShape deck = new WorldMapVectorShape { Color = new Color(plank.r, plank.g, plank.b, 1f), OddEven = false };
                    deck.Contours.Add(new[] { a + normal * half, c + normal * half, c - normal * half, a - normal * half });
                    shapes.Add(deck);
                    if (!style.DrawInk || inkPx <= 0f)
                        continue;
                    Brush.Stroke(shapes, new List<Vector2> { a + normal * half, c + normal * half }, false, Ink.Bridge, inkPx, InkColor(1f), seed++);
                    Brush.Stroke(shapes, new List<Vector2> { a - normal * half, c - normal * half }, false, Ink.Bridge, inkPx, InkColor(1f), seed++);
                }
                AddVector("Мосты", shapes);
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
            // Край области. Область — пересечение слагаемых: поле (> 0
            // внутри) или его дополнение, каждое прочитано со своим сдвигом
            // по шуму. У границы поле читается по мелкой подсетке
            // TornSub × TornSub; клетки, до которых граница со сдвигом
            // дотянуться не может, целиком внутри или снаружи. Треугольники
            // заливки и контур туши строятся по одним и тем же значениям —
            // край заливки лежит ровно на линии туши.
            // --------------------------------------------------------------

            private const int TornSub = 3;

            private sealed class EdgeWarp
            {
                public float Px;
                public double Frequency = 1.0;
                public int Octaves = 3;
                public double Salt;
                public readonly Dictionary<long, Vector2> Cache = new Dictionary<long, Vector2>();
            }

            private readonly struct EdgeTerm
            {
                public readonly float[] Field;
                public readonly EdgeWarp Warp;
                public readonly bool Negate;

                public EdgeTerm(float[] field, EdgeWarp warp, bool negate)
                {
                    Field = field;
                    Warp = warp ?? new EdgeWarp();
                    Negate = negate;
                }
            }

            private EdgeWarp tornWarp;

            // Зубцы рваного края — общие для всех заливок, поэтому соседние
            // типы местности сходятся без щелей.
            private EdgeWarp TornWarp() => tornWarp ??= new EdgeWarp
            {
                Px = style.EdgeRaggedness * Mathf.Max(2f, style.RaggedScaleHexes * hexPx) * 0.45f,
                Frequency = 1.0 / Mathf.Max(2f, style.RaggedScaleHexes * hexPx),
                Octaves = 3
            };

            // Дрожь руки у края, обведённого тушью: плавная, без зубцов.
            private EdgeWarp HandWarp(WorldMapStrokeStyle stroke, double salt) => new EdgeWarp
            {
                Px = stroke.WobbleHexes * hexPx,
                Frequency = 1.0 / Mathf.Max(2f, stroke.WobbleWaveHexes * hexPx),
                Octaves = 2,
                Salt = salt
            };

            private float[] RegionTriangles(EdgeTerm[] terms, float[] alphaField, out float[] vertexAlpha)
            {
                int gw = GridWidth, gh = GridHeight;
                byte[] state = RegionStates(terms, gw, gh);
                int gwF = (gw - 1) * TornSub + 1, ghF = (gh - 1) * TornSub + 1;
                int side = TornSub + 1;
                float[] values = new float[side * side];
                float[] alphas = alphaField != null ? new float[side * side] : null;
                List<float> result = new List<float>();
                List<float> resultAlpha = alphaField != null ? new List<float>() : null;
                List<Vector2> polygon = new List<Vector2>(8);
                List<float> polygonAlpha = new List<float>(8);
                float[] corner = new float[4], cornerAlpha = new float[4];
                Vector2[] corners = new Vector2[4];

                void Triangle(Vector2 a, Vector2 b, Vector2 c, float aa, float ab, float ac)
                {
                    AddTriangle(result, a, b, c);
                    if (resultAlpha == null)
                        return;
                    resultAlpha.Add(aa);
                    resultAlpha.Add(ab);
                    resultAlpha.Add(ac);
                }

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
                        Triangle(p0, p1, p2, 1f, 1f, 1f);
                        Triangle(p0, p2, p3, 1f, 1f, 1f);
                        run = -1;
                    }
                    for (int i = 0; i < gw - 1; i++)
                    {
                        byte cell = state[(j + 1) * (gw + 1) + i + 1];
                        if (cell == 1)
                        {
                            // Граница могла и не пройти через эту клетку.
                            RegionValues(terms, gw, gh, gwF, ghF, i, j, values);
                            int positive = 0;
                            foreach (float value in values)
                                positive += value > 0f ? 1 : 0;
                            cell = positive == 0 ? (byte)0 : positive == values.Length ? (byte)2 : (byte)1;
                        }
                        if (cell == 0)
                        {
                            Flush(i);
                            continue;
                        }
                        if (cell == 2)
                        {
                            bool solid = alphaField == null || (alphaField[j * gw + i] >= 0.999f && alphaField[j * gw + i + 1] >= 0.999f &&
                                alphaField[(j + 1) * gw + i] >= 0.999f && alphaField[(j + 1) * gw + i + 1] >= 0.999f);
                            if (solid)
                            {
                                if (run < 0)
                                    run = i;
                                continue;
                            }
                            Flush(i);
                            RegionAlpha(terms[0].Warp, alphaField, gw, gh, gwF, i, j, alphas);
                            Vector2 p0 = NodePoint(i, j), p1 = NodePoint(i + 1, j), p2 = NodePoint(i + 1, j + 1), p3 = NodePoint(i, j + 1);
                            float a0 = alphas[0], a1 = alphas[TornSub], a2 = alphas[side * side - 1], a3 = alphas[side * TornSub];
                            Triangle(p0, p1, p2, a0, a1, a2);
                            Triangle(p0, p2, p3, a0, a2, a3);
                            continue;
                        }
                        Flush(i);
                        if (alphas != null)
                            RegionAlpha(terms[0].Warp, alphaField, gw, gh, gwF, i, j, alphas);
                        for (int sj = 0; sj < TornSub; sj++)
                        {
                            for (int si = 0; si < TornSub; si++)
                            {
                                int fi = i * TornSub + si, fj = j * TornSub + sj;
                                int i0 = sj * side + si, i1 = i0 + 1, i2 = i0 + side + 1, i3 = i0 + side;
                                corner[0] = values[i0];
                                corner[1] = values[i1];
                                corner[2] = values[i2];
                                corner[3] = values[i3];
                                if (corner[0] <= 0f && corner[1] <= 0f && corner[2] <= 0f && corner[3] <= 0f)
                                    continue;
                                if (alphas != null)
                                {
                                    cornerAlpha[0] = alphas[i0];
                                    cornerAlpha[1] = alphas[i1];
                                    cornerAlpha[2] = alphas[i2];
                                    cornerAlpha[3] = alphas[i3];
                                }
                                corners[0] = FineNodePoint(fi, fj);
                                corners[1] = FineNodePoint(fi + 1, fj);
                                corners[2] = FineNodePoint(fi + 1, fj + 1);
                                corners[3] = FineNodePoint(fi, fj + 1);
                                polygon.Clear();
                                polygonAlpha.Clear();
                                for (int k = 0; k < 4; k++)
                                {
                                    float v0 = corner[k], v1 = corner[(k + 1) % 4];
                                    if (v0 > 0f)
                                    {
                                        polygon.Add(corners[k]);
                                        polygonAlpha.Add(alphas != null ? cornerAlpha[k] : 1f);
                                    }
                                    if ((v0 > 0f) != (v1 > 0f))
                                    {
                                        float t = Mathf.Clamp01(v0 / (v0 - v1));
                                        polygon.Add(Vector2.Lerp(corners[k], corners[(k + 1) % 4], t));
                                        polygonAlpha.Add(alphas != null ? Mathf.Lerp(cornerAlpha[k], cornerAlpha[(k + 1) % 4], t) : 1f);
                                    }
                                }
                                for (int k = 1; k < polygon.Count - 1; k++)
                                    Triangle(polygon[0], polygon[k], polygon[k + 1], polygonAlpha[0], polygonAlpha[k], polygonAlpha[k + 1]);
                            }
                        }
                    }
                    Flush(gw - 1);
                }
                vertexAlpha = resultAlpha?.ToArray();
                return result.ToArray();
            }

            // Контуры области по подсетке — линия туши по краю заливки.
            private List<Vector2[]> RegionContours(EdgeTerm[] terms)
            {
                int gw = GridWidth, gh = GridHeight;
                byte[] state = RegionStates(terms, gw, gh);
                int gwF = (gw - 1) * TornSub + 1, ghF = (gh - 1) * TornSub + 1;
                int side = TornSub + 1;
                float[] values = new float[side * side];
                int[] pairs = new int[4];
                Dictionary<long, Vector2> points = new Dictionary<long, Vector2>();
                Dictionary<long, List<long>> links = new Dictionary<long, List<long>>();
                void Point(long key, int fi, int fj, int edge, float a, float b, float c, float d)
                {
                    if (!points.ContainsKey(key))
                        points[key] = FineEdgePoint(fi, fj, edge, a, b, c, d);
                    if (!links.ContainsKey(key))
                        links[key] = new List<long>(2);
                }

                for (int j = -1; j < gh; j++)
                {
                    for (int i = -1; i < gw; i++)
                    {
                        if (state[(j + 1) * (gw + 1) + i + 1] != 1)
                            continue;
                        RegionValues(terms, gw, gh, gwF, ghF, i, j, values);
                        for (int sj = 0; sj < TornSub; sj++)
                        {
                            for (int si = 0; si < TornSub; si++)
                            {
                                int fi = i * TornSub + si, fj = j * TornSub + sj;
                                int i0 = sj * side + si;
                                float a = values[i0], b = values[i0 + 1], c = values[i0 + side + 1], d = values[i0 + side];
                                int count = CellSegments(a, b, c, d, pairs);
                                for (int s = 0; s < count; s++)
                                {
                                    long k0 = EdgeKey(gwF, fi, fj, pairs[s * 2]), k1 = EdgeKey(gwF, fi, fj, pairs[s * 2 + 1]);
                                    Point(k0, fi, fj, pairs[s * 2], a, b, c, d);
                                    Point(k1, fi, fj, pairs[s * 2 + 1], a, b, c, d);
                                    links[k0].Add(k1);
                                    links[k1].Add(k0);
                                }
                            }
                        }
                    }
                }

                List<Vector2[]> contours = new List<Vector2[]>();
                HashSet<long> visited = new HashSet<long>();
                List<Vector2> contour = new List<Vector2>();
                foreach (long startKey in links.Keys)
                {
                    if (visited.Contains(startKey))
                        continue;
                    contour.Clear();
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
                    Vector2[] simplified = Simplify(contour, VectorStep / TornSub * 0.3f);
                    if (simplified.Length >= 3)
                        contours.Add(simplified);
                }
                return contours;
            }

            // Состояние клеток грубой сетки (с внешним кольцом -1..gw-1):
            // 0 — снаружи, 2 — внутри, 1 — здесь может пройти край.
            private byte[] RegionStates(EdgeTerm[] terms, int gw, int gh)
            {
                byte[] result = null;
                foreach (EdgeTerm term in terms)
                {
                    byte[] states = CellStates(term.Field, gw, gh, term.Warp.Px);
                    if (term.Negate)
                    {
                        for (int n = 0; n < states.Length; n++)
                            states[n] = (byte)(2 - states[n]);
                    }
                    if (result == null)
                    {
                        result = states;
                        continue;
                    }
                    for (int n = 0; n < result.Length; n++)
                    {
                        byte a = result[n], b = states[n];
                        result[n] = (byte)(a == 0 || b == 0 ? 0 : a == 2 && b == 2 ? 2 : 1);
                    }
                }
                return result;
            }

            private byte[] CellStates(float[] field, int gw, int gh, float warpPx)
            {
                int reach = Mathf.CeilToInt(warpPx * 1.42f / VectorStep);
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

            // Значения области в узлах подсетки одной грубой клетки.
            private void RegionValues(EdgeTerm[] terms, int gw, int gh, int gwF, int ghF, int i, int j, float[] values)
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
                            value = float.MaxValue;
                            foreach (EdgeTerm term in terms)
                            {
                                Vector2 warp = Warp(term.Warp, fi, fj, gwF);
                                float v = SampleField(term.Field, gw, gh, fi * sub + warp.x, fj * sub + warp.y);
                                value = Mathf.Min(value, term.Negate ? -v : v);
                            }
                        }
                        values[sj * (TornSub + 1) + si] = value;
                    }
                }
            }

            private void RegionAlpha(EdgeWarp warp, float[] alphaField, int gw, int gh, int gwF, int i, int j, float[] alphas)
            {
                float sub = VectorStep / TornSub;
                for (int sj = 0; sj <= TornSub; sj++)
                {
                    for (int si = 0; si <= TornSub; si++)
                    {
                        int fi = i * TornSub + si, fj = j * TornSub + sj;
                        Vector2 offset = Warp(warp, fi, fj, gwF);
                        alphas[sj * (TornSub + 1) + si] = Mathf.Clamp01(SampleField(alphaField, gw, gh, fi * sub + offset.x, fj * sub + offset.y));
                    }
                }
            }

            private Vector2 Warp(EdgeWarp warp, int fi, int fj, int gwF)
            {
                if (warp.Px <= 0f)
                    return Vector2.zero;
                long key = (long)fj * (gwF + 1) + fi;
                if (warp.Cache.TryGetValue(key, out Vector2 offset))
                    return offset;
                float sub = VectorStep / TornSub;
                double x = fi * sub * warp.Frequency, y = fj * sub * warp.Frequency;
                offset = new Vector2(
                    Mathf.Clamp((float)noise.Fbm(x + 13.1 + warp.Salt, y - 7.7, warp.Octaves, 0.62) * 2f, -1f, 1f) * warp.Px,
                    Mathf.Clamp((float)noise.Fbm(x - 51.3, y + 29.9 + warp.Salt, warp.Octaves, 0.62) * 2f, -1f, 1f) * warp.Px);
                warp.Cache[key] = offset;
                return offset;
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

            private Vector2 FineEdgePoint(int fi, int fj, int edge, float a, float b, float c, float d)
            {
                Vector2 pa, pb;
                float va, vb;
                switch (edge)
                {
                    case 0: pa = FineNodePoint(fi, fj); pb = FineNodePoint(fi + 1, fj); va = a; vb = b; break;
                    case 1: pa = FineNodePoint(fi + 1, fj); pb = FineNodePoint(fi + 1, fj + 1); va = b; vb = c; break;
                    case 2: pa = FineNodePoint(fi + 1, fj + 1); pb = FineNodePoint(fi, fj + 1); va = c; vb = d; break;
                    default: pa = FineNodePoint(fi, fj); pb = FineNodePoint(fi, fj + 1); va = a; vb = d; break;
                }
                float t = Mathf.Abs(va - vb) < 1e-6f ? 0.5f : Mathf.Clamp01(va / (va - vb));
                return Vector2.Lerp(pa, pb, t);
            }

            // Отрезки границы в клетке: пары рёбер (0 — верх, 1 — право,
            // 2 — низ, 3 — лево); седло решает среднее по углам.
            private static int CellSegments(float a, float b, float c, float d, int[] pairs)
            {
                int index = (a > 0f ? 1 : 0) | (b > 0f ? 2 : 0) | (c > 0f ? 4 : 0) | (d > 0f ? 8 : 0);
                if (index == 0 || index == 15)
                    return 0;
                if (index == 5 || index == 10)
                {
                    bool centerInside = (a + b + c + d) * 0.25f > 0f;
                    if ((index == 5) == !centerInside)
                    {
                        pairs[0] = 3; pairs[1] = 0; pairs[2] = 1; pairs[3] = 2;
                    }
                    else
                    {
                        pairs[0] = 0; pairs[1] = 1; pairs[2] = 2; pairs[3] = 3;
                    }
                    return 2;
                }
                int[] segment = SegmentTable[index];
                pairs[0] = segment[0];
                pairs[1] = segment[1];
                return 1;
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
                if (inkTarget != null)
                {
                    WorldMapTextureLayer existing = inkTarget.Find(layer => layer.Name == name);
                    if (existing != null)
                    {
                        existing.Shapes = shapes;
                        return;
                    }
                    // Слоя ещё не было (например, включили линии воды) —
                    // встаёт на своё место среди слоёв туши.
                    int order = Array.IndexOf(InkLayerNames, name);
                    int at = inkTarget.FindIndex(layer => Array.IndexOf(InkLayerNames, layer.Name) > order || Array.IndexOf(UpperLayerNames, layer.Name) >= 0);
                    inkTarget.Insert(at < 0 ? inkTarget.Count : at, new WorldMapTextureLayer { Name = name, Shapes = shapes, SourceWidth = width, SourceHeight = height });
                    inkStructureChanged = true;
                    return;
                }
                layerList.Add(new WorldMapTextureLayer { Name = name, Shapes = shapes, SourceWidth = width, SourceHeight = height });
            }

            // --------------------------------------------------------------
            // Перерисовка одной туши: правка линии, не трогающая край
            // заливок, перестраивает только слои линий, дорог и мостов —
            // заливки, узоры и знаки остаются как были.
            // --------------------------------------------------------------

            private static readonly string[] InkLayerNames = { "Линии воды у берега", "Тушь берега", "Кромка леса", "Дороги и тропы", "Мосты" };
            private static readonly string[] UpperLayerNames = { "Знаки кодом", "Бумага", "Виньетка и зерно" };
            private List<WorldMapTextureLayer> inkTarget;
            private bool inkStructureChanged;

            public void RebuildInk(List<WorldMapTextureLayer> target, out bool structureChanged)
            {
                inkPx = Mathf.Max(0f, style.InkWidth * scale);
                foreach (WorldMapTextureLayer layer in target)
                {
                    if (Array.IndexOf(InkLayerNames, layer.Name) >= 0)
                        layer.Shapes = new List<WorldMapVectorShape>();
                }
                inkTarget = target;
                inkStructureChanged = false;
                try
                {
                    BuildShoreInk();
                    BuildVectorForestInk();
                    BuildVectorRoads();
                    BuildVectorBridges();
                }
                finally
                {
                    inkTarget = null;
                }
                structureChanged = inkStructureChanged;
            }

            // После рисунка большие холсты не нужны — для перерисовки туши
            // хватает полей сетки.
            public void ReleaseCanvas()
            {
                r = g = b = null;
                topAlpha = null;
                area = null;
                waterCoverage = null;
                tornWarp?.Cache.Clear();
                shoreWarp?.Cache.Clear();
            }

            private void AddFill(string name, Color color, float[] triangles, Color32[] colors, float[] vertexAlpha)
            {
                if (triangles == null || triangles.Length == 0)
                    return;
                layerList.Add(new WorldMapTextureLayer { Name = name, Triangles = triangles, Tint = color, TriangleColors = colors, VertexAlpha = vertexAlpha, SourceWidth = width, SourceHeight = height });
            }

            private void AddMasked(string name, Texture2D texture, float tilePx, float alpha, float[] triangles, float[] vertexAlpha)
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
                    VertexAlpha = vertexAlpha,
                    SourceWidth = width,
                    SourceHeight = height
                });
            }
        }
    }
}
