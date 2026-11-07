using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KingdomSurvival.MapLab
{
    // Тушевые кромки дорог и троп (этап 03). Сначала все полосы объединяются в
    // одно поле «расстояние до ближайшей дороги + неровность кромки», потом
    // обводится его нулевая изолиния (marching squares) — на развилках
    // получается внешний периметр без внутренних швов и двойных линий. Та же
    // функция кромки даёт край заливки в MapLabTiledPainter, поэтому линия
    // лежит ровно по краю полотна. Лента лежит на рельефе; толщину в пикселях
    // и рваность края задаёт шейдер MapLabInk по общему профилю пера.
    public static class MapLabInk
    {
        public const float GridStep = 0.2f;

        public struct Stats
        {
            public int Polylines;
            public int Vertices;
            public float Length;
            public int FreeEnds;        // подлинные свободные концы видимых следов
            public int Endings;         // из них с завершением (§2.5)
            public int EndingElements;  // штрихов и точек завершений
        }

        // Геометрия ленты без Unity-объектов: считается в любом потоке, меш
        // создаётся в главном (пошаговая сборка карты в игре).
        public sealed class MeshData
        {
            public string Name;
            public List<Vector3> Positions;
            public List<Vector2> Uv0, Uv1;
            public List<Vector4> Uv2;
            public List<int> Indices;

            public Mesh ToMesh()
            {
                Mesh mesh = new Mesh { name = Name, hideFlags = HideFlags.DontSave };
                if (Positions.Count > 65000)
                    mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(Positions);
                mesh.SetUVs(0, Uv0);
                mesh.SetUVs(1, Uv1);
                mesh.SetUVs(2, Uv2);
                mesh.SetTriangles(Indices, 0, true);
                Bounds bounds = mesh.bounds;
                bounds.Expand(2f);
                mesh.bounds = bounds;
                return mesh;
            }
        }

        public sealed class Contour
        {
            public readonly List<Vector2> Points = new List<Vector2>();
            public readonly List<float> Weights = new List<float>();
            public bool Closed;
            // Концы массива — художественные концы штриха (борозды), а не край карты.
            public bool FreeEnds;
            // Доля подходящих концов с завершением (малые акценты — реже).
            public float EndingChance = 0.85f;
            // Вершины, занятые чужой формой (межа у полотна дороги) — примыкание,
            // не свободный конец (null — нет).
            public List<bool> Hold;
        }

        // Поле кромки на сетке GridStep: значение < 0 — на полотне.
        // Сначала главные дороги (с расстоянием до их кромки), затем тропы с
        // устьем у узла — та же форма, что у заливки MapLabTiledPainter.
        public static float[] RoadField(MapLabData data, MapLabPaintFields fields, Vector4 weights, out int n, out float[] weightField, MapLabRoadJunction junction = default)
        {
            n = Mathf.CeilToInt(data.Size / GridStep) + 1;
            float[] f = new float[n * n];
            weightField = new float[n * n];
            byte[] kind = new byte[n * n];
            for (int i = 0; i < f.Length; i++)
                f[i] = 9f;
            List<MapLabPolyline> roads = new List<MapLabPolyline>();
            foreach (MapLabPolyline r in data.Roads)
                if (r.Kind != MapLabRoadKind.Path)
                    roads.Add(r);
            foreach (MapLabPolyline r in data.Roads)
                if (r.Kind == MapLabRoadKind.Path)
                    roads.Add(r);
            roads.AddRange(data.YardPaths);
            float inv = 1f / GridStep;
            int size = n;
            foreach (MapLabPolyline road in roads)
            {
                bool isPath = road.Kind == MapLabRoadKind.Path;
                float half = road.Width * 0.5f;
                float reach = half + 1.2f + (isPath ? junction.Mouth : 0f);
                for (int s = 0; s < road.Points.Count - 1; s++)
                {
                    Vector2 a = road.Points[s], c = road.Points[s + 1];
                    if (MapLabTiledPainter.OnBridge(data, (a + c) * 0.5f))
                        continue;
                    int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, c.x) - reach) * inv));
                    int x1 = Mathf.Min(size - 1, Mathf.CeilToInt((Mathf.Max(a.x, c.x) + reach) * inv));
                    int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, c.y) - reach) * inv));
                    int z1 = Mathf.Min(size - 1, Mathf.CeilToInt((Mathf.Max(a.y, c.y) + reach) * inv));
                    for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float d = MapLabGeometry.DistanceToSegment(new Vector2(x * GridStep, z * GridStep), a, c, out _) - half;
                        int i = z * size + x;
                        if (isPath && junction.Mouth > 0f && d < junction.Mouth + 0.5f)
                            d -= junction.MouthAt(fields.MainRoadAt(new Vector2(x * GridStep, z * GridStep)));
                        if (d < f[i])
                        {
                            f[i] = d;
                            kind[i] = (byte)road.Kind;
                        }
                    }
                }
            }
            for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                int i = z * size + x;
                bool path = kind[i] == (byte)MapLabRoadKind.Path;
                float pathness = path ? 1f - junction.BlendAt(fields.MainRoadAt(new Vector2(x * GridStep, z * GridStep))) : 0f;
                weightField[i] = Mathf.Lerp(weights.x, weights.y, pathness);
                if (f[i] > 1.6f)
                    continue;
                f[i] += fields.RoadEdgeOffset(new Vector2(x * GridStep, z * GridStep), pathness);
            }
            return f;
        }

        // Нулевая изолиния поля: сегменты по клеткам, затем сборка в ломаные
        // по общим рёбрам сетки (ключ ребра одинаков у соседних клеток).
        public static List<Contour> Trace(float[] f, float[] weightField, int n, float step)
        {
            List<Vector2> segA = new List<Vector2>(), segB = new List<Vector2>();
            List<long> keyA = new List<long>(), keyB = new List<long>();
            Vector2 Cross(int ax, int az, int bx, int bz)
            {
                float va = f[az * n + ax], vb = f[bz * n + bx];
                float t = Mathf.Clamp01(va / (va - vb));
                return new Vector2(Mathf.Lerp(ax, bx, t) * step, Mathf.Lerp(az, bz, t) * step);
            }
            // Рёбра: 0 — низ (x,z)-(x+1,z), 1 — право, 2 — верх, 3 — лево.
            long EdgeKey(int x, int z, int e)
            {
                switch (e)
                {
                    case 0: return ((long)z * n + x) * 2;
                    case 1: return ((long)z * n + x + 1) * 2 + 1;
                    case 2: return ((long)(z + 1) * n + x) * 2;
                    default: return ((long)z * n + x) * 2 + 1;
                }
            }
            Vector2 EdgePoint(int x, int z, int e)
            {
                switch (e)
                {
                    case 0: return Cross(x, z, x + 1, z);
                    case 1: return Cross(x + 1, z, x + 1, z + 1);
                    case 2: return Cross(x, z + 1, x + 1, z + 1);
                    default: return Cross(x, z, x, z + 1);
                }
            }
            void Add(int x, int z, int e0, int e1)
            {
                segA.Add(EdgePoint(x, z, e0));
                segB.Add(EdgePoint(x, z, e1));
                keyA.Add(EdgeKey(x, z, e0));
                keyB.Add(EdgeKey(x, z, e1));
            }
            for (int z = 0; z < n - 1; z++)
            for (int x = 0; x < n - 1; x++)
            {
                float v0 = f[z * n + x], v1 = f[z * n + x + 1], v2 = f[(z + 1) * n + x + 1], v3 = f[(z + 1) * n + x];
                if (v0 > 1.5f && v1 > 1.5f && v2 > 1.5f && v3 > 1.5f)
                    continue;
                int c = (v0 < 0f ? 1 : 0) | (v1 < 0f ? 2 : 0) | (v2 < 0f ? 4 : 0) | (v3 < 0f ? 8 : 0);
                switch (c)
                {
                    case 0: case 15: break;
                    case 1: case 14: Add(x, z, 3, 0); break;
                    case 2: case 13: Add(x, z, 0, 1); break;
                    case 3: case 12: Add(x, z, 3, 1); break;
                    case 4: case 11: Add(x, z, 1, 2); break;
                    case 6: case 9: Add(x, z, 0, 2); break;
                    case 7: case 8: Add(x, z, 3, 2); break;
                    case 5:
                    case 10:
                        // Седло: решает среднее по клетке.
                        bool centerInside = (v0 + v1 + v2 + v3) * 0.25f < 0f;
                        if ((c == 5) == centerInside)
                        {
                            Add(x, z, 3, 2);
                            Add(x, z, 0, 1);
                        }
                        else
                        {
                            Add(x, z, 3, 0);
                            Add(x, z, 1, 2);
                        }
                        break;
                }
            }
            // Сборка: у каждого ребра не больше двух сегментов.
            Dictionary<long, List<int>> byKey = new Dictionary<long, List<int>>();
            void Link(long key, int seg)
            {
                if (!byKey.TryGetValue(key, out List<int> list))
                    byKey[key] = list = new List<int>(2);
                list.Add(seg);
            }
            for (int i = 0; i < segA.Count; i++)
            {
                Link(keyA[i], i);
                Link(keyB[i], i);
            }
            bool[] used = new bool[segA.Count];
            List<Contour> contours = new List<Contour>();
            for (int start = 0; start < segA.Count; start++)
            {
                if (used[start])
                    continue;
                used[start] = true;
                LinkedList<(Vector2 p, long key)> chain = new LinkedList<(Vector2, long)>();
                chain.AddLast((segA[start], keyA[start]));
                chain.AddLast((segB[start], keyB[start]));
                for (int dir = 0; dir < 2; dir++)
                {
                    while (true)
                    {
                        long key = dir == 0 ? chain.Last.Value.key : chain.First.Value.key;
                        int next = -1;
                        foreach (int s in byKey[key])
                            if (!used[s])
                            {
                                next = s;
                                break;
                            }
                        if (next < 0)
                            break;
                        used[next] = true;
                        bool forward = keyA[next] == key;
                        (Vector2, long) item = forward ? (segB[next], keyB[next]) : (segA[next], keyA[next]);
                        if (dir == 0) chain.AddLast(item); else chain.AddFirst(item);
                    }
                }
                Contour contour = new Contour { Closed = chain.First.Value.key == chain.Last.Value.key };
                foreach ((Vector2 p, long key) item in chain)
                {
                    Vector2 p = item.p;
                    contour.Points.Add(p);
                    int gx = Mathf.Clamp(Mathf.RoundToInt(p.x / step), 0, n - 1), gz = Mathf.Clamp(Mathf.RoundToInt(p.y / step), 0, n - 1);
                    contour.Weights.Add(weightField[gz * n + gx]);
                }
                if (contour.Points.Count >= 3)
                    contours.Add(contour);
            }
            return contours;
        }

        // Лента пера по контурам: середина в вершине, сторона — в uv1, в uv0 —
        // (сторона −1..1, непрозрачность). Разрывы и ослабление на освещённой
        // стороне — стабильные, от мировых координат и seed.
        // Этап 05: единый профиль пера — тёплая тёмно-коричневая тушь, толщина
        // и её пределы в пикселях подняты вместе (рост толщины не съедается
        // верхним пределом), плотность пигмента, короткие разрывы, зерно.
        public static MapLabInkStyle Style(MapLabSettings settings)
        {
            if (!settings.IsStage05)
                return settings.Stage03.InkStyle;
            MapLabStage05Settings s5 = settings.Stage05;
            MapLabInkStyle s3 = settings.Stage03.InkStyle;
            return new MapLabInkStyle
            {
                Color = s5.InkColor,
                Width = s5.InkWidth,
                MinPixels = s5.InkMinPixels,
                MaxPixels = Mathf.Max(s5.InkMinPixels + 0.5f, s5.InkMaxPixels),
                Opacity = s5.InkDensity,
                Grain = s5.InkGrain,
                BreakLength = s5.InkBreakLength,
                BreakFrequency = s5.InkBreakShare,
                LitSideFade = 0.25f,
                Weights = new Vector4(1f, 0.72f, s3.Weights.z, s3.Weights.w),
            };
        }

        public static Mesh BuildRoadInk(MapLabData data, MapLabSettings settings, MapLabPaintFields fields, out Stats stats) =>
            RoadInkData(data, settings, fields, out stats)?.ToMesh();

        public static MeshData RoadInkData(MapLabData data, MapLabSettings settings, MapLabPaintFields fields, out Stats stats)
        {
            MapLabInkStyle ink = Style(settings);
            float[] f = RoadField(data, fields, ink.Weights, out int n, out float[] weights, MapLabRoadJunction.From(settings));
            List<Contour> contours = Trace(f, weights, n, GridStep);
            return BuildRibbonData(data, settings, contours, f, n, "MapLab Road Ink", out stats, ink, GridStep);
        }

        // Этап 05: перо берега по той же сглаженной границе, что береговая полоса
        // и край воды (MapLabShore). Выбранные участки (мировой шум), концы
        // участков сходят на нет; лента — синяя к воде, тёплая к суше (шейдер).
        public static Mesh BuildShoreInk(MapLabData data, MapLabSettings settings, MapLabShore shore, out Stats stats) =>
            ShoreInkData(data, settings, shore, out stats)?.ToMesh();

        public static MeshData ShoreInkData(MapLabData data, MapLabSettings settings, MapLabShore shore, out Stats stats)
        {
            stats = default;
            if (shore == null)
                return null;
            float[] weights = new float[shore.Above.Length];
            for (int i = 0; i < weights.Length; i++)
                weights[i] = 1f;
            List<Contour> contours = Trace(shore.Above, weights, shore.N, MapLabShore.Step);
            MapLabNoise pick = new MapLabNoise(MapLabRandom.StreamSeed(data.Seed, "ink05-shore", MapLabData.AlgorithmVersion));
            float share = Mathf.Clamp01(settings.Stage05.ShoreInkShare);
            List<Contour> kept = new List<Contour>();
            foreach (Contour c in contours)
            {
                // Короткие замкнутые кольца (лужицы у кромки) — без пера.
                float length = 0f;
                for (int i = 1; i < c.Points.Count; i++)
                    length += Vector2.Distance(c.Points[i - 1], c.Points[i]);
                if (length < 3f)
                    continue;
                for (int i = 0; i < c.Points.Count; i++)
                {
                    Vector2 p = c.Points[i];
                    float v = pick.Fbm(p.x / 13f, p.y / 13f, 2) * 0.5f + 0.5f;
                    c.Weights[i] = MapLabGeometry.SmoothStep(1f - share - 0.06f, 1f - share + 0.06f, v);
                }
                kept.Add(c);
            }
            MapLabInkStyle style = Style(settings);
            style.Weights = Vector4.one;
            return BuildRibbonData(data, settings, kept, shore.Above, shore.N, "MapLab Shore Ink", out stats, style, MapLabShore.Step, -0.02f);
        }

        // R03: перо полей — тот же профиль и та же лента, что у дороги, ровно по
        // краю жёлтой заливки (общая функция MapLabPaintFields.FieldEdge: внешний
        // край — сглаженная маска, внутренний — край межи у каждого поля; ось
        // межи границей не служит). У дорог линия гаснет (ложной поперечной
        // перегородки нет), часть участков — потерянные края. Плюс редкие короткие
        // штрихи внутри полей вдоль борозд: группы и большие пустые места.
        public const float FieldStep = 0.25f;
        public static int LastFieldInner, LastFieldStrips;

        private static float MaxWeight(Contour c)
        {
            float m = 0f;
            foreach (float v in c.Weights)
                m = Mathf.Max(m, v);
            return m;
        }

        private static int ToneClass(float t) => t < 0.36f ? 0 : t < 0.64f ? 1 : t < 0.86f ? 2 : 3;

        // Билинейная выборка поля на сетке step (n × n), за пределами — край.
        private static float SampleGrid(float[] f, int n, float step, Vector2 p)
        {
            float gx = Mathf.Clamp(p.x / step, 0f, n - 1.001f), gz = Mathf.Clamp(p.y / step, 0f, n - 1.001f);
            int x = (int)gx, z = (int)gz;
            float fx = gx - x, fz = gz - z;
            float a = f[z * n + x], b = f[z * n + x + 1], c = f[(z + 1) * n + x], d = f[(z + 1) * n + x + 1];
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
        }

        public static Mesh BuildFieldInk(MapLabData data, MapLabSettings settings, MapLabPaintFields fields, out Stats stats) =>
            FieldInkData(data, settings, fields, out stats)?.ToMesh();

        public static MeshData FieldInkData(MapLabData data, MapLabSettings settings, MapLabPaintFields fields, out Stats stats)
        {
            stats = default;
            LastFieldInner = 0;
            LastFieldStrips = 0;
            if (data.Fields == null || data.Fields.Count == 0 || fields == null)
                return null;
            MapLabStage05Settings s5 = settings.Stage05;
            int n = Mathf.CeilToInt(data.Size / FieldStep) + 1;
            float[] g = new float[n * n], w = new float[n * n];
            MapLabNoise pick = new MapLabNoise(MapLabRandom.StreamSeed(data.Seed, "ink05-fields", MapLabData.AlgorithmVersion));
            float share = Mathf.Clamp01(s5.FieldEdgeInkShare);
            // Полотно дорог и троп — та же функция кромки, что у заливки и пера
            // дороги: поле без полотна, межа упирается в кромку и дальше не идёт.
            MapLabInkStyle roadInk = Style(settings);
            float[] road = RoadField(data, fields, roadInk.Weights, out int rn, out _, MapLabRoadJunction.From(settings));
            float RoadAt(Vector2 q) => SampleGrid(road, rn, GridStep, q);
            // Прерывистые границы полос разного цвета внутри поля (знак — по
            // чётности полосы, ноль — граница полос).
            float[] sg = new float[n * n], sw = new float[n * n];
            MapLabNoise dashes = new MapLabNoise(MapLabRandom.StreamSeed(data.Seed, "ink05-strips", MapLabData.AlgorithmVersion));
            float stripShare = Mathf.Clamp01(s5.FieldStripInkShare);
            System.Threading.Tasks.Parallel.For(0, n, z =>
            {
                for (int x = 0; x < n; x++)
                {
                    int i = z * n + x;
                    Vector2 p = new Vector2(Mathf.Min(x * FieldStep, data.Size), Mathf.Min(z * FieldStep, data.Size));
                    fields.Get(p, out MapLabPaintFields.Sample s);
                    if (s.FieldMask < 0.12f || !data.Fields.Evaluate(p, out MapLabFields.Sample fs))
                    {
                        g[i] = 9f;
                        sg[i] = 9f;
                        continue;
                    }
                    float edge = MapLabPaintFields.FieldEdge(s, fs.BorderDistance);
                    float rd = RoadAt(p);
                    g[i] = Mathf.Max(-edge, -rd);
                    float v = pick.Fbm(p.x / 14f, p.y / 14f, 2) * 0.5f + 0.5f;
                    // Вдоль самой кромки полотна линию ведёт перо дороги — здесь веса нет.
                    w[i] = MapLabGeometry.SmoothStep(0.04f, 0.18f, rd) * MapLabGeometry.SmoothStep(1f - share - 0.05f, 1f - share + 0.05f, v);
                    int parity = ((fs.Strip % 2) + 2) % 2;
                    bool lower = fs.Across < fs.StripWidth * 0.5f;
                    sg[i] = (parity == 0 ? 1f : -1f) * Mathf.Min(fs.Across, fs.StripWidth - fs.Across);
                    float t0 = data.Fields.StripTone(fs.Block, fs.Strip), t1 = data.Fields.StripTone(fs.Block, fs.Strip + (lower ? -1 : 1));
                    float differ = ToneClass(t0) != ToneClass(t1) ? 1f : MapLabGeometry.SmoothStep(0.18f, 0.3f, Mathf.Abs(t0 - t1));
                    float dash = dashes.Fbm(p.x / 4.5f, p.y / 4.5f, 2) * 0.5f + 0.5f;
                    sw[i] = 0.8f * differ * MapLabGeometry.SmoothStep(1.2f, 1.8f, edge) * MapLabGeometry.SmoothStep(0.6f, 1f, rd)
                        * MapLabGeometry.SmoothStep(1f - stripShare - 0.04f, 1f - stripShare + 0.04f, dash);
                }
            });
            List<Contour> contours = Trace(g, w, n, FieldStep);
            contours.RemoveAll(c => c.Points.Count < 6);
            // У дороги межа упирается в кромку — это примыкание, а не свободный конец.
            foreach (Contour c in contours)
            {
                c.Hold = new List<bool>(c.Points.Count);
                foreach (Vector2 q in c.Points)
                    c.Hold.Add(RoadAt(q) < 0.3f);
            }
            // Границы полос разного цвета — прерывистые следы (свободные концы — с завершением).
            if (stripShare > 0f)
            {
                List<Contour> strips = Trace(sg, sw, n, FieldStep);
                strips.RemoveAll(c => c.Points.Count < 5 || MaxWeight(c) < 0.3f);
                foreach (Contour c in strips)
                    c.EndingChance = 0.6f;
                LastFieldStrips = strips.Count;
                contours.AddRange(strips);
            }
            // Внутренние штрихи: размер 70/25/5 по весу, группы по шуму, не у края.
            if (s5.FieldInnerInk > 0f)
            {
                MapLabRandom rng = MapLabRandom.ForStream(data.Seed, "ink05-furrows", MapLabData.AlgorithmVersion);
                MapLabNoise groups = new MapLabNoise(MapLabRandom.StreamSeed(data.Seed, "ink05-furrow-groups", MapLabData.AlgorithmVersion));
                List<Vector2> centers = new List<Vector2>();
                int attempts = Mathf.RoundToInt(data.Size * data.Size / 12f * s5.FieldInnerInk);
                float Edge(Vector2 q)
                {
                    fields.Get(q, out MapLabPaintFields.Sample s);
                    return s.FieldMask > 0.12f && data.Fields.Evaluate(q, out MapLabFields.Sample f) ? MapLabPaintFields.FieldEdge(s, f.BorderDistance) : -9f;
                }
                for (int k = 0; k < attempts; k++)
                {
                    Vector2 p = new Vector2(rng.Value(), rng.Value()) * data.Size;
                    float sizeRoll = rng.Value();
                    float lengthJ = rng.Range(0.8f, 1.2f);
                    float weight = rng.Range(0.55f, 0.85f);
                    if (groups.Fbm(p.x / 21f, p.y / 21f, 2) * 0.5f + 0.5f < 0.6f || Edge(p) < 1.4f || !data.Fields.Evaluate(p, out MapLabFields.Sample fs))
                        continue;
                    bool near = false;
                    foreach (Vector2 c in centers)
                        if ((c - p).sqrMagnitude < 1.4f * 1.4f) { near = true; break; }
                    if (near)
                        continue;
                    float angle = data.Fields.Block(fs.Block).Angle;
                    Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    float length = (sizeRoll < 0.7f ? 1.6f : sizeRoll < 0.95f ? 3f : 5.2f) * lengthJ;
                    Vector2 a = p - dir * length * 0.5f, b = p + dir * length * 0.5f;
                    if (Edge(a) < 0.5f || Edge(b) < 0.5f)
                    {
                        a = p - dir * length * 0.25f;
                        b = p + dir * length * 0.25f;
                        if (Edge(a) < 0.5f || Edge(b) < 0.5f)
                            continue;
                    }
                    Contour stroke = new Contour { FreeEnds = true, EndingChance = 0.4f };
                    for (int j = 0; j <= 8; j++)
                    {
                        stroke.Points.Add(Vector2.Lerp(a, b, j / 8f));
                        stroke.Weights.Add(weight);
                    }
                    contours.Add(stroke);
                    centers.Add(p);
                    LastFieldInner++;
                }
            }
            MapLabInkStyle style = Style(settings);
            style.Weights = Vector4.one;
            return BuildRibbonData(data, settings, contours, g, n, "MapLab Field Ink", out stats, style, FieldStep);
        }

        public static Mesh BuildRibbon(MapLabData data, MapLabSettings settings, List<Contour> contours, float[] field, int n, string name, out Stats stats, MapLabInkStyle style = null, float step = GridStep, float lift = 0.03f) =>
            BuildRibbonData(data, settings, contours, field, n, name, out stats, style, step, lift)?.ToMesh();

        public static MeshData BuildRibbonData(MapLabData data, MapLabSettings settings, List<Contour> contours, float[] field, int n, string name, out Stats stats, MapLabInkStyle style = null, float step = GridStep, float lift = 0.03f)
        {
            MapLabInkStyle ink = style ?? settings.Stage03.InkStyle;
            MapLabNoise breaks = new MapLabNoise(MapLabRandom.StreamSeed(data.Seed, "ink-breaks", MapLabData.AlgorithmVersion));
            Vector3 light3 = settings.LightDirection;
            Vector2 light = new Vector2(light3.x, light3.z);
            light = light.sqrMagnitude > 1e-6f ? light.normalized : Vector2.right;
            float threshold = 0.5f + (ink.BreakFrequency - 0.5f) * 0.55f;
            // Этап 05: свободные окончания (MAP_STYLE_GUIDE.md §2.5) — свой поток
            // случайности, география и прочие потоки не тратятся.
            MapLabStage05Settings s5 = settings.Stage05;
            bool endings = settings.IsStage05 && s5.InkEndings;
            ulong endingSeed = MapLabRandom.StreamSeed(data.Seed, "ink-endings", MapLabData.AlgorithmVersion);
            MapLabInkEndings.Options endingOptions = new MapLabInkEndings.Options
            {
                Unit = Mathf.Max(0.02f, ink.Width) * Mathf.Max(0.1f, s5.InkEndingScale),
                Variety = s5.InkEndingVariety,
                Seed = (uint)(endingSeed ^ (endingSeed >> 32)),
            };
            List<Vector3> positions = new List<Vector3>();
            List<Vector2> uv0 = new List<Vector2>(), uv1 = new List<Vector2>();
            List<Vector4> uv2 = new List<Vector4>();
            List<int> indices = new List<int>();
            List<Vector2> pts = new List<Vector2>(), sides = new List<Vector2>();
            List<float> alphas = new List<float>(), arc = new List<float>();
            List<bool> hold = new List<bool>();
            stats = default;

            float Height(Vector2 q) => Mathf.Max(data.SampleHeight(q.x, q.y), lift < 0f ? SampleLevelOrGround(data, q) : float.MinValue) + Mathf.Abs(lift);
            void Vertex(Vector2 q, Vector2 side, float alpha, float across, Vector4 extra)
            {
                positions.Add(new Vector3(q.x, Height(q), q.y));
                uv0.Add(new Vector2(across, alpha));
                uv1.Add(side);
                uv2.Add(extra);
            }
            Vector2 SideAt(float sv)
            {
                int j = MapLabInkEndings.Segment(arc, sv);
                float len = arc[j + 1] - arc[j];
                float t = len > 1e-6f ? Mathf.Clamp01((sv - arc[j]) / len) : 0f;
                Vector2 sd = Vector2.Lerp(sides[j], sides[j + 1], t);
                return sd.sqrMagnitude > 1e-8f ? sd.normalized : sides[j];
            }

            foreach (Contour c in contours)
            {
                List<Vector2> p = c.Points;
                int count = p.Count;
                if (c.Closed && count > 3)
                    count--;
                if (count < 2)
                    continue;
                pts.Clear();
                sides.Clear();
                alphas.Clear();
                hold.Clear();
                for (int i = 0; i < count; i++)
                {
                    Vector2 prev = p[c.Closed ? (i - 1 + count) % count : Mathf.Max(0, i - 1)];
                    Vector2 next = p[c.Closed ? (i + 1) % count : Mathf.Min(count - 1, i + 1)];
                    Vector2 tangent = next - prev;
                    tangent = tangent.sqrMagnitude > 1e-8f ? tangent.normalized : Vector2.right;
                    Vector2 side = new Vector2(-tangent.y, tangent.x);
                    // Наружу — по росту поля (от полотна к траве).
                    Vector2 outward = Gradient(field, n, p[i], step);
                    if (Vector2.Dot(outward, side) < 0f)
                        side = -side;
                    float alpha = c.Weights[i];
                    float b = breaks.Fbm(p[i].x / Mathf.Max(0.5f, ink.BreakLength), p[i].y / Mathf.Max(0.5f, ink.BreakLength), 2) * 0.5f + 0.5f;
                    alpha *= MapLabGeometry.SmoothStep(threshold - 0.05f, threshold + 0.05f, b);
                    alpha *= 1f - ink.LitSideFade * Mathf.Max(0f, Vector2.Dot(side, light));
                    pts.Add(p[i]);
                    sides.Add(side);
                    alphas.Add(alpha);
                    hold.Add(c.Hold != null && i < c.Hold.Count && c.Hold[i]);
                    if (i > 0)
                        stats.Length += Vector2.Distance(p[i], p[i - 1]);
                }

                // Замкнутый контур с просветом — массив с самого бледного места:
                // шов массива ложится в просвет, нулевой индекс концом не считается.
                bool closed = c.Closed;
                bool rotated = false;
                if (endings && closed)
                {
                    int least = 0;
                    for (int i = 1; i < count; i++)
                        if (alphas[i] < alphas[least])
                            least = i;
                    if (alphas[least] < MapLabInkEndings.Visible)
                    {
                        Rotate(pts, least);
                        Rotate(sides, least);
                        Rotate(alphas, least);
                        Rotate(hold, least);
                        pts.Add(pts[0]);
                        sides.Add(sides[0]);
                        alphas.Add(alphas[0]);
                        hold.Add(hold[0]);
                        closed = false;
                        rotated = true;
                    }
                }
                int m = pts.Count;
                arc.Clear();
                arc.Add(0f);
                for (int i = 1; i < m; i++)
                    arc.Add(arc[i - 1] + Vector2.Distance(pts[i - 1], pts[i]));

                MapLabInkEndings.Plan plan = null;
                if (endings && !closed)
                {
                    plan = MapLabInkEndings.Build(pts, arc, alphas, c.FreeEnds && !rotated, c.EndingChance, endingOptions, c.Hold != null ? hold : null);
                    stats.Endings += plan.Decorated;
                    stats.FreeEnds += plan.Considered;
                }

                // Основная лента; у обрыва завершения — пара вершин почти в одной
                // точке: кисть отрывается ясно, без размазанного затухания.
                int start = positions.Count;
                int cutIndex = 0;
                const float eps = 0.004f;
                for (int i = 0; i < m; i++)
                {
                    while (plan != null && cutIndex < plan.Cuts.Count && plan.Cuts[cutIndex].S <= arc[i] && i > 0)
                    {
                        MapLabInkEndings.Cut cut = plan.Cuts[cutIndex++];
                        float before = cut.Dir > 0 ? cut.Alpha : 0f, after = cut.Dir > 0 ? 0f : cut.Alpha;
                        foreach ((float sv, float a) in new[] { (cut.S - eps, before), (cut.S + eps, after) })
                        {
                            Vector2 q = MapLabInkEndings.PointAt(pts, arc, sv);
                            Vector2 sd = SideAt(sv);
                            Vector4 extra = new Vector4(0.72f, 0f, 0f, 0f);
                            Vertex(q, sd, a, -1f, extra);
                            Vertex(q, sd, a, 1f, extra);
                        }
                    }
                    float alpha = alphas[i];
                    // Концы разомкнутой линии (край карты, мост) — сходят на нет.
                    if (!closed && !rotated)
                        alpha *= Mathf.Clamp01(Mathf.Min(i, m - 1 - i) / 3f);
                    float width = 1f;
                    if (plan != null)
                    {
                        alpha *= plan.Mask(arc[i]);
                        width = plan.Taper(arc[i]);
                    }
                    Vector4 main = new Vector4(width, 0f, 0f, 0f);
                    Vertex(pts[i], sides[i], alpha, -1f, main);
                    Vertex(pts[i], sides[i], alpha, 1f, main);
                }
                int emitted = (positions.Count - start) / 2;
                int segments = closed ? emitted : emitted - 1;
                for (int i = 0; i < segments; i++)
                {
                    int a = start + i * 2, bIndex = start + ((i + 1) % emitted) * 2;
                    indices.Add(a); indices.Add(a + 1); indices.Add(bIndex + 1);
                    indices.Add(a); indices.Add(bIndex + 1); indices.Add(bIndex);
                }

                // Элементы завершения — по той же кривой, в той же сетке и материале.
                if (plan != null)
                {
                    float unit = endingOptions.Unit;
                    foreach (MapLabInkEndings.Element e in plan.Elements)
                    {
                        int first = positions.Count;
                        if (e.Kind == MapLabInkEndings.Kind.Dot)
                        {
                            float sv = (e.S0 + e.S1) * 0.5f;
                            Vector2 sd = SideAt(sv);
                            Vector2 q = MapLabInkEndings.PointAt(pts, arc, sv) + sd * (e.Offset * unit);
                            for (int k = 0; k < 4; k++)
                                Vertex(q, sd, e.Alpha, (k & 1) == 0 ? -1f : 1f, new Vector4(e.Width, k < 2 ? -1f : 1f, e.Elong, unit));
                            indices.Add(first); indices.Add(first + 1); indices.Add(first + 3);
                            indices.Add(first); indices.Add(first + 3); indices.Add(first + 2);
                        }
                        else
                        {
                            int pieces = Mathf.Max(2, Mathf.CeilToInt((e.S1 - e.S0) / 0.08f));
                            for (int k = 0; k <= pieces; k++)
                            {
                                float t = k / (float)pieces;
                                float sv = Mathf.Lerp(e.S0, e.S1, t);
                                Vector2 q = MapLabInkEndings.PointAt(pts, arc, sv);
                                Vector2 sd = SideAt(sv);
                                Vector4 extra = new Vector4(e.Width, t * 2f - 1f, 0f, unit);
                                Vertex(q, sd, e.Alpha, -1f, extra);
                                Vertex(q, sd, e.Alpha, 1f, extra);
                            }
                            for (int k = 0; k < pieces; k++)
                            {
                                int a = first + k * 2, bIndex = a + 2;
                                indices.Add(a); indices.Add(a + 1); indices.Add(bIndex + 1);
                                indices.Add(a); indices.Add(bIndex + 1); indices.Add(bIndex);
                            }
                        }
                        stats.EndingElements++;
                    }
                }
                stats.Polylines++;
            }
            stats.Vertices = positions.Count;
            if (positions.Count == 0)
                return null;
            return new MeshData { Name = name, Positions = positions, Uv0 = uv0, Uv1 = uv1, Uv2 = uv2, Indices = indices };
        }

        private static void Rotate<T>(List<T> list, int first)
        {
            if (first <= 0)
                return;
            List<T> head = list.GetRange(0, first);
            list.RemoveRange(0, first);
            list.AddRange(head);
        }

        private static float SampleLevelOrGround(MapLabData data, Vector2 p)
        {
            float level = data.SampleWaterLevel(p.x, p.y);
            return float.IsNaN(level) ? data.SampleHeight(p.x, p.y) : level;
        }

        private static Vector2 Gradient(float[] f, int n, Vector2 p, float step)
        {
            float gx = p.x / step, gz = p.y / step;
            int x = Mathf.Clamp(Mathf.RoundToInt(gx), 1, n - 2), z = Mathf.Clamp(Mathf.RoundToInt(gz), 1, n - 2);
            Vector2 g = new Vector2(f[z * n + x + 1] - f[z * n + x - 1], f[(z + 1) * n + x] - f[(z - 1) * n + x]);
            return g.sqrMagnitude > 1e-8f ? g.normalized : Vector2.zero;
        }
    }
}
