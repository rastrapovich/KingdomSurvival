using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Живописная покраска поверхности на CPU в мировых координатах:
    // базовый материал местности → крупные цветовые пятна → направленные мазки
    // и тонкие линии → локальная штриховка. Свет по нормалям, тени, бумага и
    // мелкая фактура — в шейдере, поэтому ползунки света не требуют перекраски.
    // Результат: RGB — краска (sRGB), A — сила тонкой линии (контура).
    // Базовый слой считается параллельно по строкам; мазки — заранее
    // составленный список, накладываемый непересекающимися полосами, поэтому
    // результат не зависит от числа потоков.
    public static class MapLabPainter
    {
        private const int KeyGrass = 1;
        private const int KeyForest = 2;
        private const int KeyRoad = 3;
        private const int KeyRock = 4;
        private const int KeyShore = 5;
        private const int KeyYard = 6;
        private const int KeyFieldBase = 16;

        public sealed class Result
        {
            public int Resolution;
            public Color32[] Pixels;
            public float Milliseconds;
        }

        public static Result Paint(MapLabData data, MapLabSettings settings, MapLabPalette palette)
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            int p = settings.PaintResolution;
            Context ctx = new Context(data, settings, palette, p);
            ctx.RasterizeRoads();
            ctx.BaseLayer();
            if (settings.Diagnostics.ShowStrokes)
                ctx.Strokes();
            Result result = new Result { Resolution = p, Pixels = ctx.Pack() };
            result.Milliseconds = (float)watch.Elapsed.TotalMilliseconds;
            return result;
        }

        private struct Stroke
        {
            public int X, Z, Key;
            public float DirX, DirZ, Length, Width, Alpha;
            public float R, G, B;
        }

        private sealed class Context
        {
            private readonly MapLabData data;
            private readonly MapLabSettings settings;
            private readonly MapLabPalette pal;
            private readonly int p;
            private readonly float texel;
            private readonly float pixelsPerUnit;
            private readonly float[] r, g, b, ink;
            private readonly int[] key;
            private readonly float[] roadDistance;
            private readonly Vector3 light;
            private readonly MapLabNoise macro, macro2, mottle, breakup, rockStreak, fieldNoise;

            public Context(MapLabData data, MapLabSettings settings, MapLabPalette palette, int p)
            {
                this.data = data;
                this.settings = settings;
                pal = palette;
                this.p = p;
                texel = data.Size / p;
                pixelsPerUnit = p / data.Size;
                int count = p * p;
                r = new float[count];
                g = new float[count];
                b = new float[count];
                ink = new float[count];
                key = new int[count];
                roadDistance = new float[count];
                light = settings.LightDirection;
                macro = Noise("paint-macro");
                macro2 = Noise("paint-macro2");
                mottle = Noise("paint-mottle");
                breakup = Noise("paint-breakup");
                rockStreak = Noise("paint-rock");
                fieldNoise = Noise("paint-field");
            }

            private MapLabNoise Noise(string name)
            {
                return new MapLabNoise(MapLabRandom.StreamSeed(data.Seed, name, MapLabData.AlgorithmVersion));
            }

            private Vector2 World(int x, int z)
            {
                return new Vector2((x + 0.5f) * texel, (z + 0.5f) * texel);
            }

            // Дороги — точное расстояние на пиксельной сетке.
            public void RasterizeRoads()
            {
                for (int i = 0; i < roadDistance.Length; i++)
                    roadDistance[i] = float.MaxValue;
                for (int k = 0; k < data.Roads.Count; k++)
                {
                    MapLabPolyline road = data.Roads[k];
                    float half = road.Width * 0.5f;
                    float reach = half + 2.5f;
                    for (int s = 0; s < road.Points.Count - 1; s++)
                    {
                        Vector2 a = road.Points[s];
                        Vector2 c = road.Points[s + 1];
                        if (OnBridge((a + c) * 0.5f))
                            continue;
                        int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, c.x) - reach) * pixelsPerUnit));
                        int x1 = Mathf.Min(p - 1, Mathf.CeilToInt((Mathf.Max(a.x, c.x) + reach) * pixelsPerUnit));
                        int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, c.y) - reach) * pixelsPerUnit));
                        int z1 = Mathf.Min(p - 1, Mathf.CeilToInt((Mathf.Max(a.y, c.y) + reach) * pixelsPerUnit));
                        for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                        {
                            float d = MapLabGeometry.DistanceToSegment(World(x, z), a, c, out _) - half;
                            int i = z * p + x;
                            if (d < roadDistance[i])
                                roadDistance[i] = d;
                        }
                    }
                }
            }

            private bool OnBridge(Vector2 point)
            {
                for (int i = 0; i < data.Bridges.Count; i++)
                {
                    float d = MapLabGeometry.DistanceToSegment(point, data.Bridges[i].EndA, data.Bridges[i].EndB, out float t);
                    if (d < 1f && t > 0.03f && t < 0.97f)
                        return true;
                }
                return false;
            }

            public void BaseLayer()
            {
                Parallel.For(0, p, BaseRow);
            }

            private void BaseRow(int z)
            {
                MapLabStyleSettings style = settings.PainterlyStyle;
                float macroScale = Mathf.Max(5f, style.MacroScale);
                bool showForest = settings.Diagnostics.ShowForest;
                for (int x = 0; x < p; x++)
                {
                    int i = z * p + x;
                    Vector2 w = World(x, z);
                    float h = data.SampleHeight(w.x, w.y);
                    Vector3 n = data.SampleNormal(w.x, w.y);
                    float facing = Vector3.Dot(n, light);

                    // Трава: крупные пятна двух масштабов, сухие освещённые склоны,
                    // сочная низина, неровное покрытие краской.
                    float m1 = macro.Fbm(w.x / macroScale, w.y / macroScale, 3) * 0.5f + 0.5f;
                    float m2 = macro2.Fbm(w.x / (macroScale * 0.42f), w.y / (macroScale * 0.42f), 2) * 0.5f + 0.5f;
                    float mt = mottle.Fbm(w.x / 7f, w.y / 7f, 2);
                    Color c = Color.Lerp(pal.GrassA, pal.GrassB, MapLabGeometry.SmoothStep(0.28f, 0.72f, m1));
                    float heightT = (h - data.MinHeight) / Mathf.Max(0.5f, data.MaxHeight - data.MinHeight);
                    float dryness = MapLabGeometry.SmoothStep(0.5f, 0.85f, m2) * 0.6f + MapLabGeometry.SmoothStep(0.75f, 0.98f, facing) * 0.3f + heightT * 0.15f;
                    c = Color.Lerp(c, pal.GrassDry, Mathf.Clamp01(dryness));
                    float wet = data.SampleMask(data.Shore, w.x, w.y) * 0.6f + (1f - MapLabGeometry.SmoothStep(0.1f, 0.45f, heightT)) * 0.2f + MapLabGeometry.SmoothStep(0.15f, 0.55f, -m2 + 0.5f) * 0.25f;
                    c = Color.Lerp(c, pal.GrassLush, Mathf.Clamp01(wet));
                    c *= 1f + mt * 0.07f;
                    int k = KeyGrass;

                    // Подлесок: промежутки между кронами читаются тенью.
                    float forest = showForest ? data.SampleMask(data.Forest, w.x, w.y) : 0f;
                    if (forest > 0.02f)
                    {
                        float f = Mathf.Pow(Mathf.Clamp01(forest * 1.25f), 0.7f);
                        c = Color.Lerp(c, pal.ForestFloor, f * 0.94f);
                        if (f > 0.5f) k = KeyForest;
                    }

                    // Поля: полосы-наделы, свой тон у каждой, борозды по оси участка.
                    float fieldMask = data.SampleMask(data.Field, w.x, w.y);
                    if (fieldMask > 0.3f && data.Fields != null && data.Fields.Evaluate(w, out MapLabFields.Sample fs))
                    {
                        float edgeAa = MapLabGeometry.SmoothStep(0.42f, 0.58f, fieldMask);
                        Color fc = FieldColor(fs, w);
                        float border = MapLabGeometry.SmoothStep(0.45f, 1.1f, fs.BorderDistance);
                        float stripEdge = Mathf.Min(fs.Across, fs.StripWidth - fs.Across);
                        fc = Color.Lerp(fc * 0.9f, fc, MapLabGeometry.SmoothStep(0.05f, 0.45f, stripEdge));
                        float furrow = Mathf.Abs(Mathf.Repeat(fs.Across / 0.75f, 1f) - 0.5f) * 2f;
                        fc *= 1f - 0.09f * (1f - MapLabGeometry.SmoothStep(0.25f, 0.6f, furrow));
                        fc.a = 1f;
                        float t = edgeAa * border;
                        c = Color.Lerp(c, fc, t);
                        if (t > 0.5f)
                            k = KeyFieldBase + ((fs.Block * 37 + fs.Strip) & 0xFFFF);
                        if (edgeAa > 0.5f)
                        {
                            float line = 1f - MapLabGeometry.SmoothStep(0.15f, 0.5f, fs.BorderDistance);
                            ink[i] = Mathf.Max(ink[i], 0.3f * line * Breakup(w, 0.4f));
                            // Межа — полоса травы чуть темнее луга.
                            c = Color.Lerp(c, pal.GrassLush * 0.92f, (1f - border) * edgeAa * 0.5f);
                        }
                    }

                    // Дворы и огороды.
                    float yard = YardAt(w, out bool garden, out float rows);
                    if (yard > 0f)
                    {
                        Color soil = Color.Lerp(pal.FieldFallow, pal.Road, 0.35f);
                        Color yc = garden ? Color.Lerp(soil * 0.8f, pal.GrassLush, rows) : Color.Lerp(c, soil, 0.6f);
                        yc.a = 1f;
                        c = Color.Lerp(c, yc, yard);
                        if (yard > 0.5f) k = KeyYard;
                    }

                    // Камень: по маске и крутизне, со штрихами по падению.
                    float rock = data.SampleMask(data.Rock, w.x, w.y);
                    if (rock > 0.02f)
                    {
                        float streak = rockStreak.Fbm(w.x / 2.2f, w.y / 9f, 3) * 0.5f + 0.5f;
                        Color rc = Color.Lerp(pal.RockShadow, pal.RockLight, MapLabGeometry.SmoothStep(0.25f, 0.75f, streak));
                        float t = MapLabGeometry.SmoothStep(0.15f, 0.6f, rock);
                        c = Color.Lerp(c, rc, t);
                        if (t > 0.5f) k = KeyRock;
                    }


                    // Берег: узкая полоса почвы у воды, ниже — дно.
                    float level = data.SampleWaterLevel(w.x, w.y);
                    if (!float.IsNaN(level))
                    {
                        float above = h - level;
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
                    else
                    {
                        float wd = data.WaterDistance != null ? data.Bilinear(data.WaterDistance, w.x, w.y) : 1e6f;
                        if (wd < 2.5f)
                            c = Color.Lerp(c, pal.Shore, (1f - MapLabGeometry.SmoothStep(0.6f, 2.5f, wd)) * 0.7f);
                    }

                    // Дорога поверх всего: светлое полотно, тёмная кромка местами.
                    float rd = roadDistance[i];
                    if (rd < 1.2f)
                    {
                        float body = 1f - MapLabGeometry.SmoothStep(-0.15f, 0.15f, rd);
                        float rut = mottle.Fbm(w.x / 3f, w.y / 3f, 2) * 0.07f;
                        Color rc = pal.Road * (1f + rut);
                        rc = Color.Lerp(rc, pal.RoadEdge, MapLabGeometry.SmoothStep(-0.55f, 0f, rd) * 0.35f);
                        rc.a = 1f;
                        c = Color.Lerp(c, rc, body);
                        float edgeLine = 1f - MapLabGeometry.SmoothStep(0f, 0.28f, Mathf.Abs(rd - 0.08f));
                        ink[i] = Mathf.Max(ink[i], edgeLine * 0.5f * Breakup(w, 0.35f));
                        float shoulder = (1f - MapLabGeometry.SmoothStep(0.1f, 1.2f, rd)) * (rd > 0f ? 1f : 0f);
                        c = Color.Lerp(c, Color.Lerp(c, pal.RoadEdge, 0.5f), shoulder * 0.3f);
                        if (body > 0.5f) k = KeyRoad;
                    }

                    r[i] = c.r;
                    g[i] = c.g;
                    b[i] = c.b;
                    key[i] = k;
                }
            }

            private Color FieldColor(MapLabFields.Sample fs, Vector2 w)
            {
                float tone = data.Fields.StripTone(fs.Block, fs.Strip);
                Color c;
                if (tone < 0.36f) c = Color.Lerp(pal.FieldStraw, pal.FieldOchre, tone / 0.36f * 0.4f);
                else if (tone < 0.64f) c = Color.Lerp(pal.FieldOchre, pal.FieldStraw, (tone - 0.36f) / 0.28f * 0.35f);
                else if (tone < 0.86f) c = Color.Lerp(pal.FieldGreen, pal.GrassDry, (tone - 0.64f) / 0.22f * 0.5f);
                else c = Color.Lerp(pal.FieldFallow, pal.FieldOchre, (tone - 0.86f) / 0.14f * 0.4f);
                float v = fieldNoise.Fbm(w.x / 14f, w.y / 14f, 2) * 0.06f;
                return c * (1f + v);
            }

            private float YardAt(Vector2 w, out bool garden, out float rows)
            {
                garden = false;
                rows = 0f;
                float best = 0f;
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
                    float t = 1f - MapLabGeometry.SmoothStep(y.z * 0.85f, y.z, edge);
                    if (t * 0.75f <= best)
                        continue;
                    best = t * 0.75f;
                    garden = u > y.z * 0.05f && ((i * 7) % 3) != 0;
                    rows = MapLabGeometry.SmoothStep(0.3f, 0.6f, Mathf.Abs(Mathf.Repeat(v / 0.7f, 1f) - 0.5f) * 2f);
                }
                return best;
            }

            private float Breakup(Vector2 w, float holes)
            {
                float v = breakup.Fbm(w.x / 5f, w.y / 5f, 2) * 0.5f + 0.5f;
                return MapLabGeometry.SmoothStep(holes - 0.12f, holes + 0.12f, v);
            }

            // Мазки: широкие (по горизонталям, вдоль борозд, по падению камня),
            // тонкие длинные линии по склону и штриховка теневых склонов.
            // Мазок не переходит в соседний материал (ключ).
            public void Strokes()
            {
                MapLabStyleSettings style = settings.PainterlyStyle;
                if (style.StrokeDensity <= 0.001f)
                    return;
                List<Stroke> strokes = new List<Stroke>();
                MapLabRandom rng = MapLabRandom.ForStream(data.Seed, "paint-strokes", MapLabData.AlgorithmVersion);
                float lengthPx = Mathf.Max(2f, style.BrushScale * pixelsPerUnit);
                Vector2 flat = data.Composition != null ? data.Composition.FlatStrokeDirection.normalized : Vector2.right;
                float strength = style.BrushStrength;

                int dabs = Mathf.Min(1500000, Mathf.RoundToInt(p * (float)p / (lengthPx * lengthPx * 0.2f) * style.StrokeDensity));
                for (int s = 0; s < dabs; s++)
                {
                    int cx = rng.Range(0, p), cz = rng.Range(0, p);
                    float lengthJ = rng.Range(0.6f, 1.4f);
                    float valueJ = rng.Signed();
                    float hueJ = rng.Signed();
                    float alphaJ = rng.Range(0.45f, 1f);
                    float angleJ = rng.Signed();
                    int ci = cz * p + cx;
                    int k = key[ci];
                    if (k == KeyRoad || k == KeyShore || strength <= 0.001f)
                        continue;
                    Vector2 dir = Rotate(StrokeDirection(cx, cz, k, flat), angleJ * 0.22f);
                    float length = lengthPx * lengthJ * (k == KeyRock ? 0.8f : 1f) * (k >= KeyFieldBase ? 1.5f : 1f);
                    float width = Mathf.Max(1.2f, length * (k == KeyForest ? 0.45f : 0.28f));
                    float value = 1f + valueJ * 0.15f * strength;
                    Color baseColor = new Color(r[ci], g[ci], b[ci]);
                    Color shift = k == KeyGrass ? (hueJ > 0f ? pal.GrassDry : pal.GrassLush) : (k >= KeyFieldBase ? (hueJ > 0f ? pal.FieldStraw : pal.FieldOchre) : baseColor);
                    Color color = Color.Lerp(baseColor, shift, Mathf.Abs(hueJ) * 0.45f) * value;
                    strokes.Add(Make(cx, cz, dir, length, width, color, alphaJ * Mathf.Lerp(0.3f, 0.8f, strength), k));
                }

                // Тонкие линии — «штриховка травы» вдоль горизонталей и борозд.
                int lines = Mathf.RoundToInt(p * (float)p / 240f * style.StrokeDensity * strength);
                for (int s = 0; s < lines; s++)
                {
                    int cx = rng.Range(0, p), cz = rng.Range(0, p);
                    float lengthJ = rng.Range(0.7f, 1.4f);
                    bool dark = rng.Chance(0.5f);
                    float alphaJ = rng.Range(0.22f, 0.5f);
                    float angleJ = rng.Signed();
                    int ci = cz * p + cx;
                    int k = key[ci];
                    if (k == KeyRoad || k == KeyShore || k == KeyForest)
                        continue;
                    Vector2 dir = Rotate(StrokeDirection(cx, cz, k, flat), angleJ * 0.08f);
                    Color baseColor = new Color(r[ci], g[ci], b[ci]);
                    Color color = dark ? Color.Lerp(baseColor * 0.86f, pal.GrassLush * 0.9f, k == KeyGrass ? 0.3f : 0f) : Color.Lerp(baseColor * 1.1f, pal.GrassDry, k == KeyGrass ? 0.35f : 0.15f);
                    strokes.Add(Make(cx, cz, dir, lengthPx * 2.4f * lengthJ, 1.15f, color, alphaJ, k));
                }

                // Штриховка теневых крутых склонов — по падению.
                int hatch = Mathf.RoundToInt(p * (float)p / 45f * style.Hatching);
                for (int s = 0; s < hatch; s++)
                {
                    int cx = rng.Range(0, p), cz = rng.Range(0, p);
                    float roll = rng.Value();
                    float lengthJ = rng.Range(0.6f, 1.3f);
                    int ci = cz * p + cx;
                    int k = key[ci];
                    if (k == KeyRoad || k == KeyShore || k == KeyYard || k >= KeyFieldBase)
                        continue;
                    Vector2 w = World(cx, cz);
                    float slope = data.SampleSlope(w.x, w.y);
                    float facing = Vector3.Dot(data.SampleNormal(w.x, w.y), light);
                    float want = MapLabGeometry.SmoothStep(6f, 24f, slope) * MapLabGeometry.SmoothStep(0.85f, 0.45f, facing);
                    if (k == KeyRock) want = Mathf.Max(want, 0.5f);
                    if (roll > want)
                        continue;
                    Vector2 dir = SampleDownhill(w);
                    if (dir.sqrMagnitude < 0.5f)
                        continue;
                    Color baseColor = new Color(r[ci], g[ci], b[ci]);
                    Color dark = Color.Lerp(baseColor * 0.7f, pal.ShadowTint, 0.2f);
                    strokes.Add(Make(cx, cz, dir, lengthPx * 1.2f * lengthJ, 1.1f, dark, 0.5f * style.Hatching + 0.25f, k));
                }

                StampAll(strokes);
            }

            private Vector2 StrokeDirection(int cx, int cz, int k, Vector2 flat)
            {
                Vector2 w = World(cx, cz);
                if (k >= KeyFieldBase && data.Fields != null && data.Fields.Evaluate(w, out MapLabFields.Sample fs))
                {
                    float a = data.Fields.Block(fs.Block).Angle;
                    return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                }
                Vector2 down = SampleDownhill(w);
                float slope = data.SampleSlope(w.x, w.y);
                float strength = MapLabGeometry.SmoothStep(1.5f, 8f, slope);
                Vector2 along = k == KeyRock ? down : new Vector2(-down.y, down.x);
                if (Vector2.Dot(along, flat) < 0f) along = -along;
                Vector2 dir = (along * strength + flat * (1f - strength)).normalized;
                return dir.sqrMagnitude < 0.5f ? flat : dir;
            }

            private static Stroke Make(int x, int z, Vector2 dir, float length, float width, Color color, float alpha, int k)
            {
                return new Stroke { X = x, Z = z, DirX = dir.x, DirZ = dir.y, Length = length, Width = width, Alpha = alpha, Key = k, R = color.r, G = color.g, B = color.b };
            }

            // Полосы одной чётности не пересекаются (высота полосы ≥ 2 радиусов
            // мазка) и обрабатываются параллельно; порядок внутри полосы фиксирован.
            private void StampAll(List<Stroke> strokes)
            {
                float maxExtent = 4f;
                for (int i = 0; i < strokes.Count; i++)
                    maxExtent = Mathf.Max(maxExtent, strokes[i].Length * 0.5f + strokes[i].Width);
                int band = Mathf.CeilToInt(maxExtent * 2f) + 2;
                int bands = Mathf.CeilToInt(p / (float)band);
                List<int>[] byBand = new List<int>[bands];
                for (int i = 0; i < bands; i++)
                    byBand[i] = new List<int>();
                for (int i = 0; i < strokes.Count; i++)
                    byBand[Mathf.Min(bands - 1, strokes[i].Z / band)].Add(i);
                for (int parity = 0; parity < 2; parity++)
                {
                    int start = parity;
                    Parallel.For(0, (bands - start + 1) / 2, j =>
                    {
                        List<int> list = byBand[start + j * 2];
                        for (int k = 0; k < list.Count; k++)
                            Stamp(strokes[list[k]]);
                    });
                }
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

            private void Stamp(Stroke s)
            {
                float half = s.Length * 0.5f;
                float halfW = s.Width * 0.5f;
                int extent = Mathf.CeilToInt(half + halfW);
                int x0 = Mathf.Max(0, s.X - extent), x1 = Mathf.Min(p - 1, s.X + extent);
                int z0 = Mathf.Max(0, s.Z - extent), z1 = Mathf.Min(p - 1, s.Z + extent);
                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = z * p + x;
                        if (key[i] != s.Key)
                            continue;
                        float dx = x - s.X, dz = z - s.Z;
                        float along = dx * s.DirX + dz * s.DirZ;
                        if (along < -half || along > half)
                            continue;
                        float across = -dx * s.DirZ + dz * s.DirX;
                        float t = along / half;
                        // Мазок толще в начале и сходит на нет к концу.
                        float profile = halfW * (1f - 0.5f * (t * 0.5f + 0.5f));
                        float edge = 1f - MapLabGeometry.SmoothStep(profile - 0.6f, profile + 0.4f, Mathf.Abs(across));
                        if (edge <= 0f)
                            continue;
                        float a = s.Alpha * edge * (1f - t * t);
                        r[i] += (s.R - r[i]) * a;
                        g[i] += (s.G - g[i]) * a;
                        b[i] += (s.B - b[i]) * a;
                    }
                }
            }

            public Color32[] Pack()
            {
                Color32[] pixels = new Color32[r.Length];
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(r[i] * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(g[i] * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(b[i] * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(ink[i] * 255f), 0, 255));
                }
                return pixels;
            }
        }
    }
}
