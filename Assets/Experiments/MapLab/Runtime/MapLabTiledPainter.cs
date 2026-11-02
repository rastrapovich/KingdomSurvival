using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Покраска этапа 03: те же материалы и мазки, что в MapLabPainter, но
    //  • запекание по тайлам (по чанкам рельефа) с заданной плотностью
    //    текселей на единицу мира — без растяжения одной текстуры 2048;
    //  • мазки описаны в мире: каждая ячейка StrokeCell получает свои следы от
    //    seed и координат ячейки, поэтому рисунок не зависит от плотности
    //    запекания и одинаков на стыке тайлов;
    //  • профиль следа — плотная основная часть, узкий мягкий край в текселях,
    //    слабые продольные «щетинки»; позиции — по дрожащей сетке (без куч);
    //  • кромки дороги, поля и межи — чёткие; тушь дороги рисует MapLabInk.
    // Результат тайла: RGB — краска (sRGB), A — сила тонкой линии.
    public static class MapLabTiledPainter
    {
        private const int KeyGrass = 1;
        private const int KeyForest = 2;
        private const int KeyRoad = 3;
        private const int KeyRock = 4;
        private const int KeyShore = 5;
        private const int KeyYard = 6;
        private const int KeyFieldBase = 16;
        public const int Gutter = 2;

        public sealed class Tile
        {
            public int X, Z;
            public Vector2 Origin;      // мир: угол ядра тайла
            public int Core;            // текселей ядра по стороне
            public int Size;            // текселей текстуры (ядро + 2 × Gutter)
            public Color32[] Pixels;
            // Прямоугольник текстуры в мире для шейдера: uv = (xz - xy) * zw.
            public Vector4 Rect;
        }

        public sealed class Result
        {
            public float TexelsPerUnit;
            public float TileWorld;
            public int TilesPerSide;
            public readonly List<Tile> Tiles = new List<Tile>();
            public int OverviewResolution;
            public Color32[] Overview;
            public MapLabPaintFields Fields;
            public float Milliseconds;
            public float FieldsMs;
            public long Strokes;
            public long StampedTexels;
            public long Texels;
            public float MarginUnits;
            // Сумма времени потоков по фазам (не стена часов).
            public float RoadsThreadMs, BaseThreadMs, StrokesThreadMs, PackThreadMs;
        }

        public struct Options
        {
            public bool StrokeContribution;
            public bool RoadInkInPaint;
        }

        public static Result Paint(MapLabData data, MapLabSettings settings, MapLabPalette palette, float tileWorld, Options options)
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            MapLabStage03Settings s3 = settings.Stage03;
            Result result = new Result();
            result.Fields = new MapLabPaintFields(data, settings, palette);
            result.FieldsMs = result.Fields.Milliseconds;
            int tiles = Mathf.Max(1, Mathf.CeilToInt(data.Size / tileWorld - 1e-4f));
            int core = Mathf.Max(64, Mathf.RoundToInt(tileWorld * Mathf.Clamp(s3.PaintTexelsPerUnit, 2f, 32f)));
            result.TilesPerSide = tiles;
            result.TileWorld = tileWorld;
            result.TexelsPerUnit = core / tileWorld;
            float texel = tileWorld / core;
            float brush = Mathf.Max(0.3f, settings.PainterlyStyle.BrushScale);
            // Запас тайла под мазки соседей: самый длинный след (тонкая линия) с краем.
            float margin = brush * 2.4f * 1.4f * 0.5f + 0.6f;
            result.MarginUnits = margin;
            int marginPx = Mathf.CeilToInt(margin / texel) + 2;
            for (int z = 0; z < tiles; z++)
            for (int x = 0; x < tiles; x++)
            {
                Tile t = new Tile { X = x, Z = z, Origin = new Vector2(x * tileWorld, z * tileWorld), Core = core, Size = core + Gutter * 2 };
                float span = t.Size * texel;
                t.Rect = new Vector4(t.Origin.x - Gutter * texel, t.Origin.y - Gutter * texel, 1f / span, 1f / span);
                result.Tiles.Add(t);
            }
            List<MapLabPolyline> roads = new List<MapLabPolyline>(data.Roads);
            roads.AddRange(data.YardPaths);
            long strokes = 0, stamped = 0, texels = 0;
            // Буферы тайла переиспользуются потоками: пик памяти — число потоков,
            // а не число тайлов.
            ParallelOptions parallel = new ParallelOptions { MaxDegreeOfParallelism = Mathf.Clamp(System.Environment.ProcessorCount, 1, 16) };
            System.Collections.Concurrent.ConcurrentBag<Buffers> pool = new System.Collections.Concurrent.ConcurrentBag<Buffers>();
            int bufferWidth = core + Gutter * 2 + marginPx * 2;
            Parallel.For(0, result.Tiles.Count, parallel, i =>
            {
                Tile t = result.Tiles[i];
                System.Diagnostics.Stopwatch phase = System.Diagnostics.Stopwatch.StartNew();
                if (!pool.TryTake(out Buffers buffers))
                    buffers = new Buffers(bufferWidth * bufferWidth, options.StrokeContribution);
                Context ctx = new Context(data, settings, palette, result.Fields, roads, t, texel, marginPx, options, buffers);
                ctx.RasterizeRoads();
                long roadsTicks = phase.ElapsedTicks;
                ctx.BaseLayer();
                long baseTicks = phase.ElapsedTicks;
                if (settings.Diagnostics.ShowStrokes)
                    ctx.Strokes();
                long strokeTicks = phase.ElapsedTicks;
                t.Pixels = ctx.Pack();
                long packTicks = phase.ElapsedTicks;
                lock (result)
                {
                    double ms = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    result.RoadsThreadMs += (float)(roadsTicks * ms);
                    result.BaseThreadMs += (float)((baseTicks - roadsTicks) * ms);
                    result.StrokesThreadMs += (float)((strokeTicks - baseTicks) * ms);
                    result.PackThreadMs += (float)((packTicks - strokeTicks) * ms);
                }
                Interlocked.Add(ref strokes, ctx.StrokeCount);
                Interlocked.Add(ref stamped, ctx.StampCount);
                Interlocked.Add(ref texels, (long)ctx.Width * ctx.Width);
                pool.Add(buffers);
            });
            result.Strokes = strokes;
            result.StampedTexels = stamped;
            result.Texels = texels;
            BuildOverview(data, result);
            result.Milliseconds = (float)watch.Elapsed.TotalMilliseconds;
            return result;
        }

        // Обзорная копия ~4 текселя на единицу для шейдеров, которым нужна
        // краска «где-то рядом» (верх уступов): box-фильтр ядра тайлов.
        private static void BuildOverview(MapLabData data, Result result)
        {
            int ovPerTile = Mathf.Max(16, Mathf.RoundToInt(result.TileWorld * 4f));
            int factor = Mathf.Max(1, result.Tiles[0].Core / ovPerTile);
            ovPerTile = result.Tiles[0].Core / factor;
            int res = ovPerTile * result.TilesPerSide;
            result.OverviewResolution = res;
            result.Overview = new Color32[res * res];
            foreach (Tile t in result.Tiles)
            {
                for (int oz = 0; oz < ovPerTile; oz++)
                for (int ox = 0; ox < ovPerTile; ox++)
                {
                    int r = 0, g = 0, b = 0, a = 0;
                    for (int dz = 0; dz < factor; dz++)
                    for (int dx = 0; dx < factor; dx++)
                    {
                        Color32 c = t.Pixels[(Gutter + oz * factor + dz) * t.Size + Gutter + ox * factor + dx];
                        r += c.r; g += c.g; b += c.b; a += c.a;
                    }
                    int f2 = factor * factor;
                    result.Overview[(t.Z * ovPerTile + oz) * res + t.X * ovPerTile + ox] = new Color32((byte)(r / f2), (byte)(g / f2), (byte)(b / f2), (byte)(a / f2));
                }
            }
        }

        private struct Stroke
        {
            public float X, Z;          // центр в текселях буфера
            public int Key;
            public float DirX, DirZ, Length, Width, Alpha;   // длина и ширина в текселях
            public float R, G, B;
            public float Bristle, Phase;
        }

        private sealed class Buffers
        {
            public readonly float[] R, G, B, Ink, R0, G0, B0, RoadDistance;
            public readonly int[] Key;
            public readonly byte[] RoadKind, RoadAngle;

            public Buffers(int count, bool contribution)
            {
                R = new float[count];
                G = new float[count];
                B = new float[count];
                Ink = new float[count];
                Key = new int[count];
                RoadDistance = new float[count];

                RoadKind = new byte[count];
                RoadAngle = new byte[count];
                if (contribution)
                {
                    R0 = new float[count];
                    G0 = new float[count];
                    B0 = new float[count];
                }
            }
        }

        private sealed class Context
        {
            private readonly MapLabData data;
            private readonly MapLabSettings settings;
            private readonly MapLabPalette pal;
            private readonly MapLabPaintFields fields;
            private readonly List<MapLabPolyline> roads;
            private readonly Options options;
            private readonly float texel;
            private readonly float ppu;
            private readonly float ox, oz;      // мир левого нижнего угла буфера
            private readonly int pad;           // текселей буфера до текстуры тайла
            private readonly int size;          // сторона текстуры тайла
            public readonly int Width;          // сторона буфера
            private readonly float[] r, g, b, ink;
            private readonly float[] r0, g0, b0;
            private readonly int[] key;
            private readonly float[] roadDistance;
            private readonly bool stage04;
            private readonly MapLabRoadJunction junction;
            private readonly byte[] roadKind;
            private readonly byte[] roadAngle;
            private readonly Vector3 light;
            private readonly MapLabNoise flow;
            private readonly float edgeAa;
            public long StrokeCount;
            public long StampCount;

            public Context(MapLabData data, MapLabSettings settings, MapLabPalette pal, MapLabPaintFields fields, List<MapLabPolyline> roads, Tile tile, float texel, int marginPx, Options options, Buffers buffers)
            {
                this.data = data;
                this.settings = settings;
                this.pal = pal;
                this.fields = fields;
                this.roads = roads;
                this.options = options;
                this.texel = texel;
                ppu = 1f / texel;
                pad = marginPx;
                size = tile.Size;
                Width = size + marginPx * 2;
                ox = tile.Origin.x - (Gutter + marginPx) * texel;
                oz = tile.Origin.y - (Gutter + marginPx) * texel;
                r = buffers.R;
                g = buffers.G;
                b = buffers.B;
                ink = buffers.Ink;
                key = buffers.Key;
                roadDistance = buffers.RoadDistance;

                stage04 = settings.IsStage04;
                junction = MapLabRoadJunction.From(settings);
                roadKind = buffers.RoadKind;
                roadAngle = buffers.RoadAngle;
                System.Array.Clear(ink, 0, ink.Length);
                if (options.StrokeContribution)
                {
                    r0 = buffers.R0;
                    g0 = buffers.G0;
                    b0 = buffers.B0;
                }
                light = settings.LightDirection;
                flow = new MapLabNoise(MapLabRandom.StreamSeed(data.Seed, "paint03-flow", MapLabData.AlgorithmVersion));
                edgeAa = Mathf.Clamp(settings.Stage03.StrokeEdge, 0.3f, 3f);
                yardMin = new Vector2(float.MaxValue, float.MaxValue);
                yardMax = new Vector2(float.MinValue, float.MinValue);
                foreach (Vector4 y in data.Yards)
                {
                    float reach = y.z * 1.5f;
                    yardMin = Vector2.Min(yardMin, new Vector2(y.x - reach, y.y - reach));
                    yardMax = Vector2.Max(yardMax, new Vector2(y.x + reach, y.y + reach));
                }
            }

            private readonly Vector2 yardMin, yardMax;

            private Vector2 World(int x, int z)
            {
                return new Vector2(ox + (x + 0.5f) * texel, oz + (z + 0.5f) * texel);
            }

            // Сначала главные дороги (и расстояние до их кромки — для узлов),
            // затем тропы; на этапе 04 устье тропы у дороги плавно расширяется
            // (MapLabRoadJunction — та же форма, что у пера).
            public void RasterizeRoads()
            {
                for (int i = 0; i < roadDistance.Length; i++)
                    roadDistance[i] = float.MaxValue;
                RasterizePass(MapLabRoadKind.Road);
                RasterizePass(MapLabRoadKind.Path);
            }

            private void RasterizePass(MapLabRoadKind kind)
            {
                float x0w = ox, z0w = oz, x1w = ox + Width * texel, z1w = oz + Width * texel;
                for (int k = 0; k < roads.Count; k++)
                {
                    MapLabPolyline road = roads[k];
                    if ((road.Kind == MapLabRoadKind.Path) != (kind == MapLabRoadKind.Path))
                        continue;
                    bool path = kind == MapLabRoadKind.Path;
                    float half = road.Width * 0.5f;
                    float reach = half + 2.5f + (path ? junction.Mouth : 0f);
                    for (int s = 0; s < road.Points.Count - 1; s++)
                    {
                        Vector2 a = road.Points[s];
                        Vector2 c = road.Points[s + 1];
                        if (Mathf.Max(a.x, c.x) + reach < x0w || Mathf.Min(a.x, c.x) - reach > x1w || Mathf.Max(a.y, c.y) + reach < z0w || Mathf.Min(a.y, c.y) - reach > z1w)
                            continue;
                        if (OnBridge(data, (a + c) * 0.5f))
                            continue;
                        Vector2 dir = c - a;
                        float angle = Mathf.Repeat(Mathf.Atan2(dir.y, dir.x), Mathf.PI);
                        byte angleByte = (byte)Mathf.Clamp(Mathf.RoundToInt(angle / Mathf.PI * 255f), 0, 255);
                        int px0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, c.x) - reach - ox) * ppu));
                        int px1 = Mathf.Min(Width - 1, Mathf.CeilToInt((Mathf.Max(a.x, c.x) + reach - ox) * ppu));
                        int pz0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, c.y) - reach - oz) * ppu));
                        int pz1 = Mathf.Min(Width - 1, Mathf.CeilToInt((Mathf.Max(a.y, c.y) + reach - oz) * ppu));
                        for (int z = pz0; z <= pz1; z++)
                        for (int x = px0; x <= px1; x++)
                        {
                            int i = z * Width + x;
                            float d = MapLabGeometry.DistanceToSegment(World(x, z), a, c, out _) - half;
                            if (path && junction.Mouth > 0f && d < junction.Mouth + 0.5f)
                                d -= junction.MouthAt(fields.MainRoadAt(World(x, z)));
                            if (d < roadDistance[i])
                            {
                                roadDistance[i] = d;
                                roadKind[i] = (byte)road.Kind;
                                roadAngle[i] = angleByte;
                            }
                        }
                    }
                }
            }

            public void BaseLayer()
            {
                for (int z = 0; z < Width; z++)
                    BaseRow(z);
                if (r0 != null)
                {
                    System.Array.Copy(r, r0, r.Length);
                    System.Array.Copy(g, g0, g.Length);
                    System.Array.Copy(b, b0, b.Length);
                }
            }

            private void BaseRow(int z)
            {
                Vector4 inkWeights = settings.Stage03.InkStyle.Weights;
                for (int x = 0; x < Width; x++)
                {
                    int i = z * Width + x;
                    Vector2 w = World(x, z);
                    fields.Get(w, out MapLabPaintFields.Sample s);
                    Color c = new Color(s.R, s.G, s.B, 1f);
                    int k = KeyGrass;
                    // То же, что pow(forest · 1.25, 0.7) > 0.5 у этапа 02, без pow на тексель.
                    if (s.Forest > 0.2971f)
                        k = KeyForest;
                    if (stage04 && k == KeyGrass && blotStrength > 0f)
                        c = Blot(c, s);

                    // Поля: чёткий край пашни (узкий переход маски), полосы и межа.
                    if (s.FieldMask > 0.3f && data.Fields != null && data.Fields.Evaluate(w, out MapLabFields.Sample fs))
                    {
                        float edge = MapLabGeometry.SmoothStep(0.47f, 0.53f, s.FieldMask);
                        Color fc = FieldColor(fs, s.FieldTone);
                        float widthVar = s.WidthVar;
                        float border = MapLabGeometry.SmoothStep(0.22f * widthVar, 0.95f * widthVar, fs.BorderDistance);
                        float grassy = ((fs.Block * 13 + 5) % 5) < 3 ? 1f : 0.2f;
                        float stripEdge = Mathf.Min(fs.Across, fs.StripWidth - fs.Across);
                        fc = Color.Lerp(fc * 0.9f, fc, MapLabGeometry.SmoothStep(0.03f, 0.22f, stripEdge));
                        float furrow = Mathf.Abs(Mathf.Repeat(fs.Across / 0.75f, 1f) - 0.5f) * 2f;
                        fc *= 1f - 0.09f * (1f - MapLabGeometry.SmoothStep(0.25f, 0.6f, furrow));
                        fc.a = 1f;
                        float t = edge * border;
                        c = Color.Lerp(c, fc, t);
                        if (t > 0.5f)
                            k = KeyFieldBase + ((fs.Block * 37 + fs.Strip) & 0xFFFF);
                        if (edge > 0.5f)
                        {
                            // Межа — тонкая линия тем же пером, местами прервана. Этап 04:
                            // только выбранные участки — у дороги, на склоне и длинные
                            // отрезки по мировому шуму; остальное читается цветом и бороздами.
                            float line = 1f - MapLabGeometry.SmoothStep(0.05f, 0.14f, fs.BorderDistance);
                            float select = 1f;
                            if (stage04 && line > 0f)
                            {
                                float share = settings.Stage04.FieldInkShare;
                                float nearRoad = 1f - MapLabGeometry.SmoothStep(1.2f, 2.6f, roadDistance[i]);
                                float slope = MapLabGeometry.SmoothStep(5f, 10f, data.SampleSlope(w.x, w.y)) * 0.8f;
                                float chosen = MapLabGeometry.SmoothStep(1.5f - share * 1.9f, 1.58f - share * 1.9f, widthVar);
                                select = Mathf.Max(nearRoad, Mathf.Max(slope, chosen));
                            }
                            ink[i] = Mathf.Max(ink[i], 0.55f * inkWeights.z * line * select * Breakup(s.Breakup, 0.4f));
                            Color balk = Color.Lerp(Color.Lerp(pal.GrassA, pal.GrassLush, 0.5f), pal.ForestFloor, 0.1f);
                            c = Color.Lerp(c, balk, (1f - border) * edge * 0.45f * grassy);
                        }
                    }

                    float yard = YardAt(w, out bool garden, out float rows);
                    if (yard > 0f)
                    {
                        Color soil = Color.Lerp(pal.FieldFallow, pal.Road, 0.35f);
                        Color yc = garden ? Color.Lerp(soil * 0.8f, pal.GrassLush, rows) : Color.Lerp(c, soil, 0.6f);
                        yc.a = 1f;
                        c = Color.Lerp(c, yc, yard);
                        if (yard > 0.5f) k = KeyYard;
                    }

                    if (s.Rock > 0.02f)
                    {
                        Color rc = Color.Lerp(pal.RockShadow, pal.RockLight, MapLabGeometry.SmoothStep(0.25f, 0.75f, s.RockStreak));
                        float t = MapLabGeometry.SmoothStep(0.15f, 0.6f, s.Rock);
                        c = Color.Lerp(c, rc, t);
                        if (t > 0.5f) k = KeyRock;
                    }

                    if (s.Above < 90f)
                    {
                        float above = s.Above;
                        if (above < 0f)
                        {
                            c = Color.Lerp(pal.Shore, pal.WaterDeep, MapLabGeometry.SmoothStep(0f, 1.2f, -above));
                            k = KeyShore;
                        }
                        else if (above < 0.7f)
                        {
                            float t = 1f - MapLabGeometry.SmoothStep(0.15f, 0.7f, above);
                            c = Color.Lerp(c, pal.Shore, t * 0.85f);
                            if (t > 0.5f) k = KeyShore;
                        }
                    }
                    else if (s.WaterDistance < 2.5f)
                    {
                        c = Color.Lerp(c, pal.Shore, (1f - MapLabGeometry.SmoothStep(0.6f, 2.5f, s.WaterDistance)) * 0.7f);
                    }

                    // Дорога: чёткое полотно (переход ~1 тексель) по неровной кромке.
                    // Этап 04: у узла тропа плавно перенимает цвет, плотность и вес
                    // пера главной дороги (pathness 1 → 0 вдоль тропы).
                    bool path = roadKind[i] == (byte)MapLabRoadKind.Path;
                    float pathness = path ? 1f - junction.BlendAt(s.MainRoad) : 0f;
                    float rd = roadDistance[i];
                    if (rd < 1.8f)
                    {
                        float edge = rd + MapLabPaintFields.RoadEdgeOffset(s, pathness);
                        float soft = texel * 0.75f;
                        float body = 1f - MapLabGeometry.SmoothStep(-soft, soft, edge);
                        float rut = s.Rut * 0.07f;
                        Color rc = Color.Lerp(pal.Road, Color.Lerp(pal.Road, pal.GrassDry, 0.28f), pathness) * (1f + rut);
                        rc = Color.Lerp(rc, pal.RoadEdge, MapLabGeometry.SmoothStep(-0.45f, 0f, edge) * 0.22f);
                        float tuft = MapLabGeometry.SmoothStep(0.58f, 0.82f, s.Tuft) * Mathf.Lerp(0.2f, 0.5f, pathness);
                        rc = Color.Lerp(rc, Color.Lerp(c, pal.GrassDry, 0.3f), tuft);
                        rc.a = 1f;
                        c = Color.Lerp(c, rc, body * Mathf.Lerp(1f, 0.88f, pathness));
                        if (options.RoadInkInPaint)
                        {
                            float line = 1f - MapLabGeometry.SmoothStep(0f, texel * 1.5f, Mathf.Abs(edge));
                            ink[i] = Mathf.Max(ink[i], line * Mathf.Lerp(inkWeights.x, inkWeights.y, pathness) * 0.6f * Breakup(s.Breakup, 0.35f));
                        }
                        // Узкая притоптанная обочина — без широкого мутного ореола.
                        float shoulder = edge > 0f ? 1f - MapLabGeometry.SmoothStep(0.05f, 0.45f, edge) : 0f;
                        c = Color.Lerp(c, Color.Lerp(c, pal.RoadEdge, 0.5f), shoulder * 0.14f);
                        if (body > 0.5f) k = KeyRoad;
                    }

                    r[i] = c.r;
                    g[i] = c.g;
                    b[i] = c.b;
                    key[i] = k;
                }
            }

            // Этап 04: редкое акварельное пятно луга — сухая охра или приглушённая
            // зелень, с лёгким оседанием пигмента по краю; между пятнами тишина.
            private float blotStrength => settings.Stage04.Blots;

            private Color Blot(Color c, in MapLabPaintFields.Sample s)
            {
                float threshold = 0.29f * (1f - 2f * Mathf.Clamp(settings.Stage04.BlotShare, 0f, 0.6f));
                float inside = MapLabGeometry.SmoothStep(threshold, threshold + 0.05f, s.Blot);
                if (inside <= 0f && s.Blot < threshold - 0.02f)
                    return c;
                float rim = MapLabGeometry.SmoothStep(threshold - 0.005f, threshold + 0.02f, s.Blot) * (1f - MapLabGeometry.SmoothStep(threshold + 0.03f, threshold + 0.09f, s.Blot));
                Color dry = Color.Lerp(pal.GrassDry, pal.FieldOchre, 0.25f);
                Color muted = Color.Lerp(pal.GrassLush, pal.ForestFloor, 0.35f);
                Color tone = s.BlotHue > 0f ? dry : muted;
                float strength = blotStrength * 0.32f;
                Color result = Color.Lerp(c, tone * (c.grayscale / Mathf.Max(0.05f, tone.grayscale)) * 0.6f + tone * 0.4f, inside * strength);
                result *= 1f - rim * 0.07f * blotStrength;
                result.a = 1f;
                return result;
            }

            private static float Breakup(float v, float holes)
            {
                return MapLabGeometry.SmoothStep(holes - 0.12f, holes + 0.12f, v);
            }

            private Color FieldColor(MapLabFields.Sample fs, float tone01Noise)
            {
                float tone = data.Fields.StripTone(fs.Block, fs.Strip);
                Color c;
                if (tone < 0.36f) c = Color.Lerp(pal.FieldStraw, pal.FieldOchre, tone / 0.36f * 0.4f);
                else if (tone < 0.64f) c = Color.Lerp(pal.FieldOchre, pal.FieldStraw, (tone - 0.36f) / 0.28f * 0.35f);
                else if (tone < 0.86f) c = Color.Lerp(pal.FieldGreen, pal.GrassDry, (tone - 0.64f) / 0.22f * 0.5f);
                else c = Color.Lerp(pal.FieldFallow, pal.FieldOchre, (tone - 0.86f) / 0.14f * 0.4f);
                return c * (1f + tone01Noise);
            }

            private float YardAt(Vector2 w, out bool garden, out float rows)
            {
                garden = false;
                rows = 0f;
                float best = 0f;
                if (w.x < yardMin.x || w.y < yardMin.y || w.x > yardMax.x || w.y > yardMax.y)
                    return 0f;
                for (int i = 0; i < data.Yards.Count; i++)
                {
                    Vector4 y = data.Yards[i];
                    Vector2 rel = w - new Vector2(y.x, y.y);
                    if (rel.sqrMagnitude > y.z * y.z * 2.2f)
                        continue;
                    float ca = Mathf.Cos(y.w), sa = Mathf.Sin(y.w);
                    float u = rel.x * ca + rel.y * sa;
                    float v = -rel.x * sa + rel.y * ca;
                    float edge = Mathf.Max(Mathf.Abs(u), Mathf.Abs(v));
                    float t = 1f - MapLabGeometry.SmoothStep(y.z * 0.9f, y.z, edge);
                    if (t * 0.75f <= best)
                        continue;
                    best = t * 0.75f;
                    garden = u > y.z * 0.05f && ((i * 7) % 3) != 0;
                    rows = MapLabGeometry.SmoothStep(0.3f, 0.6f, Mathf.Abs(Mathf.Repeat(v / 0.7f, 1f) - 0.5f) * 2f);
                }
                return best;
            }

            // ---------- Мазки в мировых ячейках ----------

            public void Strokes()
            {
                MapLabStyleSettings style = settings.PainterlyStyle;
                MapLabStage03Settings s3 = settings.Stage03;
                if (style.StrokeDensity <= 0.001f || style.BrushStrength <= 0.001f)
                    return;
                float cellSize = Mathf.Clamp(s3.StrokeCell, 2f, 12f);
                float brush = Mathf.Max(0.3f, style.BrushScale);
                float density = style.StrokeDensity / 0.7f;
                float strength = style.BrushStrength;
                float split = s3.StrokeColorSplit;
                Vector2 flat = data.Composition != null ? data.Composition.FlatStrokeDirection.normalized : Vector2.right;
                if (flat.sqrMagnitude < 0.5f) flat = Vector2.right;
                float reach = brush * 2.4f * 1.4f * 0.5f + 0.6f;
                int cx0 = Mathf.FloorToInt((ox - reach) / cellSize), cx1 = Mathf.FloorToInt((ox + Width * texel + reach) / cellSize);
                int cz0 = Mathf.FloorToInt((oz - reach) / cellSize), cz1 = Mathf.FloorToInt((oz + Width * texel + reach) / cellSize);
                uint stream = (uint)MapLabRandom.StreamSeed(data.Seed, "paint03-strokes", MapLabData.AlgorithmVersion);
                List<Stroke> dabs = new List<Stroke>(4096), lines = new List<Stroke>(1024), hatch = new List<Stroke>(1024), traces = new List<Stroke>(256);
                for (int cz = cz0; cz <= cz1; cz++)
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    MapLabRandom rng = new MapLabRandom(((ulong)MapLabRandom.Hash(cx, cz, stream) << 32) | MapLabRandom.Hash(cz * 7 + 3, cx * 13 + 1, stream ^ 0x9E3779B9u));
                    Vector2 cellOrigin = new Vector2(cx * cellSize, cz * cellSize);

                    // Широкие следы: дрожащая сетка — ровное расстояние между соседями.
                    float expected = cellSize * cellSize * s3.StrokesPerUnit * density;
                    int grid = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(expected)));
                    float keep = expected / (grid * grid);
                    for (int sz = 0; sz < grid; sz++)
                    for (int sx = 0; sx < grid; sx++)
                    {
                        float roll = rng.Value();
                        Vector2 p = cellOrigin + new Vector2((sx + 0.5f + rng.Signed() * 0.42f) / grid, (sz + 0.5f + rng.Signed() * 0.42f) / grid) * cellSize;
                        float lengthJ = rng.Range(0.7f, 1.3f);
                        float valueJ = rng.Signed();
                        float hueJ = rng.Signed();
                        float alphaJ = rng.Range(0.6f, 1f);
                        float angleJ = rng.Signed();
                        float phase = rng.Value() * 6.283f;
                        if (roll > keep || !Center(p, out int ci, out float px, out float pz))
                            continue;
                        int k = key[ci];
                        if (k == KeyRoad || k == KeyShore)
                            continue;
                        Vector2 dir = Rotate(StrokeDirection(p, k, flat), angleJ * 0.2f);
                        float length = brush * lengthJ * (k == KeyRock ? 0.8f : 1f) * (k >= KeyFieldBase ? 1.45f : 1f);
                        float width = length * (k == KeyForest ? 0.42f : k >= KeyFieldBase ? 0.22f : 0.3f);
                        Color baseColor = new Color(r[ci], g[ci], b[ci]);
                        Color shift = k == KeyGrass ? (hueJ > 0f ? pal.GrassDry : pal.GrassLush) : (k >= KeyFieldBase ? (hueJ > 0f ? pal.FieldStraw : pal.FieldOchre) : baseColor);
                        Color color = Color.Lerp(baseColor, shift, Mathf.Abs(hueJ) * 0.5f * split) * (1f + valueJ * 0.13f * strength * (0.5f + split));
                        dabs.Add(Make(px, pz, dir, length * ppu, Mathf.Max(1.3f, width * ppu), color, alphaJ * Mathf.Lerp(0.35f, 0.9f, strength), k, 0.07f, phase));
                    }

                    // Тонкие линии — «штриховка травы» вдоль горизонталей и борозд.
                    int lineCount = Poisson(ref rng, cellSize * cellSize * 0.11f * density * strength);
                    for (int s = 0; s < lineCount; s++)
                    {
                        Vector2 p = cellOrigin + new Vector2(rng.Value(), rng.Value()) * cellSize;
                        float lengthJ = rng.Range(0.7f, 1.4f);
                        bool dark = rng.Chance(0.5f);
                        float alphaJ = rng.Range(0.25f, 0.5f);
                        float angleJ = rng.Signed();
                        if (!Center(p, out int ci, out float px, out float pz))
                            continue;
                        int k = key[ci];
                        if (k == KeyRoad || k == KeyShore || k == KeyForest)
                            continue;
                        Vector2 dir = Rotate(StrokeDirection(p, k, flat), angleJ * 0.08f);
                        Color baseColor = new Color(r[ci], g[ci], b[ci]);
                        Color color = dark ? Color.Lerp(baseColor * 0.86f, pal.GrassLush * 0.9f, k == KeyGrass ? 0.3f : 0f) : Color.Lerp(baseColor * 1.1f, pal.GrassDry, k == KeyGrass ? 0.35f : 0.15f);
                        lines.Add(Make(px, pz, dir, brush * 2.4f * lengthJ * ppu, Mathf.Max(1.1f, 0.07f * ppu), color, alphaJ, k, 0f, 0f));
                    }

                    // Штриховка теневых крутых склонов — по падению.
                    int hatchCount = Poisson(ref rng, cellSize * cellSize * 0.55f * style.Hatching);
                    for (int s = 0; s < hatchCount; s++)
                    {
                        Vector2 p = cellOrigin + new Vector2(rng.Value(), rng.Value()) * cellSize;
                        float roll = rng.Value();
                        float lengthJ = rng.Range(0.6f, 1.3f);
                        if (!Center(p, out int ci, out float px, out float pz))
                            continue;
                        int k = key[ci];
                        if (k == KeyRoad || k == KeyShore || k == KeyYard || k >= KeyFieldBase)
                            continue;
                        float slope = data.SampleSlope(p.x, p.y);
                        float facing = Vector3.Dot(data.SampleNormal(p.x, p.y), light);
                        float want = MapLabGeometry.SmoothStep(6f, 24f, slope) * MapLabGeometry.SmoothStep(0.85f, 0.45f, facing);
                        if (k == KeyRock) want = Mathf.Max(want, 0.5f);
                        if (roll > want)
                            continue;
                        Vector2 dir = SampleDownhill(p);
                        if (dir.sqrMagnitude < 0.5f)
                            continue;
                        Color baseColor = new Color(r[ci], g[ci], b[ci]);
                        Color dark = Color.Lerp(baseColor * 0.72f, pal.ShadowTint, 0.2f);
                        hatch.Add(Make(px, pz, dir, brush * 1.2f * lengthJ * ppu, Mathf.Max(1.1f, 0.075f * ppu), dark, 0.5f * style.Hatching + 0.25f, k, 0f, 0f));
                    }

                    // Дорога: редкие продольные следы по её направлению.
                    int traceCount = Poisson(ref rng, cellSize * cellSize * 0.12f * density);
                    for (int s = 0; s < traceCount; s++)
                    {
                        Vector2 p = cellOrigin + new Vector2(rng.Value(), rng.Value()) * cellSize;
                        bool lighter = rng.Chance(0.4f);
                        float lengthJ = rng.Range(0.8f, 1.5f);
                        if (!Center(p, out int ci, out float px, out float pz) || key[ci] != KeyRoad)
                            continue;
                        float angle = roadAngle[ci] / 255f * Mathf.PI;
                        Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                        Color baseColor = new Color(r[ci], g[ci], b[ci]);
                        Color color = lighter ? Color.Lerp(baseColor, Color.white, 0.07f) : Color.Lerp(baseColor * 0.9f, pal.RoadEdge, 0.15f);
                        traces.Add(Make(px, pz, dir, brush * 1.3f * lengthJ * ppu, Mathf.Max(1.1f, 0.11f * ppu), color, 0.32f * strength + 0.12f, KeyRoad, 0f, 0f));
                    }
                }
                StrokeCount = dabs.Count + lines.Count + hatch.Count + traces.Count;
                foreach (List<Stroke> list in new[] { dabs, lines, hatch, traces })
                    for (int i = 0; i < list.Count; i++)
                        Stamp(list[i]);
            }

            private static int Poisson(ref MapLabRandom rng, float mean)
            {
                int whole = Mathf.FloorToInt(mean);
                return whole + (rng.Value() < mean - whole ? 1 : 0);
            }

            // Центр мазка в буфере тайла: ячейка-тексель и дробные координаты.
            private bool Center(Vector2 world, out int index, out float px, out float pz)
            {
                px = (world.x - ox) * ppu - 0.5f;
                pz = (world.y - oz) * ppu - 0.5f;
                int ix = Mathf.RoundToInt(px), iz = Mathf.RoundToInt(pz);
                index = iz * Width + ix;
                return ix >= 0 && iz >= 0 && ix < Width && iz < Width;
            }

            private Vector2 StrokeDirection(Vector2 w, int k, Vector2 flat)
            {
                if (k >= KeyFieldBase && data.Fields != null && data.Fields.Evaluate(w, out MapLabFields.Sample fs))
                {
                    float a = data.Fields.Block(fs.Block).Angle;
                    return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                }
                Vector2 down = SampleDownhill(w);
                float slope = data.SampleSlope(w.x, w.y);
                float strength = MapLabGeometry.SmoothStep(1.5f, 8f, slope);
                Vector2 along = k == KeyRock ? down : new Vector2(-down.y, down.x);
                // На ровном — плавный поток крупного масштаба, а не одно направление.
                Vector2 flow2 = Rotate(flat, flow.Fbm(w.x / 46f, w.y / 46f, 2) * 0.9f);
                if (Vector2.Dot(along, flow2) < 0f) along = -along;
                Vector2 dir = (along * strength + flow2 * (1f - strength)).normalized;
                return dir.sqrMagnitude < 0.5f ? flow2 : dir;
            }

            private Vector2 SampleDownhill(Vector2 w)
            {
                data.GridCoords(w.x, w.y, out int x0, out int z0, out float fx, out float fz);
                Vector2 a = data.Downhill[data.Index(x0, z0)];
                Vector2 b2 = data.Downhill[data.Index(x0 + 1, z0)];
                Vector2 c = data.Downhill[data.Index(x0, z0 + 1)];
                Vector2 d = data.Downhill[data.Index(x0 + 1, z0 + 1)];
                Vector2 v = Vector2.Lerp(Vector2.Lerp(a, b2, fx), Vector2.Lerp(c, d, fx), fz);
                return v.sqrMagnitude > 1e-4f ? v.normalized : Vector2.zero;
            }

            private static Vector2 Rotate(Vector2 v, float angle)
            {
                float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
                return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
            }

            private static Stroke Make(float x, float z, Vector2 dir, float length, float width, Color color, float alpha, int k, float bristle, float phase)
            {
                return new Stroke { X = x, Z = z, DirX = dir.x, DirZ = dir.y, Length = length, Width = width, Alpha = alpha, Key = k, R = color.r, G = color.g, B = color.b, Bristle = bristle, Phase = phase };
            }

            // След кисти: скруглённая голова, сужение к хвосту, плотная середина;
            // мягкий край — edgeAa текселей по ширине и по длине. Обход — только
            // отрезок строки внутри повёрнутого прямоугольника следа.
            private void Stamp(Stroke s)
            {
                float half = s.Length * 0.5f;
                float halfW = s.Width * 0.5f;
                float extent = half + halfW + edgeAa;
                float spanA = half + edgeAa, spanB = halfW + edgeAa;
                int z0 = Mathf.Max(0, Mathf.FloorToInt(s.Z - extent)), z1 = Mathf.Min(Width - 1, Mathf.CeilToInt(s.Z + extent));
                float invHalf = 1f / Mathf.Max(0.5f, half);
                float invEdge = 1f / edgeAa;
                float bristleK = 7.9f / Mathf.Max(0.5f, halfW);
                long stamped = 0;
                for (int z = z0; z <= z1; z++)
                {
                    float dz = z - s.Z;
                    // along = dx·DirX + dz·DirZ ∈ [−A, A]; across = −dx·DirZ + dz·DirX ∈ [−B, B].
                    float lo = -extent, hi = extent;
                    Clip(s.DirX, dz * s.DirZ, spanA, ref lo, ref hi);
                    Clip(-s.DirZ, dz * s.DirX, spanB, ref lo, ref hi);
                    if (lo > hi)
                        continue;
                    int x0 = Mathf.Max(0, Mathf.FloorToInt(s.X + lo)), x1 = Mathf.Min(Width - 1, Mathf.CeilToInt(s.X + hi));
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = z * Width + x;
                        if (key[i] != s.Key)
                            continue;
                        float dx = x - s.X;
                        float along = dx * s.DirX + dz * s.DirZ;
                        if (along < -half - edgeAa || along > half + edgeAa)
                            continue;
                        float across = -dx * s.DirZ + dz * s.DirX;
                        float t = Mathf.Clamp(along * invHalf, -1f, 1f);
                        float w = halfW * (1f - 0.45f * MapLabGeometry.SmoothStep(-0.2f, 1f, t));
                        if (t < -0.7f)
                            w *= Mathf.Sqrt(Mathf.Max(0f, 1f - Sq((t + 0.7f) / 0.3f)));
                        float cover = Mathf.Clamp01((w - Mathf.Abs(across)) * invEdge + 0.5f);
                        float ends = Mathf.Clamp01((half - Mathf.Abs(along)) * invEdge + 0.5f);
                        float a = s.Alpha * cover * ends;
                        if (a <= 0.002f)
                            continue;
                        float bristle = s.Bristle > 0f ? 1f + s.Bristle * Wave(across * bristleK + s.Phase) : 1f;
                        r[i] += (s.R * bristle - r[i]) * a;
                        g[i] += (s.G * bristle - g[i]) * a;
                        b[i] += (s.B * bristle - b[i]) * a;
                        stamped++;
                    }
                }
                StampCount += stamped;
            }

            private static float Sq(float v) => v * v;

            // Сужает [lo, hi] до dx, где |k·dx + c| ≤ limit.
            private static void Clip(float k, float c, float limit, ref float lo, ref float hi)
            {
                if (Mathf.Abs(k) < 1e-5f)
                {
                    if (Mathf.Abs(c) > limit)
                        hi = lo - 1f;
                    return;
                }
                float a = (-limit - c) / k, b = (limit - c) / k;
                if (a > b) { float t = a; a = b; b = t; }
                lo = Mathf.Max(lo, a);
                hi = Mathf.Min(hi, b);
            }

            // Треугольная волна вместо синуса: те же «щетинки» без sin на тексель.
            private static float Wave(float x)
            {
                float t = x * 0.15915494f;
                t -= Mathf.Floor(t);
                return Mathf.Abs(t * 4f - 2f) - 1f;
            }

            public Color32[] Pack()
            {
                Color32[] pixels = new Color32[size * size];
                for (int z = 0; z < size; z++)
                {
                    int src = (z + pad) * Width + pad;
                    int dst = z * size;
                    for (int x = 0; x < size; x++, src++, dst++)
                    {
                        float rr = r[src], gg = g[src], bb = b[src];
                        if (r0 != null)
                        {
                            // Диагностика: вклад мазков (серый — без изменения).
                            rr = 0.5f + (rr - r0[src]) * 3f;
                            gg = 0.5f + (gg - g0[src]) * 3f;
                            bb = 0.5f + (bb - b0[src]) * 3f;
                        }
                        pixels[dst] = new Color32(
                            (byte)Mathf.Clamp(Mathf.RoundToInt(rr * 255f), 0, 255),
                            (byte)Mathf.Clamp(Mathf.RoundToInt(gg * 255f), 0, 255),
                            (byte)Mathf.Clamp(Mathf.RoundToInt(bb * 255f), 0, 255),
                            (byte)Mathf.Clamp(Mathf.RoundToInt((r0 != null ? 0f : ink[src]) * 255f), 0, 255));
                    }
                }
                return pixels;
            }
        }

        public static bool OnBridge(MapLabData data, Vector2 point)
        {
            for (int i = 0; i < data.Bridges.Count; i++)
            {
                float d = MapLabGeometry.DistanceToSegment(point, data.Bridges[i].EndA, data.Bridges[i].EndB, out float t);
                if (d < 1f && t > 0.03f && t < 0.97f)
                    return true;
            }
            return false;
        }
    }
}
