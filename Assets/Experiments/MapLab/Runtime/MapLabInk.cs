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
        }

        public sealed class Contour
        {
            public readonly List<Vector2> Points = new List<Vector2>();
            public readonly List<float> Weights = new List<float>();
            public bool Closed;
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

        public static Mesh BuildRoadInk(MapLabData data, MapLabSettings settings, MapLabPaintFields fields, out Stats stats)
        {
            MapLabInkStyle ink = Style(settings);
            float[] f = RoadField(data, fields, ink.Weights, out int n, out float[] weights, MapLabRoadJunction.From(settings));
            List<Contour> contours = Trace(f, weights, n, GridStep);
            return BuildRibbon(data, settings, contours, f, n, "MapLab Road Ink", out stats, ink, GridStep);
        }

        // Этап 05: перо берега по той же сглаженной границе, что береговая полоса
        // и край воды (MapLabShore). Выбранные участки (мировой шум), концы
        // участков сходят на нет; лента — синяя к воде, тёплая к суше (шейдер).
        public static Mesh BuildShoreInk(MapLabData data, MapLabSettings settings, MapLabShore shore, out Stats stats)
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
            return BuildRibbon(data, settings, kept, shore.Above, shore.N, "MapLab Shore Ink", out stats, style, MapLabShore.Step, -0.02f);
        }

        public static Mesh BuildRibbon(MapLabData data, MapLabSettings settings, List<Contour> contours, float[] field, int n, string name, out Stats stats, MapLabInkStyle style = null, float step = GridStep, float lift = 0.03f)
        {
            MapLabInkStyle ink = style ?? settings.Stage03.InkStyle;
            MapLabNoise breaks = new MapLabNoise(MapLabRandom.StreamSeed(data.Seed, "ink-breaks", MapLabData.AlgorithmVersion));
            Vector3 light3 = settings.LightDirection;
            Vector2 light = new Vector2(light3.x, light3.z);
            light = light.sqrMagnitude > 1e-6f ? light.normalized : Vector2.right;
            float threshold = 0.5f + (ink.BreakFrequency - 0.5f) * 0.55f;
            List<Vector3> positions = new List<Vector3>();
            List<Vector2> uv0 = new List<Vector2>(), uv1 = new List<Vector2>();
            List<int> indices = new List<int>();
            stats = default;
            foreach (Contour c in contours)
            {
                List<Vector2> p = c.Points;
                int count = p.Count;
                if (c.Closed && count > 3)
                    count--;
                int start = positions.Count;
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
                    // Концы разомкнутой линии (край карты, мост) — сходят на нет.
                    if (!c.Closed)
                        alpha *= Mathf.Clamp01(Mathf.Min(i, count - 1 - i) / 3f);
                    float h = Mathf.Max(data.SampleHeight(p[i].x, p[i].y), lift < 0f ? SampleLevelOrGround(data, p[i]) : float.MinValue) + Mathf.Abs(lift);
                    Vector3 pos = new Vector3(p[i].x, h, p[i].y);
                    positions.Add(pos);
                    positions.Add(pos);
                    uv0.Add(new Vector2(-1f, alpha));
                    uv0.Add(new Vector2(1f, alpha));
                    uv1.Add(side);
                    uv1.Add(side);
                    if (i > 0)
                        stats.Length += Vector2.Distance(p[i], p[i - 1]);
                }
                int segments = c.Closed ? count : count - 1;
                for (int i = 0; i < segments; i++)
                {
                    int a = start + i * 2, bIndex = start + ((i + 1) % count) * 2;
                    indices.Add(a); indices.Add(a + 1); indices.Add(bIndex + 1);
                    indices.Add(a); indices.Add(bIndex + 1); indices.Add(bIndex);
                }
                stats.Polylines++;
            }
            stats.Vertices = positions.Count;
            if (positions.Count == 0)
                return null;
            Mesh mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            if (positions.Count > 65000)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetTriangles(indices, 0, true);
            Bounds bounds = mesh.bounds;
            bounds.Expand(2f);
            mesh.bounds = bounds;
            return mesh;
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
