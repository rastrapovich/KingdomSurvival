using System.Threading.Tasks;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Медленно меняющиеся поля покраски этапа 03 на общей сетке 4 отсчёта на
    // единицу мира: цвет луга и подлеска, маски поля и камня, шумы кромок
    // дорог и межей. Высокое разрешение запекания не пересчитывает шум в
    // каждом текселе — только читает эти поля билинейно. Те же поля дают кромку
    // дороги тушевым линиям (MapLabInk), поэтому линия и заливка совпадают.
    public sealed class MapLabPaintFields
    {
        public const int Channels = 19;
        public const float SamplesPerUnit = 4f;

        public struct Sample
        {
            public float R, G, B;
            public float FieldMask, WidthVar, FieldTone, Breakup;
            public float Rock, RockStreak;
            public float Above, WaterDistance;
            public float RoadWobble, Mottle16, Rut, Tuft;
            public float Forest;
            // Этап 04: поле акварельных пятен луга (−1..1 до порога) и выбор тона пятна.
            public float Blot, BlotHue;
            // Этап 04: расстояние до кромки главной дороги (для узлов троп).
            public float MainRoad;
        }

        private readonly float[] values;
        private readonly int n;
        private readonly float step;
        public float Milliseconds;

        public MapLabPaintFields(MapLabData data, MapLabSettings settings, MapLabPalette pal)
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            n = Mathf.CeilToInt(data.Size * SamplesPerUnit) + 1;
            step = data.Size / (n - 1);
            values = new float[n * n * Channels];
            MapLabNoise macro = Noise(data, "paint-macro");
            MapLabNoise macro2 = Noise(data, "paint-macro2");
            MapLabNoise mottle = Noise(data, "paint-mottle");
            MapLabNoise breakup = Noise(data, "paint-breakup");
            MapLabNoise rockStreak = Noise(data, "paint-rock");
            MapLabNoise fieldNoise = Noise(data, "paint-field");
            MapLabNoise roadWobble = Noise(data, "paint-road-wobble");
            MapLabNoise blots = Noise(data, "paint04-blots");
            MapLabNoise blotHue = Noise(data, "paint04-blot-hue");
            float blotScale = Mathf.Max(4f, settings.Stage04.BlotScale);
            MapLabStyleSettings style = settings.PainterlyStyle;
            float macroScale = Mathf.Max(5f, style.MacroScale);
            bool showForest = settings.Diagnostics.ShowForest;
            Vector3 light = settings.LightDirection;
            Parallel.For(0, n, z =>
            {
                for (int x = 0; x < n; x++)
                {
                    Vector2 w = new Vector2(x * step, z * step);
                    int o = (z * n + x) * Channels;
                    float h = data.SampleHeight(w.x, w.y);
                    Vector3 normal = data.SampleNormal(w.x, w.y);
                    float facing = Vector3.Dot(normal, light);
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
                    float forest = showForest ? data.SampleMask(data.Forest, w.x, w.y) : 0f;
                    if (forest > 0.02f)
                        c = Color.Lerp(c, pal.ForestFloor, Mathf.Pow(Mathf.Clamp01(forest * 1.25f), 0.7f) * 0.94f);
                    values[o + 0] = c.r;
                    values[o + 1] = c.g;
                    values[o + 2] = c.b;
                    float cell = data.Cell * 0.75f;
                    float soft = (data.SampleMask(data.Field, w.x - cell, w.y - cell) + data.SampleMask(data.Field, w.x + cell, w.y - cell)
                                  + data.SampleMask(data.Field, w.x - cell, w.y + cell) + data.SampleMask(data.Field, w.x + cell, w.y + cell)) * 0.25f;
                    values[o + 3] = soft + mottle.Fbm(w.x / 9f, w.y / 9f, 2) * 0.12f;
                    values[o + 4] = Mathf.Lerp(0.3f, 1.5f, MapLabGeometry.SmoothStep(0.25f, 0.75f, fieldNoise.Fbm(w.x / 27f + 3.1f, w.y / 27f, 2) * 0.5f + 0.5f));
                    values[o + 5] = fieldNoise.Fbm(w.x / 14f, w.y / 14f, 2) * 0.06f;
                    values[o + 6] = breakup.Fbm(w.x / 5f, w.y / 5f, 2) * 0.5f + 0.5f;
                    values[o + 7] = data.SampleMask(data.Rock, w.x, w.y);
                    values[o + 8] = rockStreak.Fbm(w.x / 2.2f, w.y / 9f, 3) * 0.5f + 0.5f;
                    float level = data.SampleWaterLevel(w.x, w.y);
                    values[o + 9] = float.IsNaN(level) ? 100f : h - level;
                    values[o + 10] = data.WaterDistance != null ? Mathf.Min(1000f, data.Bilinear(data.WaterDistance, w.x, w.y)) : 1000f;
                    values[o + 11] = roadWobble.Fbm(w.x / 17f, w.y / 17f, 2);
                    values[o + 12] = mottle.Fbm(w.x / 1.6f, w.y / 1.6f, 2);
                    values[o + 13] = mottle.Fbm(w.x / 3f, w.y / 3f, 2);
                    values[o + 14] = breakup.Fbm(w.x / 2.4f + 11f, w.y / 2.4f, 2) * 0.5f + 0.5f;
                    values[o + 15] = forest;
                    values[o + 16] = blots.Fbm(w.x / blotScale, w.y / blotScale, 3) + blots.Fbm(w.x / (blotScale * 0.31f) + 7.3f, w.y / (blotScale * 0.31f), 2) * 0.22f;
                    values[o + 17] = blotHue.Fbm(w.x / (blotScale * 2.3f), w.y / (blotScale * 2.3f), 2);
                }
            });
            FillMainRoad(data, settings);
            Milliseconds = (float)watch.Elapsed.TotalMilliseconds;
        }

        // Расстояние до кромки главных дорог на той же сетке, до длины перехода
        // узла: одно поле для заливки и пера — переход совпадает и не обрывается.
        private void FillMainRoad(MapLabData data, MapLabSettings settings)
        {
            float limit = (settings.IsStage04 ? settings.Stage04.JunctionLength : 0f) + 1f;
            for (int i = 0; i < n * n; i++)
                values[i * Channels + 18] = limit;
            if (!settings.IsStage04)
                return;
            float inv = 1f / step;
            foreach (MapLabPolyline road in data.Roads)
            {
                if (road.Kind == MapLabRoadKind.Path)
                    continue;
                float half = road.Width * 0.5f;
                float reach = half + limit;
                for (int s = 0; s < road.Points.Count - 1; s++)
                {
                    Vector2 a = road.Points[s], c = road.Points[s + 1];
                    if (MapLabTiledPainter.OnBridge(data, (a + c) * 0.5f))
                        continue;
                    int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, c.x) - reach) * inv));
                    int x1 = Mathf.Min(n - 1, Mathf.CeilToInt((Mathf.Max(a.x, c.x) + reach) * inv));
                    int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, c.y) - reach) * inv));
                    int z1 = Mathf.Min(n - 1, Mathf.CeilToInt((Mathf.Max(a.y, c.y) + reach) * inv));
                    for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float d = Mathf.Max(0f, MapLabGeometry.DistanceToSegment(new Vector2(x * step, z * step), a, c, out _) - half);
                        int o = (z * n + x) * Channels + 18;
                        if (d < values[o])
                            values[o] = d;
                    }
                }
            }
        }

        public float MainRoadAt(Vector2 w)
        {
            Get(w, out Sample s);
            return s.MainRoad;
        }

        private static MapLabNoise Noise(MapLabData data, string name)
        {
            return new MapLabNoise(MapLabRandom.StreamSeed(data.Seed, name, MapLabData.AlgorithmVersion));
        }

        public void Get(Vector2 w, out Sample s)
        {
            float gx = Mathf.Clamp(w.x / step, 0f, n - 1.001f);
            float gz = Mathf.Clamp(w.y / step, 0f, n - 1.001f);
            int x0 = (int)gx, z0 = (int)gz;
            float fx = gx - x0, fz = gz - z0;
            float w00 = (1f - fx) * (1f - fz), w10 = fx * (1f - fz), w01 = (1f - fx) * fz, w11 = fx * fz;
            int a = (z0 * n + x0) * Channels, b = a + Channels, c = a + n * Channels, d = c + Channels;
            float V(int k) => values[a + k] * w00 + values[b + k] * w10 + values[c + k] * w01 + values[d + k] * w11;
            s.R = V(0); s.G = V(1); s.B = V(2);
            s.FieldMask = V(3); s.WidthVar = V(4); s.FieldTone = V(5); s.Breakup = V(6);
            s.Rock = V(7); s.RockStreak = V(8);
            s.Above = V(9); s.WaterDistance = V(10);
            s.RoadWobble = V(11); s.Mottle16 = V(12); s.Rut = V(13); s.Tuft = V(14);
            s.Forest = V(15);
            s.Blot = V(16);
            s.BlotHue = V(17);
            s.MainRoad = V(18);
        }

        // Сдвиг кромки полотна дороги (ед.): неровная, но чёткая граница. Ноль
        // функции distance + RoadEdgeOffset — видимый край заливки и линия пера.
        public float RoadEdgeOffset(Vector2 w, bool path)
        {
            Get(w, out Sample s);
            return RoadEdgeOffset(s, path);
        }

        public static float RoadEdgeOffset(in Sample s, bool path)
        {
            return RoadEdgeOffset(s, path ? 1f : 0f);
        }

        // pathness: 1 — тропа, 0 — дорога; у узла этапа 04 — плавно между ними.
        public static float RoadEdgeOffset(in Sample s, float pathness)
        {
            return s.RoadWobble * Mathf.Lerp(0.5f, 0.35f, pathness) + s.Mottle16 * 0.18f;
        }

        public float RoadEdgeOffset(Vector2 w, float pathness)
        {
            Get(w, out Sample s);
            return RoadEdgeOffset(s, pathness);
        }
    }
}
