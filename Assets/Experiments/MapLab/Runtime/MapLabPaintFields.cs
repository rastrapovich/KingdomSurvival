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

        // Этап 05: общая сглаженная граница берега (канал Above берётся из неё).
        public readonly MapLabShore Shore;

        // 12У-C: поля хранятся блоками BlockSize × BlockSize отсчётов и считаются
        // по требованию (потокобезопасно); сверх бюджета давно взятые блоки
        // вытесняются и при следующем обращении считаются заново тем же кодом —
        // значения не зависят от порядка и подгрузки. Сглаживание маски полей
        // считается с полями Apron вокруг блока (три прохода радиуса 3), поэтому
        // ядро блока не зависит от соседей; у края карты — повтор крайнего
        // отсчёта. Малые карты считаются целиком сразу.
        private const int BlockSize = 128, Apron = 9, BlurRadius = 3;
        private readonly int n;
        private readonly float step;
        private readonly int blocksPerSide;
        private readonly float[][] blocks;
        private readonly object[] locks;
        private readonly System.Collections.Concurrent.ConcurrentQueue<int> resident = new System.Collections.Concurrent.ConcurrentQueue<int>();
        private int residentCount;
        private readonly int maxResident;
        public float Milliseconds;

        private readonly MapLabData data;
        private readonly MapLabPalette pal;
        private readonly MapLabNoise macro, macro2, mottle, breakup, rockStreak, fieldNoise, roadWobble, blots, blotHue;
        private readonly float blotScale, macroScale, mainRoadLimit;
        private readonly bool showForest, stage05, stage04;
        private readonly Vector3 light;

        // Бюджет памяти полей, МБ (больше — вытеснение давних блоков). 0 — авто:
        // четверть ОЗУ машины в пределах 1,5–16 ГБ.
        public static int BudgetMb;

        private static int AutoBudgetMb() => Mathf.Clamp(SystemInfo.systemMemorySize / 4, 1536, 16384);

        public int ResidentBlocks => residentCount;
        public int TotalBlocks => blocks.Length;

        public MapLabPaintFields(MapLabData data, MapLabSettings settings, MapLabPalette pal)
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            this.data = data;
            this.pal = pal;
            n = Mathf.CeilToInt(data.Size * SamplesPerUnit) + 1;
            step = data.Size / (n - 1);
            blocksPerSide = (n + BlockSize - 1) / BlockSize;
            blocks = new float[blocksPerSide * blocksPerSide][];
            locks = new object[blocks.Length];
            for (int i = 0; i < locks.Length; i++)
                locks[i] = new object();
            long blockBytes = (long)BlockSize * BlockSize * Channels * 4;
            int budget = BudgetMb > 0 ? BudgetMb : AutoBudgetMb();
            maxResident = Mathf.Max(16, (int)(budget * 1048576L / blockBytes));
            macro = Noise(data, "paint-macro");
            macro2 = Noise(data, "paint-macro2");
            mottle = Noise(data, "paint-mottle");
            breakup = Noise(data, "paint-breakup");
            rockStreak = Noise(data, "paint-rock");
            fieldNoise = Noise(data, "paint-field");
            roadWobble = Noise(data, "paint-road-wobble");
            blots = Noise(data, "paint04-blots");
            blotHue = Noise(data, "paint04-blot-hue");
            blotScale = Mathf.Max(4f, settings.Stage04.BlotScale);
            macroScale = Mathf.Max(5f, settings.PainterlyStyle.MacroScale);
            showForest = settings.Diagnostics.ShowForest;
            light = settings.LightDirection;
            stage05 = settings.IsStage05;
            stage04 = settings.IsStage04;
            mainRoadLimit = (stage04 ? settings.Stage04.JunctionLength : 0f) + 1f;
            if (stage05)
                Shore = new MapLabShore(data, settings.Stage05.ShoreSmoothing);
            if (blocks.Length <= maxResident)
                Parallel.For(0, blocks.Length, b => Ensure(b));
            Milliseconds = (float)watch.Elapsed.TotalMilliseconds;
        }

        private float[] Ensure(int b)
        {
            float[] arr = System.Threading.Volatile.Read(ref blocks[b]);
            if (arr != null)
                return arr;
            lock (locks[b])
            {
                arr = blocks[b];
                if (arr != null)
                    return arr;
                arr = ComputeBlock(b);
                System.Threading.Volatile.Write(ref blocks[b], arr);
                resident.Enqueue(b);
                if (System.Threading.Interlocked.Increment(ref residentCount) > maxResident)
                    Evict(b);
                return arr;
            }
        }

        private void Evict(int keep)
        {
            int guard = resident.Count;
            while (System.Threading.Volatile.Read(ref residentCount) > maxResident && guard-- > 0 && resident.TryDequeue(out int old))
            {
                if (old == keep)
                {
                    resident.Enqueue(old);
                    continue;
                }
                System.Threading.Volatile.Write(ref blocks[old], null);
                System.Threading.Interlocked.Decrement(ref residentCount);
            }
        }

        private float[] ComputeBlock(int b)
        {
            int bx = b % blocksPerSide, bz = b / blocksPerSide;
            int x0 = bx * BlockSize, z0 = bz * BlockSize;
            int bw = Mathf.Min(BlockSize, n - x0), bh = Mathf.Min(BlockSize, n - z0);
            float[] dst = new float[bw * bh * Channels];
            MapLabShore shore = Shore;
            for (int z = z0; z < z0 + bh; z++)
            {
                for (int x = x0; x < x0 + bw; x++)
                {
                    Vector2 w = new Vector2(x * step, z * step);
                    int o = ((z - z0) * bw + (x - x0)) * Channels;
                    ComputeSample(w, dst, o, shore);
                }
            }
            FillMainRoad(dst, x0, z0, bw, bh);
            if (stage05)
                SmoothFieldMask(dst, x0, z0, bw, bh);
            return dst;
        }

        private void ComputeSample(Vector2 w, float[] dst, int o, MapLabShore shore)
        {
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
                c = Color.Lerp(c, pal.ForestFloor, Mathf.Pow(Mathf.Clamp01(forest * 1.25f), 0.7f) * 0.82f);
            dst[o + 0] = c.r;
            dst[o + 1] = c.g;
            dst[o + 2] = c.b;
            float cell = data.Cell * 0.75f;
            float soft = (data.SampleMask(data.Field, w.x - cell, w.y - cell) + data.SampleMask(data.Field, w.x + cell, w.y - cell)
                          + data.SampleMask(data.Field, w.x - cell, w.y + cell) + data.SampleMask(data.Field, w.x + cell, w.y + cell)) * 0.25f;
            // R03: без мелкого шума кромки (частые волны) — край поля даёт сглаженная маска ниже.
            dst[o + 3] = shore != null ? soft : soft + mottle.Fbm(w.x / 9f, w.y / 9f, 2) * 0.12f;
            dst[o + 4] = Mathf.Lerp(0.3f, 1.5f, MapLabGeometry.SmoothStep(0.25f, 0.75f, fieldNoise.Fbm(w.x / 27f + 3.1f, w.y / 27f, 2) * 0.5f + 0.5f));
            dst[o + 5] = fieldNoise.Fbm(w.x / 14f, w.y / 14f, 2) * 0.06f;
            dst[o + 6] = breakup.Fbm(w.x / 5f, w.y / 5f, 2) * 0.5f + 0.5f;
            dst[o + 7] = data.SampleMask(data.Rock, w.x, w.y);
            dst[o + 8] = rockStreak.Fbm(w.x / 2.2f, w.y / 9f, 3) * 0.5f + 0.5f;
            if (shore != null)
            {
                dst[o + 9] = shore.Sample(w);
            }
            else
            {
                float level = data.SampleWaterLevel(w.x, w.y);
                dst[o + 9] = float.IsNaN(level) ? 100f : h - level;
            }
            dst[o + 10] = data.WaterDistance != null ? Mathf.Min(1000f, data.Bilinear(data.WaterDistance, w.x, w.y)) : 1000f;
            dst[o + 11] = roadWobble.Fbm(w.x / 17f, w.y / 17f, 2);
            dst[o + 12] = mottle.Fbm(w.x / 1.6f, w.y / 1.6f, 2);
            dst[o + 13] = mottle.Fbm(w.x / 3f, w.y / 3f, 2);
            dst[o + 14] = breakup.Fbm(w.x / 2.4f + 11f, w.y / 2.4f, 2) * 0.5f + 0.5f;
            dst[o + 15] = forest;
            dst[o + 16] = blots.Fbm(w.x / blotScale, w.y / blotScale, 3) + blots.Fbm(w.x / (blotScale * 0.31f) + 7.3f, w.y / (blotScale * 0.31f), 2) * 0.22f;
            dst[o + 17] = blotHue.Fbm(w.x / (blotScale * 2.3f), w.y / (blotScale * 2.3f), 2);
        }

        // Отсчёт сглаживаемой маски поля (канал 3) до сглаживания — как в Sample.
        private float RawFieldMask(int x, int z)
        {
            Vector2 w = new Vector2(x * step, z * step);
            float cell = data.Cell * 0.75f;
            float soft = (data.SampleMask(data.Field, w.x - cell, w.y - cell) + data.SampleMask(data.Field, w.x + cell, w.y - cell)
                          + data.SampleMask(data.Field, w.x - cell, w.y + cell) + data.SampleMask(data.Field, w.x + cell, w.y + cell)) * 0.25f;
            return Shore != null ? soft : soft + mottle.Fbm(w.x / 9f, w.y / 9f, 2) * 0.12f;
        }

        // R03: одна плавная граница поля. Маска полей идёт с сетки рельефа (1 ед.):
        // по диагонали её край — ступеньки «гармошки». Три прохода раздельного
        // box-фильтра радиусом 0.75 ед. (≈ гаусс σ ≈ 0.75 ед.) дают спокойные
        // дуги. Блок считается с полями Apron = 3 × радиус: ядро не зависит от
        // соседних блоков; у края карты индекс повторяет крайний отсчёт.
        private void SmoothFieldMask(float[] dst, int x0, int z0, int bw, int bh)
        {
            const int channel = 3;
            int ax0 = Mathf.Max(0, x0 - Apron), az0 = Mathf.Max(0, z0 - Apron);
            int ax1 = Mathf.Min(n, x0 + bw + Apron), az1 = Mathf.Min(n, z0 + bh + Apron);
            int aw = ax1 - ax0, ah = az1 - az0;
            float[] a = new float[aw * ah], b = new float[aw * ah];
            for (int z = 0; z < ah; z++)
                for (int x = 0; x < aw; x++)
                    a[z * aw + x] = RawFieldMask(ax0 + x, az0 + z);
            float inv = 1f / (BlurRadius * 2 + 1);
            for (int pass = 0; pass < 3; pass++)
            {
                for (int z = 0; z < ah; z++)
                    for (int x = 0; x < aw; x++)
                    {
                        float sum = 0f;
                        for (int k = -BlurRadius; k <= BlurRadius; k++)
                            sum += a[z * aw + Local(ax0 + x + k, ax0, aw)];
                        b[z * aw + x] = sum * inv;
                    }
                for (int z = 0; z < ah; z++)
                    for (int x = 0; x < aw; x++)
                    {
                        float sum = 0f;
                        for (int k = -BlurRadius; k <= BlurRadius; k++)
                            sum += b[Local(az0 + z + k, az0, ah) * aw + x];
                        a[z * aw + x] = sum * inv;
                    }
            }
            for (int z = 0; z < bh; z++)
                for (int x = 0; x < bw; x++)
                    dst[(z * bw + x) * Channels + channel] = a[(z0 - az0 + z) * aw + (x0 - ax0 + x)];
        }

        // Индекс в полях блока: глобальный — с повтором края карты, затем в поля.
        private int Local(int g, int origin, int size)
        {
            return Mathf.Clamp(Mathf.Clamp(g, 0, n - 1) - origin, 0, size - 1);
        }

        // R03: знаковое расстояние до края жёлтой заливки поля (ед.; > 0 — внутри
        // поля): внешний край — плавная маска, внутренний — край межи у каждого
        // поля (межа — полоса луга между полями, её ось границей не служит).
        public static float FieldEdge(in Sample s, float borderDistance)
        {
            float outer = (s.FieldMask - 0.5f) * 2.6f;
            float inner = borderDistance - BalkHalf(s);
            return Mathf.Min(outer, inner);
        }

        public static float BalkHalf(in Sample s) => 0.45f * Mathf.Clamp(s.WidthVar, 0.3f, 1.5f) + 0.12f;

        // Расстояние до кромки главных дорог на той же сетке, до длины перехода
        // узла: одно поле для заливки и пера — переход совпадает и не обрывается.
        private void FillMainRoad(float[] dst, int bx0, int bz0, int bw, int bh)
        {
            for (int i = 0; i < bw * bh; i++)
                dst[i * Channels + 18] = mainRoadLimit;
            if (!stage04)
                return;
            float inv = 1f / step;
            foreach (MapLabPolyline road in data.Roads)
            {
                if (road.Kind == MapLabRoadKind.Path)
                    continue;
                float half = road.Width * 0.5f;
                float reach = half + mainRoadLimit;
                for (int s = 0; s < road.Points.Count - 1; s++)
                {
                    Vector2 a = road.Points[s], c = road.Points[s + 1];
                    int x0 = Mathf.Max(bx0, Mathf.FloorToInt((Mathf.Min(a.x, c.x) - reach) * inv));
                    int x1 = Mathf.Min(bx0 + bw - 1, Mathf.CeilToInt((Mathf.Max(a.x, c.x) + reach) * inv));
                    int z0 = Mathf.Max(bz0, Mathf.FloorToInt((Mathf.Min(a.y, c.y) - reach) * inv));
                    int z1 = Mathf.Min(bz0 + bh - 1, Mathf.CeilToInt((Mathf.Max(a.y, c.y) + reach) * inv));
                    if (x0 > x1 || z0 > z1 || MapLabTiledPainter.OnBridge(data, (a + c) * 0.5f))
                        continue;
                    for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float d = Mathf.Max(0f, MapLabGeometry.DistanceToSegment(new Vector2(x * step, z * step), a, c, out _) - half);
                        int o = ((z - bz0) * bw + (x - bx0)) * Channels + 18;
                        if (d < dst[o])
                            dst[o] = d;
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

        private int Offset(int x, int z, out float[] arr)
        {
            int bx = x / BlockSize, bz = z / BlockSize;
            arr = Ensure(bz * blocksPerSide + bx);
            int bw = Mathf.Min(BlockSize, n - bx * BlockSize);
            return ((z - bz * BlockSize) * bw + (x - bx * BlockSize)) * Channels;
        }

        public void Get(Vector2 w, out Sample s)
        {
            float gx = Mathf.Clamp(w.x / step, 0f, n - 1.001f);
            float gz = Mathf.Clamp(w.y / step, 0f, n - 1.001f);
            int x0 = (int)gx, z0 = (int)gz;
            float fx = gx - x0, fz = gz - z0;
            float w00 = (1f - fx) * (1f - fz), w10 = fx * (1f - fz), w01 = (1f - fx) * fz, w11 = fx * fz;
            int a = Offset(x0, z0, out float[] va), b = Offset(x0 + 1, z0, out float[] vb), c = Offset(x0, z0 + 1, out float[] vc), d = Offset(x0 + 1, z0 + 1, out float[] vd);
            float V(int k) => va[a + k] * w00 + vb[b + k] * w10 + vc[c + k] * w01 + vd[d + k] * w11;
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
